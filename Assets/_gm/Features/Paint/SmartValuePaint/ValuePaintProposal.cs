using UnityEngine;

namespace spz {

	/// <summary>
	/// Reviewable paint assist proposal (Spec smart-value-paint R2).
	/// Does not apply strokes — consumption is Task 4 via the existing paint stack (R3).
	/// </summary>
	public struct ValuePaintProposal {
		public ValuePaintBand CurrentBin;
		public ValuePaintBand DesiredBin;
		public float BlendStrength01;
		public float EdgeSoftness01;
		public float BrushWidthHint01;
		public float OpacityHint01;
		public ValuePaintStrokeRole StrokeRole;
		public float MeanLuminance01;
		public string Source;

		public override string ToString() {
			return
				$"[ValuePaintProposal] lum={MeanLuminance01:F3} {CurrentBin}→{DesiredBin} " +
				$"role={StrokeRole} blend={BlendStrength01:F2} edgeSoft={EdgeSoftness01:F2} " +
				$"w={BrushWidthHint01:F2} op={OpacityHint01:F2} src={Source}";
		}
	}

}
