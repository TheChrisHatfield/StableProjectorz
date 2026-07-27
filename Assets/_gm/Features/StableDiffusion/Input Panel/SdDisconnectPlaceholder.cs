namespace spz {

	/// <summary>
	/// Shared disconnect-placeholder copy for SD dropdowns, plus detection used when
	/// deciding whether a dropdown option is a real model/VAE/upscaler vs. "not connected".
	/// </summary>
	public static class SdDisconnectPlaceholder {
		public const string DisplayText = "Diffusion Neural Network\nNot yet connected.";

		/// <summary>
		/// True for legacy ("Not Connected yet") and current ("Not yet connected") placeholder copy.
		/// </summary>
		public static bool IsPlaceholder(string text) {
			if (string.IsNullOrEmpty(text)) { return false; }
			string t = text.ToLowerInvariant();
			// Old copy: "Not Connected yet.\nCheck Black Window"
			// New copy: "Diffusion Neural Network\nNot yet connected."
			return t.Contains("not connected") || t.Contains("not yet connected");
		}
	}
}
