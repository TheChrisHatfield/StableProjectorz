using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Add-on bridge for the viewport orientation gizmo: RPC parameter mapping plus the main-thread attach used by
	/// both JSON-RPC (<c>spz.ui.attach_viewport_axis_gizmo</c>) and <see cref="Addon_MGR"/> when Python never runs
	/// <c>register()</c>. Capability only — the widget itself lives in <see cref="ViewportAxisGizmo_UI"/>.
	/// </summary>
	public static class ViewportAxisGizmo_AddonBridge {

		/// <summary>StreamingAssets add-on id (folder name and Python <c>ADDON_ID</c>).</summary>
		public const string AddonId = "ViewportAxisGizmoSPZ";

		/// <summary>Lantern glyph shipped inside the add-on folder; used when the RPC omits <c>center_icon</c>.</summary>
		public const string DefaultCenterIconFile = "lantern.png";

		public static string DefaultCenterIconPath =>
			Path.Combine(Application.streamingAssetsPath, "Addons", AddonId, DefaultCenterIconFile);

		/// <summary>
		/// Params (all optional): <c>size</c> (px, 64–240), <c>margin</c> (px from the viewport corner),
		/// <c>center_icon</c> (absolute path, StreamingAssets-relative path, or a bare filename under this add-on's
		/// folder), <c>center_command</c> (<see cref="RibbonDock_CommandBridge"/> id for the lantern button).
		/// </summary>
		public static ViewportAxisGizmo_Spec SpecFromRpc(JObject p) {
			p ??= new JObject();
			float size = ReadFloat(p["size"], 104f);
			float margin = ReadFloat(p["margin"], ProjectUiScale.Space(2));
			string icon = ResolveIconPath(p["center_icon"]?.ToString());
			string command = p["center_command"]?.ToString();
			return new ViewportAxisGizmo_Spec(size, margin, icon, command);
		}

		static float ReadFloat(JToken token, float fallback) {
			if (token == null) {
				return fallback;
			}
			return float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out float v)
				? v
				: fallback;
		}

		/// <summary>
		/// Resolves the lantern glyph. Absolute paths win; otherwise a value is tried under StreamingAssets, then
		/// under this add-on's folder (so a bare <c>lantern.png</c> — which is what the Python helper docs promise —
		/// still finds the shipped art instead of falling back to the line icon).
		/// </summary>
		public static string ResolveIconPath(string raw) {
			if (string.IsNullOrWhiteSpace(raw)) {
				return DefaultCenterIconPath;
			}
			string trimmed = raw.Trim().Replace('/', Path.DirectorySeparatorChar);
			if (Path.IsPathRooted(trimmed)) {
				return trimmed;
			}
			string underStreaming = Path.Combine(Application.streamingAssetsPath, trimmed);
			if (File.Exists(underStreaming)) {
				return underStreaming;
			}
			string underAddon = Path.Combine(Application.streamingAssetsPath, "Addons", AddonId, trimmed);
			if (File.Exists(underAddon)) {
				return underAddon;
			}
			// Prefer the StreamingAssets-relative path so a missing file still points at the documented location.
			return underStreaming;
		}

		/// <summary>Main-thread attach shared by the socket server and <see cref="Addon_MGR"/>. Returns the RPC result object.</summary>
		public static JObject TryAttachFromCore(JObject @params) {
			var r = new JObject { ["success"] = false };
			try {
				ViewportAxisGizmo_Spec spec = SpecFromRpc(@params);
				bool attached = ViewportAxisGizmo_UI.TryAttach(spec);
				// "Mounted" means the widget exists; "visible" means 3D navigation is on and the canvas is shown.
				// Collapsing those two made UV-mode attaches look successful-and-on when the gizmo was alpha 0.
				bool mounted = ViewportAxisGizmo_UI.IsAnyMountedGizmo();
				bool visible = mounted && ViewportAxisGizmo_CameraOps.IsGizmoUsable();
				r["success"] = attached;
				r["mounted"] = mounted;
				r["visible"] = visible;
				r["host"] = "MainViewport_UI.mainViewportRect";
				if (!attached) {
					r["error"] = "Main viewport is not in the scene yet; Add-on Manager retries the attach on the main thread.";
				}
			}
			catch (Exception e) {
				r["error"] = e.Message;
			}
			return r;
		}
	}
}
