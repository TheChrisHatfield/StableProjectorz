# Layer System & Stroke Storage: Deep Analysis

This document explains how Stable Projectorz stores brush stroke (pixel) information, how the layer system plugs into that pipeline, and what is required so that **each layer can store and display stroke information** when the brush is used on the active layer.

---

## 1. How Stable Projectorz Represents Stroke Information

### 1.1 Two-stage pipeline (no layers)

Stable Projectorz does not store “strokes” as vector data. It stores **rasterized pixel data in UV space**:

1. **Stroke capture (temporary)**  
   While the user drags, the brush is rendered into **R8 texture arrays** (`_prevBrushPath_R8`, `_currBrushPath_R8`): one slice per UDIM, screen-space stroke mask (single channel). These are **scratch buffers**; they are cleared each new stroke and are not the persistent store.

2. **Commit (persistent)**  
   On **mouse release**, `Apply_into_ColorBrushTex` runs. It:
   - Takes the current brush stroke (R8) and the **destination** `RenderUdims`.
   - Uses a **compute shader** to sample the stroke, apply brush color/opacity/sign, and **write RGBA into the destination texture array**.
   - The destination is a **RenderUdims**: a `RenderTexture` (Texture2DArray) with one slice per UDIM, same resolution as the brush (e.g. `COLOR_BRUSH_RESOLUTION`), format RGBA. This is the **persistent pixel store**.

So “stroke information” in code = **RGBA pixel data in a RenderUdims** (UV-space texture array). One buffer = one “layer” of paint in the classic sense.

### 1.2 Data structure: RenderUdims

- **RenderUdims** = wrapper around a single `RenderTexture` (Texture2DArray).
- **Layout:** one slice per UDIM; each slice is a 2D texture (e.g. 1024×1024) in **UV space** (wrapped around the mesh).
- **Content:** RGBA per texel (brush color, opacity, etc.). This is the only persistent store for brush strokes in the current design.
- **Creation:** `new RenderUdims(udims, widthHeight, format, filter, clearColor, depthBits)` allocates the GPU texture array and clears it.

So: **one RenderUdims = one full “layer” of pixel data** (all UDIMs, same resolution). The engine is inherently capable of multiple such buffers; the question is how they are assigned to “layers” and how read/write are routed.

---

## 2. No-layers path (single buffer)

When there is **no** layer stack:

- **Paint target:** Always `_ObjectUV_brushedColorRGBA` (one RenderUdims owned by `Inpaint_MaskPainter`).
- **Commit:** `OnFinal_ApplyIncomingVals_intoMask` → `Apply_into_ColorBrushTex(..., _ObjectUV_brushedColorRGBA)`. The compute shader writes into `_ObjectUV_brushedColorRGBA.texArray`.
- **Display:** `ApplyColorLayer_To_UV_Textures(ontoHere)` uses `source = _ObjectUV_brushedColorRGBA` and blits it onto the accumulation texture that is shown on the mesh.

So with no layers, **one buffer** holds all stroke pixel data and is both the write target and the display source. This is what works today.

---

## 3. Layer system: per-layer storage

### 3.1 Model: one layer = one RenderUdims

- **PaintLayer** = name, visibility, opacity, blend mode, and **Content**.
- **Content** = `RenderUdims` (same layout as the brush system: same UDIMs, same resolution, same format). So **each layer has its own RGBA texture array** for pixel data.
- **PaintLayerStack_MGR** holds a list of `PaintLayer`s and an **active layer index**.  
  - **ActiveLayerRenderUdims** = `ActiveLayer?.Content` = the RenderUdims of the currently selected layer (or null if that layer has no content).

So by design, **each layer can store stroke information**: that information is the RGBA content of `layer.Content` (a RenderUdims). No extra “stroke” abstraction is needed; it’s the same pixel buffer the rest of the app uses.

### 3.2 When does a layer get its Content (RenderUdims)?

- **PaintLayer.EnsureContent(udims, resolution, format, filter)**  
  Allocates or resizes `Content` so it matches the given UDIM list and resolution. Same format/filter as the brush (e.g. `GenData_Masks.colorBrushFormat`, `GenData_Masks.masksFilter`).

