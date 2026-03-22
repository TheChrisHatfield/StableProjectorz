using UnityEngine;
using UnityEngine.InputSystem;

namespace spz {

	/// <summary>Ctrl/Cmd+Z undo, Ctrl/Cmd+Y and Ctrl/Cmd+Shift+Z redo. Respects TMP input focus. (Unity’s Ctrl+B is Build, not this.)</summary>
	public class PaintUndo_Input : MonoBehaviour {

		void Update() {
			if (PaintUndo_MGR.instance == null) return;
			if (Settings_MGR.instance != null && !Settings_MGR.instance.get_paintUndo_enabled()) return;
			if (KeyMousePenInput.isSomeInputFieldActive()) return;
			if (!KeyMousePenInput.isKey_CtrlOrCommand_pressed()) return;
			var kb = Keyboard.current;
			if (kb == null) return;
			bool shift = KeyMousePenInput.isKey_Shift_pressed();
			if (kb.zKey.wasPressedThisFrame) {
				if (shift)
					PaintUndo_MGR.instance.TryRedo();
				else
					PaintUndo_MGR.instance.TryUndo();
			} else if (kb.yKey.wasPressedThisFrame) {
				PaintUndo_MGR.instance.TryRedo();
			}
		}
	}
}
