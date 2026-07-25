using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace spz {

	/// <summary>
	/// Add-on bridge: <b>capability</b> for ribbon-docked controls and command dispatch — not feature-specific UI copy.
	/// Feature text and command ids are supplied via JSON-RPC / add-on; core registers built-in command handlers here.
	/// </summary>
	public readonly struct RibbonDock_ButtonSpec {

		public readonly string Label;
		public readonly string CommandId;

		public RibbonDock_ButtonSpec(string label, string commandId) {
			Label = label ?? string.Empty;
			CommandId = string.IsNullOrEmpty(commandId) ? "viewport_fullview_toggle" : commandId;
		}

		/// <summary>Transport defaults when RPC omits keys (legacy clients); add-ons should pass explicit values.</summary>
			public static RibbonDock_ButtonSpec FromRpc(JObject p) {
			p ??= new JObject();
			string label = p["button_label"]?.ToString();
			if (string.IsNullOrWhiteSpace(label)) {
				// Match RibbonOnlyFullscreen Python register() and ApplyFullSrnLabelStyle — not "FULL\nSCREEN"
				// (label-only mismatch used to TearDownBuiltDock → appear/flash on enable after HTTP load).
				label = "FULL\nSRN";
			}
			label = label.Length > 200 ? label.Substring(0, 200) : label;
			string cmd = p["command"]?.ToString();
			if (string.IsNullOrWhiteSpace(cmd)) {
				cmd = "viewport_fullview_toggle";
			}
			return new RibbonDock_ButtonSpec(label, cmd);
		}
	}

	public static class RibbonDock_CommandBridge {

		static readonly Dictionary<string, Action> Commands = new Dictionary<string, Action>(StringComparer.Ordinal);

		static RibbonDock_CommandBridge() {
			Register("viewport_fullview_toggle", () => {
				// IsActive is only true for center-only (!left && !right). If the right paint column is open
				// (!left && right), the next press must still exit to the saved default — not call TryEnter again.
				var sk = Global_Skeleton_UI.instance;
				if (sk != null && sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
					if (!left) {
						// Center-only fullscreen, or right-only: leave back to pre-fullscreen layout.
						if (ViewportFullViewOnScreen_Driver.TryExit()) {
							AfterViewportFullViewLayoutChange(sk);
						}
						return;
					}
					// Default (left column visible): enter on-screen full view.
					if (ViewportFullViewOnScreen_Driver.TryEnter()) {
						AfterViewportFullViewLayoutChange(sk);
					}
					return;
				}
				// Skeleton not ready: legacy toggle; still run post-layout (chrome + refit) when a skeleton appears later.
				var skLegacy = Global_Skeleton_UI.instance;
				if (skLegacy != null && skLegacy.TryGetSidePanelVisibility(out bool _, out bool _)) {
					if (ViewportFullViewOnScreen_Driver.IsActive) {
						if (ViewportFullViewOnScreen_Driver.TryExit()) {
							AfterViewportFullViewLayoutChange(skLegacy);
						}
					} else {
						if (ViewportFullViewOnScreen_Driver.TryEnter()) {
							AfterViewportFullViewLayoutChange(skLegacy);
						}
					}
				} else {
					if (ViewportFullViewOnScreen_Driver.IsActive) {
						ViewportFullViewOnScreen_Driver.TryExit();
					} else {
						ViewportFullViewOnScreen_Driver.TryEnter();
					}
				}
			});
		}

		static void AfterViewportFullViewLayoutChange(Global_Skeleton_UI sk) {
			FullView_OuterPanel_Chrome_Binder.SyncChromeToDriver();
			if (sk != null) {
				sk.ForceLayoutRefreshAfterPanelResize();
			}
			// After SetSide + ForceLayout: first full-screen entry now has the correct inner-rect for SD w/h.
			ViewportFullViewOnScreen_Driver.NotifyLayoutRefreshedForPendingGenRefit();
		}

		/// <summary>Register a command id (e.g. from another core module or test hook). Prefer stable ids; add-ons reference them by string.</summary>
		public static void Register(string commandId, Action handler) {
			if (string.IsNullOrEmpty(commandId) || handler == null) {
				return;
			}
			Commands[commandId] = handler;
		}

		public static bool TryInvoke(string commandId) {
			if (string.IsNullOrEmpty(commandId)) {
				return false;
			}
			if (!Commands.TryGetValue(commandId, out var act)) {
				UnityEngine.Debug.LogWarning($"[RibbonDock_CommandBridge] Unknown command id: {commandId}");
				return false;
			}
			act();
			return true;
		}
	}
}
