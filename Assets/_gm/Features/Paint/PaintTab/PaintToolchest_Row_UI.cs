using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Krita-style "Painter's Toolchest" top row: workflow mode toggles + brush preset access + quick sliders (Size, Opacity, Hardness).
	/// Place workflow ribbon, preset button, and BrushRibbon size/opacity/hardness here. Does not create UI; only references and optional toggle for Brush Presets section.
	/// </summary>
	public class PaintToolchest_Row_UI : MonoBehaviour
	{
		[Header("Toolchest content (assign existing components)")]
		[Tooltip("Workflow mode toggles: Proj Mask, Inpaint Color, No Color, Entire Object, Where Empty")]
		[SerializeField] RectTransform _workflowModeStrip;
		[Tooltip("Button that expands/shows Brush Presets section (optional)")]
		[SerializeField] Button _brushPresetToggleButton;
		[Tooltip("Size slider (BrushRibbon_UI_Size)")]
		[SerializeField] RectTransform _sizeSlider;
		[Tooltip("Opacity slider (BrushRibbon_UI_Opacity)")]
		[SerializeField] RectTransform _opacitySlider;
		[Tooltip("Hardness / brush shape (BrushRibbon_UI_Hardness)")]
		[SerializeField] RectTransform _hardnessControl;
		[Tooltip("Color / FG swatch (BrushRibbon_UI_Colors)")]
		[SerializeField] RectTransform _colorSwatch;

		[Header("Optional: show/hide Brush Presets section")]
		[SerializeField] PaintTab_KritaLayout_UI _paintTabLayout;
		[SerializeField] bool _toggleBrushPresetsSectionOnButton;

		void Awake()
		{
			if (_brushPresetToggleButton != null && _paintTabLayout != null && _paintTabLayout.BrushPresetsSection != null && _toggleBrushPresetsSectionOnButton)
			{
				bool startVisible = _paintTabLayout.BrushPresetsSection.gameObject.activeSelf;
				_brushPresetToggleButton.onClick.AddListener(ToggleBrushPresetsSection);
			}
		}

		void ToggleBrushPresetsSection()
		{
			if (_paintTabLayout == null || _paintTabLayout.BrushPresetsSection == null) return;
			var go = _paintTabLayout.BrushPresetsSection.gameObject;
			go.SetActive(!go.activeSelf);
		}
	}
}
