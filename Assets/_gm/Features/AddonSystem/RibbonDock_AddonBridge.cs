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
				label = "FULL\nSCREEN";
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
				if (ViewportFullViewOnScreen_Driver.IsActive) {
					ViewportFullViewOnScreen_Driver.TryExit();
				}
				else {
					ViewportFullViewOnScreen_Driver.TryEnter();
				}
			});
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
