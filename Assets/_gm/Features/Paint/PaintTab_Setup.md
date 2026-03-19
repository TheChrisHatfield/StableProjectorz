# Paint tab (ribbon) setup – Krita-style

All paint-related controls live in **one tab** on the right ribbon, named **Paint**, with layout and organization **following Krita** (Toolchest, Brush Presets, Tool Options, Color/Palette).

## Code (already done)

- **CommandRibbon_UI** – Paint tab shows **\_Paint_Panel**; **Shift+5**; **clickPaint_toggle_manual()**; **Panel.Paint**.
- **PaintTab_KritaLayout_UI** – Root script for the Paint panel; defines four Krita-style sections: Toolchest row, Brush Presets, Tool Options, Color/Palette. Assign section transforms and optional headers.
- **PaintToolchest_Row_UI** – Optional script on the top row; references workflow strip, preset button, size/opacity/hardness, color swatch; can toggle Brush Presets section visibility.

## Krita-style structure (see PAINT_TAB_KRITA_STRUCTURE.md)

Build the Paint panel hierarchy as in **Assets/_gm/Features/Paint/PaintTab/PAINT_TAB_KRITA_STRUCTURE.md**:

1. **Toolchest row** (top) – WorkflowRibbon_UI + Brush preset button + Size + Opacity + Hardness + Color swatch.
2. **Brush Presets** section – BrushRibbon_UI_AlphaPicker (grid + Round + Refresh); BrushAlphas_MGR in scene.
3. **Tool Options** section – Direction, Bucket, Invert, Delete, Pressure; optional soft inpaint / blur / edge.
4. **Color / Palette** section – BrushRibbon_UI_Colors + palette load dropdown + PaletteSwatches_UI; ColorPalette_MGR in scene.

All session elements (brush alphas, ABR, ACO/ASE/GPL palettes, alpha picker, palette swatches, brush ribbon, workflow) are listed and mapped there.

## Paint tab appears automatically

**CommandRibbon_UI** creates the **Paint** tab and panel at runtime if **\_Paint_Panel** is not assigned. So the Paint tab appears **automatically alongside** Art list, Art BG list, Mesh, and ControlNet—no need to add the tab or panel in the Right Panel prefab unless you want a custom layout.

When auto-created:
- A **Panel_Paint** (with VerticalLayoutGroup + **PaintTab_KritaLayout_UI**) is added as a sibling of the other panels.
- A **Tab: Paint** button is added to the tab strip (same style as addon tabs).
- **PaintTab_KritaLayout_UI** runs with **CreateSectionsIfMissing**, so the four Krita-style sections (Toolchest, Brush Presets, Tool Options, Color/Palette) are created as placeholders; you can then drag your brush/palette UI into them or build a custom Paint panel in the prefab and assign it to **\_Paint_Panel** to override.

## Optional: custom Paint panel in prefab

If you prefer to build the Paint tab yourself:
1. **Tab "Paint"** – TabsGroupElem_UI with Title = `Paint` in the same tab strip as Art list, etc.
2. **Paint panel** – RectTransform (sibling of other content panels); assign to **CommandRibbon_UI._Paint_Panel**.
3. **Inside Paint panel** – Add **PaintTab_KritaLayout_UI** on the root; create the four section transforms and assign them; fill sections with the components listed in PAINT_TAB_KRITA_STRUCTURE.md (workflow, brush ribbon, alpha picker, tool options, palette swatches). Optionally add **PaintToolchest_Row_UI** on the toolchest row and assign refs.

## Shortcuts

- **Shift+1** – Art list  
- **Shift+2** – Art BG list  
- **Shift+3** – Mesh  
- **Shift+4** – ControlNet  
- **Shift+5** – **Paint**
