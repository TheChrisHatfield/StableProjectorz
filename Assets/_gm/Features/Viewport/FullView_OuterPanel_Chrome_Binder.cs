using UnityEngine;

namespace spz {

	/// <summary>
	/// Outer columns and overlays can still draw when skeleton widths hit zero (no clipping).
	/// Hides mirrored <see cref="RightColumn_UI"/>, <see cref="Left_Column_SD_Placement_UI"/> / <see cref="Left_Column_3D_Placement_UI"/> roots,
	/// optional <see cref="CommandRibbon_UI"/> when not under a right column, <see cref="CamerasMGR_PinsZone_UI"/>,
	/// the top <see cref="ExportSave_UI_MGR"/> bar (SAVE 2K, +, launch buttons), and <see cref="Connection_MGR"/> viewport-top strip.
	/// Does <b>not</b> hide <see cref="MainViewport_UI"/> <c>innerLeftRibbonRect</c> / <c>innerRightRibbonRect</c> — those viewport
	/// vertical tool ribbons stay visible in on-screen full view (see <see cref="ViewportFullViewOnScreen_Driver"/> doc).
	/// This binder follows <see cref="ViewportFullViewOnScreen_Driver.IsActive"/> — the same flag set by
	/// JSON-RPC <c>spz.cmd.set_editor_layout</c> (<c>center_max</c>, <c>viewport_focus</c>, <c>fullscreen_center</c>, or explicit
	/// <c>left_visible</c>/<c>right_visible</c>) after <see cref="Global_Skeleton_UI.SetSidePanelVisibility"/>, and by the in-app full-view control.
	/// </summary>
	public static class FullView_OuterPanel_Chrome_Binder {

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Init() {
			ViewportFullViewOnScreen_Driver.ActiveChanged += OnFullViewActiveChanged;
			SyncChromeToDriver();
		}

		/// <summary>Re-apply column / overlay chrome for the current driver state (e.g. UI spawned after the first full-view apply).</summary>
		public static void SyncChromeToDriver() {
			ApplyRightOuterPanelChrome(ViewportFullViewOnScreen_Driver.IsActive);
		}

		static void OnFullViewActiveChanged(bool fullViewOn) {
			ApplyRightOuterPanelChrome(fullViewOn);
		}

		static void ApplyRightOuterPanelChrome(bool hide) {
			var cols = Object.FindObjectsByType<RightColumn_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < cols.Length; i++) {
				var col = cols[i];
				if (col == null) {
					continue;
				}
				ApplyCanvasGroupHide(col.gameObject, hide);
			}

			var leftSd = Object.FindObjectsByType<Left_Column_SD_Placement_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < leftSd.Length; i++) {
				var c = leftSd[i];
				if (c == null || c.MirroredColumnRoot == null) {
					continue;
				}
				ApplyCanvasGroupHide(c.MirroredColumnRoot.gameObject, hide);
			}
			var left3d = Object.FindObjectsByType<Left_Column_3D_Placement_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < left3d.Length; i++) {
				var c = left3d[i];
				if (c == null || c.MirroredColumnRoot == null) {
					continue;
				}
				ApplyCanvasGroupHide(c.MirroredColumnRoot.gameObject, hide);
			}

			var ribbon = CommandRibbon_UI.instance;
			if (ribbon != null) {
				var rgo = ribbon.gameObject;
				if (rgo.GetComponentInParent<RightColumn_UI>(true) == null) {
					ApplyCanvasGroupHide(rgo, hide);
				}
			}

			var pins = CamerasMGR_PinsZone_UI.instance;
			if (pins != null) {
				ApplyCanvasGroupHide(pins.gameObject, hide);
			}

			// Top strip over the viewport/left: +, SAVE 2K, SD SERV, 3D SERV (ExportSave on same root as res controls).
			var topBar = ExportSave_UI_MGR.instance;
			if (topBar != null) {
				ApplyCanvasGroupHide(topBar.gameObject, hide);
			}

			var conn = Connection_MGR.instance;
			if (conn != null && conn.ViewportTopConnectionStrip != null) {
				ApplyCanvasGroupHide(conn.ViewportTopConnectionStrip.gameObject, hide);
			}

			// Viewport tool ribbons (Gen Art / workflow strips) always stay on — never part of the outer hide pass.
			// Also repairs alpha if an older build hid them during full view.
			var mainVp = MainViewport_UI.instance;
			if (mainVp != null) {
				if (mainVp.innerLeftRibbonRect != null) {
					ApplyCanvasGroupHide(mainVp.innerLeftRibbonRect.gameObject, false);
				}
				if (mainVp.innerRightRibbonRect != null) {
					ApplyCanvasGroupHide(mainVp.innerRightRibbonRect.gameObject, false);
				}
			}
		}

		static void ApplyCanvasGroupHide(UnityEngine.GameObject go, bool hide) {
			if (go == null) {
				return;
			}
			var cg = go.GetComponent<CanvasGroup>();
			if (cg == null) {
				cg = go.AddComponent<CanvasGroup>();
			}
			cg.alpha = hide ? 0f : 1f;
			cg.interactable = !hide;
			cg.blocksRaycasts = !hide;
		}
	}
}
