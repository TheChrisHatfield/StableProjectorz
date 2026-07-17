using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Validated runtime color tokens for add-on UI. Core UI may opt in by reading tokens
	/// or subscribing to <see cref="ThemeChanged"/>; this class never scans arbitrary scene UI.
	/// </summary>
	public static class SpzUiThemeOps {

		public const string DefaultThemeId = "stableprojectorz-default";
		public const string ThemeApiVersion = "1.12";
		const int kMaxThemeIdChars = 64;
		const int kMaxLabelChars = 128;
		const int kMaxRegisteredThemes = 32;

		static readonly string[] TokenSchema = {
			"panel_bg", "control_bg", "field_bg", "accent",
			"text_primary", "text_muted", "handle",
			"success", "danger", "border", "tab_active", "selection",
		};

		// P2 promoted all former reserved role names into TokenSchema.
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
				["token_schema"] = new JArray(TokenSchema),
				["reserved_token_names"] = new JArray(ReservedTokenNames),
				["surfaces"] = BuildSurfaces(),
				["addon_rpc_theme_version"] = ThemeApiVersion,
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
				error = "tokens must contain at least one supported color";
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
				error = "tokens must contain at least one supported color";
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

			foreach (var text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
				if (text == null)
					continue;
				text.color = string.Equals(text.gameObject.name, "Placeholder", StringComparison.Ordinal)
					? tokens.textMuted
					: tokens.textPrimary;
			}
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

		public static void ApplyTmpColor(TMP_Text text, Color token) {
			if (text != null)
				text.color = token;
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
				Surface("addon_panels", true, "AddonUI_MGR AddonPanel_* roots"),
				Surface("command_ribbon", true, "CommandRibbon_UI strip/panels/tabs"),
				Surface("paint_tab", true, "PaintTab Collect/Krita/Layers ownership roots"),
				Surface("addon_manager", true, "AddonManager_UI; REF roles → tokens"),
				Surface("settings", true, "Settings_UI chrome; product prefs untouched"),
				Surface("viewport_statusline", true, "Viewport_StatusText RGB; sticky caller-owned"),
				Surface("viewport_ribbons", true, "LeftRibbon_UI + WorkflowRibbon_UI known controls"),
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
}
