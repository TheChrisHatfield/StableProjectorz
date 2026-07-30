using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace spz {
	/// <summary>
	/// Brush / tool sliders: disables navigation focus trap and (when Input System is available) maps pen/stylus drag to value
	/// so tablets work even if some drivers deliver pen outside the normal UI pointer path.
	/// </summary>
	[RequireComponent(typeof(Slider))]
	public sealed class SliderStylusSupport : MonoBehaviour {
		Slider _slider;
		RectTransform _trackRect;
		bool _penDragging;

		void Awake() {
			_slider = GetComponent<Slider>();
			_trackRect = transform as RectTransform;
			if (_slider != null)
				_slider.navigation = new Navigation { mode = Navigation.Mode.None };
		}

#if ENABLE_INPUT_SYSTEM
		void Update() {
			// InputSystemUIInputModule already drives sliders — avoid double-driving pen.
			var es = EventSystem.current;
			if (es != null && es.GetComponent<InputSystemUIInputModule>() != null)
				return;

			var pen = UnityEngine.InputSystem.Pen.current;
			if (pen == null) {
				_penDragging = false;
				return;
			}

			if (!pen.press.isPressed) {
				_penDragging = false;
				return;
			}

			Canvas canvas = GetComponentInParent<Canvas>();
			Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
			Vector2 screen = pen.position.ReadValue();

			if (pen.press.wasPressedThisFrame) {
				if (_trackRect == null || !RectTransformUtility.RectangleContainsScreenPoint(_trackRect, screen, uiCam))
					return;
				_penDragging = true;
			}

			if (!_penDragging) return;

			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_trackRect, screen, uiCam, out Vector2 local))
				return;

			Rect r = _trackRect.rect;
			float denom = r.width;
			if (denom < 1e-4f) return;
			float t = (local.x - r.xMin) / denom;
			_slider.normalizedValue = Mathf.Clamp01(t);
		}
#endif
	}
}
