# Color palettes (ACO, ASE, GPL)

You can load **color palettes** from files and use them as brush colors. Supported formats:

- **ACO** – Adobe Photoshop Color (swatches)
- **ASE** – Adobe Swatch Exchange (used in Photoshop, Illustrator, etc.)
- **GPL** – GIMP Palette (plain text)

## Folder location

Palette files are loaded from:

- **Path:** `Application.persistentDataPath/StableProjectorz/Palettes`
- Same idea as the BrushAlphas folder (e.g. `%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\StableProjectorz\Palettes` on Windows).

Create the `Palettes` folder and drop `.aco`, `.ase`, or `.gpl` files there.

## How to use

1. **Load a palette** – Use **ColorPalette_MGR.LoadPalette(filePathOrFileName)** (e.g. from a dropdown or file picker). You can pass the full path or just the filename if the file is in the Palettes folder.
2. **Swatch strip** – Add **PaletteSwatches_UI** to your UI: assign **ColorPalette_MGR**, a **swatch root** (layout group), and a **swatch template** (prefab with Image + Button). Assign **Brush Colors** (BrushRibbon_UI_Colors) so clicking a swatch sets the brush color.
3. **Brush color** – When the user clicks a swatch, the brush color updates and the app switches to Inpaint Color mode.

## Setup in Unity

- Add **ColorPalette_MGR** to the scene (e.g. near the paint/color UI).
- Add **PaletteSwatches_UI**: set **Palette MGR**, **Swatch Root**, **Swatch Template**, and **Brush Colors** (optional but recommended so swatches set the brush).
- Optionally add a dropdown or list that lists **ColorPalette_MGR.GetPalettePathsInFolder()** and calls **LoadPalette(path)** when the user selects a file.

## Note on ABR

**ABR** (Adobe Brush) files are for **brush shapes**, not color palettes. Use the **BrushAlphas** folder and **BrushAlphas_MGR** for .abr (and PNG/TGA) brush tips. Use this **Palettes** folder for .aco / .ase / .gpl **color** palettes.
