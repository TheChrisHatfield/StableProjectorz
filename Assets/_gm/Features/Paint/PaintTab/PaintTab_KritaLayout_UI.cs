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
	/// Micro: docs/delta/20_micro/paint-tab-section-splitters.md
	/// </summary>
	public class PaintTab_KritaLayout_UI : MonoBehaviour
	{
		public const string SplitLayersBrush = "Split_Layers_Brush";
		public const string SplitBrushTool = "Split_Brush_Tool";
		public const string SplitToolColor = "Split_Tool_Color";

		public const string PrefKeyLayers = "spz.paintTab.flex.layers";
		public const string PrefKeyBrush = "spz.paintTab.flex.brush";
		public const string PrefKeyTool = "spz.paintTab.flex.tool";
		public const string PrefKeyColor = "spz.paintTab.flex.color";

		public const float DefaultFlexLayers = 1.5f;
		public const float DefaultFlexBrush = 1f;
		public const float DefaultFlexTool = 0.5f;
		public const float DefaultFlexColor = 1f;

		static readonly Color SplitterBarDefault = new Color(0.28f, 0.28f, 0.3f, 1f);

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

		/// <summary>Optional override for PlayerPrefs keys (EditMode tests). Null = production keys.</summary>
		public static string PrefsKeyPrefixOverride;

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
				RestoreSplittersUnder(transform);
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
			ThemeSplittersUnder(transform, t);
		}

		static void RestoreHeader(TextMeshProUGUI header) {
			if (header == null) return;
			// Full unwind: color + Nomad typography (tracking/font) — graphic-only left sticky spacing.
			SpzUiThemeOps.RestoreBoundChromeUnder(header.transform);
		}

		static void RestoreSectionShell(RectTransform section) {
			if (section == null) return;
			SpzUiThemeOps.RestoreBoundChromeUnder(section);
			// ThemeSectionShell may BoundChrome-tint parent Content when section is nested under it.
			if (section.parent != null && section.parent.name == "Content")
				SpzUiThemeOps.RestoreBoundChromeUnder(section.parent);
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
			if (_layersSection == null) _layersSection = CreateSection(root, "2_Layers", SectionStyle.VerticalFlex, "Layers", 60, DefaultFlexLayers);
			if (_brushPresetsSection == null) _brushPresetsSection = CreateSection(root, "3_BrushPresets", SectionStyle.VerticalFlex, "Brush Presets", 140, DefaultFlexBrush);
			if (_toolOptionsSection == null) _toolOptionsSection = CreateSection(root, "4_ToolOptions", SectionStyle.VerticalFlex, "Tool Options", 30, DefaultFlexTool);
			if (_colorPaletteSection == null) _colorPaletteSection = CreateSection(root, "5_ColorPalette", SectionStyle.VerticalFlex, "Color / Palette", 50, DefaultFlexColor);
			EnsureSectionSplitters();
			LayoutRebuilder.ForceRebuildLayoutImmediate(root);
		}

		/// <summary>
		/// Resolves the section root that owns <see cref="LayoutElement"/> flexible height.
		/// Runtime CreateSection stores ScrollContent in section refs; prefab refs are section roots.
		/// </summary>
		public static RectTransform ResolveSectionRoot(RectTransform sectionRef)
		{
			if (sectionRef == null) return null;
			for (int i = 0; i < sectionRef.childCount; i++) {
				var ch = sectionRef.GetChild(i);
				if (ch != null && ch.name == "Content" && ch.GetComponent<ScrollRect>() != null)
					return sectionRef;
			}
			var parent = sectionRef.parent;
			if (parent != null && parent.GetComponent<ScrollRect>() != null)
				return parent.parent as RectTransform;
			return sectionRef;
		}

		static LayoutElement GetSectionLayoutElement(RectTransform sectionRef)
		{
			var root = ResolveSectionRoot(sectionRef);
			return root != null ? root.GetComponent<LayoutElement>() : null;
		}

		static string PrefKey(string productionKey)
		{
			if (string.IsNullOrEmpty(PrefsKeyPrefixOverride))
				return productionKey;
			return PrefsKeyPrefixOverride + productionKey;
		}

		/// <summary>
		/// Inserts / reuses three adjacent splitters between flex sections and applies saved weights.
		/// Safe to call when sections already exist (CollectNow connectivity path).
		/// </summary>
		public void EnsureSectionSplitters()
		{
			var root = transform as RectTransform;
			if (root == null) return;

			var layersRoot = ResolveSectionRoot(_layersSection);
			var brushRoot = ResolveSectionRoot(_brushPresetsSection);
			var toolRoot = ResolveSectionRoot(_toolOptionsSection);
			var colorRoot = ResolveSectionRoot(_colorPaletteSection);
			if (layersRoot == null || brushRoot == null || toolRoot == null || colorRoot == null)
				return;

			var layersLe = layersRoot.GetComponent<LayoutElement>() ?? layersRoot.gameObject.AddComponent<LayoutElement>();
			var brushLe = brushRoot.GetComponent<LayoutElement>() ?? brushRoot.gameObject.AddComponent<LayoutElement>();
			var toolLe = toolRoot.GetComponent<LayoutElement>() ?? toolRoot.gameObject.AddComponent<LayoutElement>();
			var colorLe = colorRoot.GetComponent<LayoutElement>() ?? colorRoot.gameObject.AddComponent<LayoutElement>();

			// Prefab roots may lack mins/flex; keep usable clamps before first drag.
			EnsureFlexSectionDefaults(layersLe, 60f, DefaultFlexLayers);
			EnsureFlexSectionDefaults(brushLe, 140f, DefaultFlexBrush);
			EnsureFlexSectionDefaults(toolLe, 30f, DefaultFlexTool);
			EnsureFlexSectionDefaults(colorLe, 50f, DefaultFlexColor);

			System.Action onEnded = OnSplitterDragEnded;
			System.Action onBegan = LockAllFlexSectionsFromRect;

			var s1 = PaintTab_SectionSplitter_UI.EnsureOn(root, SplitLayersBrush, layersLe, brushLe, onEnded, SplitterBarDefault, onBegan);
			var s2 = PaintTab_SectionSplitter_UI.EnsureOn(root, SplitBrushTool, brushLe, toolLe, onEnded, SplitterBarDefault, onBegan);
			var s3 = PaintTab_SectionSplitter_UI.EnsureOn(root, SplitToolColor, toolLe, colorLe, onEnded, SplitterBarDefault, onBegan);

			// Sibling order: toolchest, layers, split, brush, split, tool, split, color
			int idx = 0;
			if (_toolchestRow != null) {
				_toolchestRow.SetSiblingIndex(idx);
				idx++;
			}
			layersRoot.SetSiblingIndex(idx++);
			if (s1 != null) s1.transform.SetSiblingIndex(idx++);
			brushRoot.SetSiblingIndex(idx++);
			if (s2 != null) s2.transform.SetSiblingIndex(idx++);
			toolRoot.SetSiblingIndex(idx++);
			if (s3 != null) s3.transform.SetSiblingIndex(idx++);
			colorRoot.SetSiblingIndex(idx);

			// CollectNow / poll can re-enter Ensure while a splitter drag is active.
			// Re-applying prefs would unlock mid-drag and snap heights back to saved flex weights.
			if (!IsAnySplitterDragging(root))
				ApplySavedSectionWeights();
			ApplyThemeTokens();
		}

		/// <summary>True while a section splitter reports an active left-button drag.</summary>
		public static bool IsAnySplitterDragging(Transform root)
		{
			if (root == null) return false;
			return IsSplitterDragging(root.Find(SplitLayersBrush))
				|| IsSplitterDragging(root.Find(SplitBrushTool))
				|| IsSplitterDragging(root.Find(SplitToolColor));
		}

		static bool IsSplitterDragging(Transform splitTf)
		{
			if (splitTf == null) return false;
			var s = splitTf.GetComponent<PaintTab_SectionSplitter_UI>();
			return s != null && s.IsDragging;
		}

		/// <summary>Legacy preferredHeight lock heuristic (tests / diagnostics).</summary>
		public static bool IsAnyFlexSectionDragLocked(
			LayoutElement layers, LayoutElement brush, LayoutElement tool, LayoutElement color)
		{
			return IsDragLocked(layers) || IsDragLocked(brush) || IsDragLocked(tool) || IsDragLocked(color);
		}

		static bool IsDragLocked(LayoutElement le)
		{
			return le != null && le.flexibleHeight <= 0f && le.preferredHeight > 0f;
		}

		static void EnsureFlexSectionDefaults(LayoutElement le, float minH, float defaultFlex)
		{
			if (le == null) return;
			if (le.minHeight < 1f)
				le.minHeight = minH;
			// Only seed flex when unset and not mid-drag locked.
			if (!IsDragLocked(le) && le.flexibleHeight <= 0f && le.preferredHeight <= 0f)
				le.flexibleHeight = defaultFlex;
		}

		public void ApplySavedSectionWeights()
		{
			ApplyFlexWeights(
				PlayerPrefs.GetFloat(PrefKey(PrefKeyLayers), DefaultFlexLayers),
				PlayerPrefs.GetFloat(PrefKey(PrefKeyBrush), DefaultFlexBrush),
				PlayerPrefs.GetFloat(PrefKey(PrefKeyTool), DefaultFlexTool),
				PlayerPrefs.GetFloat(PrefKey(PrefKeyColor), DefaultFlexColor));
		}

		public void SaveSectionWeights(float layers, float brush, float tool, float color)
		{
			PlayerPrefs.SetFloat(PrefKey(PrefKeyLayers), layers);
			PlayerPrefs.SetFloat(PrefKey(PrefKeyBrush), brush);
			PlayerPrefs.SetFloat(PrefKey(PrefKeyTool), tool);
			PlayerPrefs.SetFloat(PrefKey(PrefKeyColor), color);
			PlayerPrefs.Save();
		}

		/// <summary>Sets flexibleHeight on the four flex section roots; clears preferred lock.</summary>
		public void ApplyFlexWeights(float layers, float brush, float tool, float color)
		{
			SanitizeFlexWeights(ref layers, ref brush, ref tool, ref color);
			SetFlex(GetSectionLayoutElement(_layersSection), layers);
			SetFlex(GetSectionLayoutElement(_brushPresetsSection), brush);
			SetFlex(GetSectionLayoutElement(_toolOptionsSection), tool);
			SetFlex(GetSectionLayoutElement(_colorPaletteSection), color);
		}

		/// <summary>Rejects NaN/Inf/non-positive weights; falls back to defaults when the sum is unusable.</summary>
		public static void SanitizeFlexWeights(ref float layers, ref float brush, ref float tool, ref float color)
		{
			layers = SanitizedFlexOrDefault(layers, DefaultFlexLayers);
			brush = SanitizedFlexOrDefault(brush, DefaultFlexBrush);
			tool = SanitizedFlexOrDefault(tool, DefaultFlexTool);
			color = SanitizedFlexOrDefault(color, DefaultFlexColor);
			float sum = layers + brush + tool + color;
			if (!(sum > 0.04f) || float.IsNaN(sum) || float.IsInfinity(sum)) {
				layers = DefaultFlexLayers;
				brush = DefaultFlexBrush;
				tool = DefaultFlexTool;
				color = DefaultFlexColor;
			}
		}

		static float SanitizedFlexOrDefault(float value, float fallback)
		{
			if (float.IsNaN(value) || float.IsInfinity(value) || value < 0.01f)
				return fallback;
			return value;
		}

		static void SetFlex(LayoutElement le, float flex)
		{
			if (le == null) return;
			le.flexibleHeight = Mathf.Max(0.01f, flex);
			le.preferredHeight = -1f;
		}

		void OnSplitterDragEnded()
		{
			var layersLe = GetSectionLayoutElement(_layersSection);
			var brushLe = GetSectionLayoutElement(_brushPresetsSection);
			var toolLe = GetSectionLayoutElement(_toolOptionsSection);
			var colorLe = GetSectionLayoutElement(_colorPaletteSection);
			if (layersLe == null || brushLe == null || toolLe == null || colorLe == null)
				return;

			float hL = SectionHeightForWeight(layersLe);
			float hB = SectionHeightForWeight(brushLe);
			float hT = SectionHeightForWeight(toolLe);
			float hC = SectionHeightForWeight(colorLe);
			float sumH = hL + hB + hT + hC;
			if (sumH < 1f) return;

			const float defaultSum = DefaultFlexLayers + DefaultFlexBrush + DefaultFlexTool + DefaultFlexColor;
			float wL = hL / sumH * defaultSum;
			float wB = hB / sumH * defaultSum;
			float wT = hT / sumH * defaultSum;
			float wC = hC / sumH * defaultSum;
			ApplyFlexWeights(wL, wB, wT, wC);
			SaveSectionWeights(wL, wB, wT, wC);
			var root = transform as RectTransform;
			if (root != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(root);
		}

		void LockAllFlexSectionsFromRect()
		{
			PaintTab_SectionSplitter_UI.LockPreferredFromRect(GetSectionLayoutElement(_layersSection));
			PaintTab_SectionSplitter_UI.LockPreferredFromRect(GetSectionLayoutElement(_brushPresetsSection));
			PaintTab_SectionSplitter_UI.LockPreferredFromRect(GetSectionLayoutElement(_toolOptionsSection));
			PaintTab_SectionSplitter_UI.LockPreferredFromRect(GetSectionLayoutElement(_colorPaletteSection));
		}

		static float SectionHeightForWeight(LayoutElement le)
		{
			if (le == null) return 1f;
			if (le.preferredHeight > 0f) return le.preferredHeight;
			var rt = le.transform as RectTransform;
			if (rt != null && rt.rect.height > 1f) return rt.rect.height;
			return Mathf.Max(le.minHeight, 1f);
		}

		static void ThemeSplittersUnder(Transform root, SpzUiThemeOps.ThemeTokens t)
		{
			if (root == null) return;
			ThemeOneSplitter(root.Find(SplitLayersBrush), t);
			ThemeOneSplitter(root.Find(SplitBrushTool), t);
			ThemeOneSplitter(root.Find(SplitToolColor), t);
		}

		static void ThemeOneSplitter(Transform splitTf, SpzUiThemeOps.ThemeTokens t)
		{
			if (splitTf == null) return;
			var img = splitTf.GetComponent<Image>();
			if (img == null) return;
			// border token is ~8% alpha (hairline) — too faint for a 6px drag hit target.
			Color bar = t.controlBg;
			bar.a = Mathf.Max(bar.a, 0.85f);
			SpzUiThemeOps.ApplyBoundChromeGraphic(img, bar);
		}

		static void RestoreSplittersUnder(Transform root)
		{
			if (root == null) return;
			RestoreOneSplitter(root.Find(SplitLayersBrush));
			RestoreOneSplitter(root.Find(SplitBrushTool));
			RestoreOneSplitter(root.Find(SplitToolColor));
		}

		static void RestoreOneSplitter(Transform splitTf)
		{
			if (splitTf == null) return;
			SpzUiThemeOps.RestoreBoundChromeUnder(splitTf);
			var img = splitTf.GetComponent<Image>();
			if (img != null && !SpzUiThemeOps.ShouldRecolorBoundChrome)
				img.color = SplitterBarDefault;
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
