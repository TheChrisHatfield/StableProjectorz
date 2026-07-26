using System;

namespace spz.MlpDecimacon {

	/// <summary>Soil paint boundary — LAVD must never write ValuePaintProposal fields.</summary>
	public static class LavdPaintBoundary {
		public const string BoundaryMessage =
			"LOCKED CONFLICT: Decimacon LAVD BanditArm/scheduler policy must not map into ValuePaintProposal fields.";

		public sealed class LavdPaintBoundaryException : InvalidOperationException {
			public LavdPaintBoundaryException(string message) : base(message) { }
		}

		static readonly string[] PaintDtoFields = {
			"CurrentBin", "DesiredBin", "BlendStrength01", "EdgeSoftness01",
			"BrushWidthHint01", "OpacityHint01", "StrokeRole", "MeanLuminance01", "Source",
		};

		public static void RefuseBanditToPaintDto(string fieldName = null) {
			if (string.IsNullOrEmpty(fieldName)) return;
			for (int i = 0; i < PaintDtoFields.Length; i++) {
				if (string.Equals(PaintDtoFields[i], fieldName, StringComparison.Ordinal))
					throw new LavdPaintBoundaryException(BoundaryMessage + " Refused field=" + fieldName);
			}
		}
	}
}
