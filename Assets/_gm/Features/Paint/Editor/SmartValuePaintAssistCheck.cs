using UnityEditor;
using UnityEngine;

namespace spz.Editor {

	/// <summary>
	/// Editor checks for smart-value-paint Task 3 (propose) and Task 4 (accept into paint stack).
	/// </summary>
	public static class SmartValuePaintAssistCheck {

		[MenuItem("StableProjectorz/Smart Value Paint/Run assist check")]
		public static void RunAssistCheck() {
			var assist = new DeterministicValuePaintAssist();
			Color[] patch = {
				new Color(0.95f, 0.95f, 0.95f),
				new Color(0.75f, 0.72f, 0.70f),
				new Color(0.45f, 0.42f, 0.40f),
				new Color(0.25f, 0.22f, 0.20f),
				new Color(0.08f, 0.07f, 0.06f),
			};

			Debug.Log("[SmartValuePaint] Task 3 assist check — proposals only (no stroke apply)");
			for (int i = 0; i < patch.Length; i++) {
				var p = assist.ProposeFromColor(patch[i]);
				Debug.Log($"  sample[{i}] {patch[i]} → {p}");
			}

			var fromPatch = assist.ProposeFromColors(patch, ValuePaintStrokeState.FromBrushHints(0.6f, 0.5f, 0.4f));
			Debug.Log($"  mean-patch → {fromPatch}");

			float[] bandEdges = {
				DeterministicValuePaintAssist.HighlightMin,
				DeterministicValuePaintAssist.LightMin,
				DeterministicValuePaintAssist.MidtoneMin,
				DeterministicValuePaintAssist.ShadowMin,
				0f,
			};
			foreach (float edge in bandEdges) {
				var atEdge = assist.ProposeFromLuminance(edge);
				Debug.Log($"  edge lum={edge:F2} → bin={atEdge.CurrentBin}");
			}

			Debug.Log("[SmartValuePaint] assist check complete");
		}

		[MenuItem("StableProjectorz/Smart Value Paint/Try accept midtone proposal")]
		public static void TryAcceptMidtoneProposal() {
			var assist = new DeterministicValuePaintAssist();
			var proposal = assist.ProposeFromLuminance(0.5f);
			bool ok = ValuePaintProposalApplier.TryAccept(proposal, out string reason);
			if (ok)
				Debug.Log("[SmartValuePaint] Task 4 accept OK — " + reason
				          + " | armed=" + ValuePaintProposalApplier.IsArmed
				          + " | paint strokes will use ribbon color/opacity via Apply_into_ColorBrushTex");
			else
				Debug.LogWarning("[SmartValuePaint] Task 4 accept refused (no false success) — " + reason);
		}

		[MenuItem("StableProjectorz/Smart Value Paint/Clear armed proposal")]
		public static void ClearArmedProposal() {
			ValuePaintProposalApplier.ClearArmed();
			Debug.Log("[SmartValuePaint] Armed proposal cleared");
		}
	}

}
