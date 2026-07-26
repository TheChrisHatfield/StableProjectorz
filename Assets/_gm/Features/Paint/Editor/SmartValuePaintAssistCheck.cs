using UnityEditor;
using UnityEngine;

namespace spz.Editor {

	/// <summary>
	/// Editor checks for smart-value-paint (propose, accept, features, hardness).
	/// Pass D: thin MultiHead / Pass-C Decimacon menus removed — soil Decimacon comes back via MlpDecimacon.
	/// </summary>
	public static class SmartValuePaintAssistCheck {

		[MenuItem("StableProjectorz/Smart Value Paint/Run assist check")]
		public static void RunAssistCheck() {
			var assist = ValuePaintAssistFactory.Create(out string which);
			Debug.Log("[SmartValuePaint] assist impl=" + which);
			Color[] patch = {
				new Color(0.95f, 0.95f, 0.95f),
				new Color(0.75f, 0.72f, 0.70f),
				new Color(0.45f, 0.42f, 0.40f),
				new Color(0.25f, 0.22f, 0.20f),
				new Color(0.08f, 0.07f, 0.06f),
			};

			Debug.Log("[SmartValuePaint] assist check — proposals only (no stroke apply)");
			for (int i = 0; i < patch.Length; i++) {
				var p = assist.ProposeFromColor(patch[i]);
				Debug.Log($"  sample[{i}] {patch[i]} → {p}");
			}

			var fromPatch = assist.ProposeFromColors(patch, ValuePaintStrokeState.FromBrushHints(0.6f, 0.5f, 0.4f));
			Debug.Log($"  mean-patch → {fromPatch}");

			float[] feat = new float[ValuePaintFeatureBuilder.FeatureDim];
			StrokeFeatureExtractor.ExtractFromColors(patch, feat, width: patch.Length);
			Debug.Log($"  T6 features lum={feat[0]:F3} hist=[{feat[1]:F2},{feat[2]:F2},{feat[3]:F2},{feat[4]:F2},{feat[5]:F2}] edge={feat[6]:F3}");

			Debug.Log("  T7 Softness01→hardnessIx: " +
			          "0.9→" + ValuePaintProposalApplier.Softness01ToHardnessIx(0.9f) + " " +
			          "0.5→" + ValuePaintProposalApplier.Softness01ToHardnessIx(0.5f) + " " +
			          "0.1→" + ValuePaintProposalApplier.Softness01ToHardnessIx(0.1f));

			float[] bandEdges = {
				DeterministicValuePaintAssist.HighlightMin,
				DeterministicValuePaintAssist.LightMin,
				DeterministicValuePaintAssist.MidtoneMin,
				DeterministicValuePaintAssist.ShadowMin,
				0f,
			};
			foreach (float edge in bandEdges) {
				var atEdge = assist.ProposeFromLuminance(edge);
				Debug.Log($"  edge lum={edge:F2} → bin={atEdge.CurrentBin} role={atEdge.StrokeRole} src={atEdge.Source}");
			}

			Debug.Log("[SmartValuePaint] assist check complete");
		}

		[MenuItem("StableProjectorz/Smart Value Paint/Run Decimacon soil check")]
		public static void RunDecimaconSoilCheck() {
			spz.MlpDecimacon.DecimaconProductGate.ResetForTests(21);
			spz.MlpDecimacon.DecimaconProductGate.BeginPropose();
			if (!MlpDecimaconPaintAssist.TryCreate(out var assist, out string err)) {
				Debug.LogError("[SmartValuePaint] MlpDecimacon load failed — " + err);
				return;
			}
			var which = ValuePaintAssistFactory.Create(preferNeural: true, out string factoryWhich);
			Debug.Log("[SmartValuePaint] factory=" + factoryWhich + " assist=" + assist.SourceTag);
			Debug.Log("[SmartValuePaint] Pass D soil check — StreamingAssets/MlpDecimacon/");
			Color[] patch = {
				new Color(0.92f, 0.9f, 0.88f),
				new Color(0.5f, 0.48f, 0.45f),
				new Color(0.2f, 0.18f, 0.16f),
			};
			var p = assist.ProposeFromColors(patch);
			Debug.Log("  patch → " + p);
			var mid = assist.ProposeFromLuminance(0.5f);
			Debug.Log("  lum=0.5 → " + mid);
			if (!string.IsNullOrEmpty(mid.Source) && mid.Source.IndexOf("mlp_decimacon", System.StringComparison.Ordinal) < 0)
				Debug.LogWarning("[SmartValuePaint] expected Source to start with mlp_decimacon, got " + mid.Source);
			Debug.Log("[SmartValuePaint] Decimacon soil check complete");
		}

		[MenuItem("StableProjectorz/Smart Value Paint/Try accept midtone proposal")]
		public static void TryAcceptMidtoneProposal() {
			var assist = ValuePaintAssistFactory.Create(out string which);
			var proposal = assist.ProposeFromLuminance(0.5f);
			bool ok = ValuePaintProposalApplier.TryAccept(proposal, out string reason);
			if (ok)
				Debug.Log("[SmartValuePaint] accept OK (" + which + ") — " + reason
				          + " | armed=" + ValuePaintProposalApplier.IsArmed
				          + " | paint strokes will use ribbon color/opacity via Apply_into_ColorBrushTex");
			else
				Debug.LogWarning("[SmartValuePaint] accept refused (no false success) — " + reason);
		}

		[MenuItem("StableProjectorz/Smart Value Paint/Clear armed proposal")]
		public static void ClearArmedProposal() {
			ValuePaintProposalApplier.ClearArmed();
			Debug.Log("[SmartValuePaint] Armed proposal cleared");
		}
	}

}
