using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	/// <summary>
	/// Manages dynamic UI creation for add-ons.
	/// Creates panels, buttons, and other UI elements requested by Python add-ons.
	/// </summary>
	public class AddonUI_MGR : MonoBehaviour {
		public static AddonUI_MGR instance { get; private set; }
		
		[SerializeField] RectTransform _addonPanelsParent; // Where to place add-on panels
		[SerializeField] GameObject _panelPrefab; // Generic panel prefab
		[SerializeField] GameObject _buttonPrefab; // Generic button prefab
		
		// Registry of UI elements by add-on ID
		private Dictionary<string, List<GameObject>> _addonUIElements = new Dictionary<string, List<GameObject>>();
		
		// Callback registry for button clicks
		private Dictionary<string, Action> _buttonCallbacks = new Dictionary<string, Action>();
		
		// Registry of UI element values by element ID
		private Dictionary<string, object> _uiElementValues = new Dictionary<string, object>();
		
		// Registry of UI element references by element ID
		private Dictionary<string, Component> _uiElementComponents = new Dictionary<string, Component>();
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
		
		/// <summary>
		/// Creates a panel for an add-on
		/// </summary>
		public string CreatePanel(string addonId, string title) {
			if (_addonPanelsParent == null) {
				// Try to find the right panel
				var commandRibbon = CommandRibbon_UI.instance;
				if (commandRibbon != null) {
					// Find a suitable parent in the right panel
					// This is a placeholder - you may need to adjust based on actual UI structure
					var rightPanel = GameObject.Find("UI_Global_Right_Panel");
					if (rightPanel != null) {
						var canvas = rightPanel.GetComponentInChildren<Canvas>();
						if (canvas != null) {
							_addonPanelsParent = canvas.transform as RectTransform;
						}
					}
				}
				
				if (_addonPanelsParent == null) {
					UnityEngine.Debug.LogError("[AddonUI_MGR] No parent found for add-on panels");
					return null;
				}
			}
			
			// Create panel GameObject
			GameObject panelObj;
			if (_panelPrefab != null) {
				panelObj = Instantiate(_panelPrefab, _addonPanelsParent);
			} else {
				// Create basic panel if no prefab
				panelObj = new GameObject($"AddonPanel_{addonId}_{title}");
				panelObj.transform.SetParent(_addonPanelsParent, false);
				
				var rectTransform = panelObj.AddComponent<RectTransform>();
				rectTransform.anchorMin = new Vector2(0, 0);
				rectTransform.anchorMax = new Vector2(1, 1);
				rectTransform.sizeDelta = Vector2.zero;
				
				var image = panelObj.AddComponent<Image>();
				image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
				
				var verticalLayout = panelObj.AddComponent<VerticalLayoutGroup>();
				verticalLayout.spacing = 10f;
				verticalLayout.padding = new RectOffset(10, 10, 10, 10);
				verticalLayout.childControlHeight = false;
				verticalLayout.childControlWidth = true;
			}
			
			// Set title if panel has a text component
			var titleText = panelObj.GetComponentInChildren<TextMeshProUGUI>();
			if (titleText == null) {
				// Try to find or create title
				var titleObj = new GameObject("Title");
				titleObj.transform.SetParent(panelObj.transform, false);
				titleText = titleObj.AddComponent<TextMeshProUGUI>();
				titleText.text = title;
				titleText.fontSize = 18;
			} else {
				titleText.text = title;
			}
			
			// Register with add-on
			if (!_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId] = new List<GameObject>();
			}
			_addonUIElements[addonId].Add(panelObj);
			
			// Register with Addon_MGR
			if (Addon_MGR.instance != null) {
				Addon_MGR.instance.RegisterAddonUI(addonId, panelObj);
			}
			
			// Return panel ID (use GameObject instance ID)
			return panelObj.GetInstanceID().ToString();
		}
		
		/// <summary>
		/// Adds a button to a panel
		/// </summary>
		public string AddButton(string addonId, string panelId, string label, string callbackName) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Create button
			GameObject buttonObj;
			if (_buttonPrefab != null) {
				buttonObj = Instantiate(_buttonPrefab, panelObj.transform);
			} else {
				// Create basic button if no prefab
				buttonObj = new GameObject($"Button_{label}");
				buttonObj.transform.SetParent(panelObj.transform, false);
				
				var rectTransform = buttonObj.AddComponent<RectTransform>();
				rectTransform.sizeDelta = new Vector2(200, 30);
				
				var image = buttonObj.AddComponent<Image>();
				image.color = new Color(0.3f, 0.3f, 0.3f, 1f);
				
				var button = buttonObj.AddComponent<Button>();
				
				// Add text label
				var textObj = new GameObject("Text");
				textObj.transform.SetParent(buttonObj.transform, false);
				var textRect = textObj.AddComponent<RectTransform>();
				textRect.anchorMin = Vector2.zero;
				textRect.anchorMax = Vector2.one;
				textRect.sizeDelta = Vector2.zero;
				
				var text = textObj.AddComponent<TextMeshProUGUI>();
				text.text = label;
				text.fontSize = 14;
				text.alignment = TextAlignmentOptions.Center;
				text.color = Color.white;
			}
			
			// Set up button click handler
			var buttonComponent = buttonObj.GetComponent<Button>();
			if (buttonComponent != null) {
				string callbackId = $"{addonId}_{callbackName}";
				buttonComponent.onClick.AddListener(() => {
					if (_buttonCallbacks.ContainsKey(callbackId)) {
						_buttonCallbacks[callbackId]?.Invoke();
					} else {
						// Send callback to Python server
						SendCallbackToPython(addonId, callbackName);
					}
				});
			}
			
			// Register with add-on
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(buttonObj);
			}
			
			return buttonObj.GetInstanceID().ToString();
		}
		
		/// <summary>
		/// Registers a callback function for a button
		/// </summary>
		public void RegisterButtonCallback(string addonId, string callbackName, Action callback) {
			string callbackId = $"{addonId}_{callbackName}";
			_buttonCallbacks[callbackId] = callback;
		}
		
		/// <summary>
		/// Sends a callback event to the Python server
		/// </summary>
		void SendCallbackToPython(string addonId, string callbackName) {
			// This will be handled by the socket server
			// For now, just log it
			UnityEngine.Debug.Log($"[AddonUI_MGR] Callback: {addonId}.{callbackName}");
		}
		
		/// <summary>
		/// Finds a UI element by its ID
		/// </summary>
		GameObject FindUIElement(string elementId) {
			int instanceId;
			if (int.TryParse(elementId, out instanceId)) {
				// Search all registered UI elements
				foreach (var elements in _addonUIElements.Values) {
					foreach (var element in elements) {
						if (element != null && element.GetInstanceID() == instanceId) {
							return element;
						}
					}
				}
			}
			return null;
		}
		
		/// <summary>
		/// Adds a slider to a panel
		/// </summary>
		public string AddSlider(string addonId, string panelId, string label, float min, float max, float defaultValue) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Create slider container
			GameObject sliderObj = new GameObject($"Slider_{label}");
			sliderObj.transform.SetParent(panelObj.transform, false);
			
			var sliderRect = sliderObj.AddComponent<RectTransform>();
			sliderRect.sizeDelta = new Vector2(200, 40);
			
			// Add label
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(sliderObj.transform, false);
			var labelRect = labelObj.AddComponent<RectTransform>();
			labelRect.anchorMin = new Vector2(0, 0);
			labelRect.anchorMax = new Vector2(1, 0.5f);
			labelRect.sizeDelta = Vector2.zero;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 12;
			labelText.color = Color.white;
			
			// Add slider
			var sliderObj2 = new GameObject("Slider");
			sliderObj2.transform.SetParent(sliderObj.transform, false);
			var sliderRect2 = sliderObj2.AddComponent<RectTransform>();
			sliderRect2.anchorMin = new Vector2(0, 0.5f);
			sliderRect2.anchorMax = new Vector2(1, 1);
			sliderRect2.sizeDelta = Vector2.zero;
			
			var sliderBg = sliderObj2.AddComponent<Image>();
			sliderBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
			
			var sliderFill = new GameObject("Fill");
			sliderFill.transform.SetParent(sliderObj2.transform, false);
			var fillRect = sliderFill.AddComponent<RectTransform>();
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = new Vector2(0.5f, 1);
			fillRect.sizeDelta = Vector2.zero;
			var fillImage = sliderFill.AddComponent<Image>();
			fillImage.color = new Color(0.3f, 0.6f, 1f, 1f);
			
			var sliderHandle = new GameObject("Handle");
			sliderHandle.transform.SetParent(sliderObj2.transform, false);
			var handleRect = sliderHandle.AddComponent<RectTransform>();
			handleRect.anchorMin = new Vector2(0.5f, 0);
			handleRect.anchorMax = new Vector2(0.5f, 1);
			handleRect.sizeDelta = new Vector2(20, 0);
			var handleImage = sliderHandle.AddComponent<Image>();
			handleImage.color = Color.white;
			
			var slider = sliderObj2.AddComponent<Slider>();
			slider.minValue = min;
			slider.maxValue = max;
			slider.value = defaultValue;
			slider.fillRect = fillRect;
			slider.handleRect = handleRect;
			slider.targetGraphic = handleImage;
			
			// Add value text
			var valueObj = new GameObject("Value");
			valueObj.transform.SetParent(sliderObj.transform, false);
			var valueRect = valueObj.AddComponent<RectTransform>();
			valueRect.anchorMin = new Vector2(0.7f, 0.5f);
			valueRect.anchorMax = new Vector2(1, 1);
			valueRect.sizeDelta = Vector2.zero;
			var valueText = valueObj.AddComponent<TextMeshProUGUI>();
			valueText.text = defaultValue.ToString("F2");
			valueText.fontSize = 12;
			valueText.color = Color.white;
			valueText.alignment = TextAlignmentOptions.Right;
			
			// Update value text when slider changes
			slider.onValueChanged.AddListener((value) => {
				valueText.text = value.ToString("F2");
				string elementId = sliderObj.GetInstanceID().ToString();
				_uiElementValues[elementId] = value;
				SendValueChangeToPython(addonId, elementId, "slider", value);
			});
			
			// Register
			string elementId = sliderObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultValue;
			_uiElementComponents[elementId] = slider;
			
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(sliderObj);
			}
			
			return elementId;
		}
		
		/// <summary>
		/// Adds a text input field to a panel
		/// </summary>
		public string AddTextInput(string addonId, string panelId, string label, string defaultValue) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Create text input container
			GameObject inputObj = new GameObject($"TextInput_{label}");
			inputObj.transform.SetParent(panelObj.transform, false);
			
			var inputRect = inputObj.AddComponent<RectTransform>();
			inputRect.sizeDelta = new Vector2(200, 40);
			
			// Add label
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(inputObj.transform, false);
			var labelRect = labelObj.AddComponent<RectTransform>();
			labelRect.anchorMin = new Vector2(0, 0.5f);
			labelRect.anchorMax = new Vector2(0.3f, 1);
			labelRect.sizeDelta = Vector2.zero;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 12;
			labelText.color = Color.white;
			
			// Add input field
			var fieldObj = new GameObject("InputField");
			fieldObj.transform.SetParent(inputObj.transform, false);
			var fieldRect = fieldObj.AddComponent<RectTransform>();
			fieldRect.anchorMin = new Vector2(0.3f, 0);
			fieldRect.anchorMax = new Vector2(1, 1);
			fieldRect.sizeDelta = Vector2.zero;
			
			var fieldBg = fieldObj.AddComponent<Image>();
			fieldBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
			
			var textObj = new GameObject("Text");
			textObj.transform.SetParent(fieldObj.transform, false);
			var textRect = textObj.AddComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.sizeDelta = Vector2.zero;
			textRect.offsetMin = new Vector2(5, 2);
			textRect.offsetMax = new Vector2(-5, -2);
			var text = textObj.AddComponent<TextMeshProUGUI>();
			text.text = defaultValue;
			text.fontSize = 12;
			text.color = Color.white;
			
			var placeholderObj = new GameObject("Placeholder");
			placeholderObj.transform.SetParent(fieldObj.transform, false);
			var placeholderRect = placeholderObj.AddComponent<RectTransform>();
			placeholderRect.anchorMin = Vector2.zero;
			placeholderRect.anchorMax = Vector2.one;
			placeholderRect.sizeDelta = Vector2.zero;
			placeholderRect.offsetMin = new Vector2(5, 2);
			placeholderRect.offsetMax = new Vector2(-5, -2);
			var placeholder = placeholderObj.AddComponent<TextMeshProUGUI>();
			placeholder.text = label;
			placeholder.fontSize = 12;
			placeholder.color = new Color(0.5f, 0.5f, 0.5f, 1f);
			placeholder.gameObject.SetActive(string.IsNullOrEmpty(defaultValue));
			
			var inputField = fieldObj.AddComponent<TMP_InputField>();
			inputField.textComponent = text;
			inputField.placeholder = placeholder;
			inputField.text = defaultValue;
			
			// Update value when text changes
			inputField.onValueChanged.AddListener((value) => {
				string elementId = inputObj.GetInstanceID().ToString();
				_uiElementValues[elementId] = value;
				SendValueChangeToPython(addonId, elementId, "text", value);
			});
			
			// Register
			string elementId = inputObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultValue;
			_uiElementComponents[elementId] = inputField;
			
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(inputObj);
			}
			
			return elementId;
		}
		
		/// <summary>
		/// Adds a dropdown to a panel
		/// </summary>
		public string AddDropdown(string addonId, string panelId, string label, List<string> options, int defaultIndex) {
			GameObject panelObj = FindUIElement(panelId);
			if (panelObj == null) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Panel {panelId} not found");
				return null;
			}
			
			// Edge case: Empty or null options
			if (options == null || options.Count == 0) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Dropdown requires at least one option");
				return null;
			}
			
			// Edge case: Invalid default index - clamp to valid range
			if (defaultIndex < 0 || defaultIndex >= options.Count) {
				UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Default index {defaultIndex} out of range, clamping to 0");
				defaultIndex = 0;
			}
			
			// Create dropdown container
			GameObject dropdownObj = new GameObject($"Dropdown_{label}");
			dropdownObj.transform.SetParent(panelObj.transform, false);
			
			var dropdownRect = dropdownObj.AddComponent<RectTransform>();
			dropdownRect.sizeDelta = new Vector2(200, 40);
			
			// Add label
			var labelObj = new GameObject("Label");
			labelObj.transform.SetParent(dropdownObj.transform, false);
			var labelRect = labelObj.AddComponent<RectTransform>();
			labelRect.anchorMin = new Vector2(0, 0.5f);
			labelRect.anchorMax = new Vector2(0.3f, 1);
			labelRect.sizeDelta = Vector2.zero;
			var labelText = labelObj.AddComponent<TextMeshProUGUI>();
			labelText.text = label;
			labelText.fontSize = 12;
			labelText.color = Color.white;
			
			// Add dropdown
			var fieldObj = new GameObject("Dropdown");
			fieldObj.transform.SetParent(dropdownObj.transform, false);
			var fieldRect = fieldObj.AddComponent<RectTransform>();
			fieldRect.anchorMin = new Vector2(0.3f, 0);
			fieldRect.anchorMax = new Vector2(1, 1);
			fieldRect.sizeDelta = Vector2.zero;
			
			var fieldBg = fieldObj.AddComponent<Image>();
			fieldBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
			
			var labelObj2 = new GameObject("Label");
			labelObj2.transform.SetParent(fieldObj.transform, false);
			var labelRect2 = labelObj2.AddComponent<RectTransform>();
			labelRect2.anchorMin = Vector2.zero;
			labelRect2.anchorMax = Vector2.one;
			labelRect2.sizeDelta = Vector2.zero;
			labelRect2.offsetMin = new Vector2(10, 2);
			labelRect2.offsetMax = new Vector2(-25, -2);
			var labelText2 = labelObj2.AddComponent<TextMeshProUGUI>();
			labelText2.text = options.Count > defaultIndex ? options[defaultIndex] : "";
			labelText2.fontSize = 12;
			labelText2.color = Color.white;
			
			var arrowObj = new GameObject("Arrow");
			arrowObj.transform.SetParent(fieldObj.transform, false);
			var arrowRect = arrowObj.AddComponent<RectTransform>();
			arrowRect.anchorMin = new Vector2(1, 0);
			arrowRect.anchorMax = new Vector2(1, 1);
			arrowRect.sizeDelta = new Vector2(20, 0);
			arrowRect.anchoredPosition = new Vector2(-10, 0);
			var arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
			arrowText.text = "▼";
			arrowText.fontSize = 10;
			arrowText.color = Color.white;
			arrowText.alignment = TextAlignmentOptions.Center;
			
			var dropdown = fieldObj.AddComponent<TMP_Dropdown>();
			dropdown.captionText = labelText2;
			dropdown.options = new List<TMP_Dropdown.OptionData>();
			foreach (var option in options) {
				dropdown.options.Add(new TMP_Dropdown.OptionData(option));
			}
			dropdown.value = defaultIndex;
			
			// Update value when selection changes
			dropdown.onValueChanged.AddListener((index) => {
				string elementId = dropdownObj.GetInstanceID().ToString();
				_uiElementValues[elementId] = index;
				SendValueChangeToPython(addonId, elementId, "dropdown", index);
			});
			
			// Register
			string elementId = dropdownObj.GetInstanceID().ToString();
			_uiElementValues[elementId] = defaultIndex;
			_uiElementComponents[elementId] = dropdown;
			
			if (_addonUIElements.ContainsKey(addonId)) {
				_addonUIElements[addonId].Add(dropdownObj);
			}
			
			return elementId;
		}
		
		/// <summary>
		/// Gets the value of a UI element
		/// </summary>
		public object GetUIElementValue(string elementId) {
			if (_uiElementValues.ContainsKey(elementId)) {
				return _uiElementValues[elementId];
			}
			return null;
		}
		
		/// <summary>
		/// Sets the value of a UI element (with type safety)
		/// </summary>
		public bool SetUIElementValue(string elementId, object value) {
			if (!_uiElementComponents.ContainsKey(elementId)) return false;
			if (value == null) return false;
			
			var component = _uiElementComponents[elementId];
			
			try {
				if (component is Slider slider) {
					// Type safety: Only accept numeric types
					if (!(value is float || value is int || value is double)) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Cannot set non-numeric value to slider: {value.GetType()}");
						return false;
					}
					float floatValue = Convert.ToSingle(value);
					// Clamp to slider's min/max range
					floatValue = Mathf.Clamp(floatValue, slider.minValue, slider.maxValue);
					slider.value = floatValue;
					_uiElementValues[elementId] = slider.value;
					return true;
				} else if (component is TMP_InputField inputField) {
					// Type safety: Convert to string
					inputField.text = value.ToString();
					_uiElementValues[elementId] = inputField.text;
					return true;
				} else if (component is TMP_Dropdown dropdown) {
					// Type safety: Only accept integer types
					if (!(value is int || value is short || value is byte)) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Cannot set non-integer value to dropdown: {value.GetType()}");
						return false;
					}
					int intValue = Convert.ToInt32(value);
					// Edge case: Clamp to valid range
					if (intValue < 0 || intValue >= dropdown.options.Count) {
						UnityEngine.Debug.LogWarning($"[AddonUI_MGR] Dropdown index {intValue} out of range [0-{dropdown.options.Count-1}], clamping");
						intValue = Mathf.Clamp(intValue, 0, dropdown.options.Count - 1);
					}
					dropdown.value = intValue;
					_uiElementValues[elementId] = dropdown.value;
					return true;
				}
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonUI_MGR] Error setting UI element value: {e.Message}");
				return false;
			}
			
			return false;
		}
		
		/// <summary>
		/// Sends value change event to Python
		/// </summary>
		void SendValueChangeToPython(string addonId, string elementId, string elementType, object value) {
			// This will be handled by the socket server
			// For now, just log it
			UnityEngine.Debug.Log($"[AddonUI_MGR] Value changed: {addonId}.{elementId} ({elementType}) = {value}");
		}
		
		/// <summary>
		/// Destroys all UI elements for an add-on
		/// </summary>
		public void DestroyAddonUI(string addonId) {
			if (!_addonUIElements.ContainsKey(addonId)) return;
			
			foreach (var element in _addonUIElements[addonId]) {
				if (element != null) {
					string elementId = element.GetInstanceID().ToString();
					_uiElementValues.Remove(elementId);
					_uiElementComponents.Remove(elementId);
					Destroy(element);
				}
			}
			
			_addonUIElements.Remove(addonId);
			
			// Remove callbacks
			var keysToRemove = new List<string>();
			foreach (var key in _buttonCallbacks.Keys) {
				if (key.StartsWith($"{addonId}_")) {
					keysToRemove.Add(key);
				}
			}
			foreach (var key in keysToRemove) {
				_buttonCallbacks.Remove(key);
			}
		}
	}
}
