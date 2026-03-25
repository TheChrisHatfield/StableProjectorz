using UnityEngine;
using UnityEngine.InputSystem;

namespace spz {

	/// <summary>Ctrl/Cmd+Z and Ctrl/Cmd+Y / Ctrl/Cmd+Shift+Z when a <see cref="TMPro.TMP_InputField"/> is focused. Complements <see cref="PaintUndo_Input"/> (viewport undo when no text field is focused).</summary>
	[DefaultExecutionOrder(-40)]
	public class UiTextUndo_Input : MonoBehaviour {

		void Update() {
			if (UiTextUndo_MGR.instance == null) return;
			if (!KeyMousePenInput.isSomeInputFieldActive()) return;
			if (!KeyMousePenInput.isKey_CtrlOrCommand_pressed()) return;
			var kb = Keyboard.current;
			if (kb == null) return;
			bool shift = KeyMousePenInput.isKey_Shift_pressed();
			if (kb.zKey.wasPressedThisFrame) {
				if (shift)
					UiTextUndo_MGR.instance.TryRedo();
				else
					UiTextUndo_MGR.instance.TryUndo();
			} else if (kb.yKey.wasPressedThisFrame) {
				UiTextUndo_MGR.instance.TryRedo();
			}
		}
	}
}
