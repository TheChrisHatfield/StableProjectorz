# Paint tab – Krita-style structure and component mapping

This document maps **Krita** painting UI to the **Stable Projectorz Paint tab** and lists every element from this session that should be implemented and organized there.

---

## Krita → SPZ mapping (high level)

| Krita | Stable Projectorz Paint tab |
|-------|-----------------------------|
| **Painter's Toolchest** (toolbar) | **Toolchest row** – workflow toggles + preset + Size, Opacity, Hardness |
| **Tool Options** docker | **Tool Options** section – bucket, invert, delete, direction, pressure, soft inpaint |
| **Brush Presets** docker (PresetDocker) | **Brush Presets** section – alpha grid + Refresh + Round brushes |
| **Palette** docker (PaletteDocker) | **Color / Palette** section – current color + palette swatches (ACO/ASE/GPL) |
| **Color selector** (FG/BG) | Same section – BrushRibbon_UI_Colors (FG) + optional color picker |

---

## Recommended hierarchy (Paint panel root)

Create this under the **Paint panel** (the GameObject assigned to `CommandRibbon_UI._Paint_Panel`):

```
Paint_Panel (RectTransform)
└── PaintTab_KritaLayout_UI (script)
    │
    ├── 1_Toolchest_Row (HorizontalLayoutGroup, spacing 8)
    │   └── PaintToolchest_Row_UI (script)
    │       ├── WorkflowModeStrip     → WorkflowRibbon_UI (Proj Mask, Inpaint Color, No Color, Entire Object, Where Empty)
    │       ├── BrushPresetToggleBtn  → Button "Presets" (optional: toggles Brush Presets section)
    │       ├── SizeSlider            → BrushRibbon_UI_Size
    │       ├── OpacitySlider         → BrushRibbon_UI_Opacity
    │       ├── HardnessControl       → BrushRibbon_UI_Hardness (soft/med/hard + custom alphas)
    │       └── ColorSwatch           → BrushRibbon_UI_Colors (current brush color)
    │
    ├── 2_Layers_Section (VerticalLayoutGroup)  ← Krita-style layer stack
    │   ├── Header "Layers"           → TextMeshProUGUI (optional)
    │   └── PaintTab_LayersPanel_UI  → list of layers (visibility, opacity, add/remove, active)
    │       └── PaintLayerStack_MGR  → in scene; assign _layerStack, _listRoot, _addLayerButton; optional _layerRowTemplate
    │
    ├── 3_BrushPresets_Section (VerticalLayoutGroup)
    │   ├── Header "Brush Presets"    → TextMeshProUGUI (optional)
    │   ├── BrushRibbon_UI_AlphaPicker (grid of custom alphas from folder + ABR)
    │   │   ├── GridRoot (Horizontal/GridLayoutGroup)
    │   │   ├── ThumbnailTemplate (RawImage + Button), disabled at runtime
    │   │   ├── RoundBrushesButton    → "Round brushes" (back to soft/med/hard)
    │   │   └── RefreshButton         → "Refresh" (rescan BrushAlphas folder)
    │   └── BrushAlphas_MGR           → in scene (same object or sibling); assign in AlphaPicker
    │
    ├── 4_ToolOptions_Section (VerticalLayoutGroup)
    │   ├── Header "Tool Options"     → TextMeshProUGUI (optional)
    │   ├── Direction                 → SD_BrushRibbon_UI_Direction (add/erase)
    │   ├── BucketFill                → BrushRibbon_UI_BucketFill
    │   ├── InvertMask                → BrushRibbon_UI_InvertMask
    │   ├── DeleteButton              → BrushRibbon_UI_DeleteButton
    │   ├── PressureMode              → BrushRibbon_UI_PressureMode
    │   └── (from SD_WorkflowOptionsRibbon_UI) Soft inpaint, Tileable, Blur, Edge sliders – place here or in collapsible "Advanced"
    │
    └── 5_ColorPalette_Section (VerticalLayoutGroup)
        ├── Header "Color / Palette"  → TextMeshProUGUI (optional)
        ├── CurrentColor              → BrushRibbon_UI_Colors (big swatch + click for picker)
        ├── PaletteLoadDropdown       → Dropdown listing ColorPalette_MGR.GetPalettePathsInFolder(); on change call LoadPalette()
        └── PaletteSwatches_UI       → strip of swatches (assign ColorPalette_MGR, Swatch Root, Template, Brush Colors)
            └── ColorPalette_MGR      → in scene; assign in PaletteSwatches_UI
```

---

## Session elements to implement in the Paint tab

All of the following from this session belong in the Paint tab and should be wired as above.

### Brush alphas (Krita: Brush Presets docker)

| Element | Where | Notes |
|---------|--------|------|
| **BrushAlphas_MGR** | Scene (singleton); referenced by AlphaPicker + Hardness | Folder: `persistentDataPath/StableProjectorz/BrushAlphas`. Loads PNG, TGA, **ABR**. |
| **BrushRibbon_UI_AlphaPicker** | **Brush Presets** section | Grid of custom alphas; Round brushes button; Refresh button. Assign _brushAlphasMGR, _hardness, _gridRoot, _thumbnailTemplate. |
| **BrushRibbon_UI_Hardness** | **Toolchest row** | Soft/Medium/Hard + optional BrushAlphas_MGR for custom stamp. Assign _brushAlphasMGR so _brushHardnessTex comes from manager. |

