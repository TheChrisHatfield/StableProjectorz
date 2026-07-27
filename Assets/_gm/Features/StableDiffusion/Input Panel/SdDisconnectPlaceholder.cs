namespace spz {

	/// <summary>
	/// Shared disconnect copy for SD dropdown placeholders, viewport notifications, and tooltips,
	/// plus detection used when deciding whether a dropdown option is real vs. disconnected.
	/// </summary>
	public static class SdDisconnectPlaceholder {
		public const string DisplayText = "Diffusion Model Not Yet Connected";

		/// <summary>
		/// True for legacy ("Not Connected yet") and current ("Not Yet Connected") placeholder copy.
		/// </summary>
		public static bool IsPlaceholder(string text) {
			if (string.IsNullOrEmpty(text)) { return false; }
			string t = text.ToLowerInvariant();
			// Old: "Not Connected yet.\nCheck Black Window"
			// Prior: "Diffusion Neural Network\nNot yet connected."
			// Current: "Diffusion Model Not Yet Connected"
			return t.Contains("not connected") || t.Contains("not yet connected");
		}
	}
}
