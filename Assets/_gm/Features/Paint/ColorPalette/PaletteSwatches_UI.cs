using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Shows the current color palette as clickable swatches. When the user clicks a swatch,
	/// sets the brush color (via callback). Double-click opens the color picker to edit that swatch.
	/// Assign ColorPalette_MGR and optionally BrushRibbon_UI_Colors for brush color and color picker.
	/// </summary>
	public class PaletteSwatches_UI : MonoBehaviour
	{
		[SerializeField] ColorPalette_MGR _paletteMGR;
		[SerializeField] Transform _swatchRoot;
		[SerializeField] GameObject _swatchTemplate;
		[Tooltip("Optional. When set, clicking a swatch sets the brush color; double-click uses its color picker.")]
		[SerializeField] BrushRibbon_UI_Colors _brushColors;
		[SerializeField] int _maxSwatches = 64;
		[SerializeField] int _swatchSize = 24;
		[Tooltip("Highlight color for the active/selected swatch.")]
		[SerializeField] Color _activeSwatchHighlight = new Color(1f, 1f, 0.6f, 1f);

		/// <summary> Invoked when user clicks a swatch: (color). Connect to brush color setter. </summary>
		public Action<Color> OnSwatchSelected;

		readonly List<GameObject> _instances = new List<GameObject>();
		int _selectedSwatchIndex = -1;
		int _lastClickedIndex = -1;
		float _lastClickTime = -1f;
		const float kDoubleClickTime = 0.35f;

		/// <summary> Index of the currently selected swatch, or -1 if none. </summary>
		public int SelectedSwatchIndex => _selectedSwatchIndex;

		void Start()
		{
			if (_paletteMGR == null) _paletteMGR = ColorPalette_MGR.instance;
			if (_paletteMGR == null) _paletteMGR = FindObjectOfType<ColorPalette_MGR>(true);
			if (_brushColors == null) _brushColors = FindObjectOfType<BrushRibbon_UI_Colors>(true);
			if (_swatchRoot == null) _swatchRoot = transform;
			if (_swatchTemplate == null) _swatchTemplate = CreateDefaultSwatchTemplate();
			if (_swatchTemplate != null)
				_swatchTemplate.SetActive(false);
			if (_paletteMGR != null)
			{
				ColorPalette_MGR.OnPaletteChanged += RebuildSwatches;
				if (_paletteMGR.HasPalette)
					RebuildSwatches(_paletteMGR.CurrentPalette);
				else
					BuildDefaultPalette();
			}
			else
			{
				BuildDefaultPalette();
			}
		}

		GameObject CreateDefaultSwatchTemplate()
		{
			var go = new GameObject("SwatchTemplate");
			go.transform.SetParent(transform, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(_swatchSize, _swatchSize);
			go.AddComponent<Image>();
			go.AddComponent<Button>();
			go.SetActive(false);
			return go;
		}

		void BuildDefaultPalette()
		{
			if (_swatchRoot == null || _swatchTemplate == null) return;
			Color[] defaults = ColorPalette_MGR.DefaultPaletteColors;
			for (int i = 0; i < defaults.Length; i++)
			{
				Color c = defaults[i];
				int index = i;
				var go = Instantiate(_swatchTemplate, _swatchRoot);
				go.SetActive(true);
				_instances.Add(go);
				var img = go.GetComponent<Image>();
				if (img != null) img.color = c;
				var rect = go.GetComponent<RectTransform>();
				if (rect != null) rect.sizeDelta = new Vector2(_swatchSize, _swatchSize);
				var btn = go.GetComponent<Button>();
				if (btn != null)
					btn.onClick.AddListener(() => OnSwatchClicked(index, c, isFromPalette: false));
			}
			_selectedSwatchIndex = -1;
			ApplyHighlightForSelected();
		}

		void OnDestroy()
		{
			ColorPalette_MGR.OnPaletteChanged -= RebuildSwatches;
		}

		/// <summary> Re-resolve manager and rebuild swatches from current palette. Call after loading a palette so the grid updates even if the event was missed. </summary>
		public void RefreshFromCurrentPalette()
		{
			if (_paletteMGR == null) _paletteMGR = ColorPalette_MGR.instance ?? FindObjectOfType<ColorPalette_MGR>(true);
			if (_swatchRoot == null) _swatchRoot = transform;
			if (_swatchTemplate == null) _swatchTemplate = CreateDefaultSwatchTemplate();
			if (_swatchTemplate != null) _swatchTemplate.SetActive(false);
			if (_paletteMGR != null && _paletteMGR.HasPalette)
				RebuildSwatches(_paletteMGR.CurrentPalette);
			else
				BuildDefaultPalette();
		}

		void RebuildSwatches(IReadOnlyList<Color> palette)
		{
			foreach (var g in _instances)
			{
				if (g != null) Destroy(g);
			}
			_instances.Clear();
			if (_swatchRoot == null || _swatchTemplate == null || palette == null) return;
			// Clamp selection so it stays valid after palette change
			if (_selectedSwatchIndex >= palette.Count) _selectedSwatchIndex = palette.Count > 0 ? palette.Count - 1 : -1;
			int n = Mathf.Min(palette.Count, _maxSwatches);
			for (int i = 0; i < n; i++)
			{
				Color c = palette[i];
				int index = i;
				var go = Instantiate(_swatchTemplate, _swatchRoot);
				go.SetActive(true);
				_instances.Add(go);
				var img = go.GetComponent<Image>();
				if (img == null) img = go.GetComponentInChildren<Image>();
				if (img != null) img.color = c;
				var rect = go.GetComponent<RectTransform>();
				if (rect != null) rect.sizeDelta = new Vector2(_swatchSize, _swatchSize);
				var btn = go.GetComponent<Button>();
				if (btn == null) btn = go.GetComponentInChildren<Button>();
				if (btn != null)
					btn.onClick.AddListener(() => OnSwatchClicked(index, c, isFromPalette: true));
			}
			ApplyHighlightForSelected();
		}

		void OnSwatchClicked(int index, Color c, bool isFromPalette)
		{
			float t = Time.unscaledTime;
			bool isDoubleClick = (index == _lastClickedIndex && (t - _lastClickTime) <= kDoubleClickTime);
			_lastClickedIndex = index;
			_lastClickTime = t;

			if (isDoubleClick)
			{
				// Open color picker to edit this swatch (or add one if default palette)
				OpenColorPickerForSwatch(index, c, isFromPalette);
				return;
			}

			_selectedSwatchIndex = index;
			ApplyHighlightForSelected();
			OnSwatchSelected?.Invoke(c);
			if (_brushColors != null) _brushColors.SetBrushColorFromPalette(c);
		}

		void OpenColorPickerForSwatch(int index, Color currentColor, bool isFromPalette)
		{
			if (MouseWorkbench_Zone.instance == null) return;
			// Capture index so commit updates only this swatch, not every swatch
			int swatchIndexToUpdate = index;
			MouseWorkbench_Zone.instance.ShowAtScreenCoord(
				KeyMousePenInput.cursorScreenPos(),
				currentColor,
				(Color newColor) =>
				{
					if (_brushColors != null) _brushColors.SetBrushColorFromPalette(newColor);
					if (isFromPalette && _paletteMGR != null && swatchIndexToUpdate >= 0 && swatchIndexToUpdate < _paletteMGR.CurrentPalette.Count)
					{
						_paletteMGR.SetColorAt(swatchIndexToUpdate, newColor);
						_selectedSwatchIndex = swatchIndexToUpdate;
						ApplyHighlightForSelected();
					}
					else if (!isFromPalette && _paletteMGR != null)
					{
						// Default swatches: promote default palette into manager then edit that slot so we don't replace 16 swatches with 1
						_paletteMGR.EnsureDefaultPaletteIfEmpty();
						if (swatchIndexToUpdate >= 0 && swatchIndexToUpdate < _paletteMGR.CurrentPalette.Count)
						{
							_paletteMGR.SetColorAt(swatchIndexToUpdate, newColor);
							_selectedSwatchIndex = swatchIndexToUpdate;
							ApplyHighlightForSelected();
						}
						else
						{
							_paletteMGR.AddSwatch(newColor);
							_selectedSwatchIndex = _paletteMGR.CurrentPalette.Count - 1;
							ApplyHighlightForSelected();
						}
					}
				},
				MouseWorkbench_Zone.ShowPreference.CenterOnCursor);
		}

		void ApplyHighlightForSelected()
		{
			for (int i = 0; i < _instances.Count; i++)
			{
				var go = _instances[i];
				if (go == null) continue;
				var img = go.GetComponent<Image>();
				if (img == null) img = go.GetComponentInChildren<Image>();
				if (img == null) continue;
				var outline = img.GetComponent<Outline>();
				if (i == _selectedSwatchIndex)
				{
					if (outline == null) outline = img.gameObject.AddComponent<Outline>();
					outline.effectColor = _activeSwatchHighlight;
					outline.effectDistance = new Vector2(2, 2);
				}
				else
				{
					if (outline != null) Destroy(outline);
				}
			}
		}

		/// <summary> Remove the currently selected swatch from the palette. Called by the minus button. </summary>
		public void RemoveSelectedSwatch()
		{
			if (_paletteMGR == null || _selectedSwatchIndex < 0) return;
			if (_selectedSwatchIndex >= _paletteMGR.CurrentPalette.Count) return;
			_paletteMGR.RemoveSwatchAt(_selectedSwatchIndex);
			_selectedSwatchIndex = -1;
		}
	}
}
