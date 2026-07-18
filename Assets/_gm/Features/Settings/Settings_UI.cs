using System;
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
	    [SerializeField] Toggle _showExternalProcessWindows_toggle; // Optional: if null, runtime row is created.
	    [SerializeField] Toggle _webUiOpenBrowserOnStartup_toggle; // Optional: if null, runtime row is created.
	    [SerializeField] Toggle _sdStrictMaskIsolation_toggle; // Optional: if null, runtime row is created.
	    [SerializeField] Toggle _sdStrictIsolationFlipMask_toggle; // Optional: if null, runtime row is created (next to strict isolation).
	    [SerializeField] Toggle _sdInpaintingMaskInvert_toggle; // Optional: WebUI inpainting_mask_invert=1 (inpaint outside brush mask).
	    [SerializeField] Toggle _useVSync_toggle; // Optional: assign in scene; otherwise created at runtime.
	    [Tooltip("button_inactive from button_active_inactive_horiz; wired on Settings_UI.prefab for runtime toggles.")]
	    [SerializeField] Sprite _settingsToggleFrameSprite;
	    [Tooltip("button_active from button_active_inactive_horiz; checkmark face.")]
	    [SerializeField] Sprite _settingsToggleCheckSprite;
	    bool _paintUndoSettingsRowsCreated;
	    bool _addonManagerClickRegistered;

	    void OnEnable() {
	        EnsureAddonManagerButtonWired();
	    }

	    /// <summary>Wired in OnEnable so the first click after the panel activates cannot happen before Start().</summary>
	    void EnsureAddonManagerButtonWired() {
	        if (_addonManagerClickRegistered) return;
	        if (_openAddonManager_button == null) {
	            foreach (var b in GetComponentsInChildren<Button>(true)) {
	                if (b != null && b.gameObject.name.IndexOf("Add-on Manager", StringComparison.OrdinalIgnoreCase) >= 0) {
	                    _openAddonManager_button = b;
	                    break;
	                }
	            }
	        }
	        if (_openAddonManager_button == null) return;
	        _openAddonManager_button.onClick.AddListener(OnAddonManagerButtonClicked);
	        _addonManagerClickRegistered = true;
	        var text = _openAddonManager_button.GetComponentInChildren<TMPro.TextMeshProUGUI>();
	        if (text != null) {
	            text.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
	            text.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
	        }
	    }

	    void OnAddonManagerButtonClicked() => AddonManager_UI.OpenFromMenu();

	    void Awake(){
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	    }

	    void Start(){
	        EnsureUseVSyncRowExists();
	        EnsureExternalProcessWindowsRowExists();
	        EnsureWebUiBrowserStartupRowExists();
	        EnsureSDStrictMaskIsolationRowExists();
	        EnsureSDStrictIsolationFlipMaskRowExists();
	        EnsureSDInpaintingMaskInvertRowExists();
	        EnsureSDGpuRowExists();
	        EnsurePaintUndoSettingsRowsExist();
	        FixSettingsScrollReadability();
	        ApplyThemeTokens();
	        // Buttons (guard null so binding is safe when reference not assigned in scene)
	        if (_openHelpSettingsPanel_button != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:OpenHelpSettingsPanel", _openHelpSettingsPanel_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OpenSettingsPanel", _openSettingsPanel_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_WireframeColor", _wireframeColor_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_NoiseColor", _noiseColor_button);
	        EventsBinder.Bind_Clickable_to_event("Settings:OnButton_RestoreDefaults", _restoreDefaults_button);
	        EnsureAddonManagerButtonWired();

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
	        if (_showExternalProcessWindows_toggle != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_showExternalProcessWindows", _showExternalProcessWindows_toggle);
	        if (_webUiOpenBrowserOnStartup_toggle != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_webUiOpenBrowserOnStartup", _webUiOpenBrowserOnStartup_toggle);
	        if (_sdStrictMaskIsolation_toggle != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_sd_strictMaskIsolation", _sdStrictMaskIsolation_toggle);
	        if (_sdStrictIsolationFlipMask_toggle != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_sd_strictIsolationFlipMask", _sdStrictIsolationFlipMask_toggle);
	        if (_sdInpaintingMaskInvert_toggle != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_sd_inpaintingMaskInvert", _sdInpaintingMaskInvert_toggle);
	        if (_useVSync_toggle != null)
	            EventsBinder.Bind_Clickable_to_event("Settings:set_useVSync", _useVSync_toggle);
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

	    /// <summary>Creates "Use VSync" row at runtime (or uses scene-assigned toggle). Reduces Gfx.PresentFrame; helps AMD FirePro / Alienware.</summary>
	    void EnsureUseVSyncRowExists() {
	        if (_useVSync_toggle != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;

	        var row = new GameObject("Row_UseVSync");
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
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 320f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "Use VSync (reduces Gfx.PresentFrame, helps AMD FirePro / Alienware):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;

	        var toggle = CreateRuntimeSpzStyledToggle(row.transform, "Toggle_UseVSync", new Vector2(112f, 28f),
		        UnityEngine.PlayerPrefs.GetInt("UseVSync", 1) == 1, greenWhenOn: false);
	        _useVSync_toggle = toggle;
	        // Bound in Start() (avoid duplicate onValueChanged if Ensure runs first).
	    }

	    /// <summary>Creates "Show external process windows" row (WebUI + addon Python server console visibility) unless scene already provides it.</summary>
	    void EnsureExternalProcessWindowsRowExists() {
	        if (_showExternalProcessWindows_toggle != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;

	        var row = new GameObject("Row_ShowExternalProcessWindows");
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
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 360f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "Show external process windows (WebUI + add-on Python server):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;

	        bool current = UnityEngine.PlayerPrefs.GetInt("ShowExternalProcessWindows", 0) == 1;
	        var toggle = CreateRuntimeSpzStyledToggle(row.transform, "Toggle_ShowExternalProcessWindows", new Vector2(112f, 28f),
		        current, greenWhenOn: false);
	        _showExternalProcessWindows_toggle = toggle;
	        // Bound in Start().
	    }

	    /// <summary>Creates "Open WebUI in browser on startup" row unless scene already provides it.</summary>
	    void EnsureWebUiBrowserStartupRowExists() {
	        if (_webUiOpenBrowserOnStartup_toggle != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;

	        var row = new GameObject("Row_WebUiOpenBrowserOnStartup");
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
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 360f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "Open WebUI in browser when SD is ready (off = no auto tab; Forge/Gradio self-launch is disabled):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;

	        bool current = UnityEngine.PlayerPrefs.GetInt("WebUI_OpenBrowserOnStartup", 0) == 1;
	        var toggle = CreateRuntimeSpzStyledToggle(row.transform, "Toggle_WebUiOpenBrowserOnStartup", new Vector2(112f, 28f),
		        current, greenWhenOn: false);
	        _webUiOpenBrowserOnStartup_toggle = toggle;
	        // Bound in Start().
	    }

	    /// <summary>Creates optional strict mask isolation row (off by default, OG-aligned). When ON, img2img/redo keeps unmasked pixels from init image.</summary>
	    void EnsureSDStrictMaskIsolationRowExists() {
	        if (_sdStrictMaskIsolation_toggle != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;

	        var row = new GameObject("Row_SD_StrictMaskIsolation");
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
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 360f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "Strict inpaint mask isolation (redo affects only masked pixels; off = OG behavior):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;

	        bool current = UnityEngine.PlayerPrefs.GetInt("SD_StrictMaskIsolation", 0) == 1;
	        var toggle = CreateRuntimeSpzStyledToggle(row.transform, "Toggle_SD_StrictMaskIsolation", new Vector2(112f, 28f),
		        current, greenWhenOn: false);
	        _sdStrictMaskIsolation_toggle = toggle;
	        // Bound once in Start() with other toggles (avoid duplicate onValueChanged listeners).
	    }

	    /// <summary>Runtime row: invert which pixels strict post-SD isolation clamps to init (Paint tab mirrors the same pref).</summary>
	    void EnsureSDStrictIsolationFlipMaskRowExists() {
	        if (_sdStrictIsolationFlipMask_toggle != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;

	        var row = new GameObject("Row_SD_StrictIsolationFlipMask");
	        row.transform.SetParent(content, false);
	        var anchor = content.Find("Row_SD_StrictMaskIsolation");
	        if (anchor != null)
	            row.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
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
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 360f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "Invert strict isolation mask (keep init inside brush; post-SD only):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;

	        bool current = PaintTab_StrictIsolationBrushOptions.FlipInvertIsolationMask;
	        var toggle = CreateRuntimeSpzStyledToggle(row.transform, "Toggle_SD_StrictIsolationFlipMask", new Vector2(112f, 28f),
		        current, greenWhenOn: false);
	        _sdStrictIsolationFlipMask_toggle = toggle;
	        // Bound once in Start() with other toggles (avoid duplicate onValueChanged listeners).
	    }

	    /// <summary>WebUI API: <c>inpainting_mask_invert=1</c> so SD changes unmasked viewport pixels; brush / No Color mask region preserved in inpaint semantics.</summary>
	    void EnsureSDInpaintingMaskInvertRowExists() {
	        if (_sdInpaintingMaskInvert_toggle != null) return;
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;

	        var row = new GameObject("Row_SD_InpaintingMaskInvert");
	        row.transform.SetParent(content, false);
	        var anchorFlip = content.Find("Row_SD_StrictIsolationFlipMask");
	        var anchorStrict = content.Find("Row_SD_StrictMaskIsolation");
	        var anchor = anchorFlip != null ? anchorFlip : anchorStrict;
	        if (anchor != null)
	            row.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
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
	        var labelLE = labelGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelLE.preferredWidth = 360f;
	        labelLE.preferredHeight = 24f;
	        var labelText = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelText.text = "Inpaint outside brush mask (SD changes unmasked only; WebUI invert):";
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;

	        bool current = UnityEngine.PlayerPrefs.GetInt("SD_InpaintingMaskInvert", 0) == 1;
	        var toggle = CreateRuntimeSpzStyledToggle(row.transform, "Toggle_SD_InpaintingMaskInvert", new Vector2(112f, 28f),
		        current, greenWhenOn: false);
	        _sdInpaintingMaskInvert_toggle = toggle;
	        // Bound in Start().
	    }

	    /// <summary>Creates "SD GPU" row in Settings panel at runtime so it acts as remote control for which GPU Stable Diffusion uses when launched.</summary>
	    void EnsureSDGpuRowExists() {
	        if (_sdGpuDeviceId_input != null) {
	            _sdGpuDeviceId_input.SetMin(-1);
	            _sdGpuDeviceId_input.SetMax(Settings_MGR.SD_GPU_ID_MAX);
	            return;
	        }
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) return;
	        int current = UnityEngine.PlayerPrefs.GetInt("SD_GPU_DeviceId", -1);
	        string deviceList = LaunchWebUIBatFile.GetCudaDeviceListString();
	        string labelBase = string.IsNullOrEmpty(deviceList) ? "SD GPU (-1=default, 0/1/2=index):" : "SD GPU (" + deviceList + "):";
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
	        labelText.text = labelBase;
	        labelText.fontSize = 14;
	        labelText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelText.raycastTarget = false;
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
	        intInput.SetMax(Settings_MGR.SD_GPU_ID_MAX);
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

	    /// <summary>Runtime rows: enable paint undo + max steps (capped in Settings_MGR for RAM safety).</summary>
	    void EnsurePaintUndoSettingsRowsExist() {
	        // Resolve deps before any "already created" skip — destroyed panel/content must clear the flag so rows can be rebuilt.
	        if (_settingsPanel_go == null) {
	            _paintUndoSettingsRowsCreated = false;
	            return;
	        }
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) content = _settingsPanel_go.transform as RectTransform;
	        if (content == null) {
	            _paintUndoSettingsRowsCreated = false;
	            return;
	        }
	        if (_paintUndoSettingsRowsCreated) {
	            if (PaintUndoSettingsRowsPresentUnder(content))
	                return;
	            DestroyPaintUndoSettingsRowsUnder(content);
	            _paintUndoSettingsRowsCreated = false;
	        }

	        bool undoOn = Settings_MGR.instance != null
		        ? Settings_MGR.instance.get_paintUndo_enabled()
		        : (PlayerPrefs.GetInt("paintUndo_enabled", 1) == 1);
	        int undoDepth = Settings_MGR.instance != null
		        ? Settings_MGR.instance.get_paintUndo_maxDepth()
		        : Mathf.Clamp(PlayerPrefs.GetInt("paintUndo_maxDepth", 8), 1, Settings_MGR.PAINT_UNDO_DEPTH_MAX);

	        // Row: enable toggle
	        var rowEnable = new GameObject("Row_PaintUndo_Enable");
	        rowEnable.transform.SetParent(content, false);
	        var rowEnableRect = rowEnable.AddComponent<RectTransform>();
	        rowEnableRect.sizeDelta = new Vector2(0, 32f);
	        var rowEnableLayout = rowEnable.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
	        rowEnableLayout.spacing = 8f;
	        rowEnableLayout.padding = new RectOffset(4, 4, 2, 2);
	        rowEnableLayout.childAlignment = TextAnchor.MiddleLeft;
	        rowEnableLayout.childControlWidth = true;
	        rowEnableLayout.childControlHeight = true;
	        rowEnableLayout.childForceExpandWidth = false;
	        rowEnableLayout.childForceExpandHeight = false;

	        var labelEnGo = new GameObject("Label");
	        labelEnGo.transform.SetParent(rowEnable.transform, false);
	        var labelEnLE = labelEnGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelEnLE.preferredWidth = 220f;
	        labelEnLE.preferredHeight = 26f;
	        var labelEnText = labelEnGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelEnText.text = "Paint undo & redo (Ctrl+Z; Ctrl+Y or Ctrl+Shift+Z):";
	        labelEnText.fontSize = 14;
	        labelEnText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelEnText.raycastTarget = false;

	        var onOffGo = new GameObject(Settings_MGR.PAINT_UNDO_ONOFF_LABEL_NAME);
	        onOffGo.transform.SetParent(rowEnable.transform, false);
	        var onOffLE = onOffGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        onOffLE.preferredWidth = 42f;
	        onOffLE.preferredHeight = 26f;
	        onOffGo.AddComponent<RectTransform>();
	        var onOffTmp = onOffGo.AddComponent<TMPro.TextMeshProUGUI>();
	        onOffTmp.fontSize = 15;
	        onOffTmp.fontStyle = FontStyles.Bold;
	        onOffTmp.alignment = TextAlignmentOptions.MidlineRight;
	        onOffTmp.raycastTarget = false;

	        var paintUndoToggle = CreateRuntimeSpzStyledToggle(rowEnable.transform, "Toggle_PaintUndo", new Vector2(128f, 28f), undoOn, greenWhenOn: true);
	        EventsBinder.Bind_Clickable_to_event("Settings:set_paintUndo_enabled", paintUndoToggle);
	        Settings_MGR.SyncPaintUndoOnOffLabel(paintUndoToggle, undoOn);

	        // Row: max depth
	        var rowDepth = new GameObject("Row_PaintUndo_Depth");
	        rowDepth.transform.SetParent(content, false);
	        var rowDepthRect = rowDepth.AddComponent<RectTransform>();
	        rowDepthRect.sizeDelta = new Vector2(0, 28f);
	        var rowDepthLayout = rowDepth.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
	        rowDepthLayout.spacing = 8f;
	        rowDepthLayout.padding = new RectOffset(4, 4, 2, 2);
	        rowDepthLayout.childAlignment = TextAnchor.MiddleLeft;
	        rowDepthLayout.childControlWidth = true;
	        rowDepthLayout.childControlHeight = true;
	        rowDepthLayout.childForceExpandWidth = false;
	        rowDepthLayout.childForceExpandHeight = false;

	        var labelDpGo = new GameObject("Label");
	        labelDpGo.transform.SetParent(rowDepth.transform, false);
	        var labelDpLE = labelDpGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        labelDpLE.preferredWidth = 380f;
	        labelDpLE.preferredHeight = 24f;
	        var labelDpText = labelDpGo.AddComponent<TMPro.TextMeshProUGUI>();
	        labelDpText.text = $"Max undo steps (1–{Settings_MGR.PAINT_UNDO_DEPTH_MAX}, CPU RAM per step):";
	        labelDpText.fontSize = 14;
	        labelDpText.color = new Color(0.9f, 0.9f, 0.9f, 1f);
	        labelDpText.raycastTarget = false;

	        var inputGo = new GameObject("Input_PaintUndoDepth");
	        inputGo.transform.SetParent(rowDepth.transform, false);
	        inputGo.AddComponent<RectTransform>();
	        var inputLE = inputGo.AddComponent<UnityEngine.UI.LayoutElement>();
	        inputLE.preferredWidth = 56f;
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
	        inputField.text = undoDepth.ToString();
	        var intInput = inputGo.AddComponent<IntegerInputField>();
	        intInput.SetInputField(inputField);
	        intInput.SetMin(1);
	        intInput.SetMax(Settings_MGR.PAINT_UNDO_DEPTH_MAX);
	        intInput.SetValueWithoutNotify(undoDepth.ToString());
	        EventsBinder.Bind_Clickable_to_event("Settings:set_paintUndo_maxDepth", intInput);
	        _paintUndoSettingsRowsCreated = true;
	    }

	    /// <summary>Direct children of scroll content; matches names used in <see cref="EnsurePaintUndoSettingsRowsExist"/>.</summary>
	    static bool PaintUndoSettingsRowsPresentUnder(RectTransform content) {
	        if (content == null) return false;
	        bool hasEnable = false, hasDepth = false;
	        for (int i = 0; i < content.childCount; i++) {
	            var n = content.GetChild(i).name;
	            if (n == "Row_PaintUndo_Enable") hasEnable = true;
	            else if (n == "Row_PaintUndo_Depth") hasDepth = true;
	        }
	        return hasEnable && hasDepth;
	    }

	    static void DestroyPaintUndoSettingsRowsUnder(RectTransform content) {
	        if (content == null) return;
	        for (int i = content.childCount - 1; i >= 0; i--) {
	            Transform c = content.GetChild(i);
	            if (c.name == "Row_PaintUndo_Enable" || c.name == "Row_PaintUndo_Depth")
	                UnityEngine.Object.DestroyImmediate(c.gameObject);
	        }
	    }

	    void OnRestartWebUI_ApplyGPU() {
	        // Commit the GPU input and force-save to PlayerPrefs so the next launch uses the visible value.
	        var gpuInput = _sdGpuDeviceId_input != null ? _sdGpuDeviceId_input : EventsBinder.FindComponent<IntegerInputField>("Settings:set_sdGpuDeviceId");
	        if (gpuInput != null) {
	            gpuInput.CommitCurrentText();
	            int deviceId = Mathf.Clamp(gpuInput.recentVal, -1, Settings_MGR.SD_GPU_ID_MAX);
	            UnityEngine.PlayerPrefs.SetInt("SD_GPU_DeviceId", deviceId);
	            UnityEngine.PlayerPrefs.Save();
	            StaticEvents.Invoke<int>("Settings:set_sdGpuDeviceId", deviceId);
	        }
	        if (LaunchWebUIBatFile.instance != null) {
	            LaunchWebUIBatFile.instance.LaunchWebui_Manually(printStatusText_ifNotFound: true);
	            if (Viewport_StatusText.instance != null)
	                Viewport_StatusText.instance.ShowStatusText("WebUI launching with selected GPU. Previous WebUI closed automatically.", false, 4f, false);
	        } else if (Viewport_StatusText.instance != null)
	            Viewport_StatusText.instance.ShowStatusText("GPU preference saved. Launch WebUI from the menu to apply.", false, 3f, false);
	    }

	    /// <summary>Runtime VSync / paint-undo toggles: matches in-scene settings toggles (sliced sprites) when assigned on this component.</summary>
	    Toggle CreateRuntimeSpzStyledToggle(Transform rowParent, string rootName, Vector2 size, bool initialOn, bool greenWhenOn) {
	        var root = new GameObject(rootName);
	        root.transform.SetParent(rowParent, false);
	        var rootRt = root.AddComponent<RectTransform>();
	        rootRt.sizeDelta = size;
	        var rootLe = root.AddComponent<LayoutElement>();
	        rootLe.preferredWidth = size.x;
	        rootLe.preferredHeight = size.y;
	        rootLe.minWidth = Mathf.Min(size.x, Mathf.Max(32f, size.x * 0.72f));
	        rootLe.minHeight = Mathf.Max(24f, size.y * 0.9f);

	        var host = new GameObject("ToggleHost");
	        host.transform.SetParent(root.transform, false);
	        StretchRectToParent(host.AddComponent<RectTransform>());

	        bool useAtlas = _settingsToggleFrameSprite != null && _settingsToggleCheckSprite != null;
	        var bg = host.AddComponent<Image>();
	        bg.raycastTarget = true;
	        Graphic graphic;
	        if (useAtlas) {
	            bg.sprite = _settingsToggleFrameSprite;
	            bg.type = Image.Type.Sliced;
	            bg.color = new Color(0.79514813f, 0.7285835f, 0.6933434f, 1f);
	            var chkGo = new GameObject("Checkmark");
	            chkGo.transform.SetParent(host.transform, false);
	            StretchRectToParent(chkGo.AddComponent<RectTransform>());
	            var chk = chkGo.AddComponent<Image>();
	            chk.sprite = _settingsToggleCheckSprite;
	            chk.type = Image.Type.Sliced;
	            chk.color = new Color(0.8980392f, 0.827451f, 0.7882353f, 1f);
	            chk.raycastTarget = false;
	            graphic = chk;
	        } else {
	            bg.color = new Color(0.22f, 0.22f, 0.24f, 1f);
	            graphic = AddToggleCheckmarkGraphic(host.transform);
	        }

	        var toggle = host.AddComponent<Toggle>();
	        toggle.targetGraphic = bg;
	        toggle.graphic = graphic;
	        toggle.toggleTransition = Toggle.ToggleTransition.Fade;
	        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
	        if (useAtlas) {
	            if (greenWhenOn)
	                ApplyPaintUndoSlicedToggleColors(toggle);
	            else
	                ApplySettingsPrefabMatchToggleColors(toggle);
	        } else {
	            if (greenWhenOn)
	                ApplyPaintUndoEnableToggleColors(toggle);
	            else
	                ApplySelectableColors(toggle);
	        }
	        toggle.isOn = initialOn;
	        return toggle;
	    }

	    /// <summary>
	    /// Runtime option rows were ~28px with long labels and parent spacing 0 → text overlapped.
	    /// Enforce readable row height, label wrap, and vertical gaps; kill negative TMP lineSpacing.
	    /// </summary>
	    public void FixSettingsScrollReadability() {
	        if (_settingsPanel_go == null) return;
	        var scrollRect = _settingsPanel_go.GetComponentInChildren<ScrollRect>(true);
	        RectTransform content = scrollRect != null ? scrollRect.content : null;
	        if (content == null) return;

	        var vlg = content.GetComponent<VerticalLayoutGroup>();
	        if (vlg != null) {
	            vlg.spacing = Mathf.Max(vlg.spacing, 12f);
	            vlg.childControlHeight = true;
	            vlg.childForceExpandHeight = false;
	            var pad = vlg.padding;
	            pad.top = Mathf.Max(pad.top, 8);
	            pad.bottom = Mathf.Max(pad.bottom, 12);
	            vlg.padding = pad;
	        }

	        for (int i = 0; i < content.childCount; i++) {
	            Transform child = content.GetChild(i);
	            if (child == null || !child.name.StartsWith("Row_", StringComparison.Ordinal))
	                continue;
	            StyleRuntimeSettingsOptionRow(child);
	        }

	        foreach (var tmp in content.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            if (tmp.lineSpacing < 0f)
	                tmp.lineSpacing = 0f;
	        }

	        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
	        Canvas.ForceUpdateCanvases();
	    }

	    static void StyleRuntimeSettingsOptionRow(Transform row) {
	        if (row == null) return;
	        var rowLe = row.GetComponent<LayoutElement>();
	        if (rowLe == null)
	            rowLe = row.gameObject.AddComponent<LayoutElement>();
	        rowLe.minHeight = 44f;
	        rowLe.preferredHeight = 48f;
	        rowLe.flexibleHeight = 0f;

	        var hlg = row.GetComponent<HorizontalLayoutGroup>();
	        if (hlg != null) {
	            hlg.padding = new RectOffset(4, 8, 8, 8);
	            hlg.spacing = Mathf.Max(hlg.spacing, 10f);
	            hlg.childAlignment = TextAnchor.MiddleLeft;
	            hlg.childControlHeight = true;
	            hlg.childForceExpandHeight = false;
	        }

	        var labelTmp = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
	        if (labelTmp == null) return;
	        labelTmp.enableWordWrapping = true;
	        labelTmp.overflowMode = TextOverflowModes.Ellipsis;
	        labelTmp.lineSpacing = 0f;
	        labelTmp.fontSize = 13f;
	        labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
	        var labelLe = labelTmp.GetComponent<LayoutElement>();
	        if (labelLe != null) {
	            labelLe.minHeight = 36f;
	            labelLe.preferredHeight = 40f;
	            labelLe.flexibleWidth = 1f;
	            if (labelLe.preferredWidth < 280f)
	                labelLe.preferredWidth = 320f;
	        }
	    }

	    /// <summary>
	    /// Themes panel shell, fields, controls, and ON-state success on known Settings_UI widgets.
	    /// Wireframe/noise/product colors stay in Settings_MGR and are not touched.
	    /// </summary>
	    void ApplyThemeTokens() {
	        if (_settingsPanel_go == null) return;
	        var t = SpzUiThemeOps.Active;
	        var panelImg = _settingsPanel_go.GetComponent<Image>();
	        if (panelImg != null) {
	            // Opaque enough that welcome/help text does not ghost through the settings list.
	            Color shell = t.panelBg;
	            shell.a = Mathf.Max(shell.a, 0.96f);
	            SpzUiThemeOps.ApplyGraphicColor(panelImg, shell);
	        }
	        foreach (var input in _settingsPanel_go.GetComponentsInChildren<TMP_InputField>(true)) {
	            if (input == null || IsUnderProductColorSurface(input.transform)) continue;
	            var bg = input.GetComponent<Image>();
	            if (bg != null)
	                SpzUiThemeOps.ApplyGraphicColor(bg, t.fieldBg);
	            if (input.textComponent != null)
	                SpzUiThemeOps.ApplyTmpColor(input.textComponent, t.textPrimary);
	            if (input.placeholder is TMP_Text ph)
	                SpzUiThemeOps.ApplyTmpColor(ph, t.textMuted);
	        }
	        foreach (var btn in _settingsPanel_go.GetComponentsInChildren<Button>(true)) {
	            if (btn == null || btn.targetGraphic == null) continue;
	            // Skip product color swatch buttons (wireframe/noise) — those are prefs, not chrome.
	            if (ReferenceEquals(btn, _wireframeColor_button) || ReferenceEquals(btn, _noiseColor_button))
	                continue;
	            if (IsUnderProductColorSurface(btn.transform))
	                continue;
	            SpzUiThemeOps.ApplySelectableToken(btn, t.controlBg, t.accent);
	        }
	        foreach (var toggle in _settingsPanel_go.GetComponentsInChildren<Toggle>(true)) {
	            if (toggle == null || IsUnderProductColorSurface(toggle.transform)) continue;
	            ApplyThemeToggleColors(toggle, t);
	        }
	        foreach (var tmp in _settingsPanel_go.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null || IsUnderProductColorSurface(tmp.transform)) continue;
	            if (tmp.gameObject.name == "Placeholder" || tmp.gameObject.name == "Checkmark")
	                continue;
	            SpzUiThemeOps.ApplyTmpColor(tmp, t.textPrimary);
	            if (tmp.lineSpacing < 0f)
	                tmp.lineSpacing = 0f;
	        }
	        foreach (var slider in _settingsPanel_go.GetComponentsInChildren<Slider>(true)) {
	            if (slider == null || IsUnderProductColorSurface(slider.transform)) continue;
	            var bg = slider.GetComponent<Image>();
	            if (bg != null)
	                SpzUiThemeOps.ApplyGraphicColor(bg, t.fieldBg);
	            if (slider.fillRect != null) {
	                var fill = slider.fillRect.GetComponent<Image>();
	                if (fill != null)
	                    SpzUiThemeOps.ApplyGraphicColor(fill, t.accent);
	            }
	            if (slider.handleRect != null) {
	                var handle = slider.handleRect.GetComponent<Image>();
	                if (handle != null)
	                    SpzUiThemeOps.ApplyGraphicColor(handle, t.handle);
	            }
	        }
	    }

	    /// <summary>
	    /// Color picker / final-color preview surfaces hold product RGB — not chrome tokens.
	    /// </summary>
	    bool IsUnderProductColorSurface(Transform t) {
	        if (t == null) return false;
	        if (_settings_colorPicker != null && t.IsChildOf(_settings_colorPicker.transform))
	            return true;
	        return false;
	    }

	    static void ApplyThemeToggleColors(Toggle tgl, SpzUiThemeOps.ThemeTokens t) {
	        tgl.transition = Selectable.Transition.ColorTint;
	        // Image.color = chrome token; ColorBlock stays white-based so Unity does not multiply twice.
	        if (tgl.targetGraphic != null)
	            tgl.targetGraphic.color = t.controlBg;
	        var colors = tgl.colors;
	        colors.normalColor = Color.white;
	        colors.highlightedColor = Color.Lerp(Color.white, t.accent, 0.25f);
	        colors.pressedColor = Color.Lerp(Color.white, t.accent, 0.55f);
	        colors.selectedColor = Color.Lerp(Color.white, t.success, 0.35f);
	        colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
	        tgl.colors = colors;
	        if (tgl.graphic != null)
	            SpzUiThemeOps.ApplyGraphicColor(tgl.graphic, t.success);
	    }

	    /// <summary>Same ColorBlock as built-in Settings_UI.prefab toggles (tints sliced frame).</summary>
	    static void ApplySettingsPrefabMatchToggleColors(Selectable sel) {
	        var t = SpzUiThemeOps.Active;
	        sel.transition = Selectable.Transition.ColorTint;
	        sel.colors = new ColorBlock {
	            normalColor = Color.white,
	            highlightedColor = Color.Lerp(Color.white, t.accent, 0.15f),
	            pressedColor = Color.Lerp(Color.white, t.accent, 0.35f),
	            selectedColor = Color.Lerp(Color.white, t.success, 0.35f),
	            disabledColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 0.5019608f),
	            colorMultiplier = 1f,
	            fadeDuration = 0.1f
	        };
	    }

	    /// <summary>Green selected tint on sliced frame; normal white so sprite skin shows through.</summary>
	    static void ApplyPaintUndoSlicedToggleColors(Toggle t) {
	        ApplyThemeToggleColors(t, SpzUiThemeOps.Active);
	    }

	    /// <summary>Apply hover/pressed/selected colors so the control looks selectable and shows active state.</summary>
	    static void ApplySelectableColors(Selectable sel, Color? whenOnTint = null) {
	        var t = SpzUiThemeOps.Active;
	        sel.transition = Selectable.Transition.ColorTint;
	        if (sel.targetGraphic != null)
	            sel.targetGraphic.color = t.controlBg;
	        var block = new ColorBlock {
	            normalColor = Color.white,
	            highlightedColor = Color.Lerp(Color.white, t.accent, 0.25f),
	            pressedColor = Color.Lerp(Color.white, t.accent, 0.55f),
	            selectedColor = whenOnTint ?? t.success,
	            disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f),
	            colorMultiplier = 1f,
	            fadeDuration = 0.12f
	        };
	        sel.colors = block;
	    }

	    /// <summary>Toggle ON = bright green box (selected); OFF = dark gray. Improves visibility vs generic gray toggles.</summary>
	    static void ApplyPaintUndoEnableToggleColors(Toggle t) {
	        ApplyThemeToggleColors(t, SpzUiThemeOps.Active);
	    }

	    static void StretchRectToParent(RectTransform r) {
	        r.anchorMin = Vector2.zero;
	        r.anchorMax = Vector2.one;
	        r.offsetMin = Vector2.zero;
	        r.offsetMax = Vector2.zero;
	        r.sizeDelta = Vector2.zero;
	    }

	    /// <summary>Child checkmark for uGUI Toggle.graphic. Toggle + clickable Image must live on the same GameObject as the Toggle component.</summary>
	    static TMPro.TextMeshProUGUI AddToggleCheckmarkGraphic(Transform toggleRoot) {
	        var checkGo = new GameObject("Checkmark");
	        checkGo.transform.SetParent(toggleRoot, false);
	        var checkRect = checkGo.AddComponent<RectTransform>();
	        StretchRectToParent(checkRect);
	        var checkTmp = checkGo.AddComponent<TMPro.TextMeshProUGUI>();
	        checkTmp.text = "\u2713";
	        checkTmp.fontSize = 20;
	        checkTmp.fontStyle = FontStyles.Bold;
	        checkTmp.alignment = TextAlignmentOptions.Center;
	        checkTmp.color = Color.white;
	        checkTmp.raycastTarget = false;
	        return checkTmp;
	    }
	}
}//end namespace