- **PaintLayerStack_MGR.EnsureResolution(Vector3Int resolution)**  
  - Called from **Inpaint_MaskPainter.InitTextures** when the layer stack exists (first time we have valid width, height, numSlices from the current model).
  - Sets `_resolution` and `_udimsCount`, then calls **EnsureContent on every layer** in the stack. So after this, **every existing layer has a valid Content** (same resolution/UDIMs as the brush).

- **AddLayer**  
  When adding a new layer, if the stack already has resolution (`_resolution.x > 0 && _udimsCount > 0`), it calls `layer.EnsureContent(...)` so the new layer gets Content immediately. If not (e.g. no paint yet), the new layer gets Content the next time **EnsureResolution** runs (on first paint or on model import).

- **On_3dModel_Imported** (MaskPainter)  
  Calls `initTextures_Maybe(maskResolution())`, which calls **InitTextures** and thus **EnsureResolution** when the layer stack exists. So when a new model is loaded, all current layers are given Content matching the new model’s UDIMs.

So: **per-layer storage is created and kept in sync with brush resolution/UDIMs** via EnsureResolution / EnsureContent. The only time a layer might temporarily have no Content is right after adding a layer before any paint or model load has run; as soon as the user paints (or a model is imported), InitTextures → EnsureResolution runs and all layers get Content.

---

## 4. The glue: how strokes are written per layer

### 4.1 Where the write goes (commit path)

- **GetPaintTarget()** (Inpaint_MaskPainter):
  - If the layer stack exists and the **active layer has Content**: returns `ActiveLayerRenderUdims` (= that layer’s `Content`).
  - Otherwise: returns `_ObjectUV_brushedColorRGBA` (fallback single buffer).

- **OnRenderIntoCurrTex_please**  
  Uses `GetPaintTarget()` to choose the **render target** for the brush (which slice layout to use). The actual stroke is still drawn into the R8 scratch buffers; the “target” here is the logical target for the pipeline.

- **OnFinal_ApplyIncomingVals_intoMask** (on mouse release):
  - `target = GetPaintTarget()` (with fallback to `_ObjectUV_brushedColorRGBA` if null).
  - Calls **Apply_into_ColorBrushTex(prevBrushStroke_R8, currBrushStroke_R8, sign, maxStrength, target)**.
  - The compute shader binds **target.texArray** as `_PaintedMask` and writes the blended stroke into it.

So **the only place stroke pixel data is written** is inside `Apply_into_ColorBrushTex`, and it writes into **whatever RenderUdims is passed as `destin`**. That is exactly **GetPaintTarget()**: either the active layer’s Content or the fallback buffer. So:

- **With layers:** strokes on the active layer are written into **that layer’s Content** (its RenderUdims). Each layer’s stroke information is stored in its own buffer.
- **Without layers (or when active has no Content):** strokes are written into **\_ObjectUV_brushedColorRGBA**.

So the engine **is** capable of handling stroke information per layer: the “per layer” store is **layer.Content**, and the glue is **GetPaintTarget()** routing the commit to that buffer.

---

## 5. The glue: how layers are displayed

### 5.1 Reading per-layer pixel data

- **CompositeTo(dest)** (PaintLayerStack_MGR)  
  Composites **visible** layers (bottom to top) into `dest`:
  - Iterates layers in order; for each layer with `Visible && Content != null`, blends its **Content** (RenderUdims) into the destination using the composite blend shader (opacity, blend mode).
  - So **display is read from each layer’s Content** (the same buffers that were written in the commit step).

- **CompositeToWithActiveOnTop(dest)**  
  Same as above, then **blends the active layer’s Content on top** again so the user always sees the active layer’s strokes (even if that layer is hidden). So again, display reads from **layer.Content**.

- **ApplyColorLayer_To_UV_Textures(ontoHere)** (Inpaint_MaskPainter):
  - When the **paint target is a layer** (ActiveLayerRenderUdims != null): builds the image with **CompositeToWithActiveOnTop(_layerStackCompositeTemp)** and uses that as the source for the final blit to the mesh accumulation buffer.
  - When the **paint target is the fallback** (ActiveLayerRenderUdims == null): uses **\_ObjectUV_brushedColorRGBA** as source so what was painted is what is shown.

So **display is driven by the same buffers that store the strokes**: either the composited layer Contents or the single fallback buffer. There is no second “stroke store”; the RenderUdims **are** the store.

---

## 6. End-to-end: stroke on active layer

