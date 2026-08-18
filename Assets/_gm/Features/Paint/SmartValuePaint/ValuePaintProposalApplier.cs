using UnityEngine;

namespace spz {

	/// <summary>
	/// Accepts a reviewable <see cref="ValuePaintProposal"/> into the live color paint stack (Task 4 / Spec R3).
	/// Arms brush color / opacity / hardness via ribbon APIs, and loops
	/// <see cref="ValuePaintProposal.BrushWidthHint01"/> into canonical
	/// <see cref="BrushRibbon_UI_Size"/> (no parallel Size dial in Value Assist UI).
	/// Subsequent strokes write through
	/// <see cref="Inpaint_MaskPainter"/> → <see cref="ApplyBrushStroke_ToUvMask.Apply_into_ColorBrushTex"/>.
	/// Does not invent a parallel painter and does not silent-overwrite inactive layers.
	/// </summary>
	public static class ValuePaintProposalApplier {

		static bool _armed;
		static ValuePaintProposal _armedProposal;
		static bool _sawApplyOnArmedTarget;
		static bool _armedViaLive;
		static bool _suppressLiveSoftArm;
		static Color _liveChromaBase;
		static bool _haveLiveChromaBase;
		static string _lastFailReason = "";
		static int _lastLiveHardnessIx = int.MinValue;

		// User brush state captured at the start of a Live soft-arm session, restored when the
		// session ends (Live off / Dismiss / Assist off). Accept commits instead (snapshot dropped).
		static bool _haveUserBrushSnapshot;
		static Color _snapshotColor = Color.gray;
		static float _snapshotSize01 = float.NaN;
		static float _snapshotOpacity01 = -1f;
		static int _snapshotHardnessIx = -1;
		static bool _snapshotHardnessWasBuiltIn;
		// Last size/opacity WE wrote — detects manual dial edits between live ticks (adopt as new anchor).
		static float _lastLiveAppliedSize01 = float.NaN;
		static float _lastLiveAppliedOpacity01 = -1f;
		// User manually changed hardness mid-session — stop driving hardness until the next session.
		static bool _liveHardnessUserOverride;

		public static bool IsArmed => _armed;
		public static ValuePaintProposal ArmedProposal => _armedProposal;
		public static bool SawApplyOnArmedTarget => _sawApplyOnArmedTarget;
		public static bool ArmedViaLive => _armedViaLive;
		public static string LastFailReason => _lastFailReason;

		/// <summary>Block live soft-arm until Live is toggled on again (Dismiss / Accept lock).</summary>
		public static void SuppressLiveSoftArm() {
			RestoreUserBrushSnapshot_IfHeld();
			_suppressLiveSoftArm = true;
			_haveLiveChromaBase = false;
		}
		public static void ClearLiveSoftArmSuppress() {
			_suppressLiveSoftArm = false;
			_haveLiveChromaBase = false;
		}

		/// <summary>
		/// Call when the user explicitly picks a brush color (picker / swatch / eyedropper / load).
		/// Re-bases the Live chroma so live value steps follow the NEW selection instead of the color
		/// locked at session start, and updates the restore snapshot so ending Live returns this pick.
		/// </summary>
		public static void NotifyUserBrushColorChanged(Color c) {
			c.a = 1f;
			if (_haveLiveChromaBase)
				_liveChromaBase = c;
			if (_haveUserBrushSnapshot)
				_snapshotColor = c;
		}

		/// <summary>
		/// User chroma for Propose/Accept while Live is soft-arming the ribbon.
		/// Prefer this over reading <c>sd.brushColor</c>, which is already value-remapped and would
		/// make Propose→Accept double-shift hue/value away from the artist's pick.
		/// </summary>
		public static bool TryGetUserChromaBase(out Color chroma) {
			if (_haveLiveChromaBase) {
				chroma = _liveChromaBase;
				chroma.a = 1f;
				return true;
			}
			if (_haveUserBrushSnapshot) {
				chroma = _snapshotColor;
				chroma.a = 1f;
				return true;
			}
			chroma = default;
			return false;
		}

