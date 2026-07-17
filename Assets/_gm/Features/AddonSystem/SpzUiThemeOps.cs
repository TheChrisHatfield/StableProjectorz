using System;
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
		const int kMaxThemeIdChars = 64;

		public sealed class ThemeTokens {
			public Color panelBg;
			public Color controlBg;
			public Color fieldBg;
			public Color accent;
			public Color textPrimary;
			public Color textMuted;
			public Color handle;

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
		};

		static ThemeTokens _active = Defaults.Clone();
		static string _activeThemeId = DefaultThemeId;

		public static event Action ThemeChanged;

		public static string ActiveThemeId => _activeThemeId;
		public static ThemeTokens Active => _active.Clone();

		public static JObject GetThemeResult() {
			return new JObject {
				["success"] = true,
				["theme_id"] = _activeThemeId,
				["tokens"] = SerializeTokens(_active),
			};
		}

		/// <summary>
		/// Applies a palette atomically. Omitted supported tokens use built-in defaults so
		/// one add-on theme cannot accidentally inherit values from a previously active one.
		/// </summary>
		public static bool TryApplyTheme(string themeId, JObject tokenValues, out string error) {
			error = null;
			themeId = themeId != null ? themeId.Trim() : "";
			if (themeId.Length == 0 || themeId.Length > kMaxThemeIdChars) {
				error = $"theme_id must contain 1-{kMaxThemeIdChars} characters";
				return false;
			}
			if (tokenValues == null || !tokenValues.HasValues) {
				error = "tokens must contain at least one supported color";
				return false;
			}

			var candidate = Defaults.Clone();
			foreach (var property in tokenValues.Properties()) {
				if (!TryParseColor(property.Value, out var color)) {
					error = $"Invalid color for token '{property.Name}'; expected #RRGGBB or #RRGGBBAA";
					return false;
				}
				switch (property.Name) {
					case "panel_bg": candidate.panelBg = color; break;
					case "control_bg": candidate.controlBg = color; break;
					case "field_bg": candidate.fieldBg = color; break;
					case "accent": candidate.accent = color; break;
					case "text_primary": candidate.textPrimary = color; break;
					case "text_muted": candidate.textMuted = color; break;
					case "handle": candidate.handle = color; break;
					default:
						error = $"Unknown theme token: {property.Name}";
						return false;
				}
			}

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

		static JObject SerializeTokens(ThemeTokens tokens) {
			return new JObject {
				["panel_bg"] = ColorToHex(tokens.panelBg),
				["control_bg"] = ColorToHex(tokens.controlBg),
				["field_bg"] = ColorToHex(tokens.fieldBg),
				["accent"] = ColorToHex(tokens.accent),
				["text_primary"] = ColorToHex(tokens.textPrimary),
				["text_muted"] = ColorToHex(tokens.textMuted),
				["handle"] = ColorToHex(tokens.handle),
			};
		}

		static bool TryParseColor(JToken token, out Color color) {
			color = default;
			if (token == null || token.Type != JTokenType.String)
				return false;
			string value = token.ToString();
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
