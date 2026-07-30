using System;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Paint tab → Tool options → Brush options: when Settings "strict inpaint mask isolation" runs after SD,
	/// optionally invert which screen-mask pixels are clamped back to the init image (default: keep init outside the brush mask).
	/// </summary>
	public static class PaintTab_StrictIsolationBrushOptions {
		const string PrefKey = "PaintTab_StrictIsolationFlipMask";

		static bool _loaded;
		static bool _flipInvertMask;

		/// <summary>Fired after <see cref="FlipInvertIsolationMask"/> changes (including from API).</summary>
		public static event Action Changed;

		public static bool FlipInvertIsolationMask {
			get {
				EnsureLoaded();
				return _flipInvertMask;
			}
		}

		static void EnsureLoaded() {
			if (_loaded) return;
			_flipInvertMask = PlayerPrefs.GetInt(PrefKey, 0) == 1;
			_loaded = true;
		}

		public static void SetFlipInvertIsolationMask(bool v) {
			EnsureLoaded();
			if (v == _flipInvertMask) return;
			_flipInvertMask = v;
			PlayerPrefs.SetInt(PrefKey, v ? 1 : 0);
			PlayerPrefs.Save();
			SyncSettingsToggleIfPresent();
			Changed?.Invoke();
		}

		static void SyncSettingsToggleIfPresent() {
			var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_sd_strictIsolationFlipMask");
			if (toggle != null)
				toggle.SetIsOnWithoutNotify(_flipInvertMask);
		}

		public static bool TrySetFlipInvertIsolationMaskFromApi(bool v) {
			SetFlipInvertIsolationMask(v);
			return true;
		}
	}
}