		// Live can re-arm every frame during a stroke, and FindObjectOfType(true) is a full scene
		// scan including inactive objects. Cache the ribbon widgets; Unity's fake-null on a
		// destroyed object makes this self-heal across scene reloads / UI rebuilds.
		static BrushRibbon_UI_Opacity _opacityUiCache;
		static BrushRibbon_UI_Hardness _hardnessUiCache;

		static BrushRibbon_UI_Opacity OpacityUi() {
			if (_opacityUiCache == null)
				_opacityUiCache = Object.FindObjectOfType<BrushRibbon_UI_Opacity>(true);
			return _opacityUiCache;
		}

		static BrushRibbon_UI_Hardness HardnessUi() {
			if (_hardnessUiCache == null)
				_hardnessUiCache = Object.FindObjectOfType<BrushRibbon_UI_Hardness>(true);
			return _hardnessUiCache;
		}

		/// <summary>Snapshot user brush state before the first Live mutation of this session.</summary>
		static void CaptureUserBrushSnapshot_IfNeeded(SD_WorkflowOptionsRibbon_UI sd) {
			if (_haveUserBrushSnapshot) return;
			_lastLiveAppliedSize01 = float.NaN;
			_lastLiveAppliedOpacity01 = -1f;
			_liveHardnessUserOverride = false;
			_snapshotColor = sd.brushColor;
			_snapshotColor.a = 1f;
			float size = BrushRibbon_UI_Size.GetBrushSize01();
			_snapshotSize01 = float.IsFinite(size) && BrushRibbon_UI_Size.instance != null ? size : float.NaN;
			var opacityUi = OpacityUi();
			_snapshotOpacity01 = opacityUi != null && float.IsFinite(opacityUi.Opacity01) ? Mathf.Clamp01(opacityUi.Opacity01) : -1f;
			var hardnessUi = HardnessUi();
			if (hardnessUi != null && !hardnessUi.IsUsingCustomAlpha()) {
				_snapshotHardnessWasBuiltIn = true;
				_snapshotHardnessIx = hardnessUi.hardnessIx;
			} else {
				// Custom alpha: live never overwrites it (TrySetBuiltInOnly refuses) — nothing to restore.
				_snapshotHardnessWasBuiltIn = false;
				_snapshotHardnessIx = -1;
			}
			_haveUserBrushSnapshot = true;
		}

		/// <summary>Restore user brush state captured at Live session start (Live off / Dismiss / Assist off).</summary>
		static void RestoreUserBrushSnapshot_IfHeld() {
			if (!_haveUserBrushSnapshot) return;
			_haveUserBrushSnapshot = false;
			_lastLiveAppliedSize01 = float.NaN;
			_lastLiveAppliedOpacity01 = -1f;
			_liveHardnessUserOverride = false;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return;
			sd.SetBrushColorQuietFromApi(_snapshotColor.r, _snapshotColor.g, _snapshotColor.b, 1f);
			// Size was soft-armed into BrushRibbon_UI_Size — restore the pre-Live traditional size.
			if (float.IsFinite(_snapshotSize01) && BrushRibbon_UI_Size.instance != null)
				sd.SetBrushSize(_snapshotSize01);
			if (_snapshotOpacity01 >= 0f) {
				var opacityUi = OpacityUi();
				if (opacityUi != null)
					opacityUi.SetOpacity01(_snapshotOpacity01, quiet: true);
			}
			if (_snapshotHardnessWasBuiltIn && _snapshotHardnessIx >= 0) {
				var hardnessUi = HardnessUi();
				if (hardnessUi != null)
					hardnessUi.TrySetBuiltInOnly(_snapshotHardnessIx);
			}
		}

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
		/// Floor on saturation kept when a bright band forces desaturation: a red brush stepping to
		/// Highlight becomes a light red, never near-white. 1 = never desaturate, 0 = old white-lift.
		/// </summary>
		public const float MinChromaKeep01 = 0.45f;

