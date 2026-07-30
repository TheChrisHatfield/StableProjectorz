#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace spz {
	/// <summary>
	/// With Active Input Handling = Both, the default <see cref="StandaloneInputModule"/> often does not route tablet pen/stylus to uGUI.
	/// Replaces Standalone with <see cref="InputSystemUIInputModule"/> (mouse + pen + touch) on each <see cref="EventSystem"/>.
	/// Runs after the first scene load and again on every <see cref="SceneManager.sceneLoaded"/> so additively loaded scenes are covered.
	/// </summary>
	public static class EventSystemEnsureInputSystemUIModule {
		static bool _subscribedSceneLoaded;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void AfterFirstSceneLoad() {
			if (Application.isBatchMode) return;
			if (!_subscribedSceneLoaded) {
				SceneManager.sceneLoaded += (_, __) => ReplaceStandaloneWithInputSystemUi();
				_subscribedSceneLoaded = true;
			}
			ReplaceStandaloneWithInputSystemUi();
		}

		static void ReplaceStandaloneWithInputSystemUi() {
			if (Application.isBatchMode) return;
			var systems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			foreach (var es in systems) {
				if (es == null) continue;
				if (es.GetComponent<InputSystemUIInputModule>() != null) continue;
				var legacy = es.GetComponent<StandaloneInputModule>();
				if (legacy != null) {
					legacy.enabled = false;
					es.gameObject.AddComponent<InputSystemUIInputModule>();
					Object.Destroy(legacy);
					continue;
				}
				// No Standalone and no InputSystem module → still unwired (pen/uGUI dead).
				es.gameObject.AddComponent<InputSystemUIInputModule>();
			}
		}
	}
}
#endif
