using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Adaptive BoundChrome role matrix: map traditional UI structure under an ownership root
	/// to Nomad helpers. Never scans the global skeleton.
	/// </summary>
	public static partial class SpzUiThemeOps {

		/// <summary>
		/// Classify and theme traditional chrome under <paramref name="root"/>.
		/// Builtin / leave → <see cref="RestoreBoundChromeUnder"/>.
		/// </summary>
		public static void ApplyBoundChromeRolesUnder(Transform root, SpzUiThemeRoleMatrixOptions opts = default) {
			if (root == null) return;
			if (!ShouldRecolorBoundChrome) {
				RestoreBoundChromeUnder(root);
				return;
			}

			var t = Active;

			if (!opts.SkipDownloadSlides) {
				foreach (var slide in root.GetComponentsInChildren<SlideOut_Widget_UI>(true)) {
					if (slide == null) continue;
					if (IsExcluded(opts, slide)) continue;
					ApplyDownloadMoreSlideChrome(slide.transform);
				}
			}

			if (!opts.SkipDials) {
				foreach (var dial in root.GetComponentsInChildren<CircleSlider_Snapping_UI>(true)) {
					if (dial == null) continue;
					if (IsExcluded(opts, dial)) continue;
					// Dial owns fill + numeral (DialValue) — traditional CircleSlider path.
					dial.ApplyThemeTokens(t.accent, t.textPrimary);
				}
			}

			if (!opts.SkipSelectables) {
				ThemeSelectablesUnder(root, t, opts);
			}

			if (!opts.SkipInputFields) {
				foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true)) {
					if (input == null) continue;
					if (IsExcluded(opts, input)) continue;
					if (input.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
					var bg = input.GetComponent<Image>();
					if (bg != null) {
						ApplyBoundChromeGraphic(bg, t.fieldBg);
						ApplyRoundedControlSprite(bg, markEligible: true);
					}
					if (input.textComponent != null)
						ApplyRoleToTmp(input.textComponent, SpzUiThemeRole.FieldText, t, opts);
					if (input.placeholder is TMP_Text ph)
						ApplyBoundChromeTmp(ph, t.textMuted);
				}
			}

			if (!opts.SkipTmp) {
				foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
					if (tmp == null) continue;
					if (IsExcluded(opts, tmp)) continue;
					ApplyClassifiedTmp(tmp, t, opts);
				}
			}

			// Compact/NarrowDock clear label raycasts — re-assert Selectable faces after TMP pass.
			if (!opts.SkipSelectables) {
				foreach (var btn in root.GetComponentsInChildren<Button>(true)) {
					if (btn == null || IsExcluded(opts, btn)) continue;
					if (btn.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
					// Icon-as-face skips SolidSquare above — still need a hittable face before ClearNonFace.
					EnsureSelectableHitFace(btn);
					ClearNonFaceRaycastsForTheme(btn);
				}
				foreach (var tog in root.GetComponentsInChildren<Toggle>(true)) {
					if (tog == null || IsExcluded(opts, tog)) continue;
					if (tog.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
					EnsureSelectableHitFace(tog);
					ClearNonFaceRaycastsForTheme(tog);
				}
			}

			if (!opts.SkipLayoutScale) {
				foreach (var lg in root.GetComponentsInChildren<LayoutGroup>(true)) {
					if (lg == null) continue;
					if (IsExcluded(opts, lg)) continue;
					if (lg.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
					ApplyScaledLayoutGroup(lg);
				}
			}
		}

		/// <summary>
		/// Resolve role for a TMP from tag (nearest) or traditional hierarchy heuristics.
		/// </summary>
		public static SpzUiThemeRole ResolveTmpRole(TMP_Text text, bool detectPromptLabels = false) {
			if (text == null) return SpzUiThemeRole.Skip;
			var tagged = ResolveTaggedRole(text.transform);
			if (tagged != SpzUiThemeRole.Auto)
				return tagged;

			if (text.GetComponentInParent<RawImage>(true) != null)
				return SpzUiThemeRole.Skip;
			if (text.gameObject.name == "Placeholder")
				return SpzUiThemeRole.FieldText;
			if (text.GetComponentInParent<CircleSlider_Snapping_UI>(true) != null)
				return SpzUiThemeRole.DialValue;
			if (text.GetComponentInParent<SlideOut_Widget_UI>(true) != null) {
				if (text.GetComponentInParent<Button>(true) != null)
					return SpzUiThemeRole.CompactTool;
				return SpzUiThemeRole.ReadableBody;
			}
			if (text.GetComponentInParent<TMP_InputField>(true) != null)
				return SpzUiThemeRole.FieldText;

			var dd = text.GetComponentInParent<TMP_Dropdown>(true);
			if (dd != null && (ReferenceEquals(text, dd.captionText) || ReferenceEquals(text, dd.itemText)))
				return SpzUiThemeRole.ReadableBody;

			if (detectPromptLabels) {
				if (LooksLikePromptPolaritySign(text))
					return SpzUiThemeRole.PromptSign;
				if (LooksLikePromptHeader(text))
					return SpzUiThemeRole.PromptHeader;
			}

			if (text.GetComponentInParent<Button>(true) != null)
				return SpzUiThemeRole.CompactTool;
			if (text.GetComponentInParent<Toggle>(true) != null)
				return SpzUiThemeRole.ReadableBody;

			return SpzUiThemeRole.BoundChromeTmp;
		}

		/// <summary>
		/// Bind <paramref name="applyTheme"/> to ThemeChanged for this ownership root (no global scan).
		/// </summary>
		public static void RegisterOwnershipRoot(MonoBehaviour owner, Action applyTheme) {
			if (owner == null || applyTheme == null) return;
			var hub = owner.GetComponent<SpzUiThemeOwnershipHub>();
			if (hub == null)
				hub = owner.gameObject.AddComponent<SpzUiThemeOwnershipHub>();
			hub.Bind(applyTheme);
		}

		static void ThemeSelectablesUnder(Transform root, ThemeTokens t, SpzUiThemeRoleMatrixOptions opts) {
			foreach (var dd in root.GetComponentsInChildren<TMP_Dropdown>(true)) {
				if (dd == null) continue;
				if (IsExcluded(opts, dd)) continue;
				if (dd.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
				// Invisible authored hit pads — SolidSquare would opaque them and steal clicks (gen/CN litmus).
				if (dd.targetGraphic != null && dd.targetGraphic.color.a < 0.08f)
					continue;
				ApplyBoundChromeSelectable(dd, t.fieldBg, t.accent);
				if (dd.targetGraphic is Image fieldImg)
					ApplyRoundedControlSprite(fieldImg, markEligible: true);
				if (dd.captionText != null)
					ApplyRoleToTmp(dd.captionText, SpzUiThemeRole.ReadableBody, t, opts, 12f);
				if (dd.itemText != null)
					ApplyRoleToTmp(dd.itemText, SpzUiThemeRole.ReadableBody, t, opts, 12f);
			}

			foreach (var btn in root.GetComponentsInChildren<Button>(true)) {
				if (btn == null) continue;
				if (IsExcluded(opts, btn)) continue;
				if (btn.GetComponent<TMP_Dropdown>() != null) continue;
				if (btn.gameObject.name.StartsWith("Dropdown_", StringComparison.Ordinal)) continue;
				if (btn.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
				// Check BEFORE Ensure (Ensure creates a transparent BoundChromeHitFace when null).
				if (btn.targetGraphic != null && btn.targetGraphic.color.a < 0.08f)
					continue;
				// Opaque icon-as-face — SolidSquare blanks glyphs (SD input / Soft / CN litmus).
				if (SpzUiThemeOps.IsAuthoredIconFace(btn.targetGraphic)) {
					EnsureSelectableHitFace(btn);
					if (btn.targetGraphic is Image iconFace)
						ApplyBoundChromeIconTint(iconFace, t.iconTint);
					continue;
				}
				ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
				if (btn.targetGraphic is Image btnImg)
					ApplyRoundedControlSprite(btnImg, markEligible: true);
			}

			foreach (var toggle in root.GetComponentsInChildren<Toggle>(true)) {
				if (toggle == null) continue;
				if (IsExcluded(opts, toggle)) continue;
				if (toggle.GetComponentInParent<SlideOut_Widget_UI>(true) != null) continue;
				if (toggle.targetGraphic != null && toggle.targetGraphic.color.a < 0.08f)
					continue;
				if (opts.PreferFlatToolToggles) {
					Color face = toggle.isOn
						? Color.Lerp(t.tabActive, t.accent, 0.45f)
						: t.controlBg;
					ThemeFlatToolToggle(toggle, face, t.accent, t.textPrimary);
				}
				else {
					ThemeCheckboxToggle(toggle, t.controlBg, t.accent, t.success);
				}
			}
		}

		static void ApplyClassifiedTmp(TextMeshProUGUI tmp, ThemeTokens t, SpzUiThemeRoleMatrixOptions opts) {
			// Dial / input / slide / dropdown caption already handled — skip re-pass.
			if (tmp.GetComponentInParent<CircleSlider_Snapping_UI>(true) != null) return;
			if (tmp.GetComponentInParent<TMP_InputField>(true) != null) return;
			if (tmp.GetComponentInParent<SlideOut_Widget_UI>(true) != null) return;
			var dd = tmp.GetComponentInParent<TMP_Dropdown>(true);
			if (dd != null && (ReferenceEquals(tmp, dd.captionText) || ReferenceEquals(tmp, dd.itemText)))
				return;
			// Flat tool toggles already Compact their labels.
			if (opts.PreferFlatToolToggles && tmp.GetComponentInParent<Toggle>(true) != null)
				return;

			SpzUiThemeRole role = ResolveTmpRole(tmp, opts.DetectPromptLabels);
			ApplyRoleToTmp(tmp, role, t, opts);
		}

		static void ApplyRoleToTmp(TMP_Text text, SpzUiThemeRole role, ThemeTokens t, float fallbackPt = 14f) {
			ApplyRoleToTmp(text, role, t, default, fallbackPt);
		}

		static void ApplyRoleToTmp(TMP_Text text, SpzUiThemeRole role, ThemeTokens t, SpzUiThemeRoleMatrixOptions opts, float fallbackPt = 14f) {
			if (text == null || role == SpzUiThemeRole.Skip || role == SpzUiThemeRole.StripStack)
				return;
			switch (role) {
				case SpzUiThemeRole.DialValue:
					ApplyBoundChromeDialValueTmp(text, t.textPrimary, fallbackPt);
					text.raycastTarget = false;
					break;
				case SpzUiThemeRole.CompactTool:
					ApplyBoundChromeCompactToolLabelTmp(text, t.textPrimary, 11f);
					break;
				case SpzUiThemeRole.ReadableBody:
					ApplyBoundChromeReadableBodyTmp(text, t.textPrimary, fallbackPt);
					break;
				case SpzUiThemeRole.NarrowDock:
					ApplyBoundChromeNarrowDockLabelTmp(text, t.textPrimary, 11f);
					break;
				case SpzUiThemeRole.PromptHeader:
					ApplyBoundChromePromptHeaderTmp(text, t.textPrimary, 13f);
					break;
				case SpzUiThemeRole.PromptSign:
					ApplyBoundChromePromptPolaritySignTmp(text, t.textPrimary);
					break;
				case SpzUiThemeRole.FieldText:
					ApplyBoundChromeTmp(text, t.textPrimary, fallbackPt);
					break;
				case SpzUiThemeRole.BoundChromeTmp:
				case SpzUiThemeRole.Auto:
				default:
					if (opts.CompactLooseLabels)
						ApplyBoundChromeCompactToolLabelTmp(text, t.textPrimary, 11f);
					else {
						ApplyBoundChromeTmp(text, t.textPrimary, fallbackPt);
						text.characterSpacing = 0f;
					}
					break;
			}
		}

		static SpzUiThemeRole ResolveTaggedRole(Transform node) {
			if (node == null) return SpzUiThemeRole.Auto;
			var tag = node.GetComponentInParent<SpzUiThemeRoleTag>(true);
			if (tag == null) return SpzUiThemeRole.Auto;
			return tag.role;
		}

		static bool IsExcluded(SpzUiThemeRoleMatrixOptions opts, Component c) {
			return opts.Exclude != null && c != null && opts.Exclude(c);
		}

		static bool LooksLikePromptPolaritySign(TMP_Text text) {
			if (text == null) return false;
			string s = (text.text ?? "").Trim();
			return s == "+" || s == "-" || s == "−" || s == "–";
		}

		static bool LooksLikePromptHeader(TMP_Text text) {
			if (text == null || LooksLikePromptPolaritySign(text)) return false;
			string body = text.text ?? "";
			if (body.IndexOf("prompt", StringComparison.OrdinalIgnoreCase) < 0)
				return false;
			string n = text.gameObject.name ?? "";
			return n.IndexOf("header", StringComparison.OrdinalIgnoreCase) >= 0
			       || body.TrimStart().StartsWith("prompt", StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>Per-root ThemeChanged hub used by <see cref="SpzUiThemeOps.RegisterOwnershipRoot"/>.</summary>
	sealed class SpzUiThemeOwnershipHub : MonoBehaviour {
		Action _apply;
		bool _subscribed;

		public void Bind(Action applyTheme) {
			_apply = applyTheme;
			EnsureSubscribed();
			SafeApply();
		}

		void OnEnable() {
			EnsureSubscribed();
			SafeApply();
		}

		void OnDisable() {
			if (!_subscribed) return;
			SpzUiThemeOps.ThemeChanged -= OnThemeChanged;
			_subscribed = false;
		}

		void OnDestroy() {
			OnDisable();
			_apply = null;
		}

		void EnsureSubscribed() {
			if (_subscribed) return;
			SpzUiThemeOps.ThemeChanged += OnThemeChanged;
			_subscribed = true;
		}

		void OnThemeChanged() => SafeApply();

		void SafeApply() {
			try {
				_apply?.Invoke();
			}
			catch (Exception) {
				// Ownership root must not break ThemeChanged fan-out.
			}
		}
	}

}
