namespace spz {

	/// <summary>
	/// Shared disconnect copy for SD dropdown placeholders, viewport notifications, and tooltips,
	/// plus detection used when deciding whether a dropdown option is real vs. disconnected.
	/// </summary>
	public static class SdDisconnectPlaceholder {
		/// <summary>
		/// Short single-line caption for SD dropdowns (ellipsis-friendly). Not used for viewport deny/status.
		/// </summary>
		public const string DisplayText = "Diffusion Model Not Yet Connected";

		/// <summary>
		/// Viewport status when generation is denied because SD is not connected
		/// (<c>StableDiffusion_Hub.DenyWithMessage_ifCantGenerate</c>).
		/// </summary>
		public const string StatusText =
			"Can't Generate images\nNot yet connected to the Diffusion Model. Please wait";

		/// <summary>
		/// GEN ART / GEN BG hover tip while SD is disconnected
		/// (<c>GenerateButtons_UI.UpdateTooltips_GenButtons</c>).
		/// </summary>
		public const string TooltipText =
			"Not yet connected to the Diffusion Model.\nTo quick-view the Black box: open Settings and enable Show Black box (external process windows).";

		/// <summary>
		/// True for legacy ("Not Connected yet") and current ("Not Yet Connected") placeholder copy,
		/// plus OG status/tooltip strings that mention connection.
		/// </summary>
		public static bool IsPlaceholder(string text) {
			if (string.IsNullOrEmpty(text)) { return false; }
			string t = text.ToLowerInvariant();
			// Old: "Not Connected yet.\nCheck Black Window"
			// Prior: "Diffusion Neural Network\nNot yet connected."
			// Current dropdown: "Diffusion Model Not Yet Connected"
			// Tooltip / Status: "Not yet connected to the Diffusion Model..."
			return t.Contains("not connected") || t.Contains("not yet connected");
		}
	}
}
