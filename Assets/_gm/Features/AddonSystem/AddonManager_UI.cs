using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// UI panel for managing add-ons (install, enable/disable, remove).
	/// Runtime layout follows <c>AddonManager_ModernArchive/AddonManager_UI.FromGitMain_Reference.cs.txt</c> (main-branch template).
	/// Extra polish from <c>AddonManager_REF_SPEC.json</c> can be reintroduced incrementally.
	/// </summary>
	public class AddonManager_UI : MonoBehaviour {
		public static AddonManager_UI instance { get; private set; }

		/// <summary>
		/// Set from Settings when <see cref="instance"/> is still null (additive <c>Tool_AddonSystem</c> not finished loading).
		/// Consumed in <see cref="FinishStartBootstrap"/> — avoids <see cref="StaticEvents.Invoke"/> no-op before subscribe.
		/// </summary>
		static bool s_pendingOpenRequest;
		static bool s_deferredOpenRunning;
		/// <summary>Set by <see cref="OpenFromMenu"/>; cleared after a successful modal show so <see cref="OpenPanel"/> from the tool scene does not hide Settings.</summary>
		static bool s_closeSettingsWhenModalShown;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetStaticState() {
			s_pendingOpenRequest = false;
			s_deferredOpenRunning = false;
			s_closeSettingsWhenModalShown = false;
		}

		/// <summary>Call when opening the manager but <see cref="instance"/> may not exist yet (e.g. Settings before addon scene loads).</summary>
		public static void RequestOpenWhenReady() {
			s_pendingOpenRequest = true;
		}

		/// <summary>
		/// Single entry point from Settings (and any other menu). Handles: live instance → <see cref="OpenPanel"/>;
		/// no instance yet → pending flag + <see cref="StaticEvents.Invoke"/> once <see cref="Awake"/> has subscribed.
		/// </summary>
		public static void OpenFromMenu() {
			// Do not deactivate Settings here — that can disable this button's hierarchy mid-onClick before OpenPanel runs.
			// We hide Settings only after the modal is actually shown (see OpenPanel).
			s_closeSettingsWhenModalShown = true;

			var inst = instance;
			if (inst) {
				inst.OpenPanel();
				return;
			}
			// Inactive hierarchy: Awake has not run, so instance is still null. Activate ancestors then OpenPanel.
			if (TryWakeLatentManagerAndOpen())
				return;

			RequestOpenWhenReady();
			StaticEvents.Invoke("AddonManager:OpenPanel");
			// Invoke is a no-op until Awake subscribes; retry for a short window while Tool_AddonSystem loads async.
			if (instance) {
				instance.OpenPanel();
				return;
			}
			ScheduleDeferredOpenIfNeeded();
		}

		/// <summary>Activates self and every ancestor so disabled parents do not block Awake / rendering.</summary>
		static void EnsureTransformHierarchyActive(Transform t) {
			if (t == null) return;
			var chain = new List<Transform>(8);
			for (Transform x = t; x != null; x = x.parent)
				chain.Add(x);
			for (int i = chain.Count - 1; i >= 0; i--)
				chain[i].gameObject.SetActive(true);
		}

		/// <summary>
		/// Prefer <see cref="AddonManager_UI"/> in the same scene as <see cref="Addon_MGR"/> (works when <see cref="Scene.path"/> is empty in builds).
		/// Fallback: name/path contains Tool_AddonSystem; last resort first found.
		/// </summary>
		static AddonManager_UI FindBestAddonManagerUI() {
			var all = UnityEngine.Object.FindObjectsByType<AddonManager_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			if (all == null || all.Length == 0)
				return null;

			Addon_MGR mgr = Addon_MGR.instance;
			if (mgr == null || !mgr)
				mgr = UnityEngine.Object.FindFirstObjectByType<Addon_MGR>(FindObjectsInactive.Include);
			if (mgr != null && mgr.gameObject.scene.IsValid()) {
				Scene mgrScene = mgr.gameObject.scene;
				foreach (var m in all) {
					if (m == null || !m) continue;
					if (m.gameObject.scene == mgrScene)
						return m;
				}
			}

			const string sceneToken = "Tool_AddonSystem";
			foreach (var m in all) {
				if (m == null || !m) continue;
				if (!m.gameObject.scene.IsValid()) continue;
				var sc = m.gameObject.scene;
				string p = sc.path;
				string n = sc.name;
				if ((!string.IsNullOrEmpty(p) && p.IndexOf(sceneToken, StringComparison.OrdinalIgnoreCase) >= 0)
				    || (!string.IsNullOrEmpty(n) && n.IndexOf(sceneToken, StringComparison.OrdinalIgnoreCase) >= 0))
					return m;
			}
			return all[0];
		}

		/// <summary>Find add-on manager in loaded scenes (including inactive) and open if Awake can run.</summary>
		static void CloseSettingsPanelIfBound() {
			var settingsPanel = EventsBinder.FindComponent<RectTransform>("Settings:SettingsPanel");
			if (settingsPanel != null)
				settingsPanel.gameObject.SetActive(false);
		}

		static bool TryWakeLatentManagerAndOpen() {
			var latent = FindBestAddonManagerUI();
			if (latent == null || !latent.gameObject.scene.IsValid())
				return false;
			EnsureTransformHierarchyActive(latent.transform);
			if (!latent.enabled)
				latent.enabled = true;
			if (!instance)
				return false;
			instance.OpenPanel();
			return IsModalOpen;
		}

		sealed class DeferredOpenCoroutineHost : MonoBehaviour { }

		static DeferredOpenCoroutineHost s_deferredOpenHost;

		static void EnsureDeferredOpenCoroutineHost() {
			if (s_deferredOpenHost != null)
				return;
			var go = new GameObject("SPZ_AddonManager_DeferredOpenHost");
			go.hideFlags = HideFlags.HideInHierarchy;
			UnityEngine.Object.DontDestroyOnLoad(go);
			s_deferredOpenHost = go.AddComponent<DeferredOpenCoroutineHost>();
		}

		static void ScheduleDeferredOpenIfNeeded() {
			if (!s_pendingOpenRequest || s_deferredOpenRunning)
				return;
			MonoBehaviour host = Settings_MGR.instance;
			if (host == null)
				host = UnityEngine.Object.FindFirstObjectByType<Settings_MGR>(FindObjectsInactive.Include);
			if (host == null)
				host = UnityEngine.Object.FindFirstObjectByType<Start_Scene_Global_MGR>(FindObjectsInactive.Include);
			if (host == null)
				host = UnityEngine.Object.FindFirstObjectByType<Addon_MGR>(FindObjectsInactive.Include);
			if (host == null) {
				EnsureDeferredOpenCoroutineHost();
				host = s_deferredOpenHost;
			}
			if (host == null)
				return;
			s_deferredOpenRunning = true;
			host.StartCoroutine(CoDeferredTryOpenAddonManager(600));
		}

		static IEnumerator CoDeferredTryOpenAddonManager(int maxFrames) {
			try {
				for (int i = 0; i < maxFrames && s_pendingOpenRequest; i++) {
					yield return null;
					if (TryWakeLatentManagerAndOpen())
						yield break;
					if (instance) {
						instance.OpenPanel();
						if (IsModalOpen)
							yield break;
					}
				}
				if (s_pendingOpenRequest)
					Debug.LogWarning(
						"[AddonManager_UI] Open from menu is still pending: ensure Tool_AddonSystem.unity is in Build Settings and loads (see Start_Scene_Global_MGR).");
			}
			finally {
				s_deferredOpenRunning = false;
			}
		}

		/// <summary>True while the fullscreen add-on manager overlay is up (viewport hints / hover treat as modal).</summary>
		/// Uses <see cref="GameObject.activeInHierarchy"/> so we do not treat a child as "open" when the blocker/canvas is off
		/// (children can still have <c>activeSelf == true</c> while hidden).
		public static bool IsModalOpen =>
			instance
			&& ((instance._blocker != null && instance._blocker.activeInHierarchy)
			    || (instance._panel != null && instance._panel.activeInHierarchy));

		/// <summary> Same value as <see cref="CreatePanelIfNeeded"/> overlay canvas. File browser must sort above this while open. </summary>
		const int AddonManagerCanvasSortOrder = 32767;

		static readonly Color RefBgModalDim = new Color(0f, 0f, 0f, 0.9f);
		Color _statusOk = new Color(34f / 255f, 197f / 255f, 94f / 255f, 1f);
		Color _statusFail = new Color(239f / 255f, 68f / 255f, 68f / 255f, 1f);
		Color _statusMuted = new Color(0.63f, 0.63f, 0.67f, 1f);
		static readonly Color kAuthoredStatusOk = new Color(34f / 255f, 197f / 255f, 94f / 255f, 1f);
		static readonly Color kAuthoredStatusFail = new Color(239f / 255f, 68f / 255f, 68f / 255f, 1f);
		static readonly Color kAuthoredStatusMuted = new Color(0.63f, 0.63f, 0.67f, 1f);
		bool? _lastStatusIsSuccess;
		float _themeTitleBasePt = -1f;
		float _themeFilterLabelBasePt = -1f;
		float _themeStatusBasePt = -1f;
		float _themeRememberLabelBasePt = -1f;
		
		[SerializeField] GameObject _panel;
		[SerializeField] Button _openPanel_button;
		[SerializeField] Button _closePanel_button;
		[SerializeField] Button _installFromFile_button;
		[SerializeField] Button _refresh_button;
		Button _loadAddonsNow_button;
		Button _restartWithAddons_button;
		Button _saveAddonSettings_button;
		[SerializeField] RectTransform _addonsListParent; // Where to place add-on list items (runtime panel sets this when null)
		[SerializeField] GameObject _addonItemPrefab; // optional; otherwise rows are built like main-branch
		[SerializeField] TextMeshProUGUI _statusText;
		
		private Dictionary<string, GameObject> _addonUIItems = new Dictionary<string, GameObject>();
		/// <summary>Dial selection mirror (live enable/disable applies immediately; Save settings persists for next launch).</summary>
		readonly Dictionary<string, bool> _draftEnabledById = new Dictionary<string, bool>(StringComparer.Ordinal);
		bool _draftDirty;
		bool _hidViewportStatusForModal;
		
		// Filter state: 0 = All, 1 = Enabled, 2 = Disabled
		private int _filterState = 0;
		private Toggle _filterAllToggle;
		private Toggle _filterEnabledToggle;
		private Toggle _filterDisabledToggle;
		Toggle _rememberEnabledAddonToggle; // assigned in Create / TryAddRememberPreferenceRowIfMissing
		private GameObject _blocker; // full-screen click blocker, shown/hidden with panel
		Image _blockerDimImage; // dimmer on blocker root
		CanvasGroup _panelModalGroup;
		/// <summary>True while a row dial is applying enable/disable — skip event-driven full rebuild (destroys control mid-click).</summary>
		bool _suppressEnabledListRefresh;
		Coroutine _deferredListRefresh;
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			// Subscribe here so StaticEvents.Invoke works as soon as the singleton exists (before Start runs).
			StaticEvents.SubscribeOrReplace("AddonManager:OpenPanel", OpenPanel);
			SceneManager.sceneLoaded += OnSceneLoadedMaybeOpenPending;
			SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
		}

		/// <summary>User may request open before the add-on tool scene finishes loading; open when that scene load completes.</summary>
		void OnSceneLoadedMaybeOpenPending(Scene scene, LoadSceneMode mode) {
			if (instance != this || !s_pendingOpenRequest)
				return;
			// Do not rely on Scene.path / name matching "Tool_AddonSystem" — path is often empty in player builds and names can differ.
			if (!gameObject.scene.IsValid() || gameObject.scene != scene)
				return;
			OpenPanel();
		}
		
	void OnEnable() {
		Addon_MGR.OnAddonEnabledStateChanged += OnAddonEnabledStateChanged;
	}

	void OnDisable() {
		Addon_MGR.OnAddonEnabledStateChanged -= OnAddonEnabledStateChanged;
	}

	void Start() {
		if (_openPanel_button != null) {
			_openPanel_button.onClick.AddListener(OpenPanel);
		}
		if (_closePanel_button != null) {
			_closePanel_button.onClick.AddListener(ClosePanel);
		}
		if (_installFromFile_button != null) {
			_installFromFile_button.onClick.AddListener(OnInstallFromFile);
		}
		if (_refresh_button != null) {
			_refresh_button.onClick.AddListener(RefreshAddonsList);
		}
		
		// Always run full connectivity check (panel + list parent + ref layout), not only _panel
		CreatePanelIfNeeded();
		// Synchronous finish: no yield — avoids racing other coroutines that call OpenPanel the same frame.
		FinishStartBootstrap();
	}

		/// <summary>Apply pending open, or ensure overlay is hidden when nothing asked to show (same frame as <see cref="Start"/>).</summary>
		void FinishStartBootstrap() {
			if (s_pendingOpenRequest) {
				OpenPanel();
				return;
			}
			if (!IsModalOpen) {
				if (_hidViewportStatusForModal && Viewport_StatusText.instance != null) {
					Viewport_StatusText.instance.PreferVIsible(this);
					_hidViewportStatusForModal = false;
				}
				// Legacy parity: only hide blocker + panel. Do NOT SetActive(false) on AddonManager_Canvas —
				// if OpenPanel() ever returns early before re-enabling the canvas, the whole overlay stays dead.
				if (_blocker != null)
					_blocker.SetActive(false);
				if (_panel != null)
					_panel.SetActive(false);
			}
		}
	
	/// <summary>
	/// True when <see cref="_panel"/>, <see cref="_addonsListParent"/>, and reference chrome are wired so
	/// <see cref="RefreshAddonsList"/> can run (connectivity rule: do not skip setup when only one ref exists).
	/// </summary>
	bool AddonManagerPanelSetupIsComplete() {
		if (_panel == null || _addonsListParent == null) return false;
		if (!_addonsListParent.transform.IsChildOf(_panel.transform)) return false;
		return _panel.transform.Find("StichAddonManager_v8") != null
			&& _panel.transform.Find("FilterBar/FilterPills") != null;
	}

		void OnRememberEnabledAddonsToggleChanged(bool remember) {
			Addon_MGR.SetRememberEnabledAddonsPreference(remember);
		}

		void TryEnsureSaveSettingsButton() {
			if (_panel == null) return;
			Transform header = _panel.transform.Find("Header");
			if (header == null) return;
			Transform existing = header.Find("SaveAddonSettingsButton");
			if (existing != null) {
				_saveAddonSettings_button = existing.GetComponent<Button>();
				if (_saveAddonSettings_button != null) {
					_saveAddonSettings_button.onClick.RemoveListener(OnSaveAddonSettings);
					_saveAddonSettings_button.onClick.AddListener(OnSaveAddonSettings);
				}
				return;
			}
			// Older runtime chrome without Save settings — rebuild the manager shell once.
			Debug.Log("[AddonManager_UI] Rebuilding Add-on Manager panel to add Save settings.");
			DestroyAddonManagerPanelHierarchy();
			CreatePanelIfNeeded();
		}

		void TryAddRememberPreferenceRowIfMissing() {
			if (_panel == null) {
				return;
			}
			Transform found = _panel.transform.Find("RememberEnabledRow");
			if (found != null) {
				_rememberEnabledAddonToggle = found.GetComponentInChildren<Toggle>(true);
				if (_rememberEnabledAddonToggle != null) {
					_rememberEnabledAddonToggle.onValueChanged.RemoveListener(OnRememberEnabledAddonsToggleChanged);
					_rememberEnabledAddonToggle.onValueChanged.AddListener(OnRememberEnabledAddonsToggleChanged);
				}
				return;
			}
			Transform status = _panel.transform.Find("StatusText");
			int idx = status != null ? status.GetSiblingIndex() : _panel.transform.childCount;
			var row = BuildRememberEnabledPreferenceRow(8f);
			row.transform.SetParent(_panel.transform, false);
			row.transform.SetSiblingIndex(idx);
		}

		void SyncRememberEnabledToggleFromPrefs() {
			if (_rememberEnabledAddonToggle == null) {
				return;
			}
			_rememberEnabledAddonToggle.SetIsOnWithoutNotify(Addon_MGR.GetRememberEnabledAddonsPreference());
		}

		GameObject BuildRememberEnabledPreferenceRow(float grid) {
			var row = new GameObject("RememberEnabledRow");
			row.layer = _panel != null ? _panel.gameObject.layer : 5;
			var rowLE = row.AddComponent<LayoutElement>();
			rowLE.preferredHeight = 30f;
			rowLE.minHeight = 26f;
			var rowH = row.AddComponent<HorizontalLayoutGroup>();
			rowH.spacing = grid;
			rowH.childAlignment = TextAnchor.MiddleLeft;
			rowH.childControlWidth = true;
			rowH.childControlHeight = true;
			rowH.childForceExpandWidth = true;
			rowH.childForceExpandHeight = true;
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(row.transform, false);
			var labelLE = labelObj.AddComponent<LayoutElement>();
			labelLE.minWidth = 200f;
			labelLE.preferredWidth = 420f;
			labelLE.flexibleWidth = 1f;
			var labelT = labelObj.AddComponent<TextMeshProUGUI>();
			labelT.text = "Restore saved selection next launch (after Save settings)";
			labelT.fontSize = 12;
			labelT.color = new Color(0.65f, 0.65f, 0.68f, 1f);
			labelT.alignment = TextAlignmentOptions.MidlineLeft;
			labelT.raycastTarget = false;
			var toggleContainer = new GameObject("ToggleWrap");
			toggleContainer.transform.SetParent(row.transform, false);
			var tLE = toggleContainer.AddComponent<LayoutElement>();
			tLE.preferredWidth = 44f;
			tLE.minWidth = 40f;
			tLE.flexibleWidth = 0f;
			tLE.preferredHeight = 20f;
			tLE.minHeight = 18f;
			var bg = new GameObject("Background");
			bg.transform.SetParent(toggleContainer.transform, false);
			var bgR = bg.AddComponent<RectTransform>();
			bgR.anchorMin = Vector2.zero;
			bgR.anchorMax = Vector2.one;
			bgR.sizeDelta = Vector2.zero;
			var bgI = bg.AddComponent<UnityEngine.UI.Image>();
			SpzUiThemeOps.ApplyRoundedControlSprite(bgI, markEligible: true);
			bgI.color = new Color(0.3f, 0.3f, 0.3f, 1f);
			bgI.raycastTarget = true;
			var ck = new GameObject("Checkmark");
			ck.transform.SetParent(bg.transform, false);
			var ckR = ck.AddComponent<RectTransform>();
			ckR.anchorMin = Vector2.zero;
			ckR.anchorMax = Vector2.one;
			ckR.sizeDelta = Vector2.zero;
			var ckI = ck.AddComponent<UnityEngine.UI.Image>();
			ckI.color = new Color(0.2f, 0.8f, 0.2f, 1f);
			ckI.raycastTarget = false;
			var tgl = toggleContainer.AddComponent<Toggle>();
			tgl.isOn = Addon_MGR.GetRememberEnabledAddonsPreference();
			tgl.targetGraphic = bgI;
			// Assign graphic before BoundChrome; never solid-square the ON glyph (IsToggleCheckmarkGraphic).
			tgl.graphic = ckI;
			tgl.onValueChanged.AddListener(OnRememberEnabledAddonsToggleChanged);
			_rememberEnabledAddonToggle = tgl;
			return row;
		}

	/// <summary>
	/// Unity keeps a managed wrapper after native destroy; <c>== null</c> is true but <see cref="ReferenceEquals"/> to null is false.
	/// Do not use <c>obj != null &amp;&amp; !obj</c> — both sides use Unity's overload and never match destroyed objects.
	/// </summary>
	static bool IsUnityObjectDestroyed(UnityEngine.Object obj) {
		return !ReferenceEquals(obj, null) && obj == null;
	}

	void SanitizeDestroyedPanelRefs() {
		if (IsUnityObjectDestroyed(_panel))
			ClearAddonManagerPanelRefs();
		if (IsUnityObjectDestroyed(_addonsListParent)) {
			// Connectivity: list parent gone means the manager UI is not usable; do not leave _panel/buttons/toggles
			// pointing at a torn hierarchy. Rebuild via CreatePanelIfNeeded after full clear + destroy when possible.
			if (_panel != null)
				DestroyAddonManagerPanelHierarchy();
			else
				ClearAddonManagerPanelRefs();
		}
	}

	/// <summary>
	/// If the inspector or a partial build set <see cref="_panel"/> but not <see cref="_addonsListParent"/>,
	/// recover the scroll content <see cref="RectTransform"/> from the expected hierarchy.
	/// </summary>
	void TryResolveAddonsListParentFromPanel() {
		if (_panel == null || _addonsListParent != null) return;
		Transform t = _panel.transform.Find("ListArea/ScrollView/Content");
		if (t == null)
			t = _panel.transform.Find("ScrollView/Content");
		if (t is RectTransform rt) {
			_addonsListParent = rt;
			if (_blocker == null) {
				Transform p = _panel.transform.parent;
				if (p != null && p.name == "Blocker")
					_blocker = p.gameObject;
			}
			if (_blocker != null && _blockerDimImage == null)
				_blockerDimImage = _blocker.GetComponent<Image>();
			if (_panelModalGroup == null && _panel != null)
				_panelModalGroup = _panel.GetComponent<CanvasGroup>();
		}
	}
	
	/// <summary>
	/// Creates the UI panel dynamically if it wasn't assigned in the inspector
	/// </summary>
	void CreatePanelIfNeeded() {
		SanitizeDestroyedPanelRefs();
		if (_panel != null && _addonsListParent == null)
			TryResolveAddonsListParentFromPanel();
		if (AddonManagerPanelSetupIsComplete()) {
			return;
		}
		if (_panel != null)
			DestroyAddonManagerPanelHierarchy();
		
		// Use a dedicated canvas so the panel is always on top and blocks input (no click-through to viewport).
		// Legacy main-branch layout (see AddonManager_ModernArchive/FromGitMain_Reference): canvas is a scene root, NOT a child of
		// AddonManager_UI — parenting here tied the whole overlay to this GO; if this object or an ancestor is off, nothing renders.
		const int UILayer = 5; // Unity "UI" layer so Canvas and children render
		GameObject canvasObj = new GameObject("AddonManager_Canvas");
		canvasObj.layer = UILayer;
		if (gameObject.scene.IsValid())
			SceneManager.MoveGameObjectToScene(canvasObj, gameObject.scene);
		Canvas canvas = canvasObj.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.overrideSorting = true;
		canvas.sortingOrder = AddonManagerCanvasSortOrder; // Topmost so Add-on Manager captures input
		canvas.pixelPerfect = false;
		var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
		scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920, 1080);
		scaler.matchWidthOrHeight = 0.5f;
		canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
		
		// Full-screen blocker: panel is a CHILD so every click hits blocker or panel, nothing passes through
		GameObject blockerObj = new GameObject("Blocker");
		blockerObj.layer = UILayer;
		blockerObj.transform.SetParent(canvas.transform, false);
		var blockerRect = blockerObj.AddComponent<RectTransform>();
		blockerRect.anchorMin = Vector2.zero;
		blockerRect.anchorMax = Vector2.one;
		blockerRect.sizeDelta = Vector2.zero;
		blockerRect.anchoredPosition = Vector2.zero;
		var blockerImage = blockerObj.AddComponent<UnityEngine.UI.Image>();
		blockerImage.color = RefBgModalDim;
		blockerImage.raycastTarget = true;
		var blockerCanvasGroup = blockerObj.AddComponent<CanvasGroup>();
		blockerCanvasGroup.blocksRaycasts = true;
		blockerCanvasGroup.interactable = true;
		// So MainViewport_UI_EventListener stops treating the 3D view as "hovered" (wheel zoom / UV zoom stay off while this UI is up).
		blockerObj.AddComponent<MainViewport_RaycastBlocker>();
		// Dimmer-only close: a Button on the parent would also fire for clicks on panel children (ExecuteHierarchy bubbles).
		var dimmerClose = blockerObj.AddComponent<AddonManagerDimmerClose>();
		dimmerClose.Bind(ClosePanel);
		blockerObj.SetActive(false);
		_blockerDimImage = blockerImage;
		
		// Centered 16:9 shell based on the Stich add-on-manager reference.
		GameObject panelObj = new GameObject("AddonManager_Panel");
		panelObj.layer = UILayer;
		panelObj.transform.SetParent(blockerObj.transform, false);
		_panel = panelObj;
		
		var rectTransform = panelObj.AddComponent<RectTransform>();
		rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		rectTransform.pivot = new Vector2(0.5f, 0.5f);
		rectTransform.sizeDelta = new Vector2(1200f, 675f);
		rectTransform.anchoredPosition = Vector2.zero;
		
		var image = panelObj.AddComponent<UnityEngine.UI.Image>();
		SpzUiThemeOps.ApplyRoundedControlSprite(image, markEligible: true);
		image.color = new Color(21f / 255f, 21f / 255f, 21f / 255f, 0.985f);
		image.raycastTarget = true;
		
		var canvasGroup = panelObj.AddComponent<CanvasGroup>();
		canvasGroup.blocksRaycasts = true;
		canvasGroup.interactable = true;
		canvasGroup.alpha = 1f;
		_panelModalGroup = canvasGroup;
		
		const float Grid = 8f;
		const float PanelPadding = Grid * 4;
		const float SectionSpacing = Grid;
		const float RowSpacing = Grid * 2;
		
		var verticalLayout = panelObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		verticalLayout.spacing = SectionSpacing;
		verticalLayout.padding = new RectOffset((int)PanelPadding, (int)PanelPadding, (int)PanelPadding, (int)PanelPadding);
		verticalLayout.childControlHeight = true;
		verticalLayout.childControlWidth = true;
		verticalLayout.childForceExpandHeight = false;
		verticalLayout.childForceExpandWidth = true;

		var versionMarker = new GameObject("StichAddonManager_v8");
		versionMarker.transform.SetParent(panelObj.transform, false);
		var markerLE = versionMarker.AddComponent<LayoutElement>();
		markerLE.ignoreLayout = true;
		
		GameObject headerObj = new GameObject("Header");
		headerObj.transform.SetParent(panelObj.transform, false);
		var headerLayoutElement = headerObj.AddComponent<LayoutElement>();
		headerLayoutElement.preferredHeight = 52f;
		headerLayoutElement.minHeight = 48f;
		var headerLayout = headerObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		headerLayout.childControlWidth = true;
		headerLayout.childControlHeight = true;
		headerLayout.childForceExpandWidth = false;
		headerLayout.childForceExpandHeight = true;
		headerLayout.childAlignment = TextAnchor.MiddleCenter;
		headerLayout.spacing = 10f;
		headerLayout.padding = new RectOffset(0, 0, 0, 0);
		
		GameObject titleObj = new GameObject("Title");
		titleObj.transform.SetParent(headerObj.transform, false);
		var titleLE = titleObj.AddComponent<LayoutElement>();
		titleLE.minWidth = 230f;
		titleLE.flexibleWidth = 1f;
		var titleText = titleObj.AddComponent<TextMeshProUGUI>();
		titleText.text = "Add-on Manager";
		titleText.fontSize = 24;
		titleText.color = Color.white;
		titleText.fontStyle = FontStyles.Bold;
		titleText.alignment = TextAlignmentOptions.MidlineLeft;
		titleText.enableWordWrapping = false;
		titleText.overflowMode = TMPro.TextOverflowModes.Overflow;
		titleText.raycastTarget = false;
		
		void AddBarButton(Transform parent, string goName, string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick, Vector2 size, out Button outBtn) {
			var go = new GameObject(goName);
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>().sizeDelta = size;
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = size.x;
			le.minWidth = size.x;
			le.flexibleWidth = 0f;
			le.preferredHeight = size.y;
			var img = go.AddComponent<UnityEngine.UI.Image>();
			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			img.color = bg;
			img.raycastTarget = true;
			var btn = go.AddComponent<UnityEngine.UI.Button>();
			btn.targetGraphic = img;
			btn.onClick.AddListener(onClick);
			var to = new GameObject("Text");
			to.transform.SetParent(go.transform, false);
			var tr = to.AddComponent<RectTransform>();
			tr.anchorMin = Vector2.zero;
			tr.anchorMax = Vector2.one;
			tr.offsetMin = new Vector2(25f, 0f);
			tr.offsetMax = new Vector2(-5f, 0f);
			var tx = to.AddComponent<TextMeshProUGUI>();
			tx.text = label;
			tx.fontSize = 12;
			tx.fontStyle = FontStyles.Normal;
			tx.alignment = TextAlignmentOptions.Center;
			tx.color = fg;
			tx.raycastTarget = false;
			var iconGo = new GameObject("LineIcon");
			iconGo.transform.SetParent(go.transform, false);
			var iconRt = iconGo.AddComponent<RectTransform>();
			iconRt.anchorMin = new Vector2(0f, 0.5f);
			iconRt.anchorMax = new Vector2(0f, 0.5f);
			iconRt.pivot = new Vector2(0f, 0.5f);
			iconRt.anchoredPosition = new Vector2(8f, 0f);
			iconRt.sizeDelta = new Vector2(14f, 14f);
			var iconImg = iconGo.AddComponent<Image>();
			iconImg.sprite = UiRuntimeSprites.GetLineIcon(ResolveHeaderIcon(goName));
			iconImg.color = fg;
			iconImg.preserveAspect = true;
			iconImg.raycastTarget = false;
			outBtn = btn;
		}
		
		AddBarButton(headerObj.transform, "InstallButton", "Install from File", new Color(61f / 255f, 61f / 255f, 61f / 255f, 1f),
			Color.white, OnInstallFromFile, new Vector2(122, 34), out var installBtn);
		_installFromFile_button = installBtn;
		AddBarButton(headerObj.transform, "RefreshButton", "Refresh", new Color(61f / 255f, 61f / 255f, 61f / 255f, 1f),
			Color.white, RefreshAddonsList, new Vector2(82, 34), out var refreshBtn);
		_refresh_button = refreshBtn;
		AddBarButton(headerObj.transform, "LoadAddonsNowButton", "Load addons now", new Color(46f / 255f, 204f / 255f, 113f / 255f, 1f),
			Color.white, OnLoadAddonsNow, new Vector2(126, 34), out _loadAddonsNow_button);
		AddBarButton(headerObj.transform, "SaveAddonSettingsButton", "Save settings", new Color(242f / 255f, 202f / 255f, 80f / 255f, 1f),
			new Color(0.12f, 0.12f, 0.14f, 1f), OnSaveAddonSettings, new Vector2(118, 34), out _saveAddonSettings_button);
		AddBarButton(headerObj.transform, "RunWithAddonsButton", "Restart with addons", new Color(52f / 255f, 152f / 255f, 219f / 255f, 1f),
			Color.white, OnRestartWithAddons, new Vector2(142, 34), out _restartWithAddons_button);
		_closePanel_button = null;
		
		GameObject filterBarObj = new GameObject("FilterBar");
		filterBarObj.transform.SetParent(panelObj.transform, false);
		var filterBarLE = filterBarObj.AddComponent<LayoutElement>();
		filterBarLE.preferredHeight = 66f;
		filterBarLE.minHeight = 66f;
		var filterBarLayout = filterBarObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		filterBarLayout.spacing = 4f;
		filterBarLayout.childControlWidth = false;
		filterBarLayout.childControlHeight = true;
		filterBarLayout.childForceExpandWidth = false;
		filterBarLayout.childForceExpandHeight = false;
		filterBarLayout.childAlignment = TextAnchor.UpperLeft;
		filterBarLayout.padding = new RectOffset(0, 0, 0, 0);
		
		var filterLabelObj = new GameObject("FilterLabel");
		filterLabelObj.transform.SetParent(filterBarObj.transform, false);
		var filterLabelLE = filterLabelObj.AddComponent<LayoutElement>();
		filterLabelLE.preferredWidth = 200f;
		filterLabelLE.preferredHeight = 20f;
		filterLabelLE.minHeight = 20f;
		var filterLabelText = filterLabelObj.AddComponent<TextMeshProUGUI>();
		filterLabelText.text = "Filter";
		filterLabelText.fontSize = 14;
		filterLabelText.color = new Color(0.85f, 0.85f, 0.88f, 1f);
		filterLabelText.alignment = TextAlignmentOptions.MidlineLeft;
		filterLabelText.raycastTarget = false;
		
		var filterPillsObj = new GameObject("FilterPills");
		filterPillsObj.transform.SetParent(filterBarObj.transform, false);
		var pillsRect = filterPillsObj.AddComponent<RectTransform>();
		pillsRect.sizeDelta = new Vector2(230f, 34f);
		var pillsLE = filterPillsObj.AddComponent<LayoutElement>();
		pillsLE.preferredWidth = 230f;
		pillsLE.preferredHeight = 34f;
		pillsLE.minHeight = 34f;
		var pillsBg = filterPillsObj.AddComponent<Image>();
		SpzUiThemeOps.ApplyRoundedControlSprite(pillsBg, markEligible: true);
		pillsBg.color = new Color(39f / 255f, 39f / 255f, 42f / 255f, 0.55f);
		pillsBg.raycastTarget = false;
		var pillsLayout = filterPillsObj.AddComponent<HorizontalLayoutGroup>();
		pillsLayout.spacing = 0f;
		pillsLayout.padding = new RectOffset(3, 3, 3, 3);
		pillsLayout.childControlWidth = false;
		pillsLayout.childControlHeight = true;
		pillsLayout.childForceExpandWidth = false;
		pillsLayout.childForceExpandHeight = true;
		var toggleGroup = filterPillsObj.AddComponent<ToggleGroup>();
		toggleGroup.allowSwitchOff = false;
		_filterAllToggle = CreateFilterToggle("All", filterPillsObj.transform, toggleGroup, 0).GetComponent<Toggle>();
		_filterEnabledToggle = CreateFilterToggle("Enabled", filterPillsObj.transform, toggleGroup, 1).GetComponent<Toggle>();
		_filterDisabledToggle = CreateFilterToggle("Disabled", filterPillsObj.transform, toggleGroup, 2).GetComponent<Toggle>();
		
		GameObject scrollViewObj = new GameObject("ScrollView");
		scrollViewObj.layer = UILayer;
		scrollViewObj.transform.SetParent(panelObj.transform, false);
		var scrollViewRect = scrollViewObj.AddComponent<RectTransform>();
		scrollViewRect.anchorMin = new Vector2(0, 0);
		scrollViewRect.anchorMax = new Vector2(1, 1);
		scrollViewRect.sizeDelta = Vector2.zero;
		scrollViewRect.pivot = new Vector2(0.5f, 0.5f);
		var layoutElementScroll = scrollViewObj.AddComponent<UnityEngine.UI.LayoutElement>();
		layoutElementScroll.preferredHeight = 360f;
		layoutElementScroll.minHeight = 180f;
		layoutElementScroll.flexibleHeight = 1f;
		var scrollViewImage = scrollViewObj.AddComponent<UnityEngine.UI.Image>();
		scrollViewImage.color = new Color(0f, 0f, 0f, 0.01f);
		scrollViewImage.raycastTarget = true;
		var scrollViewMask = scrollViewObj.AddComponent<UnityEngine.UI.Mask>();
		scrollViewMask.showMaskGraphic = false;
		var scrollView = scrollViewObj.AddComponent<UnityEngine.UI.ScrollRect>();
		scrollView.horizontal = false;
		scrollView.vertical = true;
		scrollView.scrollSensitivity = 20f;
		scrollView.movementType = ScrollRect.MovementType.Clamped;
		scrollView.inertia = true;
		scrollView.decelerationRate = 0.135f;
		scrollView.viewport = scrollViewRect;
		scrollView.content = null;
		
		GameObject contentObj = new GameObject("Content");
		contentObj.layer = UILayer;
		contentObj.transform.SetParent(scrollViewObj.transform, false);
		var contentRect = contentObj.AddComponent<RectTransform>();
		contentRect.anchorMin = new Vector2(0, 1);
		contentRect.anchorMax = new Vector2(1, 1);
		contentRect.pivot = new Vector2(0.5f, 1f);
		contentRect.sizeDelta = new Vector2(0, 0);
		contentRect.anchoredPosition = Vector2.zero;
		var contentLayout = contentObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		contentLayout.spacing = RowSpacing;
		contentLayout.padding = new RectOffset(0, 0, (int)Grid, (int)Grid);
		contentLayout.childControlHeight = false;
		contentLayout.childControlWidth = true;
		contentLayout.childForceExpandHeight = false;
		contentLayout.childForceExpandWidth = true;
		var contentSizeFitter = contentObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
		contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
		contentSizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
		scrollView.content = contentRect;
		_addonsListParent = contentRect;
		
		_filterAllToggle.SetIsOnWithoutNotify(true);

		var statusObj = new GameObject("StatusText");
		statusObj.transform.SetParent(panelObj.transform, false);
		var statusLE = statusObj.AddComponent<LayoutElement>();
		statusLE.preferredHeight = 22f;
		statusLE.minHeight = 20f;
		statusLE.flexibleWidth = 1f;
		_statusText = statusObj.AddComponent<TextMeshProUGUI>();
		_statusText.text = "";
		_statusText.fontSize = 12f;
		_statusText.color = new Color(0.63f, 0.63f, 0.67f, 1f);
		_statusText.alignment = TextAlignmentOptions.MidlineLeft;
		_statusText.enableWordWrapping = false;
		_statusText.overflowMode = TextOverflowModes.Ellipsis;
		_statusText.raycastTarget = false;
		
		SetLayerRecursively(_panel.transform, UILayer);
		FitStichPanelToViewport();
		_panel.SetActive(false);
		_blocker = blockerObj;
		Debug.Log("[AddonManager_UI] Panel creation completed, set inactive initially");
	}

	static StudioLineIcon ResolveHeaderIcon(string controlName) {
		if (string.Equals(controlName, "InstallButton", StringComparison.Ordinal))
			return StudioLineIcon.Folder;
		if (string.Equals(controlName, "RefreshButton", StringComparison.Ordinal))
			return StudioLineIcon.Refresh;
		if (string.Equals(controlName, "LoadAddonsNowButton", StringComparison.Ordinal))
			return StudioLineIcon.Play;
		if (string.Equals(controlName, "SaveAddonSettingsButton", StringComparison.Ordinal))
			return StudioLineIcon.Settings;
		return StudioLineIcon.Restart;
	}
	
	static void SetLayerRecursively(Transform t, int layer) {
		t.gameObject.layer = layer;
		for (int i = 0; i < t.childCount; i++)
			SetLayerRecursively(t.GetChild(i), layer);
	}

	void FitStichPanelToViewport() {
		if (_panel == null) return;
		var panelRect = _panel.GetComponent<RectTransform>();
		var blockerRect = _blocker != null ? _blocker.GetComponent<RectTransform>() : null;
		if (panelRect == null) return;
		Vector2 outer = blockerRect != null && blockerRect.rect.width > 1f
			? blockerRect.rect.size
			: new Vector2(1920f, 1080f);
		const float baseWidth = 1200f;
		const float baseHeight = 675f;
		const float margin = 32f;
		float scale = Mathf.Min(1f,
			Mathf.Max(0.1f, (outer.x - margin * 2f) / baseWidth),
			Mathf.Max(0.1f, (outer.y - margin * 2f) / baseHeight));
		panelRect.sizeDelta = new Vector2(baseWidth * scale, baseHeight * scale);
	}

		/// <summary>
		/// Clears references into a torn-down add-on manager UI hierarchy so <see cref="CreatePanelIfNeeded"/> can rebuild cleanly.
		/// </summary>
		void ClearAddonManagerPanelRefs() {
			_panel = null;
			_blocker = null;
			_blockerDimImage = null;
			_panelModalGroup = null;
			_addonsListParent = null;
			_closePanel_button = null;
			_installFromFile_button = null;
			_refresh_button = null;
			_loadAddonsNow_button = null;
			_saveAddonSettings_button = null;
			_restartWithAddons_button = null;
			_statusText = null;
			_filterAllToggle = null;
			_filterEnabledToggle = null;
			_filterDisabledToggle = null;
			_rememberEnabledAddonToggle = null;
			_addonUIItems.Clear();
		}

		/// <summary>
		/// Removes the entire overlay (canvas + blocker + panel). Destroying only <see cref="_panel"/> leaves <see cref="_blocker"/>
		/// and its canvas alive, so the next <see cref="CreatePanelIfNeeded"/> would stack duplicate fullscreen blockers.
		/// </summary>
		void DestroyAddonManagerPanelHierarchy() {
			if (_hidViewportStatusForModal && Viewport_StatusText.instance != null) {
				Viewport_StatusText.instance.PreferVIsible(this);
				_hidViewportStatusForModal = false;
			}
			if (_panel == null) return;
			Canvas canvas = _panel.GetComponentInParent<Canvas>();
			if (canvas != null && canvas.gameObject.name == "AddonManager_Canvas") {
				canvas.gameObject.SetActive(false);
				Destroy(canvas.gameObject);
			} else if (_panel.transform.parent != null && _panel.transform.parent.name == "Blocker") {
				var blockerGo = _panel.transform.parent.gameObject;
				blockerGo.SetActive(false);
				Destroy(blockerGo);
			} else {
				_panel.SetActive(false);
				Destroy(_panel);
			}
			ClearAddonManagerPanelRefs();
		}

		/// <summary>
		/// Ensures <see cref="MainViewport_RaycastBlocker"/> is on the fullscreen blocker so wheel/input does not affect the 3D/UV viewport
		/// while the add-on manager is open (scroll stays in the manager list via UI raycasts).
		/// </summary>
		void EnsureViewportRaycastBlockerOnBlocker() {
			GameObject blockerGo = _blocker;
			if (blockerGo == null && _panel != null && _panel.transform.parent != null
			    && _panel.transform.parent.name == "Blocker")
				blockerGo = _panel.transform.parent.gameObject;
			if (blockerGo == null) return;
			// Keep _blocker in sync so OpenPanel/ClosePanel activate the same fullscreen root as the panel.
			_blocker = blockerGo;
			if (_blockerDimImage == null)
				_blockerDimImage = blockerGo.GetComponent<Image>();
			if (blockerGo.GetComponent<MainViewport_RaycastBlocker>() == null)
				blockerGo.AddComponent<MainViewport_RaycastBlocker>();
			// Migrate older shells that used a parent Button (auto-closed on any in-panel click via hierarchy bubble).
			var legacyCloseBtn = blockerGo.GetComponent<Button>();
			if (legacyCloseBtn != null)
				Destroy(legacyCloseBtn);
			var dimmerClose = blockerGo.GetComponent<AddonManagerDimmerClose>();
			if (dimmerClose == null)
				dimmerClose = blockerGo.AddComponent<AddonManagerDimmerClose>();
			dimmerClose.Bind(ClosePanel);
		}

		/// <summary>Resolves <c>AddonManager_Canvas</c> from the panel hierarchy or this scene's roots (parent lookup can fail on partial hierarchies).</summary>
		Canvas FindAddonManagerOverlayCanvas() {
			if (_panel != null) {
				var c = _panel.GetComponentInParent<Canvas>(true);
				if (c != null) return c;
			}
			if (gameObject.scene.IsValid()) {
				var roots = gameObject.scene.GetRootGameObjects();
				for (int i = 0; i < roots.Length; i++) {
					var go = roots[i];
					if (go == null) continue;
					if (go.name != "AddonManager_Canvas") continue;
					var canvas = go.GetComponent<Canvas>();
					if (canvas != null) return canvas;
				}
			}
			return null;
		}
		
		/// <summary>
		/// Opens the add-on manager panel
		/// </summary>
		public void OpenPanel() {
			bool closeSettingsAfterShow = s_closeSettingsWhenModalShown;
			// Disabled MB cannot run StartCoroutine; Start() may never have run → CreatePanelIfNeeded only here.
			if (!gameObject.activeSelf)
				gameObject.SetActive(true);
			if (!enabled)
				enabled = true;

			CreatePanelIfNeeded();
			TryAddRememberPreferenceRowIfMissing();
			TryEnsureSaveSettingsButton();
			SyncRememberEnabledToggleFromPrefs();
			if (!_draftDirty)
				SeedDraftFromLiveAddons();
			
			if (_panel == null) {
				Debug.LogError("[AddonManager_UI] Failed to open panel: _panel is null and could not be created.");
				return;
			}
			
			EnsureViewportRaycastBlockerOnBlocker();
			if (_blocker != null && _blockerDimImage == null)
				_blockerDimImage = _blocker.GetComponent<Image>();
			if (_panelModalGroup == null)
				_panelModalGroup = _panel.GetComponent<CanvasGroup>();
			
			// Activate hierarchy: canvas (parent of blocker) must be active for anything to render.
			Canvas rootCanvas = FindAddonManagerOverlayCanvas();
			if (rootCanvas != null) {
				rootCanvas.gameObject.SetActive(true);
				rootCanvas.overrideSorting = true;
				rootCanvas.sortingOrder = AddonManagerCanvasSortOrder;
				rootCanvas.enabled = true;
			} else {
				Debug.LogError("[AddonManager_UI] OpenPanel: could not resolve AddonManager overlay Canvas — UI will not render.");
			}
			if (_blocker != null) _blocker.SetActive(true);
			try {
				if (_blocker != null) {
					var prePanelBlockerRt = _blocker.GetComponent<RectTransform>();
					if (prePanelBlockerRt != null)
						LayoutRebuilder.ForceRebuildLayoutImmediate(prePanelBlockerRt);
				}
				Canvas.ForceUpdateCanvases();
			} catch (System.Exception e) {
				Debug.LogWarning($"[AddonManager_UI] Pre-panel blocker layout (non-fatal): {e.Message}");
			}
			_panel.SetActive(true);
			if (_blockerDimImage != null)
				_blockerDimImage.color = RefBgModalDim;
			if (_panelModalGroup != null) {
				_panelModalGroup.alpha = 1f;
				_panelModalGroup.interactable = true;
			}
			if (rootCanvas != null)
				s_pendingOpenRequest = false;

			if (closeSettingsAfterShow) {
				s_closeSettingsWhenModalShown = false;
				CloseSettingsPanelIfBound();
			}
			
			if (!_hidViewportStatusForModal && Viewport_StatusText.instance != null) {
				Viewport_StatusText.instance.PreferHidden(this);
				_hidViewportStatusForModal = true;
			}
			
			try {
				Canvas.ForceUpdateCanvases();
				FitStichPanelToViewport();
				var panelRect = _panel.GetComponent<RectTransform>();
				if (panelRect != null) {
					LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
					Canvas.ForceUpdateCanvases();
				}
			} catch (System.Exception e) {
				Debug.LogWarning($"[AddonManager_UI] Layout pass threw (non-fatal): {e.Message}");
			}
			
			try {
				if (Addon_MGR.instance != null)
					Addon_MGR.instance.RefreshAddons();
				RefreshAddonsList();
			} catch (System.Exception e) {
				Debug.LogError($"[AddonManager_UI] RefreshAddonsList threw: {e.Message}\n{e.StackTrace}");
			}
		}
		
		/// <summary>
		/// Closes the add-on manager panel
		/// </summary>
		public void ClosePanel() {
			if (_hidViewportStatusForModal && Viewport_StatusText.instance != null) {
				Viewport_StatusText.instance.PreferVIsible(this);
				_hidViewportStatusForModal = false;
			}
			if (_blocker != null) _blocker.SetActive(false);
			if (_panel != null) _panel.SetActive(false);
		}
		
		/// <summary>
		/// Requests Python to load all currently enabled add-ons.
		/// </summary>
		void OnLoadAddonsNow() {
			ShowStatus("Loading addons...", true);
			if (Addon_MGR.instance != null) {
				Addon_MGR.instance.RequestLoadAllEnabledAddonsNow(() => {
					ShowStatus("Addons load requested", true);
					RefreshAddonsList();
				});
			} else {
				ShowStatus("Add-on manager not available", false);
			}
		}

		/// <summary>
		/// Applies dial draft to live enable/disable (ribbon tabs), then persists the selection.
		/// </summary>
		void OnSaveAddonSettings() {
			if (Addon_MGR.instance == null) {
				ShowStatus("Add-on manager not available", false);
				return;
			}
			var addons = Addon_MGR.instance.GetAddons();
			int changed = 0;
			_suppressEnabledListRefresh = true;
			try {
				foreach (var kvp in addons) {
					if (kvp.Value == null) continue;
					bool want = GetDraftEnabled(kvp.Key, kvp.Value.isEnabled);
					if (want == kvp.Value.isEnabled) continue;
					changed++;
					if (want)
						Addon_MGR.instance.EnableAddon(kvp.Key);
					else
						Addon_MGR.instance.DisableAddon(kvp.Key);
				}
				Addon_MGR.instance.PersistEnabledAddonSelectionNow();
				SeedDraftFromLiveAddons();
			} finally {
				_suppressEnabledListRefresh = false;
			}
			RefreshAddonsList();
			if (changed == 0)
				ShowStatus("Settings saved. Ribbon already matches dials — selection persisted for next launch.", true);
			else
				ShowStatus($"Settings saved — {changed} add-on(s) applied to ribbon and persisted.", true);
		}

		bool GetDraftEnabled(string addonId, bool fallbackActual) {
			if (!string.IsNullOrEmpty(addonId) && _draftEnabledById.TryGetValue(addonId, out bool draft))
				return draft;
			return fallbackActual;
		}

		void SetDraftEnabled(string addonId, bool enabled) {
			if (string.IsNullOrEmpty(addonId)) return;
			_draftEnabledById[addonId] = enabled;
			_draftDirty = true;
		}

		void SeedDraftFromLiveAddons() {
			_draftEnabledById.Clear();
			_draftDirty = false;
			if (Addon_MGR.instance == null) return;
			foreach (var kvp in Addon_MGR.instance.GetAddons()) {
				if (kvp.Value == null) continue;
				_draftEnabledById[kvp.Key] = kvp.Value.isEnabled;
			}
		}

		/// <summary>
		/// Launches Run_with_Addons.bat (same way Run_noQuickEdit runs for WebUI) then quits so the bat starts the game with Python on PATH.
		/// </summary>
		void OnRestartWithAddons() {
			if (_restartWithAddons_button != null)
				_restartWithAddons_button.interactable = false;
			ShowStatus("Restarting with addons…", true);
			Launch_Addons_Bat_File.RestartWithAddons();
		}

		/// <summary>Status line helper for restart path (Editor messaging / in-progress feedback).</summary>
		public void ShowRestartStatus(string message, bool isSuccess) {
			ShowStatus(message, isSuccess);
			// Player builds also need the button back after a failed restart attempt.
			if (_restartWithAddons_button != null)
				_restartWithAddons_button.interactable = true;
		}

		/// <summary>
		/// Opens file browser to select a zip file for installation
		/// </summary>
		void OnInstallFromFile() {
			FileBrowser.SetFilters(true, new FileBrowser.Filter("Add-on", "zip"));
			FileBrowser.SetDefaultFilter("zip");

			// SimpleFileBrowser prefab uses sortingOrder ~2016; our overlay uses AddonManagerCanvasSortOrder — browser would render underneath.
			Canvas fbCanvas = FileBrowser.Instance != null ? FileBrowser.Instance.GetComponent<Canvas>() : null;
			int prevFbSort = fbCanvas != null ? fbCanvas.sortingOrder : 0;
			bool prevFbOverride = fbCanvas != null && fbCanvas.overrideSorting;
			if (fbCanvas != null) {
				fbCanvas.overrideSorting = true;
				fbCanvas.sortingOrder = AddonManagerCanvasSortOrder + 100;
			}

			void RestoreFileBrowserSortOrder() {
				var inst = FileBrowser.Instance;
				if (inst == null) return;
				var c = inst.GetComponent<Canvas>();
				if (c == null) return;
				c.sortingOrder = prevFbSort;
				c.overrideSorting = prevFbOverride;
			}

			FileBrowser.ShowLoadDialog((paths) => {
				RestoreFileBrowserSortOrder();
				if (paths != null && paths.Length > 0)
					InstallAddon(paths[0]);
			}, RestoreFileBrowserSortOrder, FileBrowser.PickMode.Files, false, null, null, "Install Add-on", "Install");
		}
		
		/// <summary>
		/// Installs an add-on from a zip file
		/// </summary>
		void InstallAddon(string zipPath) {
			// Validate input
			if (string.IsNullOrEmpty(zipPath)) {
				ShowStatus("Invalid file path", false);
				return;
			}
			
			if (AddonInstaller_MGR.instance == null) {
				ShowStatus("Add-on installer not available", false);
				return;
			}
			
			ShowStatus("Installing add-on...", true);
			
			AddonInstaller_MGR.instance.InstallAddonFromZip(zipPath, (success, message, addonId) => {
				if (success) {
					ShowStatus($"Add-on '{addonId}' installed successfully!", true);
					RefreshAddonsList();
				} else {
					ShowStatus($"Installation failed: {message}", false);
				}
			});
		}
		
		GameObject CreateFilterToggle(string label, Transform parent, ToggleGroup toggleGroup, int filterValue) {
			var toggleObj = new GameObject($"Filter_{label}");
			toggleObj.transform.SetParent(parent, false);
			float width = label == "All" ? 58f : 82f;
			toggleObj.AddComponent<RectTransform>().sizeDelta = new Vector2(width, 28f);
			var le = toggleObj.AddComponent<LayoutElement>();
			le.preferredWidth = width;
			le.minWidth = width;
			le.preferredHeight = 28f;
			var toggleBg = toggleObj.AddComponent<UnityEngine.UI.Image>();
			SpzUiThemeOps.ApplyRoundedControlSprite(toggleBg, markEligible: true);
			toggleBg.color = new Color(0f, 0f, 0f, 0f);
			toggleBg.raycastTarget = true;
			var toggle = toggleObj.AddComponent<Toggle>();
			toggle.group = toggleGroup;
			toggle.targetGraphic = toggleBg;
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(toggleObj.transform, false);
			var labelRect = labelObj.AddComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.sizeDelta = Vector2.zero;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 14;
			labelText.fontStyle = FontStyles.Normal;
			labelText.color = new Color(0.63f, 0.63f, 0.67f, 1f);
			labelText.alignment = TextAlignmentOptions.Center;
			labelText.raycastTarget = false;
			toggle.onValueChanged.AddListener((isOn) => {
				if (isOn) {
					_filterState = filterValue;
					if (Addon_MGR.instance != null && _addonsListParent != null)
						RefreshAddonsList();
				}
				ApplyThemeTokens();
			});
			return toggleObj;
		}
		
		/// <summary>
		/// Refreshes the list of add-ons with current filter applied (main-branch behavior — no search).
		/// </summary>
		public void RefreshAddonsList() {
			if (_addonsListParent == null) {
				Debug.LogError("[AddonManager_UI] RefreshAddonsList: _addonsListParent is null! Cannot create items.");
				ShowStatus("Error: List parent not initialized", false);
				return;
			}
			
			foreach (var item in _addonUIItems.Values) {
				if (item != null)
					Destroy(item);
			}
			_addonUIItems.Clear();
			
			if (Addon_MGR.instance == null) {
				ShowStatus("Add-on manager not available", false);
				Debug.LogError("[AddonManager_UI] Addon_MGR.instance is null!");
				return;
			}

			if (!_draftDirty)
				SeedDraftFromLiveAddons();
			
			var addons = Addon_MGR.instance.GetAddons();
			var filteredAddons = new List<KeyValuePair<string, Addon_MGR.AddonInfo>>();
			int enabledCount = 0;
			int disabledCount = 0;
			
			foreach (var kvp in addons) {
				bool draftOn = GetDraftEnabled(kvp.Key, kvp.Value != null && kvp.Value.isEnabled);
				if (draftOn) enabledCount++;
				else disabledCount++;
				bool shouldShow = _filterState == 0
					|| (_filterState == 1 && draftOn)
					|| (_filterState == 2 && !draftOn);
				if (shouldShow)
					filteredAddons.Add(kvp);
			}
			
			if (addons.Count == 0) {
				ShowStatus("No add-ons installed. Add-ons should be in StreamingAssets/Addons/", false);
				Debug.LogWarning("[AddonManager_UI] No addons found. Check StreamingAssets/Addons/ directory.");
			}
			
			foreach (var kvp in filteredAddons)
				CreateAddonListItem(kvp.Key, kvp.Value);
			
			if (_addonsListParent != null) {
				LayoutRebuilder.ForceRebuildLayoutImmediate(_addonsListParent);
				Canvas.ForceUpdateCanvases();
			}
			
			string filterText = _filterState == 0 ? "All" : (_filterState == 1 ? "Enabled" : "Disabled");
			if (addons.Count > 0) {
				if (filteredAddons.Count == 0)
					ShowStatus("No add-ons match the current filter.", false);
				else
					ShowStatus($"Showing {filteredAddons.Count} of {addons.Count} add-on(s) ({enabledCount} enabled, {disabledCount} disabled) — Filter: {filterText}", true);
			}
			ApplyThemeTokens();
		}

		/// <summary>
		/// Maps REF palette roles to semantic theme tokens on known manager widgets only.
		/// Re-run after list rebuilds; does not touch animation or layout.
		/// </summary>
		void ApplyThemeTokens() {
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				_statusOk = kAuthoredStatusOk;
				_statusFail = kAuthoredStatusFail;
				_statusMuted = kAuthoredStatusMuted;
				if (_panel != null) {
					// Full unwind: ColorBlocks / TMP metrics / line icons — not Graphic colors alone.
					SpzUiThemeOps.RestoreBoundChromeUnder(_panel.transform);
					SpzUiThemeOps.RefreshScaledLayoutGroupsUnder(_panel.transform);
					RestoreHeaderButtonAuthoredChrome(_installFromFile_button);
					RestoreHeaderButtonAuthoredChrome(_refresh_button);
					RestoreHeaderButtonAuthoredChrome(_loadAddonsNow_button);
					RestoreHeaderButtonAuthoredChrome(_saveAddonSettings_button);
					RestoreHeaderButtonAuthoredChrome(_restartWithAddons_button);
				}
				if (_closePanel_button != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_closePanel_button.transform);
				return;
			}
			var t = SpzUiThemeOps.Active;
			_statusOk = t.success;
			_statusFail = t.danger;
			_statusMuted = t.textMuted;
			bool boundChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
			if (_panel != null) {
				var panelImg = _panel.GetComponent<Image>();
				if (panelImg != null) {
					// Keep the manager shell opaque so viewport help text does not bleed through.
					Color shell = t.panelBg;
					shell.a = Mathf.Max(shell.a, 0.96f);
					SpzUiThemeOps.ApplyBoundChromeGraphic(panelImg, shell);
				}
				var panelVlg = _panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
				if (panelVlg != null) {
					int pad = Mathf.RoundToInt(SpzUiThemeOps.ScaledSpace(boundChrome ? 3 : 4));
					panelVlg.spacing = SpzUiThemeOps.ScaledSpace(boundChrome ? 2 : 1);
					panelVlg.padding = new RectOffset(pad, pad, pad, pad);
				}
				var header = _panel.transform.Find("Header");
				if (header != null) {
					var headerHlg = header.GetComponent<HorizontalLayoutGroup>();
					if (headerHlg != null) {
						headerHlg.spacing = SpzUiThemeOps.ScaledSpace(boundChrome ? 6 : 8);
						headerHlg.childAlignment = TextAnchor.MiddleLeft;
						int hPad = Mathf.RoundToInt(SpzUiThemeOps.ScaledSpace(boundChrome ? 2 : 0));
						headerHlg.padding = new RectOffset(hPad, hPad, 0, 0);
					}
				}
				var title = _panel.transform.Find("Header/Title")?.GetComponent<TextMeshProUGUI>();
				if (title != null) {
					CaptureBasePt(ref _themeTitleBasePt, title, 22f);
					SpzUiThemeOps.ApplyBoundChromeTmp(title, t.textPrimary, _themeTitleBasePt);
					if (boundChrome && SpzUiThemeOps.RibbonIconOnlyActive) {
						// Use design base — never title.fontSize * 0.92 (would compound if capture raced).
						float basePt = _themeTitleBasePt > 0.05f ? _themeTitleBasePt : 22f;
						title.fontSize = Mathf.Max(16f, basePt * t.fontScale * 0.92f);
					}
				}
				var filterLabel = _panel.transform.Find("FilterBar/FilterLabel")?.GetComponent<TextMeshProUGUI>();
				if (filterLabel != null) {
					CaptureBasePt(ref _themeFilterLabelBasePt, filterLabel, 14f);
					SpzUiThemeOps.ApplyBoundChromeTmp(filterLabel, t.textPrimary, _themeFilterLabelBasePt);
				}
				var pills = _panel.transform.Find("FilterBar/FilterPills")?.GetComponent<Image>();
				if (pills != null)
					SpzUiThemeOps.ApplyBoundChromeGraphic(pills, new Color(t.controlBg.r, t.controlBg.g, t.controlBg.b, 0.55f));
				var rememberLabel = _panel.transform.Find("RememberEnabledRow/Label")?.GetComponent<TextMeshProUGUI>();
				if (rememberLabel != null) {
					CaptureBasePt(ref _themeRememberLabelBasePt, rememberLabel, 13f);
					SpzUiThemeOps.ApplyBoundChromeTmp(rememberLabel, t.textMuted, _themeRememberLabelBasePt);
				}
			}
			if (_closePanel_button != null)
				SpzUiThemeOps.ApplyBoundChromeSelectable(_closePanel_button, t.controlBg, t.accent);
			if (_installFromFile_button != null)
				ThemeHeaderButton(_installFromFile_button, t.controlBg, t.accent, t.textPrimary);
			if (_refresh_button != null)
				ThemeHeaderButton(_refresh_button, t.controlBg, t.accent, t.textPrimary);
			if (_loadAddonsNow_button != null)
				ThemeHeaderButton(_loadAddonsNow_button,
					boundChrome ? t.controlBg : t.success,
					t.accent,
					t.textPrimary);
			if (_saveAddonSettings_button != null) {
				Color saveFg = boundChrome
					? new Color(0.235f, 0.184f, 0f, 1f)
					: new Color(0.12f, 0.12f, 0.14f, 1f);
				ThemeHeaderButton(_saveAddonSettings_button, t.accent, t.selection, saveFg);
			}
			if (_restartWithAddons_button != null) {
				// BoundChrome primary action: metallic gold fill + dark on-primary text. Default SPZ keeps light label on accent.
				Color restartFg = boundChrome
					? new Color(0.235f, 0.184f, 0f, 1f)
					: t.textPrimary;
				ThemeHeaderButton(_restartWithAddons_button, t.accent, t.selection, restartFg);
			}
			if (_addonsListParent != null) {
				var listImg = _addonsListParent.GetComponent<Image>();
				if (listImg != null)
					SpzUiThemeOps.ApplyBoundChromeGraphic(listImg, t.fieldBg);
				var listVlg = _addonsListParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
				if (listVlg != null) {
					int listPad = Mathf.RoundToInt(SpzUiThemeOps.ScaledSpace(1));
					listVlg.spacing = SpzUiThemeOps.ScaledSpace(2);
					listVlg.padding = new RectOffset(0, 0, listPad, listPad);
				}
			}
			foreach (var item in _addonUIItems.Values) {
				if (item == null) continue;
				ThemeAddonListItem(item, t);
			}
			if (_statusText != null) {
				CaptureBasePt(ref _themeStatusBasePt, _statusText, 13f);
				Color statusColor = !string.IsNullOrEmpty(_statusText.text) && _lastStatusIsSuccess.HasValue
					? (_lastStatusIsSuccess.Value ? _statusOk : _statusFail)
					: t.textMuted;
				SpzUiThemeOps.ApplyBoundChromeTmp(_statusText, statusColor, _themeStatusBasePt);
			}
			if (_filterAllToggle != null || _filterEnabledToggle != null || _filterDisabledToggle != null) {
				ThemeFilterToggle(_filterAllToggle, t);
				ThemeFilterToggle(_filterEnabledToggle, t);
				ThemeFilterToggle(_filterDisabledToggle, t);
			}
			if (_rememberEnabledAddonToggle != null) {
				SpzUiThemeOps.ThemeCheckboxToggle(
					_rememberEnabledAddonToggle, t.controlBg, t.accent, t.success);
			}
		}

		static void CaptureBasePt(ref float stored, TextMeshProUGUI tmp, float fallback) {
			if (stored > 0.05f || tmp == null) return;
			float current = tmp.fontSize;
			float scale = SpzUiThemeOps.Active.fontScale;
			if (scale > 0.05f && Mathf.Abs(scale - 1f) > 0.001f)
				stored = current / scale;
			else
				stored = current > 0.05f ? current : fallback;
		}

		static void ThemeHeaderButton(Button button, Color normal, Color highlighted, Color foreground) {
			if (button == null) return;
			SpzUiThemeOps.ApplyBoundChromeSelectable(button, normal, highlighted);
			var label = button.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
			var icon = button.transform.Find("LineIcon")?.GetComponent<Image>();
			bool boundChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
			bool iconOnly = boundChrome && SpzUiThemeOps.RibbonIconOnlyActive;
			if (icon != null && SpzUiThemeOps.ShouldRecolorBoundChrome) {
				var iconRt = icon.rectTransform;
				iconRt.anchorMin = new Vector2(iconOnly ? 0.5f : 0f, 0.5f);
				iconRt.anchorMax = new Vector2(iconOnly ? 0.5f : 0f, 0.5f);
				iconRt.pivot = new Vector2(iconOnly ? 0.5f : 0f, 0.5f);
				iconRt.sizeDelta = new Vector2(iconOnly ? 18f : 16f, iconOnly ? 18f : 16f);
				iconRt.anchoredPosition = iconOnly ? Vector2.zero : new Vector2(10f, 0f);
				icon.gameObject.SetActive(true);
				SpzUiThemeOps.ApplyLineIconTint(icon);
				if (iconOnly)
					icon.color = foreground.a > 0.01f ? foreground : SpzUiThemeOps.Active.iconTint;
			}
			if (label != null) {
				if (iconOnly) {
					SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(label);
					label.maxVisibleCharacters = 0;
					label.color = new Color(foreground.r, foreground.g, foreground.b, 0f);
				} else {
					label.maxVisibleCharacters = int.MaxValue;
					float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(label, 13f);
					SpzUiThemeOps.ApplyBoundChromeTmp(label, foreground, basePt);
					var labelRt = label.rectTransform;
					// Leave a fixed gutter after the left-aligned line icon so labels share one column.
					labelRt.offsetMin = new Vector2(boundChrome ? 30f : 25f, 0f);
					labelRt.offsetMax = new Vector2(-5f, 0f);
				}
			}
			if (button.targetGraphic is Image btnImg)
				SpzUiThemeOps.ApplyRoundedControlSprite(btnImg);
			var le = button.GetComponent<LayoutElement>();
			if (le != null) {
				if (iconOnly) {
					le.preferredWidth = 40f;
					le.minWidth = 36f;
				} else {
					float authored = ResolveAuthoredHeaderButtonWidth(button.gameObject.name);
					le.preferredWidth = authored;
					le.minWidth = authored;
				}
			}
			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(button);
		}

		static float ResolveAuthoredHeaderButtonWidth(string goName) {
			if (string.Equals(goName, "InstallButton", StringComparison.Ordinal)) return 122f;
			if (string.Equals(goName, "RefreshButton", StringComparison.Ordinal)) return 82f;
			if (string.Equals(goName, "LoadAddonsNowButton", StringComparison.Ordinal)) return 126f;
			if (string.Equals(goName, "SaveAddonSettingsButton", StringComparison.Ordinal)) return 118f;
			if (string.Equals(goName, "RunWithAddonsButton", StringComparison.Ordinal)) return 142f;
			return 100f;
		}

		static void RestoreHeaderButtonAuthoredChrome(Button button) {
			if (button == null) return;
			var label = button.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
			if (label != null) {
				label.maxVisibleCharacters = int.MaxValue;
				SpzUiThemeOps.RestoreAuthoredGraphic(label);
				var labelRt = label.rectTransform;
				labelRt.offsetMin = new Vector2(25f, 0f);
				labelRt.offsetMax = new Vector2(-5f, 0f);
			}
			var icon = button.transform.Find("LineIcon")?.GetComponent<Image>();
			if (icon != null) {
				var iconRt = icon.rectTransform;
				iconRt.anchorMin = new Vector2(0f, 0.5f);
				iconRt.anchorMax = new Vector2(0f, 0.5f);
				iconRt.pivot = new Vector2(0f, 0.5f);
				iconRt.anchoredPosition = new Vector2(8f, 0f);
				iconRt.sizeDelta = new Vector2(14f, 14f);
				icon.gameObject.SetActive(true);
			}
			var le = button.GetComponent<LayoutElement>();
			if (le != null) {
				float authored = ResolveAuthoredHeaderButtonWidth(button.gameObject.name);
				le.preferredWidth = authored;
				le.minWidth = authored;
			}
		}

		static void ThemeFilterToggle(Toggle toggle, SpzUiThemeOps.ThemeTokens t) {
			if (toggle == null) return;
			Color face = toggle.isOn
				? Color.Lerp(t.controlBg, t.accent, 0.14f)
				: t.controlBg;
			// Flat tool radios — Compact labels; avoid a≈0 SolidSquare faces that kill All/Enabled/Disabled hits.
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, face, t.accent, toggle.isOn ? t.textPrimary : t.textMuted);
		}

		void ThemeAddonListItem(GameObject item, SpzUiThemeOps.ThemeTokens t) {
			Transform remove = item.transform.Find("RemoveBtn");
			if (remove == null) remove = item.transform.Find("RemoveButton");
			if (remove != null) {
				var removeBtn = remove.GetComponent<Button>();
				if (removeBtn != null) {
					Color dangerBg = Color.Lerp(t.panelBg, t.danger, 0.18f);
					SpzUiThemeOps.ApplyBoundChromeSelectable(removeBtn, dangerBg, Color.Lerp(dangerBg, t.danger, 0.28f));
				}
				var removeLabel = remove.GetComponentInChildren<TextMeshProUGUI>(true);
				if (removeLabel != null) {
					float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(removeLabel, 12f);
					SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(removeLabel, new Color(t.danger.r, t.danger.g, t.danger.b, 0.88f), basePt);
				}
				if (removeBtn != null)
					SpzUiThemeOps.ClearNonFaceRaycastsForTheme(removeBtn);
			}
			var toggle = item.transform.Find("StatusToggle")?.GetComponent<Toggle>();
			if (toggle == null)
				toggle = item.GetComponentInChildren<Toggle>(true);
			string itemAddonId = null;
			if (item.name != null && item.name.StartsWith("AddonItem_", StringComparison.Ordinal))
				itemAddonId = item.name.Substring("AddonItem_".Length);
			bool enabled = toggle != null && toggle.isOn;
			if (!string.IsNullOrEmpty(itemAddonId) && Addon_MGR.instance != null
			    && Addon_MGR.instance.GetAddons().TryGetValue(itemAddonId, out var liveInfo) && liveInfo != null)
				enabled = GetDraftEnabled(itemAddonId, liveInfo.isEnabled);
			var name = item.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
			if (name != null) {
				float nameBase = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(name, 14f);
				SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(name, t.textPrimary, nameBase);
				name.raycastTarget = false;
			}
			if (toggle != null) {
				Color ringColor = enabled ? t.success : t.textMuted;
				var ringImg = toggle.transform.Find("Ring")?.GetComponent<Image>();
				if (ringImg != null) {
					SpzUiThemeOps.ApplyBoundChromeGraphic(ringImg, ringColor);
					ringImg.preserveAspect = true;
				}
				Image fill = toggle.graphic as Image;
				if (fill == null)
					fill = toggle.transform.Find("Ring/Checkmark")?.GetComponent<Image>();
				if (fill != null) {
					SpzUiThemeOps.ApplyBoundChromeGraphic(fill, t.success);
					fill.preserveAspect = true;
					fill.gameObject.SetActive(true);
					// Prefer Graphic.enabled over canvasRenderer.SetAlpha so Restore SPZ can unwind.
					fill.enabled = enabled;
					fill.canvasRenderer.SetAlpha(enabled ? 1f : 0f);
				}
			}
		}

		void OnAddonEnabledStateChanged(string addonId) {
			// Always mirror live enable into draft — async load-fail must not leave dial ON while isEnabled is false.
			if (!string.IsNullOrEmpty(addonId)
			    && Addon_MGR.instance != null
			    && Addon_MGR.instance.GetAddons().TryGetValue(addonId, out var live)
			    && live != null)
				_draftEnabledById[addonId] = live.isEnabled;
			if (_suppressEnabledListRefresh) {
				SyncAddonRowVisual(addonId);
				RefreshStatusCountsOnly();
				return;
			}
			// Defer so EventSystem finishes with the clicked dial before we destroy list rows.
			ScheduleRefreshAddonsList();
		}

		void ScheduleRefreshAddonsList() {
			if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
				RefreshAddonsList();
				return;
			}
			if (_deferredListRefresh != null)
				StopCoroutine(_deferredListRefresh);
			_deferredListRefresh = StartCoroutine(CoRefreshAddonsListDeferred());
		}

		IEnumerator CoRefreshAddonsListDeferred() {
			yield return null;
			_deferredListRefresh = null;
			RefreshAddonsList();
		}

		void RefreshStatusCountsOnly() {
			if (Addon_MGR.instance == null) return;
			var addons = Addon_MGR.instance.GetAddons();
			int enabledCount = 0;
			int disabledCount = 0;
			int shown = 0;
			foreach (var kvp in addons) {
				bool draftOn = GetDraftEnabled(kvp.Key, kvp.Value != null && kvp.Value.isEnabled);
				if (draftOn) enabledCount++;
				else disabledCount++;
				bool shouldShow = _filterState == 0
					|| (_filterState == 1 && draftOn)
					|| (_filterState == 2 && !draftOn);
				if (shouldShow) shown++;
			}
			string filterText = _filterState == 0 ? "All" : (_filterState == 1 ? "Enabled" : "Disabled");
			if (addons.Count == 0)
				ShowStatus("No add-ons installed. Add-ons should be in StreamingAssets/Addons/", false);
			else if (shown == 0)
				ShowStatus("No add-ons match the current filter.", false);
			else
				ShowStatus($"Showing {shown} of {addons.Count} add-on(s) ({enabledCount} on, {disabledCount} off) — Filter: {filterText}. Save settings to keep next launch.", true);
		}

		void SyncAddonRowVisual(string addonId) {
			if (string.IsNullOrEmpty(addonId) || !_addonUIItems.TryGetValue(addonId, out var item) || item == null)
				return;
			if (Addon_MGR.instance == null || !Addon_MGR.instance.GetAddons().TryGetValue(addonId, out var info))
				return;
			var toggle = item.transform.Find("StatusToggle")?.GetComponent<Toggle>();
			if (toggle == null) return;
			// Live enable flag changed (save apply / load failure) — sync this id's draft entry.
			_draftEnabledById[addonId] = info.isEnabled;
			bool showOn = info.isEnabled;
			toggle.SetIsOnWithoutNotify(showOn);
			ApplyStatusDialVisual(toggle, showOn);
			bool stillVisible = _filterState == 0
				|| (_filterState == 1 && showOn)
				|| (_filterState == 2 && !showOn);
			if (!stillVisible && !_suppressEnabledListRefresh)
				ScheduleRefreshAddonsList();
		}

		void ApplyStatusDialVisual(Toggle toggle, bool enabled) {
			if (toggle == null) return;
			Color ring = enabled ? _statusOk : _statusMuted;
			var ringImg = toggle.transform.Find("Ring")?.GetComponent<Image>();
			if (ringImg != null) {
				ringImg.color = ring;
				ringImg.preserveAspect = true;
			}
			// Prefer manual Checkmark — Toggle.graphic is left null so Unity does not hide it mid-click.
			Image fillImg = toggle.graphic as Image;
			if (fillImg == null)
				fillImg = toggle.transform.Find("Ring/Checkmark")?.GetComponent<Image>();
			if (fillImg != null) {
				fillImg.color = _statusOk;
				fillImg.preserveAspect = true;
				fillImg.gameObject.SetActive(true);
				fillImg.canvasRenderer.SetAlpha(enabled ? 1f : 0f);
			}
		}
		
		void CreateAddonListItem(string addonId, Addon_MGR.AddonInfo addonInfo) {
			if (_addonsListParent == null) {
				Debug.LogError($"[AddonManager_UI] CreateAddonListItem: _addonsListParent is null for addon {addonId}");
				return;
			}
			
			if (_addonUIItems.ContainsKey(addonId)) {
				var existingItem = _addonUIItems[addonId];
				if (existingItem != null)
					Destroy(existingItem);
				_addonUIItems.Remove(addonId);
			}
			
			const float statusSize = 14f;
			const float statusHitPad = 28f;
			var itemObj = new GameObject($"AddonItem_{addonId}");
			itemObj.transform.SetParent(_addonsListParent, false);
			itemObj.layer = _addonsListParent.gameObject.layer;
			itemObj.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 40f);
			var itemLayout = itemObj.AddComponent<LayoutElement>();
			itemLayout.preferredHeight = 40f;
			itemLayout.minHeight = 38f;
			itemLayout.minWidth = 440f;
			var horizontalLayout = itemObj.AddComponent<HorizontalLayoutGroup>();
			horizontalLayout.spacing = 10f;
			horizontalLayout.padding = new RectOffset(0, 0, 4, 4);
			horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
			horizontalLayout.childControlWidth = true;
			// Keep height control off for the status disc so layout cannot stretch it into an oval.
			horizontalLayout.childControlHeight = false;
			horizontalLayout.childForceExpandWidth = false;
			horizontalLayout.childForceExpandHeight = false;

			var toggleObj = new GameObject("StatusToggle");
			toggleObj.transform.SetParent(itemObj.transform, false);
			var toggleRect = toggleObj.AddComponent<RectTransform>();
			toggleRect.sizeDelta = new Vector2(statusHitPad, statusHitPad);
			var toggleLE = toggleObj.AddComponent<LayoutElement>();
			toggleLE.preferredWidth = statusHitPad;
			toggleLE.minWidth = statusHitPad;
			toggleLE.flexibleWidth = 0f;
			toggleLE.preferredHeight = statusHitPad;
			toggleLE.minHeight = statusHitPad;
			toggleLE.flexibleHeight = 0f;
			// Invisible hit pad — full row-height target; Color.clear still raycasts when raycastTarget is true.
			var hitPad = toggleObj.AddComponent<Image>();
			hitPad.color = Color.clear;
			hitPad.raycastTarget = true;
			var ringObj = new GameObject("Ring");
			ringObj.transform.SetParent(toggleObj.transform, false);
			var ringRect = ringObj.AddComponent<RectTransform>();
			ringRect.anchorMin = new Vector2(0.5f, 0.5f);
			ringRect.anchorMax = new Vector2(0.5f, 0.5f);
			ringRect.pivot = new Vector2(0.5f, 0.5f);
			ringRect.sizeDelta = new Vector2(statusSize, statusSize);
			var toggleRing = ringObj.AddComponent<Image>();
			toggleRing.sprite = UiRuntimeSprites.CircleRing;
			toggleRing.type = Image.Type.Simple;
			toggleRing.preserveAspect = true;
			toggleRing.raycastTarget = false;
			var toggleCheckmarkObj = new GameObject("Checkmark");
			toggleCheckmarkObj.transform.SetParent(ringObj.transform, false);
			var toggleCheckmarkRect = toggleCheckmarkObj.AddComponent<RectTransform>();
			// Inner fill ~44% of ring diameter — matches reference radio dial.
			toggleCheckmarkRect.anchorMin = new Vector2(0.28f, 0.28f);
			toggleCheckmarkRect.anchorMax = new Vector2(0.72f, 0.72f);
			toggleCheckmarkRect.offsetMin = Vector2.zero;
			toggleCheckmarkRect.offsetMax = Vector2.zero;
			var toggleCheckmark = toggleCheckmarkObj.AddComponent<Image>();
			toggleCheckmark.sprite = UiRuntimeSprites.CircleFilled;
			toggleCheckmark.type = Image.Type.Simple;
			toggleCheckmark.preserveAspect = true;
			toggleCheckmark.raycastTarget = false;
			var rowToggle = toggleObj.AddComponent<Toggle>();
			rowToggle.targetGraphic = hitPad;
			// Do NOT assign graphic — Unity Toggle would SetActive(false) on Checkmark when off and fight our alpha dial.
			rowToggle.graphic = null;
			rowToggle.transition = Selectable.Transition.None;
			rowToggle.toggleTransition = Toggle.ToggleTransition.None;
			bool draftOn = GetDraftEnabled(addonId, addonInfo.isEnabled);
			rowToggle.SetIsOnWithoutNotify(draftOn);
			ApplyStatusDialVisual(rowToggle, draftOn);

			var nameObj = new GameObject("Name");
			nameObj.transform.SetParent(itemObj.transform, false);
			var nameLE = nameObj.AddComponent<LayoutElement>();
			nameLE.minWidth = 180f;
			nameLE.flexibleWidth = 1f;
			nameLE.preferredHeight = 28f;
			var nameText = nameObj.AddComponent<TextMeshProUGUI>();
			nameText.text = !string.IsNullOrEmpty(addonInfo.displayName) ? addonInfo.displayName : addonId;
			nameText.fontSize = 16f;
			nameText.fontStyle = FontStyles.Normal;
			nameText.color = new Color(0.88f, 0.88f, 0.9f, 1f);
			nameText.alignment = TextAlignmentOptions.MidlineLeft;
			nameText.enableWordWrapping = false;
			nameText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
			nameText.raycastTarget = false;

			var removeBtnObj = new GameObject("RemoveButton");
			removeBtnObj.transform.SetParent(itemObj.transform, false);
			var removeBtnLE = removeBtnObj.AddComponent<LayoutElement>();
			removeBtnLE.preferredWidth = 76f;
			removeBtnLE.minWidth = 76f;
			removeBtnLE.flexibleWidth = 0f;
			removeBtnLE.preferredHeight = 28f;
			removeBtnLE.minHeight = 28f;
			var removeBtnImage = removeBtnObj.AddComponent<Image>();
			SpzUiThemeOps.ApplyRoundedControlSprite(removeBtnImage, markEligible: true);
			removeBtnImage.color = new Color(45f / 255f, 26f / 255f, 26f / 255f, 0.85f);
			removeBtnImage.raycastTarget = true;
			var removeBtn = removeBtnObj.AddComponent<Button>();
			removeBtn.targetGraphic = removeBtnImage;
			removeBtn.transition = Selectable.Transition.ColorTint;
			var removeBtnText = new GameObject("Text");
			removeBtnText.transform.SetParent(removeBtnObj.transform, false);
			var removeBtnTextRect = removeBtnText.AddComponent<RectTransform>();
			removeBtnTextRect.anchorMin = Vector2.zero;
			removeBtnTextRect.anchorMax = Vector2.one;
			removeBtnTextRect.sizeDelta = Vector2.zero;
			var removeBtnTextComp = removeBtnText.AddComponent<TextMeshProUGUI>();
			removeBtnTextComp.text = "Uninstall";
			removeBtnTextComp.fontSize = 11f;
			removeBtnTextComp.alignment = TextAlignmentOptions.Center;
			removeBtnTextComp.color = new Color(0.96f, 0.44f, 0.44f, 0.9f);
			removeBtnTextComp.raycastTarget = false;
			removeBtn.onClick.AddListener(() => OnRemoveAddon(addonId));

			rowToggle.onValueChanged.AddListener((isOn) => {
				if (Addon_MGR.instance == null)
					return;
				string id = addonId;
				var map = Addon_MGR.instance.GetAddons();
				if (map.TryGetValue(id, out var info) && info != null && info.isEnabled == isOn) {
					SetDraftEnabled(id, isOn);
					ApplyStatusDialVisual(rowToggle, isOn);
					// Connectivity repair: dial already matches live flag, but ribbon tab may be missing/orphan.
					Addon_MGR.instance.SyncRibbonTabWithEnabledState(id);
					return;
				}
				// Apply immediately so the command-ribbon tab appears/disappears with the dial.
				_suppressEnabledListRefresh = true;
				try {
					SetDraftEnabled(id, isOn);
					if (isOn)
						Addon_MGR.instance.EnableAddon(id);
					else
						Addon_MGR.instance.DisableAddon(id);
					ApplyStatusDialVisual(rowToggle, isOn);
					RefreshStatusCountsOnly();
					bool ribbonOnly = string.Equals(id, Addon_MGR.RibbonOnlyFullscreenAddonId, StringComparison.Ordinal);
					ShowStatus(isOn
						? (ribbonOnly
							? $"Enabled '{id}' — viewport dock on. Click Save settings to keep next launch."
							: $"Enabled '{id}' — ribbon tab on. Click Save settings to keep next launch.")
						: (ribbonOnly
							? $"Disabled '{id}' — viewport dock off. Click Save settings to keep next launch."
							: $"Disabled '{id}' — ribbon tab off. Click Save settings to keep next launch."), true);
				} finally {
					_suppressEnabledListRefresh = false;
				}
				bool stillVisible = _filterState == 0
					|| (_filterState == 1 && isOn)
					|| (_filterState == 2 && !isOn);
				if (!stillVisible)
					ScheduleRefreshAddonsList();
			});
			
			itemObj.SetActive(true);
			_addonUIItems[addonId] = itemObj;
		}
		
		/// <summary>
		/// Handles removal of an add-on
		/// </summary>
		void OnRemoveAddon(string addonId) {
			// Show confirmation dialog
			if (ConfirmPopup_UI.instance != null) {
				ConfirmPopup_UI.instance.Show(
					$"Remove add-on '{addonId}'?\n\nThis cannot be undone.",
					() => {
						if (AddonInstaller_MGR.instance != null) {
							AddonInstaller_MGR.instance.RemoveAddon(addonId, (success, message) => {
								if (success) {
									ShowStatus(message, true);
									RefreshAddonsList();
								} else {
									ShowStatus(message, false);
								}
							});
						}
					},
					null
				);
			} else {
				// Fallback if no confirmation popup
				if (AddonInstaller_MGR.instance != null) {
					AddonInstaller_MGR.instance.RemoveAddon(addonId, (success, message) => {
						ShowStatus(message, success);
						if (success) {
							RefreshAddonsList();
						}
					});
				}
			}
		}
		
		/// <summary>
		/// Shows status message
		/// </summary>
		void ShowStatus(string message, bool isSuccess) {
			_lastStatusIsSuccess = isSuccess;
			if (_statusText != null) {
				_statusText.text = message;
				_statusText.color = isSuccess ? _statusOk : _statusFail;
			}
			UnityEngine.Debug.Log($"[AddonManager_UI] {message}");
		}
		
		/// <summary>
		/// Cleanup when object is destroyed
		/// </summary>
		void OnDestroy() {
			if (instance != this) return;
			if (_deferredListRefresh != null) {
				StopCoroutine(_deferredListRefresh);
				_deferredListRefresh = null;
			}
			SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
			SceneManager.sceneLoaded -= OnSceneLoadedMaybeOpenPending;

			if (_hidViewportStatusForModal && Viewport_StatusText.instance != null) {
				Viewport_StatusText.instance.PreferVIsible(this);
				_hidViewportStatusForModal = false;
			}
			
			// Unsubscribe from StaticEvents to prevent memory leaks
			StaticEvents.Unsubscribe("AddonManager:OpenPanel", OpenPanel);
			
			// Clear instance reference
			instance = null;
		}
	}

	/// <summary>
	/// Closes the add-on manager only when the dimmer itself is the raycast hit.
	/// A parent <see cref="Button"/> would also fire for in-panel clicks because
	/// <c>ExecuteEvents.ExecuteHierarchy</c> bubbles up to the first handler.
	/// </summary>
	public sealed class AddonManagerDimmerClose : MonoBehaviour, IPointerClickHandler {
		Action _onDimmerClick;

		public void Bind(Action onDimmerClick) {
			_onDimmerClick = onDimmerClick;
		}

		public void OnPointerClick(PointerEventData eventData) {
			if (_onDimmerClick == null || eventData == null)
				return;
			var hit = eventData.pointerCurrentRaycast.gameObject;
			if (hit != gameObject)
				return;
			_onDimmerClick.Invoke();
		}
	}
}
