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
		static bool _armedViaLive;
		static bool _suppressLiveSoftArm;
		static string _lastFailReason = "";
		static int _lastLiveHardnessIx = int.MinValue;

		public static bool IsArmed => _armed;
		public static ValuePaintProposal ArmedProposal => _armedProposal;
		public static bool SawApplyOnArmedTarget => _sawApplyOnArmedTarget;
		public static bool ArmedViaLive => _armedViaLive;
		public static string LastFailReason => _lastFailReason;

		/// <summary>Block live soft-arm until Live is toggled on again (Dismiss / Accept lock).</summary>
		public static void SuppressLiveSoftArm() => _suppressLiveSoftArm = true;
		public static void ClearLiveSoftArmSuppress() => _suppressLiveSoftArm = false;

		public static Color GrayForBand(ValuePaintBand band) {
			float lum = LuminanceForBand(band);
			return new Color(lum, lum, lum, 1f);
		}

		/// <summary>Representative Rec.709 luminance for a value band (Spec R1).</summary>
		public static float LuminanceForBand(ValuePaintBand band) {
			switch (band) {
				case ValuePaintBand.Highlight: return 0.92f;
				case ValuePaintBand.Light: return 0.75f;
				case ValuePaintBand.Shadow: return 0.30f;
				case ValuePaintBand.AccentDark: return 0.10f;
				default: return 0.50f;
			}
		}

		/// <summary>
		/// Shift <paramref name="baseColor"/> to the desired value band while keeping hue/chroma ratios.
		/// Value Assist predicts tonal steps of the artist's color — not a gray replacement.
		/// </summary>
		public static Color ColorAtDesiredValue(Color baseColor, ValuePaintBand desired) {
			float targetLum = LuminanceForBand(desired);
			float r = float.IsFinite(baseColor.r) ? baseColor.r : 0.5f;
			float g = float.IsFinite(baseColor.g) ? baseColor.g : 0.5f;
			float b = float.IsFinite(baseColor.b) ? baseColor.b : 0.5f;
			float cur = DeterministicValuePaintAssist.Luminance01(new Color(r, g, b, 1f));
			if (cur < 1e-4f)
				return new Color(targetLum, targetLum, targetLum, 1f);

			float scale = targetLum / cur;
			Color c = new Color(
				Mathf.Clamp01(r * scale),
				Mathf.Clamp01(g * scale),
				Mathf.Clamp01(b * scale),
				1f);
			// If clamp crushed luminance above target (rare), scale back down.
			float after = DeterministicValuePaintAssist.Luminance01(c);
			if (after > 1e-4f && after > targetLum + 0.015f) {
				float s2 = targetLum / after;
				c = new Color(Mathf.Clamp01(c.r * s2), Mathf.Clamp01(c.g * s2), Mathf.Clamp01(c.b * s2), 1f);
			}
			return c;
		}

		/// <summary>
		/// Arm proposal onto active color paint tools. Returns false with reason if mode/target/ribbon not ready.
		/// </summary>
		public static bool TryAccept(ValuePaintProposal proposal, out string reason) {
			_lastFailReason = "";
			// Do not clear armed / saw-apply until validation passes (failed Accept must not wipe prior arm).

			if (!PaintTab_ValueAssistOptions.Enabled) {
				reason = _lastFailReason = "Value Assist is off (Paint tab → Tool Options)";
				return false;
			}

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

			Color tint = ColorAtDesiredValue(sd.brushColor, proposal.DesiredBin);
			if (!sd.SetBrushColorFromApi(tint.r, tint.g, tint.b, tint.a)) {
				reason = _lastFailReason = "SetBrushColorFromApi failed";
				return false;
			}

			// Sanitize width before ribbon mutate — Clamp01(NaN) stays NaN and can poison the size slider.
			float proposedWidth = float.IsFinite(proposal.BrushWidthHint01) ? Mathf.Clamp01(proposal.BrushWidthHint01) : 0.5f;
			float liveWidth = BrushRibbon_UI_Size.GetBrushSize01();
			if (!float.IsFinite(liveWidth)) liveWidth = proposedWidth;
			float sizeInf = PaintTab_ValueAssistOptions.SizeInfluence01;
			if (!float.IsFinite(sizeInf)) sizeInf = 1f;
			sizeInf = Mathf.Clamp01(sizeInf);
			float width01 = Mathf.Lerp(liveWidth, proposedWidth, sizeInf);
			if (!float.IsFinite(width01)) width01 = proposedWidth;
			sd.SetBrushSize(width01);
			// Apply blend into effective opacity so Accept does not silently drop BlendStrength01 (Spec R2).
			float blend = float.IsFinite(proposal.BlendStrength01) ? Mathf.Clamp01(proposal.BlendStrength01) : 1f;
			float blendOpt = PaintTab_ValueAssistOptions.Blend01;
			if (!float.IsFinite(blendOpt)) blendOpt = 1f;
			blend = Mathf.Clamp01(blend * Mathf.Clamp01(blendOpt));
			float opacity = float.IsFinite(proposal.OpacityHint01) ? Mathf.Clamp01(proposal.OpacityHint01) : 0.55f;
			float proposedOpacity = Mathf.Clamp01(opacity * blend);
			float liveOpacity = opacityUi.Opacity01;
			if (!float.IsFinite(liveOpacity)) liveOpacity = proposedOpacity;
			float opInf = PaintTab_ValueAssistOptions.OpacityInfluence01;
			if (!float.IsFinite(opInf)) opInf = 1f;
			opInf = Mathf.Clamp01(opInf);
			float effectiveOpacity = Mathf.Lerp(liveOpacity, proposedOpacity, opInf);
			if (!float.IsFinite(effectiveOpacity)) effectiveOpacity = proposedOpacity;
			opacityUi.SetOpacity01(effectiveOpacity);

			// T7 — EdgeSoftness01 → built-in hardness (0 soft / 1 med / 2 hard). High softness = soft tip.
			string hardnessNote = "hardnessSkipped=off";
			if (PaintTab_ValueAssistOptions.ApplyHardness) {
				int hardnessIx = Softness01ToHardnessIx(proposal.EdgeSoftness01);
				hardnessNote = "hardnessUi=missing";
				if (hardnessUi != null) {
					if (hardnessUi.TrySetBuiltInOnly(hardnessIx)) {
						hardnessNote = "hardnessIx=" + hardnessIx + " (from edgeSoft=" +
						               (float.IsFinite(proposal.EdgeSoftness01) ? Mathf.Clamp01(proposal.EdgeSoftness01) : 0.5f).ToString("F2") + ")";
					} else {
						hardnessNote = "hardnessSkipped=customAlpha";
					}
				}
			}

			_armedProposal = proposal;
			_armed = true;
			_armedViaLive = false;
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
			_armedViaLive = false;
			_armedProposal = default;
			_lastLiveHardnessIx = int.MinValue;
		}

		/// <summary>
		/// Drop soft live-arm only. Does not clear a user Accept arm (Propose → Accept).
		/// </summary>
		public static void ClearArmedIfLiveSoftArm() {
			if (_armedViaLive)
				ClearArmed();
		}

		/// <summary>
		/// Soft-arm from live under-cursor prediction: quiet color update (no mode spam),
		/// optional size when influence &amp; delta warrant it. Skips opacity UI (status spam).
		/// </summary>
		public static bool TryLiveArm(ValuePaintProposal proposal, out string reason) {
			_lastFailReason = "";
			if (!PaintTab_ValueAssistOptions.Enabled || !PaintTab_ValueAssistOptions.LivePredict) {
				reason = _lastFailReason = "Value Assist live predict off";
				return false;
			}
			if (_suppressLiveSoftArm) {
				reason = _lastFailReason = "live soft-arm suppressed";
				return false;
			}
			// Do not overwrite a user Accept arm — Live must wait until ClearArmed / Live toggle.
			if (_armed && !_armedViaLive) {
				reason = _lastFailReason = "accept arm active";
				return false;
			}

			var workflow = WorkflowRibbon_UI.instance;
			if (workflow == null || !workflow.isMode_using_img2img()
			    || workflow.currentMode() != WorkflowRibbon_CurrMode.Inpaint_Color) {
				reason = _lastFailReason = "not Inpaint_Color";
				return false;
			}

			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null || sd.isSmudge || !sd.isPositive) {
				reason = _lastFailReason = "tool refused";
				return false;
			}

			if (ResolveColorPaintTarget(out string targetReason) == null) {
				reason = _lastFailReason = targetReason;
				return false;
			}

			Color live = sd.brushColor;
			Color tint = ColorAtDesiredValue(live, proposal.DesiredBin);
			// Soft blend toward predicted value step using Blend01 (0 = leave color alone).
			float blendOpt = PaintTab_ValueAssistOptions.Blend01;
			if (!float.IsFinite(blendOpt)) blendOpt = 1f;
			blendOpt = Mathf.Clamp01(blendOpt);
			Color applied = Color.Lerp(live, tint, blendOpt);
			applied.a = 1f;
			if (!sd.SetBrushColorQuietFromApi(applied.r, applied.g, applied.b, applied.a)) {
				reason = _lastFailReason = "SetBrushColorQuietFromApi failed";
				return false;
			}

			float sizeInf = PaintTab_ValueAssistOptions.SizeInfluence01;
			if (float.IsFinite(sizeInf) && sizeInf > 0.02f && BrushRibbon_UI_Size.instance != null) {
				float proposedWidth = float.IsFinite(proposal.BrushWidthHint01) ? Mathf.Clamp01(proposal.BrushWidthHint01) : 0.5f;
				float liveWidth = BrushRibbon_UI_Size.GetBrushSize01();
				if (!float.IsFinite(liveWidth)) liveWidth = proposedWidth;
				float width01 = Mathf.Lerp(liveWidth, proposedWidth, Mathf.Clamp01(sizeInf));
				if (float.IsFinite(width01) && Mathf.Abs(width01 - liveWidth) > 0.015f)
					sd.SetBrushSize(width01);
			}

			if (PaintTab_ValueAssistOptions.ApplyHardness) {
				int hardnessIx = Softness01ToHardnessIx(proposal.EdgeSoftness01);
				if (hardnessIx != _lastLiveHardnessIx) {
					var hardnessUi = Object.FindObjectOfType<BrushRibbon_UI_Hardness>(true);
					if (hardnessUi != null && hardnessUi.TrySetBuiltInOnly(hardnessIx))
						_lastLiveHardnessIx = hardnessIx;
				}
			}

			bool sameDesiredArmed = _armed && _armedProposal.DesiredBin == proposal.DesiredBin;
			_armedProposal = proposal;
			_armed = true;
			_armedViaLive = true;
			// Same-band live ticks must not wipe SawApply — only a new desired bin starts a fresh cycle.
			if (!sameDesiredArmed)
				_sawApplyOnArmedTarget = false;
			reason = "live " + proposal.DesiredBin;
			return true;
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
			if (expected != null && ReferenceEquals(expected, destin)) {
				_sawApplyOnArmedTarget = true;
				// Accept lock is for the first stroke. After paint lands, demote so Live can
				// resume without requiring Dismiss (Dismiss still clears color arm entirely).
				if (!_armedViaLive)
					_armedViaLive = true;
			}
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
