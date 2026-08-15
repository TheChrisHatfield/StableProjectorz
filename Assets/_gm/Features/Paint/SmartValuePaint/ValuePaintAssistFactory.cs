namespace spz {

	/// <summary>
	/// Product assist factory. Prefers soil MLP Decimacon + value heads; else Deterministic (never MultiHead).
	/// Every resolution publishes to <see cref="ValueAssistNeuralHealth"/> (brush-behavior B8.6) so a
	/// fallback is never silent.
	/// </summary>
	public static class ValuePaintAssistFactory {
		public static IValuePaintAssist Create(out string which) {
			return Create(preferNeural: PaintTab_ValueAssistOptions.UseNeural, out which);
		}

		public static IValuePaintAssist Create(bool preferNeural, out string which) {
			if (!preferNeural) {
				which = "DeterministicValuePaintAssist (neural off)";
				ValueAssistNeuralHealth.ReportNeuralOff(which);
				return new DeterministicValuePaintAssist();
			}
			if (MlpDecimaconPaintAssist.TryCreate(out var dec, out string decErr)) {
				which = "MlpDecimaconPaintAssist";
				ValueAssistNeuralHealth.ReportNeuralActive(which);
				return dec;
			}
			which = string.IsNullOrEmpty(decErr)
				? "DeterministicValuePaintAssist (decimacon unavailable)"
				: "DeterministicValuePaintAssist (decimacon unavailable: " + decErr + ")";
			ValueAssistNeuralHealth.ReportFallback(which, decErr);
			return new DeterministicValuePaintAssist();
		}

		public static IValuePaintAssist Create() => Create(out _);
	}
}
