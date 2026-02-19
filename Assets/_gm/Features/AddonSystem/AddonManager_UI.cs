using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// UI panel for managing add-ons (install, enable/disable, remove)
	/// Similar to Blender's add-on preferences panel
	/// </summary>
	public class AddonManager_UI : MonoBehaviour {
		public static AddonManager_UI instance { get; private set; }
		
		[SerializeField] GameObject _panel;
		[SerializeField] Button _openPanel_button;
		[SerializeField] Button _closePanel_button;
		[SerializeField] Button _installFromFile_button;
		[SerializeField] Button _refresh_button;
		[SerializeField] RectTransform _addonsListParent; // Where to place add-on list items
		[SerializeField] GameObject _addonItemPrefab; // Prefab for each add-on in the list
		[SerializeField] TextMeshProUGUI _statusText;
		
		private Dictionary<string, GameObject> _addonUIItems = new Dictionary<string, GameObject>();
		
		// Filter state: 0 = All, 1 = Enabled, 2 = Disabled
		private int _filterState = 0;
		private Toggle _filterAllToggle;
		private Toggle _filterEnabledToggle;
		private Toggle _filterDisabledToggle;
		private GameObject _blocker; // full-screen click blocker, shown/hidden with panel
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
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
		
		// Also register with StaticEvents for Settings menu access
		StaticEvents.SubscribeUnique("AddonManager:OpenPanel", OpenPanel);
		
		// Always ensure panel exists (create if needed)
		if (_panel == null) {
			CreatePanelIfNeeded();
		}
		if (_panel != null) {
			_panel.SetActive(false);
		}
	}
	
	/// <summary>
	/// Creates the UI panel dynamically if it wasn't assigned in the inspector
	/// </summary>
	void CreatePanelIfNeeded() {
		if (_panel != null) return;
		
		// Use a dedicated canvas so the panel is always on top and blocks input (no click-through to viewport)
		const int UILayer = 5; // Unity "UI" layer so Canvas and children render
		GameObject canvasObj = new GameObject("AddonManager_Canvas");
		canvasObj.layer = UILayer;
		Canvas canvas = canvasObj.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 32767; // Topmost so Add-on Manager captures input
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
		blockerImage.color = new Color(0f, 0f, 0f, 0.01f);
		blockerImage.raycastTarget = true;
		var blockerCanvasGroup = blockerObj.AddComponent<CanvasGroup>();
		blockerCanvasGroup.blocksRaycasts = true;
		blockerCanvasGroup.interactable = true;
		blockerObj.SetActive(false);
		
		// Panel as child of blocker so all input is under the blocker
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
		image.raycastTarget = true; // Block clicks from passing through
		
		var canvasGroup = panelObj.AddComponent<CanvasGroup>();
		canvasGroup.blocksRaycasts = true;
		canvasGroup.interactable = true;
		
		// Typographic grid: base unit for consistent spacing (8px grid)
		const float Grid = 8f;
		const float PanelPadding = Grid * 3;   // 24
		const float SectionSpacing = Grid * 2; // 16
		const float RowSpacing = Grid * 1;    // 8
		
		var verticalLayout = panelObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		verticalLayout.spacing = SectionSpacing;
		verticalLayout.padding = new RectOffset((int)PanelPadding, (int)PanelPadding, (int)PanelPadding, (int)PanelPadding);
		verticalLayout.childControlHeight = false;
		verticalLayout.childControlWidth = true;
		verticalLayout.childForceExpandHeight = false;
		verticalLayout.childForceExpandWidth = true;
		
		// Header: title left, Close right — grid padding so title is never covered
		GameObject headerObj = new GameObject("Header");
		headerObj.transform.SetParent(panelObj.transform, false);
		var headerRect = headerObj.AddComponent<RectTransform>();
		headerRect.sizeDelta = new Vector2(0, 48);
		var headerLE = headerObj.AddComponent<LayoutElement>();
		headerLE.preferredHeight = 48;
		headerLE.minHeight = 40;
		var headerLayout = headerObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		headerLayout.childControlWidth = true;
		headerLayout.childControlHeight = true;
		headerLayout.childForceExpandWidth = false;
		headerLayout.childForceExpandHeight = true;
		headerLayout.spacing = Grid * 2;
		headerLayout.padding = new RectOffset(0, 0, 0, 0);
		
		// Title — takes all space left of Close; min width so "Add-on Manager" is never clipped
		GameObject titleObj = new GameObject("Title");
		titleObj.transform.SetParent(headerObj.transform, false);
		var titleRect = titleObj.AddComponent<RectTransform>();
		titleRect.anchorMin = Vector2.zero;
		titleRect.anchorMax = Vector2.one;
		titleRect.sizeDelta = Vector2.zero;
		var titleLE = titleObj.AddComponent<LayoutElement>();
		titleLE.minWidth = 180f;
		titleLE.flexibleWidth = 1;
		var titleText = titleObj.AddComponent<TextMeshProUGUI>();
		titleText.text = "Add-on Manager";
		titleText.fontSize = 22;
		titleText.color = Color.white;
		titleText.fontStyle = FontStyles.Bold;
		titleText.alignment = TextAlignmentOptions.Left;
		titleText.enableWordWrapping = false;
		titleText.overflowMode = TMPro.TextOverflowModes.Overflow;
		titleText.raycastTarget = false;
		
		// Close button — fixed width on the right, never overlaps title
		GameObject closeBtnObj = new GameObject("CloseButton");
		closeBtnObj.transform.SetParent(headerObj.transform, false);
		var closeBtnRect = closeBtnObj.AddComponent<RectTransform>();
		var closeBtnLE = closeBtnObj.AddComponent<LayoutElement>();
		closeBtnLE.preferredWidth = 88f;
		closeBtnLE.minWidth = 72f;
		closeBtnLE.flexibleWidth = 0;
		closeBtnLE.preferredHeight = 32f;
		closeBtnRect.sizeDelta = new Vector2(88, 32);
		
		// Button Image (background)
		var closeBtnImage = closeBtnObj.AddComponent<UnityEngine.UI.Image>();
		closeBtnImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
		closeBtnImage.raycastTarget = true;
		// Don't set sprite - Unity will use default white sprite, which works in IL2CPP
		
		var closeBtn = closeBtnObj.AddComponent<UnityEngine.UI.Button>();
		closeBtn.targetGraphic = closeBtnImage;
		closeBtn.onClick.AddListener(ClosePanel); // Bind immediately
		
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
		closeBtnText.color = new Color(0.11f, 0.11f, 0.11f, 1f); // Dark text like other buttons
		closeBtnText.raycastTarget = false; // Don't block button clicks
		_closePanel_button = closeBtn;
		
		// Button bar — grid spacing
		GameObject buttonBarObj = new GameObject("ButtonBar");
		buttonBarObj.transform.SetParent(panelObj.transform, false);
		var buttonBarRect = buttonBarObj.AddComponent<RectTransform>();
		var buttonBarLE = buttonBarObj.AddComponent<LayoutElement>();
		buttonBarLE.preferredHeight = 40;
		buttonBarLE.minHeight = 36;
		var buttonBarLayout = buttonBarObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		buttonBarLayout.spacing = Grid * 2;
		buttonBarLayout.childControlWidth = false;
		buttonBarLayout.childControlHeight = true;
		buttonBarLayout.childForceExpandWidth = false;
		buttonBarLayout.childForceExpandHeight = true;
		buttonBarLayout.padding = new RectOffset(0, 0, 0, 0);
		
		// Install from File button
		GameObject installBtnObj = new GameObject("InstallButton");
		installBtnObj.transform.SetParent(buttonBarObj.transform, false);
		var installBtnRect = installBtnObj.AddComponent<RectTransform>();
		installBtnRect.sizeDelta = new Vector2(150, 30);
		
		// Button Image (background)
		var installBtnImage = installBtnObj.AddComponent<UnityEngine.UI.Image>();
		installBtnImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
		installBtnImage.raycastTarget = true;
		var installBtn = installBtnObj.AddComponent<UnityEngine.UI.Button>();
		installBtn.targetGraphic = installBtnImage;
		installBtn.onClick.AddListener(OnInstallFromFile); // Bind immediately
		
		var installBtnTextObj = new GameObject("Text");
		installBtnTextObj.transform.SetParent(installBtnObj.transform, false);
		var installBtnTextRect = installBtnTextObj.AddComponent<RectTransform>();
		installBtnTextRect.anchorMin = Vector2.zero;
		installBtnTextRect.anchorMax = Vector2.one;
		installBtnTextRect.sizeDelta = Vector2.zero;
		var installBtnText = installBtnTextObj.AddComponent<TextMeshProUGUI>();
		installBtnText.text = "Install from File";
		installBtnText.fontSize = 14;
		installBtnText.alignment = TextAlignmentOptions.Center;
		installBtnText.color = new Color(0.11f, 0.11f, 0.11f, 1f); // Dark text like other buttons
		installBtnText.raycastTarget = false; // Don't block button clicks
		_installFromFile_button = installBtn;
		
		// Refresh button
		GameObject refreshBtnObj = new GameObject("RefreshButton");
		refreshBtnObj.transform.SetParent(buttonBarObj.transform, false);
		var refreshBtnRect = refreshBtnObj.AddComponent<RectTransform>();
		refreshBtnRect.sizeDelta = new Vector2(100, 30);
		
		// Button Image (background)
		var refreshBtnImage = refreshBtnObj.AddComponent<UnityEngine.UI.Image>();
		refreshBtnImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);
		refreshBtnImage.raycastTarget = true;
		var refreshBtn = refreshBtnObj.AddComponent<UnityEngine.UI.Button>();
		refreshBtn.targetGraphic = refreshBtnImage;
		refreshBtn.onClick.AddListener(RefreshAddonsList); // Bind immediately
		
		var refreshBtnTextObj = new GameObject("Text");
		refreshBtnTextObj.transform.SetParent(refreshBtnObj.transform, false);
		var refreshBtnTextRect = refreshBtnTextObj.AddComponent<RectTransform>();
		refreshBtnTextRect.anchorMin = Vector2.zero;
		refreshBtnTextRect.anchorMax = Vector2.one;
		refreshBtnTextRect.sizeDelta = Vector2.zero;
		var refreshBtnText = refreshBtnTextObj.AddComponent<TextMeshProUGUI>();
		refreshBtnText.text = "Refresh";
		refreshBtnText.fontSize = 14;
		refreshBtnText.alignment = TextAlignmentOptions.Center;
		refreshBtnText.color = new Color(0.11f, 0.11f, 0.11f, 1f); // Dark text like other buttons
		refreshBtnText.raycastTarget = false; // Don't block button clicks
		_refresh_button = refreshBtn;
		
		// Load addons now — request Python to load all enabled addons
		GameObject loadNowBtnObj = new GameObject("LoadAddonsNowButton");
		loadNowBtnObj.transform.SetParent(buttonBarObj.transform, false);
		var loadNowBtnRect = loadNowBtnObj.AddComponent<RectTransform>();
		loadNowBtnRect.sizeDelta = new Vector2(130, 30);
		var loadNowBtnImage = loadNowBtnObj.AddComponent<UnityEngine.UI.Image>();
		loadNowBtnImage.color = new Color(0.25f, 0.45f, 0.25f, 1f);
		loadNowBtnImage.raycastTarget = true;
		var loadNowBtn = loadNowBtnObj.AddComponent<UnityEngine.UI.Button>();
		loadNowBtn.targetGraphic = loadNowBtnImage;
		loadNowBtn.onClick.AddListener(OnLoadAddonsNow);
		var loadNowBtnTextObj = new GameObject("Text");
		loadNowBtnTextObj.transform.SetParent(loadNowBtnObj.transform, false);
		var loadNowBtnTextRect = loadNowBtnTextObj.AddComponent<RectTransform>();
		loadNowBtnTextRect.anchorMin = Vector2.zero;
		loadNowBtnTextRect.anchorMax = Vector2.one;
		loadNowBtnTextRect.sizeDelta = Vector2.zero;
		var loadNowBtnText = loadNowBtnTextObj.AddComponent<TextMeshProUGUI>();
		loadNowBtnText.text = "Load addons now";
		loadNowBtnText.fontSize = 13;
		loadNowBtnText.alignment = TextAlignmentOptions.Center;
		loadNowBtnText.color = new Color(0.95f, 0.95f, 0.95f, 1f);
		loadNowBtnText.raycastTarget = false;
		
		// Run with addons — same pattern as Run_noQuickEdit for WebUI: launch Run_with_Addons.bat then quit
		GameObject runWithAddonsBtnObj = new GameObject("RunWithAddonsButton");
		runWithAddonsBtnObj.transform.SetParent(buttonBarObj.transform, false);
		var runWithAddonsRect = runWithAddonsBtnObj.AddComponent<RectTransform>();
		runWithAddonsRect.sizeDelta = new Vector2(150, 30);
		var runWithAddonsImg = runWithAddonsBtnObj.AddComponent<UnityEngine.UI.Image>();
		runWithAddonsImg.color = new Color(0.2f, 0.5f, 0.6f, 1f);
		runWithAddonsImg.raycastTarget = true;
		var runWithAddonsBtn = runWithAddonsBtnObj.AddComponent<UnityEngine.UI.Button>();
		runWithAddonsBtn.targetGraphic = runWithAddonsImg;
		runWithAddonsBtn.onClick.AddListener(OnRestartWithAddons);
		var runWithAddonsTextObj = new GameObject("Text");
		runWithAddonsTextObj.transform.SetParent(runWithAddonsBtnObj.transform, false);
		var runWithAddonsTextRect = runWithAddonsTextObj.AddComponent<RectTransform>();
		runWithAddonsTextRect.anchorMin = Vector2.zero;
		runWithAddonsTextRect.anchorMax = Vector2.one;
		runWithAddonsTextRect.sizeDelta = Vector2.zero;
		var runWithAddonsText = runWithAddonsTextObj.AddComponent<TextMeshProUGUI>();
		runWithAddonsText.text = "Restart with addons";
		runWithAddonsText.fontSize = 13;
		runWithAddonsText.alignment = TextAlignmentOptions.Center;
		runWithAddonsText.color = new Color(0.95f, 0.95f, 0.95f, 1f);
		runWithAddonsText.raycastTarget = false;
		
		// Filter bar — grid spacing
		GameObject filterBarObj = new GameObject("FilterBar");
		filterBarObj.transform.SetParent(panelObj.transform, false);
		var filterBarRect = filterBarObj.AddComponent<RectTransform>();
		var filterBarLE = filterBarObj.AddComponent<LayoutElement>();
		filterBarLE.preferredHeight = 36;
		filterBarLE.minHeight = 32;
		var filterBarLayout = filterBarObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		filterBarLayout.spacing = Grid * 2;
		filterBarLayout.childControlWidth = false;
		filterBarLayout.childControlHeight = true;
		filterBarLayout.padding = new RectOffset(0, 0, 0, 0);
		
		// Filter label — fixed width for alignment
		var filterLabelObj = new GameObject("FilterLabel");
		filterLabelObj.transform.SetParent(filterBarObj.transform, false);
		var filterLabelRect = filterLabelObj.AddComponent<RectTransform>();
		var filterLabelLE = filterLabelObj.AddComponent<LayoutElement>();
		filterLabelLE.preferredWidth = 48;
		filterLabelLE.minWidth = 40;
		var filterLabelText = filterLabelObj.AddComponent<TextMeshProUGUI>();
		filterLabelText.text = "Filter:";
		filterLabelText.fontSize = 14;
		filterLabelText.color = Color.white;
		filterLabelText.alignment = TextAlignmentOptions.Left;
		filterLabelText.raycastTarget = false;
		
		// Create toggle group for radio buttons (mutually exclusive)
		var toggleGroup = filterBarObj.AddComponent<ToggleGroup>();
		toggleGroup.allowSwitchOff = false; // Radio button behavior
		
		// All filter button (radio) — do not set isOn yet; listener would call RefreshAddonsList() before _addonsListParent exists
		var filterAllObj = CreateFilterToggle("All", filterBarObj.transform, toggleGroup, 0);
		_filterAllToggle = filterAllObj.GetComponent<Toggle>();
		
		// Enabled filter button (radio)
		var filterEnabledObj = CreateFilterToggle("Enabled", filterBarObj.transform, toggleGroup, 1);
		_filterEnabledToggle = filterEnabledObj.GetComponent<Toggle>();
		
		// Disabled filter button (radio)
		var filterDisabledObj = CreateFilterToggle("Disabled", filterBarObj.transform, toggleGroup, 2);
		_filterDisabledToggle = filterDisabledObj.GetComponent<Toggle>();
		
			Debug.Log("[AddonManager_UI] Filter bar created with All/Enabled/Disabled toggles");
			
			// Scroll area: same pattern as 3D GENERATION PANEL BUILDER + Scroll Rect — viewport = scroll container (self), Content = direct child
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
		scrollView.viewport = scrollViewRect; // viewport = self (like 3D panel prefab)
		scrollView.content = null; // set below
		
		// Content = direct child of ScrollView; height grows with addon count so list scrolls when many addons
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
		contentLayout.childForceExpandWidth = true; // rows get full width so grid aligns
		var contentSizeFitter = contentObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
		contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
		contentSizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
		scrollView.content = contentRect;
		_addonsListParent = contentRect;
		Debug.Log($"[AddonManager_UI] Set _addonsListParent to {contentRect.name} (active: {contentRect.gameObject.activeSelf})");
		
		// Set default filter selection after _addonsListParent exists so RefreshAddonsList() in the listener can run
		_filterAllToggle.isOn = true;
		
		// Status row — aligned to typographic grid (same left inset as scroll content = Grid)
		GameObject statusObj = new GameObject("StatusText");
		statusObj.transform.SetParent(panelObj.transform, false);
		var statusRect = statusObj.AddComponent<RectTransform>();
		var statusLE = statusObj.AddComponent<LayoutElement>();
		statusLE.preferredHeight = 32;
		statusLE.minHeight = 28;
		var statusLayout = statusObj.AddComponent<HorizontalLayoutGroup>();
		statusLayout.padding = new RectOffset((int)Grid, 0, (int)Grid, 0);
		statusLayout.childControlWidth = true;
		statusLayout.childControlHeight = true;
		statusLayout.childForceExpandWidth = true;
		statusLayout.childForceExpandHeight = true;
		statusLayout.spacing = 0;
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
		/// Opens the add-on manager panel
		/// </summary>
		public void OpenPanel() {
			Debug.Log("[AddonManager_UI] OpenPanel() called");
			
			// Ensure panel exists and has filter bar (recreate if missing)
			if (_panel == null) {
				Debug.Log("[AddonManager_UI] Panel is null, creating it...");
				CreatePanelIfNeeded();
			} else {
				// Check if filter bar exists (for panels created before filter feature was added)
				var filterBar = _panel.transform.Find("FilterBar");
				if (filterBar == null) {
					Debug.Log("[AddonManager_UI] Panel exists but missing FilterBar, recreating panel...");
					Destroy(_panel);
					_panel = null;
					_addonsListParent = null; // Will be set in CreatePanelIfNeeded
					CreatePanelIfNeeded();
				}
			}
			
			if (_panel != null) {
				Debug.Log($"[AddonManager_UI] Panel found, setting active. Panel name: {_panel.name}, Active: {_panel.activeSelf}");
				if (_blocker != null) _blocker.SetActive(true);
				_panel.SetActive(true);
				
				// Ensure panel canvas is on top so it captures input (no click-through)
				Canvas canvas = _panel.GetComponentInParent<Canvas>();
				if (canvas != null) {
					canvas.sortingOrder = 32767;
					canvas.enabled = true;
				}
				
				// Force layout then enforce scroll container size (HTML: div with height + overflow:auto must have explicit size)
				const float scrollAreaHeight = 280f;
				var panelRect = _panel.GetComponent<RectTransform>();
				if (panelRect != null) {
					LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
					Canvas.ForceUpdateCanvases();
					var scrollViewTr = _panel.transform.Find("ScrollView");
					if (scrollViewTr != null) {
						var scrollViewRect = scrollViewTr.GetComponent<RectTransform>();
						if (scrollViewRect != null) {
							LayoutRebuilder.ForceRebuildLayoutImmediate(scrollViewRect);
							// Ensure scroll container always has height (like CSS height: 280px) so viewport clips and scrolls
							scrollViewRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, scrollAreaHeight);
						}
					}
				}
				
				// Force refresh of addons before showing list
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.RefreshAddons();
					Debug.Log("[AddonManager_UI] Refreshed addon discovery");
				}
				
				Debug.Log($"[AddonManager_UI] About to call RefreshAddonsList, _addonsListParent: {_addonsListParent?.name}");
				RefreshAddonsList();
			} else {
				Debug.LogError("[AddonManager_UI] Failed to open panel: _panel is null and could not be created.");
			}
		}
		
		/// <summary>
		/// Closes the add-on manager panel
		/// </summary>
		public void ClosePanel() {
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
			
			FileBrowser.ShowLoadDialog((paths) => {
				if (paths.Length > 0) {
					InstallAddon(paths[0]);
				}
			}, null, FileBrowser.PickMode.Files, false, null, null, "Install Add-on", "Install");
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
		
		/// <summary>
		/// Creates a filter toggle button (radio button style)
		/// </summary>
		GameObject CreateFilterToggle(string label, Transform parent, ToggleGroup toggleGroup, int filterValue) {
			var toggleObj = new GameObject($"Filter_{label}");
			toggleObj.transform.SetParent(parent, false);
			var toggleRect = toggleObj.AddComponent<RectTransform>();
			toggleRect.sizeDelta = new Vector2(80, 25);
			
			// Toggle background
			var toggleBg = toggleObj.AddComponent<UnityEngine.UI.Image>();
			toggleBg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
			toggleBg.raycastTarget = true;
			
			// Toggle component
			var toggle = toggleObj.AddComponent<Toggle>();
			toggle.group = toggleGroup;
			toggle.targetGraphic = toggleBg;
			
			// Toggle label
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
			
			// Update label color based on toggle state
			toggle.onValueChanged.AddListener((isOn) => {
				labelText.color = isOn ? new Color(0.4f, 1f, 0.4f) : Color.white;
				toggleBg.color = isOn ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
				if (isOn) {
					_filterState = filterValue;
					RefreshAddonsList(); // Refresh when filter changes
				}
			});
			
			// Set initial state
			labelText.color = toggle.isOn ? new Color(0.4f, 1f, 0.4f) : Color.white;
			toggleBg.color = toggle.isOn ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f);
			
			return toggleObj;
		}
		
		/// <summary>
		/// Refreshes the list of add-ons with current filter applied
		/// </summary>
		public void RefreshAddonsList() {
			if (_addonsListParent == null) {
				Debug.LogError("[AddonManager_UI] RefreshAddonsList: _addonsListParent is null! Cannot create items.");
				ShowStatus("Error: List parent not initialized", false);
				return;
			}
			
			Debug.Log($"[AddonManager_UI] RefreshAddonsList: _addonsListParent = {_addonsListParent.name}, active = {_addonsListParent.gameObject.activeSelf}");
			
			// Clear existing items
			foreach (var item in _addonUIItems.Values) {
				if (item != null) {
					Destroy(item);
				}
			}
			_addonUIItems.Clear();
			
			// Get list of add-ons
			if (Addon_MGR.instance == null) {
				ShowStatus("Add-on manager not available", false);
				Debug.LogError("[AddonManager_UI] Addon_MGR.instance is null!");
				return;
			}
			
			var addons = Addon_MGR.instance.GetAddons();
			
			Debug.Log($"[AddonManager_UI] Found {addons.Count} addon(s) in registry, filter: {_filterState}");
			
			if (addons.Count == 0) {
				ShowStatus("No add-ons installed. Add-ons should be in StreamingAssets/Addons/", false);
				Debug.LogWarning("[AddonManager_UI] No addons found. Check StreamingAssets/Addons/ directory.");
				return;
			}
			
			// Filter addons based on current filter state
			var filteredAddons = new List<KeyValuePair<string, Addon_MGR.AddonInfo>>();
			int enabledCount = 0;
			int disabledCount = 0;
			
			foreach (var kvp in addons) {
				if (kvp.Value.isEnabled) enabledCount++;
				else disabledCount++;
				
				// Apply filter
				bool shouldShow = false;
				if (_filterState == 0) { // All
					shouldShow = true;
				} else if (_filterState == 1) { // Enabled
					shouldShow = kvp.Value.isEnabled;
				} else if (_filterState == 2) { // Disabled
					shouldShow = !kvp.Value.isEnabled;
				}
				
				if (shouldShow) {
					filteredAddons.Add(kvp);
				}
			}
			
			Debug.Log($"[AddonManager_UI] After filtering: {filteredAddons.Count} addon(s) to display (filter state: {_filterState})");
			
			// Create UI item for each filtered add-on
			foreach (var kvp in filteredAddons) {
				Debug.Log($"[AddonManager_UI] Creating UI item for addon: {kvp.Key}");
				CreateAddonListItem(kvp.Key, kvp.Value);
			}
			
			// Force layout rebuild so scroll content height updates and list items are visible
			if (_addonsListParent != null) {
				UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_addonsListParent);
				Canvas.ForceUpdateCanvases();
			}
			
			Debug.Log($"[AddonManager_UI] Created {_addonUIItems.Count} UI items");
			
			// Update status message with filter info
			string filterText = _filterState == 0 ? "All" : (_filterState == 1 ? "Enabled" : "Disabled");
			ShowStatus($"Showing {filteredAddons.Count} of {addons.Count} add-on(s) ({enabledCount} enabled, {disabledCount} disabled) - Filter: {filterText}", true);
		}

		void OnAddonEnabledStateChanged(string addonId) {
			RefreshAddonsList();
		}
		
		/// <summary>
		/// Creates a UI item for an add-on in the list
		/// </summary>
		void CreateAddonListItem(string addonId, Addon_MGR.AddonInfo addonInfo) {
			if (_addonsListParent == null) {
				Debug.LogError($"[AddonManager_UI] CreateAddonListItem: _addonsListParent is null for addon {addonId}");
				return;
			}
			
			// Remove existing item if it exists (shouldn't happen, but safety check)
			if (_addonUIItems.ContainsKey(addonId)) {
				var existingItem = _addonUIItems[addonId];
				if (existingItem != null) {
					Destroy(existingItem);
				}
				_addonUIItems.Remove(addonId);
			}
			
			GameObject itemObj;
			
			if (_addonItemPrefab != null) {
				itemObj = Instantiate(_addonItemPrefab, _addonsListParent);
				Debug.Log($"[AddonManager_UI] Created item from prefab for {addonId}");
			} else {
				// Create basic UI item if no prefab
				itemObj = new GameObject($"AddonItem_{addonId}");
				itemObj.transform.SetParent(_addonsListParent, false);
				itemObj.layer = _addonsListParent.gameObject.layer;
				Debug.Log($"[AddonManager_UI] Creating dynamic UI item for {addonId}, parent: {_addonsListParent.name}");
				
				var rectTransform = itemObj.AddComponent<RectTransform>();
				rectTransform.sizeDelta = new Vector2(0, 40);
				var itemLayout = itemObj.AddComponent<LayoutElement>();
				itemLayout.preferredHeight = 40;
				itemLayout.minHeight = 40;
				itemLayout.minWidth = 440f; // ensure row has width so text gets horizontal space
				
				// Grid-style row: [Name 220] [Toggle 120] [Uninstall 90] — fixed widths, grid padding
				var horizontalLayout = itemObj.AddComponent<HorizontalLayoutGroup>();
				horizontalLayout.spacing = 12f;
				horizontalLayout.padding = new RectOffset(8, 6, 8, 6);
				horizontalLayout.childControlWidth = true;
				horizontalLayout.childControlHeight = true;
				horizontalLayout.childForceExpandWidth = false;
				horizontalLayout.childForceExpandHeight = true;
				
				// Column 1: Addon name — fixed width cell, stretch so text has proper boundary
				const float colNameWidth = 220f;
				var nameObj = new GameObject("Name");
				nameObj.transform.SetParent(itemObj.transform, false);
				var nameRect = nameObj.AddComponent<RectTransform>();
				nameRect.anchorMin = Vector2.zero;
				nameRect.anchorMax = Vector2.one;
				nameRect.sizeDelta = Vector2.zero;
				nameRect.offsetMin = nameRect.offsetMax = Vector2.zero;
				var nameLayoutElement = nameObj.AddComponent<LayoutElement>();
				nameLayoutElement.preferredWidth = colNameWidth;
				nameLayoutElement.minWidth = colNameWidth;
				var nameText = nameObj.AddComponent<TextMeshProUGUI>();
				string statusIcon = addonInfo.isEnabled ? "✓" : "○";
				nameText.text = $"{statusIcon} {addonId}";
				nameText.fontSize = 14;
				nameText.color = addonInfo.isEnabled ? new Color(0.4f, 1f, 0.4f) : new Color(0.95f, 0.95f, 0.95f);
				nameText.alignment = TextAlignmentOptions.Left;
				nameText.enableWordWrapping = false;
				nameText.overflowMode = TMPro.TextOverflowModes.Ellipsis;
				nameText.raycastTarget = false;
				
				// Column 2: Toggle container
				const float colToggleWidth = 120f;
				var toggleContainerObj = new GameObject("ToggleContainer");
				toggleContainerObj.transform.SetParent(itemObj.transform, false);
				var toggleContainerRect = toggleContainerObj.AddComponent<RectTransform>();
				var toggleContainerLE = toggleContainerObj.AddComponent<LayoutElement>();
				toggleContainerLE.preferredWidth = colToggleWidth;
				toggleContainerLE.minWidth = colToggleWidth;
				toggleContainerRect.sizeDelta = new Vector2(colToggleWidth, 0);
				var toggleContainerLayout = toggleContainerObj.AddComponent<HorizontalLayoutGroup>();
				toggleContainerLayout.spacing = 5;
				toggleContainerLayout.childControlWidth = false;
				toggleContainerLayout.childControlHeight = true;
				
				// Toggle label — fixed width so text stays horizontal
				var toggleLabelObj = new GameObject("ToggleLabel");
				toggleLabelObj.transform.SetParent(toggleContainerObj.transform, false);
				var toggleLabelRect = toggleLabelObj.AddComponent<RectTransform>();
				toggleLabelRect.anchorMin = Vector2.zero;
				toggleLabelRect.anchorMax = Vector2.one;
				toggleLabelRect.sizeDelta = Vector2.zero;
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
				
				// Toggle switch
				var toggleObj = new GameObject("Toggle");
				toggleObj.transform.SetParent(toggleContainerObj.transform, false);
				var toggleRect = toggleObj.AddComponent<RectTransform>();
				toggleRect.sizeDelta = new Vector2(50, 20);
				
				// Toggle background
				var toggleBg = toggleObj.AddComponent<UnityEngine.UI.Image>();
				toggleBg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				toggleBg.raycastTarget = true;
				
				// Toggle checkmark
				var toggleCheckmarkObj = new GameObject("Checkmark");
				toggleCheckmarkObj.transform.SetParent(toggleObj.transform, false);
				var toggleCheckmarkRect = toggleCheckmarkObj.AddComponent<RectTransform>();
				toggleCheckmarkRect.anchorMin = Vector2.zero;
				toggleCheckmarkRect.anchorMax = Vector2.one;
				toggleCheckmarkRect.sizeDelta = Vector2.zero;
				var toggleCheckmark = toggleCheckmarkObj.AddComponent<UnityEngine.UI.Image>();
				toggleCheckmark.color = new Color(0.2f, 0.8f, 0.2f, 1f);
				
				var toggle = toggleObj.AddComponent<Toggle>();
				toggle.targetGraphic = toggleBg;
				toggle.graphic = toggleCheckmark;
				toggle.isOn = addonInfo.isEnabled; // Set after graphic so visibility is correct
				toggle.onValueChanged.AddListener((_) => {
					if (Addon_MGR.instance == null) {
						Debug.LogWarning("[AddonManager_UI] Addon_MGR.instance is null, cannot enable/disable addon");
						return;
					}
					string id = addonId; // Capture for closure; avoid using item UI refs after refresh
					bool desired = toggle.isOn; // Use actual toggle state after click (reliable; param can be stale)
					var addons = Addon_MGR.instance.GetAddons();
					if (addons.TryGetValue(id, out var info) && info.isEnabled == desired) {
						return; // Already in desired state (e.g. programmatic change or double-fire)
					}
					if (desired) {
						Addon_MGR.instance.EnableAddon(id);
					} else {
						Addon_MGR.instance.DisableAddon(id);
					}
					// Refresh list (recreates all items with correct state). Do not touch toggleLabelText/itemObj
					// after this—they are destroyed; touching them would cause null refs.
					RefreshAddonsList();
				});
				
				// Column 3: Uninstall button — fixed width for grid alignment
				const float colButtonWidth = 90f;
				var removeBtnObj = new GameObject("RemoveButton");
				removeBtnObj.transform.SetParent(itemObj.transform, false);
				var removeBtnRect = removeBtnObj.AddComponent<RectTransform>();
				var removeBtnLE = removeBtnObj.AddComponent<LayoutElement>();
				removeBtnLE.preferredWidth = colButtonWidth;
				removeBtnLE.minWidth = colButtonWidth;
				removeBtnLE.preferredHeight = 30;
				removeBtnRect.sizeDelta = new Vector2(colButtonWidth, 30);
				
				// Button background (reddish for destructive action)
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
				removeBtn.onClick.AddListener(() => {
					OnRemoveAddon(addonId);
				});
			}
			
			// Ensure item is active and visible
			itemObj.SetActive(true);
			var itemRect = itemObj.GetComponent<RectTransform>();
			if (itemRect != null) {
				itemRect.localScale = Vector3.one;
			}
			
			_addonUIItems[addonId] = itemObj;
			Debug.Log($"[AddonManager_UI] Successfully created and registered UI item for {addonId}, active: {itemObj.activeSelf}, parent: {itemObj.transform.parent?.name}");
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
				_statusText.color = isSuccess ? Color.green : Color.red;
			}
			UnityEngine.Debug.Log($"[AddonManager_UI] {message}");
		}
		
		/// <summary>
		/// Cleanup when object is destroyed
		/// </summary>
		void OnDestroy() {
			if (instance != this) return;
			
			// Unsubscribe from StaticEvents to prevent memory leaks
			StaticEvents.Unsubscribe("AddonManager:OpenPanel", OpenPanel);
			
			// Clear instance reference
			instance = null;
		}
	}
}