		/// <summary>
		/// Shift <paramref name="baseColor"/> to the desired value band while preserving hue and chroma.
		/// Value Assist predicts tonal steps of the artist's color — not a gray/white replacement.
		/// Pure value (HSV V) moves first; only if a saturated hue cannot physically reach a bright band
		/// (pure red tops out at luminance ~0.21) does it desaturate, and never below
		/// <see cref="MinChromaKeep01"/> of the original saturation — hitting exact band luminance is
		/// less important than the stroke staying recognizably the artist's color.
		/// </summary>
		public static Color ColorAtDesiredValue(Color baseColor, ValuePaintBand desired) {
			float targetLum = LuminanceForBand(desired);
			float r = float.IsFinite(baseColor.r) ? Mathf.Clamp01(baseColor.r) : 0.5f;
			float g = float.IsFinite(baseColor.g) ? Mathf.Clamp01(baseColor.g) : 0.5f;
			float b = float.IsFinite(baseColor.b) ? Mathf.Clamp01(baseColor.b) : 0.5f;
			Color.RGBToHSV(new Color(r, g, b, 1f), out float hue, out float sat, out float _);

			float lumAtFullValue = DeterministicValuePaintAssist.Luminance01(Color.HSVToRGB(hue, sat, 1f));
			if (lumAtFullValue < 1e-4f)
				return new Color(targetLum, targetLum, targetLum, 1f);

			// Reachable by value alone → hue and saturation stay exactly the artist's pick.
			float vNeeded = targetLum / lumAtFullValue;
			if (vNeeded <= 1f) {
				Color c = Color.HSVToRGB(hue, sat, vNeeded);
				c.a = 1f;
				return c;
			}

			// Bright band beyond this hue's value range: desaturate at full value, minimally,
			// bounded by MinChromaKeep01. Luminance at V=1 rises monotonically as saturation falls.
			float satFloor = sat * MinChromaKeep01;
			float lumAtFloor = DeterministicValuePaintAssist.Luminance01(Color.HSVToRGB(hue, satFloor, 1f));
			float chosenSat;
			if (lumAtFloor <= targetLum) {
				// Even max allowed desaturation stays below target — chroma identity wins over band accuracy.
				chosenSat = satFloor;
			} else {
				float lo = satFloor, hi = sat; // lum(lo) > targetLum > lum(hi)
				for (int i = 0; i < 14; i++) {
					float mid = 0.5f * (lo + hi);
					float lumMid = DeterministicValuePaintAssist.Luminance01(Color.HSVToRGB(hue, mid, 1f));
					if (lumMid > targetLum) lo = mid; else hi = mid;
				}
				chosenSat = 0.5f * (lo + hi);
			}
			Color outC = Color.HSVToRGB(hue, chosenSat, 1f);
			outC.a = 1f;
			return outC;
		}

		/// <summary>
		/// Arm proposal onto active color paint tools. Returns false with reason if mode/target/ribbon not ready.
		/// <paramref name="baseColor"/> is the Propose-time brush color; when omitted, uses the live brush
		/// (which may already have been soft-armed by Live — prefer passing the snapshot).
		/// </summary>
		public static bool TryAccept(ValuePaintProposal proposal, out string reason) {
			return TryAccept(proposal, default, useBrushAsBase: true, out reason);
		}

		public static bool TryAccept(ValuePaintProposal proposal, Color proposeBaseColor, out string reason) {
			return TryAccept(proposal, proposeBaseColor, useBrushAsBase: false, out reason);
		}

		static bool TryAccept(ValuePaintProposal proposal, Color proposeBaseColor, bool useBrushAsBase, out string reason) {
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

			var opacityUi = OpacityUi();
			if (opacityUi == null) {
				reason = _lastFailReason = "BrushRibbon_UI_Opacity missing — refuse before mutating brush color/opacity/size";
				return false;
			}
			// Width hint is applied into canonical SPZ size (BrushRibbon_UI_Size) — not a VA-only Size dial.
			if (BrushRibbon_UI_Size.instance == null) {
				reason = _lastFailReason = "BrushRibbon_UI_Size missing — refuse before mutating brush color";
				return false;
			}
			// Resolve hardness before any ribbon mutate (validate-then-commit). Missing UI is noted, not refused.
			var hardnessUi = HardnessUi();

			Color baseCol = useBrushAsBase ? sd.brushColor : proposeBaseColor;
			Color tint = ColorAtDesiredValue(baseCol, proposal.DesiredBin);
			if (!sd.SetBrushColorFromApi(tint.r, tint.g, tint.b, tint.a)) {
				reason = _lastFailReason = "SetBrushColorFromApi failed";
				return false;
			}
			// SetBrushColorFromApi routes through the palette path, which treats the write as a
			// user pick and re-bases the Live chroma to OUR remapped tint. Re-base back to the
			// artist's chroma so Live (resuming after the first stroke) never remaps its own output.
			if (_haveLiveChromaBase) {
				_liveChromaBase = baseCol;
				_liveChromaBase.a = 1f;
			}

			float width01 = SanitizeBrushWidthHint01(proposal.BrushWidthHint01);
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
			// Accept is an explicit commit — the accepted brush state must survive session end.
			_haveUserBrushSnapshot = false;
			_lastLiveAppliedSize01 = float.NaN;
			_lastLiveAppliedOpacity01 = -1f;
			reason = "Armed on target=" + DescribeTarget(target) + " desiredBin=" + proposal.DesiredBin
			         + " color=" + tint + " size01=" + width01.ToString("F2")
			         + " opacity01=" + effectiveOpacity.ToString("F2")
			         + " (blend=" + blend.ToString("F2") + ") " + hardnessNote
			         + " size→BrushRibbon_UI_Size";
			return true;
		}

