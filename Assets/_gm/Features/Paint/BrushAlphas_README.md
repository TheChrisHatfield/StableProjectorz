# Custom Brush Alphas

You can use **custom brush shapes** (alphas) instead of only the round brushes: e.g. text, patterns, material details. Great for combining a **material-style prompt** with a **text-shaped** or **detail-shaped** brush for more control over layering.

## How to use

1. **Folder location**  
   Custom alphas are loaded from:
   - **Windows:** `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\StableProjectorz\BrushAlphas`
   - **macOS:** `~/Library/Application Support/<CompanyName>/<ProductName>/StableProjectorz/BrushAlphas`
   - In code: `BrushAlphas_MGR.BrushAlphasFolderPath` (same as `Application.persistentDataPath/StableProjectorz/BrushAlphas`).

2. **Add your alphas**  
   - Create the `BrushAlphas` folder if it doesn’t exist (the app can create it on first run).
   - Drop **PNG**, **TGA**, or **ABR** (Adobe Photoshop brush) files into that folder.
   - **PNG/TGA:** The brush uses the **alpha channel** as the stamp (e.g. white text on transparent = paint where alpha is white). Grayscale images also work (luminance = brush strength).
   - **ABR:** Photoshop brush sets are parsed best-effort (version 1/2); each brush in the file becomes a selectable brush alpha. Complex or v6+ ABR may need to be exported as PNG.

3. **In the app**  
   - Use the **brush shape / alpha picker** in the paint ribbon to select a custom alpha (grid of thumbnails).
   - **Round brushes** (soft / medium / hard) are still available via the hardness button (H or Ctrl+1/2/3).
   - **Refresh:** If you add or remove files in the folder while the app is running, use the **Refresh** button in the alpha picker to rescan.
   - **Load brush file…:** In the Paint tab (Brush Presets section), use **Load brush file…** (or **Load ABR / PNG…**) to pick an ABR, PNG, or TGA from anywhere; the file is copied into the BrushAlphas folder and the grid updates.

4. **Save/Load**  
   - The currently selected brush (built-in or custom alpha) is saved with the project so it restores on load.

## Tips

- **Text alphas:** Export text as PNG with transparent background; the character shape becomes the brush.
- **Material details:** Use small tileable or detail alphas (scratches, weave, etc.) and a material-style prompt for layered texture control.
- **Resolution:** Alphas are scaled to the brush size; very large PNGs may be slower. 256–512 px is usually enough.

## Setup in Unity (one-time)

- Add a **BrushAlphas_MGR** component to the scene (e.g. under the same object as the paint ribbon).
- Assign the **same 3 round-brush Sprites** to `BrushAlphas_MGR._builtInBrushShapes` (in the same order as in BrushRibbon_UI_Hardness: soft, medium, hard).
- Optionally add **BrushRibbon_UI_AlphaPicker**: assign the same `BrushAlphas_MGR` and `BrushRibbon_UI_Hardness`, a grid root, a thumbnail template (with RawImage + Button), and Round/Refresh buttons. Assign **Load brush file** button to enable loading ABR/PNG/TGA from a file dialog (files are copied into the BrushAlphas folder).
- In **BrushRibbon_UI_Hardness**, assign the **BrushAlphas_MGR** reference so the current stamp comes from the manager (built-in or custom).
