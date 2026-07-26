namespace spz {

	/// <summary>
	/// Product assist factory. Prefers soil MLP Decimacon + value heads; else Deterministic (never MultiHead).
	/// </summary>
	public static class ValuePaintAssistFactory {
		public static IValuePaintAssist Create(out string which) {
			return Create(preferNeural: PaintTab_ValueAssistOptions.UseNeural, out which);
		}

		public static IValuePaintAssist Create(bool preferNeural, out string which) {
			if (!preferNeural) {
				which = "DeterministicValuePaintAssist (neural off)";
				return new DeterministicValuePaintAssist();
			}
			if (MlpDecimaconPaintAssist.TryCreate(out var dec, out string decErr)) {
				which = "MlpDecimaconPaintAssist";
				return dec;
			}
			which = string.IsNullOrEmpty(decErr)
				? "DeterministicValuePaintAssist (decimacon unavailable)"
				: "DeterministicValuePaintAssist (decimacon unavailable: " + decErr + ")";
			return new DeterministicValuePaintAssist();
		}

		public static IValuePaintAssist Create() => Create(out _);
	}
}
