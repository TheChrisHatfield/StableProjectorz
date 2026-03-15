using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// Holds the currently loaded color palette (from ACO, ASE, or GPL file) for the brush.
	/// Drop .aco, .ase, or .gpl files into the Palettes folder, or use "Load ASE/ACO/GPL..." in the Paint tab.
	/// </summary>
	public class ColorPalette_MGR : MonoBehaviour
	{
		public static ColorPalette_MGR instance { get; private set; }

		List<Color> _currentPalette = new List<Color>();
		string _currentPaletteName;
		/// <summary> Full path of last successfully loaded palette (for Reload). </summary>
		string _currentPalettePath;

		/// <summary> Current palette colors (read-only). Empty if none loaded. </summary>
		public IReadOnlyList<Color> CurrentPalette => _currentPalette;

		public string CurrentPaletteName => _currentPaletteName;

		public bool HasPalette => _currentPalette.Count > 0;

		public static event Action<List<Color>> OnPaletteChanged;

		void Awake()
		{
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			PaletteLoader.EnsurePalettesFolderExists();
		}

		void OnDestroy()
		{
			if (instance == this) instance = null;
		}

		/// <summary> Get list of palette file paths in the user Palettes folder. </summary>
		public static List<string> GetPalettePathsInFolder()
		{
			var list = new List<string>();
			string folder = PaletteLoader.PalettesFolderPath;
			if (!Directory.Exists(folder)) return list;
			try
			{
				list.AddRange(Directory.GetFiles(folder, "*.aco", SearchOption.TopDirectoryOnly));
				list.AddRange(Directory.GetFiles(folder, "*.ase", SearchOption.TopDirectoryOnly));
				list.AddRange(Directory.GetFiles(folder, "*.gpl", SearchOption.TopDirectoryOnly));
			}
			catch { }
			return list;
		}

		/// <summary> Opens a file dialog to pick an ASE, ACO, or GPL file and load it as the current palette. </summary>
		public void OpenLoadPaletteDialog()
		{
			FileBrowser.SetFilters(true,
				new FileBrowser.Filter("Palette", "ase", "aco", "gpl"),
				new FileBrowser.Filter("ASE (Adobe Swatch)", "ase"),
				new FileBrowser.Filter("ACO (Adobe Color)", "aco"),
				new FileBrowser.Filter("GPL (GIMP)", "gpl"));
			FileBrowser.SetDefaultFilter("ase");
			FileBrowser.ShowLoadDialog(
				(paths) => {
					if (paths != null && paths.Length > 0 && LoadPalette(paths[0]) && Viewport_StatusText.instance != null)
						Viewport_StatusText.instance.ShowStatusText("Palette loaded: " + CurrentPaletteName, false, 2f, false);
				},
				null,
				FileBrowser.PickMode.Files,
				false,
				null,
				null,
				"Load palette (ASE / ACO / GPL)",
				"Load");
		}

		/// <summary> Opens a file dialog to pick an ASE, ACO, or GPL file and add its swatches to the current palette. </summary>
		public void OpenAddPaletteDialog()
		{
			FileBrowser.SetFilters(true,
				new FileBrowser.Filter("Palette", "ase", "aco", "gpl"),
				new FileBrowser.Filter("ASE (Adobe Swatch)", "ase"),
				new FileBrowser.Filter("ACO (Adobe Color)", "aco"),
				new FileBrowser.Filter("GPL (GIMP)", "gpl"));
			FileBrowser.SetDefaultFilter("ase");
			FileBrowser.ShowLoadDialog(
				(paths) => {
					if (paths != null && paths.Length > 0)
					{
						int n = LoadPaletteAdd(paths[0]);
						if (Viewport_StatusText.instance != null)
							Viewport_StatusText.instance.ShowStatusText(n > 0 ? "Added " + n + " swatches" : "No colors in file or load failed", false, 2f, false);
					}
				},
				null,
				FileBrowser.PickMode.Files,
				false,
				null,
				null,
				"Add palette (ASE / ACO / GPL)",
				"Add");
		}

		/// <summary> Load a palette from file (path or filename in Palettes folder). Replaces current palette. </summary>
		public bool LoadPalette(string filePathOrName)
		{
			string path = ResolvePalettePath(filePathOrName);
			var colors = PaletteLoader.LoadFromFile(path);
			if (colors == null || colors.Count == 0) return false;
			_currentPalette = colors;
			_currentPaletteName = Path.GetFileName(path);
			_currentPalettePath = path;
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
			return true;
		}

		/// <summary> Re-load the current palette from the last loaded file. Use when the file was fixed or when ASE didn't load. Returns true if a path was set and reload succeeded. </summary>
		public bool ReloadCurrentPalette()
		{
			if (string.IsNullOrEmpty(_currentPalettePath)) return false;
			var colors = PaletteLoader.LoadFromFile(_currentPalettePath);
			if (colors == null || colors.Count == 0) return false;
			_currentPalette = colors;
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
			return true;
		}

		/// <summary> Load colors from file and add them to the current palette (does not replace). Returns number of colors added, or 0 on failure. </summary>
		public int LoadPaletteAdd(string filePathOrName)
		{
			string path = ResolvePalettePath(filePathOrName);
			var colors = PaletteLoader.LoadFromFile(path);
			if (colors == null || colors.Count == 0) return 0;
			int added = 0;
			foreach (var c in colors)
			{
				_currentPalette.Add(c);
				added++;
			}
			if (added > 0)
			{
				if (string.IsNullOrEmpty(_currentPaletteName))
					_currentPaletteName = Path.GetFileName(path);
				else
					_currentPaletteName = _currentPaletteName + " + " + Path.GetFileName(path);
				OnPaletteChanged?.Invoke(_currentPalette);
				RefreshAllSwatchesUI();
			}
			return added;
		}

		static string ResolvePalettePath(string filePathOrName)
		{
			if (string.IsNullOrEmpty(filePathOrName)) return filePathOrName;
			return Path.IsPathRooted(filePathOrName)
				? filePathOrName
				: Path.Combine(PaletteLoader.PalettesFolderPath, filePathOrName);
		}

		/// <summary> Force all palette swatch UIs to refresh from current palette. Call after external changes or when ASE/UI didn't update. </summary>
		public void RefreshSwatchesUI()
		{
			RefreshAllSwatchesUI();
		}

		void RefreshAllSwatchesUI()
		{
			var swatches = UnityEngine.Object.FindObjectsOfType<PaletteSwatches_UI>(true);
			for (int i = 0; i < swatches.Length; i++)
				swatches[i].RefreshFromCurrentPalette();
		}

		/// <summary> Clear current palette. </summary>
		public void ClearPalette()
		{
			_currentPalette.Clear();
			_currentPaletteName = null;
			_currentPalettePath = null;
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
		}

		/// <summary> Get color at index; returns black if out of range. </summary>
		public Color GetColor(int index)
		{
			if (index < 0 || index >= _currentPalette.Count) return Color.black;
			return _currentPalette[index];
		}

		/// <summary> Default palette colors shown when no file is loaded. Same order as PaletteSwatches_UI.BuildDefaultPalette. </summary>
		public static readonly Color[] DefaultPaletteColors = new Color[] {
			Color.white, Color.black, Color.red, Color.green, Color.blue,
			Color.yellow, Color.cyan, Color.magenta, new Color(1f, 0.5f, 0f),
			new Color(0.5f, 0f, 0.5f), new Color(0f, 0.5f, 0.5f), Color.gray,
			new Color(0.3f, 0.15f, 0f), new Color(0.9f, 0.8f, 0.6f),
			new Color(0.2f, 0.4f, 0.2f), new Color(0.6f, 0.6f, 0.9f)
		};

		/// <summary> If current palette is empty, fill it with the default palette so "edit default swatch" works (double-click default → commit updates that slot instead of replacing all with one color). </summary>
		public void EnsureDefaultPaletteIfEmpty()
		{
			if (_currentPalette.Count > 0) return;
			_currentPalette.Clear();
			for (int i = 0; i < DefaultPaletteColors.Length; i++)
				_currentPalette.Add(DefaultPaletteColors[i]);
			_currentPaletteName = null;
			_currentPalettePath = null;
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
		}

		/// <summary> Add a swatch to the current palette (in-memory). </summary>
		public void AddSwatch(Color c)
		{
			_currentPalette.Add(c);
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
		}

		/// <summary> Remove swatch at index. Returns true if removed. </summary>
		public bool RemoveSwatchAt(int index)
		{
			if (index < 0 || index >= _currentPalette.Count) return false;
			_currentPalette.RemoveAt(index);
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
			return true;
		}

		/// <summary> Set color at index (edit swatch in-place). Returns true if updated. </summary>
		public bool SetColorAt(int index, Color c)
		{
			if (index < 0 || index >= _currentPalette.Count) return false;
			_currentPalette[index] = c;
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
			return true;
		}

		/// <summary> Save current palette to project (SPZ). </summary>
		public void Save(StableProjectorz_SL spz)
		{
			if (spz == null) return;
			spz.colorPalette = new ColorPalette_SL();
			spz.colorPalette.paletteName = _currentPaletteName;
			spz.colorPalette.colors = new List<ColorSerializable>();
			for (int i = 0; i < _currentPalette.Count; i++)
				spz.colorPalette.colors.Add((ColorSerializable)_currentPalette[i]);
		}

		/// <summary> Load palette from project (SPZ). Restores swatch colors and display name. </summary>
		public void Load(StableProjectorz_SL spz)
		{
			if (spz?.colorPalette == null || spz.colorPalette.colors == null || spz.colorPalette.colors.Count == 0)
				return;
			_currentPalette.Clear();
			foreach (var cs in spz.colorPalette.colors)
				_currentPalette.Add(cs.toColor());
			_currentPaletteName = spz.colorPalette.paletteName;
			_currentPalettePath = null; // no file path after load-from-project
			OnPaletteChanged?.Invoke(_currentPalette);
			RefreshAllSwatchesUI();
		}
	}
}