1. User selects **layer A** (active).
2. **GetPaintTarget()** = `PaintLayerStack_MGR.instance.ActiveLayerRenderUdims` = **layer A’s Content** (assuming it has Content).
3. User draws; on release, **Apply_into_ColorBrushTex(..., target)** runs with **target = layer A’s Content**. The compute shader writes stroke pixels into **layer A’s RenderUdims**.
4. **ApplyColorLayer_To_UV_Textures** uses **CompositeToWithActiveOnTop**, which:
   - Composites all visible layers (each from its own Content) into a temp buffer.
   - Draws the active layer (layer A) on top.
   So layer A’s Content is read and shown.
5. That result is blitted onto the mesh accumulation texture.

So **stroke information is stored per layer** in `layer.Content` (RenderUdims), and **display reads from those same buffers**. The glue is:

- **Write:** GetPaintTarget() → Apply_into_ColorBrushTex(destin = that RenderUdims).
- **Read:** CompositeTo / CompositeToWithActiveOnTop reading each layer’s Content.

---

## 7. What can go wrong (and what to check)

| Issue | Cause | Check / fix |
|-------|--------|-------------|
| Active layer has no Content | EnsureResolution not run yet (e.g. no paint since layers were added, or resolution not set). | Ensure **InitTextures** (and thus **EnsureResolution**) runs when the stack exists and resolution is known. It currently runs on first paint and on model import. Optionally call EnsureResolution when entering paint mode or when the layer panel is first shown (if model and UDIMs are already known). |
| Strokes go to fallback instead of layer | GetPaintTarget() returns _ObjectUV_brushedColorRGBA because ActiveLayerRenderUdims is null. | Same as above: ensure the active layer has Content (EnsureResolution for all layers). |
| Strokes not visible on mesh | Display source not aligned with paint target (e.g. compositing only visible layers and active was hidden, or using composite when paint target was fallback). | Already addressed by using **CompositeToWithActiveOnTop** when painting to a layer and using **\_ObjectUV_brushedColorRGBA** as source when painting to the fallback. |
| New layer has no Content until first paint | AddLayer only calls EnsureContent when _resolution > 0. Before first paint, _resolution can be 0. | By design, EnsureResolution runs on first InitTextures (first paint or model import), and then all layers get Content. If you need new layers to have Content before first stroke, call EnsureResolution when resolution becomes available (e.g. when a model is loaded and the paint tab is active). |

---

## 8. Required modifications summary

- **Already in place:**
  - Each layer has a **Content** (RenderUdims) and can store stroke pixel data.
  - **GetPaintTarget()** routes commits to the active layer’s Content or the fallback buffer.
  - **Apply_into_ColorBrushTex** writes to whatever RenderUdims it is given (no layer-specific logic inside it).
  - **CompositeTo** / **CompositeToWithActiveOnTop** read from each layer’s Content for display.
  - **ApplyColorLayer_To_UV_Textures** chooses source from composite vs fallback based on whether the paint target is a layer.

- **To make “brush strokes on active layer stored and displayed per layer” robust:**
  1. **Ensure every layer has Content before the user can paint on it.**  
     Current: EnsureResolution runs in InitTextures (first paint or model import). If you add layers before any paint, call **EnsureResolution** when the paint context is valid (e.g. when the user has a model and switches to the Paint tab or selects a layer), using resolution from **maskResolution()**, so that all layers (including newly added) get Content.
  2. **Keep a single fallback buffer** when the active layer has no Content so strokes still have a target and display uses that buffer (already done).
  3. **Keep display logic aligned with paint target** (composite + active on top when painting to a layer; fallback as source when painting to fallback) (already done).

---

## 9. Summary

- **Stable Projectorz** stores stroke information as **RGBA pixel data in UV-space texture arrays** (RenderUdims). It does not store strokes as vectors; the only persistent store is these buffers.
- The **layer system** gives each layer its own **RenderUdims** (`Content`). So the app **is** capable of storing and displaying stroke information **per layer**.
- The **glue** is: **GetPaintTarget()** (write destination) and **CompositeTo / CompositeToWithActiveOnTop** (read for display). When the user brushes on the active layer, strokes are written into that layer’s Content and read back from it during composite. The main thing to guarantee is that **every layer that can be painted on has Content** (via EnsureResolution at the right time).
