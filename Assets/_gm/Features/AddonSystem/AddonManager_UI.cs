using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
		TextAnchor _authoredHeaderChildAlignment = TextAnchor.MiddleCenter;
		bool _authoredHeaderChildControlHeight = true;
		bool _authoredHeaderChildForceExpandHeight = false;
		bool _headerChildAlignSnapshotted;
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
		/// <summary>Show-in-Ribbon prefs at last clean open/save — Close without Save reverts to these.</summary>
		readonly Dictionary<string, bool> _showInRibbonSnapshotById = new Dictionary<string, bool>();
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
			AttachTooltip(_openPanel_button.gameObject, "Open the Add-on Manager.");
		}
		// Header Install/Refresh/… may only exist after CreatePanelIfNeeded — bind after setup.
		CreatePanelIfNeeded();
		EnsureHeaderActionButtonsWired();
		EnsureChromeTooltips();
		// Synchronous finish: no yield — avoids racing other coroutines that call OpenPanel the same frame.
		FinishStartBootstrap();
		ApplyThemeTokens();
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
	const string PanelShellVersionMarker = "StichAddonManager_v11";

	bool AddonManagerPanelSetupIsComplete() {
		if (_panel == null || _addonsListParent == null) return false;
		if (!_addonsListParent.transform.IsChildOf(_panel.transform)) return false;
		// v11: RectMask2D list (no Mask white bar). Older v10 shells must rebuild.
		return _panel.transform.Find(PanelShellVersionMarker) != null
			&& _panel.transform.Find("FilterBar/FilterPills") != null
			&& ListScrollViewportIsHealthy();
	}

	static Transform FindListViewport(Transform panel) {
		if (panel == null) return null;
		Transform viewport = panel.Find("ScrollView/Viewport");
		if (viewport == null)
			viewport = panel.Find("ListArea/ScrollView/Viewport");
		return viewport;
	}

	bool ListScrollViewportIsHealthy() {
		Transform viewport = FindListViewport(_panel != null ? _panel.transform : null);
		if (viewport == null) return false;
		if (viewport.GetComponent<Mask>() != null) return false;
		if (viewport.GetComponent<RectMask2D>() == null) return false;
		var img = viewport.GetComponent<Image>();
		// Enabled opaque/near-opaque Viewport Image is the white vertical bar bug.
		if (img != null && img.enabled && img.color.a > 0.02f) return false;
		return true;
	}

	/// <summary>
	/// Connectivity: recovered shells keep <see cref="_panel"/> but drop button refs — rebind Install/Refresh/etc.
	/// </summary>
	void EnsureHeaderActionButtonsWired() {
		if (_panel == null) return;
		Transform header = _panel.transform.Find("Header");
		if (header == null) return;

		void Bind(string childName, ref Button field, UnityEngine.Events.UnityAction handler, string tip) {
			Transform t = header.Find(childName);
			if (t == null) return;
			var btn = t.GetComponent<Button>();
			if (btn == null) return;
			field = btn;
			btn.onClick.RemoveListener(handler);
			btn.onClick.AddListener(handler);
			btn.interactable = true;
			if (btn.targetGraphic != null)
				btn.targetGraphic.raycastTarget = true;
			AttachTooltip(btn.gameObject, tip);
		}

		Bind("InstallButton", ref _installFromFile_button, OnInstallFromFile,
			"Install an add-on from a .zip file into StreamingAssets/Addons.");
		Bind("RefreshButton", ref _refresh_button, RescanAndRefreshAddonsList,
			"Rescan the Addons folder and refresh this list (keeps enable state for add-ons still present).");
		Bind("LoadAddonsNowButton", ref _loadAddonsNow_button, OnLoadAddonsNow,
			"Ask Python to load every currently enabled add-on now (register / create panels).");
		Bind("SaveAddonSettingsButton", ref _saveAddonSettings_button, OnSaveAddonSettings,
			"Persist enabled add-ons and Preferences (e.g. Show in Command Ribbon) for the next launch.");
		Bind("RunWithAddonsButton", ref _restartWithAddons_button, OnRestartWithAddons,
			"Quit and relaunch StableProjectorz with the add-on Python server (Run_with_Addons).");
		Bind("CloseButton", ref _closePanel_button, ClosePanel,
			"Close the Add-on Manager.");
	}

		/// <summary>
		/// Legacy shells used Mask+Image on Viewport — BoundChrome/SolidSquare turns that into a white vertical bar.
		/// Disable any Viewport Image (RectMask2D does not need it); put a clear raycast plate on ScrollView instead.
		/// </summary>
		void EnsureListScrollViewportHealthy() {
			if (_panel == null) return;
			Transform viewport = FindListViewport(_panel.transform);
			if (viewport == null) return;

			var legacyMask = viewport.GetComponent<Mask>();
			if (legacyMask != null)
				Destroy(legacyMask);

			if (viewport.GetComponent<RectMask2D>() == null)
				viewport.gameObject.AddComponent<RectMask2D>();

			var img = viewport.GetComponent<Image>();
			if (img != null) {
				img.sprite = UiRuntimeSprites.SolidRect;
				img.type = Image.Type.Simple;
				img.color = Color.clear;
				img.raycastTarget = false;
				// Critical: even alpha~0 SolidRect can draw a white plate under some UI materials.
				img.enabled = false;
			}

			Transform scrollT = viewport.parent;
			if (scrollT != null) {
				var scrollHit = scrollT.GetComponent<Image>();
				if (scrollHit == null)
					scrollHit = scrollT.gameObject.AddComponent<Image>();
				scrollHit.sprite = UiRuntimeSprites.SolidRect;
				scrollHit.type = Image.Type.Simple;
				scrollHit.color = Color.clear;
				scrollHit.raycastTarget = true;
				scrollHit.enabled = true;
			}

			// Scrub any other opaque white plates left under the scroll hierarchy (legacy scrollbar faces, etc.).
			if (scrollT != null) {
				foreach (var childImg in scrollT.GetComponentsInChildren<Image>(true)) {
					if (childImg == null) continue;
					if (childImg.transform == scrollT) continue;
					if (childImg.transform.IsChildOf(viewport) && childImg.transform != viewport)
						continue; // list item chrome
					string n = childImg.gameObject.name ?? "";
					if (n == "Viewport" || n.IndexOf("Scrollbar", StringComparison.OrdinalIgnoreCase) >= 0) {
						childImg.color = Color.clear;
						childImg.enabled = n != "Viewport" ? childImg.enabled : false;
						if (n.IndexOf("Scrollbar", StringComparison.OrdinalIgnoreCase) >= 0)
							childImg.gameObject.SetActive(false);
					}
				}
			}
		}

		void OnRememberEnabledAddonsToggleChanged(bool remember) {
			Addon_MGR.SetRememberEnabledAddonsPreference(remember);
			ThemeRememberActionButton(_rememberEnabledAddonToggle, remember);
			ShowStatus(
				remember
					? "Remember on — current enabled set saved for next launch."
					: "Remember off — next launch starts with add-ons disabled (prefs like Show in Ribbon still use Save).",
				true);
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
					AttachTooltip(_saveAddonSettings_button.gameObject,
						"Persist enabled add-ons and Preferences (e.g. Show in Command Ribbon) for the next launch.");
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
				// Old chrome: tiny unmarked corner checkbox + separate label — rebuild as labeled button.
				if (found.Find("ToggleWrap/Label") == null) {
					UnityEngine.Object.Destroy(found.gameObject);
					found = null;
					_rememberEnabledAddonToggle = null;
				} else {
					_rememberEnabledAddonToggle = found.GetComponentInChildren<Toggle>(true);
					if (_rememberEnabledAddonToggle != null) {
						_rememberEnabledAddonToggle.onValueChanged.RemoveListener(OnRememberEnabledAddonsToggleChanged);
						_rememberEnabledAddonToggle.onValueChanged.AddListener(OnRememberEnabledAddonsToggleChanged);
					}
					EnsureRememberRowTooltip(found.gameObject);
					return;
				}
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
			bool on = Addon_MGR.GetRememberEnabledAddonsPreference();
			_rememberEnabledAddonToggle.SetIsOnWithoutNotify(on);
			ThemeRememberActionButton(_rememberEnabledAddonToggle, on);
		}

		GameObject BuildRememberEnabledPreferenceRow(float grid) {
			var row = new GameObject("RememberEnabledRow");
			row.layer = _panel != null ? _panel.gameObject.layer : 5;
			var rowLE = row.AddComponent<LayoutElement>();
			rowLE.preferredHeight = 34f;
			rowLE.minHeight = 30f;
			var rowHit = row.AddComponent<Image>();
			rowHit.color = Color.clear;
			rowHit.raycastTarget = true;
			var rowH = row.AddComponent<HorizontalLayoutGroup>();
			rowH.spacing = grid;
			rowH.padding = new RectOffset(0, 0, 2, 2);
			rowH.childAlignment = TextAnchor.MiddleLeft;
			rowH.childControlWidth = true;
			// false: Nomad BoundChrome + force-expand stretched the Remember face into a green capsule.
			rowH.childControlHeight = false;
			rowH.childForceExpandWidth = false;
			rowH.childForceExpandHeight = false;

			// Visible action button (not a tiny unmarked checkbox in the corner).
			var toggleContainer = new GameObject("ToggleWrap");
			toggleContainer.transform.SetParent(row.transform, false);
			var tLE = toggleContainer.AddComponent<LayoutElement>();
			tLE.preferredWidth = 210f;
			tLE.minWidth = 180f;
			tLE.flexibleWidth = 0f;
			tLE.preferredHeight = 28f;
			tLE.minHeight = 28f;
			tLE.flexibleHeight = 0f;
			var bgI = toggleContainer.AddComponent<UnityEngine.UI.Image>();
			AssignSolidFaceThenMarkRounded(bgI);
			bgI.raycastTarget = true;
			var ck = new GameObject("Checkmark");
			ck.transform.SetParent(toggleContainer.transform, false);
			var ckR = ck.AddComponent<RectTransform>();
			// Small filled dial on the left of the button face.
			ckR.anchorMin = new Vector2(0f, 0.5f);
			ckR.anchorMax = new Vector2(0f, 0.5f);
			ckR.pivot = new Vector2(0.5f, 0.5f);
			ckR.anchoredPosition = new Vector2(14f, 0f);
			ckR.sizeDelta = new Vector2(12f, 12f);
			var ckI = ck.AddComponent<UnityEngine.UI.Image>();
			ckI.sprite = UiRuntimeSprites.CircleFilled;
			ckI.type = Image.Type.Simple;
			ckI.preserveAspect = true;
			ckI.color = new Color(0.2f, 0.8f, 0.2f, 1f);
			ckI.raycastTarget = false;
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(toggleContainer.transform, false);
			var labelRt = labelObj.AddComponent<RectTransform>();
			labelRt.anchorMin = Vector2.zero;
			labelRt.anchorMax = Vector2.one;
			labelRt.offsetMin = new Vector2(28f, 0f);
			labelRt.offsetMax = new Vector2(-8f, 0f);
			var labelT = labelObj.AddComponent<TextMeshProUGUI>();
			bool rememberOn = Addon_MGR.GetRememberEnabledAddonsPreference();
			labelT.text = rememberOn ? "Remembering next launch" : "Remember next launch";
			labelT.fontSize = 12f;
			labelT.alignment = TextAlignmentOptions.MidlineLeft;
			labelT.enableWordWrapping = false;
			labelT.overflowMode = TextOverflowModes.Ellipsis;
			labelT.raycastTarget = false;
			var tgl = toggleContainer.AddComponent<Toggle>();
			tgl.SetIsOnWithoutNotify(rememberOn);
			tgl.targetGraphic = bgI;
			// Assign graphic before BoundChrome; never solid-square the ON glyph (IsToggleCheckmarkGraphic).
			tgl.graphic = ckI;
			tgl.transition = Selectable.Transition.ColorTint;
			tgl.onValueChanged.AddListener(OnRememberEnabledAddonsToggleChanged);
			_rememberEnabledAddonToggle = tgl;
			ThemeRememberActionButton(tgl, rememberOn);
			EnsureRememberRowTooltip(row);
			return row;
		}

		static string RememberButtonLabel(bool rememberOn) {
			return rememberOn ? "Remembering next launch" : "Remember next launch";
		}

		void ThemeRememberActionButton(Toggle toggle, bool isOn) {
			if (toggle == null) return;
			LockRememberToggleSquare(toggle);
			var face = toggle.targetGraphic as Image;
			if (face != null) {
				AssignSolidFaceThenMarkRounded(face);
				face.color = isOn
					? new Color(0.12f, 0.32f, 0.20f, 0.95f)
					: new Color(0.24f, 0.24f, 0.28f, 0.95f);
				face.raycastTarget = true;
			}
			if (toggle.graphic is Image ck) {
				UnwindDialFillHiddenForTheme(ck);
				ck.sprite = UiRuntimeSprites.CircleFilled;
				ck.preserveAspect = true;
				ck.type = Image.Type.Simple;
				ck.enabled = true;
				ck.gameObject.SetActive(true);
				Color ok = _statusOk;
				ok.a = 1f;
				TintStatusDialGraphic(ck, ok);
				ck.canvasRenderer.SetAlpha(isOn ? 1f : 0.35f);
			}
			var label = toggle.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
			if (label != null) {
				label.text = RememberButtonLabel(isOn);
				label.enableWordWrapping = false;
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.color = isOn
					? new Color(0.78f, 0.96f, 0.82f, 1f)
					: new Color(0.88f, 0.88f, 0.92f, 1f);
				float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(label, 12f);
				if (SpzUiThemeOps.ShouldRecolorBoundChrome)
					SpzUiThemeOps.ApplyBoundChromeTmp(label, label.color, basePt);
				else
					label.fontSize = basePt;
			}
		}

		const string RememberRowTooltip =
			"When on, this preference saves immediately and the next launch restores which add-ons were enabled. Preferences such as Show in Command Ribbon still need Save settings.";

		void EnsureRememberRowTooltip(GameObject rowOrToggle) {
			if (rowOrToggle == null)
				return;
			if (string.Equals(rowOrToggle.name, "RememberEnabledRow", StringComparison.Ordinal)) {
				var hit = rowOrToggle.GetComponent<Image>();
				if (hit == null) {
					hit = rowOrToggle.AddComponent<Image>();
					hit.color = Color.clear;
				}
				hit.raycastTarget = true;
			}
			AttachTooltip(rowOrToggle, RememberRowTooltip);
			if (_rememberEnabledAddonToggle != null)
				AttachTooltip(_rememberEnabledAddonToggle.gameObject, RememberRowTooltip);
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
		Transform t = _panel.transform.Find("ListArea/ScrollView/Viewport/Content");
		if (t == null)
			t = _panel.transform.Find("ScrollView/Viewport/Content");
		if (t == null)
			t = _panel.transform.Find("ListArea/ScrollView/Content");
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
			EnsureHeaderActionButtonsWired();
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
		AssignSolidFaceThenMarkRounded(image);
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

		var versionMarker = new GameObject(PanelShellVersionMarker);
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
		// false: Nomad SolidSquare + force-expand stretched Install/Refresh/Save into tall capsules.
		headerLayout.childControlHeight = false;
		headerLayout.childForceExpandWidth = false;
		headerLayout.childForceExpandHeight = false;
		headerLayout.childAlignment = TextAnchor.MiddleCenter;
		headerLayout.spacing = 10f;
		headerLayout.padding = new RectOffset(0, 0, 0, 0);
		
		GameObject titleObj = new GameObject("Title");
		titleObj.transform.SetParent(headerObj.transform, false);
		var titleLE = titleObj.AddComponent<LayoutElement>();
		titleLE.minWidth = 160f;
		titleLE.preferredWidth = 220f;
		titleLE.flexibleWidth = 1f;
		var titleText = titleObj.AddComponent<TextMeshProUGUI>();
		titleText.text = "Add-on Manager";
		titleText.fontSize = 24;
		titleText.color = Color.white;
		titleText.fontStyle = FontStyles.Bold;
		titleText.alignment = TextAlignmentOptions.MidlineLeft;
		titleText.enableWordWrapping = false;
		titleText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
		titleText.raycastTarget = false;
		
		void AddBarButton(Transform parent, string goName, string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick, Vector2 size, out Button outBtn, string tooltip = null) {
			var go = new GameObject(goName);
			go.transform.SetParent(parent, false);
			go.AddComponent<RectTransform>().sizeDelta = size;
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = size.x;
			le.minWidth = size.x;
			le.flexibleWidth = 0f;
			le.preferredHeight = size.y;
			var img = go.AddComponent<UnityEngine.UI.Image>();
			AssignSolidFaceThenMarkRounded(img);
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
			AttachTooltip(go, tooltip);
			outBtn = btn;
		}
		
		AddBarButton(headerObj.transform, "InstallButton", "Install from File", new Color(61f / 255f, 61f / 255f, 61f / 255f, 1f),
			Color.white, OnInstallFromFile, new Vector2(122, 34), out var installBtn,
			"Install an add-on from a .zip file into StreamingAssets/Addons.");
		_installFromFile_button = installBtn;
		AddBarButton(headerObj.transform, "RefreshButton", "Refresh", new Color(61f / 255f, 61f / 255f, 61f / 255f, 1f),
			Color.white, RescanAndRefreshAddonsList, new Vector2(82, 34), out var refreshBtn,
			"Rescan the Addons folder and refresh this list (keeps enable state for add-ons still present).");
		_refresh_button = refreshBtn;
		AddBarButton(headerObj.transform, "LoadAddonsNowButton", "Load addons now", new Color(46f / 255f, 204f / 255f, 113f / 255f, 1f),
			Color.white, OnLoadAddonsNow, new Vector2(126, 34), out _loadAddonsNow_button,
			"Ask Python to load every currently enabled add-on now (register / create panels).");
		AddBarButton(headerObj.transform, "SaveAddonSettingsButton", "Save settings", new Color(242f / 255f, 202f / 255f, 80f / 255f, 1f),
			new Color(0.12f, 0.12f, 0.14f, 1f), OnSaveAddonSettings, new Vector2(118, 34), out _saveAddonSettings_button,
			"Persist enabled add-ons and Preferences (e.g. Show in Command Ribbon) for the next launch.");
		AddBarButton(headerObj.transform, "RunWithAddonsButton", "Restart with addons", new Color(52f / 255f, 152f / 255f, 219f / 255f, 1f),
			Color.white, OnRestartWithAddons, new Vector2(142, 34), out _restartWithAddons_button,
			"Quit and relaunch StableProjectorz with the add-on Python server (Run_with_Addons).");
		// Runtime rebuild clears serialized Close — recreate so Nomad theme + users are not stuck with dimmer-only dismiss.
		AddBarButton(headerObj.transform, "CloseButton", "Close", new Color(61f / 255f, 61f / 255f, 61f / 255f, 1f),
			Color.white, ClosePanel, new Vector2(88, 34), out _closePanel_button,
			"Close the Add-on Manager.");
		
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
		AssignSolidFaceThenMarkRounded(pillsBg);
		pillsBg.color = new Color(39f / 255f, 39f / 255f, 42f / 255f, 0.55f);
		pillsBg.raycastTarget = false;
		var pillsLayout = filterPillsObj.AddComponent<HorizontalLayoutGroup>();
		pillsLayout.spacing = 0f;
		pillsLayout.padding = new RectOffset(3, 3, 3, 3);
		pillsLayout.childControlWidth = false;
		pillsLayout.childControlHeight = false;
		pillsLayout.childForceExpandWidth = false;
		pillsLayout.childForceExpandHeight = false;
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
		var scrollView = scrollViewObj.AddComponent<UnityEngine.UI.ScrollRect>();
		scrollView.horizontal = false;
		scrollView.vertical = true;
		scrollView.scrollSensitivity = 20f;
		scrollView.movementType = ScrollRect.MovementType.Clamped;
		scrollView.inertia = true;
		scrollView.decelerationRate = 0.135f;
		// Clear hit plate on ScrollView — never put an enabled Image on Viewport (white bar bug).
		var scrollHit = scrollViewObj.AddComponent<UnityEngine.UI.Image>();
		scrollHit.sprite = UiRuntimeSprites.SolidRect;
		scrollHit.type = Image.Type.Simple;
		scrollHit.color = Color.clear;
		scrollHit.raycastTarget = true;

		// Nested Viewport — RectMask2D only (no Mask, no enabled Image).
		GameObject viewportObj = new GameObject("Viewport");
		viewportObj.layer = UILayer;
		viewportObj.transform.SetParent(scrollViewObj.transform, false);
		var viewportRect = viewportObj.AddComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.sizeDelta = Vector2.zero;
		viewportRect.pivot = new Vector2(0.5f, 0.5f);
		viewportObj.AddComponent<RectMask2D>();

		GameObject contentObj = new GameObject("Content");
		contentObj.layer = UILayer;
		contentObj.transform.SetParent(viewportObj.transform, false);
		var contentRect = contentObj.AddComponent<RectTransform>();
		contentRect.anchorMin = new Vector2(0, 1);
		contentRect.anchorMax = new Vector2(1, 1);
		contentRect.pivot = new Vector2(0.5f, 1f);
		contentRect.sizeDelta = new Vector2(0, 0);
		contentRect.anchoredPosition = Vector2.zero;
		var contentLayout = contentObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		contentLayout.spacing = RowSpacing;
		// Extra bottom pad so the last row's Uninstall / Preferences stay clear of the clip edge.
		contentLayout.padding = new RectOffset(0, 0, (int)Grid, (int)(Grid * 3));
		// Must control height: with false, items keep sizeDelta.y=40 while LayoutElement grows →
		// expanded PreferencesBody paints over HeaderRow (name + "Host preferences" overlap).
		contentLayout.childControlHeight = true;
		contentLayout.childControlWidth = true;
		contentLayout.childForceExpandHeight = false;
		contentLayout.childForceExpandWidth = true;
		var contentSizeFitter = contentObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
		contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
		contentSizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
		scrollView.viewport = viewportRect;
		scrollView.content = contentRect;
		_addonsListParent = contentRect;
		
		_filterAllToggle.SetIsOnWithoutNotify(true);

		var statusObj = new GameObject("StatusText");
		statusObj.transform.SetParent(panelObj.transform, false);
		var statusLE = statusObj.AddComponent<LayoutElement>();
		statusLE.preferredHeight = 36f;
		statusLE.minHeight = 28f;
		statusLE.flexibleWidth = 1f;
		_statusText = statusObj.AddComponent<TextMeshProUGUI>();
		_statusText.text = "";
		_statusText.fontSize = 12f;
		_statusText.color = new Color(0.63f, 0.63f, 0.67f, 1f);
		_statusText.alignment = TextAlignmentOptions.TopLeft;
		_statusText.enableWordWrapping = true;
		_statusText.overflowMode = TextOverflowModes.Ellipsis;
		_statusText.maxVisibleLines = 2;
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
		if (string.Equals(controlName, "CloseButton", StringComparison.Ordinal))
			return StudioLineIcon.ChevronLeft;
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
			EnsureHeaderActionButtonsWired();
			EnsureListScrollViewportHealthy();
			TryAddRememberPreferenceRowIfMissing();
			TryEnsureSaveSettingsButton();
			EnsureChromeTooltips();
			SyncRememberEnabledToggleFromPrefs();
			if (!_draftDirty) {
				SeedDraftFromLiveAddons();
				// Baseline for Close-without-Save → RevertShowInRibbonPrefsFromSnapshot (empty snapshot = no-op).
				SnapshotShowInRibbonPrefs();
			}
			// SoftLoad enables may already differ from Remember prefs — keep Save nudge honest.
			RecomputeDraftDirtyFromLive();
			// Late ribbon / prior migrate give-up: retry park→shell while the user has the manager open.
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.RequestMigrateParkedPanelsNow();
			
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
			// Only clear pending when overlay canvas exists — otherwise deferred open must keep retrying.
			if (rootCanvas != null)
				s_pendingOpenRequest = false;
			if (_blockerDimImage != null)
				_blockerDimImage.color = RefBgModalDim;
			if (_panelModalGroup != null) {
				_panelModalGroup.alpha = 1f;
				_panelModalGroup.interactable = true;
			}

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
			string closeWarn = null;
			if (_draftDirty) {
				// Show-in-Ribbon was applied live — revert unsaved ribbon prefs so a later Save cannot persist a "discarded" flip.
				RevertShowInRibbonPrefsFromSnapshot();
				closeWarn =
					"Closed without Save settings — enable selection may not persist next launch; ribbon prefs reverted.";
				SeedDraftFromLiveAddons();
				SnapshotShowInRibbonPrefs();
			}
			// SoftLoad dials apply live; SeedDraft clears _draftDirty — restore dirty vs Remember prefs.
			RecomputeDraftDirtyFromLive();
			if (_hidViewportStatusForModal && Viewport_StatusText.instance != null) {
				Viewport_StatusText.instance.PreferVIsible(this);
				_hidViewportStatusForModal = false;
			}
			if (_blocker != null) _blocker.SetActive(false);
			if (_panel != null) _panel.SetActive(false);
			// Panel status is hidden with the modal — mirror the close warning to the viewport toast.
			if (!string.IsNullOrEmpty(closeWarn) && Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText(closeWarn, false, 5f, false);
		}
		
		bool _loadAddonsNowInFlight;

		/// <summary>
		/// Requests Python to load all currently enabled add-ons.
		/// </summary>
		void OnLoadAddonsNow() {
			if (_loadAddonsNowInFlight) {
				ShowStatus("Load already in progress…", false);
				return;
			}
			_loadAddonsNowInFlight = true;
			if (_loadAddonsNow_button != null)
				_loadAddonsNow_button.interactable = false;
			ShowStatus("Loading addons...", true);
			if (Addon_MGR.instance != null) {
				Addon_MGR.instance.RequestLoadAllEnabledAddonsNow((requested, hardFail, softFail) => {
					_loadAddonsNowInFlight = false;
					if (_loadAddonsNow_button != null)
						_loadAddonsNow_button.interactable = true;
					int parkedAwaiting = AddonUI_MGR.instance != null
						? AddonUI_MGR.instance.CountParkedAwaitingRibbonShow()
						: 0;
					if (requested == 0)
						ShowStatus("No enabled add-ons to load.", false);
					else if (hardFail > 0)
						ShowStatus(
							$"Load finished — {hardFail}/{requested} failed (disabled). Check log.",
							false);
					else if (softFail > 0)
						ShowStatus(
							$"Load finished — {softFail}/{requested} used native/dock fallback (Python load failed). Check log.",
							false);
					else if (parkedAwaiting > 0)
						ShowStatus(
							$"Load finished for {requested} add-on(s) — {parkedAwaiting} panel(s) still off-ribbon (waiting for ribbon shell / Show in Ribbon).",
							false);
					else
						ShowStatus($"Load finished for {requested} add-on(s).", true);
					RefreshAddonsList();
				});
			} else {
				_loadAddonsNowInFlight = false;
				if (_loadAddonsNow_button != null)
					_loadAddonsNow_button.interactable = true;
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
				Addon_MGR.instance.PersistAddonPrefsNow();
				SeedDraftFromLiveAddons();
				SnapshotShowInRibbonPrefs();
			} finally {
				_suppressEnabledListRefresh = false;
			}
			RefreshAddonsList();
			bool rememberOn = Addon_MGR.GetRememberEnabledAddonsPreference();
			if (changed == 0) {
				ShowStatus(
					rememberOn
						? "Settings saved. Selection and preferences persisted for next launch."
						: "Settings saved. Preferences persisted; enable selection restore is off (Remember).",
					true);
			} else {
				ShowStatus(
					rememberOn
						? $"Settings saved — {changed} add-on(s) applied; selection and preferences persisted."
						: $"Settings saved — {changed} add-on(s) applied; preferences persisted (Remember off — selection not restored next launch).",
					true);
			}
		}

		string KeepNextLaunchHint() {
			return Addon_MGR.GetRememberEnabledAddonsPreference()
				? "Click Save settings to keep next launch."
				: "Save persists prefs; enable restore is off (Remember).";
		}

		string KeepPrefsHint() {
			return "Click Save settings to keep.";
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

		void SnapshotShowInRibbonPrefs() {
			_showInRibbonSnapshotById.Clear();
			if (Addon_MGR.instance == null) return;
			foreach (var kvp in Addon_MGR.instance.GetAddons()) {
				if (string.IsNullOrEmpty(kvp.Key)) continue;
				_showInRibbonSnapshotById[kvp.Key] = Addon_MGR.instance.ShouldShowInCommandRibbon(kvp.Key);
			}
		}

		/// <summary>
		/// After install/refresh while draft is dirty, new add-on ids are missing from the Close-without-Save
		/// baseline — merge them at current live value so a later discard can still revert their ribbon flips.
		/// </summary>
		void EnsureShowInRibbonSnapshotCoversAllAddons() {
			if (Addon_MGR.instance == null) return;
			foreach (var kvp in Addon_MGR.instance.GetAddons()) {
				if (string.IsNullOrEmpty(kvp.Key)) continue;
				if (_showInRibbonSnapshotById.ContainsKey(kvp.Key)) continue;
				_showInRibbonSnapshotById[kvp.Key] = Addon_MGR.instance.ShouldShowInCommandRibbon(kvp.Key);
			}
		}

		void RevertShowInRibbonPrefsFromSnapshot() {
			if (Addon_MGR.instance == null || _showInRibbonSnapshotById.Count == 0) return;
			foreach (var kvp in _showInRibbonSnapshotById) {
				if (Addon_MGR.instance.ShouldShowInCommandRibbon(kvp.Key) == kvp.Value)
					continue;
				Addon_MGR.instance.SetShowInCommandRibbon(kvp.Key, kvp.Value);
			}
		}

		/// <summary>Clear false Close warnings when live enable matches draft (e.g. after load-fail flips dial off).</summary>
		void RecomputeDraftDirtyFromLive() {
			if (Addon_MGR.instance == null) {
				_draftDirty = false;
				return;
			}
			foreach (var kvp in Addon_MGR.instance.GetAddons()) {
				if (kvp.Value == null) continue;
				if (GetDraftEnabled(kvp.Key, kvp.Value.isEnabled) != kvp.Value.isEnabled) {
					_draftDirty = true;
					return;
				}
			}
			foreach (var kvp in _showInRibbonSnapshotById) {
				if (Addon_MGR.instance.ShouldShowInCommandRibbon(kvp.Key) != kvp.Value) {
					_draftDirty = true;
					return;
				}
			}
			// SoftLoad mirrors draft←live so the loops above stay clean; still dirty vs last Save when Remember is on.
			if (Addon_MGR.instance.LiveEnabledSelectionDiffersFromPersisted()) {
				_draftDirty = true;
				return;
			}
			_draftDirty = false;
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

		Coroutine _installFromFilePickCo;

		/// <summary>
		/// Opens file browser to select a zip / __init__.py for installation.
		/// Uses deferred helper so the dialog is not opened on the same pointer-up as the button click.
		/// </summary>
		void OnInstallFromFile() {
			if (!isActiveAndEnabled || !gameObject.activeInHierarchy) {
				ShowStatus("Add-on Manager is not ready to install.", false);
				return;
			}
			if (_installFromFilePickCo != null)
				StopCoroutine(_installFromFilePickCo);
			_installFromFilePickCo = StartCoroutine(AddonInstallFromFile_Helper.CoDeferredThenPickZipOrInitPy(
				AddonManagerCanvasSortOrder,
				path => {
					_installFromFilePickCo = null;
					InstallAddon(path);
				},
				() => { _installFromFilePickCo = null; },
				ex => {
					_installFromFilePickCo = null;
					Debug.LogError($"[AddonManager_UI] Install file browser failed: {ex.Message}\n{ex.StackTrace}");
					ShowStatus("Could not open Install file browser.", false);
				}));
		}
		
		/// <summary>
		/// Installs an add-on from a zip path or an <c>__init__.py</c> path (folder install).
		/// </summary>
		void InstallAddon(string path) {
			if (string.IsNullOrEmpty(path)) {
				ShowStatus("Invalid file path", false);
				return;
			}

			string ext = Path.GetExtension(path);
			if (!string.IsNullOrEmpty(ext) && ext.Equals(".py", StringComparison.OrdinalIgnoreCase)) {
				string root = Path.GetDirectoryName(path);
				if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
					ShowStatus("Could not resolve add-on folder from __init__.py", false);
					return;
				}
				ShowStatus("Installing add-on folder...", true);
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				if (AddonInstaller_MGR.TryPublishAddonRootToStreamingAssets(root, addonsPath, out string addonId, out string err)) {
					ShowStatus($"Add-on '{addonId}' installed successfully!", true);
					RefreshAddonsList();
				} else {
					ShowStatus($"Installation failed: {err}", false);
				}
				return;
			}

			if (AddonInstaller_MGR.instance == null) {
				ShowStatus("Add-on installer not available", false);
				return;
			}
			
			ShowStatus("Installing add-on...", true);
			
			AddonInstaller_MGR.instance.InstallAddonFromZip(path, (success, message, addonId) => {
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
			AssignSolidFaceThenMarkRounded(toggleBg);
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
			string tip = filterValue == 0
				? "Show all installed add-ons."
				: filterValue == 1
					? "Show only enabled (loaded) add-ons."
					: "Show only disabled add-ons.";
			AttachTooltip(toggleObj, tip);
			return toggleObj;
		}

		/// <summary>Hover tip via shared <see cref="CanShowTooltip_UI"/> (respects Settings → Allow tooltips).</summary>
		static void AttachTooltip(GameObject go, string tip) {
			if (go == null || string.IsNullOrEmpty(tip))
				return;
			var tipUi = go.GetComponent<CanShowTooltip_UI>() ?? go.AddComponent<CanShowTooltip_UI>();
			tipUi.set_overrideMessage(tip);
		}

		/// <summary>Re-apply tips on header/remember chrome recovered from an older panel shell.</summary>
		void EnsureChromeTooltips() {
			if (_installFromFile_button != null)
				AttachTooltip(_installFromFile_button.gameObject,
					"Install an add-on from a .zip file into StreamingAssets/Addons.");
			if (_refresh_button != null)
				AttachTooltip(_refresh_button.gameObject,
					"Rescan the Addons folder and refresh this list (keeps enable state for add-ons still present).");
			if (_loadAddonsNow_button != null)
				AttachTooltip(_loadAddonsNow_button.gameObject,
					"Ask Python to load every currently enabled add-on now (register / create panels).");
			if (_saveAddonSettings_button != null)
				AttachTooltip(_saveAddonSettings_button.gameObject,
					"Persist enabled add-ons and Preferences (e.g. Show in Command Ribbon) for the next launch.");
			if (_restartWithAddons_button != null)
				AttachTooltip(_restartWithAddons_button.gameObject,
					"Quit and relaunch StableProjectorz with the add-on Python server (Run_with_Addons).");
			if (_openPanel_button != null)
				AttachTooltip(_openPanel_button.gameObject, "Open the Add-on Manager.");
			if (_closePanel_button != null)
				AttachTooltip(_closePanel_button.gameObject, "Close the Add-on Manager.");
			if (_rememberEnabledAddonToggle != null)
				AttachTooltip(_rememberEnabledAddonToggle.gameObject, RememberRowTooltip);
			var rememberRow = _panel != null ? _panel.transform.Find("RememberEnabledRow") : null;
			if (rememberRow != null)
				EnsureRememberRowTooltip(rememberRow.gameObject);
			if (_filterAllToggle != null)
				AttachTooltip(_filterAllToggle.gameObject, "Show all installed add-ons.");
			if (_filterEnabledToggle != null)
				AttachTooltip(_filterEnabledToggle.gameObject, "Show only enabled (loaded) add-ons.");
			if (_filterDisabledToggle != null)
				AttachTooltip(_filterDisabledToggle.gameObject, "Show only disabled add-ons.");
		}
		
		/// <summary>
		/// Refreshes the list of add-ons with current filter applied (main-branch behavior — no search).
		/// </summary>
		/// <summary>
		/// Header Refresh: disk rescan then rebuild list. Filter/search/enable events use <see cref="RefreshAddonsList"/> only.
		/// </summary>
		public void RescanAndRefreshAddonsList() {
			try {
				if (Addon_MGR.instance != null)
					Addon_MGR.instance.RefreshAddons();
			} catch (System.Exception e) {
				Debug.LogError($"[AddonManager_UI] RescanAndRefreshAddonsList: Discover failed: {e.Message}\n{e.StackTrace}");
				ShowStatus("Rescan failed — list may be stale.", false);
			}
			RefreshAddonsList();
		}

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

			if (!_draftDirty) {
				SeedDraftFromLiveAddons();
				// Re-baseline after install/refresh so new add-on ids are discardable if the user dirties later.
				SnapshotShowInRibbonPrefs();
			} else {
				EnsureShowInRibbonSnapshotCoversAllAddons();
			}
			
			var addons = Addon_MGR.instance.GetAddons();
			var filteredAddons = new List<KeyValuePair<string, Addon_MGR.AddonInfo>>();
			int enabledCount = 0;
			int disabledCount = 0;
			
			foreach (var kvp in addons) {
				if (kvp.Value == null) continue;
				bool draftOn = GetDraftEnabled(kvp.Key, kvp.Value.isEnabled);
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
				RebuildAddonListScrollLayout(null);
			}
			
			string filterText = _filterState == 0 ? "All" : (_filterState == 1 ? "Enabled" : "Disabled");
			if (addons.Count > 0) {
				if (filteredAddons.Count == 0)
					ShowStatus("No add-ons match the current filter.", false);
				else
					ShowStatus($"Showing {filteredAddons.Count} of {addons.Count} add-on(s) ({enabledCount} enabled, {disabledCount} disabled) — Filter: {filterText}", true);
			}
			ApplyThemeTokens();
			EnsureListScrollViewportHealthy();
		}
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				_statusOk = kAuthoredStatusOk;
				_statusFail = kAuthoredStatusFail;
				_statusMuted = kAuthoredStatusMuted;
				if (_openPanel_button != null) {
					SpzUiThemeOps.RestoreBoundChromeUnder(_openPanel_button.transform);
					HideMonolithUnder(_openPanel_button.transform);
				}
				if (_panel != null) {
					// Full unwind: ColorBlocks / TMP metrics / line icons — not Graphic colors alone.
					SpzUiThemeOps.RestoreBoundChromeUnder(_panel.transform);
					SpzUiThemeOps.RefreshScaledLayoutGroupsUnder(_panel.transform);
					RestoreHeaderChildAlignment();
					RestoreHeaderButtonAuthoredChrome(_installFromFile_button);
					RestoreHeaderButtonAuthoredChrome(_refresh_button);
					RestoreHeaderButtonAuthoredChrome(_loadAddonsNow_button);
					RestoreHeaderButtonAuthoredChrome(_saveAddonSettings_button);
					RestoreHeaderButtonAuthoredChrome(_restartWithAddons_button);
					EnsureListScrollViewportHealthy();
				}
				if (_closePanel_button != null)
					SpzUiThemeOps.RestoreBoundChromeUnder(_closePanel_button.transform);
				// Dial/prefs colors are tinted without Flatten — re-apply authored after Restore so Nomad green does not stick.
				ReapplyAuthoredStatusDialsAfterThemeRestore();
				return;
			}
			var t = SpzUiThemeOps.Active;
			_statusOk = t.success;
			_statusFail = t.danger;
			_statusMuted = t.textMuted;
			bool boundChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
			ThemeOpenLauncherButton(t);
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
					// Snapshot authored pads first — absolute Nomad writes must not become the Restore baseline.
					SpzUiThemeOps.ApplyScaledLayoutGroup(panelVlg);
					int pad = Mathf.RoundToInt(SpzUiThemeOps.ScaledSpace(3));
					panelVlg.spacing = SpzUiThemeOps.ScaledSpace(2);
					panelVlg.padding = new RectOffset(pad, pad, pad, pad);
				}
				var header = _panel.transform.Find("Header");
				if (header != null) {
					var headerHlg = header.GetComponent<HorizontalLayoutGroup>();
					if (headerHlg != null) {
						SpzUiThemeOps.ApplyScaledLayoutGroup(headerHlg);
						headerHlg.spacing = SpzUiThemeOps.ScaledSpace(6);
						// Snapshot authored alignment before Nomad write — leave RefreshScaled restores pad only.
						if (!_headerChildAlignSnapshotted) {
							_authoredHeaderChildAlignment = headerHlg.childAlignment;
							_authoredHeaderChildControlHeight = headerHlg.childControlHeight;
							_authoredHeaderChildForceExpandHeight = headerHlg.childForceExpandHeight;
							_headerChildAlignSnapshotted = true;
						}
						headerHlg.childAlignment = TextAnchor.MiddleLeft;
						int hPad = Mathf.RoundToInt(SpzUiThemeOps.ScaledSpace(2));
						headerHlg.padding = new RectOffset(hPad, hPad, 0, 0);
						headerHlg.childControlHeight = false;
						headerHlg.childForceExpandHeight = false;
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
				var rememberLabel = _panel.transform.Find("RememberEnabledRow/ToggleWrap/Label")?.GetComponent<TextMeshProUGUI>();
				if (rememberLabel == null)
					rememberLabel = _panel.transform.Find("RememberEnabledRow/Label")?.GetComponent<TextMeshProUGUI>();
				if (rememberLabel != null) {
					CaptureBasePt(ref _themeRememberLabelBasePt, rememberLabel, 12f);
					SpzUiThemeOps.ApplyBoundChromeTmp(rememberLabel, t.textPrimary, _themeRememberLabelBasePt);
				}
			}
			if (_closePanel_button != null) {
				SpzUiThemeOps.EnsureSelectableHitFace(_closePanel_button);
				SpzUiThemeOps.ApplyBoundChromeSelectable(_closePanel_button, t.controlBg, t.accent);
				foreach (var tmp in _closePanel_button.GetComponentsInChildren<TextMeshProUGUI>(true)) {
					if (tmp == null) continue;
					// Not CompactToolLabel — UpperCase+Truncate clips Close like Uninstall/Disabled did.
					float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(tmp, 11f);
					SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary, basePt);
					tmp.enableWordWrapping = false;
					tmp.overflowMode = TextOverflowModes.Ellipsis;
					tmp.fontStyle = FontStyles.Normal;
					tmp.characterSpacing = 0f;
					tmp.maxVisibleCharacters = int.MaxValue;
				}
				SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_closePanel_button);
			}
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
				EnsureListScrollViewportHealthy();
				var listImg = _addonsListParent.GetComponent<Image>();
				if (listImg != null)
					SpzUiThemeOps.ApplyBoundChromeGraphic(listImg, t.fieldBg);
				var listVlg = _addonsListParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
				if (listVlg != null) {
					SpzUiThemeOps.ApplyScaledLayoutGroup(listVlg);
					int listPad = Mathf.RoundToInt(SpzUiThemeOps.ScaledSpace(1));
					// Keep authored bottom clearance (Grid*3 at create) so last Uninstall / expanded
					// Preferences stay above the Mask clip edge under Nomad.
					const int listBottomClearance = 24;
					listVlg.spacing = SpzUiThemeOps.ScaledSpace(2);
					listVlg.padding = new RectOffset(0, 0, listPad, Mathf.Max(listPad, listBottomClearance));
					// Theme scale must not leave height uncontrolled (prefs overlay dial/name).
					listVlg.childControlHeight = true;
					listVlg.childForceExpandHeight = false;
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
				// Do not ThemeCheckboxToggle — that paints a tiny unmarked square; use labeled action button.
				ThemeRememberActionButton(_rememberEnabledAddonToggle, _rememberEnabledAddonToggle.isOn);
			}
		}

		/// <summary>Keep Remember action button sized after theme passes.</summary>
		static void LockRememberToggleSquare(Toggle toggle) {
			if (toggle == null) return;
			var le = toggle.GetComponent<LayoutElement>();
			if (le != null) {
				SpzUiThemeOps.SnapshotLayoutElementForTheme(le);
				le.preferredWidth = 210f;
				le.minWidth = 180f;
				le.preferredHeight = 28f;
				le.minHeight = 28f;
				le.flexibleWidth = 0f;
				le.flexibleHeight = 0f;
			}
			var rt = toggle.transform as RectTransform;
			if (rt != null) {
				SpzUiThemeOps.SnapshotToolFaceLayout(rt);
				rt.sizeDelta = new Vector2(210f, 28f);
			}
			var row = toggle.transform.parent;
			if (row != null) {
				var hlg = row.GetComponent<HorizontalLayoutGroup>();
				if (hlg != null) {
					hlg.childControlHeight = false;
					hlg.childForceExpandHeight = false;
					hlg.childForceExpandWidth = false;
				}
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

		/// <summary>
		/// Strip open launcher sits outside the panel — theme like Settings gear (SolidSquare + Monolith).
		/// </summary>
		void ThemeOpenLauncherButton(SpzUiThemeOps.ThemeTokens t) {
			if (_openPanel_button == null)
				return;
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				SpzUiThemeOps.RestoreBoundChromeUnder(_openPanel_button.transform);
				HideMonolithUnder(_openPanel_button.transform);
				return;
			}
			SpzUiThemeOps.EnsureSelectableHitFace(_openPanel_button);
			if (_openPanel_button.targetGraphic == null) return;
			SpzUiThemeOps.ApplySolidSquareChrome(_openPanel_button, t.controlBg, t.accent);
			if (_openPanel_button.targetGraphic != null)
				_openPanel_button.targetGraphic.raycastTarget = true;
			SpzUiThemeOps.ApplyControlLineIcon(_openPanel_button.transform, StudioLineIcon.Grid, 18f);
			foreach (var tmp in _openPanel_button.GetComponentsInChildren<TextMeshProUGUI>(true)) {
				if (tmp == null) continue;
				SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary);
				SpzUiThemeOps.HideAuthoredGraphicForTheme(tmp);
			}
			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_openPanel_button);
		}

		static void HideMonolithUnder(Transform root) {
			if (root == null) return;
			foreach (var tr in root.GetComponentsInChildren<Transform>(true)) {
				if (tr == null) continue;
				string n = tr.name ?? "";
				if (n == "MonolithLineIcon" || n == "MonolithActiveBar")
					tr.gameObject.SetActive(false);
			}
		}

		static void ThemeHeaderButton(Button button, Color normal, Color highlighted, Color foreground) {
			if (button == null) return;
			SpzUiThemeOps.EnsureSelectableHitFace(button);
			SpzUiThemeOps.ApplyBoundChromeSelectable(button, normal, highlighted);
			var label = button.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
			var icon = button.transform.Find("LineIcon")?.GetComponent<Image>();
			bool boundChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
			// RibbonIconOnly is CommandRibbon-only — applying it here hides Install/Save/Close labels in Manager.
			bool iconOnly = false;
			if (icon != null && SpzUiThemeOps.ShouldRecolorBoundChrome) {
				var iconRt = icon.rectTransform;
				SpzUiThemeOps.SnapshotToolFaceLayout(iconRt);
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
					SpzUiThemeOps.SnapshotToolFaceLayout(labelRt);
					// Leave a fixed gutter after the left-aligned line icon so labels share one column.
					labelRt.offsetMin = new Vector2(boundChrome ? 30f : 25f, 0f);
					labelRt.offsetMax = new Vector2(-5f, 0f);
				}
			}
			if (button.targetGraphic is Image btnImg)
				AssignSolidFaceThenMarkRounded(btnImg);
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
			if (string.Equals(goName, "CloseButton", StringComparison.Ordinal)) return 88f;
			return 100f;
		}

		void RestoreHeaderChildAlignment() {
			if (!_headerChildAlignSnapshotted || _panel == null) return;
			var header = _panel.transform.Find("Header");
			var headerHlg = header != null ? header.GetComponent<HorizontalLayoutGroup>() : null;
			if (headerHlg != null) {
				headerHlg.childAlignment = _authoredHeaderChildAlignment;
				headerHlg.childControlHeight = _authoredHeaderChildControlHeight;
				headerHlg.childForceExpandHeight = _authoredHeaderChildForceExpandHeight;
			}
		}

		static void RestoreHeaderButtonAuthoredChrome(Button button) {
			if (button == null) return;
			// Unwind BoundChrome + snapshotted icon/label rects (do not hardcode 25/-5 / 8,14).
			SpzUiThemeOps.RestoreBoundChromeUnder(button.transform);
			var label = button.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
			if (label != null)
				label.maxVisibleCharacters = int.MaxValue;
			var icon = button.transform.Find("LineIcon")?.GetComponent<Image>();
			if (icon != null)
				icon.gameObject.SetActive(true);
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
			// Flat fill for the pill — but not ThemeFlatToolToggle: CompactToolLabel truncates "Disabled" → DISABLE□.
			SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, face, t.accent);
			if (toggle.targetGraphic is Image bg) {
				bg.color = face;
				bg.raycastTarget = true;
			}
			var label = toggle.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
			if (label != null) {
				float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(label, 14f);
				SpzUiThemeOps.ApplyBoundChromeTmp(label, toggle.isOn ? t.textPrimary : t.textMuted, basePt);
				label.enableWordWrapping = false;
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.fontStyle = FontStyles.Normal;
				label.characterSpacing = 0f;
				label.maxVisibleCharacters = int.MaxValue;
			}
			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);
		}

		void ThemeAddonListItem(GameObject item, SpzUiThemeOps.ThemeTokens t) {
			// Keep nested prefs under the header — theme must not leave item height uncontrolled.
			var itemVlg = item.GetComponent<VerticalLayoutGroup>();
			if (itemVlg != null) {
				itemVlg.childControlHeight = true;
				itemVlg.childForceExpandHeight = false;
			}
			Transform header = item.transform.Find("HeaderRow");
			if (header != null) {
				var headerLe = header.GetComponent<LayoutElement>();
				if (headerLe != null)
					headerLe.flexibleHeight = 0f;
				var headerHlg = header.GetComponent<HorizontalLayoutGroup>();
				if (headerHlg != null) {
					headerHlg.childControlHeight = false;
					headerHlg.childForceExpandHeight = false;
				}
			}
			Transform remove = null;
			var prefsCardForRemove = item.transform.Find("PreferencesBody/PreferencesCard");
			if (prefsCardForRemove != null) {
				remove = prefsCardForRemove.Find("RemoveButton");
				if (remove == null) remove = prefsCardForRemove.Find("RemoveBtn");
			}
			if (remove == null && header != null) {
				remove = header.Find("RemoveBtn");
				if (remove == null) remove = header.Find("RemoveButton");
			}
			if (remove == null) remove = item.transform.Find("RemoveBtn");
			if (remove == null) remove = item.transform.Find("RemoveButton");
			if (remove != null) {
				var removeBtn = remove.GetComponent<Button>();
				if (removeBtn != null) {
					Color dangerBg = Color.Lerp(t.panelBg, t.danger, 0.18f);
					SpzUiThemeOps.ApplyBoundChromeSelectable(removeBtn, dangerBg, Color.Lerp(dangerBg, t.danger, 0.28f));
					var removeLe = remove.GetComponent<LayoutElement>();
					if (removeLe != null) {
						SpzUiThemeOps.SnapshotLayoutElementForTheme(removeLe);
						removeLe.preferredWidth = 92f;
						removeLe.minWidth = 88f;
						removeLe.preferredHeight = 28f;
						removeLe.minHeight = 28f;
						removeLe.flexibleWidth = 0f;
					}
				}
				var removeLabel = remove.GetComponentInChildren<TextMeshProUGUI>(true);
				if (removeLabel != null) {
					float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(removeLabel, 11f);
					// Not CompactToolLabel — UpperCase+Truncate turns "Uninstall" into UNINSTA□ in the 76px button.
					SpzUiThemeOps.ApplyBoundChromeTmp(removeLabel, new Color(t.danger.r, t.danger.g, t.danger.b, 0.88f), basePt);
					removeLabel.enableWordWrapping = false;
					removeLabel.overflowMode = TextOverflowModes.Ellipsis;
					removeLabel.fontStyle = FontStyles.Normal;
					removeLabel.characterSpacing = 0f;
				}
				if (removeBtn != null)
					SpzUiThemeOps.ClearNonFaceRaycastsForTheme(removeBtn);
			}
			Transform expandT = header != null ? header.Find("ExpandChevron") : item.transform.Find("ExpandChevron");
			if (expandT != null) {
				var prefsOpen = item.transform.Find("PreferencesBody");
				bool expanded = prefsOpen != null && prefsOpen.gameObject.activeSelf;
				// Same chevron in Nomad and default — do not ClearNonFace/RestoreBoundChrome (that undoes the arrow).
				ApplyExpandChevronVisual(expandT, expanded);
			}
			var toggle = (header != null ? header.Find("StatusToggle") : null)?.GetComponent<Toggle>();
			if (toggle == null)
				toggle = item.transform.Find("StatusToggle")?.GetComponent<Toggle>();
			if (toggle == null)
				toggle = item.transform.Find("HeaderRow/StatusToggle")?.GetComponent<Toggle>();
			string itemAddonId = null;
			if (item.name != null && item.name.StartsWith("AddonItem_", StringComparison.Ordinal))
				itemAddonId = item.name.Substring("AddonItem_".Length);
			bool enabled = toggle != null && toggle.isOn;
			if (!string.IsNullOrEmpty(itemAddonId) && Addon_MGR.instance != null
			    && Addon_MGR.instance.GetAddons().TryGetValue(itemAddonId, out var liveInfo) && liveInfo != null)
				enabled = GetDraftEnabled(itemAddonId, liveInfo.isEnabled);
			var name = (header != null ? header.Find("Name") : null)?.GetComponent<TextMeshProUGUI>();
			if (name == null)
				name = item.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
			if (name != null) {
				float nameBase = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(name, 14f);
				// Not ReadableBody — wrap+Overflow stomps single-line Ellipsis and spills into prefs under Nomad.
				SpzUiThemeOps.ApplyBoundChromeTmp(name, t.textPrimary, nameBase);
				name.enableWordWrapping = false;
				name.overflowMode = TextOverflowModes.Ellipsis;
				name.raycastTarget = false;
			}
			if (toggle != null) {
				Color ringColor = enabled ? t.success : t.textMuted;
				var ringImg = toggle.transform.Find("Ring")?.GetComponent<Image>();
				if (ringImg != null) {
					// Keep CircleRing sprite — ApplyBoundChromeGraphic flattens dials into grey/green capsules.
					TintStatusDialGraphic(ringImg, ringColor);
					ringImg.preserveAspect = true;
					ringImg.type = Image.Type.Simple;
				}
				Image fill = toggle.graphic as Image;
				if (fill == null)
					fill = toggle.transform.Find("Ring/Checkmark")?.GetComponent<Image>();
				if (fill != null) {
					TintStatusDialGraphic(fill, t.success);
					fill.preserveAspect = true;
					fill.type = Image.Type.Simple;
					fill.gameObject.SetActive(true);
					fill.enabled = enabled;
					fill.canvasRenderer.SetAlpha(enabled ? 1f : 0f);
				}
			}
			var prefsBodyT = item.transform.Find("PreferencesBody");
			if (prefsBodyT != null) {
				var prefsBodyImg = prefsBodyT.GetComponent<Image>();
				if (prefsBodyImg != null) {
					prefsBodyImg.color = Color.clear;
					prefsBodyImg.raycastTarget = false;
				}
				var prefsCardT = prefsBodyT.Find("PreferencesCard");
				if (prefsCardT != null) {
					var cardImg = prefsCardT.GetComponent<Image>();
					if (cardImg != null) {
						// No giant grey plate — metadata + ribbon action button sit on the list bg.
						cardImg.color = Color.clear;
						cardImg.raycastTarget = false;
					}
				}
				var prefRowBg = (prefsCardT != null ? prefsCardT.Find("PrefRow_ShowInRibbon") : prefsBodyT.Find("PrefRow_ShowInRibbon"))
					?.GetComponent<Image>();
				if (prefRowBg != null) {
					prefRowBg.color = Color.clear;
					prefRowBg.raycastTarget = false;
				}
				Transform metaRoot = prefsCardT != null ? prefsCardT : prefsBodyT;
				var prefsHdr = metaRoot.Find("PrefsDropdownHeader")?.GetComponent<TextMeshProUGUI>();
				if (prefsHdr != null) {
					float hdrBase = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(prefsHdr, 11f);
					SpzUiThemeOps.ApplyBoundChromeTmp(prefsHdr, t.textMuted, hdrBase);
					prefsHdr.fontStyle = FontStyles.Bold;
				}
				ThemePrefsMetaTmp(metaRoot.Find("AddonSummary")?.GetComponent<TextMeshProUGUI>(), t.textMuted, 13f);
				ThemePrefsMetaTmp(metaRoot.Find("AddonVersion")?.GetComponent<TextMeshProUGUI>(), t.textMuted, 12f);
				ThemePrefsMetaTmp(metaRoot.Find("AddonAuthor")?.GetComponent<TextMeshProUGUI>(), t.textMuted, 12f);
				if (prefsBodyT.gameObject.activeSelf) {
					ApplyResponsivePrefsDropdownLayout(prefsBodyT);
					// Theme/responsive pass updates PreferencesBody preferredHeight — keep the AddonItem
					// LayoutElement in sync or the inset card clips under ContentSizeFitter.
					SyncExpandedAddonItemHeight(item, prefsBodyT);
				} else
					LockPreferencesBodyLayout(prefsBodyT);
			}
			var ribbonToggle = FindChildRecursive(item.transform, "ShowInRibbonToggle")?.GetComponent<Toggle>();
			if (ribbonToggle != null && ribbonToggle.gameObject.activeSelf) {
				ThemeShowInRibbonDial(ribbonToggle, ribbonToggle.isOn, t.success, t.textMuted, t.success);
			}
			// Keep status dial square after any theme pass (layout smash otherwise elongates CircleFilled).
			if (toggle != null)
				LockStatusDialLayout(toggle);
		}

		static Transform FindChildRecursive(Transform root, string childName) {
			if (root == null || string.IsNullOrEmpty(childName))
				return null;
			if (string.Equals(root.name, childName, StringComparison.Ordinal))
				return root;
			for (int i = 0; i < root.childCount; i++) {
				var found = FindChildRecursive(root.GetChild(i), childName);
				if (found != null)
					return found;
			}
			return null;
		}

		void OnAddonEnabledStateChanged(string addonId) {
			// Always mirror live enable into draft — async load-fail must not leave dial ON while isEnabled is false.
			if (!string.IsNullOrEmpty(addonId)
			    && Addon_MGR.instance != null
			    && Addon_MGR.instance.GetAddons().TryGetValue(addonId, out var live)
			    && live != null)
				_draftEnabledById[addonId] = live.isEnabled;
			RecomputeDraftDirtyFromLive();
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
				if (kvp.Value == null) continue;
				bool draftOn = GetDraftEnabled(kvp.Key, kvp.Value.isEnabled);
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
			if (Addon_MGR.instance == null
			    || !Addon_MGR.instance.GetAddons().TryGetValue(addonId, out var info)
			    || info == null)
				return;
			var toggle = item.transform.Find("HeaderRow/StatusToggle")?.GetComponent<Toggle>();
			if (toggle == null)
				toggle = item.transform.Find("StatusToggle")?.GetComponent<Toggle>();
			if (toggle == null) return;
			// Live enable flag changed (save apply / load failure) — sync this id's draft entry.
			_draftEnabledById[addonId] = info.isEnabled;
			bool showOn = info.isEnabled;
			toggle.SetIsOnWithoutNotify(showOn);
			ApplyStatusDialVisual(toggle, showOn);
			var ribbonToggle = FindChildRecursive(item.transform, "ShowInRibbonToggle")?.GetComponent<Toggle>();
			if (ribbonToggle != null) {
				bool ribbonOnly = string.Equals(addonId, Addon_MGR.RibbonOnlyFullscreenAddonId, StringComparison.Ordinal);
				bool showRibbon = !ribbonOnly && Addon_MGR.instance.ShouldShowInCommandRibbon(addonId);
				ribbonToggle.SetIsOnWithoutNotify(showRibbon);
				if (ribbonToggle.gameObject.activeSelf)
					ThemeShowInRibbonDial(ribbonToggle, showRibbon, _statusOk, _statusMuted, _statusOk);
			}
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
				TintStatusDialGraphic(ringImg, ring);
				ringImg.preserveAspect = true;
				ringImg.type = Image.Type.Simple;
			}
			// Prefer manual Checkmark — Toggle.graphic is left null so Unity does not hide it mid-click.
			Image fillImg = toggle.graphic as Image;
			if (fillImg == null)
				fillImg = toggle.transform.Find("Ring/Checkmark")?.GetComponent<Image>();
			if (fillImg != null) {
				UnwindDialFillHiddenForTheme(fillImg);
				TintStatusDialGraphic(fillImg, _statusOk);
				fillImg.preserveAspect = true;
				fillImg.type = Image.Type.Simple;
				fillImg.gameObject.SetActive(true);
				fillImg.enabled = true;
				fillImg.canvasRenderer.SetAlpha(enabled ? 1f : 0f);
			}
			LockStatusDialLayout(toggle);
		}

		/// <summary>
		/// Tint dial/prefs faces without FlattenSlicedChromeFace. Snapshot under Nomad so Restore SPZ can unwind.
		/// </summary>
		static void TintStatusDialGraphic(Graphic graphic, Color color) {
			if (graphic == null) return;
			if (SpzUiThemeOps.ShouldRecolorBoundChrome)
				SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(graphic);
			graphic.color = color;
		}

		/// <summary>
		/// Ensure a real sprite exists before BoundChrome marks the face eligible — otherwise Restore SPZ rewinds null.
		/// </summary>
		static void AssignSolidFaceThenMarkRounded(Image img) {
			if (img == null) return;
			if (img.sprite == null) {
				img.sprite = UiRuntimeSprites.SolidRect;
				img.type = Image.Type.Simple;
			}
			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
		}

		const float ExpandChevronHit = 18f;
		/// <summary>Fixed arrow tint — identical under Nomad and default Addon Manager (not theme textPrimary).</summary>
		static readonly Color ExpandChevronArrowColor = new Color(0.88f, 0.88f, 0.92f, 1f);

		/// <summary>
		/// Prefs expand control: ChevronRight image — 0° = closed (▶), −90° = open (▼).
		/// Theme-agnostic: same sprite, size, rotation, and color in Nomad and default.
		/// </summary>
		static void ApplyExpandChevronVisual(Transform expandT, bool expanded) {
			if (expandT == null) return;
			SpzUiThemeOps.RestoreControlLineIconsUnder(expandT);
			var le = expandT.GetComponent<LayoutElement>();
			if (le != null) {
				SpzUiThemeOps.SnapshotLayoutElementForTheme(le);
				le.preferredWidth = ExpandChevronHit;
				le.minWidth = ExpandChevronHit;
				le.preferredHeight = ExpandChevronHit;
				le.minHeight = ExpandChevronHit;
				le.flexibleWidth = 0f;
				le.flexibleHeight = 0f;
			}
			var rootRt = expandT as RectTransform;
			if (rootRt != null)
				rootRt.sizeDelta = new Vector2(ExpandChevronHit, ExpandChevronHit);

			var face = expandT.GetComponent<Image>();
			if (face != null) {
				face.sprite = UiRuntimeSprites.SolidRect;
				face.type = Image.Type.Simple;
				face.color = Color.clear;
				face.raycastTarget = true;
			}

			// Hide leftover TMP glyph — image arrow is the affordance.
			var legacyText = expandT.Find("Text");
			if (legacyText != null)
				legacyText.gameObject.SetActive(false);

			Transform arrowT = expandT.Find("Arrow");
			if (arrowT == null) {
				var go = new GameObject("Arrow");
				go.transform.SetParent(expandT, false);
				arrowT = go.transform;
				go.AddComponent<RectTransform>();
				go.AddComponent<Image>();
			}
			arrowT.gameObject.SetActive(true);
			var arrowRt = arrowT as RectTransform;
			if (arrowRt != null) {
				arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0.5f, 0.5f);
				arrowRt.pivot = new Vector2(0.5f, 0.5f);
				arrowRt.anchoredPosition = Vector2.zero;
				arrowRt.sizeDelta = new Vector2(14f, 14f);
				// Closed → point right; open → point down.
				arrowRt.localEulerAngles = new Vector3(0f, 0f, expanded ? -90f : 0f);
			}
			var arrowImg = arrowT.GetComponent<Image>();
			if (arrowImg != null) {
				// Nomad HideAuthoredIconsUnder can disable preserveAspect glyphs — force visible again.
				var hidden = arrowImg.GetComponent<SpzUiThemeHiddenGraphic>();
				if (hidden != null) {
					if (Application.isPlaying)
						UnityEngine.Object.Destroy(hidden);
					else
						UnityEngine.Object.DestroyImmediate(hidden);
				}
				arrowImg.sprite = UiRuntimeSprites.GetLineIcon(StudioLineIcon.ChevronRight);
				arrowImg.type = Image.Type.Simple;
				arrowImg.preserveAspect = true;
				arrowImg.raycastTarget = false;
				arrowImg.enabled = true;
				// Do not ApplyLineIconTint — that diverges Nomad iconTint vs default authored color.
				arrowImg.color = ExpandChevronArrowColor;
			}
		}

		/// <summary>
		/// Show-in-Ribbon: clean ring radio (same family as enable dial) — never a filled green plate.
		/// Under Nomad, BoundChrome may hide Checkmark graphics — unwind before showing the ON fill.
		/// </summary>
		static void ThemeShowInRibbonDial(Toggle toggle, bool isOn, Color ringOn, Color ringOff, Color fillOk) {
			if (toggle == null) return;
			LockShowInRibbonDialLayout(toggle);
			var ringImg = toggle.transform.Find("Ring")?.GetComponent<Image>();
			if (ringImg != null) {
				TintStatusDialGraphic(ringImg, isOn ? ringOn : ringOff);
				ringImg.sprite = UiRuntimeSprites.CircleRing;
				ringImg.preserveAspect = true;
				ringImg.type = Image.Type.Simple;
				ringImg.enabled = true;
			}
			Image fill = toggle.graphic as Image;
			if (fill == null)
				fill = toggle.transform.Find("Ring/Checkmark")?.GetComponent<Image>();
			if (fill != null) {
				UnwindDialFillHiddenForTheme(fill);
				fill.sprite = UiRuntimeSprites.CircleFilled;
				fill.preserveAspect = true;
				fill.type = Image.Type.Simple;
				fill.gameObject.SetActive(true);
				fill.enabled = true;
				Color fillColor = fillOk;
				fillColor.a = 1f;
				TintStatusDialGraphic(fill, fillColor);
				// ON = filled center (normal radio); OFF = empty ring only.
				fill.canvasRenderer.SetAlpha(isOn ? 1f : 0f);
			}
			// Hit target stays clear — never paint a green/grey square under the dial.
			if (toggle.targetGraphic is Image hit && hit.transform == toggle.transform) {
				hit.color = Color.clear;
				hit.raycastTarget = true;
			}
		}

		/// <summary>Nomad HideAuthoredGraphicForTheme can leave Checkmark disabled — restore for dial ON state.</summary>
		static void UnwindDialFillHiddenForTheme(Image fill) {
			if (fill == null) return;
			var hidden = fill.GetComponent<SpzUiThemeHiddenGraphic>();
			if (hidden != null) {
				if (Application.isPlaying)
					UnityEngine.Object.Destroy(hidden);
				else
					UnityEngine.Object.DestroyImmediate(hidden);
			}
			fill.enabled = true;
		}

		/// <summary>Nomad-only geometry lock — leave must not re-stamp 28×28 after RestoreBoundChromeUnder.</summary>
		static void LockShowInRibbonDialLayout(Toggle toggle) {
			if (toggle == null) return;
			var le = toggle.GetComponent<LayoutElement>();
			if (le != null) {
				SpzUiThemeOps.SnapshotLayoutElementForTheme(le);
				le.preferredWidth = 28f;
				le.minWidth = 28f;
				le.preferredHeight = 28f;
				le.minHeight = 28f;
				le.flexibleWidth = 0f;
				le.flexibleHeight = 0f;
			}
			var rt = toggle.transform as RectTransform;
			if (rt != null) {
				SpzUiThemeOps.SnapshotToolFaceLayout(rt);
				rt.sizeDelta = new Vector2(28f, 28f);
			}
			var ring = toggle.transform.Find("Ring") as RectTransform;
			if (ring != null) {
				SpzUiThemeOps.SnapshotToolFaceLayout(ring);
				ring.anchorMin = ring.anchorMax = new Vector2(0.5f, 0.5f);
				ring.pivot = new Vector2(0.5f, 0.5f);
				ring.sizeDelta = new Vector2(14f, 14f);
				ring.anchoredPosition = Vector2.zero;
			}
			var checkRt = toggle.transform.Find("Ring/Checkmark") as RectTransform;
			if (checkRt != null) {
				SpzUiThemeOps.SnapshotToolFaceLayout(checkRt);
				checkRt.anchorMin = new Vector2(0.28f, 0.28f);
				checkRt.anchorMax = new Vector2(0.72f, 0.72f);
				checkRt.offsetMin = Vector2.zero;
				checkRt.offsetMax = Vector2.zero;
			}
		}

		static void LockStatusDialLayout(Toggle toggle) {
			// Same geometry lock as Show-in-Ribbon — snapshot LE/RTs for Leave SPZ.
			LockShowInRibbonDialLayout(toggle);
		}

		static void ThemePrefsMetaTmp(TextMeshProUGUI tmp, Color color, float designPt) {
			if (tmp == null) return;
			float basePt = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(tmp, designPt);
			if (SpzUiThemeOps.ShouldRecolorBoundChrome)
				SpzUiThemeOps.ApplyBoundChromeTmp(tmp, color, basePt);
			else
				tmp.fontSize = basePt;
		}

		static void AddPrefsMetaLine(Transform parent, string name, string text, float fontSize, Color color, float height, bool bold) {
			var go = new GameObject(name);
			go.transform.SetParent(parent, false);
			var le = go.AddComponent<LayoutElement>();
			le.preferredHeight = height;
			le.minHeight = Mathf.Min(18f, height);
			le.flexibleWidth = 1f;
			le.flexibleHeight = 0f;
			var tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.text = text ?? "";
			tmp.fontSize = fontSize;
			tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
			tmp.color = color;
			tmp.alignment = TextAlignmentOptions.TopLeft;
			tmp.enableWordWrapping = true;
			tmp.overflowMode = TextOverflowModes.Ellipsis;
			tmp.raycastTarget = false;
		}

		static string FormatAddonVersionDisplay(string version) {
			if (string.IsNullOrWhiteSpace(version)) return "—";
			version = version.Trim();
			return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
		}

		/// <summary>Expanded prefs card: a little under half the manager panel (not full row width).</summary>
		const float PrefsCardWidthFrac = 0.45f;
		const float PrefsCardMinWidth = 220f;
		const float PrefsCardMaxWidth = 420f;

		float ResolvePreferencesCardWidth() {
			float panelW = 800f;
			if (_panel != null) {
				var pr = _panel.GetComponent<RectTransform>();
				if (pr != null && pr.rect.width > 1f)
					panelW = pr.rect.width;
			}
			float w = panelW * PrefsCardWidthFrac;
			return Mathf.Clamp(w, PrefsCardMinWidth, PrefsCardMaxWidth);
		}

		void ApplyPreferencesCardWidthCap(LayoutElement cardLe, float cardW) {
			if (cardLe == null) return;
			SpzUiThemeOps.SnapshotLayoutElementForTheme(cardLe);
			cardLe.flexibleWidth = 0f;
			cardLe.preferredWidth = cardW;
			cardLe.minWidth = Mathf.Min(PrefsCardMinWidth, cardW);
			cardLe.layoutPriority = 2;
		}

		void LockPreferencesBodyLayout(Transform prefsBody) {
			if (prefsBody == null) return;
			var le = prefsBody.GetComponent<LayoutElement>();
			if (le != null) {
				SpzUiThemeOps.SnapshotLayoutElementForTheme(le);
				le.flexibleHeight = 0f;
				le.flexibleWidth = 1f;
			}
			var hlg = prefsBody.GetComponent<HorizontalLayoutGroup>();
			if (hlg != null) {
				hlg.childControlHeight = true;
				hlg.childForceExpandHeight = false;
				hlg.childControlWidth = true;
				// Card owns its width — do not stretch PreferencesCard across the full row.
				hlg.childForceExpandWidth = false;
				hlg.childAlignment = TextAnchor.UpperLeft;
			}
			var card = prefsBody.Find("PreferencesCard");
			if (card != null) {
				ApplyPreferencesCardWidthCap(card.GetComponent<LayoutElement>(), ResolvePreferencesCardWidth());
				var cardVlg = card.GetComponent<VerticalLayoutGroup>();
				if (cardVlg != null) {
					cardVlg.childControlHeight = true;
					cardVlg.childForceExpandHeight = false;
					cardVlg.childControlWidth = true;
					cardVlg.childForceExpandWidth = true;
					cardVlg.childAlignment = TextAnchor.UpperLeft;
				}
			}
			var row = card != null ? card.Find("PrefRow_ShowInRibbon") : prefsBody.Find("PrefRow_ShowInRibbon");
			if (row != null) {
				var rowLe = row.GetComponent<LayoutElement>();
				if (rowLe != null)
					SpzUiThemeOps.SnapshotLayoutElementForTheme(rowLe);
				var rowHlg = row.GetComponent<HorizontalLayoutGroup>();
				if (rowHlg != null) {
					rowHlg.childControlHeight = false;
					rowHlg.childForceExpandHeight = false;
					rowHlg.childControlWidth = true;
					rowHlg.childForceExpandWidth = false;
					rowHlg.childAlignment = TextAnchor.MiddleLeft;
				}
			}
		}

		/// <summary>
		/// Nested Preferences dropdown: inset card, wrap, and row height follow <see cref="ProjectUiScale"/> bands.
		/// </summary>
		void ApplyResponsivePrefsDropdownLayout(Transform prefsBody) {
			if (prefsBody == null) return;
			float panelW = 800f;
			if (_panel != null) {
				var pr = _panel.GetComponent<RectTransform>();
				if (pr != null && pr.rect.width > 1f)
					panelW = pr.rect.width;
			}
			var band = ProjectUiScale.GetBand(panelW);
			bool narrow = band <= ProjectUiScale.Band.Sm;
			int leftGutter = narrow ? 24 : 30;
			int rightGutter = narrow ? 16 : 28;
			int padY = Mathf.RoundToInt(ProjectUiScale.Space(narrow ? 2 : 1));
			float sectionGap = ProjectUiScale.Space(narrow ? 2 : 1);
			float rowH = narrow ? 40f : 32f;

			var bodyLE = prefsBody.GetComponent<LayoutElement>();
			var bodyHlg = prefsBody.GetComponent<HorizontalLayoutGroup>();
			if (bodyHlg != null) {
				// Snapshot design pads before responsive gutters so Restore SPZ / RefreshScaled cannot capture polluted values.
				SpzUiThemeOps.ApplyScaledLayoutGroup(bodyHlg);
				bodyHlg.padding = new RectOffset(leftGutter, rightGutter, 0, 2);
				bodyHlg.childControlHeight = true;
				bodyHlg.childForceExpandHeight = false;
				bodyHlg.childControlWidth = true;
				bodyHlg.childForceExpandWidth = false;
				bodyHlg.childAlignment = TextAnchor.UpperLeft;
			}

			var card = prefsBody.Find("PreferencesCard");
			Transform metaRoot = card != null ? card : prefsBody;
			float cardW = ResolvePreferencesCardWidth();
			if (card != null) {
				var cardLeW = card.GetComponent<LayoutElement>();
				ApplyPreferencesCardWidthCap(cardLeW, cardW);
			}
			var cardVlg = card != null ? card.GetComponent<VerticalLayoutGroup>() : prefsBody.GetComponent<VerticalLayoutGroup>();
			if (cardVlg != null) {
				SpzUiThemeOps.ApplyScaledLayoutGroup(cardVlg);
				cardVlg.padding = new RectOffset(
					Mathf.RoundToInt(ProjectUiScale.Space(2)),
					Mathf.RoundToInt(ProjectUiScale.Space(2)),
					padY,
					padY);
				cardVlg.spacing = sectionGap;
				cardVlg.childControlHeight = true;
				cardVlg.childForceExpandHeight = false;
				cardVlg.childControlWidth = true;
				cardVlg.childForceExpandWidth = true;
				cardVlg.childAlignment = TextAnchor.UpperLeft;
			}

			var header = metaRoot.Find("PrefsDropdownHeader")?.GetComponent<TextMeshProUGUI>();
			if (header != null) {
				var headerLE = header.GetComponent<LayoutElement>();
				if (headerLE != null) {
					SpzUiThemeOps.SnapshotLayoutElementForTheme(headerLE);
					headerLE.preferredHeight = narrow ? 22f : 18f;
					headerLE.minHeight = headerLE.preferredHeight;
				}
				float hdrDesign = narrow ? 12f : 11f;
				float hdrBase = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(header, hdrDesign);
				if (SpzUiThemeOps.ShouldRecolorBoundChrome)
					SpzUiThemeOps.ApplyBoundChromeTmp(header, SpzUiThemeOps.Active.textMuted, hdrBase);
				else
					header.fontSize = hdrBase;
				header.enableWordWrapping = true;
				header.overflowMode = TextOverflowModes.Overflow;
			}

			var summaryLe = metaRoot.Find("AddonSummary")?.GetComponent<LayoutElement>();
			if (summaryLe != null) {
				summaryLe.preferredHeight = narrow ? 44f : 36f;
				summaryLe.minHeight = 28f;
			}

			var row = metaRoot.Find("PrefRow_ShowInRibbon");
			if (row != null) {
				var rowLE = row.GetComponent<LayoutElement>();
				if (rowLE != null) {
					SpzUiThemeOps.SnapshotLayoutElementForTheme(rowLE);
					rowLE.preferredHeight = rowH;
					rowLE.minHeight = rowH;
					rowLE.flexibleWidth = 1f;
				}
				var rowHlg = row.GetComponent<HorizontalLayoutGroup>();
				if (rowHlg != null) {
					rowHlg.spacing = ProjectUiScale.Space(1);
					rowHlg.padding = new RectOffset(0, 0,
						Mathf.RoundToInt(ProjectUiScale.Space(1) * 0.5f),
						Mathf.RoundToInt(ProjectUiScale.Space(1) * 0.5f));
					rowHlg.childControlHeight = false;
					rowHlg.childForceExpandHeight = false;
					rowHlg.childControlWidth = true;
					rowHlg.childForceExpandWidth = false;
					rowHlg.childAlignment = TextAnchor.MiddleLeft;
				}
				var ribbonDial = row.Find("ShowInRibbonToggle")?.GetComponent<Toggle>();
				if (ribbonDial != null)
					LockShowInRibbonDialLayout(ribbonDial);
				var label = row.Find("ShowInRibbonLabel")?.GetComponent<TextMeshProUGUI>();
				if (label != null) {
					var labelLE = label.GetComponent<LayoutElement>();
					if (labelLE != null) {
						labelLE.flexibleWidth = 1f;
						labelLE.minWidth = narrow ? 120f : 140f;
						labelLE.preferredHeight = rowH - 8f;
					}
					label.enableWordWrapping = false;
					label.overflowMode = TextOverflowModes.Ellipsis;
					const float labelDesign = 13f;
					float labelBase = SpzUiThemeOps.ResolveOrCaptureDesignFontPt(label, labelDesign);
					if (SpzUiThemeOps.ShouldRecolorBoundChrome)
						SpzUiThemeOps.ApplyBoundChromeTmp(label, SpzUiThemeOps.Active.textMuted, labelBase);
					else
						label.fontSize = labelBase;
				}
			}

			if (card != null) {
				var cardLe = card.GetComponent<LayoutElement>();
				if (cardLe != null) {
					float cardH = MeasurePreferencesBodyHeight(card);
					cardLe.preferredHeight = cardH;
					cardLe.minHeight = cardH;
					ApplyPreferencesCardWidthCap(cardLe, cardW);
				}
			}
			float bodyH = MeasurePreferencesBodyHeight(prefsBody);
			if (bodyLE != null) {
				bodyLE.preferredHeight = bodyH;
				bodyLE.minHeight = bodyH;
				bodyLE.flexibleHeight = 0f;
				bodyLE.flexibleWidth = 1f;
			}
		}

		static float MeasurePreferencesBodyHeight(Transform prefsBody) {
			if (prefsBody == null) return 36f;
			var vlg = prefsBody.GetComponent<VerticalLayoutGroup>();
			var hlg = prefsBody.GetComponent<HorizontalLayoutGroup>();
			float pad = 0f;
			float spacing = 0f;
			if (vlg != null) {
				pad = vlg.padding.top + vlg.padding.bottom;
				spacing = vlg.spacing;
			} else if (hlg != null) {
				pad = hlg.padding.top + hlg.padding.bottom;
			}
			float rows = 0f;
			int active = 0;
			for (int i = 0; i < prefsBody.childCount; i++) {
				var ch = prefsBody.GetChild(i);
				if (ch == null || !ch.gameObject.activeSelf) continue;
				var le = ch.GetComponent<LayoutElement>();
				float h = le != null && le.preferredHeight > 0f ? le.preferredHeight : 24f;
				if (hlg != null)
					rows = Mathf.Max(rows, h);
				else {
					rows += h;
					active++;
				}
			}
			if (vlg != null && active > 1)
				rows += spacing * (active - 1);
			return Mathf.Max(36f, pad + rows);
		}

		/// <summary>After prefs body height changes (expand / theme / responsive), sync the parent AddonItem LE + rect.</summary>
		static void SyncExpandedAddonItemHeight(GameObject item, Transform prefsBody) {
			if (item == null || prefsBody == null) return;
			var itemLe = item.GetComponent<LayoutElement>();
			var headerLe = item.transform.Find("HeaderRow")?.GetComponent<LayoutElement>();
			var bodyLe = prefsBody.GetComponent<LayoutElement>();
			var vlg = item.GetComponent<VerticalLayoutGroup>();
			if (itemLe == null || headerLe == null || bodyLe == null || vlg == null) return;
			float bodyH = bodyLe.preferredHeight > 0f ? bodyLe.preferredHeight : MeasurePreferencesBodyHeight(prefsBody);
			float h = headerLe.preferredHeight
				+ bodyH
				+ vlg.spacing
				+ vlg.padding.top
				+ vlg.padding.bottom;
			itemLe.preferredHeight = h;
			itemLe.minHeight = h;
			var itemRt = item.GetComponent<RectTransform>();
			if (itemRt != null)
				itemRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
		}

		void ReapplyAuthoredStatusDialsAfterThemeRestore() {
			foreach (var item in _addonUIItems.Values) {
				if (item == null) continue;
				var header = item.transform.Find("HeaderRow");
				var toggle = (header != null ? header.Find("StatusToggle") : null)?.GetComponent<Toggle>();
				if (toggle != null)
					ApplyStatusDialVisual(toggle, toggle.isOn);
				// Show-in-Ribbon dials also TintStatusDialGraphic under Nomad — restore must re-tint or green sticks.
				var ribbonToggle = FindChildRecursive(item.transform, "ShowInRibbonToggle")?.GetComponent<Toggle>();
				if (ribbonToggle != null && ribbonToggle.gameObject.activeSelf)
					ThemeShowInRibbonDial(ribbonToggle, ribbonToggle.isOn, _statusOk, _statusMuted, _statusOk);
			}
		}
		
		void CreateAddonListItem(string addonId, Addon_MGR.AddonInfo addonInfo) {
			if (_addonsListParent == null) {
				Debug.LogError($"[AddonManager_UI] CreateAddonListItem: _addonsListParent is null for addon {addonId}");
				return;
			}
			if (addonInfo == null) {
				Debug.LogError($"[AddonManager_UI] CreateAddonListItem: AddonInfo null for '{addonId}'");
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
			bool ribbonOnly = string.Equals(addonId, Addon_MGR.RibbonOnlyFullscreenAddonId, StringComparison.Ordinal);
			bool showInRibbon = !ribbonOnly
				&& (Addon_MGR.instance == null || Addon_MGR.instance.ShouldShowInCommandRibbon(addonId));

			var itemObj = new GameObject($"AddonItem_{addonId}");
			itemObj.transform.SetParent(_addonsListParent, false);
			itemObj.layer = _addonsListParent.gameObject.layer;
			itemObj.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 48f);
			var itemLayout = itemObj.AddComponent<LayoutElement>();
			itemLayout.preferredHeight = 48f;
			itemLayout.minHeight = 44f;
			itemLayout.minWidth = 280f;
			itemLayout.flexibleWidth = 1f;
			var verticalLayout = itemObj.AddComponent<VerticalLayoutGroup>();
			// Gap between HeaderRow and PreferencesBody so prefs bg does not clip Preferences/Uninstall.
			verticalLayout.spacing = 8f;
			verticalLayout.padding = new RectOffset(0, 0, 2, 4);
			verticalLayout.childAlignment = TextAnchor.UpperLeft;
			verticalLayout.childControlWidth = true;
			// true: assign HeaderRow / PreferencesBody heights from LayoutElement so prefs nest under the name.
			// HeaderRow HLG stays childControlHeight=false so dials are not stretched into grey bars.
			verticalLayout.childControlHeight = true;
			verticalLayout.childForceExpandWidth = true;
			verticalLayout.childForceExpandHeight = false;

			var headerObj = new GameObject("HeaderRow");
			headerObj.transform.SetParent(itemObj.transform, false);
			headerObj.AddComponent<RectTransform>();
			var headerLE = headerObj.AddComponent<LayoutElement>();
			// Tall enough for 28px buttons + HLG pad without prefs body eating the bottom edge.
			headerLE.preferredHeight = 40f;
			headerLE.minHeight = 38f;
			headerLE.flexibleWidth = 1f;
			headerLE.flexibleHeight = 0f;
			var horizontalLayout = headerObj.AddComponent<HorizontalLayoutGroup>();
			horizontalLayout.spacing = 8f;
			horizontalLayout.padding = new RectOffset(0, 0, 4, 4);
			horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
			horizontalLayout.childControlWidth = true;
			horizontalLayout.childControlHeight = false;
			horizontalLayout.childForceExpandWidth = false;
			horizontalLayout.childForceExpandHeight = false;

			// Arrow button: ChevronRight (▶) collapsed, rotated −90° (▼) when prefs open — no solid square plate.
			const float chevronHit = ExpandChevronHit;
			var expandObj = new GameObject("ExpandChevron");
			expandObj.transform.SetParent(headerObj.transform, false);
			var expandRt = expandObj.AddComponent<RectTransform>();
			expandRt.sizeDelta = new Vector2(chevronHit, chevronHit);
			var expandLE = expandObj.AddComponent<LayoutElement>();
			expandLE.preferredWidth = chevronHit;
			expandLE.minWidth = chevronHit;
			expandLE.preferredHeight = chevronHit;
			expandLE.minHeight = chevronHit;
			expandLE.flexibleWidth = 0f;
			expandLE.flexibleHeight = 0f;
			var expandHit = expandObj.AddComponent<Image>();
			expandHit.sprite = UiRuntimeSprites.SolidRect;
			expandHit.type = Image.Type.Simple;
			expandHit.color = Color.clear;
			expandHit.raycastTarget = true;
			var expandBtn = expandObj.AddComponent<Button>();
			expandBtn.targetGraphic = expandHit;
			expandBtn.transition = Selectable.Transition.ColorTint;
			var expandColors = expandBtn.colors;
			expandColors.normalColor = Color.white;
			expandColors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
			expandColors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
			expandColors.selectedColor = Color.white;
			expandBtn.colors = expandColors;
			ApplyExpandChevronVisual(expandObj.transform, false);
			AttachTooltip(expandObj, "▶ Preferences closed — click to open. ▼ Preferences open — click to close.");

			void SetExpandChevron(bool expanded) {
				ApplyExpandChevronVisual(expandObj.transform, expanded);
				AttachTooltip(expandObj, expanded
					? "▼ Preferences open — click to close details."
					: "▶ Preferences closed — click to open details.");
			}

			var toggleObj = new GameObject("StatusToggle");
			toggleObj.transform.SetParent(headerObj.transform, false);
			var toggleRect = toggleObj.AddComponent<RectTransform>();
			toggleRect.sizeDelta = new Vector2(statusHitPad, statusHitPad);
			var toggleLE = toggleObj.AddComponent<LayoutElement>();
			toggleLE.preferredWidth = statusHitPad;
			toggleLE.minWidth = statusHitPad;
			toggleLE.flexibleWidth = 0f;
			toggleLE.preferredHeight = statusHitPad;
			toggleLE.minHeight = statusHitPad;
			toggleLE.flexibleHeight = 0f;
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
			rowToggle.graphic = null;
			rowToggle.transition = Selectable.Transition.None;
			rowToggle.toggleTransition = Toggle.ToggleTransition.None;
			bool draftOn = GetDraftEnabled(addonId, addonInfo.isEnabled);
			rowToggle.SetIsOnWithoutNotify(draftOn);
			ApplyStatusDialVisual(rowToggle, draftOn);
			AttachTooltip(toggleObj,
				"Enable or disable this add-on. Enabled add-ons load (Python register) and can show a Command Ribbon tab.");

			var nameObj = new GameObject("Name");
			nameObj.transform.SetParent(headerObj.transform, false);
			var nameLE = nameObj.AddComponent<LayoutElement>();
			nameLE.minWidth = 140f;
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

			// Nested Blender-like details under HeaderRow — Uninstall lives inside prefs (not header far-right).
			var prefsBody = new GameObject("PreferencesBody");
			prefsBody.transform.SetParent(itemObj.transform, false);
			prefsBody.AddComponent<RectTransform>();
			var prefsBodyLE = prefsBody.AddComponent<LayoutElement>();
			prefsBodyLE.preferredHeight = 120f;
			prefsBodyLE.minHeight = 72f;
			prefsBodyLE.flexibleHeight = 0f;
			prefsBodyLE.flexibleWidth = 1f;
			var prefsBodyBg = prefsBody.AddComponent<Image>();
			prefsBodyBg.sprite = UiRuntimeSprites.SolidRect;
			prefsBodyBg.type = Image.Type.Simple;
			prefsBodyBg.color = Color.clear;
			prefsBodyBg.raycastTarget = false;
			var prefsBodyHLG = prefsBody.AddComponent<HorizontalLayoutGroup>();
			// Left gutter aligns card under the name (chevron + dial); card width is capped (~half), not full row.
			prefsBodyHLG.padding = new RectOffset(30, 28, 0, 2);
			prefsBodyHLG.spacing = 0;
			prefsBodyHLG.childAlignment = TextAnchor.UpperLeft;
			prefsBodyHLG.childControlWidth = true;
			prefsBodyHLG.childControlHeight = true;
			prefsBodyHLG.childForceExpandWidth = false;
			prefsBodyHLG.childForceExpandHeight = false;
			prefsBody.SetActive(false);

			var prefsCard = new GameObject("PreferencesCard");
			prefsCard.transform.SetParent(prefsBody.transform, false);
			prefsCard.AddComponent<RectTransform>();
			var prefsCardLE = prefsCard.AddComponent<LayoutElement>();
			ApplyPreferencesCardWidthCap(prefsCardLE, ResolvePreferencesCardWidth());
			prefsCardLE.flexibleHeight = 0f;
			var prefsCardBg = prefsCard.AddComponent<Image>();
			prefsCardBg.sprite = UiRuntimeSprites.SolidRect;
			prefsCardBg.type = Image.Type.Simple;
			// Transparent — a filled plate read as a dead giant square with no affordance.
			prefsCardBg.color = Color.clear;
			prefsCardBg.raycastTarget = false;
			var prefsCardVLG = prefsCard.AddComponent<VerticalLayoutGroup>();
			prefsCardVLG.spacing = ProjectUiScale.Space(1);
			prefsCardVLG.padding = new RectOffset(
				Mathf.RoundToInt(ProjectUiScale.Space(2)),
				Mathf.RoundToInt(ProjectUiScale.Space(2)),
				Mathf.RoundToInt(ProjectUiScale.Space(1)),
				Mathf.RoundToInt(ProjectUiScale.Space(1)));
			prefsCardVLG.childAlignment = TextAnchor.UpperLeft;
			prefsCardVLG.childControlWidth = true;
			prefsCardVLG.childControlHeight = true;
			prefsCardVLG.childForceExpandWidth = true;
			prefsCardVLG.childForceExpandHeight = false;

			string summaryText = !string.IsNullOrWhiteSpace(addonInfo.description)
				? addonInfo.description
				: (!string.IsNullOrWhiteSpace(addonInfo.listSubtitle)
					? addonInfo.listSubtitle
					: "Installed add-on.");
			AddPrefsMetaLine(prefsCard.transform, "AddonSummary", summaryText, 13f, new Color(0.78f, 0.78f, 0.82f, 1f), 36f, bold: false);

			var sep = new GameObject("PrefsSeparator");
			sep.transform.SetParent(prefsCard.transform, false);
			var sepLE = sep.AddComponent<LayoutElement>();
			sepLE.preferredHeight = 1f;
			sepLE.minHeight = 1f;
			sepLE.flexibleWidth = 1f;
			var sepImg = sep.AddComponent<Image>();
			sepImg.sprite = UiRuntimeSprites.SolidRect;
			sepImg.type = Image.Type.Simple;
			sepImg.color = new Color(1f, 1f, 1f, 0.08f);
			sepImg.raycastTarget = false;

			string verDisp = FormatAddonVersionDisplay(addonInfo.version);
			string authorDisp = !string.IsNullOrWhiteSpace(addonInfo.author) ? addonInfo.author.Trim() : "—";
			AddPrefsMetaLine(prefsCard.transform, "AddonVersion", "Version:  " + verDisp, 12f, new Color(0.62f, 0.62f, 0.66f, 1f), 18f, bold: false);
			AddPrefsMetaLine(prefsCard.transform, "AddonAuthor", "Author:  " + authorDisp, 12f, new Color(0.62f, 0.62f, 0.66f, 1f), 18f, bold: false);

			var prefsHeaderObj = new GameObject("PrefsDropdownHeader");
			prefsHeaderObj.transform.SetParent(prefsCard.transform, false);
			var prefsHeaderLE = prefsHeaderObj.AddComponent<LayoutElement>();
			prefsHeaderLE.preferredHeight = 18f;
			prefsHeaderLE.minHeight = 18f;
			prefsHeaderLE.flexibleWidth = 1f;
			prefsHeaderLE.flexibleHeight = 0f;
			var prefsHeader = prefsHeaderObj.AddComponent<TextMeshProUGUI>();
			prefsHeader.text = "Host preferences";
			prefsHeader.fontSize = 11f;
			prefsHeader.fontStyle = FontStyles.Bold;
			prefsHeader.color = new Color(0.55f, 0.55f, 0.6f, 1f);
			prefsHeader.alignment = TextAlignmentOptions.MidlineLeft;
			prefsHeader.enableWordWrapping = true;
			prefsHeader.overflowMode = TextOverflowModes.Overflow;
			prefsHeader.raycastTarget = false;

			var prefRow = new GameObject("PrefRow_ShowInRibbon");
			prefRow.transform.SetParent(prefsCard.transform, false);
			prefRow.AddComponent<RectTransform>();
			var prefRowLE = prefRow.AddComponent<LayoutElement>();
			prefRowLE.preferredHeight = 32f;
			prefRowLE.minHeight = 28f;
			prefRowLE.flexibleWidth = 1f;
			prefRowLE.flexibleHeight = 0f;
			var prefRowBg = prefRow.AddComponent<Image>();
			prefRowBg.sprite = UiRuntimeSprites.SolidRect;
			prefRowBg.type = Image.Type.Simple;
			prefRowBg.color = Color.clear;
			prefRowBg.raycastTarget = false;
			var prefRowHLG = prefRow.AddComponent<HorizontalLayoutGroup>();
			prefRowHLG.spacing = ProjectUiScale.Space(1);
			prefRowHLG.padding = new RectOffset(0, 0, 2, 2);
			prefRowHLG.childAlignment = TextAnchor.MiddleLeft;
			prefRowHLG.childControlWidth = true;
			prefRowHLG.childControlHeight = false;
			prefRowHLG.childForceExpandWidth = false;
			prefRowHLG.childForceExpandHeight = false;

			// Clean radio dial + label (no filled green plate).
			const float ribbonDialHit = 28f;
			const float ribbonDialSize = 14f;
			var ribbonToggleObj = new GameObject("ShowInRibbonToggle");
			ribbonToggleObj.transform.SetParent(prefRow.transform, false);
			var ribbonToggleRt = ribbonToggleObj.AddComponent<RectTransform>();
			ribbonToggleRt.sizeDelta = new Vector2(ribbonDialHit, ribbonDialHit);
			var ribbonToggleLE = ribbonToggleObj.AddComponent<LayoutElement>();
			ribbonToggleLE.preferredWidth = ribbonDialHit;
			ribbonToggleLE.minWidth = ribbonDialHit;
			ribbonToggleLE.preferredHeight = ribbonDialHit;
			ribbonToggleLE.minHeight = ribbonDialHit;
			ribbonToggleLE.flexibleWidth = 0f;
			ribbonToggleLE.flexibleHeight = 0f;
			var ribbonHit = ribbonToggleObj.AddComponent<Image>();
			ribbonHit.color = Color.clear;
			ribbonHit.raycastTarget = true;
			var ribbonRingObj = new GameObject("Ring");
			ribbonRingObj.transform.SetParent(ribbonToggleObj.transform, false);
			var ribbonRingRt = ribbonRingObj.AddComponent<RectTransform>();
			ribbonRingRt.anchorMin = ribbonRingRt.anchorMax = new Vector2(0.5f, 0.5f);
			ribbonRingRt.pivot = new Vector2(0.5f, 0.5f);
			ribbonRingRt.sizeDelta = new Vector2(ribbonDialSize, ribbonDialSize);
			var ribbonRing = ribbonRingObj.AddComponent<Image>();
			ribbonRing.sprite = UiRuntimeSprites.CircleRing;
			ribbonRing.type = Image.Type.Simple;
			ribbonRing.preserveAspect = true;
			ribbonRing.raycastTarget = false;
			var ribbonCheckGo = new GameObject("Checkmark");
			ribbonCheckGo.transform.SetParent(ribbonRingObj.transform, false);
			var ribbonCheckRt = ribbonCheckGo.AddComponent<RectTransform>();
			ribbonCheckRt.anchorMin = new Vector2(0.28f, 0.28f);
			ribbonCheckRt.anchorMax = new Vector2(0.72f, 0.72f);
			ribbonCheckRt.offsetMin = Vector2.zero;
			ribbonCheckRt.offsetMax = Vector2.zero;
			var ribbonCheck = ribbonCheckGo.AddComponent<Image>();
			ribbonCheck.sprite = UiRuntimeSprites.CircleFilled;
			ribbonCheck.type = Image.Type.Simple;
			ribbonCheck.preserveAspect = true;
			ribbonCheck.color = new Color(34f / 255f, 197f / 255f, 94f / 255f, 1f);
			ribbonCheck.raycastTarget = false;
			var ribbonToggle = ribbonToggleObj.AddComponent<Toggle>();
			ribbonToggle.targetGraphic = ribbonHit;
			ribbonToggle.graphic = null;
			ribbonToggle.transition = Selectable.Transition.None;
			ribbonToggle.toggleTransition = Toggle.ToggleTransition.None;
			ribbonToggle.SetIsOnWithoutNotify(showInRibbon);
			ribbonToggle.interactable = !ribbonOnly;
			ThemeShowInRibbonDial(ribbonToggle, showInRibbon, _statusOk, _statusMuted, _statusOk);

			var ribbonLabelObj = new GameObject("ShowInRibbonLabel");
			ribbonLabelObj.transform.SetParent(prefRow.transform, false);
			var ribbonLabelLE = ribbonLabelObj.AddComponent<LayoutElement>();
			ribbonLabelLE.flexibleWidth = 1f;
			ribbonLabelLE.minWidth = 140f;
			ribbonLabelLE.preferredHeight = 28f;
			ribbonLabelLE.flexibleHeight = 0f;
			var ribbonLabel = ribbonLabelObj.AddComponent<TextMeshProUGUI>();
			ribbonLabel.text = ribbonOnly
				? "Viewport Gen Art dock only — no Command Ribbon tab"
				: "Show in Command Ribbon";
			ribbonLabel.fontSize = 13f;
			ribbonLabel.color = new Color(0.78f, 0.78f, 0.82f, 1f);
			ribbonLabel.alignment = TextAlignmentOptions.MidlineLeft;
			ribbonLabel.enableWordWrapping = false;
			ribbonLabel.overflowMode = TextOverflowModes.Ellipsis;
			ribbonLabel.raycastTarget = false;
			if (ribbonOnly) {
				ribbonToggleObj.SetActive(false);
				ribbonLabel.raycastTarget = true;
				AttachTooltip(ribbonLabelObj,
					"RibbonOnlyFullscreen uses the viewport Gen Art dock — it never appears as a Command Ribbon tab.");
			} else {
				AttachTooltip(ribbonToggleObj,
					"When on, an enabled add-on shows a Command Ribbon tab. When off, it stays active but the tab is hidden.");
			}

			var removeBtnObj = new GameObject("RemoveButton");
			removeBtnObj.transform.SetParent(prefsCard.transform, false);
			var removeBtnLE = removeBtnObj.AddComponent<LayoutElement>();
			removeBtnLE.preferredWidth = 92f;
			removeBtnLE.minWidth = 88f;
			removeBtnLE.flexibleWidth = 0f;
			removeBtnLE.preferredHeight = 28f;
			removeBtnLE.minHeight = 28f;
			var removeBtnImage = removeBtnObj.AddComponent<Image>();
			AssignSolidFaceThenMarkRounded(removeBtnImage);
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
			AttachTooltip(removeBtnObj, "Uninstall this add-on from StreamingAssets/Addons (cannot be undone).");

			void SetItemExpandedHeight(bool expanded) {
				const float collapsedH = 48f;
				float h;
				if (!expanded) {
					h = collapsedH;
					itemLayout.preferredHeight = h;
					itemLayout.minHeight = 44f;
					var itemRtCollapsed = itemObj.GetComponent<RectTransform>();
					if (itemRtCollapsed != null)
						itemRtCollapsed.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
				} else {
					ApplyResponsivePrefsDropdownLayout(prefsBody.transform);
					SyncExpandedAddonItemHeight(itemObj, prefsBody.transform);
				}
			}

			expandBtn.onClick.AddListener(() => {
				bool next = !prefsBody.activeSelf;
				// Allow multiple add-ons expanded — do not collapse siblings.
				prefsBody.SetActive(next);
				if (next) {
					ApplyResponsivePrefsDropdownLayout(prefsBody.transform);
					LockPreferencesBodyLayout(prefsBody.transform);
					ThemeShowInRibbonDial(ribbonToggle, ribbonToggle.isOn, _statusOk, _statusMuted, _statusOk);
					LockStatusDialLayout(rowToggle);
				}
				SetExpandChevron(next);
				SetItemExpandedHeight(next);
				RebuildAddonListScrollLayout(next ? itemObj.transform as RectTransform : null);
			});

			ribbonToggle.onValueChanged.AddListener((isOn) => {
				if (Addon_MGR.instance == null || ribbonOnly)
					return;
				Addon_MGR.instance.SetShowInCommandRibbon(addonId, isOn);
				// Live ribbon pref vs Open snapshot — clear false Close warnings when toggled back.
				RecomputeDraftDirtyFromLive();
				ThemeShowInRibbonDial(ribbonToggle, isOn, _statusOk, _statusMuted, _statusOk);
				bool enabled = GetDraftEnabled(addonId, addonInfo.isEnabled);
				ShowStatus(enabled
					? (isOn
						? $"'{addonId}' — Command Ribbon tab shown. {KeepPrefsHint()}"
						: $"'{addonId}' — active, ribbon hidden. {KeepPrefsHint()}")
					: $"'{addonId}' — ribbon preference updated (enable dial to load).", true);
			});

			rowToggle.onValueChanged.AddListener((isOn) => {
				if (Addon_MGR.instance == null)
					return;
				string id = addonId;
				var map = Addon_MGR.instance.GetAddons();
				if (map.TryGetValue(id, out var info) && info != null && info.isEnabled == isOn) {
					// Already live-synced — do not SetDraftEnabled (that would false-dirty the close warning).
					if (GetDraftEnabled(id, info.isEnabled) != isOn)
						SetDraftEnabled(id, isOn);
					ApplyStatusDialVisual(rowToggle, isOn);
					Addon_MGR.instance.SyncRibbonTabWithEnabledState(id);
					return;
				}
				_suppressEnabledListRefresh = true;
				try {
					SetDraftEnabled(id, isOn);
					if (isOn)
						Addon_MGR.instance.EnableAddon(id);
					else
						Addon_MGR.instance.DisableAddon(id);
					ApplyStatusDialVisual(rowToggle, isOn);
					RefreshStatusCountsOnly();
					bool showRibbon = Addon_MGR.instance.ShouldShowInCommandRibbon(id);
					ShowStatus(isOn
						? (ribbonOnly
							? $"Enabled '{id}' — viewport dock on. {KeepNextLaunchHint()}"
							: showRibbon
								? $"Enabled '{id}' — ribbon tab on. {KeepNextLaunchHint()}"
								: $"Enabled '{id}' — active, ribbon hidden. {KeepNextLaunchHint()}")
						: (ribbonOnly
							? $"Disabled '{id}' — viewport dock off. {KeepNextLaunchHint()}"
							: $"Disabled '{id}' — unloaded. {KeepNextLaunchHint()}"), true);
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
				Canvas popupCanvas = ConfirmPopup_UI.instance.GetComponentInParent<Canvas>();
				int prevSort = popupCanvas != null ? popupCanvas.sortingOrder : 0;
				bool prevOverride = popupCanvas != null && popupCanvas.overrideSorting;
				if (popupCanvas != null) {
					popupCanvas.overrideSorting = true;
					popupCanvas.sortingOrder = AddonManagerCanvasSortOrder + 100;
				}
				ConfirmPopup_UI.instance.Show(
					$"Remove add-on '{addonId}'?\n\nThis cannot be undone.",
					() => {
						RestoreConfirmPopupSort(popupCanvas, prevSort, prevOverride);
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
					() => RestoreConfirmPopupSort(popupCanvas, prevSort, prevOverride)
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

		static void RestoreConfirmPopupSort(Canvas popupCanvas, int prevSort, bool prevOverride) {
			if (popupCanvas == null) return;
			popupCanvas.sortingOrder = prevSort;
			popupCanvas.overrideSorting = prevOverride;
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
		/// <summary>
		/// Rebuild content + viewport so ScrollRect bounds match the list, then optionally scroll
		/// <paramref name="ensureVisible"/> into the viewport (expanded Preferences near the bottom).
		/// </summary>
		void RebuildAddonListScrollLayout(RectTransform ensureVisible) {
			if (_addonsListParent == null) return;
			if (ensureVisible != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(ensureVisible);
			LayoutRebuilder.ForceRebuildLayoutImmediate(_addonsListParent);
			var scroll = _addonsListParent.GetComponentInParent<ScrollRect>();
			if (scroll != null && scroll.viewport != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
			Canvas.ForceUpdateCanvases();
			if (ensureVisible != null)
				EnsureAddonItemVisibleInScroll(ensureVisible);
		}

		/// <summary>Scroll the add-on list so <paramref name="item"/> is fully inside the viewport.</summary>
		void EnsureAddonItemVisibleInScroll(RectTransform item) {
			if (item == null || _addonsListParent == null) return;
			var scroll = _addonsListParent.GetComponentInParent<ScrollRect>();
			if (scroll == null || scroll.viewport == null || scroll.content == null) return;
			RectTransform viewport = scroll.viewport;
			Bounds itemBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
			float viewH = viewport.rect.height;
			if (viewH < 1f) return;
			float contentH = scroll.content.rect.height;
			float overflow = contentH - viewH;
			if (overflow <= 1f) {
				scroll.verticalNormalizedPosition = 1f;
				return;
			}
			// itemBounds is in viewport space; yMax above 0 / yMin below -viewH means clipped.
			float pad = 8f;
			float delta = 0f;
			if (itemBounds.max.y > -pad)
				delta = itemBounds.max.y + pad;
			else if (itemBounds.min.y < -viewH + pad)
				delta = itemBounds.min.y + viewH - pad;
			if (Mathf.Abs(delta) < 0.5f) return;
			Vector2 pos = scroll.content.anchoredPosition;
			pos.y = Mathf.Clamp(pos.y - delta, 0f, overflow);
			scroll.content.anchoredPosition = pos;
			scroll.StopMovement();
		}

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
