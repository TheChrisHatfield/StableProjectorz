# Dedicated Painting Ribbon, Layers & Brushes — Krita → Stable Projectorz Mapping

This document maps **Krita’s** painting systems to **Stable Projectorz** and outlines a **dedicated painting ribbon**, a **layers system**, and **brush presets** that can be implemented on top of the existing Paint/Mask stack.

---

## 1. Krita → Stable Projectorz system mapping

| Krita (C++/Qt) | Stable Projectorz (Unity/C#) | Notes |
|-----------------|------------------------------|--------|
| **KisPart** (doc/window lifecycle) | `Start_Scene_Global_MGR` + optional **Painting_MGR** | One “painting context” owner (active canvas, layer stack, brush). |
| **KisDocument / KisImage** (layer tree) | **PaintingLayerStack** (new) | Ordered list of paint layers; one “active” layer; each layer = texture(s) + blend + opacity. |
| **KisLayer** (paint layer, group, etc.) | **PaintLayer** (new) | Single layer: name, visibility, opacity, blend mode, RenderTexture (or RenderUdims for UV). |
| **Brush preset / KisBrush** | **BrushPreset** (ScriptableObject or serializable) | Preset = size curve, hardness tex, opacity, color (optional), pressure curves; stored in project or user folder. |
| **KisResourceLocator** (resource root) | **PaintingResourceLocator** (new) or `Application.persistentDataPath` + `/Brushes` | Single folder for brush presets; optional index for quick load. |
| **Toolbox + options docker** | **Dedicated Painting Ribbon** (new) | One ribbon that shows only when “painting” is active: brush presets, size, opacity, hardness, color, layer list, blend mode. |
| **Brush engine / paintop** | Existing **MaskPainter** pipeline | Reuse `MaskPainter` / brush shaders; feed parameters from **BrushPreset** + current layer. |
| **Layer stack docker** | **Layers panel** (section of Painting Ribbon or separate panel) | List of layers; reorder, visibility, lock, opacity, blend mode; active layer selection. |
| **Canvas (KisView)** | **MainViewport** + current “paint target” | Paint target = active layer’s texture (2D) or UV mask (3D projection); already partially there via Inpaint_MaskPainter / Background_Painter. |

---

## 2. Dedicated Painting Ribbon (new)

**Goal:** A ribbon used only for **creative painting** (layers + brushes), separate from the current **Brush Ribbon** that drives **masking** (inpaint, projection mask, background).

- **Current “Brush” ribbon** → Stays for: workflow mode (Inpaint Color/NoColor, Projections, etc.) + mask brush (size, opacity, hardness, bucket, invert, delete). Used by `MaskPainter`, `SD_WorkflowOptionsRibbon_UI`, `WorkflowRibbon_UI`.
- **New “Painting” ribbon** → For: brush **presets**, **layer stack**, **blend modes**, and painting-specific options (smudge, stabilizer, etc. later).

### 2.1 When to show the Painting Ribbon

- Option A: New workflow mode in `WorkflowRibbon_UI`, e.g. `Painting_Layers`, that switches to “layer painting” and shows the Painting Ribbon instead of (or in addition to) the mask brush options.
- Option B: Separate “Paint” tab or tool in the left ribbon; when selected, show the Painting Ribbon and layer stack.
- Option C: Always visible in a collapsible section when the user has at least one “paint layer” in the current context (e.g. current GenData2D or current background).

Recommendation: **Option A or B** so the user explicitly enters “painting mode” and the UI clearly separates “masking” from “painting.”

### 2.2 Painting Ribbon contents (Krita-inspired)

| Section | Krita analogue | SP implementation |
|--------|----------------|-------------------|
| **Brush presets** | Brush preset strip / dropdown | List or grid of **BrushPreset** assets; click to set active brush; optional “current preset” name label. |
| **Size** | Size slider | Reuse `BrushRibbon_UI_Size` logic or new slider bound to **active BrushPreset** + override. |
| **Opacity** | Opacity slider | Same idea; drive from preset or override. |
| **Hardness** | Hardness / brush tip | Reuse `BrushRibbon_UI_Hardness` / `_brushHardnessTex`; can be per-preset. |
| **Color** | Foreground color | Reuse `BrushRibbon_UI_Colors` or shared `SD_WorkflowOptionsRibbon_UI.brushColor` when in painting mode. |
| **Layers** | Layer stack docker | **Layers panel**: add/remove layer, reorder, visibility eye, lock, opacity slider, blend mode dropdown; highlight active layer. |
| **Blend mode** | Layer blend mode | Dropdown or strip: Normal, Multiply, Screen, Overlay, etc.; applies to **active layer** when painting. |
| **Eyedropper** | Color picker tool | Enable existing `BrushRibbon_UI_EyeDropperTool` when in Painting Ribbon context. |

### 2.3 Code / scene layout suggestion

- New scripts (e.g. under `Assets/_gm/Features/Paint/`):
  - `PaintingRibbon_UI.cs` – root for the dedicated ribbon; references brush preset strip, size, opacity, hardness, color, **LayersPanel_UI**, blend mode.
  - `PaintingRibbon_UI_BrushPresets.cs` – list of `BrushPreset`; selection sets “active brush” used by the painter.
- New prefab: e.g. `Painting Ribbon (Layers + Brushes).prefab` that can be placed in the same area as the current “Painting - Workflow_Ribbon container” but shown only when in painting mode.
- **Active brush** and **active layer** can live on a small manager (e.g. `Painting_MGR` singleton) so both the ribbon and the painter read from one place.

---

## 3. Layers system

**Goal:** Multiple “paint layers” per paint context (e.g. per GenData2D or per background), with order, visibility, opacity, and blend mode.

### 3.1 Data model (Krita-style layer stack)

- **PaintLayerStack** (per “paint context”):
  - `List<PaintLayer> layers` (order = bottom to top, index 0 = bottom).
  - `int activeLayerIndex` (which layer receives brush strokes).
- **PaintLayer**:
  - `string name`
  - `bool visible`
  - `bool locked` (optional; if locked, no painting).
  - `float opacity` (0–1).
  - `BlendMode blendMode` (Normal, Multiply, Screen, Overlay, etc.).
  - **Content:** Either a single `RenderTexture` (2D screen-space, e.g. background) or `RenderUdims` (UV-space for 3D/projection). Reuse existing `RenderUdims` / mask texture format where possible.

### 3.2 Where the stack lives

- **Option A – Per GenData2D:** Each `GenData2D` (or “icon”/result) has an optional `PaintLayerStack`; when user paints “on this result,” they paint on the active layer of that stack.
- **Option B – Global painting canvas:** One global stack for “current view” (e.g. main viewport composite); layers are composited on top of the current view.
- **Option C – Per “paint target” enum:** Same as today (inpaint mask, background, projection mask) but each target has its own layer stack instead of a single texture.

Recommendation: **Option A** or **C** so layers are clearly tied to an existing concept (one GenData2D or one mask target). Start with one layer per target, then extend to multiple layers (add/remove from stack).

### 3.3 Compositing

- When rendering the “paint result” (e.g. for SD or for display), composite layers from bottom to top:
  - `result = layers[0]` (or clear);
  - for i = 1 .. count: `result = Blend( result, layers[i], layers[i].opacity, layers[i].blendMode )`.
- Use a small shader or compute that implements blend modes (Normal, Multiply, Screen, Overlay, etc.).

### 3.4 Save / load

- Add **PaintLayerStack_SL** and **PaintLayer_SL** to `SerializationObjects.cs` (or a separate painting serialization file).
- Each layer: name, visible, opacity, blendMode, and reference to stored texture (e.g. in project `_Data` folder, same pattern as `GenData_Masks` / `RenderUdims_SL`).
- **BrushRibbon_UI_SL** already exists; add a **PaintingRibbon_SL** (or extend) for: active preset ID, layer order, and per-layer serialization when you add the stack.

---

## 4. Brush presets (Krita-style brushes)

**Goal:** Multiple named brushes (presets) the user can switch between; each preset stores size, hardness, opacity, color (optional), pressure curves.

### 4.1 BrushPreset asset (ScriptableObject)

- **BrushPreset** (ScriptableObject, e.g. `Assets/_gm/Features/Paint/BrushPresets/`):
  - `string presetName`
  - `Texture2D stampTexture` (brush tip; can reuse `_brushHardnessTex` style).
  - `AnimationCurve sizeCurve` (0–1 → world size) or reuse global curve + multiplier.
  - `float opacityDefault` (0–1).
  - `float hardnessDefault` (0–1) or index into hardness textures.
  - `Color colorDefault` (optional; if not set, use current ribbon color).
  - `AnimationCurve pressureSize` (optional; tablet).
  - `AnimationCurve pressureOpacity` (optional; tablet).
  - Optional: `bool useColorFromPreset` vs “use current color.”

### 4.2 Resource location (Krita KisResourceLocator style)

- **User presets:** `Application.persistentDataPath + "/StableProjectorz/Brushes/"` (or under project `_Data` if you want per-project brushes).
- **Default presets:** In `Assets/_gm/Features/Paint/BrushPresets/` as ScriptableObjects; at runtime copy or reference into a list.
- **PaintingBrushRegistry** (optional): Singleton that loads all presets from the resource folder + defaults; provides `List<BrushPreset> GetAll()`, `BrushPreset GetById(string id)`, `void SavePreset(BrushPreset p)`. Same idea as Krita’s resource storage + cache.

### 4.3 Painting ribbon ↔ preset

- **PaintingRibbon_UI** (or **Painting_MGR**) holds `BrushPreset activePreset`.
- When user selects a preset from the strip, set `activePreset = selected`.
- When painting, **MaskPainter** (or a new **LayerPainter** that uses the same brush pipeline) reads:
  - size from `activePreset.sizeCurve` (and optional override from ribbon slider),
  - hardness from `activePreset.stampTexture`,
  - opacity from `activePreset.opacityDefault` (and optional override),
  - color from `activePreset.colorDefault` or ribbon color.
- Existing **BrushRibbon_UI_SL** can stay for “mask” brush; add **BrushPreset_SL** (preset id + overrides) or **PaintingRibbon_SL** for the dedicated painting ribbon state.

---

## 5. Implementation order (suggested)

1. **Brush presets (data + UI)**  
   - Add `BrushPreset` ScriptableObject and a default set (e.g. Soft Round, Hard Round, same as current hardness options).  
   - Add **PaintingBrushRegistry** (or simple list) loading from Resources or `persistentDataPath`.  
   - Add **PaintingRibbon_UI** with a small strip of preset buttons; on click set “active preset” in a **Painting_MGR** (or SD_WorkflowOptionsRibbon_UI extension).  
   - No new painter yet: keep using current brush pipeline but **drive** size/opacity/hardness from active preset when “painting mode” is on.

2. **Dedicated Painting Ribbon (full)**  
   - Add **PaintingRibbon_UI** with: preset strip, size, opacity, hardness, color, blend mode dropdown (for now only “Normal” and store it for the next step).  
   - Hook “painting mode” to a new workflow mode or a “Paint” tool in the left ribbon so the ribbon is visible only when needed.

3. **Layers (single context)**  
   - Define **PaintLayer** and **PaintLayerStack**; tie stack to one context (e.g. current GenData2D or current background).  
   - Start with **two layers**: “Background” (read-only from existing texture) + “Paint Layer 1” (writable).  
   - **Layers panel** in the Painting Ribbon: list layers, set active layer, visibility, opacity, blend mode (Normal only first).  
   - Composite: one shader that blends two textures with opacity; call it when building the result for SD or display.

4. **Layers (full stack)**  
   - Add/remove layers, reorder (drag in UI), save/load **PaintLayerStack_SL** in project.  
   - Implement more blend modes in the composite shader.

5. **Layer painter**  
   - Either extend **MaskPainter** to “paint onto a layer’s texture” from the active preset and active layer, or add **LayerPainter** that reuses the same brush shaders and stroke logic but writes to the active layer’s RT instead of the mask.  
   - Ensure **Painting_MGR** (or equivalent) provides: active preset, active layer, current blend mode.

---

## 6. File / folder layout (suggested)

```
Assets/_gm/Features/Paint/
├── BrushRibbon_UI/              # existing: mask brush
├── PaintingRibbon_UI/            # NEW: dedicated painting ribbon
│   ├── PaintingRibbon_UI.cs
│   ├── PaintingRibbon_UI_BrushPresets.cs
│   ├── PaintingRibbon_UI_LayersPanel.cs
│   └── PaintingRibbon_UI_BlendMode.cs
├── BrushPresets/                 # NEW: ScriptableObject presets
│   ├── BrushPreset.cs            # ScriptableObject
│   ├── SoftRound.asset
│   └── HardRound.asset
├── Layers/                       # NEW: layer stack
│   ├── PaintLayer.cs
│   ├── PaintLayerStack.cs
│   ├── PaintLayerStack_Composite.cs  # blend shader runner
│   └── PaintLayerStack_SL.cs      # serialization (or in SerializationObjects.cs)
├── Painting_MGR.cs              # NEW: singleton – active preset, active layer, paint context
├── MaskPainter.cs                # existing
├── Projections_MaskPainter.cs     # existing
├── Inpaint/                      # existing
├── BG painter/                   # existing
└── DESIGN_PaintingRibbon_Layers_Brushes.md  # this file
```

---

## 7. Summary

- **Krita → SP:** Map document/layer model to **PaintLayerStack** + **PaintLayer**; map brush presets to **BrushPreset** + **PaintingBrushRegistry**; map options docker to **Painting Ribbon** (dedicated UI).
- **Dedicated ribbon:** New **Painting Ribbon** for layers + brush presets + blend mode, shown when in “painting mode”; keep current Brush Ribbon for masking.
- **Layers:** One stack per paint context (e.g. per GenData2D); each layer has name, visibility, opacity, blend mode, and one texture; composite bottom-to-top for display/SD.
- **Brushes:** **BrushPreset** ScriptableObjects + optional user folder (Krita-style resource location); Painting Ribbon shows preset strip and sets active preset for the shared brush pipeline.

This gives you a Krita-inspired workflow (layers + presets + dedicated ribbon) while reusing your existing **MaskPainter** and brush shaders and fitting into **StableProjectorz_SL** save/load.
