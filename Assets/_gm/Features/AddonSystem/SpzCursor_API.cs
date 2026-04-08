using UnityEngine;

namespace spz {

	/// <summary>
	/// Cursor lock / visibility for add-on JSON-RPC (no FastPath dependency).
	/// </summary>
	public static class SpzCursor_API {

		public static void GetState(out CursorLockMode lockMode, out bool visible) {
			lockMode = Cursor.lockState;
			visible = Cursor.visible;
		}

		public static bool TryParseLockMode(string s, out CursorLockMode mode) {
			mode = CursorLockMode.None;
			if (string.IsNullOrEmpty(s))
				return false;
			switch (s.Trim().ToLowerInvariant()) {
				case "none":
					mode = CursorLockMode.None;
					return true;
				case "locked":
					mode = CursorLockMode.Locked;
					return true;
				case "confined":
					mode = CursorLockMode.Confined;
					return true;
				default:
					return false;
			}
		}

		public static void Apply(CursorLockMode lockMode, bool visible) {
			Cursor.lockState = lockMode;
			Cursor.visible = visible;
		}
	}
}
