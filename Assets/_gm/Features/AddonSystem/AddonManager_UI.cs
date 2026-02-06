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
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
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
		
		// Find or create Canvas - prefer existing UI Canvas
		Canvas canvas = FindObjectOfType<Canvas>();
		if (canvas == null) {
			GameObject canvasObj = new GameObject("AddonManager_Canvas");
			canvas = canvasObj.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 1000; // Ensure it's on top
			canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
			canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
		} else {
			// Use existing canvas but ensure it's on top
			canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 1000);
		}
		
		// Create panel
		GameObject panelObj = new GameObject("AddonManager_Panel");
		panelObj.transform.SetParent(canvas.transform, false);
		_panel = panelObj;
		
		var rectTransform = panelObj.AddComponent<RectTransform>();
		rectTransform.anchorMin = new Vector2(0.2f, 0.2f);
		rectTransform.anchorMax = new Vector2(0.8f, 0.8f);
		rectTransform.sizeDelta = Vector2.zero;
		rectTransform.anchoredPosition = Vector2.zero;
		
		var image = panelObj.AddComponent<UnityEngine.UI.Image>();
		image.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
		image.raycastTarget = true; // Block clicks from passing through
		
		var verticalLayout = panelObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		verticalLayout.spacing = 15f; // Increased spacing for better organization
		verticalLayout.padding = new RectOffset(25, 25, 25, 25); // More padding
		verticalLayout.childControlHeight = false;
		verticalLayout.childControlWidth = true;
		verticalLayout.childForceExpandHeight = false;
		verticalLayout.childForceExpandWidth = true;
		
		// Create header with title and close button
		GameObject headerObj = new GameObject("Header");
		headerObj.transform.SetParent(panelObj.transform, false);
		var headerRect = headerObj.AddComponent<RectTransform>();
		headerRect.sizeDelta = new Vector2(0, 50); // Taller header
		var headerLayout = headerObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		headerLayout.childControlWidth = false;
		headerLayout.childControlHeight = true;
		headerLayout.childForceExpandWidth = false;
		headerLayout.childForceExpandHeight = true;
		headerLayout.spacing = 10f;
		headerLayout.padding = new RectOffset(0, 0, 0, 0);
		
		// Title
		GameObject titleObj = new GameObject("Title");
		titleObj.transform.SetParent(headerObj.transform, false);
		var titleRect = titleObj.AddComponent<RectTransform>();
		titleRect.sizeDelta = new Vector2(0, 0); // Let it expand
		var layoutElement = titleObj.AddComponent<UnityEngine.UI.LayoutElement>();
		layoutElement.flexibleWidth = 1; // Take remaining space
		var titleText = titleObj.AddComponent<TextMeshProUGUI>();
		titleText.text = "Add-on Manager";
		titleText.fontSize = 24; // Larger title
		titleText.color = Color.white;
		titleText.fontStyle = FontStyles.Bold;
		titleText.alignment = TextAlignmentOptions.Left;
		
		// Close button
		GameObject closeBtnObj = new GameObject("CloseButton");
		closeBtnObj.transform.SetParent(headerObj.transform, false);
		var closeBtnRect = closeBtnObj.AddComponent<RectTransform>();
		closeBtnRect.sizeDelta = new Vector2(100, 30);
		
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
		
		// Create button bar
		GameObject buttonBarObj = new GameObject("ButtonBar");
		buttonBarObj.transform.SetParent(panelObj.transform, false);
		var buttonBarRect = buttonBarObj.AddComponent<RectTransform>();
		buttonBarRect.sizeDelta = new Vector2(0, 45); // Taller button bar
		var buttonBarLayout = buttonBarObj.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
		buttonBarLayout.spacing = 15f; // More spacing between buttons
		buttonBarLayout.childControlWidth = false;
		buttonBarLayout.childControlHeight = true;
		buttonBarLayout.childForceExpandWidth = false;
		buttonBarLayout.childForceExpandHeight = true;
		buttonBarLayout.padding = new RectOffset(0, 0, 5, 5);
		
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
		
		// Create scroll view for addon list
		GameObject scrollViewObj = new GameObject("ScrollView");
		scrollViewObj.transform.SetParent(panelObj.transform, false);
		var scrollViewRect = scrollViewObj.AddComponent<RectTransform>();
		scrollViewRect.sizeDelta = Vector2.zero;
		var layoutElementScroll = scrollViewObj.AddComponent<UnityEngine.UI.LayoutElement>();
		layoutElementScroll.flexibleHeight = 1; // Take remaining vertical space
		var scrollView = scrollViewObj.AddComponent<UnityEngine.UI.ScrollRect>();
		scrollView.horizontal = false;
		scrollView.vertical = true;
		var scrollViewImage = scrollViewObj.AddComponent<UnityEngine.UI.Image>();
		scrollViewImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
		scrollViewImage.raycastTarget = true; // Block clicks
		
		// Viewport
		GameObject viewportObj = new GameObject("Viewport");
		viewportObj.transform.SetParent(scrollViewObj.transform, false);
		var viewportRect = viewportObj.AddComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.sizeDelta = Vector2.zero;
		viewportRect.anchoredPosition = Vector2.zero;
		var viewportMask = viewportObj.AddComponent<UnityEngine.UI.Mask>();
		viewportMask.showMaskGraphic = false;
		var viewportImage = viewportObj.AddComponent<UnityEngine.UI.Image>();
		viewportImage.color = Color.clear;
		scrollView.viewport = viewportRect;
		
		// Content
		GameObject contentObj = new GameObject("Content");
		contentObj.transform.SetParent(viewportObj.transform, false);
		var contentRect = contentObj.AddComponent<RectTransform>();
		contentRect.anchorMin = new Vector2(0, 1);
		contentRect.anchorMax = new Vector2(1, 1);
		contentRect.pivot = new Vector2(0.5f, 1);
		contentRect.sizeDelta = new Vector2(0, 0);
		contentRect.anchoredPosition = Vector2.zero;
		var contentLayout = contentObj.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		contentLayout.spacing = 5f;
		contentLayout.padding = new RectOffset(10, 10, 10, 10);
		contentLayout.childControlHeight = false;
		contentLayout.childControlWidth = true;
		var contentSizeFitter = contentObj.AddComponent<UnityEngine.UI.ContentSizeFitter>();
		contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
		scrollView.content = contentRect;
		_addonsListParent = contentRect;
		
		// Status text
		GameObject statusObj = new GameObject("StatusText");
		statusObj.transform.SetParent(panelObj.transform, false);
		var statusRect = statusObj.AddComponent<RectTransform>();
		statusRect.sizeDelta = new Vector2(0, 35); // Taller status bar
		var statusText = statusObj.AddComponent<TextMeshProUGUI>();
		statusText.text = "Ready";
		statusText.fontSize = 14; // Larger font
		statusText.color = Color.green;
		statusText.alignment = TextAlignmentOptions.Left;
		_statusText = statusText;
		
		_panel.SetActive(false);
	}
		
		/// <summary>
		/// Opens the add-on manager panel
		/// </summary>
		public void OpenPanel() {
			Debug.Log("[AddonManager_UI] OpenPanel() called");
			
			// Ensure panel exists
			if (_panel == null) {
				Debug.Log("[AddonManager_UI] Panel is null, creating it...");
				CreatePanelIfNeeded();
			}
			
			if (_panel != null) {
				Debug.Log($"[AddonManager_UI] Panel found, setting active. Panel name: {_panel.name}, Active: {_panel.activeSelf}");
				_panel.SetActive(true);
				
				// Ensure panel is on top
				Canvas canvas = _panel.GetComponentInParent<Canvas>();
				if (canvas != null) {
					canvas.sortingOrder = 1000; // Put it on top
					Debug.Log($"[AddonManager_UI] Canvas found, sorting order set to {canvas.sortingOrder}");
				} else {
					Debug.LogWarning("[AddonManager_UI] No Canvas found for panel!");
				}
				
				RefreshAddonsList();
			} else {
				Debug.LogError("[AddonManager_UI] Failed to open panel: _panel is null and could not be created.");
			}
		}
		
		/// <summary>
		/// Closes the add-on manager panel
		/// </summary>
		public void ClosePanel() {
			if (_panel != null) {
				_panel.SetActive(false);
			}
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
		/// Refreshes the list of add-ons
		/// </summary>
		public void RefreshAddonsList() {
			if (_addonsListParent == null) return;
			
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
				return;
			}
			
			var addons = Addon_MGR.instance.GetAddons();
			
			if (addons.Count == 0) {
				ShowStatus("No add-ons installed", true);
				return;
			}
			
			// Create UI item for each add-on
			foreach (var kvp in addons) {
				CreateAddonListItem(kvp.Key, kvp.Value);
			}
			
			ShowStatus($"Found {addons.Count} add-on(s)", true);
		}
		
		/// <summary>
		/// Creates a UI item for an add-on in the list
		/// </summary>
		void CreateAddonListItem(string addonId, Addon_MGR.AddonInfo addonInfo) {
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
			} else {
				// Create basic UI item if no prefab
				itemObj = new GameObject($"AddonItem_{addonId}");
				itemObj.transform.SetParent(_addonsListParent, false);
				
				var rectTransform = itemObj.AddComponent<RectTransform>();
				rectTransform.sizeDelta = new Vector2(0, 40);
				
				var horizontalLayout = itemObj.AddComponent<HorizontalLayoutGroup>();
				horizontalLayout.spacing = 10;
				horizontalLayout.padding = new RectOffset(10, 10, 5, 5);
				horizontalLayout.childControlWidth = false;
				horizontalLayout.childControlHeight = true;
				
				// Add name label
				var nameObj = new GameObject("Name");
				nameObj.transform.SetParent(itemObj.transform, false);
				var nameRect = nameObj.AddComponent<RectTransform>();
				nameRect.sizeDelta = new Vector2(200, 0);
				var nameText = nameObj.AddComponent<TextMeshProUGUI>();
				nameText.text = addonId;
				nameText.fontSize = 14;
				nameText.color = Color.white;
				
				// Add enable/disable toggle
				var toggleObj = new GameObject("Toggle");
				toggleObj.transform.SetParent(itemObj.transform, false);
				var toggleRect = toggleObj.AddComponent<RectTransform>();
				toggleRect.sizeDelta = new Vector2(100, 0);
				var toggle = toggleObj.AddComponent<Toggle>();
				toggle.isOn = addonInfo.isEnabled;
				toggle.onValueChanged.AddListener((enabled) => {
					if (Addon_MGR.instance == null) {
						Debug.LogWarning("[AddonManager_UI] Addon_MGR.instance is null, cannot enable/disable addon");
						return;
					}
					if (enabled) {
						Addon_MGR.instance.EnableAddon(addonId);
					} else {
						Addon_MGR.instance.DisableAddon(addonId);
					}
				});
				
				// Add remove button
				var removeBtnObj = new GameObject("RemoveButton");
				removeBtnObj.transform.SetParent(itemObj.transform, false);
				var removeBtnRect = removeBtnObj.AddComponent<RectTransform>();
				removeBtnRect.sizeDelta = new Vector2(80, 30);
				var removeBtn = removeBtnObj.AddComponent<Button>();
				var removeBtnText = new GameObject("Text");
				removeBtnText.transform.SetParent(removeBtnObj.transform, false);
				var removeBtnTextRect = removeBtnText.AddComponent<RectTransform>();
				removeBtnTextRect.anchorMin = Vector2.zero;
				removeBtnTextRect.anchorMax = Vector2.one;
				removeBtnTextRect.sizeDelta = Vector2.zero;
				var removeBtnTextComp = removeBtnText.AddComponent<TextMeshProUGUI>();
				removeBtnTextComp.text = "Remove";
				removeBtnTextComp.fontSize = 12;
				removeBtnTextComp.alignment = TextAlignmentOptions.Center;
				removeBtn.onClick.AddListener(() => {
					OnRemoveAddon(addonId);
				});
			}
			
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
