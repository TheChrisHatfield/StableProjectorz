using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Shared UI chrome operations for add-on JSON-RPC: main canvas scale, named objects, status line, EventSystem.
	/// </summary>
	public static class SpzUiChromeOps {

		const float kMinUiScale = 0.5f;
		const float kMaxUiScale = 2f;
		const int kMaxStatusChars = 2048;
		const float kMinStatusDur = 0.25f;
		const float kMaxStatusDur = 60f;

		static CanvasScaler _mainScaler;
		static Vector2 _referenceBaseline;

		static CanvasScaler ResolveMainScaler() {
			if ((UnityEngine.Object)_mainScaler == null)
				_mainScaler = null;
			if (_mainScaler != null)
				return _mainScaler;
			var sk = Global_Skeleton_UI.instance;
			if (sk == null)
				return null;
			_mainScaler = sk.GetComponent<CanvasScaler>();
			// Capture baseline once per session; do not overwrite after set_ui_scale or transient scaler refresh.
			if (_mainScaler != null && _mainScaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
			    && _referenceBaseline.x < 0.01f)
				_referenceBaseline = _mainScaler.referenceResolution;
			return _mainScaler;
		}

		public static bool TryGetUiScale(out float multiplier, out float refX, out float refY) {
			multiplier = 1f;
			refX = 0f;
			refY = 0f;
			if (Application.isBatchMode)
				return false;
			var s = ResolveMainScaler();
			if (s == null || s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
				return false;
			var cur = s.referenceResolution;
			refX = cur.x;
			refY = cur.y;
			if (_referenceBaseline.x > 0.01f)
				multiplier = Mathf.Clamp(_referenceBaseline.x / cur.x, kMinUiScale, kMaxUiScale);
			else
				multiplier = 1f;
			return true;
		}

		/// <summary>1 = scene baseline. &gt;1 enlarges UI (Scale With Screen Size reference is reduced).</summary>
		public static bool SetUiScaleMultiplier(float multiplier) {
			if (Application.isBatchMode)
				return false;
			var s = ResolveMainScaler();
			if (s == null || s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
				return false;
			multiplier = Mathf.Clamp(multiplier, kMinUiScale, kMaxUiScale);
			if (_referenceBaseline.x < 0.01f)
				_referenceBaseline = s.referenceResolution;
			var b = _referenceBaseline;
			s.referenceResolution = new Vector2(b.x / multiplier, b.y / multiplier);
			Canvas.ForceUpdateCanvases();
			return true;
		}

		static readonly string[] BuiltinTargetIds = {
			"global_skeleton_canvas",
			"viewport_statusline",
			"command_ribbon",
			"left_ribbon",
			"workflow_ribbon",
			"workflow_options",
			"generate_buttons",
			"multiview_ribbon",
		};

		public static List<string> ListUiTargetIds() {
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var b in BuiltinTargetIds)
				set.Add(b);
			if (SpzUiChromeRegistry.instance != null) {
				foreach (var x in SpzUiChromeRegistry.instance.ListExtraIds())
					set.Add(x);
			}
			var r = new List<string>(set);
			r.Sort(StringComparer.OrdinalIgnoreCase);
			return r;
		}

		public static bool TryResolveUiTarget(string id, out GameObject go) {
			go = null;
			if (string.IsNullOrEmpty(id))
				return false;
			string idLower = id.Trim().ToLowerInvariant();
			switch (idLower) {
				case "global_skeleton_canvas":
					go = Global_Skeleton_UI.instance != null ? Global_Skeleton_UI.instance.gameObject : null;
					return go != null;
				case "viewport_statusline":
					go = Viewport_StatusText.instance != null ? Viewport_StatusText.instance.gameObject : null;
					return go != null;
				case "command_ribbon":
					go = CommandRibbon_UI.instance != null ? CommandRibbon_UI.instance.gameObject : null;
					return go != null;
				case "left_ribbon":
					go = LeftRibbon_UI.instance != null ? LeftRibbon_UI.instance.gameObject : null;
					return go != null;
				case "workflow_ribbon":
					go = WorkflowRibbon_UI.instance != null ? WorkflowRibbon_UI.instance.gameObject : null;
					return go != null;
				case "workflow_options":
					go = SD_WorkflowOptionsRibbon_UI.instance != null
						? SD_WorkflowOptionsRibbon_UI.instance.gameObject
						: null;
					return go != null;
				case "generate_buttons":
					go = GenerateButtons_Main_UI.instance != null
						? GenerateButtons_Main_UI.instance.gameObject
						: null;
					return go != null;
				case "multiview_ribbon":
					go = MultiView_Ribbon_UI.instance != null ? MultiView_Ribbon_UI.instance.gameObject : null;
					return go != null;
				default:
					return SpzUiChromeRegistry.instance != null
					       && SpzUiChromeRegistry.instance.TryResolveExtra(idLower, out go)
					       && go != null;
			}
		}

		public static bool TryGetUiTargetActive(string id, out bool active) {
			active = false;
			if (!TryResolveUiTarget(id, out var go) || go == null)
				return false;
			active = go.activeSelf;
			return true;
		}

		public static bool SetUiTargetActive(string id, bool active) {
			if (!TryResolveUiTarget(id, out var go) || go == null)
				return false;
			go.SetActive(active);
			return true;
		}

		public static bool ShowStatusText(string message, bool textIsEta, float durationSec, bool progressVisibility) {
			if (Viewport_StatusText.instance == null)
				return false;
			if (string.IsNullOrEmpty(message))
				message = "";
			if (message.Length > kMaxStatusChars)
				message = message.Substring(0, kMaxStatusChars);
			durationSec = Mathf.Clamp(durationSec, kMinStatusDur, kMaxStatusDur);
			Viewport_StatusText.instance.ShowStatusText(message, textIsEta, durationSec, progressVisibility);
			return true;
		}

		public static bool TryGetEventSystemEnabled(out bool enabled) {
			enabled = false;
			var es = EventSystem.current;
			if (es == null)
				return false;
			enabled = es.enabled;
			return true;
		}

		public static bool SetEventSystemEnabled(bool enabled) {
			var es = EventSystem.current;
			if (es == null)
				return false;
			es.enabled = enabled;
			return true;
		}
	}
}
