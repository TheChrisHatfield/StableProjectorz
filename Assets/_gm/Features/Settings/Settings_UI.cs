using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

namespace spz {

	// Holds sliders, buttons, which are used by the other script, Settings_MGR.
	public class Settings_UI : MonoBehaviour
	{
	    [SerializeField] Button _openHelpSettingsPanel_button;
	    [SerializeField] Button _openSettingsPanel_button;
	    [SerializeField] GameObject _settingsPanel_go;
	    [SerializeField] ScrollRect_AutoScroll _autoScroll;
	    [Space(10)]
	    [SerializeField] ColorPalette_Panel_UI _settings_colorPicker;
	    [Space(10)]
	    [SerializeField] IntegerInputField _targetFrameRate_input;
	    [Tooltip("Stable Diffusion GPU index: -1 = default/auto, 0/1/2 = use that GPU. Set Min=-1 in inspector.")]
	    [SerializeField] IntegerInputField _sdGpuDeviceId_input;
	    [FormerlySerializedAs("_brushPrecision_2048_toggle")][SerializeField] Toggle _brushPrecision_4k_toggle;
	    [SerializeField] Toggle _prompt_textHighlight_toggle;
	    [SerializeField] Toggle _alwaysFocusCameraPivot;
	    [SerializeField] Toggle _show_cameraInfoText_toggle;
	    [SerializeField] Toggle _enableTooltips_toggle;
	    [SerializeField] Toggle _avoid_NSFW_generations_toggle;
	    [SerializeField] Toggle _viewport_in_center_toggle;
	    [SerializeField] Toggle _viewport_isSwapVerticalRibbons_toggle;
	    [SerializeField] Button _wireframeColor_button;
	    [SerializeField] SliderUI_Snapping _prompt_textSize_slider;
	    [SerializeField] SliderUI_Snapping _wireframeOpacity_slider;
	    [SerializeField] SliderUI_Snapping _shadowR_chunkSize_slider;
	    [SerializeField] TextMeshProUGUI _shadowR_chunkSize_descript;
	    [SerializeField] Button _restoreDefaults_button;
	    [SerializeField] Button _openAddonManager_button;
	    [SerializeField] SliderUI_Snapping _uvWarpSpeed_slider;
	    [Space(10)]
	    [SerializeField] SliderUI_Snapping _bgNoiseSpeed_slider;
	    [SerializeField] Button _noiseColor_button;
	    [Space(10)]
	    [SerializeField] Toggle _layout_askServerOften_toggle;//helpful if we are developing a ui layout txt document.
	    [SerializeField] Toggle _useCtrlScroll_WorkflowMode_swaps_toggle;//ProjMask ->Color -> No Color.
	    [SerializeField] Toggle _ignoreCtrl_if_clickSelectMeshes_toggle;//holding ctrl will not activate the 'ClickSelect_Meshes mode'.

	    void Start(){
	        EnsureSDGpuRowExists();
	        // Buttons
	        EventsBinder.Bind_Clickable_to_event("Settings:OpenHelpSettingsPanel", _openHelpSettingsPanel_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OpenSettingsPanel", _openSettingsPanel_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_WireframeColor", _wireframeColor_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_NoiseColor", _noiseColor_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_RestoreDefaults", _restoreDefaults_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_OpenAddonManager", _openAddonManager_button);
	        
	        // Force center alignment for Add-on Manager button text
	        if (_openAddonManager_button != null) {
	            var text = _openAddonManager_button.GetComponentInChildren<TMPro.TextMeshProUGUI>();
	            if (text != null) {
	                text.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
	                text.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
	            }
	        }

	        // Toggles
	        EventsBinder.Bind_Clickable_to_event("Settings:set_brushPrecision_res", _brushPrecision_4k_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_prompt_textHighlight", _prompt_textHighlight_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_isAlwaysFocusCameraPivot", _alwaysFocusCameraPivot);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_isShow_CameraInfoText", _show_cameraInfoText_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_isAllowTooltips", _enableTooltips_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_avoid_NSFW_generations", _avoid_NSFW_generations_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_viewport_in_center", _viewport_in_center_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_viewport_isSwapVerticalRibbons", _viewport_isSwapVerticalRibbons_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_layout_askServerOften", _layout_askServerOften_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_useCtrlScroll_for_WorkflowMode_swaps", _useCtrlScroll_WorkflowMode_swaps_toggle);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_ignoreCtrl_if_clickSelectingMeshes", _ignoreCtrl_if_clickSelectMeshes_toggle);

	        // Custom Sliders
	        EventsBinder.Bind_Clickable_to_event("Settings:set_prompt_textSize", _prompt_textSize_slider);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_wireframeOpacity", _wireframeOpacity_slider);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_ShadowR_chunkSize", _shadowR_chunkSize_slider);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_uvWarpSpeed01", _uvWarpSpeed_slider);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_noiseSpeed", _bgNoiseSpeed_slider);

	        // Custom Input Fields
	        EventsBinder.Bind_Clickable_to_event("Settings:set_targetFrameRate", _targetFrameRate_input);
	        if (_sdGpuDeviceId_input != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_sdGpuDeviceId", _sdGpuDeviceId_input);

	        EventsBinder.Bind_Clickable_to_event("Settings:ColorPicker", _settings_colorPicker);

	        EventsBinder.Bind_Clickable_to_event("Settings:SettingsPanel", _settingsPanel_go);
	        EventsBinder.Bind_Clickable_to_event("Settings:AutoScroll", _autoScroll);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_ShadowR_chunkSize_descript_text", _shadowR_chunkSize_descript);
	    }

