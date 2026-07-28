using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Validated runtime theme tokens for add-on UI (colors + scale multipliers).
	/// Core UI may opt in by reading tokens or subscribing to <see cref="ThemeChanged"/>;
	/// this class never scans arbitrary scene UI.
	/// </summary>
	public static class SpzUiThemeOps {

		public const string DefaultThemeId = "stableprojectorz-default";
		public const string ThemeApiVersion = "1.18";
		const int kMaxThemeIdChars = 64;
		const int kMaxLabelChars = 128;
		const int kMaxRegisteredThemes = 32;
		public const float ScaleTokenMin = 0.75f;
		public const float ScaleTokenMax = 1.5f;
		public const float CornerRadiusMin = 0f;
		public const float CornerRadiusMax = 12f;
		public const float DefaultCornerRadius = 6f;
		public const float PanelWidthMin = 180f;
		public const float PanelWidthMax = 400f;
		public const float DefaultPanelWidth = 220f;
		public const float PanelAlphaMin = 0.5f;
		public const float PanelAlphaMax = 1f;
		public const float DefaultPanelAlpha = 1f;
		public const float RibbonIconOnlyMin = 0f;
		public const float RibbonIconOnlyMax = 1f;
		public const float DefaultRibbonIconOnly = 0f;

		const string PrefsActiveThemeId = "SpzUiTheme.ActiveThemeId";
		const string PrefsActiveTokensJson = "SpzUiTheme.ActiveTokensJson";
		static bool _suppressPersist;

		static readonly string[] ReservedTokenNames = { };

		sealed class ThemePreset {
			public string id;
			public string label;
			public string owner;
			public ThemeTokens tokens;
		}

		public sealed class ThemeTokens {
			public Color panelBg;
			public Color controlBg;
			public Color fieldBg;
			public Color accent;
			public Color textPrimary;
			public Color textMuted;
			public Color handle;
			public Color success;
			public Color danger;
			public Color border;
			public Color tabActive;
			public Color selection;
			public Color iconTint;
			public float fontScale = 1f;
			public float spacingScale = 1f;
			public float cornerRadius = DefaultCornerRadius;
			public float panelWidth = DefaultPanelWidth;
			public float panelAlpha = DefaultPanelAlpha;
			public float ribbonIconOnly = DefaultRibbonIconOnly;

			public ThemeTokens Clone() {
				return (ThemeTokens)MemberwiseClone();
			}
		}

		static readonly ThemeTokens Defaults = new ThemeTokens {
			panelBg = new Color(0.2f, 0.2f, 0.2f, 0.8f),
			controlBg = new Color(0.3f, 0.3f, 0.3f, 1f),
			fieldBg = new Color(0.15f, 0.15f, 0.15f, 1f),
			accent = new Color(0.3f, 0.6f, 1f, 1f),
			textPrimary = Color.white,
			textMuted = new Color(0.5f, 0.5f, 0.5f, 1f),
			handle = Color.white,
			success = new Color(0.133f, 0.773f, 0.369f, 1f),   // #22C55E
			danger = new Color(0.937f, 0.267f, 0.267f, 1f),    // #EF4444
			border = new Color(1f, 1f, 1f, 0.08f),             // #FFFFFF14
			tabActive = new Color(0.329f, 0.329f, 0.329f, 1f), // #545454
			selection = new Color(0.231f, 0.510f, 0.965f, 1f), // #3B82F6
			iconTint = new Color(0.5f, 0.5f, 0.5f, 1f),
			fontScale = 1f,
			spacingScale = 1f,
			cornerRadius = DefaultCornerRadius,
			panelWidth = DefaultPanelWidth,
			panelAlpha = DefaultPanelAlpha,
			ribbonIconOnly = DefaultRibbonIconOnly,
		};

		static ThemeTokens _active = Defaults.Clone();
		static string _activeThemeId = DefaultThemeId;
		static readonly Dictionary<string, ThemePreset> RegisteredThemes =
			new Dictionary<string, ThemePreset>(StringComparer.Ordinal);

		public static event Action ThemeChanged;

		public static string ActiveThemeId => _activeThemeId;
		public static ThemeTokens Active => _active.Clone();

		/// <summary>True when the builtin SPZ palette id is active.</summary>
		public static bool IsBuiltinDefaultActive =>
			string.Equals(_activeThemeId, DefaultThemeId, StringComparison.Ordinal);

		/// <summary>
		/// Bound core chrome may retint only when a non-builtin theme is active.
		/// Authored SPZ colors stay until Nomad/custom Apply — addon theme vs permanent UI boundary.
		/// <para><b>Silo contract:</b> every chrome mutator must no-op or restore when this is false;
		/// snapshot authored fields <i>before</i> first Nomad write; leave via <see cref="RestoreBoundChromeUnder"/>.
		/// Regression: <c>NomadThemeSiloContractTests</c> + <c>.cursor/rules/nomad-theme-silo.mdc</c>.</para>
		/// </summary>
		public static bool ShouldRecolorBoundChrome => !IsBuiltinDefaultActive;

		/// <summary>True when <c>ribbon_icon_only</c> ≥ 0.5 (CommandRibbon strip hides labels, enlarges line icons).</summary>
		public static bool RibbonIconOnlyActive =>
			_active.ribbonIconOnly >= 0.5f;

		static readonly Dictionary<int, Color> AuthoredGraphicColors =
			new Dictionary<int, Color>();
		static readonly Dictionary<int, ColorBlock> AuthoredColorBlocks =
			new Dictionary<int, ColorBlock>();
		static readonly Dictionary<int, float> AuthoredPixelsPerUnit =
			new Dictionary<int, float>();

		static void SnapshotAuthoredGraphic(Graphic graphic) {
			if (graphic == null) return;
			int id = graphic.GetInstanceID();
			if (!AuthoredGraphicColors.ContainsKey(id))
				AuthoredGraphicColors[id] = graphic.color;
		}

		static void SnapshotAuthoredPixelsPerUnit(Image image) {
			if (image == null) return;
			int id = image.GetInstanceID();
			if (!AuthoredPixelsPerUnit.ContainsKey(id))
				AuthoredPixelsPerUnit[id] = image.pixelsPerUnitMultiplier;
		}

		/// <summary>Snapshots Selectable ColorBlock once so Restore SPZ can unwind accent hover/press tints.</summary>
		public static void SnapshotAuthoredColorBlock(Selectable selectable) {
			if (selectable == null) return;
			int id = selectable.GetInstanceID();
			if (!AuthoredColorBlocks.ContainsKey(id))
				AuthoredColorBlocks[id] = selectable.colors;
		}

		/// <summary>Restores a graphic's pre-theme color when snapshotted; no-op otherwise.</summary>
		public static void RestoreAuthoredGraphic(Graphic graphic) {
			if (graphic == null) return;
			if (AuthoredGraphicColors.TryGetValue(graphic.GetInstanceID(), out Color c))
				graphic.color = c;
			if (graphic is Image img && AuthoredPixelsPerUnit.TryGetValue(img.GetInstanceID(), out float ppu))
				img.pixelsPerUnitMultiplier = ppu;
			if (graphic is TMP_Text tmp)
				RestoreNomadTypography(tmp);
		}

		/// <summary>Restores Selectable ColorBlock snapshotted before Nomad accent tinting.</summary>
		public static void RestoreAuthoredColorBlock(Selectable selectable) {
			if (selectable == null) return;
			if (AuthoredColorBlocks.TryGetValue(selectable.GetInstanceID(), out ColorBlock block))
				selectable.colors = block;
		}

		/// <summary>
		/// Full BoundChrome unwind under a root (line icons, rounded/flat sprites, colors, ColorBlocks, slider thumbs).
		/// Call from ThemeChanged leave paths so Restore SPZ does not leave Nomad holdovers.
		/// </summary>
		public static void RestoreBoundChromeUnder(Transform root) {
			if (root == null) return;
			RestoreControlLineIconsUnder(root);
			RestoreRoundedControlSpritesUnder(root);
			RestoreSliderHandleLayoutsUnder(root);
			RestoreToolFaceLayoutsUnder(root);
			RestorePanelWidthsUnder(root);
			foreach (var g in root.GetComponentsInChildren<Graphic>(true))
				RestoreAuthoredGraphic(g);
			foreach (var s in root.GetComponentsInChildren<Selectable>(true))
				RestoreAuthoredColorBlock(s);
			foreach (var tmp in root.GetComponentsInChildren<TMP_Text>(true)) {
				if (tmp == null) continue;
				var fontTag = tmp.GetComponent<SpzUiThemeDesignFontPt>();
				if (fontTag != null && fontTag.designPt > 0.05f)
					tmp.fontSize = fontTag.designPt;
			}
		}

		/// <summary>
		/// Settings-style checkbox: flat face + keep Toggle.graphic glyph (tinted success).
		/// Use for menus / ON-OFF rows. Tool cells that use bevel plates as selection should hide separately.
		/// </summary>
		public static void ThemeCheckboxToggle(Toggle toggle, Color face, Color accent, Color checkSuccess) {
			if (toggle == null || toggle.targetGraphic == null)
				return;
			ApplyBoundChromeSelectable(toggle, face, accent);
			if (toggle.graphic != null) {
				// Solid-square name-hide can disable a Checkmark child before we tint it — force ON glyph back.
				toggle.graphic.enabled = true;
				var hidden = toggle.graphic.GetComponent<SpzUiThemeHiddenGraphic>();
				if (hidden != null) {
					if (Application.isPlaying)
						UnityEngine.Object.Destroy(hidden);
					else
						UnityEngine.Object.DestroyImmediate(hidden);
				}
				ApplyBoundChromeGraphic(toggle.graphic, checkSuccess);
			}
		}

		/// <summary>
		/// Soft/solid fill stretched edge-to-edge; snapshots RectTransform for Restore SPZ.
		/// Pair with solid-square chrome (<see cref="ApplySolidSquareChrome"/> /
		/// <see cref="ApplyRoundedControlSprite"/>).
		/// </summary>
		public static void FlattenToolFaceImage(Image img) {
			if (img == null || !ShouldRecolorBoundChrome) return;
			if (IsUiMaskGraphic(img)) return;
			if (img.type == Image.Type.Filled) return;
			img.preserveAspect = false;
			img.pixelsPerUnitMultiplier = 1f;
			if (UiRuntimeSprites.IsCachedRoundedRect(img.sprite))
				img.type = Image.Type.Sliced;
			else if (UiRuntimeSprites.IsSolidRect(img.sprite))
				img.type = Image.Type.Simple;
			var rt = img.rectTransform;
			if (rt == null || !(rt.parent is RectTransform)) return;
			SnapshotToolFaceLayout(rt);
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.anchoredPosition = Vector2.zero;
			rt.sizeDelta = Vector2.zero;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			rt.localScale = Vector3.one;
		}

		static void SnapshotToolFaceLayout(RectTransform rt) {
			if (rt == null) return;
			var tag = rt.GetComponent<SpzUiThemeDesignRectTransform>();
			if (tag == null) {
				tag = rt.gameObject.AddComponent<SpzUiThemeDesignRectTransform>();
				CaptureRectTransform(tag, rt);
			}
			else if (!tag.hasSnapshot) {
				CaptureRectTransform(tag, rt);
			}
		}

		static void CaptureRectTransform(SpzUiThemeDesignRectTransform tag, RectTransform rt) {
			tag.anchorMin = rt.anchorMin;
			tag.anchorMax = rt.anchorMax;
			tag.pivot = rt.pivot;
			tag.anchoredPosition = rt.anchoredPosition;
			tag.sizeDelta = rt.sizeDelta;
			tag.offsetMin = rt.offsetMin;
			tag.offsetMax = rt.offsetMax;
			tag.localScale = rt.localScale;
			tag.hasSnapshot = true;
		}

		static void RestoreToolFaceLayoutsUnder(Transform root) {
			if (root == null) return;
			var tags = root.GetComponentsInChildren<SpzUiThemeDesignRectTransform>(true);
			for (int i = 0; i < tags.Length; i++) {
				var tag = tags[i];
				if (tag == null) continue;
				var rt = tag.transform as RectTransform;
				if (rt != null && tag.hasSnapshot) {
					rt.anchorMin = tag.anchorMin;
					rt.anchorMax = tag.anchorMax;
					rt.pivot = tag.pivot;
					rt.anchoredPosition = tag.anchoredPosition;
					rt.sizeDelta = tag.sizeDelta;
					rt.offsetMin = tag.offsetMin;
					rt.offsetMax = tag.offsetMax;
					rt.localScale = tag.localScale;
				}
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(tag);
				else
					UnityEngine.Object.DestroyImmediate(tag);
			}
		}

		/// <summary>
		/// Applies a chrome token color only when <see cref="ShouldRecolorBoundChrome"/>;
		/// otherwise restores the authored snapshot (if any).
		/// Under Nomad, 9-slice chrome faces are flattened — but never Toggle checkmark faces
		/// (Settings ON/OFF success sprites must keep their authored glyph).
		/// </summary>
		public static void ApplyBoundChromeGraphic(Graphic graphic, Color token) {
			if (graphic == null) return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(graphic);
				return;
			}
			// Mask sprites define clip shape (and often showMaskGraphic) — tint/flatten breaks Restore SPZ.
			if (graphic is Image maskImg && IsUiMaskGraphic(maskImg))
				return;
			SnapshotAuthoredGraphic(graphic);
			graphic.color = token;
			if (graphic is Image img && !IsToggleCheckmarkGraphic(img))
				FlattenSlicedChromeFace(img);
		}

		/// <summary>
		/// Selectable chrome apply gated by <see cref="ShouldRecolorBoundChrome"/>.
		/// Litmus expanded: hard opaque solid squares (SAVE 2K pattern) — no soft 9-slice / whiskers.
		/// Does not hide Toggle.graphic checkmarks — Multiview POV bevel plates hide via name match.
		/// </summary>
		public static void ApplyBoundChromeSelectable(Selectable selectable, Color normal, Color accent) {
			if (selectable == null || selectable.targetGraphic == null)
				return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(selectable.targetGraphic);
				RestoreAuthoredColorBlock(selectable);
				return;
			}
			ApplySolidSquareChrome(selectable, normal, accent);
		}

		/// <summary>True when <paramref name="img"/> is a Toggle's ON-state graphic (checkmark / tick plate).</summary>
		public static bool IsToggleCheckmarkGraphic(Image img) {
			if (img == null) return false;
			// includeInactive: theme apply often runs while panels/prefabs are inactive.
			var toggle = img.GetComponentInParent<Toggle>(true);
			return toggle != null && ReferenceEquals(toggle.graphic, img);
		}

		/// <summary>
		/// True when <paramref name="img"/> drives a <see cref="Mask"/> (often with <c>showMaskGraphic</c>).
		/// Solid-square / PPU rewrites turn soft mask sprites into white capsule artifacts after Restore SPZ
		/// (workflow mode strip, hardness dial).
		/// </summary>
		public static bool IsUiMaskGraphic(Image img) {
			return img != null && img.GetComponent<Mask>() != null;
		}

		/// <summary>
		/// Converts authored <see cref="Image.Type.Sliced"/> chrome to a flat solid square.
		/// Skips Nomad slider segment tiles, Toggle checkmarks, UI Mask sprites, and <see cref="Image.Type.Filled"/>
		/// radial dials (CircleSlider) — flattening those into SolidRect causes overlay soup.
		/// </summary>
		public static void FlattenSlicedChromeFace(Image image) {
			if (image == null || !ShouldRecolorBoundChrome)
				return;
			if (IsToggleCheckmarkGraphic(image))
				return;
			if (IsUiMaskGraphic(image))
				return;
			if (image.type == Image.Type.Filled)
				return;
			if (UiRuntimeSprites.IsNomadSliderSegmentTile(image.sprite))
				return;
			if (image.type != Image.Type.Sliced)
				return;
			ApplyRoundedControlSprite(image, markEligible: true);
		}

		/// <summary>
		/// TMP color/scale apply gated by <see cref="ShouldRecolorBoundChrome"/>.
		/// Non-builtin themes also apply Nomad-style tracking (open character spacing).
		/// </summary>
		public static void ApplyBoundChromeTmp(TMP_Text text, Color token, float fallbackBasePt = 14f) {
			if (text == null) return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(text);
				RestoreDesignFontSize(text, fallbackBasePt);
				return;
			}
			SnapshotAuthoredGraphic(text);
			SnapshotNomadTypography(text);
			ApplyTmpScaledCaptured(text, token, fallbackBasePt);
			ApplyNomadTypographyMetrics(text);
			ClearLabelRaycastIfUnderSelectable(text);
		}

		/// <summary>
		/// Compact vertical strip labels (workflow modes: PROJ MASK, COLOR, …).
		/// Uppercase + open tracking + eased line stack + soft fringe — not SPZ lowercase crush.
		/// </summary>
		public static void ApplyBoundChromeStripLabelTmp(TMP_Text text, Color token, float fallbackBasePt = 12f) {
			if (text == null) return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(text);
				RestoreDesignFontSize(text, fallbackBasePt);
				return;
			}
			SnapshotAuthoredGraphic(text);
			SnapshotNomadTypography(text);
			ApplyTmpScaledCaptured(text, token, fallbackBasePt);
			ApplyNomadStripLabelMetrics(text);
			ClearLabelRaycastIfUnderSelectable(text);
		}

		/// <summary>
		/// Tool/mode labels must not steal EventSystem hits from their parent Button/Toggle.
		/// Skips TMP_InputField text (needs raycasts for caret/selection).
		/// </summary>
		static void ClearLabelRaycastIfUnderSelectable(TMP_Text text) {
			if (text == null) return;
			if (text.GetComponentInParent<TMP_InputField>(true) != null)
				return;
			if (text.GetComponentInParent<Selectable>(true) == null)
				return;
			text.raycastTarget = false;
		}

		/// <summary>
		/// Unwinds <c>font_scale</c> by restoring the once-captured design point size.
		/// Leave paths that only restored color left TMP stuck at the scaled size.
		/// </summary>
		static void RestoreDesignFontSize(TMP_Text text, float fallbackBasePt) {
			if (text == null) return;
			var tag = text.GetComponent<SpzUiThemeDesignFontPt>();
			if (tag != null && tag.designPt > 0.05f) {
				text.fontSize = tag.designPt;
				return;
			}
			if (fallbackBasePt > 0.05f && Mathf.Abs(_active.fontScale - 1f) < 0.001f)
				text.fontSize = fallbackBasePt;
		}

		const string ControlLineIconChildName = "MonolithLineIcon";
		const float NomadLabelCharacterSpacing = 10f;
		const float NomadStripLabelCharacterSpacing = 18f;
		const float NomadStripLabelLineSpacing = -8f;

		const string RobotoRegularSdfAssetPath = "Assets/_gm/Art/Fonts/ENG - Roboto-Regular SDF.asset";
		static TMP_FontAsset _cachedNomadUiFont;

		/// <summary>Roboto Regular SDF when present (Nomad theme type); else TMP default.</summary>
		public static TMP_FontAsset ResolveNomadUiFont() {
			if (_cachedNomadUiFont != null)
				return _cachedNomadUiFont;
#if UNITY_EDITOR
			_cachedNomadUiFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(RobotoRegularSdfAssetPath);
#endif
			if (_cachedNomadUiFont == null) {
				var loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
				for (int i = 0; i < loaded.Length; i++) {
					var f = loaded[i];
					if (f == null || string.IsNullOrEmpty(f.name)) continue;
					string n = f.name;
					if (n.IndexOf("Roboto", StringComparison.OrdinalIgnoreCase) < 0) continue;
					if (n.IndexOf("Regular", StringComparison.OrdinalIgnoreCase) < 0) continue;
					_cachedNomadUiFont = f;
					break;
				}
			}
			if (_cachedNomadUiFont == null)
				_cachedNomadUiFont = TMP_Settings.defaultFontAsset;
			return _cachedNomadUiFont;
		}

		/// <summary>Editor/tests: drop cached Roboto resolve so AssetDatabase path can be re-probed.</summary>
		public static void ClearNomadUiFontCache() => _cachedNomadUiFont = null;

		static void SnapshotNomadTypography(TMP_Text text) {
			if (text == null) return;
			var tag = text.GetComponent<SpzUiThemeDesignTypography>();
			if (tag == null) {
				tag = text.gameObject.AddComponent<SpzUiThemeDesignTypography>();
				tag.characterSpacing = text.characterSpacing;
				tag.lineSpacing = text.lineSpacing;
				tag.fontStyle = text.fontStyle;
				tag.outlineWidth = text.outlineWidth;
				tag.outlineColor = text.outlineColor;
				tag.alignment = text.alignment;
				tag.hasAlignmentSnapshot = true;
				tag.authoredFont = text.font;
				tag.authoredFontSharedMaterial = text.fontSharedMaterial;
				tag.hasFontSnapshot = true;
				tag.authoredRaycastTarget = text.raycastTarget;
				tag.hasRaycastSnapshot = true;
				tag.hasSnapshot = true;
			}
			else if (!tag.hasSnapshot) {
				tag.characterSpacing = text.characterSpacing;
				tag.lineSpacing = text.lineSpacing;
				tag.fontStyle = text.fontStyle;
				tag.outlineWidth = text.outlineWidth;
				tag.outlineColor = text.outlineColor;
				tag.hasSnapshot = true;
			}
			if (!tag.hasAlignmentSnapshot) {
				tag.alignment = text.alignment;
				tag.hasAlignmentSnapshot = true;
			}
			if (!tag.hasFontSnapshot) {
				tag.authoredFont = text.font;
				tag.authoredFontSharedMaterial = text.fontSharedMaterial;
				tag.hasFontSnapshot = true;
			}
			if (!tag.hasRaycastSnapshot) {
				tag.authoredRaycastTarget = text.raycastTarget;
				tag.hasRaycastSnapshot = true;
			}
		}

		static void RestoreNomadTypography(TMP_Text text) {
			if (text == null) return;
			var tag = text.GetComponent<SpzUiThemeDesignTypography>();
			if (tag == null) return;
			if (tag.hasSnapshot) {
				text.characterSpacing = tag.characterSpacing;
				text.lineSpacing = tag.lineSpacing;
				text.fontStyle = tag.fontStyle;
				TrySetNomadOutline(text, tag.outlineWidth, tag.outlineColor);
			}
			if (tag.hasAlignmentSnapshot)
				text.alignment = tag.alignment;
			if (tag.hasFontSnapshot) {
				text.font = tag.authoredFont;
				if (tag.authoredFontSharedMaterial != null)
					text.fontSharedMaterial = tag.authoredFontSharedMaterial;
			}
			if (tag.hasRaycastSnapshot)
				text.raycastTarget = tag.authoredRaycastTarget;
		}

		/// <summary>Swap BoundChrome TMP to Roboto Regular when Nomad theme is active.</summary>
		static void ApplyNomadUiFont(TMP_Text text) {
			if (text == null) return;
			var font = ResolveNomadUiFont();
			if (font == null) return;
			text.font = font;
			if (font.material != null)
				text.fontSharedMaterial = font.material;
		}

		/// <summary>Open tracking for sculpt-chrome labels + Roboto.</summary>
		static void ApplyNomadTypographyMetrics(TMP_Text text) {
			if (text == null) return;
			ApplyNomadUiFont(text);
			if (text.font == null) return;
			text.characterSpacing = NomadLabelCharacterSpacing;
			// Soft dark fringe so reverse-out type (DEP, etc.) stays legible on flat cells.
			TrySetNomadOutline(text, 0.18f, new Color(0.05f, 0.05f, 0.07f, 0.72f));
		}

		/// <summary>Workflow / compact vertical strip: Roboto + uppercase stack with open tracking.</summary>
		static void ApplyNomadStripLabelMetrics(TMP_Text text) {
			if (text == null) return;
			ApplyNomadUiFont(text);
			if (text.font == null) return;
			text.fontStyle = FontStyles.UpperCase;
			text.characterSpacing = NomadStripLabelCharacterSpacing;
			// Authored SPZ stacks use ~-17…-30; ease so letters breathe without overflowing the cell.
			if (text.lineSpacing < NomadStripLabelLineSpacing)
				text.lineSpacing = NomadStripLabelLineSpacing;
			TrySetNomadOutline(text, 0.22f, new Color(0.04f, 0.04f, 0.06f, 0.78f));
		}

		/// <summary>
		/// TMP outline needs a live font material; EditMode / freshly-created TMP can NRE in SetOutlineThickness.
		/// </summary>
		static void TrySetNomadOutline(TMP_Text text, float width, Color color) {
			if (text == null) return;
			if (text.fontSharedMaterial == null && (text.font == null || text.font.material == null))
				return;
			try {
				text.outlineWidth = width;
				text.outlineColor = color;
			}
			catch (Exception) {
				// Headless / incomplete TMP material — skip fringe; color+font still applied.
			}
		}

		/// <summary>
		/// Ensures a centered <c>MonolithLineIcon</c> under <paramref name="owner"/> and hides authored
		/// icon Images named icon/Icon. Restores when builtin default is active.
		/// </summary>
		public static void ApplyControlLineIcon(Transform owner, StudioLineIcon glyph, float sizePx = 22f) {
			ApplyControlLineIconAt(owner, glyph, sizePx, Vector2.zero);
		}

		/// <summary>Same as <see cref="ApplyControlLineIcon"/> with an explicit anchored position (icon-above-label cells).</summary>
		public static void ApplyControlLineIconAt(Transform owner, StudioLineIcon glyph, float sizePx, Vector2 anchoredPosition) {
			if (owner == null) return;
			// Transform.Find skips inactive children — must scan manually or leave→re-Apply duplicates icons.
			Transform iconT = FindDirectChildIncludingInactive(owner, ControlLineIconChildName);
			if (!ShouldRecolorBoundChrome) {
				if (iconT != null)
					iconT.gameObject.SetActive(false);
				RestoreHiddenAuthoredIconsUnder(owner);
				return;
			}
			HideAuthoredIconsUnder(owner);
			bool created = false;
			if (iconT == null) {
				var go = new GameObject(ControlLineIconChildName, typeof(RectTransform));
				go.transform.SetParent(owner, false);
				iconT = go.transform;
				var imgNew = go.AddComponent<Image>();
				imgNew.raycastTarget = false;
				imgNew.preserveAspect = true;
				created = true;
			}
			iconT.gameObject.SetActive(true);
			var rt = iconT as RectTransform;
			if (rt != null) {
				rt.anchorMin = new Vector2(0.5f, 0.5f);
				rt.anchorMax = new Vector2(0.5f, 0.5f);
				rt.pivot = new Vector2(0.5f, 0.5f);
				rt.anchoredPosition = anchoredPosition;
				rt.sizeDelta = new Vector2(sizePx, sizePx);
			}
			var icon = iconT.GetComponent<Image>();
			if (icon != null) {
				icon.sprite = UiRuntimeSprites.GetLineIcon(glyph);
				icon.enabled = true;
				ApplyLineIconTint(icon);
			}
			if (created)
				iconT.SetAsLastSibling();
		}

		/// <summary>
		/// Nomad sculpt strip cell: thin line icon upper-center, label band underneath (Roboto).
		/// Label RectTransforms are snapshotted for <see cref="RestoreBoundChromeUnder"/>.
		/// </summary>
		/// <param name="stripUppercase">True = workflow PROJ MASK style (uppercase stack); false = title-case tool names (Paint/Smudge).</param>
		public static void ApplyNomadStackedToolCell(
			Transform cell,
			StudioLineIcon glyph,
			Color labelColor,
			float iconPx = 20f,
			Func<TMP_Text, bool> includeLabel = null,
			bool stripUppercase = true) {
			if (cell == null) return;
			if (!ShouldRecolorBoundChrome) {
				// Leave path: hide Monolith icon, restore label rects + authored TMP (font/align).
				ApplyControlLineIcon(cell, glyph, iconPx);
				RestoreToolFaceLayoutsUnder(cell);
				foreach (var tmp in cell.GetComponentsInChildren<TMP_Text>(true)) {
					if (tmp == null) continue;
					if (includeLabel != null && !includeLabel(tmp))
						continue;
					RestoreAuthoredGraphic(tmp);
					RestoreDesignFontSize(tmp, stripUppercase ? 11f : 12f);
				}
				return;
			}
			float yLift = Mathf.Max(4f, iconPx * 0.28f);
			ApplyControlLineIconAt(cell, glyph, iconPx, new Vector2(0f, yLift));
			foreach (var tmp in cell.GetComponentsInChildren<TMP_Text>(true)) {
				if (tmp == null) continue;
				if (includeLabel != null && !includeLabel(tmp))
					continue;
				var lrt = tmp.rectTransform;
				if (lrt != null) {
					SnapshotToolFaceLayout(lrt);
					lrt.anchorMin = new Vector2(0.06f, 0.02f);
					lrt.anchorMax = new Vector2(0.94f, 0.40f);
					lrt.pivot = new Vector2(0.5f, 0.5f);
					lrt.anchoredPosition = Vector2.zero;
					lrt.offsetMin = Vector2.zero;
					lrt.offsetMax = Vector2.zero;
					lrt.sizeDelta = Vector2.zero;
				}
				// Snapshot authored alignment BEFORE forcing Center (otherwise restore keeps Nomad align).
				SnapshotNomadTypography(tmp);
				tmp.alignment = TextAlignmentOptions.Center;
				// Labels stretch over the face under Nomad — must not steal EventSystem hits from the Selectable.
				tmp.raycastTarget = false;
				if (stripUppercase)
					ApplyBoundChromeStripLabelTmp(tmp, labelColor, 11f);
				else
					ApplyBoundChromeTmp(tmp, labelColor, 12f);
			}
		}

		/// <summary>
		/// Like <see cref="Transform.Find(string)"/> for a direct child, but includes inactive objects
		/// (Unity's Find skips inactive children and would duplicate Monolith overlays on re-Apply).
		/// </summary>
		public static Transform FindDirectChildIncludingInactive(Transform parent, string childName) {
			if (parent == null || string.IsNullOrEmpty(childName)) return null;
			for (int i = 0; i < parent.childCount; i++) {
				Transform child = parent.GetChild(i);
				if (child != null && child.name == childName)
					return child;
			}
			return null;
		}

		/// <summary>Deactivates MonolithLineIcon children and restores authored icon Images under <paramref name="root"/>.</summary>
		public static void RestoreControlLineIconsUnder(Transform root) {
			if (root == null) return;
			foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
				if (t != null && t.name == ControlLineIconChildName)
					t.gameObject.SetActive(false);
			}
			RestoreHiddenAuthoredIconsUnder(root);
		}

		static void HideAuthoredIconsUnder(Transform owner) {
			if (owner == null) return;
			foreach (var img in owner.GetComponentsInChildren<Image>(true)) {
				if (img == null) continue;
				string n = img.gameObject.name ?? "";
				if (n == ControlLineIconChildName || n == "MonolithActiveBar")
					continue;
				// Real Toggle ON glyphs must stay (Settings / context menus); tool cells hide those explicitly.
				if (IsToggleCheckmarkGraphic(img))
					continue;
				// Never disable a Selectable's targetGraphic — dead clicks under Nomad (name may contain "Icon").
				if (IsSelectableTargetGraphic(img))
					continue;
				if (!IsAuthoredIconImageName(n))
					continue;
				var tag = img.GetComponent<SpzUiThemeHiddenGraphic>();
				if (tag == null) {
					tag = img.gameObject.AddComponent<SpzUiThemeHiddenGraphic>();
					tag.wasEnabled = img.enabled;
					tag.hasSnapshot = true;
				}
				else if (!tag.hasSnapshot) {
					tag.wasEnabled = img.enabled;
					tag.hasSnapshot = true;
				}
				img.enabled = false;
			}
		}

		/// <summary>True when <paramref name="img"/> is any ancestor Selectable's click/raycast face.</summary>
		public static bool IsSelectableTargetGraphic(Image img) {
			if (img == null) return false;
			var sel = img.GetComponentInParent<Selectable>(true);
			return sel != null && ReferenceEquals(sel.targetGraphic, img);
		}

		static void RestoreHiddenAuthoredIconsUnder(Transform root) {
			if (root == null) return;
			var tags = root.GetComponentsInChildren<SpzUiThemeHiddenGraphic>(true);
			for (int i = 0; i < tags.Length; i++) {
				var tag = tags[i];
				if (tag == null) continue;
				var g = tag.GetComponent<Graphic>();
				if (g != null && tag.hasSnapshot)
					g.enabled = tag.wasEnabled;
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(tag);
				else
					UnityEngine.Object.DestroyImmediate(tag);
			}
		}

		static bool IsAuthoredIconImageName(string name) {
			if (string.IsNullOrEmpty(name)) return false;
			if (name.IndexOf("Monolith", StringComparison.OrdinalIgnoreCase) >= 0)
				return false;
			// Prefer explicit icon object names. Avoid bare IndexOf("Icon") alone for *Button faces —
			// those are gated out via IsSelectableTargetGraphic; still tighten matching.
			if (name.Equals("icon", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("Icon", StringComparison.Ordinal)
				|| name.StartsWith("icon_", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("_icon", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("Icon", StringComparison.Ordinal))
				return true;
			// Compound child labels like "BrushIcon" / "Smudge Icon" — not "...IconButton" root faces.
			if (name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) < 0)
				return false;
			if (name.EndsWith("Button", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("Toggle", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("Selectable", StringComparison.OrdinalIgnoreCase))
				return false;
			return true;
		}

		/// <summary>
		/// Hides an authored Graphic under Nomad (tick / secondary chrome / launcher TMP) and snapshots for Restore SPZ.
		/// No-op on builtin default (silo).
		/// </summary>
		public static void HideAuthoredGraphicForTheme(Graphic graphic) {
			if (graphic == null || !ShouldRecolorBoundChrome) return;
			// Never disable a Selectable click/raycast face (dead UI under Nomad).
			if (graphic is Image img && IsSelectableTargetGraphic(img))
				return;
			var tag = graphic.GetComponent<SpzUiThemeHiddenGraphic>();
			if (tag == null) {
				tag = graphic.gameObject.AddComponent<SpzUiThemeHiddenGraphic>();
				tag.wasEnabled = graphic.enabled;
				tag.hasSnapshot = true;
			}
			else if (!tag.hasSnapshot) {
				tag.wasEnabled = graphic.enabled;
				tag.hasSnapshot = true;
			}
			graphic.enabled = false;
		}

		/// <summary>
		/// Nomad sculpt vertical slider: charcoal pill track, segmented coral fill, bullseye thumb.
		/// Horizontal sliders get pill + solid accent fill + handle tint only (no segment tile).
		/// </summary>
		public static void ApplyNomadSliderChrome(Slider slider) {
			if (slider == null) return;
			if (!ShouldRecolorBoundChrome) {
				RestoreBoundChromeUnder(slider.transform);
				return;
			}
			var t = _active;
			bool vertical = slider.direction == Slider.Direction.BottomToTop
				|| slider.direction == Slider.Direction.TopToBottom;

			Image bg = slider.targetGraphic as Image;
			if (bg == null)
				bg = slider.GetComponent<Image>();
			if (bg != null) {
				ApplyBoundChromeGraphic(bg, t.fieldBg);
				ApplyRoundedControlSprite(bg, markEligible: true);
			}

			if (slider.fillRect != null) {
				var fill = slider.fillRect.GetComponent<Image>();
				if (fill != null) {
					ApplyRoundedControlSprite(fill, markEligible: true);
					if (vertical) {
						fill.sprite = UiRuntimeSprites.NomadSliderSegmentTile;
						fill.type = Image.Type.Tiled;
						ApplyBoundChromeGraphic(fill, ResolveNomadSliderFillColor(t));
					}
					else {
						fill.sprite = UiRuntimeSprites.SolidRect;
						fill.type = Image.Type.Simple;
						ApplyBoundChromeGraphic(fill, t.accent);
					}
				}
			}

			if (slider.handleRect != null) {
				RestoreControlLineIconsUnder(slider.handleRect);
				var handle = slider.handleRect.GetComponent<Image>();
				if (handle != null) {
					ApplyRoundedControlSprite(handle, markEligible: true);
					if (vertical) {
						SnapshotSliderHandleLayout(slider.handleRect);
						handle.sprite = UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bullseye);
						handle.type = Image.Type.Simple;
						handle.preserveAspect = true;
						ApplyBoundChromeGraphic(handle, t.iconTint);
						var hrt = slider.handleRect;
						if (hrt != null) {
							float s = Mathf.Clamp(Mathf.Max(hrt.sizeDelta.x, hrt.sizeDelta.y), 18f, 28f);
							hrt.sizeDelta = new Vector2(s, s);
						}
					}
					else {
						ApplyBoundChromeGraphic(handle, t.handle);
					}
				}
			}
		}

		static void SnapshotSliderHandleLayout(RectTransform handleRect) {
			if (handleRect == null) return;
			var tag = handleRect.GetComponent<SpzUiThemeSliderHandleLayout>();
			if (tag == null) {
				tag = handleRect.gameObject.AddComponent<SpzUiThemeSliderHandleLayout>();
				tag.authoredSizeDelta = handleRect.sizeDelta;
				tag.hasSnapshot = true;
			}
			else if (!tag.hasSnapshot) {
				tag.authoredSizeDelta = handleRect.sizeDelta;
				tag.hasSnapshot = true;
			}
		}

		static void RestoreSliderHandleLayoutsUnder(Transform root) {
			if (root == null) return;
			var tags = root.GetComponentsInChildren<SpzUiThemeSliderHandleLayout>(true);
			for (int i = 0; i < tags.Length; i++) {
				var tag = tags[i];
				if (tag == null) continue;
				var rt = tag.transform as RectTransform;
				if (rt != null && tag.hasSnapshot)
					rt.sizeDelta = tag.authoredSizeDelta;
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(tag);
				else
					UnityEngine.Object.DestroyImmediate(tag);
			}
		}

		/// <summary>Muted coral fill (Nomad sculpt) — danger token darkened toward terracotta.</summary>
		public static Color ResolveNomadSliderFillColor(ThemeTokens tokens) {
			Color coral = new Color(0.72f, 0.38f, 0.34f, 1f);
			return Color.Lerp(tokens.danger, coral, 0.55f);
		}

		public static JObject GetThemeResult() {
			return new JObject {
				["success"] = true,
				["theme_id"] = _activeThemeId,
				["tokens"] = SerializeTokens(_active),
				["token_schema"] = BuildTokenSchema(),
				["reserved_token_names"] = new JArray(ReservedTokenNames),
				["surfaces"] = BuildSurfaces(),
				["addon_rpc_theme_version"] = ThemeApiVersion,
				["ui_scale_source"] = "chrome",
				["persistence"] = "player_prefs",
				["persisted_theme_id"] = PlayerPrefs.GetString(PrefsActiveThemeId, ""),
				["line_icons"] = ListLineIconNames(),
				["composes_with"] = new JArray {
					"spz.cmd.set_ui_scale",
					"spz.cmd.list_ui_targets",
					"spz.cmd.set_ui_target_active",
					"spz.cmd.set_skybox_color",
					"spz.cmd.set_editor_layout",
				},
			};
		}

		public static JObject ListThemesResult() {
			var themes = new JArray {
				SerializeCatalogEntry(DefaultThemeId, "StableProjectorz default", "builtin",
					string.Equals(_activeThemeId, DefaultThemeId, StringComparison.Ordinal)),
			};
			var ids = new List<string>(RegisteredThemes.Keys);
			ids.Sort(StringComparer.Ordinal);
			foreach (string id in ids) {
				ThemePreset preset = RegisteredThemes[id];
				themes.Add(SerializeCatalogEntry(preset.id, preset.label, "registered",
					string.Equals(_activeThemeId, preset.id, StringComparison.Ordinal), preset.owner));
			}

			bool activeKnown = string.Equals(_activeThemeId, DefaultThemeId, StringComparison.Ordinal)
				|| RegisteredThemes.ContainsKey(_activeThemeId);
			var result = new JObject {
				["success"] = true,
				["themes"] = themes,
				["active_theme_id"] = _activeThemeId,
				["registered_count"] = RegisteredThemes.Count,
			};
			if (!activeKnown)
				result["active_orphan"] = true;
			return result;
		}

		public static bool TryRegisterTheme(
			string themeId, string label, JObject tokenValues, string owner, out string error) {
			error = null;
			if (!TryNormalizeThemeId(themeId, out themeId, out error))
				return false;
			if (string.Equals(themeId, DefaultThemeId, StringComparison.Ordinal)) {
				error = $"Builtin theme '{DefaultThemeId}' cannot be replaced";
				return false;
			}

			label = label != null ? label.Trim() : "";
			if (label.Length == 0)
				label = themeId;
			if (label.Length > kMaxLabelChars) {
				error = $"label must contain at most {kMaxLabelChars} characters";
				return false;
			}
			owner = owner != null ? owner.Trim() : "";
			if (tokenValues == null || !tokenValues.HasValues) {
				error = "tokens must contain at least one supported token";
				return false;
			}

			var candidate = Defaults.Clone();
			if (!TryOverlayTokens(candidate, tokenValues, out error))
				return false;
			bool replacing = RegisteredThemes.ContainsKey(themeId);
			if (!replacing && RegisteredThemes.Count >= kMaxRegisteredThemes) {
				error = $"Registered theme cap is {kMaxRegisteredThemes}";
				return false;
			}

			RegisteredThemes[themeId] = new ThemePreset {
				id = themeId,
				label = label,
				owner = owner,
				tokens = candidate,
			};
			return true;
		}

		public static bool TryUnregisterTheme(string themeId, out string error) {
			error = null;
			if (!TryNormalizeThemeId(themeId, out themeId, out error))
				return false;
			if (string.Equals(themeId, DefaultThemeId, StringComparison.Ordinal)) {
				error = $"Builtin theme '{DefaultThemeId}' cannot be removed";
				return false;
			}
			if (!RegisteredThemes.Remove(themeId)) {
				error = $"Registered theme not found: {themeId}";
				return false;
			}
			// Active id may become an orphan until reset_theme; callers that own the preset
			// (e.g. NomadThemeSPZ unload) must ResetTheme before unregister when desired.
			return true;
		}

		/// <summary>
		/// Foundation-compatible overload: token bodies replace from built-in defaults.
		/// </summary>
		public static bool TryApplyTheme(string themeId, JObject tokenValues, out string error) {
			return TryApplyTheme(themeId, tokenValues, "replace", out error);
		}

		/// <summary>
		/// Applies a token body or registered preset atomically using P1 forms A/B/C.
		/// </summary>
		public static bool TryApplyTheme(string themeId, JObject tokenValues, string mode, out string error) {
			error = null;
			if (!TryNormalizeThemeId(themeId, out themeId, out error))
				return false;
			mode = mode != null ? mode.Trim().ToLowerInvariant() : "replace";
			if (mode.Length == 0)
				mode = "replace";
			if (mode != "replace" && mode != "patch") {
				error = "mode must be 'replace' or 'patch'";
				return false;
			}

			bool hasTokens = tokenValues != null;
			if (hasTokens && !tokenValues.HasValues) {
				error = "tokens must contain at least one supported token";
				return false;
			}
			bool hasPreset = TryGetPresetTokens(themeId, out ThemeTokens presetTokens);
			if (!hasTokens && !hasPreset) {
				error = $"Unknown theme_id '{themeId}'; provide tokens or register the preset first";
				return false;
			}

			ThemeTokens candidate;
			if (hasPreset && hasTokens) {
				// First apply / replace: preset + overrides. Patch while that preset is already
				// active: keep runtime overrides (e.g. ribbon_icon_only) instead of rebuilding
				// from the registered snapshot.
				bool patchWhileActive = mode == "patch"
					&& string.Equals(_activeThemeId, themeId, StringComparison.Ordinal);
				candidate = patchWhileActive ? _active.Clone() : presetTokens.Clone();
			}
			else if (hasPreset)
				candidate = mode == "patch" ? _active.Clone() : presetTokens.Clone();
			else
				candidate = mode == "patch" ? _active.Clone() : Defaults.Clone();

			// A complete preset overlays the active base in form B patch. Form C already
			// selected the preset as its base, and mode intentionally does not alter it.
			if (hasPreset && !hasTokens && mode == "patch")
				OverlayTokens(candidate, presetTokens);
			if (hasTokens && !TryOverlayTokens(candidate, tokenValues, out error))
				return false;

			_activeThemeId = themeId;
			_active = candidate;
			NotifyThemeChanged();
			PersistActiveTheme();
			return true;
		}

		public static void ResetTheme() {
			_activeThemeId = DefaultThemeId;
			_active = Defaults.Clone();
			NotifyThemeChanged();
			ClearPersistedTheme();
		}

		/// <summary>
		/// Restores last applied theme from PlayerPrefs (token body). Returns false when nothing to restore.
		/// </summary>
		public static bool TryRestorePersistedTheme(out string detail) {
			detail = null;
			string themeId = PlayerPrefs.GetString(PrefsActiveThemeId, "");
			string tokensJson = PlayerPrefs.GetString(PrefsActiveTokensJson, "");
			if (string.IsNullOrEmpty(themeId)
			    || string.Equals(themeId, DefaultThemeId, StringComparison.Ordinal)) {
				detail = "no persisted non-default theme";
				return false;
			}
			if (string.IsNullOrEmpty(tokensJson)) {
				detail = "persisted theme id without token body";
				return false;
			}
			JObject tokens;
			try {
				tokens = JObject.Parse(tokensJson);
			} catch (Exception e) {
				detail = $"invalid persisted tokens: {e.Message}";
				return false;
			}
			_suppressPersist = true;
			try {
				// Re-register common add-on presets so list_themes stays honest after boot.
				if (string.Equals(themeId, "nomad-inspired", StringComparison.Ordinal))
					TryRegisterTheme(themeId, "Nomad inspired", tokens, "NomadThemeSPZ", out _);
				if (!TryApplyTheme(themeId, tokens, "replace", out string error)) {
					detail = error ?? "apply failed";
					return false;
				}
			} finally {
				_suppressPersist = false;
			}
			// Re-write prefs so persisted_theme_id stays aligned after a successful restore.
			PersistActiveTheme();
			detail = $"restored '{themeId}'";
			return true;
		}

		public static void ClearPersistedTheme() {
			if (_suppressPersist)
				return;
			PlayerPrefs.DeleteKey(PrefsActiveThemeId);
			PlayerPrefs.DeleteKey(PrefsActiveTokensJson);
			PlayerPrefs.Save();
		}

		static void PersistActiveTheme() {
			if (_suppressPersist)
				return;
			if (string.Equals(_activeThemeId, DefaultThemeId, StringComparison.Ordinal)) {
				ClearPersistedTheme();
				return;
			}
			PlayerPrefs.SetString(PrefsActiveThemeId, _activeThemeId);
			PlayerPrefs.SetString(PrefsActiveTokensJson, SerializeTokens(_active).ToString(Formatting.None));
			PlayerPrefs.Save();
		}

		static void NotifyThemeChanged() {
			var listeners = ThemeChanged;
			if (listeners == null)
				return;
			foreach (Action listener in listeners.GetInvocationList()) {
				try {
					listener();
				}
				catch (Exception e) {
					Debug.LogError($"[SpzUiThemeOps] Theme consumer failed: {e.Message}");
				}
			}
		}

		/// <summary>
		/// Styles only known add-on widget roles under a registered add-on root.
		/// Transparent dropdown row hit targets and unrelated prefab images are preserved.
		/// </summary>
		public static void ApplyToAddonUiRoot(GameObject root) {
			if (root == null)
				return;
			if (!ShouldRecolorBoundChrome) {
				RestoreBoundChromeUnder(root.transform);
				return;
			}
			foreach (var g in root.GetComponentsInChildren<Graphic>(true))
				SnapshotAuthoredGraphic(g);
			var tokens = _active;

			var rootImage = root.GetComponent<Image>();
			if (rootImage != null && root.name.StartsWith("AddonPanel_", StringComparison.Ordinal))
				rootImage.color = ResolvePanelShellColor();

			// panel_width applies to the addon shell only — never every child control (blows up compact rows).
			if (root.name.StartsWith("AddonPanel_", StringComparison.Ordinal))
				ApplyPanelWidth(root.GetComponent<LayoutElement>());

			foreach (var button in root.GetComponentsInChildren<Button>(true)) {
				if (button == null || button.targetGraphic == null)
					continue;
				// Dropdown_* row images are almost transparent pointer hit targets.
				if (button.gameObject.name.StartsWith("Dropdown_", StringComparison.Ordinal))
					continue;
				bool isField = button.GetComponent<TMP_Dropdown>() != null
					|| string.Equals(button.gameObject.name, "Dropdown", StringComparison.Ordinal);
				Color normal = isField ? tokens.fieldBg : tokens.controlBg;
				ApplyBoundChromeSelectable(button, normal, tokens.accent);
			}

			foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true)) {
				if (input == null)
					continue;
				var bg = input.GetComponent<Image>();
				if (bg != null) {
					ApplyBoundChromeGraphic(bg, tokens.fieldBg);
					ApplyRoundedControlSprite(bg);
				}
			}

			foreach (var slider in root.GetComponentsInChildren<Slider>(true)) {
				if (slider == null)
					continue;
				var bg = slider.GetComponent<Image>();
				if (bg != null) {
					ApplyBoundChromeGraphic(bg, tokens.fieldBg);
					ApplyRoundedControlSprite(bg);
				}
				if (slider.fillRect != null) {
					var fill = slider.fillRect.GetComponent<Image>();
					if (fill != null)
						ApplyBoundChromeGraphic(fill, tokens.accent);
				}
				if (slider.handleRect != null) {
					var handleImage = slider.handleRect.GetComponent<Image>();
					if (handleImage != null)
						ApplyBoundChromeGraphic(handleImage, tokens.handle);
				}
			}

			foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) {
				if (toggle == null || toggle.targetGraphic == null)
					continue;
				Color face = toggle.isOn
					? Color.Lerp(tokens.tabActive, tokens.accent, 0.45f)
					: tokens.controlBg;
				ThemeCheckboxToggle(toggle, face, tokens.accent, tokens.success);
			}

			foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
				if (text == null)
					continue;
				Color c = string.Equals(text.gameObject.name, "Placeholder", StringComparison.Ordinal)
					? tokens.textMuted
					: tokens.textPrimary;
				ApplyBoundChromeTmp(text, c, 14f);
			}

			foreach (var img in root.GetComponentsInChildren<Image>(true)) {
				if (img == null)
					continue;
				string n = img.gameObject.name ?? "";
				if (n == "LineIcon" || n == "MonolithLineIcon")
					ApplyLineIconTint(img);
			}
		}

		/// <summary>
		/// Hard opaque rectangle face — litmus pattern for Nomad chrome (expanded beyond SAVE 2K).
		/// No 9-slice borders, no soft-AA rounded sprite, no SPZ bevel plate.
		/// Preserves real <see cref="Toggle.graphic"/> checkmarks. Restore via <see cref="RestoreBoundChromeUnder"/>.
		/// </summary>
		public static void ApplySolidSquareChrome(Selectable selectable, Color fill, Color accent) {
			if (selectable == null || selectable.targetGraphic == null)
				return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(selectable.targetGraphic);
				RestoreAuthoredColorBlock(selectable);
				return;
			}
			SnapshotAuthoredGraphic(selectable.targetGraphic);
			SnapshotAuthoredColorBlock(selectable);
			ApplySelectableToken(selectable, fill, accent);
			if (selectable.targetGraphic is Image face
			    && !IsToggleCheckmarkGraphic(face)
			    && !IsUiMaskGraphic(face)) {
				SnapshotAuthoredPixelsPerUnit(face);
				var tag = face.GetComponent<SpzUiThemeRoundedControl>();
				if (tag == null) {
					tag = face.gameObject.AddComponent<SpzUiThemeRoundedControl>();
					tag.authoredSprite = face.sprite;
					tag.authoredType = face.type;
					tag.authoredPixelsPerUnitMultiplier = face.pixelsPerUnitMultiplier;
					tag.hasAuthoredSnapshot = true;
				}
				else if (!tag.hasAuthoredSnapshot) {
					tag.authoredSprite = face.sprite;
					tag.authoredType = face.type;
					tag.authoredPixelsPerUnitMultiplier = face.pixelsPerUnitMultiplier;
					tag.hasAuthoredSnapshot = true;
				}
				face.sprite = UiRuntimeSprites.SolidRect;
				face.type = Image.Type.Simple;
				face.preserveAspect = false;
				face.pixelsPerUnitMultiplier = 1f;
				face.fillCenter = true;
			}
			// Drop authored corner-chevron / fake tick plates under this control only.
			foreach (var img in selectable.GetComponentsInChildren<Image>(true)) {
				if (img == null || img == selectable.targetGraphic) continue;
				if (IsToggleCheckmarkGraphic(img)) continue;
				string n = img.gameObject.name ?? "";
				if (n == ControlLineIconChildName || n == "MonolithActiveBar")
					continue;
				if (n.IndexOf("triangle", StringComparison.OrdinalIgnoreCase) >= 0
					|| n.Equals("Checkmark", StringComparison.OrdinalIgnoreCase)
					|| n.Equals("tick", StringComparison.OrdinalIgnoreCase))
					HideAuthoredGraphicForTheme(img);
			}
		}

		/// <summary>
		/// Assigns a solid-square fill to eligible control Images (Nomad litmus expanded).
		/// Soft <c>corner_radius</c> sliced sprites caused horizontal whiskers on wide buttons —
		/// always use opaque <see cref="UiRuntimeSprites.SolidRect"/> + <see cref="Image.Type.Simple"/>.
		/// Snapshots the authored sprite once so <see cref="RestoreRoundedControlSpritesUnder"/> can unwind.
		/// </summary>
		public static void ApplyRoundedControlSprite(Image image, bool markEligible = false) {
			if (image == null || !ShouldRecolorBoundChrome)
				return;
			if (IsToggleCheckmarkGraphic(image))
				return;
			if (IsUiMaskGraphic(image))
				return;
			// Radial/filled dials must keep authored sprites (CircleSlider fillAmount).
			if (image.type == Image.Type.Filled)
				return;
			var tag = image.GetComponent<SpzUiThemeRoundedControl>();
			if (tag == null) {
				bool eligible = markEligible || UiRuntimeSprites.IsCachedRoundedRect(image.sprite)
					|| UiRuntimeSprites.IsSolidRect(image.sprite);
				if (!eligible)
					return;
				tag = image.gameObject.AddComponent<SpzUiThemeRoundedControl>();
				tag.authoredSprite = image.sprite;
				tag.authoredType = image.type;
				tag.authoredPixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
				tag.hasAuthoredSnapshot = true;
			}
			SnapshotAuthoredPixelsPerUnit(image);
			image.pixelsPerUnitMultiplier = 1f;
			image.preserveAspect = false;
			image.fillCenter = true;
			image.sprite = UiRuntimeSprites.SolidRect;
			image.type = Image.Type.Simple;
		}

		/// <summary>
		/// Restores authored sprites replaced by <see cref="ApplyRoundedControlSprite"/> and removes tags.
		/// Call when leaving non-builtin theme chrome (e.g. Settings / Addon Manager restore paths).
		/// </summary>
		public static void RestoreRoundedControlSpritesUnder(Transform root) {
			if (root == null) return;
			var tags = root.GetComponentsInChildren<SpzUiThemeRoundedControl>(true);
			for (int i = 0; i < tags.Length; i++) {
				var tag = tags[i];
				if (tag == null) continue;
				var img = tag.GetComponent<Image>();
				if (img != null && tag.hasAuthoredSnapshot) {
					img.sprite = tag.authoredSprite;
					img.type = tag.authoredType;
					// Soft 9-slice Mask/chrome used high PPU (e.g. hardness 11); leaving 1 makes white capsule blobs.
					if (AuthoredPixelsPerUnit.TryGetValue(img.GetInstanceID(), out float ppu))
						img.pixelsPerUnitMultiplier = ppu;
					else if (tag.authoredPixelsPerUnitMultiplier > 0.01f)
						img.pixelsPerUnitMultiplier = tag.authoredPixelsPerUnitMultiplier;
				}
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(tag);
				else
					UnityEngine.Object.DestroyImmediate(tag);
			}
		}

		/// <summary>Panel shell color with glass-lite <c>panel_alpha</c> multiplied onto <c>panel_bg</c> alpha.</summary>
		public static Color ResolvePanelShellColor() {
			Color c = _active.panelBg;
			c.a = Mathf.Clamp01(c.a * Mathf.Clamp(_active.panelAlpha, PanelAlphaMin, PanelAlphaMax));
			return c;
		}

		/// <summary>Tints a line-icon Image with the active <c>icon_tint</c> token. No-op on builtin.</summary>
		public static void ApplyLineIconTint(Image image) {
			if (image == null || !ShouldRecolorBoundChrome)
				return;
			image.color = _active.iconTint;
		}

		/// <summary>
		/// Applies active <c>panel_width</c> to a control LayoutElement (preferred + min width).
		/// No-op on builtin so addon panel widths do not stick after Restore SPZ.
		/// </summary>
		public static void ApplyPanelWidth(LayoutElement layout) {
			if (layout == null || !ShouldRecolorBoundChrome)
				return;
			SnapshotPanelWidth(layout);
			float w = Mathf.Clamp(_active.panelWidth, PanelWidthMin, PanelWidthMax);
			layout.preferredWidth = w;
			if (layout.minWidth > 0.5f)
				layout.minWidth = Mathf.Min(layout.minWidth, w);
		}

		static void SnapshotPanelWidth(LayoutElement layout) {
			if (layout == null) return;
			var tag = layout.GetComponent<SpzUiThemeDesignLayoutElement>();
			if (tag == null) {
				tag = layout.gameObject.AddComponent<SpzUiThemeDesignLayoutElement>();
				tag.preferredWidth = layout.preferredWidth;
				tag.minWidth = layout.minWidth;
				tag.hasSnapshot = true;
			}
			else if (!tag.hasSnapshot) {
				tag.preferredWidth = layout.preferredWidth;
				tag.minWidth = layout.minWidth;
				tag.hasSnapshot = true;
			}
		}

		static void RestorePanelWidthsUnder(Transform root) {
			if (root == null) return;
			var tags = root.GetComponentsInChildren<SpzUiThemeDesignLayoutElement>(true);
			for (int i = 0; i < tags.Length; i++) {
				var tag = tags[i];
				if (tag == null || !tag.hasSnapshot) continue;
				var le = tag.GetComponent<LayoutElement>();
				if (le != null) {
					le.preferredWidth = tag.preferredWidth;
					le.minWidth = tag.minWidth;
				}
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(tag);
				else
					UnityEngine.Object.DestroyImmediate(tag);
			}
		}

		/// <summary>
		/// Themes a context-menu ownership root (panel/buttons/TMP/circle sliders) without walking the global skeleton.
		/// </summary>
		public static void ApplyContextMenuChrome(GameObject root) {
			if (root == null)
				return;
			if (!ShouldRecolorBoundChrome) {
				RestoreBoundChromeUnder(root.transform);
				return;
			}
			var tokens = _active;
			var rootImage = root.GetComponent<Image>();
			if (rootImage != null) {
				SnapshotAuthoredGraphic(rootImage);
				rootImage.color = ResolvePanelShellColor();
			}

			foreach (var button in root.GetComponentsInChildren<Button>(true)) {
				if (button == null || button.targetGraphic == null)
					continue;
				ApplyBoundChromeSelectable(button, tokens.controlBg, tokens.accent);
			}

			foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
				if (text == null)
					continue;
				ApplyBoundChromeTmp(text, tokens.textPrimary);
			}

			foreach (var slider in root.GetComponentsInChildren<Slider>(true)) {
				if (slider == null)
					continue;
				var bg = slider.GetComponent<Image>();
				if (bg != null) {
					SnapshotAuthoredGraphic(bg);
					bg.color = tokens.fieldBg;
				}
				if (slider.fillRect != null) {
					var fill = slider.fillRect.GetComponent<Image>();
					if (fill != null) {
						SnapshotAuthoredGraphic(fill);
						fill.color = tokens.accent;
					}
				}
				if (slider.handleRect != null) {
					var handleImage = slider.handleRect.GetComponent<Image>();
					if (handleImage != null) {
						SnapshotAuthoredGraphic(handleImage);
						handleImage.color = tokens.handle;
					}
				}
			}

			foreach (var circle in root.GetComponentsInChildren<CircleSlider_Snapping_UI>(true)) {
				if (circle != null)
					circle.ApplyThemeTokens(tokens.accent, tokens.textPrimary);
			}
		}

		/// <summary>
		/// Captures design TMP point size once so repeated theme applies do not compound <c>font_scale</c>.
		/// </summary>
		public static float ResolveOrCaptureDesignFontPt(TMP_Text text, float fallbackBasePt) {
			if (text == null)
				return fallbackBasePt > 0.05f ? fallbackBasePt : 14f;
			var tag = text.gameObject.GetComponent<SpzUiThemeDesignFontPt>();
			if (tag == null) {
				tag = text.gameObject.AddComponent<SpzUiThemeDesignFontPt>();
				// Capture authored size as-is. Do not divide by Active.fontScale — first theme
				// apply runs while TMP still holds design points (dividing cancelled the scale).
				float current = text.fontSize > 0.05f ? text.fontSize : fallbackBasePt;
				tag.designPt = current < 0.05f
					? (fallbackBasePt > 0.05f ? fallbackBasePt : 14f)
					: current;
			}
			return tag.designPt;
		}

		/// <summary>
		/// Image.color holds the token; ColorBlock stays a white-based multiplier so Unity
		/// does not darken tokens by multiplying the same color twice.
		/// </summary>
		public static void ApplyGraphicColor(Graphic graphic, Color token) {
			if (graphic != null)
				graphic.color = token;
		}

		public static void ApplySelectableToken(Selectable selectable, Color normal, Color accent) {
			if (selectable == null || selectable.targetGraphic == null)
				return;
			selectable.targetGraphic.color = normal;
			var colors = selectable.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = Color.Lerp(Color.white, accent, 0.25f);
			colors.pressedColor = Color.Lerp(Color.white, accent, 0.55f);
			colors.selectedColor = colors.highlightedColor;
			// Keep hard-disabled faces readable (Brush flat block / GEN cancel parity).
			colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
			colors.colorMultiplier = 1f;
			selectable.colors = colors;
		}

		/// <summary>
		/// Sets TMP color and scaled font size using a once-captured design base (no compound resize).
		/// </summary>
		public static void ApplyTmpScaledCaptured(TMP_Text text, Color token, float fallbackBasePt = 14f) {
			if (text == null)
				return;
			ApplyTmpScaled(text, token, ResolveOrCaptureDesignFontPt(text, fallbackBasePt));
		}

		/// <summary>
		/// Scales an existing layout group's spacing/padding from design bases stored on first apply.
		/// Safe to call on builtin (spacing_scale 1) to unwind a prior non-default scale.
		/// </summary>
		public static void ApplyScaledLayoutGroup(LayoutGroup group) {
			if (group == null)
				return;
			var hv = group as HorizontalOrVerticalLayoutGroup;
			var tag = group.gameObject.GetComponent<SpzUiThemeDesignLayoutGroup>();
			if (tag == null) {
				tag = group.gameObject.AddComponent<SpzUiThemeDesignLayoutGroup>();
				// Capture authored spacing/padding as-is (same reason as font design capture).
				float spacing0 = hv != null ? hv.spacing : 0f;
				tag.spacing = spacing0;
				tag.padL = group.padding.left;
				tag.padR = group.padding.right;
				tag.padT = group.padding.top;
				tag.padB = group.padding.bottom;
			}
			float s = _active.spacingScale;
			if (hv != null)
				hv.spacing = tag.spacing * s;
			group.padding = new RectOffset(
				Mathf.RoundToInt(tag.padL * s),
				Mathf.RoundToInt(tag.padR * s),
				Mathf.RoundToInt(tag.padT * s),
				Mathf.RoundToInt(tag.padB * s));
		}

		/// <summary>
		/// Re-applies <see cref="ApplyScaledLayoutGroup"/> under a root so leaving a theme
		/// (spacing_scale → 1) unwinds scaled padding/spacing even when color chrome is gated off.
		/// </summary>
		public static void RefreshScaledLayoutGroupsUnder(Transform root) {
			if (root == null) return;
			foreach (var lg in root.GetComponentsInChildren<LayoutGroup>(true))
				ApplyScaledLayoutGroup(lg);
		}

		public static void ApplyTmpColor(TMP_Text text, Color token) {
			if (text != null)
				text.color = token;
		}

		/// <summary>
		/// Sets TMP color and <c>fontSize = basePt * Active.fontScale</c> (does not compound across applies).
		/// </summary>
		public static void ApplyTmpScaled(TMP_Text text, Color token, float basePt) {
			if (text == null)
				return;
			text.color = token;
			if (basePt > 0.05f)
				text.fontSize = basePt * _active.fontScale;
		}

		/// <summary>
		/// <see cref="ProjectUiScale.Space(int)"/> multiplied by the active theme <c>spacing_scale</c>.
		/// </summary>
		public static float ScaledSpace(int n) {
			return ProjectUiScale.Space(n, _active.spacingScale);
		}

		static JArray BuildTokenSchema() {
			return new JArray {
				SchemaColor("panel_bg"),
				SchemaColor("control_bg"),
				SchemaColor("field_bg"),
				SchemaColor("accent"),
				SchemaColor("text_primary"),
				SchemaColor("text_muted"),
				SchemaColor("handle"),
				SchemaColor("success"),
				SchemaColor("danger"),
				SchemaColor("border"),
				SchemaColor("tab_active"),
				SchemaColor("selection"),
				SchemaColor("icon_tint"),
				SchemaFloat("font_scale", ScaleTokenMin, ScaleTokenMax),
				SchemaFloat("spacing_scale", ScaleTokenMin, ScaleTokenMax),
				SchemaFloat("corner_radius", CornerRadiusMin, CornerRadiusMax),
				SchemaFloat("panel_width", PanelWidthMin, PanelWidthMax),
				SchemaFloat("panel_alpha", PanelAlphaMin, PanelAlphaMax),
				SchemaFloat("ribbon_icon_only", RibbonIconOnlyMin, RibbonIconOnlyMax),
			};
		}

		static JObject SchemaColor(string name) {
			return new JObject {
				["name"] = name,
				["type"] = "color",
			};
		}

		static JObject SchemaFloat(string name, float min, float max) {
			return new JObject {
				["name"] = name,
				["type"] = "float",
				["min"] = min,
				["max"] = max,
			};
		}

		static JObject SerializeTokens(ThemeTokens tokens) {
			return new JObject {
				["panel_bg"] = ColorToHex(tokens.panelBg),
				["control_bg"] = ColorToHex(tokens.controlBg),
				["field_bg"] = ColorToHex(tokens.fieldBg),
				["accent"] = ColorToHex(tokens.accent),
				["text_primary"] = ColorToHex(tokens.textPrimary),
				["text_muted"] = ColorToHex(tokens.textMuted),
				["handle"] = ColorToHex(tokens.handle),
				["success"] = ColorToHex(tokens.success),
				["danger"] = ColorToHex(tokens.danger),
				["border"] = ColorToHex(tokens.border),
				["tab_active"] = ColorToHex(tokens.tabActive),
				["selection"] = ColorToHex(tokens.selection),
				["icon_tint"] = ColorToHex(tokens.iconTint),
				["font_scale"] = tokens.fontScale,
				["spacing_scale"] = tokens.spacingScale,
				["corner_radius"] = tokens.cornerRadius,
				["panel_width"] = tokens.panelWidth,
				["panel_alpha"] = tokens.panelAlpha,
				["ribbon_icon_only"] = tokens.ribbonIconOnly,
			};
		}

		static JObject SerializeCatalogEntry(
			string id, string label, string source, bool active, string owner = null) {
			var entry = new JObject {
				["id"] = id,
				["label"] = label,
				["source"] = source,
				["active"] = active,
			};
			if (!string.IsNullOrEmpty(owner))
				entry["owner"] = owner;
			return entry;
		}

		static JArray BuildSurfaces() {
			return new JArray {
				Surface("addon_panels", true, "AddonUI_MGR AddonPanel_* roots; colors + font_scale"),
				Surface("command_ribbon", true, "CommandRibbon_UI strip/panels/tabs; recolor only when non-builtin theme; Monolith icons gated; ribbon_icon_only hides labels"),
				Surface("paint_tab", true, "PaintTab Collect/Krita/Layers; colors only when non-builtin theme"),
				Surface("addon_manager", true, "AddonManager_UI; REF roles → tokens when non-builtin theme"),
				Surface("settings", true, "Settings_UI chrome when non-builtin theme; product prefs untouched"),
				Surface("viewport_statusline", true, "Viewport_StatusText RGB when non-builtin theme; sticky caller-owned"),
				Surface("viewport_ribbons", true, "LeftRibbon + WorkflowRibbon + GenButtons; colors when non-builtin theme"),
				Surface("sd_input_panel", true, "SD_InputPanel_UI column; colors when non-builtin theme"),
				Surface("export_save_menu", true, "ExportSave_UI_MGR buttons; colors when non-builtin theme"),
				Surface("scene_resolution", true, "SceneResolution_MGR SAVE Nx / filters; colors when non-builtin theme"),
				Surface("connection_panels", true, "ConnectionPanel_UI SD SERV / 3D SERV; colors when non-builtin theme"),
				Surface("right_panel_lists", true, "Art/BG/Mesh/Art3D/CN list chrome; colors when non-builtin theme"),
				Surface("multiview_pins", true, "MultiView_Ribbon_UI + CamerasMGR_PinsZone_UI; colors when non-builtin theme"),
				Surface("workflow_options", true, "Colors slide-out + SD_WorkflowOptionsRibbon_UI; colors when non-builtin theme"),
				Surface("context_menus", true, "Art/AO/3D icon context menus; Value Assist; colors when non-builtin theme"),
				Surface("chrome_targets", true, "spz.cmd.list_ui_targets / set_ui_target_active show-hide only (constrained DOM)"),
			};
		}

		static JObject Surface(string id, bool bound, string notes) {
			return new JObject {
				["id"] = id,
				["bound"] = bound,
				["notes"] = notes,
			};
		}

		static bool TryNormalizeThemeId(string value, out string normalized, out string error) {
			normalized = value != null ? value.Trim() : "";
			error = null;
			if (normalized.Length == 0 || normalized.Length > kMaxThemeIdChars) {
				error = $"theme_id must contain 1-{kMaxThemeIdChars} characters";
				return false;
			}
			foreach (char c in normalized) {
				if (char.IsControl(c)) {
					error = "theme_id cannot contain control characters";
					return false;
				}
			}
			return true;
		}

		static bool TryGetPresetTokens(string themeId, out ThemeTokens tokens) {
			if (string.Equals(themeId, DefaultThemeId, StringComparison.Ordinal)) {
				tokens = Defaults;
				return true;
			}
			if (RegisteredThemes.TryGetValue(themeId, out ThemePreset preset)) {
				tokens = preset.tokens;
				return true;
			}
			tokens = null;
			return false;
		}

		static bool TryOverlayTokens(ThemeTokens candidate, JObject tokenValues, out string error) {
			error = null;
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in tokenValues.Properties()) {
				string tokenName = property.Name != null
					? property.Name.Trim().ToLowerInvariant()
					: "";
				if (IsReservedTokenName(tokenName)) {
					error = $"Theme token '{tokenName}' is reserved for a later API version";
					return false;
				}
				if (!seen.Add(tokenName)) {
					error = $"Duplicate theme token after normalization: {tokenName}";
					return false;
				}
				switch (tokenName) {
					case "font_scale":
					case "spacing_scale":
						if (!TryParseScale(property.Value, out float scale, out error)) {
							if (string.IsNullOrEmpty(error))
								error = $"Invalid float for token '{property.Name}'; expected number in [{ScaleTokenMin},{ScaleTokenMax}]";
							else
								error = $"Invalid float for token '{property.Name}': {error}";
							return false;
						}
						if (tokenName == "font_scale")
							candidate.fontScale = scale;
						else
							candidate.spacingScale = scale;
						break;
					case "corner_radius":
						if (!TryParseCornerRadius(property.Value, out float radius, out error)) {
							if (string.IsNullOrEmpty(error))
								error = $"Invalid float for token '{property.Name}'; expected number in [{CornerRadiusMin},{CornerRadiusMax}]";
							else
								error = $"Invalid float for token '{property.Name}': {error}";
							return false;
						}
						candidate.cornerRadius = radius;
						break;
					case "panel_width":
						if (!TryParsePanelWidth(property.Value, out float width, out error)) {
							if (string.IsNullOrEmpty(error))
								error = $"Invalid float for token '{property.Name}'; expected number in [{PanelWidthMin},{PanelWidthMax}]";
							else
								error = $"Invalid float for token '{property.Name}': {error}";
							return false;
						}
						candidate.panelWidth = width;
						break;
					case "panel_alpha":
						if (!TryParsePanelAlpha(property.Value, out float alpha, out error)) {
							if (string.IsNullOrEmpty(error))
								error = $"Invalid float for token '{property.Name}'; expected number in [{PanelAlphaMin},{PanelAlphaMax}]";
							else
								error = $"Invalid float for token '{property.Name}': {error}";
							return false;
						}
						candidate.panelAlpha = alpha;
						break;
					case "ribbon_icon_only":
						if (!TryParseRibbonIconOnly(property.Value, out float iconOnly, out error)) {
							if (string.IsNullOrEmpty(error))
								error = $"Invalid float for token '{property.Name}'; expected number in [{RibbonIconOnlyMin},{RibbonIconOnlyMax}]";
							else
								error = $"Invalid float for token '{property.Name}': {error}";
							return false;
						}
						candidate.ribbonIconOnly = iconOnly;
						break;
					case "panel_bg":
					case "control_bg":
					case "field_bg":
					case "accent":
					case "text_primary":
					case "text_muted":
					case "handle":
					case "success":
					case "danger":
					case "border":
					case "tab_active":
					case "selection":
					case "icon_tint":
						if (!TryParseColor(property.Value, out var color)) {
							error = $"Invalid color for token '{property.Name}'; expected #RRGGBB or #RRGGBBAA";
							return false;
						}
						switch (tokenName) {
							case "panel_bg": candidate.panelBg = color; break;
							case "control_bg": candidate.controlBg = color; break;
							case "field_bg": candidate.fieldBg = color; break;
							case "accent": candidate.accent = color; break;
							case "text_primary": candidate.textPrimary = color; break;
							case "text_muted": candidate.textMuted = color; break;
							case "handle": candidate.handle = color; break;
							case "success": candidate.success = color; break;
							case "danger": candidate.danger = color; break;
							case "border": candidate.border = color; break;
							case "tab_active": candidate.tabActive = color; break;
							case "selection": candidate.selection = color; break;
							case "icon_tint": candidate.iconTint = color; break;
						}
						break;
					default:
						error = $"Unknown theme token: {property.Name}";
						return false;
				}
			}
			return true;
		}

		static bool IsReservedTokenName(string tokenName) {
			foreach (string reserved in ReservedTokenNames) {
				if (string.Equals(tokenName, reserved, StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		static void OverlayTokens(ThemeTokens target, ThemeTokens source) {
			target.panelBg = source.panelBg;
			target.controlBg = source.controlBg;
			target.fieldBg = source.fieldBg;
			target.accent = source.accent;
			target.textPrimary = source.textPrimary;
			target.textMuted = source.textMuted;
			target.handle = source.handle;
			target.success = source.success;
			target.danger = source.danger;
			target.border = source.border;
			target.tabActive = source.tabActive;
			target.selection = source.selection;
			target.iconTint = source.iconTint;
			target.fontScale = source.fontScale;
			target.spacingScale = source.spacingScale;
			target.cornerRadius = source.cornerRadius;
			target.panelWidth = source.panelWidth;
			target.panelAlpha = source.panelAlpha;
			target.ribbonIconOnly = source.ribbonIconOnly;
		}

		static bool TryParseRibbonIconOnly(JToken token, out float value, out string error) {
			value = DefaultRibbonIconOnly;
			error = null;
			if (token == null) {
				error = "value is null";
				return false;
			}
			if (token.Type == JTokenType.Boolean) {
				value = token.Value<bool>() ? 1f : 0f;
				return true;
			}
			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) {
				value = token.Value<float>();
			}
			else if (token.Type == JTokenType.String) {
				string s = token.ToString().Trim();
				if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) {
					value = 1f;
					return true;
				}
				if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) {
					value = 0f;
					return true;
				}
				if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out value)) {
					error = "expected number or bool";
					return false;
				}
			}
			else {
				error = "expected number or bool";
				return false;
			}
			return TryValidateFiniteFloatInRange(value, RibbonIconOnlyMin, RibbonIconOnlyMax, out error);
		}

		static bool TryParsePanelAlpha(JToken token, out float alpha, out string error) {
			alpha = DefaultPanelAlpha;
			error = null;
			if (token == null) {
				error = "value is null";
				return false;
			}
			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) {
				alpha = token.Value<float>();
			}
			else if (token.Type == JTokenType.String) {
				string s = token.ToString().Trim();
				if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out alpha)) {
					error = "expected number";
					return false;
				}
			}
			else {
				error = "expected number";
				return false;
			}
			return TryValidateFiniteFloatInRange(alpha, PanelAlphaMin, PanelAlphaMax, out error);
		}

		/// <summary>Names of built-in <see cref="StudioLineIcon"/> glyphs (icon pack v1).</summary>
		public static JArray ListLineIconNames() {
			var arr = new JArray();
			foreach (StudioLineIcon icon in Enum.GetValues(typeof(StudioLineIcon)))
				arr.Add(icon.ToString());
			return arr;
		}

		public static bool TryParseStudioLineIcon(string name, out StudioLineIcon icon, out string error) {
			icon = StudioLineIcon.Folder;
			error = null;
			if (string.IsNullOrWhiteSpace(name)) {
				error = "icon name is empty";
				return false;
			}
			if (Enum.TryParse(name.Trim(), true, out icon))
				return true;
			error = $"Unknown line icon '{name}'. Use list_line_icons.";
			return false;
		}

		/// <summary>
		/// Sets CommandRibbon strip tab MonolithLineIcon glyphs for every tab whose name contains <paramref name="tabMatch"/>.
		/// </summary>
		public static bool TrySetStripTabLineIcon(string tabMatch, string iconName, out string error) {
			error = null;
			if (!TryParseStudioLineIcon(iconName, out StudioLineIcon icon, out error))
				return false;
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon == null) {
				error = "CommandRibbon_UI not available";
				return false;
			}
			if (!ribbon.TrySetStripTabLineIcon(tabMatch, icon, out error))
				return false;
			return true;
		}

		static bool TryParsePanelWidth(JToken token, out float width, out string error) {
			width = DefaultPanelWidth;
			error = null;
			if (token == null) {
				error = "value is null";
				return false;
			}
			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) {
				width = token.Value<float>();
			}
			else if (token.Type == JTokenType.String) {
				string s = token.ToString().Trim();
				if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out width)) {
					error = "expected number";
					return false;
				}
			}
			else {
				error = "expected number";
				return false;
			}
			return TryValidateFiniteFloatInRange(width, PanelWidthMin, PanelWidthMax, out error);
		}

		static bool TryParseCornerRadius(JToken token, out float radius, out string error) {
			radius = DefaultCornerRadius;
			error = null;
			if (token == null) {
				error = "value is null";
				return false;
			}
			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) {
				radius = token.Value<float>();
			}
			else if (token.Type == JTokenType.String) {
				string s = token.ToString().Trim();
				if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out radius)) {
					error = "expected number";
					return false;
				}
			}
			else {
				error = "expected number";
				return false;
			}
			return TryValidateFiniteFloatInRange(radius, CornerRadiusMin, CornerRadiusMax, out error);
		}

		static bool TryParseScale(JToken token, out float scale, out string error) {
			scale = 1f;
			error = null;
			if (token == null) {
				error = "value is null";
				return false;
			}
			if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) {
				scale = token.Value<float>();
			}
			else if (token.Type == JTokenType.String) {
				string s = token.ToString().Trim();
				if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out scale)) {
					error = "expected number";
					return false;
				}
			}
			else {
				error = "expected number";
				return false;
			}
			return TryValidateFiniteFloatInRange(scale, ScaleTokenMin, ScaleTokenMax, out error);
		}

		/// <summary>Reject NaN/Infinity — bare min/max compares treat NaN as in-range.</summary>
		static bool TryValidateFiniteFloatInRange(float value, float min, float max, out string error) {
			error = null;
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				error = "must be a finite number";
				return false;
			}
			if (value < min || value > max) {
				error = $"must be between {min} and {max}";
				return false;
			}
			return true;
		}

		static bool TryParseColor(JToken token, out Color color) {
			color = default;
			if (token == null || token.Type != JTokenType.String)
				return false;
			string value = token.ToString().Trim();
			if ((value.Length != 7 && value.Length != 9) || value[0] != '#')
				return false;
			return ColorUtility.TryParseHtmlString(value, out color);
		}

		static string ColorToHex(Color color) {
			var c = (Color32)color;
			return $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}";
		}
	}

	/// <summary>Stores design-time TMP point size for theme <c>font_scale</c> without compounding.</summary>
	public sealed class SpzUiThemeDesignFontPt : MonoBehaviour {
		public float designPt = 14f;
	}

	/// <summary>Stores design-time layout group spacing/padding for theme <c>spacing_scale</c>.</summary>
	public sealed class SpzUiThemeDesignLayoutGroup : MonoBehaviour {
		public float spacing;
		public int padL, padR, padT, padB;
	}

	/// <summary>Snapshots LayoutElement widths so panel_width can unwind on Restore SPZ.</summary>
	public sealed class SpzUiThemeDesignLayoutElement : MonoBehaviour {
		public float preferredWidth = -1f;
		public float minWidth = -1f;
		public bool hasSnapshot;
	}

	/// <summary>Marks an Image as eligible for theme <c>corner_radius</c> 9-slice updates.</summary>
	public sealed class SpzUiThemeRoundedControl : MonoBehaviour {
		public Sprite authoredSprite;
		public Image.Type authoredType = Image.Type.Simple;
		public float authoredPixelsPerUnitMultiplier = 1f;
		public bool hasAuthoredSnapshot;
	}

	/// <summary>
	/// On a CommandRibbon strip cell: sprite was set by <c>spz.ui.set_line_icon</c> / compose —
	/// do not replace with auto-resolved tab glyphs on ThemeChanged.
	/// </summary>
	public sealed class SpzStripLineIconOverride : MonoBehaviour {
		public StudioLineIcon Icon;
	}

		/// <summary>Snapshots authored TMP tracking/style/font so Nomad typography can unwind on builtin restore.</summary>
		public sealed class SpzUiThemeDesignTypography : MonoBehaviour {
			public float characterSpacing;
			public float lineSpacing;
			public FontStyles fontStyle;
			public float outlineWidth;
			public Color outlineColor = Color.clear;
			public TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft;
			public bool hasAlignmentSnapshot;
			public TMP_FontAsset authoredFont;
			public Material authoredFontSharedMaterial;
			public bool hasFontSnapshot;
			public bool authoredRaycastTarget = true;
			public bool hasRaycastSnapshot;
			public bool hasSnapshot;
		}

	/// <summary>Marks an authored icon Image hidden while a MonolithLineIcon overlay is shown.</summary>
	public sealed class SpzUiThemeHiddenGraphic : MonoBehaviour {
		public bool wasEnabled = true;
		public bool hasSnapshot;
	}

	/// <summary>Snapshots vertical slider handle sizeDelta so bullseye thumbs unwind on Restore SPZ.</summary>
	public sealed class SpzUiThemeSliderHandleLayout : MonoBehaviour {
		public Vector2 authoredSizeDelta;
		public bool hasSnapshot;
	}

	/// <summary>Snapshots tool-face RectTransform before Nomad edge-to-edge flatten; unwound by RestoreBoundChromeUnder.</summary>
	public sealed class SpzUiThemeDesignRectTransform : MonoBehaviour {
		public Vector2 anchorMin;
		public Vector2 anchorMax;
		public Vector2 pivot;
		public Vector2 anchoredPosition;
		public Vector2 sizeDelta;
		public Vector2 offsetMin;
		public Vector2 offsetMax;
		public Vector3 localScale = Vector3.one;
		public bool hasSnapshot;
	}
}
