using UnityEngine;

namespace spz {

	/// <summary>
	/// Accepts a reviewable <see cref="ValuePaintProposal"/> into the live color paint stack (Task 4 / Spec R3).
	/// Arms brush color/size/opacity via existing ribbon APIs; subsequent strokes write through
	/// <see cref="Inpaint_MaskPainter"/> → <see cref="ApplyBrushStroke_ToUvMask.Apply_into_ColorBrushTex"/>.
	/// Does not invent a parallel painter and does not silent-overwrite inactive layers.
	/// </summary>
	public static class ValuePaintProposalApplier {

		static bool _armed;
		static ValuePaintProposal _armedProposal;
		static bool _sawApplyOnArmedTarget;
		static string _lastFailReason = "";

		public static bool IsArmed => _armed;
		public static ValuePaintProposal ArmedProposal => _armedProposal;
		public static bool SawApplyOnArmedTarget => _sawApplyOnArmedTarget;
		public static string LastFailReason => _lastFailReason;

		public static Color GrayForBand(ValuePaintBand band) {
			float lum;
			switch (band) {
				case ValuePaintBand.Highlight: lum = 0.92f; break;
				case ValuePaintBand.Light: lum = 0.75f; break;
				case ValuePaintBand.Shadow: lum = 0.30f; break;
				case ValuePaintBand.AccentDark: lum = 0.10f; break;
				default: lum = 0.50f; break;
			}
			return new Color(lum, lum, lum, 1f);
		}

		/// <summary>
		/// Arm proposal onto active color paint tools. Returns false with reason if mode/target/ribbon not ready.
		/// </summary>
		public static bool TryAccept(ValuePaintProposal proposal, out string reason) {
			_lastFailReason = "";
			// Do not clear armed / saw-apply until validation passes (failed Accept must not wipe prior arm).

			var workflow = WorkflowRibbon_UI.instance;
			if (workflow == null) {
				reason = _lastFailReason = "WorkflowRibbon_UI missing";
				return false;
			}
			if (!workflow.isMode_using_img2img()) {
				reason = _lastFailReason = "Not in img2img / inpaint workflow";
				return false;
			}
			if (workflow.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor) {
				reason = _lastFailReason = "Inpaint_NoColor refused (color value proposal targets Content)";
				return false;
			}
			if (workflow.currentMode() != WorkflowRibbon_CurrMode.Inpaint_Color) {
				reason = _lastFailReason = "Expected Inpaint_Color mode; got " + workflow.currentMode();
				return false;
			}

			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) {
				reason = _lastFailReason = "SD_WorkflowOptionsRibbon_UI missing";
				return false;
			}
			if (sd.isSmudge) {
				reason = _lastFailReason = "Smudge active — refuse (normal color path only)";
				return false;
			}
			if (!sd.isPositive) {
				reason = _lastFailReason = "Erase tool active — refuse (value proposals arm paint, not erase)";
				return false;
			}

			RenderUdims target = ResolveColorPaintTarget(out string targetReason);
			if (target == null) {
				reason = _lastFailReason = targetReason;
				return false;
			}

			var opacityUi = Object.FindObjectOfType<BrushRibbon_UI_Opacity>(true);
			if (opacityUi == null) {
				reason = _lastFailReason = "BrushRibbon_UI_Opacity missing — refuse before mutating brush color/size";
				return false;
			}
			// Size lives on SD ribbon's BrushRibbon_UI_Size; null slider → SetBrushSize NREs after color already applied.
			if (BrushRibbon_UI_Size.instance == null) {
				reason = _lastFailReason = "BrushRibbon_UI_Size missing — refuse before mutating brush color";
				return false;
			}
			// Resolve hardness before any ribbon mutate (validate-then-commit). Missing UI is noted, not refused.
			var hardnessUi = Object.FindObjectOfType<BrushRibbon_UI_Hardness>(true);

			Color tint = GrayForBand(proposal.DesiredBin);
			if (!sd.SetBrushColorFromApi(tint.r, tint.g, tint.b, tint.a)) {
				reason = _lastFailReason = "SetBrushColorFromApi failed";
				return false;
			}