	    /// <summary>Creates "SD GPU" row in Settings panel at runtime so it acts as remote control for which GPU Stable Diffusion uses when launched.</summary>
	    void EnsureSDGpuRowExists() {
	        if (_sdGpuDeviceId_input != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;
	        int current = UnityEngine.PlayerPrefs.GetInt("SD_GPU_DeviceId", -1);
	        var row = new GameObject("Row_SD_GPU");
	        row.transform.SetParent(content, false);
	        var rowRect = row.AddComponent<RectTransform>();
	        rowRect.sizeDelta = new Vector2(0, 28f);
	        var rowLayout = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
	        rowLayout.spacing = 8f;
	        rowLayout.padding = new RectOffset(4, 4, 2, 2);
	        rowLayout.childAlignment = TextAnchor.MiddleLeft;
	        rowLayout.childControlWidth = true;
	        rowLayout.childControlHeight = true;
	        rowLayout.childForceExpandWidth = false;
	        rowLayout.childForceExpandHeight = false;
	        var labelGo = new GameObject("Label");
	        labelGo.transform.SetParent(row.transform, false);
	        var labelRect = labelGo.AddComponent<RectTransform>();
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 280f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "SD GPU (-1=default, 0/1/2=index):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        var inputGo = new GameObject("Input");
	        inputGo.transform.SetParent(row.transform, false);
	        var inputRect = inputGo.AddComponent<RectTransform>();
	        var inputLE = inputGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        inputLE.preferredWidth = 60f;
	        inputLE.preferredHeight = 24f;
	        var inputBg = inputGo.AddComponent<UnityEngine.UI.Image>();
	        inputBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
	        var inputField = inputGo.AddComponent<TMPro.TMP_InputField>();
	        var inputText = new GameObject("Text");
	        inputText.transform.SetParent(inputGo.transform, false);
	        var inputTextRect = inputText.AddComponent<RectTransform>();
	        inputTextRect.anchorMin = Vector2.zero;
	        inputTextRect.anchorMax = Vector2.one;
	        inputTextRect.sizeDelta = Vector2.zero;
	        var inputTextTMP = inputText.AddComponent<TMPro.TextMeshProUGUI>();
	        inputTextTMP.fontSize = 14;
	        inputTextTMP.color = Color.white;
	        inputField.textViewport = inputTextRect;
	        inputField.textComponent = inputTextTMP;
	        inputField.text = current.ToString();
	        var intInput = inputGo.AddComponent<IntegerInputField>();
	        intInput.SetInputField(inputField);
	        intInput.SetMin(-1);
	        intInput.SetMax(7);
	        intInput.SetValueWithoutNotify(current.ToString());
	        _sdGpuDeviceId_input = intInput;

	        var restartBtnGo = new GameObject("RestartWebUI_ApplyGPU");
	        restartBtnGo.transform.SetParent(row.transform, false);
	        var restartBtnRect = restartBtnGo.AddComponent<RectTransform>();
	        var restartBtnLE = restartBtnGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        restartBtnLE.preferredWidth = 180f;
	        restartBtnLE.preferredHeight = 24f;
	        var restartBtnImg = restartBtnGo.AddComponent<UnityEngine.UI.Image>();
	        restartBtnImg.color = new Color(0.25f, 0.5f, 0.6f, 1f);
	        restartBtnImg.raycastTarget = true;
	        var restartBtn = restartBtnGo.AddComponent<UnityEngine.UI.Button>();
	        restartBtn.targetGraphic = restartBtnImg;
	        restartBtn.onClick.AddListener(OnRestartWebUI_ApplyGPU);
	        var restartBtnTextGo = new GameObject("Text");
	        restartBtnTextGo.transform.SetParent(restartBtnGo.transform, false);
	        var restartBtnTextRect = restartBtnTextGo.AddComponent<RectTransform>();
	        restartBtnTextRect.anchorMin = Vector2.zero;
	        restartBtnTextRect.anchorMax = Vector2.one;
	        restartBtnTextRect.sizeDelta = Vector2.zero;
	        var restartBtnText = restartBtnTextGo.AddComponent<TMPro.TextMeshProUGUI>();
	        restartBtnText.text = "Restart WebUI (apply GPU)";
	        restartBtnText.fontSize = 12;
	        restartBtnText.color = new Color(0.95f, 0.95f, 0.95f, 1f);
	        restartBtnText.raycastTarget = false;
	    }

	    void OnRestartWebUI_ApplyGPU() {
	        // Commit the GPU input so the value is saved even if the user didn't tab out (IntegerInputField only saves on EndEdit).
	        var gpuInput = EventsBinder.FindComponent<IntegerInputField>("Settings:set_sdGpuDeviceId");
	        if (gpuInput != null)
	            gpuInput.CommitCurrentText();
	        if (LaunchWebUIBatFile.instance != null) {
	            LaunchWebUIBatFile.instance.LaunchWebui_Manually(printStatusText_ifNotFound: true);
	            if (Viewport_StatusText.instance != null)
	                Viewport_StatusText.instance.ShowStatusText("WebUI launching with selected GPU. Previous WebUI closed automatically.", false, 4f, false);
	        }
	    }
	}
}//end namespace
