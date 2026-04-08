using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Optional inspector-assigned named <see cref="GameObject"/> hooks for add-on JSON-RPC
	/// (<c>set_ui_target_active</c>). Built-in ids still resolve without this component.
	/// </summary>
	public class SpzUiChromeRegistry : MonoBehaviour {
		public static SpzUiChromeRegistry instance { get; private set; }

		[Serializable]
		public class NamedEntry {
			public string id;
			public GameObject target;
		}

		[Tooltip("Extra ids merge with built-ins (global_skeleton_canvas, viewport_statusline, command_ribbon). Id is case-insensitive.")]
		[SerializeField] List<NamedEntry> _extraTargets = new List<NamedEntry>();

		void Awake() {
			if (instance != null && instance != this) {
				DestroyImmediate(this);
				return;
			}
			instance = this;
		}

		void OnDestroy() {
			if (instance == this)
				instance = null;
		}

		public bool TryResolveExtra(string idLower, out GameObject go) {
			go = null;
			if (string.IsNullOrEmpty(idLower) || _extraTargets == null)
				return false;
			for (int i = 0; i < _extraTargets.Count; i++) {
				var e = _extraTargets[i];
				if (e == null || string.IsNullOrEmpty(e.id) || e.target == null)
					continue;
				if (string.Equals(e.id.Trim(), idLower, StringComparison.OrdinalIgnoreCase)) {
					go = e.target;
					return true;
				}
			}
			return false;
		}

		public List<string> ListExtraIds() {
			var r = new List<string>();
			if (_extraTargets == null)
				return r;
			foreach (var e in _extraTargets) {
				if (e != null && !string.IsNullOrEmpty(e.id))
					r.Add(e.id.Trim());
			}
			return r;
		}
	}
}