		/// <summary>
		/// Loop proposal width into canonical SPZ brush size. Implementation stays in Value Assist;
		/// the write target is always <see cref="BrushRibbon_UI_Size"/> via the SD ribbon setter.
		/// </summary>
		public static float SanitizeBrushWidthHint01(float brushWidthHint01) {
			return float.IsFinite(brushWidthHint01) ? Mathf.Clamp01(brushWidthHint01) : 0.5f;
		}

		/// <summary>
		/// Soft-arm Live: push width hint into <see cref="BrushRibbon_UI_Size"/> while respecting
		/// traditional size edits ([ ] / Shift+RMB / size slider) as the new session anchor.
		/// Accept applies the hint at full strength; Live uses a fixed soft factor (no Size dial).
		/// </summary>
		const float LiveSizeSoftArm01 = 0.35f;

		static void SoftArmBrushWidthIntoSpzSize(SD_WorkflowOptionsRibbon_UI sd, ValuePaintProposal proposal) {
			if (sd == null || BrushRibbon_UI_Size.instance == null) return;
			float proposedWidth = SanitizeBrushWidthHint01(proposal.BrushWidthHint01);
			float liveWidth = BrushRibbon_UI_Size.GetBrushSize01();
			if (!float.IsFinite(liveWidth)) liveWidth = proposedWidth;
			// User moved traditional size since our last write? Adopt it as the new session anchor
			// (and restore target) instead of yanking the slider every tick.
			if (float.IsFinite(_lastLiveAppliedSize01) && Mathf.Abs(liveWidth - _lastLiveAppliedSize01) > 0.01f)
				_snapshotSize01 = liveWidth;
			float anchor = float.IsFinite(_snapshotSize01) ? _snapshotSize01 : liveWidth;
			float width01 = Mathf.Lerp(anchor, proposedWidth, LiveSizeSoftArm01);
			if (float.IsFinite(width01) && Mathf.Abs(width01 - liveWidth) > 0.015f) {
				sd.SetBrushSize(width01);
				_lastLiveAppliedSize01 = width01;
			} else {
				_lastLiveAppliedSize01 = liveWidth;
			}
		}

		/// <summary>Map Spec R2 EdgeSoftness01 → built-in round tip index (0=soft, 1=medium, 2=hard).</summary>
		public static int Softness01ToHardnessIx(float edgeSoftness01) {
			float s = float.IsFinite(edgeSoftness01) ? Mathf.Clamp01(edgeSoftness01) : 0.5f;
			if (s >= 0.66f) return 0;
			if (s >= 0.33f) return 1;
			return 2;
		}

		public static void ClearArmed() {
			// End of assist session — hand the brush back exactly as the user had set it.
			// (Accept drops the snapshot on success, so an accepted state is never rolled back.)
			RestoreUserBrushSnapshot_IfHeld();
			_armed = false;
			_sawApplyOnArmedTarget = false;
			_armedViaLive = false;
			_armedProposal = default;
			_lastLiveHardnessIx = int.MinValue;
			_haveLiveChromaBase = false;
		}

