using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

		static readonly Color RefBgModalDim = new Color(0f, 0f, 0f, 0.78f);
		static readonly Color RefGreen = new Color(34f / 255f, 197f / 255f, 94f / 255f, 1f);
		static readonly Color RefRedText = new Color(239f / 255f, 68f / 255f, 68f / 255f, 1f);
		
		[SerializeField] GameObject _panel;
		[SerializeField] Button _openPanel_button;
		[SerializeField] Button _closePanel_button;
		[SerializeField] Button _installFromFile_button;
		[SerializeField] Button _refresh_button;
		[SerializeField] RectTransform _addonsListParent; // Where to place add-on list items (runtime panel sets this when null)
		[SerializeField] GameObject _addonItemPrefab; // optional; otherwise rows are built like main-branch
		[SerializeField] TextMeshProUGUI _statusText;
		
		private Dictionary<string, GameObject> _addonUIItems = new Dictionary<string, GameObject>();
		bool _hidViewportStatusForModal;
		
		// Filter state: 0 = All, 1 = Enabled, 2 = Disabled
		private int _filterState = 0;
		private Toggle _filterAllToggle;
		private Toggle _filterEnabledToggle;
		private Toggle _filterDisabledToggle;
		private GameObject _blocker; // full-screen click blocker, shown/hidden with panel
		Image _blockerDimImage; // dimmer on blocker root
		CanvasGroup _panelModalGroup;
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			// Subscribe here so StaticEvents.Invoke works as soon as the singleton exists (before Start runs).
			StaticEvents.SubscribeOrReplace("AddonManager:OpenPanel", OpenPanel);
			SceneManager.sceneLoaded += OnSceneLoadedMaybeOpenPending;
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
		return _panel.transform.Find("FilterBar") != null;
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
		if (AddonManagerPanelSetupIsComplete())
			return;
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
		blockerObj.SetActive(false);
		_blockerDimImage = blockerImage;
		
		// Panel as child of blocker — main-branch template (centered stretch region, simple controls).
		GameObject panelObj = new GameObject("AddonManager_Panel");
		panelObj.layer = UILayer;
		panelObj.transform.SetParent(blockerObj.transform, false);
		_panel = panelObj;
		
		var rectTransform = panelObj.AddComponent<RectTransform>();
		rectTransform.anchorMin = new Vector2(0.2f, 0.2f);
		rectTransform.anchorMax = new Vector2(0.8f, 0.8f);
		rectTransform.sizeDelta = Vector2.zero;
		rectTransform.anchoredPosition = Vector2.zero;
		
		var image = panelObj.AddComponent<UnityEngine.UI.Image>();
		image.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
		image.raycastTarget = true;
		
		var canvasGroup = panelObj.AddComponent<CanvasGroup>();
		canvasGroup.blocksRaycasts = true;
		canvasGroup.interactable = true;
		canvasGroup.alpha = 1f;
		_panelModalGroup = canvasGroup;
		
		const float Grid = 8f;
		const float PanelPadding = Grid * 3;
		const float SectionSpacing = Grid * 2;
		const float RowSpacing = Grid;
		
		var verticalLayout = panelObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		verticalLayout.spacing = SectionSpacing;
		verticalLayout.padding = new RectOffset((int)PanelPadding, (int)PanelPadding, (int)PanelPadding, (int)PanelPadding);
		verticalLayout.childControlHeight = false;
		verticalLayout.childControlWidth = true;
		verticalLayout.childForceExpandHeight = false;
		verticalLayout.childForceExpandWidth = true;
		
		GameObject headerObj = new GameObject("Header");
		headerObj.transform.SetParent(panelObj.transform, false);
		var headerLayoutElement = headerObj.AddComponent<LayoutElement>();
		headerLayoutElement.preferredHeight = 48f;
		headerLayoutElement.minHeight = 40f;
		var headerLayout = headerObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		headerLayout.childControlWidth = true;
		headerLayout.childControlHeight = true;
		headerLayout.childForceExpandWidth = false;
		headerLayout.childForceExpandHeight = true;
		headerLayout.spacing = Grid * 2;
		headerLayout.padding = new RectOffset(0, 0, 0, 0);
		
		GameObject titleObj = new GameObject("Title");
		titleObj.transform.SetParent(headerObj.transform, false);
		var titleLE = titleObj.AddComponent<LayoutElement>();
		titleLE.minWidth = 180f;
		titleLE.flexibleWidth = 1f;
		var titleText = titleObj.AddComponent<TextMeshProUGUI>();
		titleText.text = "Add-on Manager";
		titleText.fontSize = 22;
		titleText.color = Color.white;
		titleText.fontStyle = FontStyles.Bold;
		titleText.alignment = TextAlignmentOptions.Left;
		titleText.enableWordWrapping = false;
		titleText.overflowMode = TMPro.TextOverflowModes.Overflow;
		titleText.raycastTarget = false;
		
		GameObject closeBtnObj = new GameObject("CloseButton");
		closeBtnObj.transform.SetParent(headerObj.transform, false);
		var closeBtnLE = closeBtnObj.AddComponent<LayoutElement>();
		closeBtnLE.preferredWidth = 88f;
		closeBtnLE.minWidth = 72f;
		closeBtnLE.flexibleWidth = 0f;
		closeBtnLE.preferredHeight = 32f;
		var closeBtnImage = closeBtnObj.AddComponent<UnityEngine.UI.Image>();
		closeBtnImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
		closeBtnImage.raycastTarget = true;
		var closeBtn = closeBtnObj.AddComponent<UnityEngine.UI.Button>();
		closeBtn.targetGraphic = closeBtnImage;
		closeBtn.onClick.AddListener(ClosePanel);
		var closeBtnTextObj = new GameObject("Text");
		closeBtnTextObj.transform.SetParent(closeBtnObj.transform, false);
		var closeBtnTextRect = closeBtnTextObj.AddComponent<RectTransform>();
		closeBtnTextRect.anchorMin = Vector2.zero;
		closeBtnTextRect.anchorMax = Vector2.one;
		closeBtnTextRect.sizeDelta = Vector2.zero;
		var closeBtnText = closeBtnTextObj.AddComponent<TextMeshProUGUI>();
		closeBtnText.text = "Close";
		closeBtnText.fontSize = 14;
		closeBtnText.alignment = TextAlignmentOptions.Center;
		closeBtnText.color = new Color(0.11f, 0.11f, 0.11f, 1f);
		closeBtnText.raycastTarget = false;
		_closePanel_button = closeBtn;
		
		GameObject buttonBarObj = new GameObject("ButtonBar");
		buttonBarObj.transform.SetParent(panelObj.transform, false);
		var buttonBarLE = buttonBarObj.AddComponent<LayoutElement>();
		buttonBarLE.preferredHeight = 40f;
		buttonBarLE.minHeight = 36f;
		var buttonBarLayout = buttonBarObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		buttonBarLayout.spacing = Grid * 2;
		buttonBarLayout.childControlWidth = false;
		buttonBarLayout.childControlHeight = true;
		buttonBarLayout.childForceExpandWidth = false;
		buttonBarLayout.childForceExpandHeight = true;
		buttonBarLayout.padding = new RectOffset(0, 0, 0, 0);
		
		void AddBarButton(Transform parent, string goName, string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick, Vector2 size, out Button outBtn) {
			var go = new GameObject(goName);
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>().sizeDelta = size;
			var img = go.AddComponent<UnityEngine.UI.Image>();
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
			tr.sizeDelta = Vector2.zero;
			var tx = to.AddComponent<TextMeshProUGUI>();
			tx.text = label;
			tx.fontSize = label.Length > 14 ? 13 : 14;
			tx.alignment = TextAlignmentOptions.Center;
			tx.color = fg;
			tx.raycastTarget = false;
			outBtn = btn;
		}
		
		AddBarButton(buttonBarObj.transform, "InstallButton", "Install from File", new Color(0.3f, 0.3f, 0.3f, 1f),
			new Color(0.11f, 0.11f, 0.11f, 1f), OnInstallFromFile, new Vector2(150, 30), out var installBtn);
		_installFromFile_button = installBtn;
		AddBarButton(buttonBarObj.transform, "RefreshButton", "Refresh", new Color(0.3f, 0.3f, 0.3f, 1f),
			new Color(0.11f, 0.11f, 0.11f, 1f), RefreshAddonsList, new Vector2(100, 30), out var refreshBtn);
		_refresh_button = refreshBtn;
		AddBarButton(buttonBarObj.transform, "LoadAddonsNowButton", "Load addons now", new Color(0.25f, 0.45f, 0.25f, 1f),
			new Color(0.95f, 0.95f, 0.95f, 1f), OnLoadAddonsNow, new Vector2(130, 30), out _);
		AddBarButton(buttonBarObj.transform, "RunWithAddonsButton", "Restart with addons", new Color(0.2f, 0.5f, 0.6f, 1f),
			new Color(0.95f, 0.95f, 0.95f, 1f), OnRestartWithAddons, new Vector2(150, 30), out _);
		
		GameObject filterBarObj = new GameObject("FilterBar");
		filterBarObj.transform.SetParent(panelObj.transform, false);
		var filterBarLE = filterBarObj.AddComponent<LayoutElement>();
		filterBarLE.preferredHeight = 36f;
		filterBarLE.minHeight = 32f;
		var filterBarLayout = filterBarObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		filterBarLayout.spacing = Grid * 2;
		filterBarLayout.childControlWidth = false;
		filterBarLayout.childControlHeight = true;
		filterBarLayout.padding = new RectOffset(0, 0, 0, 0);
		
		var filterLabelObj = new GameObject("FilterLabel");
		filterLabelObj.transform.SetParent(filterBarObj.transform, false);
		var filterLabelLE = filterLabelObj.AddComponent<LayoutElement>();
		filterLabelLE.preferredWidth = 48f;
		filterLabelLE.minWidth = 40f;
		var filterLabelText = filterLabelObj.AddComponent<TextMeshProUGUI>();
		filterLabelText.text = "Filter:";
		filterLabelText.fontSize = 14;
		filterLabelText.color = Color.white;
		filterLabelText.alignment = TextAlignmentOptions.Left;
		filterLabelText.raycastTarget = false;
		
		var toggleGroup = filterBarObj.AddComponent<ToggleGroup>();
		toggleGroup.allowSwitchOff = false;
		_filterAllToggle = CreateFilterToggle("All", filterBarObj.transform, toggleGroup, 0).GetComponent<Toggle>();
		_filterEnabledToggle = CreateFilterToggle("Enabled", filterBarObj.transform, toggleGroup, 1).GetComponent<Toggle>();
		_filterDisabledToggle = CreateFilterToggle("Disabled", filterBarObj.transform, toggleGroup, 2).GetComponent<Toggle>();
		
		const float scrollAreaHeight = 280f;
		GameObject scrollViewObj = new GameObject("ScrollView");
		scrollViewObj.layer = UILayer;
		scrollViewObj.transform.SetParent(panelObj.transform, false);
		var scrollViewRect = scrollViewObj.AddComponent<RectTransform>();
		scrollViewRect.anchorMin = new Vector2(0, 0);
		scrollViewRect.anchorMax = new Vector2(1, 1);
		scrollViewRect.sizeDelta = Vector2.zero;
		scrollViewRect.pivot = new Vector2(0.5f, 0.5f);
		var layoutElementScroll = scrollViewObj.AddComponent<UnityEngine.UI.LayoutElement>();
		layoutElementScroll.preferredHeight = scrollAreaHeight;
		layoutElementScroll.minHeight = scrollAreaHeight;
		var scrollViewImage = scrollViewObj.AddComponent<UnityEngine.UI.Image>();
		scrollViewImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
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
		contentLayout.padding = new RectOffset((int)Grid, (int)Grid, (int)Grid, (int)Grid);
		contentLayout.childControlHeight = false;
		contentLayout.childControlWidth = true;
		contentLayout.childForceExpandHeight = false;
		contentLayout.childForceExpandWidth = true;
		var contentSizeFitter = contentObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
		contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
		contentSizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
		scrollView.content = contentRect;
		_addonsListParent = contentRect;
		
		_filterAllToggle.isOn = true;
		
		GameObject statusObj = new GameObject("StatusText");
		statusObj.transform.SetParent(panelObj.transform, false);
		var statusLE = statusObj.AddComponent<LayoutElement>();
		statusLE.preferredHeight = 32f;
		statusLE.minHeight = 28f;
		var statusLayout = statusObj.AddComponent<HorizontalLayoutGroup>();
		statusLayout.padding = new RectOffset((int)Grid, 0, (int)Grid, 0);
		statusLayout.childControlWidth = true;
		statusLayout.childControlHeight = true;
		statusLayout.childForceExpandWidth = true;
		statusLayout.childForceExpandHeight = true;
		GameObject statusTextObj = new GameObject("Text");
		statusTextObj.transform.SetParent(statusObj.transform, false);
		var statusTextRect = statusTextObj.AddComponent<RectTransform>();
		statusTextRect.anchorMin = Vector2.zero;
		statusTextRect.anchorMax = Vector2.one;
		statusTextRect.sizeDelta = Vector2.zero;
		var statusText = statusTextObj.AddComponent<TextMeshProUGUI>();
		statusText.text = "Ready";
		statusText.fontSize = 13;
		statusText.color = new Color(0.4f, 1f, 0.45f);
		statusText.alignment = TextAlignmentOptions.Left;
		statusText.raycastTarget = false;
		_statusText = statusText;
		
		SetLayerRecursively(_panel.transform, UILayer);
		_panel.SetActive(false);
		_blocker = blockerObj;
		Debug.Log("[AddonManager_UI] Panel creation completed, set inactive initially");
	}
	
	static void SetLayerRecursively(Transform t, int layer) {
		t.gameObject.layer = layer;
		for (int i = 0; i < t.childCount; i++)
			SetLayerRecursively(t.GetChild(i), layer);
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
			_statusText = null;
			_filterAllToggle = null;
			_filterEnabledToggle = null;
			_filterDisabledToggle = null;
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
				const float scrollAreaHeight = 280f;
				Canvas.ForceUpdateCanvases();
				var panelRect = _panel.GetComponent<RectTransform>();
				if (panelRect != null) {
					LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
					Canvas.ForceUpdateCanvases();
					var scrollViewTr = _panel.transform.Find("ScrollView");
					if (scrollViewTr != null) {
						var svr = scrollViewTr.GetComponent<RectTransform>();
						if (svr != null) {
							LayoutRebuilder.ForceRebuildLayoutImmediate(svr);
							svr.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, scrollAreaHeight);
						}
					}
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
		/// Requests Python to load all enabled addons so their panels appear (e.g. in the ctrl tab). No save needed — enable then click this.
		/// </summary>
		void OnLoadAddonsNow() {
			if (_statusText != null) _statusText.text = "Loading addons...";
			if (Addon_MGR.instance != null) {
				Addon_MGR.instance.RequestLoadAllEnabledAddonsNow(() => {
					if (_statusText != null) _statusText.text = "Ready";
					RefreshAddonsList();
				});
			} else {
				if (_statusText != null) _statusText.text = "Ready";
			}
		}

		/// <summary>
		/// Launches Run_with_Addons.bat (same way Run_noQuickEdit runs for WebUI) then quits so the bat starts the game with Python on PATH.
		/// </summary>
		void OnRestartWithAddons() {
			Launch_Addons_Bat_File.RestartWithAddons();
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
			toggleObj.AddComponent<RectTransform>().sizeDelta = new Vector2(80, 25);
			var toggleBg = toggleObj.AddComponent<UnityEngine.UI.Image>();
			toggleBg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
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
			labelText.fontSize = 12;
			labelText.color = Color.white;
			labelText.alignment = TextAlignmentOptions.Center;
			labelText.raycastTarget = false;
			toggle.onValueChanged.AddListener((isOn) => {
				labelText.color = isOn ? new Color(0.4f, 1f, 0.4f) : Color.white;
				toggleBg.color = isOn ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
				if (isOn) {
					_filterState = filterValue;
					RefreshAddonsList();
				}
			});
			labelText.color = toggle.isOn ? new Color(0.4f, 1f, 0.4f) : Color.white;
			toggleBg.color = toggle.isOn ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
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
			
			var addons = Addon_MGR.instance.GetAddons();
			var filteredAddons = new List<KeyValuePair<string, Addon_MGR.AddonInfo>>();
			int enabledCount = 0;
			int disabledCount = 0;
			
			foreach (var kvp in addons) {
				if (kvp.Value.isEnabled) enabledCount++;
				else disabledCount++;
				bool shouldShow = _filterState == 0
					|| (_filterState == 1 && kvp.Value.isEnabled)
					|| (_filterState == 2 && !kvp.Value.isEnabled);
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
		}

		void OnAddonEnabledStateChanged(string addonId) {
			RefreshAddonsList();
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
			
			GameObject itemObj;
			if (_addonItemPrefab != null) {
				itemObj = Instantiate(_addonItemPrefab, _addonsListParent);
			} else {
				itemObj = new GameObject($"AddonItem_{addonId}");
				itemObj.transform.SetParent(_addonsListParent, false);
				itemObj.layer = _addonsListParent.gameObject.layer;
				var rectTransform = itemObj.AddComponent<RectTransform>();
				rectTransform.sizeDelta = new Vector2(0, 40);
				var itemLayout = itemObj.AddComponent<LayoutElement>();
				itemLayout.preferredHeight = 40;
				itemLayout.minHeight = 40;
				itemLayout.minWidth = 440f;
				var horizontalLayout = itemObj.AddComponent<HorizontalLayoutGroup>();
				horizontalLayout.spacing = 12f;
				horizontalLayout.padding = new RectOffset(8, 6, 8, 6);
				horizontalLayout.childControlWidth = true;
				horizontalLayout.childControlHeight = true;
				horizontalLayout.childForceExpandWidth = false;
				horizontalLayout.childForceExpandHeight = true;
				
				const float colNameWidth = 220f;
				var nameObj = new GameObject("Name");
				nameObj.transform.SetParent(itemObj.transform, false);
				var nameLE = nameObj.AddComponent<LayoutElement>();
				nameLE.preferredWidth = colNameWidth;
				nameLE.minWidth = colNameWidth;
				var nameText = nameObj.AddComponent<TextMeshProUGUI>();
				string statusIcon = addonInfo.isEnabled ? "\u2713" : "\u25CB";
				nameText.text = $"{statusIcon} {addonId}";
				nameText.fontSize = 14;
				nameText.color = addonInfo.isEnabled ? new Color(0.4f, 1f, 0.4f) : new Color(0.95f, 0.95f, 0.95f);
				nameText.alignment = TextAlignmentOptions.Left;
				nameText.enableWordWrapping = false;
				nameText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
				nameText.raycastTarget = false;
				
				const float colToggleWidth = 120f;
				var toggleContainerObj = new GameObject("ToggleContainer");
				toggleContainerObj.transform.SetParent(itemObj.transform, false);
				var toggleContainerLE = toggleContainerObj.AddComponent<LayoutElement>();
				toggleContainerLE.preferredWidth = colToggleWidth;
				toggleContainerLE.minWidth = colToggleWidth;
				var toggleContainerLayout = toggleContainerObj.AddComponent<HorizontalLayoutGroup>();
				toggleContainerLayout.spacing = 5;
				toggleContainerLayout.childControlWidth = false;
				toggleContainerLayout.childControlHeight = true;
				
				var toggleLabelObj = new GameObject("ToggleLabel");
				toggleLabelObj.transform.SetParent(toggleContainerObj.transform, false);
				var toggleLabelLE = toggleLabelObj.AddComponent<LayoutElement>();
				toggleLabelLE.preferredWidth = 56f;
				toggleLabelLE.minWidth = 56f;
				var toggleLabelText = toggleLabelObj.AddComponent<TextMeshProUGUI>();
				toggleLabelText.text = addonInfo.isEnabled ? "Enabled" : "Disabled";
				toggleLabelText.fontSize = 12;
				toggleLabelText.color = addonInfo.isEnabled ? new Color(0.4f, 1f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
				toggleLabelText.alignment = TextAlignmentOptions.Left;
				toggleLabelText.enableWordWrapping = false;
				toggleLabelText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
				toggleLabelText.raycastTarget = false;
				
				var toggleObj = new GameObject("Toggle");
				toggleObj.transform.SetParent(toggleContainerObj.transform, false);
				toggleObj.AddComponent<RectTransform>().sizeDelta = new Vector2(50, 20);
				var toggleBg = toggleObj.AddComponent<UnityEngine.UI.Image>();
				toggleBg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				toggleBg.raycastTarget = true;
				var toggleCheckmarkObj = new GameObject("Checkmark");
				toggleCheckmarkObj.transform.SetParent(toggleObj.transform, false);
				var toggleCheckmarkRect = toggleCheckmarkObj.AddComponent<RectTransform>();
				toggleCheckmarkRect.anchorMin = Vector2.zero;
				toggleCheckmarkRect.anchorMax = Vector2.one;
				toggleCheckmarkRect.sizeDelta = Vector2.zero;
				var toggleCheckmark = toggleCheckmarkObj.AddComponent<UnityEngine.UI.Image>();
				toggleCheckmark.color = new Color(0.2f, 0.8f, 0.2f, 1f);
				var rowToggle = toggleObj.AddComponent<Toggle>();
				rowToggle.targetGraphic = toggleBg;
				rowToggle.graphic = toggleCheckmark;
				rowToggle.isOn = addonInfo.isEnabled;
				rowToggle.onValueChanged.AddListener((_) => {
					if (Addon_MGR.instance == null)
						return;
					string id = addonId;
					bool desired = rowToggle.isOn;
					var map = Addon_MGR.instance.GetAddons();
					if (map.TryGetValue(id, out var info) && info.isEnabled == desired)
						return;
					if (desired)
						Addon_MGR.instance.EnableAddon(id);
					else
						Addon_MGR.instance.DisableAddon(id);
					RefreshAddonsList();
				});
				
				const float colButtonWidth = 90f;
				var removeBtnObj = new GameObject("RemoveButton");
				removeBtnObj.transform.SetParent(itemObj.transform, false);
				var removeBtnLE = removeBtnObj.AddComponent<LayoutElement>();
				removeBtnLE.preferredWidth = colButtonWidth;
				removeBtnLE.minWidth = colButtonWidth;
				removeBtnLE.preferredHeight = 30;
				var removeBtnImage = removeBtnObj.AddComponent<UnityEngine.UI.Image>();
				removeBtnImage.color = new Color(0.5f, 0.2f, 0.2f, 1f);
				removeBtnImage.raycastTarget = true;
				var removeBtn = removeBtnObj.AddComponent<Button>();
				removeBtn.targetGraphic = removeBtnImage;
				var removeBtnText = new GameObject("Text");
				removeBtnText.transform.SetParent(removeBtnObj.transform, false);
				var removeBtnTextRect = removeBtnText.AddComponent<RectTransform>();
				removeBtnTextRect.anchorMin = Vector2.zero;
				removeBtnTextRect.anchorMax = Vector2.one;
				removeBtnTextRect.sizeDelta = Vector2.zero;
				var removeBtnTextComp = removeBtnText.AddComponent<TextMeshProUGUI>();
				removeBtnTextComp.text = "Uninstall";
				removeBtnTextComp.fontSize = 12;
				removeBtnTextComp.alignment = TextAlignmentOptions.Center;
				removeBtnTextComp.color = Color.white;
				removeBtnTextComp.raycastTarget = false;
				removeBtn.onClick.AddListener(() => OnRemoveAddon(addonId));
			}
			
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
			if (_statusText != null) {
				_statusText.text = message;
				_statusText.color = isSuccess ? RefGreen : RefRedText;
			}
			UnityEngine.Debug.Log($"[AddonManager_UI] {message}");
		}
		
		/// <summary>
		/// Cleanup when object is destroyed
		/// </summary>
		void OnDestroy() {
			if (instance != this) return;
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
}
