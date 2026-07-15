using UnityEngine;

namespace spz {

	/// <summary>
	/// Assist surface for smart-value-paint (Spec R2 / R5).
	/// Baseline implementations may be heuristic or MLP; all return proposals only — no stroke writes.
	/// </summary>
	public interface IValuePaintAssist {
		ValuePaintProposal ProposeFromLuminance(float luminance01, ValuePaintStrokeState strokeState = default);
		ValuePaintProposal ProposeFromColor(Color sample, ValuePaintStrokeState strokeState = default);
		ValuePaintProposal ProposeFromColors(Color[] patch, ValuePaintStrokeState strokeState = default);
	}

}
