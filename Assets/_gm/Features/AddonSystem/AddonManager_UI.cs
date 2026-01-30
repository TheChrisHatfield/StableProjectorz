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
			
			if (_panel != null) {
				_panel.SetActive(false);
			}
		}
		
		/// <summary>
		/// Opens the add-on manager panel
		/// </summary>
		public void OpenPanel() {
			if (_panel != null) {
				_panel.SetActive(true);
				RefreshAddonsList();
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
	}
}
