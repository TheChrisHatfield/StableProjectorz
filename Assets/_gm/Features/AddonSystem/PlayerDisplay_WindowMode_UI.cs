using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Optional UI hook: assign a button to toggle borderless fullscreen using primary monitor resolution
	/// and restore the last windowed size when leaving (see <see cref="SpzPlayerDisplay_API"/>).
	/// </summary>
	public class PlayerDisplay_WindowMode_UI : MonoBehaviour {

		[SerializeField] Button _toggleFullscreenButton;

		void Awake() {
			if (_toggleFullscreenButton != null) {
				_toggleFullscreenButton.onClick.AddListener(OnToggleFullscreenClicked);
			}
		}

		void OnDestroy() {
			if (_toggleFullscreenButton != null) {
				_toggleFullscreenButton.onClick.RemoveListener(OnToggleFullscreenClicked);
			}
		}

		public void OnToggleFullscreenClicked() {
			SpzPlayerDisplay_API.ToggleBorderlessFullscreenPreferMonitor();
		}
	}
}