		/// <summary>
		/// Drop soft live-arm only. Does not clear a user Accept arm (Propose → Accept).
		/// Always releases an orphan Live brush snapshot (capture-then-fail / never-armed session)
		/// so the next Live turn-on does not restore a stale prior brush.
		/// </summary>
		public static void ClearArmedIfLiveSoftArm() {
			if (_armedViaLive)
				ClearArmed();
			else
				RestoreUserBrushSnapshot_IfHeld();
		}

		/// <summary>
		/// Live soft-arm is only legal on Add-paint in Inpaint_Color. Smudge / Erase / other
		/// workflows must not keep the last Live-written opacity / hardness / color / size.
		/// </summary>
		public static bool IsLiveToolAndModeEligible() {
			var workflow = WorkflowRibbon_UI.instance;
			if (workflow == null || !workflow.isMode_using_img2img()
			    || workflow.currentMode() != WorkflowRibbon_CurrMode.Inpaint_Color)
				return false;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			return sd != null && !sd.isSmudge && sd.isPositive;
		}

		static bool _leaveHooksWired;

		/// <summary>
		/// Subscribe once to direction + workflow changes so a tool switch restores the
		/// pre-Live brush even when the sampler is not ticking (e.g. cursor off viewport).
		/// Safe to call repeatedly.
		/// </summary>
		public static void EnsureLiveLeaveHooks() {
			if (_leaveHooksWired) return;
			_leaveHooksWired = true;
			BrushRibbon_UI_Direction.OnDirectionToggleChanged += LeaveLiveSoftArmIfToolIneligible;
			WorkflowRibbon_UI._Act_OnModeChanged += OnWorkflowModeMaybeLeftLive;
		}

		static void OnWorkflowModeMaybeLeftLive(WorkflowRibbon_CurrMode _) =>
			LeaveLiveSoftArmIfToolIneligible();

		/// <summary>
		/// B2.2c — when the active tool/mode leaves Live-eligible, restore the pre-Live brush
		/// snapshot and clear Live UI state. A user Accept arm is preserved. Idempotent.
		/// </summary>
		public static void LeaveLiveSoftArmIfToolIneligible() {
			if (IsLiveToolAndModeEligible()) return;
			bool hadLiveUi = ValuePaintLivePredictor.HasLastProposal
				|| !string.IsNullOrEmpty(ValuePaintLivePredictor.LastRefusalReason);
			bool hadSoftArm = _armedViaLive || _haveUserBrushSnapshot;
			if (!hadSoftArm && !hadLiveUi) return;
			ClearArmedIfLiveSoftArm();
			ValuePaintLivePredictor.ClearLiveUiState();
		}

