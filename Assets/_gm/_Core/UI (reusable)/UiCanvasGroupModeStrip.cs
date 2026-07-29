using UnityEngine;

namespace spz {

	/// <summary>
	/// Dimension / mode strips that share one skeleton slot must not crossfade both CanvasGroups
	/// in place — the outgoing panel ghosts under the incoming one (3D↔SD left column litmus).
	/// Hide snaps off; show may ease in.
	/// </summary>
	public static class UiCanvasGroupModeStrip {
		const float HideEpsilon = 0.0001f;

		/// <param name="show">True = this strip owns the slot for the current DimensionMode.</param>
		public static void Tick(CanvasGroup cg, bool show, float fadeInSpeed) {
			if (cg == null) return;
			if (show) {
				if (!cg.gameObject.activeSelf)
					cg.gameObject.SetActive(true);
				float speed = Mathf.Max(0.01f, fadeInSpeed);
				cg.alpha = Mathf.MoveTowards(cg.alpha, 1f, Time.deltaTime * speed);
				cg.blocksRaycasts = cg.alpha > HideEpsilon;
				cg.interactable = cg.alpha > 0.95f;
			}
			else {
				// Instant hide — do not leave a semi-transparent twin under the other mode's strip.
				cg.interactable = false;
				cg.blocksRaycasts = false;
				cg.alpha = 0f;
				if (cg.gameObject.activeSelf)
					cg.gameObject.SetActive(false);
			}
		}
	}
}
