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
		/// </summary>
		public static bool ShouldRecolorBoundChrome => !IsBuiltinDefaultActive;

		/// <summary>True when <c>ribbon_icon_only</c> ≥ 0.5 (CommandRibbon strip hides labels, enlarges line icons).</summary>
		public static bool RibbonIconOnlyActive =>
			_active.ribbonIconOnly >= 0.5f;

		static readonly Dictionary<int, Color> AuthoredGraphicColors =
			new Dictionary<int, Color>();

		static void SnapshotAuthoredGraphic(Graphic graphic) {
			if (graphic == null) return;
			int id = graphic.GetInstanceID();
			if (!AuthoredGraphicColors.ContainsKey(id))
				AuthoredGraphicColors[id] = graphic.color;
		}

		/// <summary>Restores a graphic's pre-theme color when snapshotted; no-op otherwise.</summary>
		public static void RestoreAuthoredGraphic(Graphic graphic) {
			if (graphic == null) return;
			if (AuthoredGraphicColors.TryGetValue(graphic.GetInstanceID(), out Color c))
				graphic.color = c;
			if (graphic is TMP_Text tmp)
				RestoreNomadTypography(tmp);
		}

		/// <summary>
		/// Applies a chrome token color only when <see cref="ShouldRecolorBoundChrome"/>;
		/// otherwise restores the authored snapshot (if any).
		/// </summary>
		public static void ApplyBoundChromeGraphic(Graphic graphic, Color token) {
			if (graphic == null) return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(graphic);
				return;
			}
			SnapshotAuthoredGraphic(graphic);
			graphic.color = token;
		}

		/// <summary>
		/// Selectable chrome apply gated by <see cref="ShouldRecolorBoundChrome"/>.
		/// </summary>
		public static void ApplyBoundChromeSelectable(Selectable selectable, Color normal, Color accent) {
			if (selectable == null || selectable.targetGraphic == null)
				return;
			if (!ShouldRecolorBoundChrome) {
				RestoreAuthoredGraphic(selectable.targetGraphic);
				return;
			}
			SnapshotAuthoredGraphic(selectable.targetGraphic);
			ApplySelectableToken(selectable, normal, accent);
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

		static void SnapshotNomadTypography(TMP_Text text) {
			if (text == null) return;
			var tag = text.GetComponent<SpzUiThemeDesignTypography>();
			if (tag == null) {
				tag = text.gameObject.AddComponent<SpzUiThemeDesignTypography>();
				tag.characterSpacing = text.characterSpacing;
				tag.fontStyle = text.fontStyle;
				tag.hasSnapshot = true;
			}
			else if (!tag.hasSnapshot) {
				tag.characterSpacing = text.characterSpacing;
				tag.fontStyle = text.fontStyle;
				tag.hasSnapshot = true;
			}
		}

		static void RestoreNomadTypography(TMP_Text text) {
			if (text == null) return;
			var tag = text.GetComponent<SpzUiThemeDesignTypography>();
			if (tag == null || !tag.hasSnapshot) return;
			text.characterSpacing = tag.characterSpacing;
			text.fontStyle = tag.fontStyle;
		}

		/// <summary>Open tracking for sculpt-chrome labels (no font-asset swap — LiberationSans SDF).</summary>
		static void ApplyNomadTypographyMetrics(TMP_Text text) {
			if (text == null) return;
			text.characterSpacing = NomadLabelCharacterSpacing;
		}

		/// <summary>
		/// Ensures a centered <c>MonolithLineIcon</c> under <paramref name="owner"/> and hides authored
		/// icon Images named icon/Icon. Restores when builtin default is active.
		/// </summary>
		public static void ApplyControlLineIcon(Transform owner, StudioLineIcon glyph, float sizePx = 22f) {
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
				rt.anchoredPosition = Vector2.zero;
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

		static void RestoreHiddenAuthoredIconsUnder(Transform root) {
			if (root == null) return;
			var tags = root.GetComponentsInChildren<SpzUiThemeHiddenGraphic>(true);
			for (int i = 0; i < tags.Length; i++) {
				var tag = tags[i];
				if (tag == null) continue;
				var img = tag.GetComponent<Image>();
				if (img != null && tag.hasSnapshot)
					img.enabled = tag.wasEnabled;
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
			return name.Equals("icon", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("icon_", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("_icon", StringComparison.OrdinalIgnoreCase)
				|| name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0;
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
				foreach (var g in root.GetComponentsInChildren<Graphic>(true))
					RestoreAuthoredGraphic(g);
				return;
			}
			foreach (var g in root.GetComponentsInChildren<Graphic>(true))
				SnapshotAuthoredGraphic(g);
			var tokens = _active;

			var rootImage = root.GetComponent<Image>();
			if (rootImage != null && root.name.StartsWith("AddonPanel_", StringComparison.Ordinal))
				rootImage.color = ResolvePanelShellColor();

			foreach (var button in root.GetComponentsInChildren<Button>(true)) {
				if (button == null || button.targetGraphic == null)
					continue;
				// Dropdown_* row images are almost transparent pointer hit targets.
				if (button.gameObject.name.StartsWith("Dropdown_", StringComparison.Ordinal))
					continue;
				bool isField = button.GetComponent<TMP_Dropdown>() != null
					|| string.Equals(button.gameObject.name, "Dropdown", StringComparison.Ordinal);
				Color normal = isField ? tokens.fieldBg : tokens.controlBg;
				// Image.color is the token; ColorBlock stays a white-based multiplier so Unity does
				// not darken tokens by multiplying the same color twice.
				button.targetGraphic.color = normal;
				if (button.targetGraphic is Image btnImg)
					ApplyRoundedControlSprite(btnImg);
				ApplyPanelWidth(button.GetComponent<LayoutElement>());
				var colors = button.colors;
				colors.normalColor = Color.white;
				colors.highlightedColor = Color.Lerp(Color.white, tokens.accent, 0.25f);
				colors.pressedColor = Color.Lerp(Color.white, tokens.accent, 0.55f);
				colors.selectedColor = colors.highlightedColor;
				button.colors = colors;
			}

			foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true)) {
				if (input == null)
					continue;
				var bg = input.GetComponent<Image>();
				if (bg != null) {
					bg.color = tokens.fieldBg;
					ApplyRoundedControlSprite(bg);
				}
				ApplyPanelWidth(input.GetComponent<LayoutElement>());
				var parentLe = input.transform.parent != null
					? input.transform.parent.GetComponent<LayoutElement>()
					: null;
				ApplyPanelWidth(parentLe);
			}

			foreach (var slider in root.GetComponentsInChildren<Slider>(true)) {
				if (slider == null)
					continue;
				var bg = slider.GetComponent<Image>();
				if (bg != null) {
					bg.color = tokens.fieldBg;
					ApplyRoundedControlSprite(bg);
				}
				if (slider.fillRect != null) {
					var fill = slider.fillRect.GetComponent<Image>();
					if (fill != null)
						fill.color = tokens.accent;
				}
				if (slider.handleRect != null) {
					var handleImage = slider.handleRect.GetComponent<Image>();
					if (handleImage != null)
						handleImage.color = tokens.handle;
				}
			}

			foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) {
				if (toggle == null || toggle.targetGraphic == null)
					continue;
				Color normal = toggle.isOn
					? Color.Lerp(tokens.tabActive, tokens.accent, 0.45f)
					: tokens.controlBg;
				ApplySelectableToken(toggle, normal, tokens.accent);
				if (toggle.targetGraphic is Image toggleBg)
					ApplyRoundedControlSprite(toggleBg);
				ApplyPanelWidth(toggle.GetComponent<LayoutElement>());
				if (toggle.graphic is Image check)
					check.color = tokens.accent;
			}

			foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
				if (text == null)
					continue;
				Color c = string.Equals(text.gameObject.name, "Placeholder", StringComparison.Ordinal)
					? tokens.textMuted
					: tokens.textPrimary;
				float basePt = ResolveOrCaptureDesignFontPt(text, 14f);
				ApplyTmpScaled(text, c, basePt);
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
		/// Assigns the active <c>corner_radius</c> 9-slice to eligible control Images only
		/// (tagged or already using a runtime rounded sprite). Never retargets RawImage art.
		/// Snapshots the authored sprite once so <see cref="RestoreRoundedControlSpritesUnder"/> can unwind.
		/// </summary>
		public static void ApplyRoundedControlSprite(Image image, bool markEligible = false) {
			if (image == null)
				return;
			var tag = image.GetComponent<SpzUiThemeRoundedControl>();
			if (tag == null) {
				bool eligible = markEligible || UiRuntimeSprites.IsCachedRoundedRect(image.sprite);
				if (!eligible)
					return;
				tag = image.gameObject.AddComponent<SpzUiThemeRoundedControl>();
				tag.authoredSprite = image.sprite;
				tag.authoredType = image.type;
				tag.hasAuthoredSnapshot = true;
			}
			int radius = Mathf.RoundToInt(Mathf.Clamp(_active.cornerRadius, CornerRadiusMin, CornerRadiusMax));
			image.sprite = UiRuntimeSprites.GetRoundedRectSliced(radius);
			image.type = Image.Type.Sliced;
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

		/// <summary>Tints a line-icon Image with the active <c>icon_tint</c> token.</summary>
		public static void ApplyLineIconTint(Image image) {
			if (image == null)
				return;
			image.color = _active.iconTint;
		}

		/// <summary>Applies active <c>panel_width</c> to a control LayoutElement (preferred + min width).</summary>
		public static void ApplyPanelWidth(LayoutElement layout) {
			if (layout == null)
				return;
			float w = Mathf.Clamp(_active.panelWidth, PanelWidthMin, PanelWidthMax);
			layout.preferredWidth = w;
			if (layout.minWidth > 0.5f)
				layout.minWidth = Mathf.Min(layout.minWidth, w);
		}

		/// <summary>
		/// Themes a context-menu ownership root (panel/buttons/TMP/circle sliders) without walking the global skeleton.
		/// </summary>
		public static void ApplyContextMenuChrome(GameObject root) {
			if (root == null)
				return;
			if (!ShouldRecolorBoundChrome) {
				foreach (var g in root.GetComponentsInChildren<Graphic>(true))
					RestoreAuthoredGraphic(g);
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
				SnapshotAuthoredGraphic(button.targetGraphic);
				ApplySelectableToken(button, tokens.controlBg, tokens.accent);
				if (button.targetGraphic is Image btnImg)
					ApplyRoundedControlSprite(btnImg);
			}

			foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
				if (text == null)
					continue;
				SnapshotAuthoredGraphic(text);
				ApplyTmpScaledCaptured(text, tokens.textPrimary);
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
				float scale = _active.fontScale;
				float current = text.fontSize > 0.05f ? text.fontSize : fallbackBasePt;
				tag.designPt = (scale > 0.05f && Mathf.Abs(scale - 1f) > 0.001f)
					? current / scale
					: current;
				if (tag.designPt < 0.05f)
					tag.designPt = fallbackBasePt > 0.05f ? fallbackBasePt : 14f;
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
				float s0 = _active.spacingScale;
				bool unscale = s0 > 0.05f && Mathf.Abs(s0 - 1f) > 0.001f;
				float spacing0 = hv != null ? hv.spacing : 0f;
				tag.spacing = unscale ? spacing0 / s0 : spacing0;
				tag.padL = unscale ? Mathf.RoundToInt(group.padding.left / s0) : group.padding.left;
				tag.padR = unscale ? Mathf.RoundToInt(group.padding.right / s0) : group.padding.right;
				tag.padT = unscale ? Mathf.RoundToInt(group.padding.top / s0) : group.padding.top;
				tag.padB = unscale ? Mathf.RoundToInt(group.padding.bottom / s0) : group.padding.bottom;
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

	/// <summary>Marks an Image as eligible for theme <c>corner_radius</c> 9-slice updates.</summary>
	public sealed class SpzUiThemeRoundedControl : MonoBehaviour {
		public Sprite authoredSprite;
		public Image.Type authoredType = Image.Type.Simple;
		public bool hasAuthoredSnapshot;
	}

	/// <summary>
	/// On a CommandRibbon strip cell: sprite was set by <c>spz.ui.set_line_icon</c> / compose —
	/// do not replace with auto-resolved tab glyphs on ThemeChanged.
	/// </summary>
	public sealed class SpzStripLineIconOverride : MonoBehaviour {
		public StudioLineIcon Icon;
	}

	/// <summary>Snapshots authored TMP tracking/style so Nomad typography can unwind on builtin restore.</summary>
	public sealed class SpzUiThemeDesignTypography : MonoBehaviour {
		public float characterSpacing;
		public FontStyles fontStyle;
		public bool hasSnapshot;
	}

	/// <summary>Marks an authored icon Image hidden while a MonolithLineIcon overlay is shown.</summary>
	public sealed class SpzUiThemeHiddenGraphic : MonoBehaviour {
		public bool wasEnabled = true;
		public bool hasSnapshot;
	}
}