		/// <summary>
		/// Soft-arm from live under-cursor prediction: quiet color update (no mode spam),
		/// optional size when influence &amp; delta warrant it. Skips opacity UI (status spam).
		/// </summary>
		public static bool TryLiveArm(ValuePaintProposal proposal, out string reason) {
			_lastFailReason = "";
			EnsureLiveLeaveHooks();
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
			// While the color picker is open the user is actively choosing — live rewrites would
			// stomp the pick under their cursor. Pause; resume after the picker closes.
			if (MouseWorkbench_Zone.instance != null && MouseWorkbench_Zone.instance.isShowing) {
				reason = _lastFailReason = "color picker open";
				return false;
			}

			if (ResolveColorPaintTarget(out string targetReason) == null) {
				reason = _lastFailReason = targetReason;
				return false;
			}

			// Capture only after all refuse gates — a pre-mutate capture left orphan snapshots
			// when SetBrushColorQuietFromApi failed or Live was toggled off before first arm.
			bool justCaptured = !_haveUserBrushSnapshot;
			CaptureUserBrushSnapshot_IfNeeded(sd);

			Color live = sd.brushColor;
			// Lock chroma to the brush color at the start of this Live session. Remapping from an
			// already-lifted/shifted brush each tick washes hue (Highlight→white lift→Shadow scale).
			// User color picks re-base this via NotifyUserBrushColorChanged.
			if (!_haveLiveChromaBase) {
				_liveChromaBase = live;
				_haveLiveChromaBase = true;
			}
			Color tint = ColorAtDesiredValue(_liveChromaBase, proposal.DesiredBin);
			// Blend01 mixes USER color vs value-mapped tint (0 = leave color alone, 1 = full remap).
			// Must anchor on _liveChromaBase: lerping from the live brush (assist's own last write)
			// converges to full tint after a few ticks and the Blend dial loses all effect.
			float blendOpt = PaintTab_ValueAssistOptions.Blend01;
			if (!float.IsFinite(blendOpt)) blendOpt = 1f;
			blendOpt = Mathf.Clamp01(blendOpt);
			Color applied = Color.Lerp(_liveChromaBase, tint, blendOpt);
			applied.a = 1f;
			if (!sd.SetBrushColorQuietFromApi(applied.r, applied.g, applied.b, applied.a)) {
				// Only discard a snapshot we created THIS call (brush not mutated yet). A mid-session
				// failure must not drop the restore target for an already-running Live soft-arm.
				if (justCaptured) {
					_haveUserBrushSnapshot = false;
					_haveLiveChromaBase = false;
					_lastLiveAppliedSize01 = float.NaN;
					_lastLiveAppliedOpacity01 = -1f;
					_liveHardnessUserOverride = false;
				}
				reason = _lastFailReason = "SetBrushColorQuietFromApi failed";
				return false;
			}

			// Loop width hint into canonical SPZ size (same owner as [ ] / Shift+RMB / size slider).
			SoftArmBrushWidthIntoSpzSize(sd, proposal);

			if (PaintTab_ValueAssistOptions.ApplyHardness && !_liveHardnessUserOverride) {
				int hardnessIx = Softness01ToHardnessIx(proposal.EdgeSoftness01);
				var hardnessUi = HardnessUi();
				if (hardnessUi != null && !hardnessUi.IsUsingCustomAlpha()) {
					// User changed hardness since capture / last assist write — their pick wins.
					// Checked every tick: nesting this under "assist wants a different index"
					// missed the override whenever the assist wanted the index it last wrote.
					bool changedSinceAssistWrite = _lastLiveHardnessIx != int.MinValue
						&& hardnessUi.hardnessIx != _lastLiveHardnessIx;
					bool changedBeforeFirstAssistWrite = _lastLiveHardnessIx == int.MinValue
						&& _snapshotHardnessWasBuiltIn
						&& hardnessUi.hardnessIx != _snapshotHardnessIx;
					if (changedSinceAssistWrite || changedBeforeFirstAssistWrite) {
						_liveHardnessUserOverride = true;
						_snapshotHardnessIx = hardnessUi.hardnessIx;
						_snapshotHardnessWasBuiltIn = true;
					} else if (hardnessIx != _lastLiveHardnessIx && hardnessUi.TrySetBuiltInOnly(hardnessIx)) {
						_lastLiveHardnessIx = hardnessIx;
					}
				}
			}

			// Live previously skipped opacity — value steps looked like one flat wash.
			float opInf = PaintTab_ValueAssistOptions.OpacityInfluence01;
			if (float.IsFinite(opInf) && opInf > 0.02f) {
				var opacityUi = OpacityUi();
				if (opacityUi != null) {
					float proposedOpacity = float.IsFinite(proposal.OpacityHint01)
						? Mathf.Clamp01(proposal.OpacityHint01) : 0.6f;
					float liveOpacity = opacityUi.Opacity01;
					if (!float.IsFinite(liveOpacity)) liveOpacity = proposedOpacity;
					// Same anchor rule as size: user opacity is the base; adopt manual changes.
					if (_lastLiveAppliedOpacity01 >= 0f && Mathf.Abs(liveOpacity - _lastLiveAppliedOpacity01) > 0.01f)
						_snapshotOpacity01 = liveOpacity;
					float anchor = _snapshotOpacity01 >= 0f ? _snapshotOpacity01 : liveOpacity;
					float effective = Mathf.Lerp(anchor, proposedOpacity, Mathf.Clamp01(opInf));
					if (float.IsFinite(effective) && Mathf.Abs(effective - liveOpacity) > 0.02f) {
						opacityUi.SetOpacity01(effective, quiet: true); // no per-tick "Brush Opacity NN" status spam
						_lastLiveAppliedOpacity01 = Mathf.Clamp01(effective);
					} else {
						_lastLiveAppliedOpacity01 = Mathf.Clamp01(liveOpacity);
					}
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