### Color palette (Krita: Palette docker + color selector)

| Element | Where | Notes |
|---------|--------|------|
| **ColorPalette_MGR** | Scene (singleton); referenced by PaletteSwatches | Loads ACO, ASE, GPL from `persistentDataPath/StableProjectorz/Palettes`. |
| **PaletteLoader** | Static helper | LoadFromFile(path), GetPalettePathsInFolder(), EnsurePalettesFolderExists(). |
| **PaletteSwatches_UI** | **Color / Palette** section | Strip of swatches; assign _paletteMGR, _swatchRoot, _swatchTemplate, _brushColors (BrushRibbon_UI_Colors). Clicking swatch sets brush color. |
| **BrushRibbon_UI_Colors** | **Toolchest row** (color swatch) + **Color / Palette** | SetBrushColorFromPalette() used by PaletteSwatches. |

### Brush ribbon (Krita: Tool Options + Toolchest sliders)

| Element | Where | Notes |
|---------|--------|------|
| **BrushRibbon_UI** | Parent of Size, Opacity, Hardness, Bucket, Invert, Delete, Colors, Pressure, EyeDropper | Save/Load in project. |
| **BrushRibbon_UI_Size** | Toolchest row | Slider; Shift+RMB drag in viewport to resize. |
| **BrushRibbon_UI_Opacity** | Toolchest row | Opacity / strength. |
| **BrushRibbon_UI_BucketFill** | Tool Options section | Bucket fill. |
| **BrushRibbon_UI_InvertMask** | Tool Options section | Invert mask. |
| **BrushRibbon_UI_DeleteButton** | Tool Options section | Clear/delete. |
| **BrushRibbon_UI_PressureMode** | Tool Options section | Tablet pressure (size/opacity). |
| **SD_BrushRibbon_UI_Direction** | Tool Options section | Add vs erase. |
| **WorkflowRibbon_UI** | Toolchest row | Mode toggles (Proj Mask, Inpaint Color, No Color, Entire Object, Where Empty). |
| **SD_WorkflowOptionsRibbon_UI** | Tool Options (or split) | Soft inpaint, tileable, blur, edge sliders; also holds refs to brush color, opacity, hardness, etc. Can stay as one block or split into Toolchest + Tool Options. |

### Layers (Krita: Layer stack docker)

| Element | Where | Notes |
|---------|--------|------|
| **PaintLayerStack_MGR** | Scene (singleton) | Holds layer list, active layer, composite. Assign _compositeBlendShader (PaintLayer_CompositeBlend). When present, inpaint paints to active layer and composite is used for display/SD. |
| **PaintTab_LayersPanel_UI** | **Layers** section | List of layers: name, visibility (eye), opacity slider, delete; Add layer button; click row to set active. Assign _layerStack, _listRoot, _addLayerButton; optional _layerRowTemplate. |

### Paint tab shell (Krita: dock areas)

| Element | Where | Notes |
|---------|--------|------|
| **CommandRibbon_UI._Paint_Panel** | Right Panel prefab | Assign the root Paint panel RectTransform. |
| **TabsGroupElem_UI** title "Paint" | Same tab strip as Art list, etc. | Shift+5 to switch. |
| **PaintTab_KritaLayout_UI** | On Paint panel root | Assign _toolchestRow, _layersSection, _brushPresetsSection, _toolOptionsSection, _colorPaletteSection. |
| **PaintToolchest_Row_UI** | On 1_Toolchest_Row | Assign workflow strip, preset button, size, opacity, hardness, color swatch; optional toggle for Brush Presets. |

---

## Layout tips (Krita-like)

- **Toolchest row:** Horizontal, single row; compact so it doesn’t wrap on narrow panels.
- **Brush Presets:** Grid or horizontal wrap; thumbnail size ~48px; "Round brushes" and "Refresh" always visible.
- **Tool Options:** Vertical list of buttons/sliders; optional collapsible "Advanced" for blur/edge/soft inpaint.
- **Color / Palette:** Current color on top (large swatch); below it palette load dropdown (optional) and scrollable swatch strip (PaletteSwatches_UI).

---

## Functionality checklist (all from this session)

- [ ] **Brush alphas:** Folder `BrushAlphas`; PNG, TGA, **ABR** load; grid in Brush Presets section; Refresh; Round brushes button.
- [ ] **Brush stamp:** Hardness (soft/med/hard) + custom alpha from BrushAlphas_MGR; all painters use CurrentBrushStampTex.
- [ ] **Color palette:** Folder `Palettes`; ACO, ASE, GPL load; ColorPalette_MGR; PaletteSwatches_UI in Color/Palette section; swatch click sets brush color (SetBrushColorFromPalette).
- [ ] **Paint tab:** One tab "Paint" on right ribbon; _Paint_Panel shows this hierarchy; Shift+5.
- [ ] **Krita structure:** Toolchest row → Brush Presets → Tool Options → Color/Palette; PaintTab_KritaLayout_UI + PaintToolchest_Row_UI for structure.
- [ ] **Save/Load:** BrushRibbon_UI_SL (hardness, customAlphaIx, color, opacity, size, etc.); project save/load restores brush and palette selection.

Once the prefab is built with the hierarchy above and refs assigned, all session features are organized in the Paint tab with Krita-like layout and full functionality.