			// Sanitize width before ribbon mutate — Clamp01(NaN) stays NaN and can poison the size slider.
			float width01 = float.IsFinite(proposal.BrushWidthHint01) ? Mathf.Clamp01(proposal.BrushWidthHint01) : 0.5f;
			sd.SetBrushSize(width01);
			// Apply blend into effective opacity so Accept does not silently drop BlendStrength01 (Spec R2).
			float blend = float.IsFinite(proposal.BlendStrength01) ? Mathf.Clamp01(proposal.BlendStrength01) : 1f;
			float opacity = float.IsFinite(proposal.OpacityHint01) ? Mathf.Clamp01(proposal.OpacityHint01) : 0.55f;
			float effectiveOpacity = Mathf.Clamp01(opacity * blend);
			opacityUi.SetOpacity01(effectiveOpacity);

			// T7 — EdgeSoftness01 → built-in hardness (0 soft / 1 med / 2 hard). High softness = soft tip.
			int hardnessIx = Softness01ToHardnessIx(proposal.EdgeSoftness01);
			string hardnessNote = "hardnessUi=missing";
			if (hardnessUi != null) {
				if (hardnessUi.TrySetBuiltInOnly(hardnessIx)) {
					hardnessNote = "hardnessIx=" + hardnessIx + " (from edgeSoft=" +
					               (float.IsFinite(proposal.EdgeSoftness01) ? Mathf.Clamp01(proposal.EdgeSoftness01) : 0.5f).ToString("F2") + ")";
				} else {
					hardnessNote = "hardnessSkipped=customAlpha";
				}
			}

			_armedProposal = proposal;
			_armed = true;
			_sawApplyOnArmedTarget = false;
			reason = "Armed on target=" + DescribeTarget(target) + " desiredBin=" + proposal.DesiredBin
			         + " color=" + tint + " size01=" + width01.ToString("F2")
			         + " opacity01=" + effectiveOpacity.ToString("F2")
			         + " (blend=" + blend.ToString("F2") + ") " + hardnessNote;
			return true;
		}

		/// <summary>Map Spec R2 EdgeSoftness01 → built-in round tip index (0=soft, 1=medium, 2=hard).</summary>
		public static int Softness01ToHardnessIx(float edgeSoftness01) {
			float s = float.IsFinite(edgeSoftness01) ? Mathf.Clamp01(edgeSoftness01) : 0.5f;
			if (s >= 0.66f) return 0;
			if (s >= 0.33f) return 1;
			return 2;
		}

		public static void ClearArmed() {
			_armed = false;
			_sawApplyOnArmedTarget = false;
			_armedProposal = default;
		}

		/// <summary>
		/// Called from <see cref="Inpaint_MaskPainter"/> after a successful color UV apply.
		/// Verifies destin matches the resolved color Content path when a proposal is armed.
		/// Does not alter stroke math.
		/// </summary>
		public static void OnColorBrushApplied(RenderUdims destin) {
			if (!_armed || destin == null)
				return;
			var expected = ResolveColorPaintTarget(out _);
			if (expected != null && ReferenceEquals(expected, destin))
				_sawApplyOnArmedTarget = true;
		}

		static RenderUdims ResolveColorPaintTarget(out string reason) {
			reason = "";
			var painter = Inpaint_MaskPainter.instance;
			if (painter == null) {
				reason = "Inpaint_MaskPainter.instance missing";
				return null;
			}
			var target = painter.GetPaintTarget_Undo();
			if (target == null) {
				reason = "No paint target (load a 3D model / ensure active layer Content)";
				return null;
			}
			var stack = PaintLayerStack_MGR.instance;
			var active = stack?.ActiveLayer;
			if (active != null) {
				if (active.Content == null) {
					reason = "ActiveLayer exists but Content is null — refuse fallback buffer (Spec R3 layer path)";
					return null;
				}
				if (!ReferenceEquals(target, active.Content)) {
					// NoColor path or unexpected buffer — refuse color proposal write diversion
					reason = "Paint target is not ActiveLayer.Content (mode/buffer mismatch)";
					return null;
				}
			}
			return target;
		}

		static string DescribeTarget(RenderUdims target) {
			var stack = PaintLayerStack_MGR.instance;
			var active = stack?.ActiveLayer;
			if (active != null && ReferenceEquals(target, active.Content))
				return "ActiveLayer.Content";
			return "RenderUdims";
		}
	}

}
