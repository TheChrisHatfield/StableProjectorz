using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace spz {

	/// <summary>
	/// Populates the Paint tab's Krita-style sections with paint UI.
	/// First tries to find existing instances (FindObjectOfType); if none exist,
	/// creates the managers and UI components at runtime so the sections have content.
	/// 
	/// IMPORTANT: The Paint panel is inactive most of the time (only active when user clicks Paint tab).
	/// Coroutines die on inactive GameObjects, so all waiting/retry logic lives in CommandRibbon_UI
	/// (which IS always active). This component only holds the synchronous CollectNow() method.
	/// OnEnable also calls CollectNow() so switching to the Paint tab retries if singletons loaded late.
	/// </summary>
	public class PaintTab_CollectPaintUI : MonoBehaviour
	{
		[SerializeField] PaintTab_KritaLayout_UI _layout;
		[Tooltip("Defaults to ENG Roboto-Regular SDF via OnValidate in Editor; assign for builds if needed.")]
		[SerializeField] TMP_FontAsset _paintTabUiFontAsset;

		static PaintTab_CollectPaintUI _paintTabTmpStyleSource;
		const float kPaintTabUiFontSize = 10f;
		/// <summary> Rich-text size for symmetry on/off subline (was 8pt; +3 for readability). </summary>
		const int kSymmetryOnOffSublineTmpSize = 11;
		static readonly System.Collections.Generic.List<System.Action> _brushSettingsHandlers = new System.Collections.Generic.List<System.Action>();
		/// <summary> Kept when tool options are built once so we can re-subscribe after OnDisable cleared handlers. </summary>
		static System.Action _cachedBrushOptsOnSettingsChanged;
		static System.Action _cachedSymmetryOnSettingsChanged;
		/// <summary>Delegates for smudge UI sync; cleared in <see cref="UnregisterSmudgeBrushOptsHandlers"/> (tab OnDisable or before re-bind).</summary>
		static System.Action _smudgeSliderSyncFromStoreHandler;
		/// <summary>Subscribed to <see cref="BrushRibbon_UI_Direction.OnDirectionToggleChanged"/>.</summary>
		static System.Action _smudgeOptsVisibilityOnDirChangedHandler;
		/// <summary>Subscribed to <see cref="WorkflowRibbon_UI._Act_OnModeChanged"/>.</summary>
		static System.Action<WorkflowRibbon_CurrMode> _smudgeOptsVisibilityOnWorkflowModeHandler;

		/// <summary>Delegates for strict-isolation flip UI; cleared in <see cref="UnregisterStrictIsolationBrushOptsHandlers"/>.</summary>
		static System.Action _strictIsolationSyncFromStoreHandler;
		static System.Action<WorkflowRibbon_CurrMode> _strictIsolationVisibilityOnWorkflowModeHandler;

		bool _collected;
		bool _toolchestCollected;

		public bool IsFullyCollected => _collected && _toolchestCollected;

		public void SetLayout(PaintTab_KritaLayout_UI layout) { _layout = layout; }

		/// <summary>For Layers section: returns (scroll content RectTransform, section root Transform). Panel goes in scroll content; Add button goes in section root so it stays visible. Handles both prefab (section root = LayersSection) and CreateSectionsIfMissing (LayersSection = ScrollContent).</summary>
		static void GetLayersScrollContentAndRoot(RectTransform layersSectionRef, out RectTransform scrollContent, out Transform sectionRoot)
		{
			scrollContent = null;
			sectionRoot = layersSectionRef != null ? layersSectionRef : null;
			if (layersSectionRef == null) return;
			// Prefab case: section ref is section root (e.g. 2_Layers) with child "Content" that has ScrollRect
			for (int i = 0; i < layersSectionRef.childCount; i++)
			{
				var ch = layersSectionRef.GetChild(i);
				if (ch.name == "Content")
				{
					var sr = ch.GetComponent<ScrollRect>();
					if (sr != null && sr.content != null)
					{
						scrollContent = sr.content;
						sectionRoot = layersSectionRef;
						return;
					}
					break;
				}
			}
			// CreateSectionsIfMissing case: section ref is ScrollContent; section root is Content.parent
			if (layersSectionRef.parent != null)
			{
				var content = layersSectionRef.parent;
				if (content.GetComponent<ScrollRect>() != null)
				{
					scrollContent = layersSectionRef as RectTransform;
					sectionRoot = content.parent;
					return;
				}
			}
			scrollContent = layersSectionRef;
			sectionRoot = layersSectionRef;
		}

		/// <summary>Returns the RectTransform that actually holds the scrollable content (ScrollContent). If BrushPresetsSection is the section root (prefab), finds Content -> ScrollRect.content; otherwise returns BrushPresetsSection (runtime-created section returns ScrollContent).</summary>
		static RectTransform GetBrushPresetsScrollContent(RectTransform brushPresetsSection)
		{
			if (brushPresetsSection == null) return null;
			for (int i = 0; i < brushPresetsSection.childCount; i++)
			{
				var child = brushPresetsSection.GetChild(i);
				if (child.name == "Content")
				{
					var scroll = child.GetComponent<ScrollRect>();
					if (scroll != null && scroll.content != null)
						return scroll.content;
					break;
				}
			}
			return brushPresetsSection;
		}

		/// <summary>Returns the Brush Presets section root (parent of Header + Content). Buttons stay here so they don't scroll.</summary>
		static Transform GetBrushPresetsSectionRoot(RectTransform scrollContent)
		{
			if (scrollContent == null) return null;
			var p = scrollContent.parent;
			if (p != null && p.name == "Content")
				p = p.parent;
			return p;
		}

		/// <summary>Finds the button row (BrushPresets_Buttons) inside scrollContent. Returns null if not found or already moved out.</summary>
		static Transform FindBrushPresetsButtonRow(Transform scrollContent)
		{
			if (scrollContent == null) return null;
			for (int i = 0; i < scrollContent.childCount; i++)
			{
				var c = scrollContent.GetChild(i);
				if (c.name == "BrushPresets_Buttons")
					return c;
			}
			return null;
		}

		/// <summary>Fallback bottom padding when picker has not run yet. Picker uses adaptive padding (thumbnail size + spacing + buffer) in RebuildGrid.</summary>
		const int kBrushPresetsScrollBottomPad = 14;
		// Picker top padding: use single source of truth so padding is never overwritten or out of sync (BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx).

		/// <summary>Ensures the brush presets scroll content has ContentSizeFitter (vertical PreferredSize) and VLG so the content height grows with the picker and ScrollRect can scroll.</summary>
		static void EnsureBrushPresetsScrollContentCanGrow(RectTransform scrollContent)
		{
			if (scrollContent == null) return;
			var csf = scrollContent.GetComponent<ContentSizeFitter>();
			if (csf == null) csf = scrollContent.gameObject.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			var vlg = scrollContent.GetComponent<VerticalLayoutGroup>();
			if (vlg == null)
			{
				vlg = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
			}
			vlg.spacing = BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx;
			vlg.padding = new RectOffset(2, 2, 2, 2 + kBrushPresetsScrollBottomPad);
			vlg.childForceExpandHeight = false;
			vlg.childControlHeight = false; // let picker drive its own height so ContentSizeFitter gets correct total and scroll works
		}

#if UNITY_EDITOR
		void OnValidate()
		{
			if (_paintTabUiFontAsset == null)
				_paintTabUiFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
					"Assets/_gm/Art/Fonts/ENG - Roboto-Regular SDF.asset");
		}
#endif

		TMP_FontAsset ResolvePaintTabUiFont()
			=> _paintTabUiFontAsset != null ? _paintTabUiFontAsset : TMP_Settings.defaultFontAsset;

		/// <summary>Roboto (or default TMP) + wrap + ellipsis so labels stay inside button rects; same base size as Bucket Fill / Depth Limit.</summary>
		static void ApplyPaintTabToolRowTmp(TextMeshProUGUI tmp, TextAlignmentOptions alignment, float fontSize = kPaintTabUiFontSize)
		{
			if (tmp == null) return;
			tmp.font = _paintTabTmpStyleSource != null ? _paintTabTmpStyleSource.ResolvePaintTabUiFont() : TMP_Settings.defaultFontAsset;
			tmp.fontSize = fontSize;
			tmp.enableWordWrapping = true;
			tmp.overflowMode = TextOverflowModes.Ellipsis;
			tmp.alignment = alignment;
			tmp.raycastTarget = false;
		}

		static void StylePaintTabTmp(TextMeshProUGUI tmp, string text, float fontSize = kPaintTabUiFontSize,
			TextAlignmentOptions alignment = TextAlignmentOptions.Left)
		{
			if (tmp == null) return;
			ApplyPaintTabToolRowTmp(tmp, alignment, fontSize);
			tmp.text = text;
		}

		/// <summary>Populate/repair Krita sections and re-run <see cref="CollectNow"/> so subscriptions (brush + smudge) attach when the tab becomes active.</summary>
		void OnEnable()
		{
			_paintTabTmpStyleSource = this;
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
			if (_layout == null) _layout = GetComponent<PaintTab_KritaLayout_UI>();
			if (_layout != null)
			{
				CollectNow();
				SyncBrushPanelModalBlockStateForToolSection(_layout.ToolOptionsSection);
				StartCoroutine(RefreshBrushPresetsLayoutWhenReady());
			}
		}

		/// <summary>Paint tab inactive: drop brush-size listeners and smudge store/visibility subscriptions (rebound on next CollectNow when UI exists).</summary>
		void OnDisable()
		{
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			if (_layout != null && TryGetToolOptionsRowAndExpando(_layout.ToolOptionsSection, out var row, out var expando))
				SetToolOptionsRowBehindBrushPanelBlocked(row, expando, false);
			UnregisterBrushSettingsHandlers();
			UnregisterSmudgeBrushOptsHandlers();
			UnregisterStrictIsolationBrushOptsHandlers();
			if (_paintTabTmpStyleSource == this)
				_paintTabTmpStyleSource = null;
		}

		/// <summary>
		/// Re-applies semantic tokens to Paint ownership roots after CollectNow / theme changes.
		/// Tool-on uses accent; add/success and delete/danger map to role tokens.
		/// </summary>
		void ApplyThemeTokens()
		{
			if (_layout == null) _layout = GetComponent<PaintTab_KritaLayout_UI>();
			if (_layout == null) return;
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				SpzUiThemeOps.RestoreBoundChromeUnder(transform);
				// Sections may be remapped outside the collector host (Krita dockers).
				RestoreOwnedSection(_layout.ToolOptionsSection);
				RestoreOwnedSection(_layout.BrushPresetsSection);
				RestoreOwnedSection(_layout.ColorPaletteSection);
				RestoreOwnedSection(_layout.LayersSection);
				RestoreOwnedSection(_layout.ToolchestRow);
				_layout.ApplyThemeTokens();
				PaintTab_LayersPanel_UI layersRestore = null;
				if (_layout.LayersSection != null)
					layersRestore = _layout.LayersSection.GetComponentInChildren<PaintTab_LayersPanel_UI>(true);
				if (layersRestore == null)
					layersRestore = GetComponentInChildren<PaintTab_LayersPanel_UI>(true);
				if (layersRestore != null)
					layersRestore.ApplyThemeTokens();
				return;
			}
			var t = SpzUiThemeOps.Active;
			_layout.ApplyThemeTokens();
			ThemeOwnedSection(_layout.ToolOptionsSection, t, preferFlatToolToggles: true);
			ThemeOwnedSection(_layout.BrushPresetsSection, t, preferFlatToolToggles: false);
			ThemeOwnedSection(_layout.ColorPaletteSection, t, preferFlatToolToggles: false);
			ThemeOwnedSection(_layout.LayersSection, t, preferFlatToolToggles: false);
			PaintTab_LayersPanel_UI layers = null;
			if (_layout.LayersSection != null)
				layers = _layout.LayersSection.GetComponentInChildren<PaintTab_LayersPanel_UI>(true);
			if (layers == null)
				layers = GetComponentInChildren<PaintTab_LayersPanel_UI>(true);
			if (layers != null)
				layers.ApplyThemeTokens();
		}

		static void RestoreOwnedSection(RectTransform section)
		{
			if (section != null)
				SpzUiThemeOps.RestoreBoundChromeUnder(section);
		}

		static void ThemeOwnedSection(RectTransform section, SpzUiThemeOps.ThemeTokens t, bool preferFlatToolToggles = false)
		{
			if (section == null) return;
			foreach (var slider in section.GetComponentsInChildren<Slider>(true))
			{
				if (slider == null) continue;
				var bg = slider.GetComponent<Image>();
				if (bg != null)
					SpzUiThemeOps.ApplyBoundChromeGraphic(bg, t.fieldBg);
				if (slider.fillRect != null)
				{
					var fill = slider.fillRect.GetComponent<Image>();
					if (fill != null)
						SpzUiThemeOps.ApplyBoundChromeGraphic(fill, t.accent);
				}
				if (slider.handleRect != null)
				{
					var handle = slider.handleRect.GetComponent<Image>();
					if (handle != null)
						SpzUiThemeOps.ApplyBoundChromeGraphic(handle, t.handle);
				}
			}
			foreach (var btn in section.GetComponentsInChildren<Button>(true))
			{
				if (btn == null) continue;
				// Content-bearing widgets: Image/RawImage IS the payload (brush alpha, palette swatch, transparent hit).
				if (IsContentBearingPaintButton(btn))
					continue;
				// Value Assist owns its chrome via ApplyContextMenuChrome — avoid double SolidSquare.
				if (btn.GetComponentInParent<PaintTab_ValueAssistPanel_UI>(true) != null)
					continue;
				// Layers panel owns Add/Collapse/Delete/Visibility — dual Collect SolidSquare races leave.
				if (btn.GetComponentInParent<PaintTab_LayersPanel_UI>(true) != null)
					continue;
				// Floating/embedded color picker owns Commit + shell — Collect must not double SolidSquare.
				if (btn.GetComponentInParent<ColorPalette_Panel_UI>(true) != null)
					continue;
				string n = btn.gameObject.name ?? "";
				Color normal = t.controlBg;
				if (IsPaintActionName(n, "Add", "Bucket", "+"))
					normal = t.success;
				else if (IsPaintActionName(n, "Delete", "Clear", "Remove", "−", "-"))
					normal = t.danger;
				// Tool-on accent must come from explicit tool-state refresh — never color heuristics.
				SpzUiThemeOps.ApplyBoundChromeSelectable(btn, normal, t.accent);
			}
			foreach (var toggle in section.GetComponentsInChildren<Toggle>(true))
			{
				if (toggle == null) continue;
				if (toggle.targetGraphic is RawImage) continue;
				if (toggle.GetComponentInParent<PaintTab_LayersPanel_UI>(true) != null)
					continue;
				if (toggle.GetComponentInParent<ColorPalette_Panel_UI>(true) != null)
					continue;
				Color fill = toggle.isOn
					? Color.Lerp(t.controlBg, t.accent, 0.14f)
					: t.controlBg;
				if (preferFlatToolToggles) {
					// Smudge / Strict Isolation / tool radios — bevel Checkmark is not a real ✓.
					SpzUiThemeOps.ThemeFlatToolToggle(toggle, fill, t.accent, t.textPrimary);
				} else if (toggle.graphic != null) {
					SpzUiThemeOps.ThemeCheckboxToggle(toggle, fill, t.accent, t.success);
				} else {
					SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, fill, t.accent);
				}
				EnsurePaintToggleChromeHook(toggle);
			}
			foreach (var tmp in section.GetComponentsInChildren<TextMeshProUGUI>(true))
			{
				if (tmp == null) continue;
				// Do not retint labels that sit on content thumbnails/swatches.
				if (tmp.GetComponentInParent<RawImage>() != null)
					continue;
				if (IsUnderNamedAncestor(tmp.transform, "Swatch", "Brush_"))
					continue;
				// Design base must be stable across applies — never derive from current fontSize/scale
				// (that cancels font_scale changes: size/s * s == size).
				float basePt = ResolvePaintLabelDesignBasePt(tmp);
				if (tmp.gameObject.name == "Header" || tmp.gameObject.name == "Placeholder") {
					SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textMuted, basePt);
					tmp.characterSpacing = 0f;
				} else if (tmp.GetComponentInParent<Button>(true) != null
				           || tmp.GetComponentInParent<Toggle>(true) != null) {
					var parentBtn = tmp.GetComponentInParent<Button>(true);
					// Content-bearing: Compact clears label raycasts; brush/swatch may rely on TMP hits when face is payload.
					if (parentBtn != null && IsContentBearingPaintButton(parentBtn))
						continue;
					string raw = tmp.text ?? "";
					// Tool Options radios like "Follow stroke" — Compact UpperCase+Truncate → FOLLOW ST□.
					bool useReadable = raw.IndexOf(' ') >= 0 || raw.Length >= 10;
					if (useReadable) {
						SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary, basePt);
						tmp.enableWordWrapping = false;
						tmp.overflowMode = TextOverflowModes.Ellipsis;
						tmp.fontStyle = FontStyles.Normal;
						tmp.characterSpacing = 0f;
						tmp.maxVisibleCharacters = int.MaxValue;
					} else {
						SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, basePt);
					}
				} else {
					SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary, basePt);
					tmp.characterSpacing = 0f;
				}
			}
			// Compact clears label raycasts — re-assert face-only hits on chrome buttons/toggles.
			foreach (var btn in section.GetComponentsInChildren<Button>(true)) {
				if (btn == null || IsContentBearingPaintButton(btn)) continue;
				if (btn.GetComponentInParent<PaintTab_ValueAssistPanel_UI>(true) != null) continue;
				if (btn.GetComponentInParent<PaintTab_LayersPanel_UI>(true) != null) continue;
				if (btn.GetComponentInParent<ColorPalette_Panel_UI>(true) != null) continue;
				SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
			}
			foreach (var toggle in section.GetComponentsInChildren<Toggle>(true)) {
				if (toggle == null || toggle.targetGraphic is RawImage) continue;
				if (toggle.GetComponentInParent<PaintTab_LayersPanel_UI>(true) != null) continue;
				if (toggle.GetComponentInParent<ColorPalette_Panel_UI>(true) != null) continue;
				SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);
			}
		}

		static UnityEngine.Events.UnityAction<bool> s_paintToggleChromeHook;

		/// <summary>Selection fill is BoundChrome face color — retint when radios flip without ThemeChanged.</summary>
		static void EnsurePaintToggleChromeHook(Toggle toggle)
		{
			if (toggle == null) return;
			if (s_paintToggleChromeHook == null) {
				s_paintToggleChromeHook = _ => {
					var all = UnityEngine.Object.FindObjectsByType<PaintTab_CollectPaintUI>(
						FindObjectsInactive.Include, FindObjectsSortMode.None);
					for (int i = 0; i < all.Length; i++) {
						if (all[i] != null)
							all[i].ApplyThemeTokens();
					}
				};
			}
			toggle.onValueChanged.RemoveListener(s_paintToggleChromeHook);
			toggle.onValueChanged.AddListener(s_paintToggleChromeHook);
		}

		const float kPaintLabelDesignBasePt = 12f;

		static float ResolvePaintLabelDesignBasePt(TextMeshProUGUI tmp)
		{
			if (tmp == null) return kPaintLabelDesignBasePt;
			var tag = tmp.gameObject.GetComponent<PaintTab_ThemeDesignFontPt>();
			if (tag == null) {
				tag = tmp.gameObject.AddComponent<PaintTab_ThemeDesignFontPt>();
				float scale = SpzUiThemeOps.Active.fontScale;
				float current = tmp.fontSize > 0.05f ? tmp.fontSize : kPaintLabelDesignBasePt;
				// First sight: undo active scale if TMP was already theme-sized; else treat as design size.
				tag.designPt = (scale > 0.05f && Mathf.Abs(scale - 1f) > 0.001f)
					? current / scale
					: current;
				if (tag.designPt < 0.05f)
					tag.designPt = kPaintLabelDesignBasePt;
			}
			return tag.designPt;
		}

		/// <summary>
		/// Brush thumbnails, palette swatches, and transparent hit targets must keep Image.color as content.
		/// </summary>
		static bool IsContentBearingPaintButton(Button btn)
		{
			if (btn == null) return true;
			string n = btn.gameObject.name ?? "";
			if (n.StartsWith("Brush_", System.StringComparison.OrdinalIgnoreCase))
				return true;
			if (string.Equals(n, "HitArea", System.StringComparison.OrdinalIgnoreCase))
				return true;
			if (n.StartsWith("Swatch", System.StringComparison.OrdinalIgnoreCase))
				return true;
			// Layer row: soft rename plate + visibility chip own their colors (SolidSquare blanks/covers labels).
			if (string.Equals(n, "DisplayBlock", System.StringComparison.OrdinalIgnoreCase)
			    || string.Equals(n, "Visibility", System.StringComparison.OrdinalIgnoreCase)
			    || string.Equals(n, "Grip", System.StringComparison.OrdinalIgnoreCase))
				return true;
			if (btn.GetComponentInChildren<RawImage>(true) != null)
				return true;
			// Soft / transparent hit-only graphics are scaffolding, not chrome tokens.
			// DisplayBlock ships ~0.12a — threshold must be above that (was 0.05 → opaque crush).
			var g = btn.targetGraphic;
			if (g != null && g.color.a < 0.15f)
				return true;
			return false;
		}

		static bool IsUnderNamedAncestor(Transform t, params string[] prefixes)
		{
			if (t == null || prefixes == null) return false;
			for (Transform p = t; p != null; p = p.parent)
			{
				string n = p.name ?? "";
				for (int i = 0; i < prefixes.Length; i++)
				{
					string pref = prefixes[i];
					if (string.IsNullOrEmpty(pref)) continue;
					if (n.StartsWith(pref, System.StringComparison.OrdinalIgnoreCase))
						return true;
				}
			}
			return false;
		}

		static bool IsPaintActionName(string name, params string[] tokens)
		{
			if (string.IsNullOrEmpty(name) || tokens == null) return false;
			for (int i = 0; i < tokens.Length; i++)
			{
				string tok = tokens[i];
				if (string.IsNullOrEmpty(tok)) continue;
				// Boundary-exact: whole name, or token as a distinct camel/underscore/space segment.
				if (string.Equals(name, tok, System.StringComparison.OrdinalIgnoreCase))
					return true;
				if (name.StartsWith(tok, System.StringComparison.OrdinalIgnoreCase)
				    && name.Length > tok.Length
				    && !char.IsLetterOrDigit(name[tok.Length]))
					return true;
				for (int c = 0; c + tok.Length <= name.Length; c++)
				{
					if (string.Compare(name, c, tok, 0, tok.Length, System.StringComparison.OrdinalIgnoreCase) != 0)
						continue;
					bool leftOk = c == 0 || !char.IsLetterOrDigit(name[c - 1]);
					bool rightOk = c + tok.Length >= name.Length || !char.IsLetterOrDigit(name[c + tok.Length]);
					// Prefer PascalCase boundary: "AddLayer" matches "Add"; "Padding" must not match "Add".
					if (leftOk && (rightOk || (c + tok.Length < name.Length && char.IsUpper(name[c + tok.Length]))))
						return true;
				}
			}
			return false;
		}

		static void RegisterBrushSettingsHandler(System.Action handler)
		{
			if (handler == null) return;
			BrushRibbon_UI_Size.OnBrushSettingsChanged += handler;
			_brushSettingsHandlers.Add(handler);
		}

		static void UnregisterBrushSettingsHandlers()
		{
			for (int i = 0; i < _brushSettingsHandlers.Count; i++)
				BrushRibbon_UI_Size.OnBrushSettingsChanged -= _brushSettingsHandlers[i];
			_brushSettingsHandlers.Clear();
		}

		static bool HasRuntimeToolOptionsRow(Transform toolOptionsSection)
		{
			if (toolOptionsSection == null) return false;
			for (int i = 0; i < toolOptionsSection.childCount; i++)
			{
				if (toolOptionsSection.GetChild(i) != null && toolOptionsSection.GetChild(i).name == "ToolOptionsRow")
					return true;
			}
			return false;
		}

		/// <summary> OnDisable unsubscribes all; when the paint tab re-enables, CreateToolOptionsRuntime is skipped if UI exists — re-wire cached handlers. </summary>
		static void ResubscribeBrushSettingsHandlersIfToolOptionsExist()
		{
			if (_cachedBrushOptsOnSettingsChanged == null && _cachedSymmetryOnSettingsChanged == null)
				return;
			UnregisterBrushSettingsHandlers();
			if (_cachedBrushOptsOnSettingsChanged != null)
				RegisterBrushSettingsHandler(_cachedBrushOptsOnSettingsChanged);
			if (_cachedSymmetryOnSettingsChanged != null)
				RegisterBrushSettingsHandler(_cachedSymmetryOnSettingsChanged);
		}

		/// <summary>Inpaint color/no-color and smudge tool — single predicate for smudge block visibility and scroll-into-view.</summary>
		static bool ComputeSmudgeBrushOptsShouldBeVisible()
		{
			var wf = WorkflowRibbon_UI.instance;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (wf == null || sd == null) return false;
			var m = wf.currentMode();
			bool inpaint = m == WorkflowRibbon_CurrMode.Inpaint_Color || m == WorkflowRibbon_CurrMode.Inpaint_NoColor;
			return inpaint && sd.isSmudge;
		}

		/// <summary>Shows the smudge block only for inpaint workflows (color / no-color) while the brush tool is smudge.</summary>
		static void SyncSmudgeBrushOptsVisibilityForRoot(GameObject smudgeOptsRoot)
		{
			if (smudgeOptsRoot == null) return;
			smudgeOptsRoot.SetActive(ComputeSmudgeBrushOptsShouldBeVisible());
		}

		/// <summary>Strict isolation / Klein local composite flip: show for every Inpaint workflow that captures a bake mask (Color / NoColor / TotalObject / WhereEmpty).</summary>
		static bool ComputeStrictIsolationBrushOptsShouldBeVisible()
		{
			var wf = WorkflowRibbon_UI.instance;
			if (wf == null) return false;
			switch (wf.currentMode())
			{
				case WorkflowRibbon_CurrMode.Inpaint_Color:
				case WorkflowRibbon_CurrMode.Inpaint_NoColor:
				case WorkflowRibbon_CurrMode.TotalObject:
				case WorkflowRibbon_CurrMode.WhereEmpty:
					return true;
				default:
					return false;
			}
		}

		static void SyncStrictIsolationBrushOptsVisibilityForRoot(GameObject strictIsoRoot)
		{
			if (strictIsoRoot == null) return;
			strictIsoRoot.SetActive(ComputeStrictIsolationBrushOptsShouldBeVisible());
		}

		static void UnregisterStrictIsolationBrushOptsHandlers()
		{
			if (_strictIsolationSyncFromStoreHandler != null)
			{
				PaintTab_StrictIsolationBrushOptions.Changed -= _strictIsolationSyncFromStoreHandler;
				_strictIsolationSyncFromStoreHandler = null;
			}
			if (_strictIsolationVisibilityOnWorkflowModeHandler != null)
			{
				WorkflowRibbon_UI._Act_OnModeChanged -= _strictIsolationVisibilityOnWorkflowModeHandler;
				_strictIsolationVisibilityOnWorkflowModeHandler = null;
			}
		}

		static bool TryFindStrictIsolationBrushOptsUi(RectTransform toolOptionsSection, out Toggle flipToggle, out GameObject strictIsoRoot)
		{
			flipToggle = null;
			strictIsoRoot = null;
			if (toolOptionsSection == null) return false;
			Transform panel = null;
			for (int i = 0; i < toolOptionsSection.childCount; i++)
			{
				var ch = toolOptionsSection.GetChild(i);
				if (ch != null && ch.name == "BrushOptsPanel")
				{
					panel = ch;
					break;
				}
			}
			if (panel == null) return false;
			var block = panel.Find("StrictIsolationBrushOptsBlock");
			if (block == null) return false;
			var row = block.Find("StrictIsolationFlipRow");
			if (row != null)
				flipToggle = row.GetComponentInChildren<Toggle>(true);
			strictIsoRoot = block.gameObject;
			return flipToggle != null && strictIsoRoot != null;
		}

		/// <summary>
		/// Tool-row / checkbox face: BoundChrome tokens when active, else authored SPZ on/off colors.
		/// </summary>
		static Color PaintToolFaceColor(bool on, Color authoredOn, Color authoredOff)
		{
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome)
				return on ? authoredOn : authoredOff;
			var t = SpzUiThemeOps.Active;
			return on
				? Color.Lerp(t.controlBg, t.accent, 0.14f)
				: t.controlBg;
		}

		static void RegisterStrictIsolationBrushOptsHandlersForUi(Toggle flipToggle, GameObject strictIsoRoot)
		{
			if (flipToggle == null || strictIsoRoot == null) return;
			UnregisterStrictIsolationBrushOptsHandlers();

			Color authoredOff = new Color(0.34f, 0.36f, 0.4f, 1f);
			Color authoredOn = new Color(0.22f, 0.45f, 0.55f, 1f);

			void SyncFlipToggleFromStore()
			{
				if (flipToggle == null) return;
				bool on = PaintTab_StrictIsolationBrushOptions.FlipInvertIsolationMask;
				flipToggle.SetIsOnWithoutNotify(on);
				if (flipToggle.targetGraphic is Image img)
					img.color = PaintToolFaceColor(on, authoredOn, authoredOff);
			}

			void SyncVisibility() => SyncStrictIsolationBrushOptsVisibilityForRoot(strictIsoRoot);

			_strictIsolationSyncFromStoreHandler = SyncFlipToggleFromStore;
			PaintTab_StrictIsolationBrushOptions.Changed += _strictIsolationSyncFromStoreHandler;
			_strictIsolationVisibilityOnWorkflowModeHandler = (_) => SyncVisibility();
			WorkflowRibbon_UI._Act_OnModeChanged += _strictIsolationVisibilityOnWorkflowModeHandler;
			SyncFlipToggleFromStore();
			SyncVisibility();
		}

		static void ResubscribeStrictIsolationBrushOptsHandlersIfUiExists(RectTransform toolOptionsSection)
		{
			if (!TryFindStrictIsolationBrushOptsUi(toolOptionsSection, out var tgl, out var root)) return;
			RegisterStrictIsolationBrushOptsHandlersForUi(tgl, root);
		}

		/// <summary>Removes smudge listeners from <see cref="PaintTab_SmudgeBrushOptions.Changed"/>, direction toggles, and workflow mode.</summary>
		static void UnregisterSmudgeBrushOptsHandlers()
		{
			if (_smudgeSliderSyncFromStoreHandler != null)
			{
				PaintTab_SmudgeBrushOptions.Changed -= _smudgeSliderSyncFromStoreHandler;
				_smudgeSliderSyncFromStoreHandler = null;
			}
			if (_smudgeOptsVisibilityOnDirChangedHandler != null)
			{
				BrushRibbon_UI_Direction.OnDirectionToggleChanged -= _smudgeOptsVisibilityOnDirChangedHandler;
				_smudgeOptsVisibilityOnDirChangedHandler = null;
			}
			if (_smudgeOptsVisibilityOnWorkflowModeHandler != null)
			{
				WorkflowRibbon_UI._Act_OnModeChanged -= _smudgeOptsVisibilityOnWorkflowModeHandler;
				_smudgeOptsVisibilityOnWorkflowModeHandler = null;
			}
		}

		/// <summary>Locates runtime smudge UI under the Tool Options section: <c>BrushOptsPanel/SmudgeBrushOptsBlock</c> and row sliders.</summary>
		static bool TryFindSmudgeBrushOptsUi(RectTransform toolOptionsSection, out Slider strengthSlider, out Slider angleSlider,
			out Slider colorMixSlider, out Slider neighborRadiusSlider, out Toggle meshUnderToggle, out GameObject smudgeOptsRoot)
		{
			strengthSlider = null;
			angleSlider = null;
			colorMixSlider = null;
			neighborRadiusSlider = null;
			meshUnderToggle = null;
			smudgeOptsRoot = null;
			if (toolOptionsSection == null) return false;
			Transform panel = null;
			for (int i = 0; i < toolOptionsSection.childCount; i++)
			{
				var ch = toolOptionsSection.GetChild(i);
				if (ch != null && ch.name == "BrushOptsPanel")
				{
					panel = ch;
					break;
				}
			}
			if (panel == null) return false;
			var block = panel.Find("SmudgeBrushOptsBlock");
			if (block == null) return false;
			var strRow = block.Find("SmudgeStrengthRow");
			var angRow = block.Find("SmudgeAngleRow");
			if (strRow == null || angRow == null) return false;
			strengthSlider = strRow.GetComponentInChildren<Slider>(true);
			angleSlider = angRow.GetComponentInChildren<Slider>(true);
			if (strengthSlider == null || angleSlider == null) return false;
			var mixRow = block.Find("SmudgeColorMixRow");
			if (mixRow != null)
				colorMixSlider = mixRow.GetComponentInChildren<Slider>(true);
			var radRow = block.Find("SmudgeNeighborRadiusRow");
			if (radRow != null)
				neighborRadiusSlider = radRow.GetComponentInChildren<Slider>(true);
			var meshRow = block.Find("SmudgeMeshUnderRow");
			if (meshRow != null)
				meshUnderToggle = meshRow.GetComponentInChildren<Toggle>(true);
			smudgeOptsRoot = block.gameObject;
			return true;
		}

		/// <summary>Unregisters any prior smudge handlers, then binds slider sync and visibility for this UI instance.</summary>
		static void RegisterSmudgeBrushOptsHandlersForUi(Slider smudgeStrSlider, Slider smudgeAngSlider, Slider smudgeColorMixSlider,
			Slider smudgeNeighborRadiusSlider, Toggle smudgeMeshUnderToggle, GameObject smudgeOptsRoot)
		{
			if (smudgeStrSlider == null || smudgeAngSlider == null || smudgeOptsRoot == null) return;
			UnregisterSmudgeBrushOptsHandlers();

			Color meshUvUnderOff = new Color(0.34f, 0.36f, 0.4f, 1f);
			Color meshUvUnderOn = new Color(0.22f, 0.45f, 0.55f, 1f);

			void SyncSmudgeSlidersFromStore()
			{
				if (smudgeStrSlider != null)
					smudgeStrSlider.SetValueWithoutNotify(PaintTab_SmudgeBrushOptions.Strength01);
				if (smudgeAngSlider != null)
					smudgeAngSlider.SetValueWithoutNotify(PaintTab_SmudgeBrushOptions.AngleDeg);
				if (smudgeColorMixSlider != null)
					smudgeColorMixSlider.SetValueWithoutNotify(PaintTab_SmudgeBrushOptions.ColorMixSimilarity01);
				if (smudgeNeighborRadiusSlider != null)
					smudgeNeighborRadiusSlider.SetValueWithoutNotify(PaintTab_SmudgeBrushOptions.NeighborGridRadius);
				if (smudgeMeshUnderToggle != null)
				{
					bool on = PaintTab_SmudgeBrushOptions.IncludeUvMeshInLayerSmudge;
					smudgeMeshUnderToggle.SetIsOnWithoutNotify(on);
					if (smudgeMeshUnderToggle.targetGraphic is Image img)
						img.color = PaintToolFaceColor(on, meshUvUnderOn, meshUvUnderOff);
				}
			}

			void SyncSmudgeBrushOptsVisibility() => SyncSmudgeBrushOptsVisibilityForRoot(smudgeOptsRoot);

			_smudgeSliderSyncFromStoreHandler = SyncSmudgeSlidersFromStore;
			PaintTab_SmudgeBrushOptions.Changed += _smudgeSliderSyncFromStoreHandler;
			_smudgeOptsVisibilityOnDirChangedHandler = SyncSmudgeBrushOptsVisibility;
			_smudgeOptsVisibilityOnWorkflowModeHandler = (_) => SyncSmudgeBrushOptsVisibility();
			BrushRibbon_UI_Direction.OnDirectionToggleChanged += _smudgeOptsVisibilityOnDirChangedHandler;
			WorkflowRibbon_UI._Act_OnModeChanged += _smudgeOptsVisibilityOnWorkflowModeHandler;
			// Store can change while the tab was inactive (API / defaults); widgets are not notified until Changed fires.
			SyncSmudgeSlidersFromStore();
			SyncSmudgeBrushOptsVisibility();
		}

		/// <summary>After tab re-enable when ToolOptionsRow already exists: find smudge controls and call <see cref="RegisterSmudgeBrushOptsHandlersForUi"/>.</summary>
		static void ResubscribeSmudgeBrushOptsHandlersIfUiExists(RectTransform toolOptionsSection)
		{
			if (!TryFindSmudgeBrushOptsUi(toolOptionsSection, out var s, out var a, out var mix, out var rad, out var meshTgl, out var root)) return;
			RegisterSmudgeBrushOptsHandlersForUi(s, a, mix, rad, meshTgl, root);
		}

		static bool SmudgeBrushOptsShouldShowForScroll() => ComputeSmudgeBrushOptsShouldBeVisible();

		/// <summary>Adjusts vertical scroll so <paramref name="target"/> is inside the viewport (iterative; world-space corners).</summary>
		static void ScrollRectVerticalClampChildVisible(ScrollRect scrollRect, RectTransform target)
		{
			if (scrollRect == null || target == null || !target.gameObject.activeInHierarchy) return;
			RectTransform viewport = scrollRect.viewport;
			RectTransform content = scrollRect.content;
			if (viewport == null || content == null) return;

			const float padWorld = 6f;
			for (int iter = 0; iter < 8; iter++)
			{
				Canvas.ForceUpdateCanvases();
				Vector3[] vCorners = new Vector3[4];
				viewport.GetWorldCorners(vCorners);
				float viewTop = Mathf.Max(vCorners[0].y, vCorners[1].y, vCorners[2].y, vCorners[3].y);
				float viewBottom = Mathf.Min(vCorners[0].y, vCorners[1].y, vCorners[2].y, vCorners[3].y);

				Vector3[] tCorners = new Vector3[4];
				target.GetWorldCorners(tCorners);
				float tTop = Mathf.Max(tCorners[0].y, tCorners[1].y, tCorners[2].y, tCorners[3].y);
				float tBottom = Mathf.Min(tCorners[0].y, tCorners[1].y, tCorners[2].y, tCorners[3].y);

				float adjust = 0f;
				if (tBottom < viewBottom + padWorld)
					adjust = (tBottom - viewBottom) - padWorld;
				else if (tTop > viewTop - padWorld)
					adjust = (tTop - viewTop) + padWorld;

				if (Mathf.Abs(adjust) < 0.5f)
					break;

				float scrollRange = content.rect.height - viewport.rect.height;
				if (scrollRange <= 1f)
					break;

				// Map world-space Y delta to normalized scroll: scrollRange is local; adjust is world (Canvas scale safe).
				float viewWorldH = Mathf.Max(1e-4f, viewTop - viewBottom);
				float viewLocalH = Mathf.Max(1e-4f, viewport.rect.height);
				float adjustLocal = adjust * (viewLocalH / viewWorldH);
				float normDelta = adjustLocal / scrollRange;
				float prev = scrollRect.verticalNormalizedPosition;
				float newNorm = Mathf.Clamp01(prev + normDelta);
				if (Mathf.Approximately(newNorm, prev))
					break;
				scrollRect.verticalNormalizedPosition = newNorm;
			}
		}

		System.Collections.IEnumerator RefreshBrushPresetsLayoutWhenReady()
		{
			yield return null; // wait one frame so panel is active and has valid rect
			var scrollContent = GetBrushPresetsScrollContent(_layout != null ? _layout.BrushPresetsSection : null);
			if (scrollContent == null) yield break;
			var picker = FindObjectOfType<BrushRibbon_UI_AlphaPicker>(true);
			if (picker != null)
			{
				picker.RebuildGrid();
				LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
				Canvas.ForceUpdateCanvases();
			}
		}

		/// <summary>After Brush options expand, layout + ContentSizeFitter need a frame before scroll metrics are final.</summary>
		System.Collections.IEnumerator CoScrollSmudgeBlockIntoViewAfterOpen(ScrollRect sr, RectTransform smudgeBlockRt, RectTransform toolSectionForRebuild)
		{
			yield return null;
			if (toolSectionForRebuild != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(toolSectionForRebuild);
			Canvas.ForceUpdateCanvases();
			if (sr != null && smudgeBlockRt != null && smudgeBlockRt.gameObject.activeInHierarchy)
				ScrollRectVerticalClampChildVisible(sr, smudgeBlockRt);
		}

		/// <summary>Synchronous populate. Safe to call multiple times from any context.</summary>
		public void CollectNow()
		{
			if (_layout == null) _layout = GetComponent<PaintTab_KritaLayout_UI>();
			if (_layout == null) return;
			// Any missing section must recreate scaffolding — BrushPresets-only gate left Tool Options
			// (and Value Assist) unmounted when other refs were assigned (PAINT_TAB_SCAFFOLDING_AUDIT).
			if (_layout.ToolchestRow == null || _layout.LayersSection == null || _layout.BrushPresetsSection == null
			    || _layout.ToolOptionsSection == null || _layout.ColorPaletteSection == null)
				_layout.SetCreateSectionsIfMissing(true);
			bool did = false;
			bool toolchestDid = false;

			// --- Toolchest row: workflow ribbons ---
			if (WorkflowRibbon_UI.instance != null && _layout.ToolchestRow != null)
			{
				var tr = (RectTransform)WorkflowRibbon_UI.instance.transform;
				tr.SetParent(_layout.ToolchestRow, false);
				tr.anchorMin = new Vector2(0, 0.5f);
				tr.anchorMax = new Vector2(0, 0.5f);
				tr.pivot = new Vector2(0, 0.5f);
				EnsureLayoutElement(tr, flexibleWidth: 0f);
				did = true;
				toolchestDid = true;
			}
			if (SD_WorkflowOptionsRibbon_UI.instance != null && _layout.ToolchestRow != null)
			{
				var tr = (RectTransform)SD_WorkflowOptionsRibbon_UI.instance.transform;
				tr.SetParent(_layout.ToolchestRow, false);
				tr.anchorMin = new Vector2(0, 0.5f);
				tr.anchorMax = new Vector2(0, 0.5f);
				tr.pivot = new Vector2(0, 0.5f);
				EnsureLayoutElement(tr, flexibleWidth: 1f);
				did = true;
				toolchestDid = true;
			}

			// --- Layers section ---
			if (_layout.LayersSection != null)
			{
				GetLayersScrollContentAndRoot(_layout.LayersSection, out var layersScrollContent, out var layersSectionRoot);
				// Ensure stack exists so panel can wire to it (whether panel is found or created)
				if (PaintLayerStack_MGR.instance == null)
				{
					var mgrGo = new GameObject("PaintLayerStack_MGR_Runtime");
					mgrGo.AddComponent<PaintLayerStack_MGR>();
				}
				var layersPanel = layersScrollContent != null
					? layersScrollContent.GetComponentInChildren<PaintTab_LayersPanel_UI>(true)
					: null;
				if (layersPanel == null && layersSectionRoot != null)
					layersPanel = layersSectionRoot.GetComponentInChildren<PaintTab_LayersPanel_UI>(true);
				if (layersPanel == null)
					layersPanel = GetComponentInChildren<PaintTab_LayersPanel_UI>(true);
				if (layersPanel == null && layersScrollContent != null)
					layersPanel = CreateLayersPanelRuntime(layersScrollContent, layersSectionRoot);
				// Always wire panel to stack and Add Layer button so all buttons work (found or created panel)
				if (layersPanel != null)
				{
					layersPanel.SetLayerStack(PaintLayerStack_MGR.instance);
					Button addBtn = null;
					// Search for existing LayerButtonsRow in scroll content and section root
					Transform btnRowSearch = FindLayerButtonsRow(layersScrollContent);
					if (btnRowSearch == null && layersSectionRoot != null)
						btnRowSearch = FindLayerButtonsRow(layersSectionRoot);
					if (btnRowSearch != null)
					{
						addBtn = btnRowSearch.Find("AddLayerBtn")?.GetComponent<Button>();
						if (addBtn == null) addBtn = btnRowSearch.GetComponentInChildren<Button>(true);
					}
					// If no Add Layer button, create it (only creates if row doesn't already exist)
					if (addBtn == null && layersScrollContent != null)
						addBtn = EnsureLayersAddButtonRow(layersScrollContent);
					if (addBtn != null)
						layersPanel.SetAddLayerButton(addBtn);
				}
				if (layersPanel != null && layersScrollContent != null && layersPanel.transform.parent != layersScrollContent)
				{
					var tr = (RectTransform)layersPanel.transform;
					tr.SetParent(layersScrollContent, false);
					tr.SetAsFirstSibling();
					tr.anchorMin = new Vector2(0, 1);
					tr.anchorMax = Vector2.one;
					tr.pivot = new Vector2(0.5f, 1);
					tr.offsetMin = Vector2.zero;
					tr.offsetMax = Vector2.zero;
				}
				// Move LayerButtonsRow outside scroll content into section root at the bottom so it never scrolls away
				if (layersSectionRoot != null && layersScrollContent != null && layersSectionRoot != (Transform)layersScrollContent)
				{
					Transform btnRow = FindLayerButtonsRow(layersScrollContent);
					if (btnRow != null)
					{
						btnRow.SetParent(layersSectionRoot, false);
						btnRow.SetAsLastSibling(); // bottom of section: below the scroll area
					}
				}
				if (layersPanel != null)
				{
					did = true;
					if (layersScrollContent != null)
						LayoutRebuilder.ForceRebuildLayoutImmediate(layersScrollContent);
					if (layersSectionRoot != null && layersSectionRoot is RectTransform sectionRect)
						LayoutRebuilder.ForceRebuildLayoutImmediate(sectionRect);
				}
			}

			// --- Brush Presets section ---
			var scrollContent = GetBrushPresetsScrollContent(_layout.BrushPresetsSection);
			if (scrollContent != null)
			{
				// Ensure scroll content can grow vertically so ScrollRect actually scrolls when many brushes are added
				// (matches Layers section: ContentSizeFitter.PreferredSize + VLG with childForceExpandHeight = false)
				EnsureBrushPresetsScrollContentCanGrow(scrollContent);
				var alphaPicker = FindObjectOfType<BrushRibbon_UI_AlphaPicker>(true);
				if (alphaPicker == null)
					alphaPicker = CreateBrushPresetsRuntime(scrollContent);
				// Reparent picker into the actual scroll content so dropdown aligns with Load ABR/PNG (critical when BrushPresetsSection is section root from prefab)
				if (alphaPicker != null && !alphaPicker.transform.IsChildOf(scrollContent))
				{
					var tr = (RectTransform)alphaPicker.transform;
					tr.SetParent(scrollContent, false);
					tr.anchorMin = Vector2.zero;
					tr.anchorMax = Vector2.one;
					tr.offsetMin = Vector2.zero;
					tr.offsetMax = Vector2.zero;
				}
				// Keep button row static: move it out of scroll content into section root so only the picker scrolls
				var sectionRoot = GetBrushPresetsSectionRoot(scrollContent);
				var btnRow = FindBrushPresetsButtonRow(scrollContent);
				if (sectionRoot != null && btnRow != null && btnRow.parent == scrollContent)
				{
					btnRow.SetParent(sectionRoot, false);
					btnRow.SetSiblingIndex(1); // after Header (0), before Content (2)
					var scrollVlg = scrollContent.GetComponent<VerticalLayoutGroup>();
					if (scrollVlg != null)
						scrollVlg.spacing = 0; // only picker in scroll content now
					var sectionVlg = sectionRoot.GetComponent<VerticalLayoutGroup>();
					if (sectionVlg != null)
						sectionVlg.padding = new RectOffset(2, 0, 0, 2); // align button row left with picker (same as scroll content edge)
				}
				if (alphaPicker != null)
				{
					// --- Left-alignment fix ---
					// ScrollContent VLG left=3 is the ONLY left offset.
					// Button row HLG left=0, picker VLG left=0, section VLG left=0, header HLG left=0.
					// So both "Load ABR/PNG" and chevron/folder/name start at exactly 3px.
					const int kEdgePad = 2; // compact; aligns Load ABR/PNG and section headers
					var pickerParent = alphaPicker.transform.parent;
					// Scroll content: keep stretch anchors so it fills the viewport width
					if (pickerParent != null)
					{
						var parentRect = pickerParent as RectTransform;
						if (parentRect != null)
						{
							parentRect.anchorMin = new Vector2(0, 1);
							parentRect.anchorMax = new Vector2(1, 1); // stretch full width
							parentRect.pivot = new Vector2(0, 1);
							// Do NOT set sizeDelta on scroll content — ContentSizeFitter must control height so scrolling works when many brushes are added
							var parentVlg = pickerParent.GetComponent<VerticalLayoutGroup>();
							if (parentVlg != null)
							{
								parentVlg.padding = new RectOffset(kEdgePad, kEdgePad, kEdgePad, kEdgePad + kBrushPresetsScrollBottomPad);
								parentVlg.spacing = BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx; // gap between button row and dropdown row
								parentVlg.childAlignment = TextAnchor.UpperLeft;
							}
						}
					}
					if (_layout.BrushPresetsSection != null && _layout.BrushPresetsSection != scrollContent)
					{
						var sectionRootForLayout = _layout.BrushPresetsSection;
						var sectionVlg = sectionRootForLayout.GetComponent<VerticalLayoutGroup>();
						if (sectionVlg != null)
						{
							bool hasStaticButtons = false;
							for (int j = 0; j < sectionRootForLayout.childCount; j++)
								if (sectionRootForLayout.GetChild(j).name == "BrushPresets_Buttons") { hasStaticButtons = true; break; }
							sectionVlg.padding = hasStaticButtons ? new RectOffset(2, 0, 0, 2) : new RectOffset(0, 0, 0, 0);
							sectionVlg.childAlignment = TextAnchor.UpperLeft;
						}
						_layout.BrushPresetsSection.pivot = new Vector2(0, 1);
					}
					// Picker VLG: left=0; top = spacing above dropdown arrow (single source of truth in AlphaPicker)
					var pickerVlg = alphaPicker.GetComponent<VerticalLayoutGroup>();
					if (pickerVlg != null)
						pickerVlg.padding = new RectOffset(0, 0, BrushRibbon_UI_AlphaPicker.PickerTopSpacingPx, 0);
					var pickerRect = alphaPicker.transform as RectTransform;
					if (pickerRect != null)
					{
						pickerRect.anchorMin = new Vector2(0, 0);
						pickerRect.anchorMax = new Vector2(1, 1);
						pickerRect.pivot = new Vector2(0, 1);
						pickerRect.offsetMin = Vector2.zero;
						pickerRect.offsetMax = Vector2.zero;
					}
					// Button row: left=0 so Load ABR/PNG starts at same position as chevron
					for (int i = 0; i < scrollContent.childCount; i++)
					{
						var child = scrollContent.GetChild(i);
						var hlg = child.GetComponent<HorizontalLayoutGroup>();
						if (hlg != null)
						{
							hlg.padding = new RectOffset(0, 0, 0, 0);
							hlg.childAlignment = TextAnchor.MiddleLeft;
							break;
						}
					}
					alphaPicker.RebuildGrid();
					// Re-apply picker VLG after RebuildGrid (gap is scroll spacing, not picker padding)
					pickerVlg = alphaPicker.GetComponent<VerticalLayoutGroup>();
					if (pickerVlg != null)
					{
						pickerVlg.padding = new RectOffset(0, 0, 0, 0);
						if (pickerRect != null)
							LayoutRebuilder.ForceRebuildLayoutImmediate(pickerRect);
					}
					if (pickerParent != null)
					{
						var parentRect = pickerParent as RectTransform;
						if (parentRect != null)
							LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
					}
					LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
					did = true;
				}
			}

			// --- Tool Options section ---
			if (_layout.ToolOptionsSection != null && _layout.ToolOptionsSection.childCount <= 1)
			{
				CreateToolOptionsRuntime(_layout.ToolOptionsSection);
				did = true;
			}
			else if (_layout.ToolOptionsSection != null && HasRuntimeToolOptionsRow(_layout.ToolOptionsSection))
			{
				// Runtime row already present (e.g. tab re-opened): re-wire brush radios + smudge store/visibility after OnDisable cleared subscriptions.
				ResubscribeBrushSettingsHandlersIfToolOptionsExist();
				ResubscribeSmudgeBrushOptsHandlersIfUiExists(_layout.ToolOptionsSection);
				ResubscribeStrictIsolationBrushOptsHandlersIfUiExists(_layout.ToolOptionsSection);
			}
			// Value Assist proposal review (Spec R3) — sibling under Tool Options, not inside the grid row.
			if (_layout.ToolOptionsSection != null && PaintTab_ValueAssistPanel_UI.EnsureUnder(_layout.ToolOptionsSection) != null)
				did = true;

			// --- Color / Palette section ---
			if (_layout.ColorPaletteSection != null)
			{
				EnsurePaletteLoadButtonExists(_layout.ColorPaletteSection);
				var swatches = FindObjectOfType<PaletteSwatches_UI>(true);
				if (swatches == null)
					swatches = CreatePaletteSwatchesRuntime(_layout.ColorPaletteSection);
				if (swatches != null && swatches.transform.parent != _layout.ColorPaletteSection)
				{
					var tr = (RectTransform)swatches.transform;
					tr.SetParent(_layout.ColorPaletteSection, false);
					tr.anchorMin = Vector2.zero;
					tr.anchorMax = new Vector2(1, 0);
					tr.pivot = new Vector2(0.5f, 0);
					tr.offsetMin = Vector2.zero;
					tr.offsetMax = new Vector2(0, 120);
				}
				if (swatches != null) did = true;
			}

			if (did) _collected = true;
			if (toolchestDid) _toolchestCollected = true;
			if (toolchestDid) {
				RibbonViewportFullViewOnScreen_Toggle_UI.NotifyAllAttachRequested();
			}

			// Connectivity: sections may exist without splitters (prefab / prior Collect). Always ensure.
			_layout.EnsureSectionSplitters();

			var root = _layout.transform as RectTransform;
			if (root != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(root);
			if (_layout.ToolchestRow != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(_layout.ToolchestRow);
			ApplyThemeTokens();
		}

		// ---- Runtime creation of missing UI components ----

		static Transform FindLayerButtonsRow(Transform parent)
		{
			if (parent == null) return null;
			for (int i = 0; i < parent.childCount; i++)
			{
				var c = parent.GetChild(i);
				if (c != null && c.name == "LayerButtonsRow") return c;
			}
			return null;
		}

		/// <summary>Creates the LayerButtonsRow with the "+ Layer" button if it doesn't already exist.</summary>
		static Button EnsureLayersAddButtonRow(RectTransform scrollContent)
		{
			if (scrollContent == null) return null;
			// Check if a LayerButtonsRow already exists anywhere in the hierarchy (scroll content, section root, etc.)
			Transform existingRow = FindLayerButtonsRow(scrollContent);
			if (existingRow == null && scrollContent.parent != null)
				existingRow = FindLayerButtonsRow(scrollContent.parent);
			if (existingRow != null)
			{
				var existingBtn = existingRow.Find("AddLayerBtn")?.GetComponent<Button>();
				if (existingBtn != null) return existingBtn;
			}
			var buttonsRowGo = new GameObject("LayerButtonsRow");
			buttonsRowGo.transform.SetParent(scrollContent, false);
			buttonsRowGo.transform.SetAsLastSibling();
			buttonsRowGo.AddComponent<RectTransform>();
			var rowLE = buttonsRowGo.AddComponent<LayoutElement>();
			rowLE.preferredHeight = 26;
			rowLE.flexibleWidth = 1;
			rowLE.flexibleHeight = 0;
			var rowHLG = buttonsRowGo.AddComponent<HorizontalLayoutGroup>();
			rowHLG.spacing = 4;
			rowHLG.childAlignment = TextAnchor.MiddleLeft;
			rowHLG.childControlWidth = true;
			rowHLG.childControlHeight = true;
			rowHLG.childForceExpandWidth = false;
			rowHLG.childForceExpandHeight = false;
			rowHLG.padding = new RectOffset(0, 2, 0, 0);

			var addBtnGo = new GameObject("AddLayerBtn");
			addBtnGo.transform.SetParent(buttonsRowGo.transform, false);
			var addLE = addBtnGo.AddComponent<LayoutElement>();
			addLE.preferredWidth = 80;
			addLE.preferredHeight = 24;
			addLE.flexibleWidth = 1;
			var addImg = addBtnGo.AddComponent<Image>();
			addImg.color = new Color(0.25f, 0.45f, 0.3f, 0.95f);
			addImg.raycastTarget = true;
			var addBtn = addBtnGo.AddComponent<Button>();
			addBtn.targetGraphic = addImg;
			var addTxtGo = new GameObject("Text");
			addTxtGo.transform.SetParent(addBtnGo.transform, false);
			var addTxtRect = addTxtGo.AddComponent<RectTransform>();
			addTxtRect.anchorMin = Vector2.zero;
			addTxtRect.anchorMax = Vector2.one;
			addTxtRect.offsetMin = Vector2.zero;
			addTxtRect.offsetMax = Vector2.zero;
			var addTxt = addTxtGo.AddComponent<TextMeshProUGUI>();
			addTxt.text = "+ Layer";
			addTxt.fontSize = 12;
			addTxt.color = Color.white;
			addTxt.alignment = TextAlignmentOptions.Center;
			addTxt.raycastTarget = false;

			// Collapse visible layers into one (same row)
			var collapseBtnGo = new GameObject("CollapseBtn");
			collapseBtnGo.transform.SetParent(buttonsRowGo.transform, false);
			var collapseLE = collapseBtnGo.AddComponent<LayoutElement>();
			collapseLE.preferredWidth = 90;
			collapseLE.preferredHeight = 24;
			collapseLE.flexibleWidth = 0;
			var collapseImg = collapseBtnGo.AddComponent<Image>();
			collapseImg.color = new Color(0.45f, 0.35f, 0.25f, 0.95f);
			collapseImg.raycastTarget = true;
			var collapseBtn = collapseBtnGo.AddComponent<Button>();
			collapseBtn.targetGraphic = collapseImg;
			var collapseTxtGo = new GameObject("Text");
			collapseTxtGo.transform.SetParent(collapseBtnGo.transform, false);
			var collapseTxtRect = collapseTxtGo.AddComponent<RectTransform>();
			collapseTxtRect.anchorMin = Vector2.zero;
			collapseTxtRect.anchorMax = Vector2.one;
			collapseTxtRect.offsetMin = Vector2.zero;
			collapseTxtRect.offsetMax = Vector2.zero;
			var collapseTxt = collapseTxtGo.AddComponent<TextMeshProUGUI>();
			collapseTxt.text = "Collapse";
			collapseTxt.fontSize = 12;
			collapseTxt.color = Color.white;
			collapseTxt.alignment = TextAlignmentOptions.Center;
			collapseTxt.raycastTarget = false;

			return addBtn;
		}

		static PaintTab_LayersPanel_UI CreateLayersPanelRuntime(RectTransform scrollContent, Transform sectionRoot)
		{
			if (PaintLayerStack_MGR.instance == null)
			{
				var mgrGo = new GameObject("PaintLayerStack_MGR_Runtime");
				mgrGo.AddComponent<PaintLayerStack_MGR>();
			}

			// Panel lives inside scroll content so the layer list scrolls; list root = panel transform
			var go = new GameObject("PaintTab_LayersPanel_Runtime");
			go.transform.SetParent(scrollContent, false);
			go.transform.SetAsFirstSibling();
			var rect = go.AddComponent<RectTransform>();
			rect.anchorMin = new Vector2(0, 1);
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0.5f, 1);
			rect.sizeDelta = Vector2.zero;
			var goLE = go.AddComponent<LayoutElement>();
			goLE.flexibleWidth = 1;
			goLE.flexibleHeight = 0;
			goLE.minHeight = 0;
			var vlg = go.AddComponent<VerticalLayoutGroup>();
			vlg.spacing = 2;
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlWidth = true;
			vlg.childControlHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.padding = new RectOffset(0, 0, 0, 0);
			var csf = go.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

			var panel = go.AddComponent<PaintTab_LayersPanel_UI>();
			panel.SetLayerStack(PaintLayerStack_MGR.instance);

			// Add Layer button row (always present)
			var addBtn = EnsureLayersAddButtonRow(scrollContent);
			panel.SetAddLayerButton(addBtn);

			return panel;
		}

		static BrushRibbon_UI_AlphaPicker CreateBrushPresetsRuntime(RectTransform parent)
		{
			var mgr = BrushAlphas_MGR.instance;
			if (mgr == null)
			{
				var mgrGo = new GameObject("BrushAlphas_MGR_Runtime");
				mgr = mgrGo.AddComponent<BrushAlphas_MGR>();
				// Keep manager in same hierarchy so it stays findable and isn't unloaded
				mgrGo.transform.SetParent(parent.root, true);
			}

			const int brushPresetsContentMinHeight = 140;
			var btnRow = new GameObject("BrushPresets_Buttons");
			btnRow.transform.SetParent(parent, false);
			btnRow.transform.SetAsFirstSibling();
			var btnRowRect = btnRow.AddComponent<RectTransform>();
			btnRowRect.sizeDelta = new Vector2(0, 26);
			var btnRowLE = btnRow.AddComponent<LayoutElement>();
			btnRowLE.preferredHeight = 26;
			btnRowLE.minHeight = 26;
			btnRowLE.flexibleHeight = 0; // don't stretch row vertically — keeps buttons compact
			btnRowLE.flexibleWidth = 1;
			var btnRowH = btnRow.AddComponent<HorizontalLayoutGroup>();
			btnRowH.spacing = 6;
			btnRowH.childAlignment = TextAnchor.MiddleLeft;
			btnRowH.childControlWidth = false;
			btnRowH.childControlHeight = true;
			btnRowH.childForceExpandHeight = false; // don't stretch buttons vertically
			btnRowH.padding = new RectOffset(0, 0, 0, 0); // no padding; scroll content VLG handles the 3px edge

			var content = new GameObject("BrushPresets_Content");
			content.transform.SetParent(parent, false);
			var contentRect = content.AddComponent<RectTransform>();
			contentRect.anchorMin = new Vector2(0, 1);
			contentRect.anchorMax = Vector2.one;
			contentRect.pivot = new Vector2(0, 1); // left-aligned: consolidate with Load ABR/PNG button row
			contentRect.offsetMin = Vector2.zero;
			contentRect.offsetMax = Vector2.zero;
			var contentLE = content.AddComponent<LayoutElement>();
			contentLE.flexibleWidth = 1;
			contentLE.minWidth = 120;
			contentLE.minHeight = brushPresetsContentMinHeight;
			contentLE.flexibleHeight = 0f; // use preferred height only so scroll content height = button row + picker; enables scrolling when many brushes
			var csf = content.AddComponent<ContentSizeFitter>();
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // full width so collapse/folder/name align under Load ABR/PNG
			var vlg = content.AddComponent<VerticalLayoutGroup>();
			vlg.spacing = 1; // must match AlphaPicker root spacing so section stack is tight; was 6 and blocked flush layout
			vlg.padding = new RectOffset(0, 0, 0, 0); // gap is scroll content spacing (PickerTopSpacingPx), not picker padding
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlWidth = true;
			vlg.childControlHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.childForceExpandHeight = false;
			var picker = content.AddComponent<BrushRibbon_UI_AlphaPicker>();
			picker.SetBrushAlphasMGR(mgr);
			picker.RebuildGrid();

			AddBrushPresetButton(btnRow.transform, "Load ABR/PNG…", new Color(0.3f, 0.45f, 0.5f, 1f), () => picker.OpenLoadBrushDialog());
			AddBrushPresetButton(btnRow.transform, "Refresh", new Color(0.35f, 0.4f, 0.35f, 1f), () => picker.RefreshFromFolder());
			AddBrushPresetButton(btnRow.transform, "Delete", new Color(0.5f, 0.25f, 0.25f, 1f), () => picker.DeleteSelectedBrush());
			AddBrushPresetButton(btnRow.transform, "Delete permanently", new Color(0.55f, 0.2f, 0.2f, 1f), () => picker.DeleteSelectedBrushPermanently());

			return picker;
		}

		static void AddBrushPresetButton(Transform parent, string label, Color bgColor, UnityEngine.Events.UnityAction onClick)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", "_").Replace("…", "").Replace("/", "_"));
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(100, 22);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = 100;
			le.preferredHeight = 22;
			le.minHeight = 22;
			le.flexibleHeight = 0; // keep buttons short — avoid elongated look
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.onClick.AddListener(onClick);
			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = Vector2.zero;
			txtRect.offsetMax = Vector2.zero;
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.text = label;
			txt.fontSize = 10;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
		}

		static void EnsurePaletteLoadButtonExists(RectTransform section)
		{
			for (int i = 0; i < section.childCount; i++)
			{
				if (section.GetChild(i).name == "PaletteLoadButtonRow")
					return;
			}
			var row = new GameObject("PaletteLoadButtonRow");
			row.transform.SetParent(section, false);
			row.transform.SetAsFirstSibling();
			var rowRect = row.AddComponent<RectTransform>();
			rowRect.sizeDelta = new Vector2(0, 28);
			var rowLE = row.AddComponent<LayoutElement>();
			rowLE.preferredHeight = 28;
			rowLE.flexibleWidth = 1;
			rowLE.flexibleHeight = 0;
			var hlg = row.AddComponent<HorizontalLayoutGroup>();
			hlg.spacing = 4;
			hlg.childAlignment = TextAnchor.MiddleLeft;
			hlg.childControlWidth = true;
			hlg.childControlHeight = true;
			hlg.childForceExpandWidth = false;
			hlg.childForceExpandHeight = true;
			hlg.padding = new RectOffset(0, 0, 0, 0);
		AddPaletteButton(row.transform, "Refresh", new Color(0.3f, 0.4f, 0.35f, 1f), () =>
		{
			if (ColorPalette_MGR.instance == null) return;
			if (ColorPalette_MGR.instance.ReloadCurrentPalette() && Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText("Palette reloaded: " + ColorPalette_MGR.instance.CurrentPaletteName, false, 2f, false);
			else if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(ColorPalette_MGR.instance.HasPalette ? "Reload failed (file missing or invalid?)" : "No palette loaded to refresh", false, 2f, false);
		});
		AddPaletteButton(row.transform, "Load ASE/ACO/GPL…", new Color(0.35f, 0.45f, 0.5f, 1f), () =>
		{
			if (ColorPalette_MGR.instance == null)
			{
				var mgrGo = new GameObject("ColorPalette_MGR_Runtime");
				mgrGo.AddComponent<ColorPalette_MGR>();
			}
			ColorPalette_MGR.instance?.OpenLoadPaletteDialog();
		});
		AddPaletteButton(row.transform, "Add to current palette…", new Color(0.4f, 0.38f, 0.5f, 1f), () =>
		{
			if (ColorPalette_MGR.instance == null)
			{
				var mgrGo = new GameObject("ColorPalette_MGR_Runtime");
				mgrGo.AddComponent<ColorPalette_MGR>();
			}
			ColorPalette_MGR.instance?.OpenAddPaletteDialog();
		});
			// Square +/- buttons: add swatch (current brush color) or remove selected swatch
			AddPaletteSquareButton(row.transform, "+", new Color(0.25f, 0.45f, 0.3f, 1f), () =>
			{
				if (ColorPalette_MGR.instance == null) return;
				var brushColors = FindObjectOfType<BrushRibbon_UI_Colors>(true);
				Color c = brushColors != null ? brushColors._brushColor : Color.gray;
				ColorPalette_MGR.instance.AddSwatch(c);
				if (Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText("Swatch added", false, 1.5f, false);
			});
			AddPaletteSquareButton(row.transform, "−", new Color(0.5f, 0.25f, 0.25f, 1f), () =>
			{
				var swatches = FindObjectOfType<PaletteSwatches_UI>(true);
				if (swatches != null && swatches.SelectedSwatchIndex >= 0)
				{
					swatches.RemoveSelectedSwatch();
					if (Viewport_StatusText.instance != null)
						Viewport_StatusText.instance.ShowStatusText("Swatch removed", false, 1.5f, false);
				}
				else if (Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText("Select a swatch first", false, 1.5f, false);
			});
		}

		static void AddPaletteButton(Transform parent, string label, Color bgColor, System.Action onClick)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", ""));
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(140, 24);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = 140;
			le.preferredHeight = 24;
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(() => onClick?.Invoke());
			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = Vector2.zero;
			txtRect.offsetMax = Vector2.zero;
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.text = label;
			txt.fontSize = 11;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
		}

		/// <summary> Adds a square button (e.g. + or −) to the palette row. </summary>
		static void AddPaletteSquareButton(Transform parent, string symbol, Color bgColor, System.Action onClick)
		{
			const int size = 24;
			var go = new GameObject("Btn_" + (symbol == "−" ? "Minus" : "Plus"));
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(size, size);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = size;
			le.preferredHeight = size;
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(() => onClick?.Invoke());
			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = Vector2.zero;
			txtRect.offsetMax = Vector2.zero;
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.text = symbol;
			txt.fontSize = 14;
			txt.color = Color.white;
			txt.alignment = TextAlignmentOptions.Center;
			txt.raycastTarget = false;
		}

		static PaletteSwatches_UI CreatePaletteSwatchesRuntime(RectTransform parent)
		{
			if (ColorPalette_MGR.instance == null)
			{
				var mgrGo = new GameObject("ColorPalette_MGR_Runtime");
				mgrGo.AddComponent<ColorPalette_MGR>();
			}
			var go = new GameObject("PaletteSwatches_Runtime");
			go.transform.SetParent(parent, false);
			var rect = go.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			var glg = go.AddComponent<GridLayoutGroup>();
			glg.cellSize = new Vector2(24, 24);
			glg.spacing = new Vector2(2, 2);
			glg.constraint = GridLayoutGroup.Constraint.Flexible;
			glg.padding = new RectOffset(4, 4, 4, 4);
			var swatches = go.AddComponent<PaletteSwatches_UI>();
			return swatches;
		}

		static void CreateToolOptionsRuntime(RectTransform parent)
		{
			var rowGo = new GameObject("ToolOptionsRow");
			rowGo.transform.SetParent(parent, false);
			var rowRect = rowGo.AddComponent<RectTransform>();
			rowRect.sizeDelta = new Vector2(0, 0);
			var rowLE = rowGo.AddComponent<LayoutElement>();
			rowLE.flexibleWidth = 1;
			// Do not flexibleHeight-steal inside Tool Options ScrollContent — that fights ContentSizeFitter scroll.
			rowLE.flexibleHeight = 0;
			rowLE.minHeight = 28f;
			var glg = rowGo.AddComponent<GridLayoutGroup>();
			glg.cellSize = new Vector2(80, 28);
			glg.spacing = new Vector2(4, 4);
			glg.constraint = GridLayoutGroup.Constraint.Flexible;
			glg.padding = new RectOffset(2, 2, 2, 2);
			glg.childAlignment = TextAnchor.UpperLeft;

			MakeToolButton(rowGo.transform, "Bucket Fill", "Ctrl+F", new Color(0.28f, 0.38f, 0.5f, 1f),
				() => { BrushRibbon_UI_BucketFill._Act_onClicked?.Invoke(); ShowToolFeedback("Bucket Fill"); });
			MakeToolButton(rowGo.transform, "Invert Mask", "", new Color(0.4f, 0.35f, 0.5f, 1f),
				() => { BrushRibbon_UI_InvertMask.onClicked?.Invoke(); ShowToolFeedback("Invert Mask"); });
			MakeToolButton(rowGo.transform, "Clear Mask", "Ctrl+E", new Color(0.5f, 0.28f, 0.28f, 1f),
				() => { BrushRibbon_UI_DeleteButton.onClicked?.Invoke(); ShowToolFeedback("Clear Mask"); });
			MakeDepthLimitToggle(rowGo.transform);
			MakeDepthLimitSlider(rowGo.transform);
			MakePaintSymmetryToggle(rowGo.transform);
			MakeBrushToolOptionsExpando(rowGo.transform, parent);
		}

		static void ShowToolFeedback(string toolName)
		{
			if (Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(toolName + " triggered.", false, 1.2f, false);
			else
				UnityEngine.Debug.Log("[Paint Tab] " + toolName + " triggered.");
		}

		static void SetSelectablesInteractableOnSubtree(Transform t, bool interactable)
		{
			if (t == null) return;
			var selectables = t.GetComponentsInChildren<Selectable>(true);
			for (int i = 0; i < selectables.Length; i++)
			{
				if (selectables[i] != null)
					selectables[i].interactable = interactable;
			}
		}

		/// <summary>While Brush options panel is open, depth/tools in the same row cannot steal drags/clicks; expando headers stay active.</summary>
		static void SetToolOptionsRowBehindBrushPanelBlocked(Transform toolOptionsRow, Transform brushExpandoRoot, bool blockRowTools)
		{
			if (toolOptionsRow == null) return;
			for (int i = 0; i < toolOptionsRow.childCount; i++)
			{
				var ch = toolOptionsRow.GetChild(i);
				if (ch == null) continue;
				// Keep Brush options + Value Assist headers clickable so either expando can still be toggled.
				if (brushExpandoRoot != null && ch == brushExpandoRoot)
					continue;
				if (ch.name == "ValueAssistExpando")
					continue;
				SetSelectablesInteractableOnSubtree(ch, !blockRowTools);
			}
		}

		static bool TryGetToolOptionsRowAndExpando(RectTransform section, out Transform row, out Transform expando)
		{
			row = null;
			expando = null;
			if (section == null) return false;
			for (int i = 0; i < section.childCount; i++)
			{
				var c = section.GetChild(i);
				if (c != null && c.name == "ToolOptionsRow")
				{
					row = c;
					expando = c.Find("BrushToolOptionsExpando");
					return true;
				}
			}
			return false;
		}

		/// <summary>Keep depth/tools row blocked while Brush options and/or Value Assist panel is open.</summary>
		public static void SyncToolOptionsRowModalBlockForSection(RectTransform section)
		{
			if (!TryGetToolOptionsRowAndExpando(section, out var row, out var expando)) return;
			Transform brushPanel = section.Find("BrushOptsPanel");
			Transform vaPanel = section.Find("ValueAssistPanel");
			bool open = (brushPanel != null && brushPanel.gameObject.activeSelf)
			            || (vaPanel != null && vaPanel.gameObject.activeSelf);
			SetToolOptionsRowBehindBrushPanelBlocked(row, expando, open);
		}

		/// <summary>Accordion helper — close Value Assist (+ pinned collapse bar) when Brush options opens.</summary>
		public static void CloseValueAssistPanel(RectTransform toolSectionParent)
		{
			PaintTab_ValueAssistPanel_UI.CollapseUnder(toolSectionParent);
		}

		/// <summary>Accordion helper — close Brush options (+ pinned collapse bar) when Value Assist opens.</summary>
		public static void CloseBrushOptionsPanel(RectTransform toolSectionParent)
		{
			if (toolSectionParent == null) return;
			Transform panel = toolSectionParent.Find("BrushOptsPanel");
			bool wasOpen = panel != null && panel.gameObject.activeSelf;
			if (panel != null && wasOpen) {
				panel.gameObject.SetActive(false);
				var le = panel.GetComponent<LayoutElement>();
				if (le != null) le.preferredHeight = 0f;
			}
			var sr = toolSectionParent.GetComponentInParent<ScrollRect>();
			if (sr != null && sr.viewport != null) {
				Transform collapse = sr.viewport.Find("BrushOptsCollapseBtn");
				if (collapse != null)
					collapse.gameObject.SetActive(false);
			}
			if (TryGetToolOptionsRowAndExpando(toolSectionParent, out _, out var expando) && expando != null) {
				var headerTxt = expando.GetComponentInChildren<TextMeshProUGUI>(true);
				if (headerTxt != null)
					headerTxt.text = "Brush options ▼";
			}
			SyncToolOptionsRowModalBlockForSection(toolSectionParent);
			if (wasOpen)
				LayoutRebuilder.ForceRebuildLayoutImmediate(toolSectionParent);
		}

		/// <summary>Keep depth/tools row blocked iff an expando panel is open (e.g. after tab re-enable).</summary>
		static void SyncBrushPanelModalBlockStateForToolSection(RectTransform section)
		{
			SyncToolOptionsRowModalBlockForSection(section);
		}

		static void MakeToolButton(Transform parent, string label, string shortcut, Color bgColor, UnityEngine.Events.UnityAction onClick)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", ""));
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var img = go.AddComponent<Image>();
			img.color = bgColor;
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.onClick.AddListener(onClick);
			var colors = btn.colors;
			colors.highlightedColor = new Color(bgColor.r + 0.15f, bgColor.g + 0.15f, bgColor.b + 0.15f, 1f);
			colors.pressedColor = new Color(bgColor.r + 0.25f, bgColor.g + 0.25f, bgColor.b + 0.25f, 1f);
			btn.colors = colors;

			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = new Vector2(4, 0);
			txtRect.offsetMax = new Vector2(-4, 0);
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			string display = string.IsNullOrEmpty(shortcut) ? label : label + "\n<size=8>" + shortcut + "</size>";
			txt.text = display;
			txt.color = Color.white;
			ApplyPaintTabToolRowTmp(txt, TextAlignmentOptions.Center);
		}

		static void MakeDepthLimitToggle(Transform parent)
		{
			Color authoredOff = new Color(0.3f, 0.3f, 0.3f, 1f);
			Color authoredOn = new Color(0.2f, 0.55f, 0.35f, 1f);

			var go = new GameObject("Btn_DepthLimit");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var img = go.AddComponent<Image>();
			img.color = PaintToolFaceColor(false, authoredOn, authoredOff);
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();

			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = new Vector2(4, 0);
			txtRect.offsetMax = new Vector2(-4, 0);
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.color = Color.white;
			ApplyPaintTabToolRowTmp(txt, TextAlignmentOptions.Center);

			System.Action refreshButtonState = () =>
			{
				var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
				bool isOn = ribbon != null && ribbon.brushDepthLimit01 > 0f;
				img.color = PaintToolFaceColor(isOn, authoredOn, authoredOff);
				txt.text = isOn ? "Depth Limit\n<size=8>ON</size>" : "Depth Limit\n<size=8>OFF</size>";
			};
			refreshButtonState();

			btn.onClick.AddListener(() =>
			{
				var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
				if (ribbon == null) return;
				bool isOn = ribbon.brushDepthLimit01 > 0f;
				if (isOn)
				{
					ribbon.SetBrushDepthLimit(0f);
					ShowToolFeedback("Depth limit OFF — classic painting");
				}
				else
				{
					ribbon.SetBrushDepthLimit(SD_WorkflowOptionsRibbon_UI.DepthLimitDefaultRange);
					ShowToolFeedback("Depth limit ON — adjust slider for tight/loose");
				}
				refreshButtonState();
				SyncDepthLimitSliderFromRibbon(parent);
			});
		}

		/// <summary>Find the Depth Limit slider in the same tool row and set its value from ribbon (for toggle or init).</summary>
		static void SyncDepthLimitSliderFromRibbon(Transform toolRowTransform)
		{
			var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
			if (ribbon == null) return;
			var slider = toolRowTransform.GetComponentInChildren<Slider>(true);
			if (slider != null && slider.gameObject.name.Contains("DepthLimit"))
			{
				slider.SetValueWithoutNotify(ribbon.GetBrushDepthLimitSlider01());
			}
		}

		/// <summary>Depth limit range slider: 0 = off, 0.01–1 = tight to loose. Gives user flexibility.</summary>
		static void MakeDepthLimitSlider(Transform parent)
		{
			var go = new GameObject("DepthLimitSlider");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minWidth = 80;
			le.preferredWidth = 80;

			var bg = go.AddComponent<Image>();
			bg.color = new Color(0.22f, 0.28f, 0.35f, 0.95f);
			bg.raycastTarget = true;

			var slider = go.AddComponent<Slider>();
			slider.minValue = 0f;
			slider.maxValue = 1f;
			slider.wholeNumbers = false;
			slider.fillRect = null;
			slider.handleRect = null;
			slider.direction = Slider.Direction.LeftToRight;
			slider.transition = Selectable.Transition.None;

			var fillArea = new GameObject("Fill Area");
			fillArea.transform.SetParent(go.transform, false);
			var fillAreaRect = fillArea.AddComponent<RectTransform>();
			fillAreaRect.anchorMin = new Vector2(0, 0.25f);
			fillAreaRect.anchorMax = new Vector2(1, 0.75f);
			fillAreaRect.offsetMin = new Vector2(4, 0);
			fillAreaRect.offsetMax = new Vector2(-4, 0);
			var fill = new GameObject("Fill");
			fill.transform.SetParent(fillArea.transform, false);
			var fillRect = fill.AddComponent<RectTransform>();
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = Vector2.one;
			fillRect.offsetMin = Vector2.zero;
			fillRect.offsetMax = Vector2.zero;
			var fillImg = fill.AddComponent<Image>();
			fillImg.color = new Color(0.2f, 0.5f, 0.35f, 0.8f);
			slider.fillRect = fillRect;
			var handleArea = new GameObject("Handle Slide Area");
			handleArea.transform.SetParent(go.transform, false);
			var handleAreaRect = handleArea.AddComponent<RectTransform>();
			handleAreaRect.anchorMin = new Vector2(0, 0);
			handleAreaRect.anchorMax = new Vector2(1, 1);
			handleAreaRect.offsetMin = new Vector2(4, 0);
			handleAreaRect.offsetMax = new Vector2(-4, 0);
			var handle = new GameObject("Handle");
			handle.transform.SetParent(handleArea.transform, false);
			var handleRect = handle.AddComponent<RectTransform>();
			handleRect.sizeDelta = new Vector2(8, 20);
			var handleImg = handle.AddComponent<Image>();
			handleImg.color = Color.white;
			slider.handleRect = handleRect;
			slider.targetGraphic = handleImg;

			var labelGo = new GameObject("Label");
			labelGo.transform.SetParent(go.transform, false);
			var labelRect = labelGo.AddComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(2, 0);
			labelRect.offsetMax = new Vector2(-2, 0);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "Depth";
			label.color = new Color(0.9f, 0.9f, 0.9f, 1f);
			ApplyPaintTabToolRowTmp(label, TextAlignmentOptions.Left, 9f);

			var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
			if (ribbon != null)
				slider.SetValueWithoutNotify(ribbon.GetBrushDepthLimitSlider01());

			slider.onValueChanged.AddListener((float v) =>
			{
				SD_WorkflowOptionsRibbon_UI.instance?.SetBrushDepthLimitFromSlider01(v);
				SyncDepthLimitButtonState(parent);
			});
		}

		/// <summary>Horizontal track + fill + handle (depth-limit style). Parent must supply layout (row / column).</summary>
		static Slider BuildBrushOptsSliderTrack(Transform parent, float min, float max, float initialValue,
			UnityEngine.Events.UnityAction<float> onChanged, bool wholeNumbers = false)
		{
			var go = new GameObject("Slider");
			go.transform.SetParent(parent, false);
			var le = go.AddComponent<LayoutElement>();
			le.flexibleWidth = 1f;
			le.minWidth = 120;
			le.minHeight = 28;
			le.preferredHeight = 28;

			var bg = go.AddComponent<Image>();
			bg.color = new Color(0.22f, 0.28f, 0.35f, 0.95f);
			bg.raycastTarget = true;

			var slider = go.AddComponent<Slider>();
			slider.minValue = min;
			slider.maxValue = max;
			slider.wholeNumbers = wholeNumbers;
			slider.fillRect = null;
			slider.handleRect = null;
			slider.direction = Slider.Direction.LeftToRight;
			slider.transition = Selectable.Transition.None;

			var fillArea = new GameObject("Fill Area");
			fillArea.transform.SetParent(go.transform, false);
			var fillAreaRect = fillArea.AddComponent<RectTransform>();
			fillAreaRect.anchorMin = new Vector2(0, 0.25f);
			fillAreaRect.anchorMax = new Vector2(1, 0.75f);
			fillAreaRect.offsetMin = new Vector2(4, 0);
			fillAreaRect.offsetMax = new Vector2(-4, 0);
			var fill = new GameObject("Fill");
			fill.transform.SetParent(fillArea.transform, false);
			var fillRect = fill.AddComponent<RectTransform>();
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = Vector2.one;
			fillRect.offsetMin = Vector2.zero;
			fillRect.offsetMax = Vector2.zero;
			var fillImg = fill.AddComponent<Image>();
			fillImg.color = new Color(0.2f, 0.5f, 0.35f, 0.8f);
			slider.fillRect = fillRect;
			var handleArea = new GameObject("Handle Slide Area");
			handleArea.transform.SetParent(go.transform, false);
			var handleAreaRect = handleArea.AddComponent<RectTransform>();
			handleAreaRect.anchorMin = new Vector2(0, 0);
			handleAreaRect.anchorMax = new Vector2(1, 1);
			handleAreaRect.offsetMin = new Vector2(4, 0);
			handleAreaRect.offsetMax = new Vector2(-4, 0);
			var handle = new GameObject("Handle");
			handle.transform.SetParent(handleArea.transform, false);
			var handleRect = handle.AddComponent<RectTransform>();
			handleRect.sizeDelta = new Vector2(8, 20);
			var handleImg = handle.AddComponent<Image>();
			handleImg.color = Color.white;
			slider.handleRect = handleRect;
			slider.targetGraphic = handleImg;

			slider.SetValueWithoutNotify(initialValue);
			slider.onValueChanged.AddListener(onChanged);
			return slider;
		}

		/// <summary>Label + horizontal slider row inside Brush options expando (tool row / grid).</summary>
		static Slider MakeBrushOptsSliderRow(Transform parent, string rowName, string labelText, float min, float max,
			float initialValue, UnityEngine.Events.UnityAction<float> onChanged)
		{
			var row = new GameObject(rowName);
			row.transform.SetParent(parent, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 30;
			rowLe.preferredHeight = 30;
			rowLe.flexibleWidth = 1;
			var h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 8;
			h.padding = new RectOffset(0, 0, 0, 0);
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = false;
			h.childControlHeight = true;
			h.childForceExpandWidth = true;
			h.childForceExpandHeight = false;

			var lblGo = new GameObject("Lbl");
			lblGo.transform.SetParent(row.transform, false);
			var lblLe = lblGo.AddComponent<LayoutElement>();
			lblLe.minWidth = 88;
			lblLe.preferredWidth = 88;
			var lbl = lblGo.AddComponent<TextMeshProUGUI>();
			StylePaintTabTmp(lbl, labelText, 9f, TextAlignmentOptions.Left);

			return BuildBrushOptsSliderTrack(row.transform, min, max, initialValue, onChanged, false);
		}

		/// <summary>Stacked label above full-width slider (matches readability of Scatter / Mirror rows in narrow Brush options panel).</summary>
		static Slider MakeBrushOptsStackedSliderRow(Transform parent, string rowName, string labelText, float min, float max,
			float initialValue, UnityEngine.Events.UnityAction<float> onChanged, bool wholeNumbers = false)
		{
			var row = new GameObject(rowName);
			row.transform.SetParent(parent, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 58;
			rowLe.preferredHeight = 58;
			rowLe.flexibleWidth = 1;
			var v = row.AddComponent<VerticalLayoutGroup>();
			v.spacing = 6;
			v.padding = new RectOffset(0, 0, 0, 0);
			v.childAlignment = TextAnchor.UpperLeft;
			v.childControlWidth = true;
			v.childControlHeight = true;
			v.childForceExpandWidth = true;
			v.childForceExpandHeight = false;

			var lblGo = new GameObject("Lbl");
			lblGo.transform.SetParent(row.transform, false);
			var lblLe = lblGo.AddComponent<LayoutElement>();
			lblLe.minHeight = 22;
			lblLe.preferredHeight = 22;
			lblLe.flexibleWidth = 1;
			var lbl = lblGo.AddComponent<TextMeshProUGUI>();
			lbl.color = new Color(0.88f, 0.89f, 0.92f, 1f);
			StylePaintTabTmp(lbl, labelText, kPaintTabUiFontSize, TextAlignmentOptions.Left);

			return BuildBrushOptsSliderTrack(row.transform, min, max, initialValue, onChanged, wholeNumbers);
		}

		/// <summary>Compact checkbox row for brush/smudge tool options (matches radio tint colors).</summary>
		static Toggle MakeBrushOptsCheckboxRow(Transform parent, string rowName, string labelText, bool initialOn,
			UnityEngine.Events.UnityAction<bool> onChanged)
		{
			Color authoredOff = new Color(0.34f, 0.36f, 0.4f, 1f);
			Color authoredOn = new Color(0.22f, 0.45f, 0.55f, 1f);
			var row = new GameObject(rowName);
			row.transform.SetParent(parent, false);
			row.AddComponent<RectTransform>();
			var rowLe = row.AddComponent<LayoutElement>();
			rowLe.minHeight = 28;
			rowLe.preferredHeight = 28;
			rowLe.flexibleWidth = 1;
			var h = row.AddComponent<HorizontalLayoutGroup>();
			h.spacing = 8;
			h.padding = new RectOffset(0, 0, 0, 0);
			h.childAlignment = TextAnchor.MiddleLeft;
			h.childControlWidth = false;
			h.childControlHeight = true;
			h.childForceExpandWidth = false;
			h.childForceExpandHeight = false;

			var boxGo = new GameObject("Box");
			boxGo.transform.SetParent(row.transform, false);
			var boxLe = boxGo.AddComponent<LayoutElement>();
			boxLe.minWidth = 36;
			boxLe.preferredWidth = 36;
			var img = boxGo.AddComponent<Image>();
			img.color = PaintToolFaceColor(initialOn, authoredOn, authoredOff);
			var toggle = boxGo.AddComponent<Toggle>();
			toggle.targetGraphic = img;
			toggle.graphic = null;
			var cb = toggle.colors;
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(0.95f, 0.95f, 1f);
			cb.pressedColor = new Color(0.88f, 0.88f, 0.92f);
			cb.selectedColor = Color.white;
			toggle.colors = cb;
			toggle.isOn = initialOn;
			toggle.onValueChanged.AddListener(isOn =>
			{
				img.color = PaintToolFaceColor(isOn, authoredOn, authoredOff);
				onChanged?.Invoke(isOn);
			});

			var lblGo = new GameObject("Lbl");
			lblGo.transform.SetParent(row.transform, false);
			var lblLe = lblGo.AddComponent<LayoutElement>();
			lblLe.flexibleWidth = 1;
			lblLe.minWidth = 40;
			var lbl = lblGo.AddComponent<TextMeshProUGUI>();
			lbl.color = new Color(0.88f, 0.89f, 0.92f, 1f);
			StylePaintTabTmp(lbl, labelText, kPaintTabUiFontSize, TextAlignmentOptions.Left);

			return toggle;
		}

		/// <summary> Collapsible dropdown-style block: compact header in tool row + panel below with radio groups. </summary>
		static void MakeBrushToolOptionsExpando(Transform toolRowParent, RectTransform toolSectionParent)
		{
			Color radioOff = new Color(0.34f, 0.36f, 0.4f, 1f);
			Color radioOn = new Color(0.22f, 0.45f, 0.55f, 1f);

			var root = new GameObject("BrushToolOptionsExpando");
			root.transform.SetParent(toolRowParent, false);
			root.AddComponent<RectTransform>();
			var rootLe = root.AddComponent<LayoutElement>();
			rootLe.minWidth = 80;
			rootLe.preferredWidth = 80;
			rootLe.flexibleWidth = 0;
			rootLe.minHeight = 28;
			rootLe.preferredHeight = 28;
			rootLe.flexibleHeight = 0;

			var headerGo = new GameObject("BrushOptsHeaderBtn");
			headerGo.transform.SetParent(root.transform, false);
			var headerRt = headerGo.AddComponent<RectTransform>();
			headerRt.anchorMin = Vector2.zero;
			headerRt.anchorMax = Vector2.one;
			headerRt.offsetMin = Vector2.zero;
			headerRt.offsetMax = Vector2.zero;
			var headerImg = headerGo.AddComponent<Image>();
			headerImg.color = new Color(0.25f, 0.32f, 0.4f, 1f);
			headerImg.raycastTarget = true;
			var headerBtn = headerGo.AddComponent<Button>();
			var headerColors = headerBtn.colors;
			headerColors.highlightedColor = new Color(0.32f, 0.4f, 0.48f, 1f);
			headerColors.pressedColor = new Color(0.2f, 0.26f, 0.34f, 1f);
			headerBtn.colors = headerColors;

			var headerTxtGo = new GameObject("Label");
			headerTxtGo.transform.SetParent(headerGo.transform, false);
			var headerTxtRt = headerTxtGo.AddComponent<RectTransform>();
			headerTxtRt.anchorMin = Vector2.zero;
			headerTxtRt.anchorMax = Vector2.one;
			headerTxtRt.offsetMin = new Vector2(6, 0);
			headerTxtRt.offsetMax = new Vector2(-6, 0);
			var headerTxt = headerTxtGo.AddComponent<TextMeshProUGUI>();
			headerTxt.color = new Color(0.92f, 0.93f, 0.95f, 1f);
			StylePaintTabTmp(headerTxt, "Brush options ▼", kPaintTabUiFontSize, TextAlignmentOptions.Left);

			var panelGo = new GameObject("BrushOptsPanel");
			panelGo.transform.SetParent(toolSectionParent, false);
			panelGo.SetActive(false);
			panelGo.AddComponent<RectTransform>();
			var panelLe = panelGo.AddComponent<LayoutElement>();
			panelLe.flexibleWidth = 1;
			panelLe.minHeight = 100;
			panelLe.preferredHeight = 0;
			panelLe.flexibleHeight = 0;
			var panelImg = panelGo.AddComponent<Image>();
			panelImg.color = new Color(0.16f, 0.18f, 0.22f, 0.98f);
			// Block raycasts for the full panel rect so holes/gaps do not click through to ToolOptionsRow (depth slider, etc.).
			panelImg.raycastTarget = true;
			var panelVlg = panelGo.AddComponent<VerticalLayoutGroup>();
			panelVlg.spacing = 4;
			panelVlg.padding = new RectOffset(6, 6, 6, 6);
			panelVlg.childControlWidth = true;
			panelVlg.childControlHeight = true;
			panelVlg.childForceExpandWidth = true;
			var panelCsf = panelGo.AddComponent<ContentSizeFitter>();
			panelCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			panelCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			var toolOptsScroll = toolSectionParent.GetComponentInParent<ScrollRect>();
			bool pinCollapseToToolViewport = toolOptsScroll != null && toolOptsScroll.viewport != null;

			MakeBrushOptsSectionLabel(panelGo.transform, "Scatter (viewport jitter)");
			var scatterRow = new GameObject("ScatterRow");
			scatterRow.transform.SetParent(panelGo.transform, false);
			scatterRow.AddComponent<RectTransform>();
			var scatterRowLe = scatterRow.AddComponent<LayoutElement>();
			scatterRowLe.minHeight = 32;
			var scatterH = scatterRow.AddComponent<HorizontalLayoutGroup>();
			scatterH.spacing = 4;
			scatterH.childAlignment = TextAnchor.MiddleLeft;
			scatterH.childControlWidth = false;
			scatterH.childControlHeight = true;
			scatterH.childForceExpandWidth = false;
			var tgScatter = scatterRow.AddComponent<ToggleGroup>();
			tgScatter.allowSwitchOff = false;
			var scatterToggles = new Toggle[3];
			string[] scatterLabels = { "Off", "Light", "Med" };
			for (int i = 0; i < 3; i++)
			{
				int ix = i;
				scatterToggles[i] = MakeToolOptionsRadioToggle(scatterRow.transform, tgScatter, scatterLabels[ix], radioOff, radioOn, () =>
				{
					var inst = BrushRibbon_UI_Size.instance;
					if (inst != null) inst.SetScatterMode((BrushScatterMode)ix);
					ShowToolFeedback("Scatter: " + scatterLabels[ix]);
				});
			}

			MakeBrushOptsSectionLabel(panelGo.transform, "Tip rotation");
			var angleRow = new GameObject("TipAngleRow");
			angleRow.transform.SetParent(panelGo.transform, false);
			angleRow.AddComponent<RectTransform>();
			var angleRowLe = angleRow.AddComponent<LayoutElement>();
			angleRowLe.minHeight = 32;
			var angleH = angleRow.AddComponent<HorizontalLayoutGroup>();
			angleH.spacing = 4;
			angleH.childAlignment = TextAnchor.MiddleLeft;
			angleH.childControlWidth = false;
			angleH.childControlHeight = true;
			angleH.childForceExpandWidth = false;
			var tgAngle = angleRow.AddComponent<ToggleGroup>();
			tgAngle.allowSwitchOff = false;
			var angleToggles = new Toggle[2];
			string[] angleLabels = { "Fixed", "Follow stroke" };
			for (int i = 0; i < 2; i++)
			{
				BrushTipAngleMode angleMode = (BrushTipAngleMode)i;
				string angleLabel = angleLabels[i];
				angleToggles[i] = MakeToolOptionsRadioToggle(angleRow.transform, tgAngle, angleLabel, radioOff, radioOn, () =>
				{
					var inst = BrushRibbon_UI_Size.instance;
					if (inst != null) inst.SetTipAngleMode(angleMode);
					ShowToolFeedback("Tip: " + angleLabel);
				});
			}

			var smudgeOptsRoot = new GameObject("SmudgeBrushOptsBlock");
			smudgeOptsRoot.transform.SetParent(panelGo.transform, false);
			smudgeOptsRoot.AddComponent<RectTransform>();
			var smudgeBlockLe = smudgeOptsRoot.AddComponent<LayoutElement>();
			smudgeBlockLe.flexibleWidth = 1;
			smudgeBlockLe.minHeight = 8;
			var smudgeVlg = smudgeOptsRoot.AddComponent<VerticalLayoutGroup>();
			smudgeVlg.spacing = 6;
			smudgeVlg.padding = new RectOffset(0, 0, 2, 4);
			smudgeVlg.childAlignment = TextAnchor.UpperLeft;
			smudgeVlg.childControlWidth = true;
			smudgeVlg.childControlHeight = true;
			smudgeVlg.childForceExpandWidth = true;
			smudgeVlg.childForceExpandHeight = false;

			MakeBrushOptsSectionLabel(smudgeOptsRoot.transform, "Smudge (inpaint)");
			var smudgeStrSlider = MakeBrushOptsStackedSliderRow(smudgeOptsRoot.transform, "SmudgeStrengthRow",
				"Strength (0–100% × brush opacity)", 0f, 1f,
				PaintTab_SmudgeBrushOptions.Strength01, v =>
				{
					PaintTab_SmudgeBrushOptions.SetStrength01(v);
					Viewport_StatusText.instance?.ShowStatusText(
						$"Smudge strength {Mathf.RoundToInt(Mathf.Clamp01(v) * 100)}%", false, 0.65f, false);
				});
			var smudgeAngSlider = MakeBrushOptsStackedSliderRow(smudgeOptsRoot.transform, "SmudgeAngleRow",
				"Smear angle (0°–360°)", 0f, 360f,
				PaintTab_SmudgeBrushOptions.AngleDeg, v =>
				{
					PaintTab_SmudgeBrushOptions.SetAngleDeg(v);
					Viewport_StatusText.instance?.ShowStatusText(
						$"Smudge angle {Mathf.RoundToInt(v)}°", false, 0.65f, false);
				});
			var smudgeMixSlider = MakeBrushOptsStackedSliderRow(smudgeOptsRoot.transform, "SmudgeColorMixRow",
				"Surface color mix (neighbor match)", 0f, 1f,
				PaintTab_SmudgeBrushOptions.ColorMixSimilarity01, v =>
				{
					PaintTab_SmudgeBrushOptions.SetColorMixSimilarity01(v);
					Viewport_StatusText.instance?.ShowStatusText(
						$"Surface color mix {Mathf.RoundToInt(Mathf.Clamp01(v) * 100)}%", false, 0.65f, false);
				});
			var smudgeRadSlider = MakeBrushOptsStackedSliderRow(smudgeOptsRoot.transform, "SmudgeNeighborRadiusRow",
				"Sample radius (UV grid steps)", 1f, 4f,
				PaintTab_SmudgeBrushOptions.NeighborGridRadius, v =>
				{
					PaintTab_SmudgeBrushOptions.SetNeighborGridRadius(Mathf.RoundToInt(v));
					Viewport_StatusText.instance?.ShowStatusText(
						$"Smudge radius {Mathf.Clamp(Mathf.RoundToInt(v), 1, 4)}", false, 0.65f, false);
				}, wholeNumbers: true);

			var smudgeMeshUnderToggle = MakeBrushOptsCheckboxRow(smudgeOptsRoot.transform, "SmudgeMeshUnderRow",
				"Sample Art / mesh UV under paint layers", PaintTab_SmudgeBrushOptions.IncludeUvMeshInLayerSmudge, isOn =>
				{
					PaintTab_SmudgeBrushOptions.SetIncludeUvMeshInLayerSmudge(isOn);
					Viewport_StatusText.instance?.ShowStatusText(
						isOn
							? "Smudge: Art/mesh UV adapts under transparent layer paint"
							: "Smudge: layers only (no Art/mesh UV underlay)",
						false, 0.65f, false);
				});

			// Smudge rows: sync sliders when PaintTab_SmudgeBrushOptions changes (API); show/hide block for inpaint + smudge tool. Handlers cleared on tab OnDisable.
			RegisterSmudgeBrushOptsHandlersForUi(smudgeStrSlider, smudgeAngSlider, smudgeMixSlider, smudgeRadSlider, smudgeMeshUnderToggle, smudgeOptsRoot);

			var strictIsoRoot = new GameObject("StrictIsolationBrushOptsBlock");
			strictIsoRoot.transform.SetParent(panelGo.transform, false);
			strictIsoRoot.AddComponent<RectTransform>();
			var strictIsoLe = strictIsoRoot.AddComponent<LayoutElement>();
			strictIsoLe.flexibleWidth = 1;
			strictIsoLe.minHeight = 8;
			var strictIsoVlg = strictIsoRoot.AddComponent<VerticalLayoutGroup>();
			strictIsoVlg.spacing = 6;
			strictIsoVlg.padding = new RectOffset(0, 0, 2, 4);
			strictIsoVlg.childAlignment = TextAnchor.UpperLeft;
			strictIsoVlg.childControlWidth = true;
			strictIsoVlg.childControlHeight = true;
			strictIsoVlg.childForceExpandWidth = true;
			strictIsoVlg.childForceExpandHeight = false;

			MakeBrushOptsSectionLabel(strictIsoRoot.transform, "Strict mask isolation (post-SD)");
			var strictFlipToggle = MakeBrushOptsCheckboxRow(strictIsoRoot.transform, "StrictIsolationFlipRow",
				"Invert mask (keep init inside brush; SD outside)",
				PaintTab_StrictIsolationBrushOptions.FlipInvertIsolationMask,
				isOn =>
				{
					PaintTab_StrictIsolationBrushOptions.SetFlipInvertIsolationMask(isOn);
					Viewport_StatusText.instance?.ShowStatusText(
						isOn
							? "Strict isolation: inverted (init preserved in brush)"
							: "Strict isolation: default (init preserved outside brush)",
						false, 1.1f, false);
				});
			RegisterStrictIsolationBrushOptsHandlersForUi(strictFlipToggle, strictIsoRoot);

			MakeBrushOptsSectionLabel(panelGo.transform, "Mirror plane (mesh symmetry)");
			var planeRow = new GameObject("SymmetryPlaneRow");
			planeRow.transform.SetParent(panelGo.transform, false);
			planeRow.AddComponent<RectTransform>();
			var planeRowLe = planeRow.AddComponent<LayoutElement>();
			planeRowLe.minHeight = 32;
			var planeH = planeRow.AddComponent<HorizontalLayoutGroup>();
			planeH.spacing = 4;
			planeH.childAlignment = TextAnchor.MiddleLeft;
			planeH.childControlWidth = false;
			planeH.childControlHeight = true;
			planeH.childForceExpandWidth = false;
			var tgPlane = planeRow.AddComponent<ToggleGroup>();
			tgPlane.allowSwitchOff = false;
			// UI order ≠ enum numeric order (FacePick=2, ObjectLocal=3); map explicitly.
			var symPlaneRowOrder = new[]
			{
				PaintSymmetryPlaneSource.Auto,
				PaintSymmetryPlaneSource.ViewAligned,
				PaintSymmetryPlaneSource.ObjectLocal,
				PaintSymmetryPlaneSource.FacePick,
			};
			var planeToggles = new Toggle[4];
			string[] planeLabels = { "Auto", "View", "Mesh", "Face" };
			for (int i = 0; i < 4; i++)
			{
				PaintSymmetryPlaneSource planeSource = symPlaneRowOrder[i];
				string planeLabel = planeLabels[i];
				planeToggles[i] = MakeToolOptionsRadioToggle(planeRow.transform, tgPlane, planeLabel, radioOff, radioOn, () =>
				{
					var inst = BrushRibbon_UI_Size.instance;
					if (inst != null) inst.SetPaintSymmetryPlaneSource(planeSource);
					ShowToolFeedback("Mirror plane: " + planeLabel);
				});
			}

			var planeActionsRow = new GameObject("SymmetryPlaneActions");
			planeActionsRow.transform.SetParent(panelGo.transform, false);
			planeActionsRow.AddComponent<RectTransform>();
			var planeActLe = planeActionsRow.AddComponent<LayoutElement>();
			planeActLe.minHeight = 32;
			var planeActH = planeActionsRow.AddComponent<HorizontalLayoutGroup>();
			planeActH.spacing = 4;
			planeActH.childAlignment = TextAnchor.MiddleLeft;
			planeActH.childControlWidth = false;
			planeActH.childControlHeight = true;
			planeActH.childForceExpandWidth = false;
			MakeBrushOptsActionButton(planeActionsRow.transform, "Pick @ cursor", () =>
			{
				TryPickSymmetryPlaneUnderCursor();
				SyncBrushToolRadiosFromSize();
			}, 108);
			MakeBrushOptsActionButton(planeActionsRow.transform, "Flip", () =>
			{
				var inst = BrushRibbon_UI_Size.instance;
				if (inst == null) return;
				if (inst.paintSymmetryPlaneSource == PaintSymmetryPlaneSource.FacePick)
				{
					inst.FlipPickedSymmetryPlaneNormal();
					ShowToolFeedback("Mirror plane: flipped face normal");
				}
				else if (inst.paintSymmetryPlaneSource == PaintSymmetryPlaneSource.ObjectLocal)
				{
					inst.FlipSymmetryObjectLocalSign();
					ShowToolFeedback("Mirror plane: flipped mesh lateral axis");
				}
				else
					ShowToolFeedback("Mirror plane: use Mesh or Face mode to flip");
			}, 72);

			if (pinCollapseToToolViewport)
			{
				var spacerGo = new GameObject("BrushOptsBottomSpacer");
				spacerGo.transform.SetParent(panelGo.transform, false);
				spacerGo.AddComponent<RectTransform>();
				var spacerLe = spacerGo.AddComponent<LayoutElement>();
				spacerLe.minHeight = 40;
				spacerLe.preferredHeight = 40;
			}

			// Slight transparency so scrolling content remains visible under the pinned collapse bar (viewport overlay).
			const float collapseBarA = 0.78f;
			const float collapseBarHoverA = 0.86f;
			const float collapseBarPressA = 0.74f;

			var collapseBtnGo = new GameObject("BrushOptsCollapseBtn");
			collapseBtnGo.AddComponent<RectTransform>();
			var collapseImg = collapseBtnGo.AddComponent<Image>();
			collapseImg.raycastTarget = true;
			var collapseBtn = collapseBtnGo.AddComponent<Button>();
			var collapseColors = collapseBtn.colors;
			collapseColors.normalColor = new Color(0.22f, 0.28f, 0.36f, collapseBarA);
			collapseColors.highlightedColor = new Color(0.30f, 0.36f, 0.44f, collapseBarHoverA);
			collapseColors.pressedColor = new Color(0.17f, 0.22f, 0.30f, collapseBarPressA);
			collapseColors.selectedColor = collapseColors.normalColor;
			collapseColors.disabledColor = new Color(0.22f, 0.28f, 0.36f, 0.45f);
			collapseBtn.colors = collapseColors;
			collapseImg.color = collapseColors.normalColor;
			var collapseTxtGo = new GameObject("Label");
			collapseTxtGo.transform.SetParent(collapseBtnGo.transform, false);
			var collapseTxtRt = collapseTxtGo.AddComponent<RectTransform>();
			collapseTxtRt.anchorMin = Vector2.zero;
			collapseTxtRt.anchorMax = Vector2.one;
			collapseTxtRt.offsetMin = new Vector2(4, 0);
			collapseTxtRt.offsetMax = new Vector2(-4, 0);
			var collapseTxt = collapseTxtGo.AddComponent<TextMeshProUGUI>();
			collapseTxt.color = Color.white;
			StylePaintTabTmp(collapseTxt, "Collapse ▲", kPaintTabUiFontSize, TextAlignmentOptions.Center);

			var collapseRt = collapseBtnGo.GetComponent<RectTransform>();
			if (pinCollapseToToolViewport)
			{
				collapseBtnGo.transform.SetParent(toolOptsScroll.viewport, false);
				collapseRt.anchorMin = new Vector2(0f, 0f);
				collapseRt.anchorMax = new Vector2(1f, 0f);
				collapseRt.pivot = new Vector2(0.5f, 0f);
				collapseRt.anchoredPosition = Vector2.zero;
				collapseRt.offsetMin = new Vector2(6f, 4f);
				collapseRt.offsetMax = new Vector2(-6f, 4f + 30f);
				collapseBtnGo.SetActive(false);
				collapseBtnGo.transform.SetAsLastSibling();
			}
			else
			{
				collapseBtnGo.transform.SetParent(panelGo.transform, false);
				var collapseLe = collapseBtnGo.AddComponent<LayoutElement>();
				collapseLe.minHeight = 30;
				collapseLe.preferredHeight = 30;
			}

			void ApplyRadioRowTint(Toggle[] group)
			{
				foreach (var t in group)
				{
					if (t == null) continue;
					var img = t.targetGraphic as Image;
					if (img != null) img.color = PaintToolFaceColor(t.isOn, radioOn, radioOff);
				}
			}

			void SyncBrushToolRadiosFromSize()
			{
				var inst = BrushRibbon_UI_Size.instance;
				int sm = inst != null ? (int)inst.scatterMode : 0;
				int am = inst != null ? (int)inst.tipAngleMode : 0;
				PaintSymmetryPlaneSource ps = inst != null ? inst.paintSymmetryPlaneSource : PaintSymmetryPlaneSource.Auto;
				int pm = 0;
				for (int i = 0; i < symPlaneRowOrder.Length; i++) {
					if (symPlaneRowOrder[i] == ps) {
						pm = i;
						break;
					}
				}
				for (int i = 0; i < scatterToggles.Length; i++)
					scatterToggles[i].SetIsOnWithoutNotify(i == sm);
				for (int i = 0; i < angleToggles.Length; i++)
					angleToggles[i].SetIsOnWithoutNotify(i == am);
				for (int i = 0; i < planeToggles.Length; i++)
					planeToggles[i].SetIsOnWithoutNotify(i == pm);
				ApplyRadioRowTint(scatterToggles);
				ApplyRadioRowTint(angleToggles);
				ApplyRadioRowTint(planeToggles);
			}

			void OnBrushSettingsMaybeSync()
			{
				if (panelGo.activeSelf) SyncBrushToolRadiosFromSize();
			}

			_cachedBrushOptsOnSettingsChanged = OnBrushSettingsMaybeSync;
			RegisterBrushSettingsHandler(OnBrushSettingsMaybeSync);

			headerBtn.onClick.AddListener(() =>
			{
				bool open = !panelGo.activeSelf;
				if (open)
					CloseValueAssistPanel(toolSectionParent);
				panelGo.SetActive(open);
				if (pinCollapseToToolViewport)
					collapseBtnGo.SetActive(open);
				panelLe.preferredHeight = open ? -1f : 0f;
				headerTxt.text = open ? "Brush options ▴" : "Brush options ▼";
				SyncToolOptionsRowModalBlockForSection(toolSectionParent);
				if (open) SyncBrushToolRadiosFromSize();
				if (open) SyncSmudgeBrushOptsVisibilityForRoot(smudgeOptsRoot);
				if (open)
				{
					LayoutRebuilder.ForceRebuildLayoutImmediate(toolSectionParent);
					Canvas.ForceUpdateCanvases();
					var sr = toolSectionParent.GetComponentInParent<ScrollRect>();
					if (sr != null)
					{
						if (SmudgeBrushOptsShouldShowForScroll() && smudgeOptsRoot.activeSelf)
						{
							var host = headerGo.GetComponentInParent<PaintTab_CollectPaintUI>();
							var smudgeRt = smudgeOptsRoot.GetComponent<RectTransform>();
							if (host != null && smudgeRt != null && host.isActiveAndEnabled)
								host.StartCoroutine(host.CoScrollSmudgeBlockIntoViewAfterOpen(sr, smudgeRt, toolSectionParent));
							else
								ScrollRectVerticalClampChildVisible(sr, smudgeRt);
						}
						else
							sr.verticalNormalizedPosition = 0f;
					}
					if (pinCollapseToToolViewport)
						collapseBtnGo.transform.SetAsLastSibling();
				}
			});

			collapseBtn.onClick.AddListener(() =>
			{
				panelGo.SetActive(false);
				if (pinCollapseToToolViewport)
					collapseBtnGo.SetActive(false);
				panelLe.preferredHeight = 0f;
				headerTxt.text = "Brush options ▼";
				SyncToolOptionsRowModalBlockForSection(toolSectionParent);
				LayoutRebuilder.ForceRebuildLayoutImmediate(toolSectionParent);
			});

			SyncBrushToolRadiosFromSize();
		}

		static void TryPickSymmetryPlaneUnderCursor()
		{
			var cam = UserCameras_MGR.instance?._curr_viewCamera?.myCamera;
			var mv = MainViewport_UI.instance;
			var sz = BrushRibbon_UI_Size.instance;
			if (cam == null || mv == null || sz == null)
			{
				ShowToolFeedback("Mirror plane: need viewport & brush UI");
				return;
			}
			Vector2 uv = mv.cursorMainViewportPos01;
			if (!PaintSymmetryMesh.TryPreferredRaycast(cam, uv, out RaycastHit hit))
			{
				ShowToolFeedback("Mirror plane: nothing hit under cursor");
				return;
			}
			sz.ApplySymmetryPlaneFromFaceHit(hit);
			ShowToolFeedback("Mirror plane: face under cursor");
		}

		static void MakeBrushOptsActionButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, int minWidth)
		{
			var go = new GameObject("Btn_" + label.Replace(" ", "").Replace("@", ""));
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minHeight = 28;
			le.preferredHeight = 28;
			le.minWidth = minWidth;
			le.preferredWidth = minWidth;
			var img = go.AddComponent<Image>();
			img.color = new Color(0.28f, 0.32f, 0.38f, 1f);
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();
			btn.onClick.AddListener(onClick);
			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var tr = txtGo.AddComponent<RectTransform>();
			tr.anchorMin = Vector2.zero;
			tr.anchorMax = Vector2.one;
			tr.offsetMin = new Vector2(4, 0);
			tr.offsetMax = new Vector2(-4, 0);
			var tmp = txtGo.AddComponent<TextMeshProUGUI>();
			tmp.color = Color.white;
			StylePaintTabTmp(tmp, label, kPaintTabUiFontSize, TextAlignmentOptions.Center);
		}

		static void MakeBrushOptsSectionLabel(Transform parent, string text)
		{
			var go = new GameObject("BrushOptsLbl");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var le = go.AddComponent<LayoutElement>();
			le.minHeight = 24;
			var tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.color = new Color(0.75f, 0.76f, 0.8f, 1f);
			StylePaintTabTmp(tmp, text, 9f, TextAlignmentOptions.Left);
		}

		static Toggle MakeToolOptionsRadioToggle(Transform rowParent, ToggleGroup group, string label, Color offCol, Color onCol, UnityEngine.Events.UnityAction onChosenWhenOn)
		{
			var go = new GameObject("Radio_" + label.Replace(" ", ""));
			go.transform.SetParent(rowParent, false);
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = label.Length >= 12 ? 120 : (label.Length > 5 ? 72 : 52);
			le.minHeight = 28;
			le.preferredHeight = 28;
			var img = go.AddComponent<Image>();
			img.color = PaintToolFaceColor(false, onCol, offCol);
			var toggle = go.AddComponent<Toggle>();
			toggle.targetGraphic = img;
			toggle.group = group;
			toggle.graphic = null;
			var cb = toggle.colors;
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(0.95f, 0.95f, 1f);
			cb.pressedColor = new Color(0.88f, 0.88f, 0.92f);
			cb.selectedColor = Color.white;
			toggle.colors = cb;
			toggle.onValueChanged.AddListener(isOn =>
			{
				img.color = PaintToolFaceColor(isOn, onCol, offCol);
				if (isOn && onChosenWhenOn != null) onChosenWhenOn();
			});

			var txtGo = new GameObject("Text");
			txtGo.transform.SetParent(go.transform, false);
			var tr = txtGo.AddComponent<RectTransform>();
			tr.anchorMin = Vector2.zero;
			tr.anchorMax = Vector2.one;
			tr.offsetMin = new Vector2(3, 1);
			tr.offsetMax = new Vector2(-3, -1);
			var tmp = txtGo.AddComponent<TextMeshProUGUI>();
			tmp.color = Color.white;
			StylePaintTabTmp(tmp, label, kPaintTabUiFontSize, TextAlignmentOptions.Center);
			return toggle;
		}

		/// <summary>Vertical mirror: duplicate brush at x&apos; = 1−x in viewport UV (inpaint, projection, background mask).</summary>
		static void MakePaintSymmetryToggle(Transform parent)
		{
			Color authoredOff = new Color(0.3f, 0.3f, 0.3f, 1f);
			Color authoredOn = new Color(0.38f, 0.26f, 0.52f, 1f);

			var go = new GameObject("Btn_PaintSymmetry");
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>();
			var img = go.AddComponent<Image>();
			img.color = PaintToolFaceColor(false, authoredOn, authoredOff);
			img.raycastTarget = true;
			var btn = go.AddComponent<Button>();

			var txtGo = new GameObject("Label");
			txtGo.transform.SetParent(go.transform, false);
			var txtRect = txtGo.AddComponent<RectTransform>();
			txtRect.anchorMin = Vector2.zero;
			txtRect.anchorMax = Vector2.one;
			txtRect.offsetMin = new Vector2(4, 0);
			txtRect.offsetMax = new Vector2(-4, 0);
			var txt = txtGo.AddComponent<TextMeshProUGUI>();
			txt.color = Color.white;
			ApplyPaintTabToolRowTmp(txt, TextAlignmentOptions.Center);

			void RefreshSymmetryButton()
			{
				bool on = BrushRibbon_UI_Size.GetPaintSymmetryXOn();
				img.color = PaintToolFaceColor(on, authoredOn, authoredOff);
				int szLine = kSymmetryOnOffSublineTmpSize;
				txt.text = on
					? $"Symmetry\n<size={szLine}>On</size>"
					: $"Symmetry\n<size={szLine}>Off</size>";
			}
			RefreshSymmetryButton();
			_cachedSymmetryOnSettingsChanged = RefreshSymmetryButton;
			RegisterBrushSettingsHandler(RefreshSymmetryButton);

			btn.onClick.AddListener(() =>
			{
				var sz = BrushRibbon_UI_Size.instance;
				if (sz == null)
				{
					ShowToolFeedback("Symmetry: open Paint tab / brush size UI first");
					return;
				}
				sz.SetPaintSymmetryXOn(!sz.paintSymmetryXOn);
				RefreshSymmetryButton();
				ShowToolFeedback(sz.paintSymmetryXOn
					? "Paint symmetry on (3D: mesh plane; 2D: screen)"
					: "Paint symmetry off");
			});
		}

		static void SyncDepthLimitButtonState(Transform toolRowTransform)
		{
			foreach (var btn in toolRowTransform.GetComponentsInChildren<Button>(true))
			{
				if (!btn.gameObject.name.Contains("Btn_DepthLimit")) continue;
				var img = btn.GetComponent<Image>();
				var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
				if (img == null || txt == null) continue;
				var ribbon = SD_WorkflowOptionsRibbon_UI.instance;
				bool isOn = ribbon != null && ribbon.brushDepthLimit01 > 0f;
				img.color = isOn ? new Color(0.2f, 0.55f, 0.35f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
				txt.text = isOn ? "Depth Limit\n<size=8>ON</size>" : "Depth Limit\n<size=8>OFF</size>";
				break;
			}
		}

		static void EnsureLayoutElement(RectTransform rect, float flexibleWidth)
		{
			if (rect == null) return;
			var le = rect.GetComponent<LayoutElement>();
			if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
			le.flexibleWidth = flexibleWidth;
		}
	}

	/// <summary>Stores design-time TMP point size so theme <c>font_scale</c> does not compound.</summary>
	sealed class PaintTab_ThemeDesignFontPt : MonoBehaviour {
		public float designPt = 12f;
	}
}
