using System;
using System.Collections.Generic;
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
		public const string ThemeApiVersion = "1.13";
		const int kMaxThemeIdChars = 64;
		const int kMaxLabelChars = 128;
		const int kMaxRegisteredThemes = 32;
		public const float ScaleTokenMin = 0.75f;
		public const float ScaleTokenMax = 1.5f;

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
			public float fontScale = 1f;
			public float spacingScale = 1f;

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
			fontScale = 1f,
			spacingScale = 1f,
		};

		static ThemeTokens _active = Defaults.Clone();
		static string _activeThemeId = DefaultThemeId;
		static readonly Dictionary<string, ThemePreset> RegisteredThemes =
			new Dictionary<string, ThemePreset>(StringComparer.Ordinal);

		public static event Action ThemeChanged;

		public static string ActiveThemeId => _activeThemeId;
		public static ThemeTokens Active => _active.Clone();

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
			if (hasPreset && hasTokens)
				candidate = presetTokens.Clone();
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
			return true;
		}

		public static void ResetTheme() {
			_activeThemeId = DefaultThemeId;
			_active = Defaults.Clone();
			NotifyThemeChanged();
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
			var tokens = _active;

			var rootImage = root.GetComponent<Image>();
			if (rootImage != null && root.name.StartsWith("AddonPanel_", StringComparison.Ordinal))
				rootImage.color = tokens.panelBg;

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
				if (bg != null)
					bg.color = tokens.fieldBg;
			}

			foreach (var slider in root.GetComponentsInChildren<Slider>(true)) {
				if (slider == null)
					continue;
				var bg = slider.GetComponent<Image>();
				if (bg != null)
					bg.color = tokens.fieldBg;
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
				SchemaFloat("font_scale", ScaleTokenMin, ScaleTokenMax),
				SchemaFloat("spacing_scale", ScaleTokenMin, ScaleTokenMax),
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
				["font_scale"] = tokens.fontScale,
				["spacing_scale"] = tokens.spacingScale,
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
				Surface("command_ribbon", true, "CommandRibbon_UI strip/panels/tabs; colors + font_scale"),
				Surface("paint_tab", true, "PaintTab Collect/Krita/Layers; colors + font_scale + spacing where VLG"),
				Surface("addon_manager", true, "AddonManager_UI; REF roles → tokens; font_scale + spacing_scale"),
				Surface("settings", true, "Settings_UI chrome; font_scale + spacing; product prefs untouched"),
				Surface("viewport_statusline", true, "Viewport_StatusText RGB + font_scale; sticky caller-owned"),
				Surface("viewport_ribbons", true, "LeftRibbon + WorkflowRibbon + GenButtons; colors + font_scale"),
				Surface("sd_input_panel", true, "SD_InputPanel_UI column; colors + font_scale"),
				Surface("export_save_menu", true, "ExportSave_UI_MGR buttons; colors + font_scale"),
				Surface("scene_resolution", true, "SceneResolution_MGR SAVE Nx / filters; colors + font_scale"),
				Surface("connection_panels", true, "ConnectionPanel_UI SD SERV / 3D SERV chrome; colors + font_scale"),
				Surface("right_panel_lists", true, "Art/BG IconsUI_List header+scroll; Mesh ModelsHandler_3D_UI; Art3D + ControlNet thumbs chrome only"),
				Surface("multiview_pins", true, "MultiView_Ribbon_UI + CamerasMGR_PinsZone_UI pin/TMP chrome"),
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
			target.fontScale = source.fontScale;
			target.spacingScale = source.spacingScale;
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
			if (scale < ScaleTokenMin || scale > ScaleTokenMax) {
				error = $"must be between {ScaleTokenMin} and {ScaleTokenMax}";
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
}
