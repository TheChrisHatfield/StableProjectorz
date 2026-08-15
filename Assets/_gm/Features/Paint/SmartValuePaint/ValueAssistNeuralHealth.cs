namespace spz {

	/// <summary>
	/// Observable state of the Value Assist neural path (Spec R8 / brush-behavior B8.6).
	/// Exists so "quietly running on the deterministic fallback" is diagnosable instead of silent.
	/// </summary>
	public static class ValueAssistNeuralHealth {

		public enum State {
			Unknown = 0,
			/// <summary>MLP Decimacon value heads resolved and in use.</summary>
			NeuralActive = 1,
			/// <summary>Decimacon failed to load; deterministic is standing in.</summary>
			FallbackDeterministic = 2,
			/// <summary>User turned neural off in Tool Options.</summary>
			NeuralOff = 3,
		}

		public static State Current { get; private set; } = State.Unknown;
		public static string Reason { get; private set; } = "";
		public static string ImplName { get; private set; } = "";

		public static bool IsNeuralActive => Current == State.NeuralActive;

		/// <summary>True when the user wants neural but we could not give it to them.</summary>
		public static bool IsUnwantedFallback => Current == State.FallbackDeterministic;

		public static void ReportNeuralActive(string implName) {
			Current = State.NeuralActive;
			ImplName = implName ?? "";
			Reason = "";
		}

		public static void ReportFallback(string implName, string reason) {
			Current = State.FallbackDeterministic;
			ImplName = implName ?? "";
			Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
		}

		public static void ReportNeuralOff(string implName) {
			Current = State.NeuralOff;
			ImplName = implName ?? "";
			Reason = "neural off";
		}

		public static void Reset() {
			Current = State.Unknown;
			Reason = "";
			ImplName = "";
		}

		/// <summary>Short one-line diagnostic for Editor checks / logs.</summary>
		public static string Describe() {
			switch (Current) {
				case State.NeuralActive: return "neural active: " + ImplName;
				case State.FallbackDeterministic: return "FALLBACK to deterministic: " + Reason;
				case State.NeuralOff: return "neural off (user)";
				default: return "unknown";
			}
		}
	}
}
