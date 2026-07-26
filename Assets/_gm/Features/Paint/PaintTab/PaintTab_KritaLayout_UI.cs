using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	/// <summary>
	/// Root layout for the Paint tab, following Krita's painting UI structure:
	/// 1. Toolchest row (workflow modes + preset + Size, Opacity, Flow/Hardness)
	/// 2. Layers section (layer list, add/remove only; container-only, no visibility/opacity/active UI)
	/// 3. Brush Presets section (grid of alphas + Refresh, like PresetDocker)
	/// 4. Tool Options section (bucket, invert, delete, pressure, direction, etc.)
	/// 5. Color / Palette section (current color + palette swatches, like PaletteDocker + color selector)
	/// Attach to the root of the Paint panel content; assign section transforms.
	/// </summary>
	public class PaintTab_KritaLayout_UI : MonoBehaviour
	{
		[Header("Krita-style sections (assign in Paint panel)")]
		[Tooltip("Top row: workflow toggles + brush preset + Size, Opacity, Hardness (like Painter's Toolchest)")]
		[SerializeField] RectTransform _toolchestRow;
		[Tooltip("Layers docker: layer list, add/remove only (container-only; no visibility/opacity/active UI)")]
		[SerializeField] RectTransform _layersSection;
		[Tooltip("Brush Presets docker: grid of brush alphas + Round brushes + Refresh")]
		[SerializeField] RectTransform _brushPresetsSection;
		[Tooltip("Tool Options: bucket, invert, delete, direction, pressure, soft inpaint, etc.")]
		[SerializeField] RectTransform _toolOptionsSection;
		[Tooltip("Color / Palette: current color swatch + palette swatches (ACO/ASE/GPL)")]
		[SerializeField] RectTransform _colorPaletteSection;

		[Header("Optional section headers (TextMeshPro)")]
		[SerializeField] TextMeshProUGUI _headerLayers;
		[SerializeField] TextMeshProUGUI _headerBrushPresets;
		[SerializeField] TextMeshProUGUI _headerToolOptions;
		[SerializeField] TextMeshProUGUI _headerColorPalette;

		[Tooltip("If true, creates minimal section placeholders at runtime when sections are null (so you can drag components in).")]
		[SerializeField] bool _createSectionsIfMissing;

		public RectTransform ToolchestRow => _toolchestRow;
		public RectTransform LayersSection => _layersSection;
		/// <summary>Enable and run CreateSectionsIfMissing (e.g. when Paint panel is created at runtime).</summary>
		public void SetCreateSectionsIfMissing(bool value) { _createSectionsIfMissing = value; CreateSectionsIfMissing(); }
		public RectTransform BrushPresetsSection => _brushPresetsSection;
		public RectTransform ToolOptionsSection => _toolOptionsSection;
		public RectTransform ColorPaletteSection => _colorPaletteSection;

		void Awake()
		{
			if (_createSectionsIfMissing)
				CreateSectionsIfMissing();
			if (_headerLayers != null) _headerLayers.text = "Layers";
			if (_headerBrushPresets != null) _headerBrushPresets.text = "Brush Presets";
			if (_headerToolOptions != null) _headerToolOptions.text = "Tool Options";
			if (_headerColorPalette != null) _headerColorPalette.text = "Color / Palette";
			SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
			ApplyThemeTokens();
		}

		void OnDestroy()
		{
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
		}

		/// <summary>Themes section headers and Content mask shells owned by this layout root.</summary>
		public void ApplyThemeTokens()
		{
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				RestoreHeader(_headerLayers);
				RestoreHeader(_headerBrushPresets);
				RestoreHeader(_headerToolOptions);
				RestoreHeader(_headerColorPalette);
				RestoreSectionShell(_layersSection);
				RestoreSectionShell(_brushPresetsSection);
				RestoreSectionShell(_toolOptionsSection);
				RestoreSectionShell(_colorPaletteSection);
				return;
			}
			var t = SpzUiThemeOps.Active;
			const float headerBasePt = 14f;
			ApplyHeaderScaled(_headerLayers, t.textMuted, headerBasePt);
			ApplyHeaderScaled(_headerBrushPresets, t.textMuted, headerBasePt);
			ApplyHeaderScaled(_headerToolOptions, t.textMuted, headerBasePt);
			ApplyHeaderScaled(_headerColorPalette, t.textMuted, headerBasePt);
			ThemeSectionShell(_layersSection, t);
			ThemeSectionShell(_brushPresetsSection, t);
			ThemeSectionShell(_toolOptionsSection, t);
			ThemeSectionShell(_colorPaletteSection, t);
		}

		static void RestoreHeader(TextMeshProUGUI header) {
			if (header != null)
				SpzUiThemeOps.RestoreAuthoredGraphic(header);
		}

		static void RestoreSectionShell(RectTransform section) {
			if (section == null) return;
			foreach (var g in section.GetComponentsInChildren<Graphic>(true))
				SpzUiThemeOps.RestoreAuthoredGraphic(g);
		}

		static void ApplyHeaderScaled(TextMeshProUGUI header, Color color, float basePt)
		{
			if (header != null)
				SpzUiThemeOps.ApplyBoundChromeTmp(header, color, basePt);
		}

		static void ThemeSectionShell(RectTransform section, SpzUiThemeOps.ThemeTokens t)
		{
			if (section == null) return;
			Transform content = section;
			// Prefab: section root with child Content; runtime CreateSection returns ScrollContent.
			if (section.parent != null && section.parent.name == "Content")
				content = section.parent;
			else
			{
				var child = section.Find("Content");
				if (child != null) content = child;
			}
			var maskImg = content.GetComponent<Image>();
			if (maskImg != null)
				SpzUiThemeOps.ApplyBoundChromeGraphic(maskImg, t.fieldBg);
			Transform header = null;
			if (content != section)
				header = section.Find("Header");
			else if (section.parent != null)
				header = section.parent.Find("Header");
			if (header == null)
				header = section.Find("Header");
			if (header != null)
			{
				var tmp = header.GetComponent<TextMeshProUGUI>();
				if (tmp != null)
					SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textMuted, 14f);
			}
		}

		void OnValidate()
		{
			if (_headerLayers != null && string.IsNullOrEmpty(_headerLayers.text)) _headerLayers.text = "Layers";
			if (_headerBrushPresets != null && string.IsNullOrEmpty(_headerBrushPresets.text)) _headerBrushPresets.text = "Brush Presets";
			if (_headerToolOptions != null && string.IsNullOrEmpty(_headerToolOptions.text)) _headerToolOptions.text = "Tool Options";
			if (_headerColorPalette != null && string.IsNullOrEmpty(_headerColorPalette.text)) _headerColorPalette.text = "Color / Palette";
		}

		void CreateSectionsIfMissing()
		{
			var root = transform as RectTransform;
			if (root == null) return;
			if (_toolchestRow == null) _toolchestRow = CreateSection(root, "1_Toolchest_Row", SectionStyle.HorizontalFixed, null, 36);
			if (_layersSection == null) _layersSection = CreateSection(root, "2_Layers", SectionStyle.VerticalFlex, "Layers", 60, 1.5f);
			if (_brushPresetsSection == null) _brushPresetsSection = CreateSection(root, "3_BrushPresets", SectionStyle.VerticalFlex, "Brush Presets", 140, 1f);
			if (_toolOptionsSection == null) _toolOptionsSection = CreateSection(root, "4_ToolOptions", SectionStyle.VerticalFlex, "Tool Options", 30, 0.5f);
			if (_colorPaletteSection == null) _colorPaletteSection = CreateSection(root, "5_ColorPalette", SectionStyle.VerticalFlex, "Color / Palette", 50, 1f);
			LayoutRebuilder.ForceRebuildLayoutImmediate(root);
		}

		enum SectionStyle { HorizontalFixed, VerticalFlex }

		static RectTransform CreateSection(RectTransform parent, string sectionName, SectionStyle style, string headerLabel, float minH = 40, float flex = 0)
		{
			var go = new GameObject(sectionName);
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0.5f, 1);
			rect.sizeDelta = Vector2.zero;

			var le = go.AddComponent<LayoutElement>();
			le.minHeight = minH;
			le.flexibleHeight = flex;

			if (style == SectionStyle.HorizontalFixed)
			{
				le.preferredHeight = minH;
				var h = go.AddComponent<HorizontalLayoutGroup>();
				h.spacing = 8;
				h.childAlignment = TextAnchor.MiddleLeft;
				h.childControlWidth = false;
				h.childControlHeight = true;
				h.childForceExpandWidth = false;
				return rect;
			}

			var outerVlg = go.AddComponent<VerticalLayoutGroup>();
			outerVlg.spacing = 2;
			outerVlg.childAlignment = TextAnchor.UpperLeft;
			outerVlg.childControlWidth = true;
			outerVlg.childControlHeight = true;
			outerVlg.childForceExpandWidth = true;
			outerVlg.childForceExpandHeight = false;
			outerVlg.padding = new RectOffset(0, 0, 0, 2);

			if (!string.IsNullOrEmpty(headerLabel))
				AddSectionHeader(go.transform, headerLabel);

			var contentGo = new GameObject("Content");
			contentGo.transform.SetParent(go.transform, false);
			var contentRect = contentGo.AddComponent<RectTransform>();
			contentRect.anchorMin = Vector2.zero;
			contentRect.anchorMax = Vector2.one;
			contentRect.sizeDelta = Vector2.zero;
			var contentLE = contentGo.AddComponent<LayoutElement>();
			contentLE.flexibleHeight = 1;
			contentLE.flexibleWidth = 1;

			var maskImg = contentGo.AddComponent<Image>();
			maskImg.color = new Color(0.18f, 0.18f, 0.18f, 1f);
			maskImg.raycastTarget = true;
			var mask = contentGo.AddComponent<Mask>();
			mask.showMaskGraphic = true;

			var scrollGo = contentGo;
			var scrollInnerGo = new GameObject("ScrollContent");
			scrollInnerGo.transform.SetParent(scrollGo.transform, false);
			var scrollInnerRect = scrollInnerGo.AddComponent<RectTransform>();
			scrollInnerRect.anchorMin = new Vector2(0, 1);
			scrollInnerRect.anchorMax = Vector2.one;
			scrollInnerRect.pivot = new Vector2(0, 1); // top-left so content stays flush left (aligns with Load ABR/PNG button)
			scrollInnerRect.sizeDelta = Vector2.zero;

			var scrollInnerVlg = scrollInnerGo.AddComponent<VerticalLayoutGroup>();
			scrollInnerVlg.spacing = 1; // compact (Photoshop-style)
			scrollInnerVlg.childAlignment = TextAnchor.UpperLeft;
			scrollInnerVlg.childControlWidth = true;
			scrollInnerVlg.childControlHeight = false;
			scrollInnerVlg.childForceExpandWidth = true;
			scrollInnerVlg.childForceExpandHeight = false;
			// Extra bottom padding (2+14) so brush presets can scroll to show last thumbnail
			scrollInnerVlg.padding = new RectOffset(2, 2, 2, 16);

			var csf = scrollInnerGo.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

			var scrollRect = scrollGo.AddComponent<ScrollRect>();
			scrollRect.content = scrollInnerRect;
			scrollRect.horizontal = false;
			scrollRect.vertical = true;
			scrollRect.movementType = ScrollRect.MovementType.Clamped;
			scrollRect.scrollSensitivity = 20f;
			scrollRect.viewport = contentRect;

			return scrollInnerRect;
		}

		static void AddSectionHeader(Transform sectionParent, string label)
		{
			var headerGo = new GameObject("Header");
			headerGo.transform.SetParent(sectionParent, false);
			var headerRect = headerGo.AddComponent<RectTransform>();
			headerRect.anchorMin = new Vector2(0, 1);
			headerRect.anchorMax = new Vector2(1, 1);
			headerRect.pivot = new Vector2(0, 1);
			headerRect.anchoredPosition = Vector2.zero;
			headerRect.sizeDelta = new Vector2(0, 18);
			var le = headerGo.AddComponent<LayoutElement>();
			le.minHeight = 18;
			le.preferredHeight = 18;
			le.flexibleWidth = 1;
			le.flexibleHeight = 0;
			var text = headerGo.AddComponent<TextMeshProUGUI>();
			text.text = label;
			text.fontSize = 11;
			text.color = new Color(0.7f, 0.7f, 0.75f, 1f);
			text.raycastTarget = false;
			text.overflowMode = TMPro.TextOverflowModes.Ellipsis;
			text.fontStyle = FontStyles.Bold;
		}
	}
}
