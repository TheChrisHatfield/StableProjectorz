# How to Use the Paint Tab

The **Paint tab** lets you paint masks and colors on your 3D model’s UV layout. Use it for inpainting (guiding where the AI paints), adding details, or painting vertex colors. The UI is organized in a **Krita-style** layout: **Layers**, **Brush** (size, hardness, shape), **Color**, and **Brush Presets**.

---

## Opening the Paint Tab

1. In the main UI, click the **Paint** tab (in the tab strip with other tools).
2. The Paint panel opens with the **Layers** list, **Brush** controls, **Color** / palette, and **Brush Presets** (custom brush shapes).

---

## Layers

- **Bottom layer**  
  The first layer holds the **scene** (your current view / mesh). It is filled automatically when you start painting. All other layers stack on top.

- **Active layer**  
  The layer you paint on is the **active** one. It is shown with a **blue highlight** in the list.
  - **Click a row** in the layers list to make that layer active.
  - **+ Layer** adds a new layer and makes it active.

- **Per-layer controls**  
  - **Eye icon** – Toggle visibility (dark blue = visible, light = hidden).
  - **Red Delete** – Remove that layer.

- **Order**  
  Layers are drawn from bottom to top. The bottom layer is the base; upper layers are composited over it.

---

## Brush

- **Size**  
  Set brush size with the size slider or control in the Brush section.

- **Hardness (round brushes)**  
  Choose **Soft**, **Medium**, or **Hard** round brush (e.g. **H** or **Ctrl+1 / 2 / 3** if bound). These are the default round brushes.

- **Brush shape (alpha)**  
  Use the **brush shape / alpha picker** (grid of thumbnails) to choose a **custom brush** (e.g. text, patterns).  
  See **[Custom Brush Alphas](BrushAlphas_README.md)** for:
  - Where to put PNG, TGA, or ABR (Photoshop) files
  - How to use **Load brush file…** to add ABR/PNG/TGA
  - How to **Refresh** after adding files to the BrushAlphas folder

---

## Color

- **Current color**  
  The brush uses the selected color from the **Color** / palette area.

- **Palettes**  
  You can load color palettes from files:
  - **Formats:** ACO (Adobe Color), ASE (Adobe Swatch Exchange), GPL (GIMP Palette).
  - **Folder:**  
    - **Windows:** `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\StableProjectorz\Palettes`  
    - **macOS:** `~/Library/Application Support/<CompanyName>/<ProductName>/StableProjectorz/Palettes`  
    - (In code: `PaletteLoader.PalettesFolderPath` = `Application.persistentDataPath/StableProjectorz/Palettes`.)
  - **In the Paint tab:** Use **Load palette…** (or **Load ASE/ACO/GPL…**) to pick a file; it is loaded for the brush. Use **Refresh** to reload the current palette from disk.

- **Swatches**  
  Click a swatch in the palette row to set the current brush color.

---

## Painting

1. Open the **Paint** tab and ensure the **viewport** shows your model (or the UV view you use for painting).
2. Select the **layer** you want to paint on by **clicking its row** (active = blue).
3. Set **brush size**, **hardness** (or a custom alpha), and **color**.
4. Paint on the viewport; strokes go to the **active layer** and are composited with the layers below.

---

## Tips

- **New layer** – Use **+ Layer** when you want a separate layer for details or masks; the new layer is automatically active.
- **Scene on bottom** – The bottom layer is the only one that gets the “static scene” base; upper layers are drawn on top.
- **Custom brushes** – For text or pattern brushes, see [BrushAlphas_README.md](BrushAlphas_README.md).
- **Palettes** – Drop `.aco`, `.ase`, or `.gpl` files into the Palettes folder, then load or refresh in the Paint tab.

---

## Related Docs

- **[BrushAlphas_README.md](BrushAlphas_README.md)** – Custom brush shapes (PNG, TGA, ABR), folder location, Load brush file, Refresh.
