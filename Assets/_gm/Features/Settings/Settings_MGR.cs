using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace spz {

	//Singleton
	public class Settings_MGR : MonoBehaviour
	{
	    public static Settings_MGR instance { get; private set; }

	    /// <summary>False during Awake tryLoad so restoring prefs does not relaunch WebUI/addon; true after load for live Settings toggles.</summary>
	    bool _settingsInSessionApplyEnabled;

	    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	    static void ResetSettingsMgrStatics() {
	        instance = null;
	    }

	    int _idleFramerate = 2; //when the window isn't focused. 5 was too much
	    bool _hasFocus = true;

	    /// <summary>When true, PresentFrame is throttled to monitor refresh (helps AMD FirePro / Alienware and reduces GPU load).</summary>
	    bool _useVSync = true;
	    public bool get_useVSync() => _useVSync;
	    public void set_useVSync(bool use) {
	        _useVSync = use;
	        PlayerPrefs.SetInt("UseVSync", _useVSync ? 1 : 0);
	        PlayerPrefs.Save();
	        QualitySettings.vSyncCount = _useVSync ? 1 : 0;
	        Application.targetFrameRate = _useVSync ? 0 : _targetFrameRate; // 0 = let VSync drive; else use cap
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_useVSync");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(_useVSync);
	    }
	    void tryLoad_useVSync() {
	        set_useVSync(PlayerPrefs.GetInt("UseVSync", 1) == 1);
	    }

	    //allows for smooth fps after user drag-and-drops some file, etc.
	    float _dontThrottle_any_FPS_until = -1;
	    public void dontThrottle_any_FPS_until(float timestamp) => _dontThrottle_any_FPS_until = timestamp;

	    int _targetFrameRate = 60; // lowered from 70 to reduce Gfx.PresentFrame on weak/workstation GPUs (e.g. AMD FirePro W2100)
	    public int get_targetFrameRate() => _targetFrameRate;
	    public void set_targetFrameRate(int fps) {
	        _targetFrameRate = fps;
	        PlayerPrefs.SetInt("_targetFrameRate", _targetFrameRate); PlayerPrefs.Save();
	        var inputField = EventsBinder.FindComponent<IntegerInputField>("Settings:set_targetFrameRate");
	        if (inputField != null) inputField.SetValueWithoutNotify(fps.ToString());
	        QualitySettings.vSyncCount = _useVSync ? 1 : 0;
	        Application.targetFrameRate = _useVSync ? 0 : _targetFrameRate;
	    }
	    void tryLoad_targetFrameRate() {
	        set_targetFrameRate(PlayerPrefs.GetInt("_targetFrameRate", 60));
	    }



	    int _brushPrecision_res = 2048;
	    public int get_uv_brushPrecision_res() => _brushPrecision_res;
	    void set_brushPrecision_res(int newRes, bool skipConfirmPopup = false) {
	        string confirmMsg = $"Change brush UV precision to {newRes}?\n<b>This will delete ALL the Art icons.</b>";
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_brushPrecision_res");

	        if (skipConfirmPopup) { OnYes(); }
	        else {
	            bool wasOn_beforeClick = newRes != 4096;
	            if (toggle != null) toggle.SetIsOnWithoutNotify(wasOn_beforeClick);
	            ConfirmPopup_UI.instance.Show(confirmMsg, onYes: OnYes, OnNo);
	        }
	        void OnYes() {
	            _brushPrecision_res = newRes;
	            PlayerPrefs.SetInt("_brushPrecision_res_v2", _brushPrecision_res); PlayerPrefs.Save();
	            if (toggle != null) toggle.SetIsOnWithoutNotify(newRes == 4096);

	            if (GenData2D_Archive.instance != null){ 
	                GenData2D_Archive.instance.Dispose_ALL_genData();
	            }
	        }
	        void OnNo() {
	            if (toggle != null) toggle.SetIsOnWithoutNotify(_brushPrecision_res == 4096);
	        }
	    }
	    void tryLoad_brushPrecision_res()
	        => set_brushPrecision_res(PlayerPrefs.GetInt("_brushPrecision_res_v2", 2048), skipConfirmPopup: true);



	    bool _prompt_textHighlight = true;
	    public bool get_prompt_textHighlight() => _prompt_textHighlight;
	    void set_prompt_textHighlight(bool isHighlight) {
	        _prompt_textHighlight = isHighlight;
	        PlayerPrefs.SetInt("_prompt_textHighlight", _prompt_textHighlight ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_prompt_textHighlight");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isHighlight);
	    }
	    void tryLoad_prompt_textHighlight()
	        => set_prompt_textHighlight(PlayerPrefs.GetInt("_prompt_textHighlight", 1) == 1);



	    bool _isAllowTooltips = true;
	    public bool get_isAllowTooltips() => _isAllowTooltips;
	    void set_isAllowTooltips(bool isAllow) {
	        _isAllowTooltips = isAllow;
	        PlayerPrefs.SetInt("_isAllowTooltips", _isAllowTooltips ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_isAllowTooltips");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isAllow);
	    }
	    void tryLoad_isAllowTooltips()
	        => set_isAllowTooltips(PlayerPrefs.GetInt("_isAllowTooltips", 1) == 1);



	    bool _isShow_CameraInfoText = false;
	    public bool get_isShow_CameraInfoText() => _isShow_CameraInfoText;
	    void set_isShow_CameraInfoText(bool isShow) {
	        _isShow_CameraInfoText = isShow;
	        PlayerPrefs.SetInt("_isShow_CameraInfoText", isShow ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_isShow_CameraInfoText");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isShow);
	    }
	    void tryLoad_isShow_CameraInfoText()
	        => set_isShow_CameraInfoText(PlayerPrefs.GetInt("_isShow_CameraInfoText", 0) == 1);



	    bool _avoid_NSFW_generations = false;
	    public bool get_avoid_NSFW_generations() => _avoid_NSFW_generations;
	    void set_avoid_NSFW_generations(bool isAvoid) {
	        _avoid_NSFW_generations = isAvoid;
	        PlayerPrefs.SetInt("_avoid_NSFW_generations", _avoid_NSFW_generations ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_avoid_NSFW_generations");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isAvoid);
	    }
	    void tryLoad_avoid_NSFW_generations()
	        => set_avoid_NSFW_generations(PlayerPrefs.GetInt("_avoid_NSFW_generations", 1) == 1);

	    // Optional strict mask isolation for img2img/redo.
	    // Default OFF to stay aligned with original StableProjectorz behavior.
	    bool _sd_strictMaskIsolation = false;
	    public bool get_sd_strictMaskIsolation() => _sd_strictMaskIsolation;
	    public void set_sd_strictMaskIsolation(bool on) {
	        _sd_strictMaskIsolation = on;
	        PlayerPrefs.SetInt("SD_StrictMaskIsolation", _sd_strictMaskIsolation ? 1 : 0); PlayerPrefs.Save();
	        var t = EventsBinder.FindComponent<Toggle>("Settings:set_sd_strictMaskIsolation");
	        if (t != null) t.SetIsOnWithoutNotify(on);
	    }
	    void tryLoad_sd_strictMaskIsolation()
	        => set_sd_strictMaskIsolation(PlayerPrefs.GetInt("SD_StrictMaskIsolation", 0) == 1);

	    /// <summary>Same pref as <see cref="PaintTab_StrictIsolationBrushOptions"/>; inverts which screen-mask pixels post-SD strict isolation restores from init.</summary>
	    public bool get_sd_strictIsolationFlipMask() => PaintTab_StrictIsolationBrushOptions.FlipInvertIsolationMask;

	    void set_sd_strictIsolationFlipMask(bool on) {
	        PaintTab_StrictIsolationBrushOptions.SetFlipInvertIsolationMask(on);
	    }

	    /// <summary>When true, img2img requests set WebUI <c>inpainting_mask_invert=1</c>: diffusion targets <b>outside</b> the brush mask (unmasked), preserving masked / No Color strokes in API semantics. Default OFF.</summary>
	    bool _sd_inpaintingMaskInvert = false;
	    public bool get_sd_inpaintingMaskInvert() => _sd_inpaintingMaskInvert;

	    /// <summary>For <see cref="FastPath_API"/> / add-ons; same as toggling Settings.</summary>
	    public void set_sd_inpaintingMaskInvert_from_api(bool on) => set_sd_inpaintingMaskInvert(on);

	    void set_sd_inpaintingMaskInvert(bool on) {
	        _sd_inpaintingMaskInvert = on;
	        PlayerPrefs.SetInt("SD_InpaintingMaskInvert", _sd_inpaintingMaskInvert ? 1 : 0); PlayerPrefs.Save();
	        var t = EventsBinder.FindComponent<Toggle>("Settings:set_sd_inpaintingMaskInvert");
	        if (t != null) t.SetIsOnWithoutNotify(on);
	    }
	    void tryLoad_sd_inpaintingMaskInvert()
	        => set_sd_inpaintingMaskInvert(PlayerPrefs.GetInt("SD_InpaintingMaskInvert", 0) == 1);

	    // Stable Diffusion GPU: -1 = default (auto), 0/1/2... = use that CUDA device (sets CUDA_VISIBLE_DEVICES when launching WebUI).
	    public const int SD_GPU_ID_MAX = 31; // reasonable upper bound for GPU index (UI + launch clamp)
	    int _sdGpuDeviceId = -1;
	    public int get_sdGpuDeviceId() => _sdGpuDeviceId;
	    void set_sdGpuDeviceId(int id) {
	        _sdGpuDeviceId = id < 0 ? -1 : Mathf.Clamp(id, 0, SD_GPU_ID_MAX);
	        PlayerPrefs.SetInt("SD_GPU_DeviceId", _sdGpuDeviceId); PlayerPrefs.Save();
	        var inputField = EventsBinder.FindComponent<IntegerInputField>("Settings:set_sdGpuDeviceId");
	        if (inputField != null) inputField.SetValueWithoutNotify(_sdGpuDeviceId.ToString());
	        // Explicit default must drop Forge-side pin so next launch is not forced back to an old index.
	        if (_sdGpuDeviceId < 0 && _settingsInSessionApplyEnabled)
	            LaunchWebUIBatFile.ClearSdDeviceFromKnownWebuiFolders();
	    }
	    void tryLoad_sdGpuDeviceId()
	        => set_sdGpuDeviceId(PlayerPrefs.GetInt("SD_GPU_DeviceId", -1));

	    // External process windows (WebUI + addon Python server): false = hidden, true = visible console.
	    bool _showExternalProcessWindows = false;
	    public bool get_showExternalProcessWindows() => _showExternalProcessWindows;
	    void set_showExternalProcessWindows(bool show) {
	        bool changed = show != _showExternalProcessWindows;
	        _showExternalProcessWindows = show;
	        PlayerPrefs.SetInt("ShowExternalProcessWindows", _showExternalProcessWindows ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_showExternalProcessWindows");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(show);
	        // Prefs alone only affect next spawn — apply live when the user flips the toggle (not Awake tryLoad).
	        if (changed && _settingsInSessionApplyEnabled)
	            LaunchWebUIBatFile.ApplyExternalProcessWindowsSettingInSession(show);
	    }
	    void tryLoad_showExternalProcessWindows()
	        => set_showExternalProcessWindows(PlayerPrefs.GetInt("ShowExternalProcessWindows", 0) == 1);

	    // When false (default): launch wrappers set SD_WEBUI_RESTARTING=1 and GRADIO_INBROWSER=0, and the app
	    // will not OpenURL when SD is ready. When true: same Forge envs (one tab from Unity at "ready" only; Forge webui.py ignores Gradio for "Local" autolaunch).
	    bool _webUiOpenBrowserOnStartup = false;
	    public bool get_webUiOpenBrowserOnStartup() => _webUiOpenBrowserOnStartup;
	    void set_webUiOpenBrowserOnStartup(bool isOn) {
	        bool changed = isOn != _webUiOpenBrowserOnStartup;
	        _webUiOpenBrowserOnStartup = isOn;
	        PlayerPrefs.SetInt("WebUI_OpenBrowserOnStartup", _webUiOpenBrowserOnStartup ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_webUiOpenBrowserOnStartup");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isOn);
	        if (changed && _settingsInSessionApplyEnabled)
	            LaunchWebUIBatFile.ApplyOpenBrowserSettingInSession(isOn);
	    }
	    void tryLoad_webUiOpenBrowserOnStartup()
	        => set_webUiOpenBrowserOnStartup(PlayerPrefs.GetInt("WebUI_OpenBrowserOnStartup", 0) == 1);

	    public static Action<bool> _Act_viewportInCenterChanged { get; set; } = null;
	    bool _viewport_in_center = true;
	    public bool get_viewport_in_center() => _viewport_in_center;
	    void set_viewport_in_center(bool isCenter) {
	        _viewport_in_center = isCenter;
	        PlayerPrefs.SetInt("_viewport_in_center", _viewport_in_center ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_viewport_in_center");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isCenter);
	        _Act_viewportInCenterChanged?.Invoke(isCenter);
	    }
	    void tryLoad_viewport_in_center()
	        => set_viewport_in_center(PlayerPrefs.GetInt("_viewport_in_center", 1) == 1);



	    public static Action<bool> _Act_verticalRibbonsSwapped { get; set; } = null;
	    bool _viewport_isSwapVerticalRibbons = false;
	    public bool get_viewport_isSwapVerticalRibbons() => _viewport_isSwapVerticalRibbons;
	    void set_viewport_isSwapVerticalRibbons(bool isSwapped) {
	        _viewport_isSwapVerticalRibbons = isSwapped;
	        PlayerPrefs.SetInt("_viewport_isSwapVerticalRibbons", _viewport_isSwapVerticalRibbons ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_viewport_isSwapVerticalRibbons");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isSwapped);
	        _Act_verticalRibbonsSwapped?.Invoke(isSwapped);
	    }
	    void tryLoad_viewport_isSwapVerticalRibbons()
	        => set_viewport_isSwapVerticalRibbons(PlayerPrefs.GetInt("_viewport_isSwapVerticalRibbons", 0) == 1);



	    public static Action<Color> _Act_onWireframeColor { get; set; } = null;
	    static Color _default_wireframeColor = new Color(1, 0.74f, 0.471f, 0.902f);
	    Color _wireframeColor = _default_wireframeColor;
	    public Color get_wireframeColor() => _wireframeColor;
	    void set_wireframeColor(Color col) {
	        col.a = _wireframeOpacity;
	        _wireframeColor = col;
	        string hexCol = ColorUtility.ToHtmlStringRGBA(_wireframeColor);
	        PlayerPrefs.SetString("_wireframeColor", hexCol); PlayerPrefs.Save();
	        var buttonImage = EventsBinder.FindComponent<Button>("Settings:OnButton_WireframeColor")?.GetComponent<Image>();
	        if (buttonImage != null) {
	            Color fullAlpha = col;
	            fullAlpha.a = 1;
	            buttonImage.color = fullAlpha;
	        }
	        _Act_onWireframeColor?.Invoke(col);
	    }
	    void tryLoad_wireframeColor() {
	        string hexColor = PlayerPrefs.GetString("_wireframeColor", ColorUtility.ToHtmlStringRGBA(_default_wireframeColor));
	        if (ColorUtility.TryParseHtmlString("#" + hexColor, out Color color)) {
	            set_wireframeColor(color);
	        } else {
	            set_wireframeColor(_default_wireframeColor);
	        }
	    }
	    void OnButton_WireframeColor() {
	        var colorPicker = EventsBinder.FindComponent<ColorPalette_Panel_UI>("Settings:ColorPicker");
	        if (colorPicker != null) colorPicker.Show(_wireframeColor, set_wireframeColor);
	    }





	    static float _default_wireframeOpacity = 0.902f;
	    float _wireframeOpacity = _default_wireframeOpacity;
	    void set_wireframeOpacity(float opacity) {
	        _wireframeOpacity = Mathf.Clamp01(opacity);
	        PlayerPrefs.SetFloat("_wireframeOpacity", _wireframeOpacity); PlayerPrefs.Save();
	        var slider = EventsBinder.FindComponent<SliderUI_Snapping>("Settings:set_wireframeOpacity");
	        if (slider != null) slider.SetSliderValue(opacity, invokeCallback: false);
	        Color newColor = _wireframeColor;
	        newColor.a = _wireframeOpacity;
	        set_wireframeColor(newColor);
	    }
	    void tryLoad_wireframeOpacity() {
	        set_wireframeOpacity(PlayerPrefs.GetFloat("_wireframeOpacity", _default_wireframeOpacity));
	    }



	    public static Action<int> _Act_onTextSize { get; set; } = null;
	    static int _default_promptTextSize = 19;
	    int _promptTextSize = _default_promptTextSize;
	    public int get_getPromptTextSize() => _promptTextSize;
	    void set_prompt_textSize(float textSize) {
	        _promptTextSize = Mathf.RoundToInt(textSize);
	        PlayerPrefs.SetInt("_promptTextSize", _promptTextSize); PlayerPrefs.Save();
	        var slider = EventsBinder.FindComponent<SliderUI_Snapping>("Settings:set_prompt_textSize");
	        if (slider != null) slider.SetSliderValue(_promptTextSize, invokeCallback: false);
	        _Act_onTextSize?.Invoke(_promptTextSize);
	    }
	    void tryLoad_promptTextSize() {
	        set_prompt_textSize(PlayerPrefs.GetInt("_promptTextSize", _default_promptTextSize));
	    }



	    public static Action<int> _Act_OnShadowR_ChunkSize { get; set; } = null;
	    static int _default_ShadowR_chunkSize = 512;
	    int _ShadowR_chunkSize = _default_ShadowR_chunkSize;
	    public int get_ShadowR_chunkSize() => _ShadowR_chunkSize;
	    void set_ShadowR_chunkSize(float increment05) {
	        int chunkSize = 512;
	        switch (Mathf.RoundToInt(increment05)) {
	            case 0: chunkSize = 128; break;
	            case 1: chunkSize = 256; break;
	            case 2: chunkSize = 384; break;
	            case 3: chunkSize = 512; break;
	            case 4: chunkSize = 768; break;
	            case 5: chunkSize = 1024; break;
	        }
	        _ShadowR_chunkSize = chunkSize;
	        PlayerPrefs.SetInt("_ShadowR_chunkSize", chunkSize); PlayerPrefs.Save();
	        var slider = EventsBinder.FindComponent<SliderUI_Snapping>("Settings:set_ShadowR_chunkSize");
	        if (slider != null) slider.SetSliderValue(increment05, invokeCallback: false);
	        var textComponent = EventsBinder.FindComponent<TextMeshProUGUI>("Settings:set_ShadowR_chunkSize_descript_text"); // Assumes you bind this text
	        if (textComponent != null) textComponent.text = $"Shadow R chunk size ({_ShadowR_chunkSize})\n(lower=faster but makes seams)";
	        _Act_OnShadowR_ChunkSize?.Invoke(_ShadowR_chunkSize);
	    }
	    void tryLoad_ShadowR_chunkSize() {
	        float size = PlayerPrefs.GetInt("_ShadowR_chunkSize", _default_ShadowR_chunkSize);
	        int sliderVal = 0;
	        if (size <= 128) { sliderVal = 0; }
	        else if (size <= 256) { sliderVal = 1; }
	        else if (size <= 384) { sliderVal = 2; }
	        else if (size <= 512) { sliderVal = 3; }
	        else if (size <= 768) { sliderVal = 4; }
	        else { sliderVal = 5; }
	        set_ShadowR_chunkSize(sliderVal);
	    }



	    bool _isAlwaysFocusCameraPivot = true;
	    public bool get_isAlwaysFocusCameraPivot() => _isAlwaysFocusCameraPivot;
	    void set_isAlwaysFocusCameraPivot(bool isAlwaysFocus) {
	        _isAlwaysFocusCameraPivot = isAlwaysFocus;
	        PlayerPrefs.SetInt("_isAlwaysFocusCameraPivot", _isAlwaysFocusCameraPivot ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_isAlwaysFocusCameraPivot");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(isAlwaysFocus);
	    }
	    void tryLoad_isAlwaysFocusCameraPivot()
	        => set_isAlwaysFocusCameraPivot(PlayerPrefs.GetInt("_isAlwaysFocusCameraPivot", 1) == 1);



	    bool _useCtrlScroll_for_WorkflowMode_swaps = false;
	    public bool get_useCtrlScroll_for_WorkflowMode_swaps() => _useCtrlScroll_for_WorkflowMode_swaps;
	    void set_useCtrlScroll_for_WorkflowMode_swaps(bool useCtrlScroll) {
	        _useCtrlScroll_for_WorkflowMode_swaps = useCtrlScroll;
	        PlayerPrefs.SetInt("_useCtrlScroll_for_WorkflowMode_swaps", _useCtrlScroll_for_WorkflowMode_swaps ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_useCtrlScroll_for_WorkflowMode_swaps");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(useCtrlScroll);
	    }
	    void tryLoad_useCtrlScroll_for_WorkflowMode_swaps()
	        => set_useCtrlScroll_for_WorkflowMode_swaps(PlayerPrefs.GetInt("_useCtrlScroll_for_WorkflowMode_swaps", 0) == 1);



	    bool _ignoreCtrl_if_clickSelectingMeshes = false;
	    public bool get_ignoreCtrl_if_clickSelectingMeshes() => _ignoreCtrl_if_clickSelectingMeshes;
	    void set_ignoreCtrl_if_clickSelectingMeshes(bool ignoreCtrl) {
	        _ignoreCtrl_if_clickSelectingMeshes = ignoreCtrl;
	        PlayerPrefs.SetInt("_ignoreCtrl_if_clickSelectingMeshes", _ignoreCtrl_if_clickSelectingMeshes ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_ignoreCtrl_if_clickSelectingMeshes");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(ignoreCtrl);
	    }
	    void tryLoad_ignoreCtrl_if_clickSelectingMeshes()
	        => set_ignoreCtrl_if_clickSelectingMeshes(PlayerPrefs.GetInt("_ignoreCtrl_if_clickSelectingMeshes", 0) == 1);


	    // --- Paint undo (session-only history; see docs/PAINT_UNDO_SPEC.md) ---
	    /// <summary>Upper cap for undo depth in Settings UI and PlayerPrefs (each step holds compressed full active layer in RAM).</summary>
	    public const int PAINT_UNDO_DEPTH_MAX = 16;
	    /// <summary>Direct child of Row_PaintUndo_Enable; must match Settings_UI runtime row.</summary>
	    public const string PAINT_UNDO_ONOFF_LABEL_NAME = "PaintUndo_OnOff_Label";

	    bool _paintUndo_enabled = true;
	    public bool get_paintUndo_enabled() => _paintUndo_enabled;
	    public void set_paintUndo_enabled(bool on) {
		    _paintUndo_enabled = on;
		    PlayerPrefs.SetInt("paintUndo_enabled", _paintUndo_enabled ? 1 : 0);
		    PlayerPrefs.Save();
		    var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_paintUndo_enabled");
		    if (toggle != null) {
			    toggle.SetIsOnWithoutNotify(on);
			    SyncPaintUndoOnOffLabel(toggle, on);
		    }
	    }

	    public static void SyncPaintUndoOnOffLabel(Toggle toggle, bool on) {
		    if (toggle == null) return;
		    Transform anc = toggle.transform.parent;
		    TextMeshProUGUI tmp = null;
		    while (anc != null) {
			    var tr = anc.Find(PAINT_UNDO_ONOFF_LABEL_NAME);
			    if (tr != null) {
				    tmp = tr.GetComponent<TextMeshProUGUI>();
				    break;
			    }
			    anc = anc.parent;
		    }
		    if (tmp == null) return;
		    tmp.text = on ? "ON" : "OFF";
		    tmp.color = on ? new Color(0.35f, 1f, 0.45f, 1f) : new Color(0.55f, 0.55f, 0.58f, 1f);
	    }
	    void tryLoad_paintUndo_enabled()
		    => set_paintUndo_enabled(PlayerPrefs.GetInt("paintUndo_enabled", 1) == 1);

	    int _paintUndo_maxDepth = 8;
	    public int get_paintUndo_maxDepth() => _paintUndo_maxDepth;
	    public void set_paintUndo_maxDepth(int depth) {
		    _paintUndo_maxDepth = Mathf.Clamp(depth, 1, PAINT_UNDO_DEPTH_MAX);
		    PlayerPrefs.SetInt("paintUndo_maxDepth", _paintUndo_maxDepth);
		    PlayerPrefs.Save();
		    var inputField = EventsBinder.FindComponent<IntegerInputField>("Settings:set_paintUndo_maxDepth");
		    if (inputField != null) inputField.SetValueWithoutNotify(_paintUndo_maxDepth.ToString());
		    if (PaintUndo_MGR.instance != null)
			    PaintUndo_MGR.instance.ApplyMaxDepthFromSettings();
	    }
	    void tryLoad_paintUndo_maxDepth() {
		    int raw = PlayerPrefs.GetInt("paintUndo_maxDepth", 8);
		    set_paintUndo_maxDepth(Mathf.Clamp(raw, 1, PAINT_UNDO_DEPTH_MAX));
	    }



	    [SerializeField] AnimationCurve _warpSpeed_curve;
	    static float _default_uvWarpSpeed01 = 0.5f;
	    float _uvWarpSpeed01 = _default_uvWarpSpeed01;
	    public float get_uvWarpSpeed01() => _uvWarpSpeed01;
	    public float get_uvWarpSpeed() => _warpSpeed_curve.Evaluate(_uvWarpSpeed01);
	    void set_uvWarpSpeed01(float speed01) {
	        _uvWarpSpeed01 = speed01;
	        PlayerPrefs.SetFloat("_uvWarpSpeed01", _uvWarpSpeed01); PlayerPrefs.Save();
	        var slider = EventsBinder.FindComponent<SliderUI_Snapping>("Settings:set_uvWarpSpeed01");
	        if (slider != null) slider.SetSliderValue(speed01, invokeCallback: false);
	    }
	    void tryLoad_uvWarpSpeed01() {
	        set_uvWarpSpeed01(PlayerPrefs.GetFloat("_uvWarpSpeed01", _default_uvWarpSpeed01));
	    }



	    static float _default_noiseSpeed = 1f;
	    float _noiseSpeed = _default_noiseSpeed;
	    public float get_noiseSpeed() => _noiseSpeed;
	    void set_noiseSpeed(float speed) {
	        _noiseSpeed = Mathf.Clamp(speed, 0.01f, 2f);
	        PlayerPrefs.SetFloat("_noiseSpeed", _noiseSpeed); PlayerPrefs.Save();
	        var slider = EventsBinder.FindComponent<SliderUI_Snapping>("Settings:set_noiseSpeed");
	        if (slider != null) slider.SetSliderValue(speed, invokeCallback: false);
	    }
	    void tryLoad_noiseSpeed() {
	        set_noiseSpeed(PlayerPrefs.GetFloat("_noiseSpeed", _default_noiseSpeed));
	    }



	    static Color _default_noiseColor = new Color(0.231f, 0.05f, 0.374f, 1f);
	    Color _noiseColor = _default_noiseColor;
	    public Color get_noiseColor() => _noiseColor;
	    void set_noiseColor(Color col) {
	        _noiseColor = col;
	        string hexCol = ColorUtility.ToHtmlStringRGBA(_noiseColor);
	        PlayerPrefs.SetString("_noiseColor", hexCol); PlayerPrefs.Save();
	        var buttonImage = EventsBinder.FindComponent<Button>("Settings:OnButton_NoiseColor")?.GetComponent<Image>();
	        if (buttonImage != null) buttonImage.color = col;
	    }
	    void tryLoad_noiseColor() {
	        string hexColor = PlayerPrefs.GetString("_noiseColor", ColorUtility.ToHtmlStringRGBA(_default_noiseColor));
	        if (ColorUtility.TryParseHtmlString("#" + hexColor, out Color color)) {
	            set_noiseColor(color);
	        } else {
	            set_noiseColor(_default_noiseColor);
	        }
	    }
	    void OnButton_NoiseColor() {
	        var colorPicker = EventsBinder.FindComponent<ColorPalette_Panel_UI>("Settings:ColorPicker"); // Assumes picker is bound
	        if (colorPicker != null) colorPicker.Show(_noiseColor, set_noiseColor);
	    }



	    bool _layout_askServerOften = false;
	    public bool get_layout_askServerOften() => _layout_askServerOften;
	    void set_layout_askServerOften(bool askOften) {
	        _layout_askServerOften = askOften;
	        PlayerPrefs.SetInt("_layout_askServerOften", _layout_askServerOften ? 1 : 0); PlayerPrefs.Save();
	        var toggle = EventsBinder.FindComponent<Toggle>("Settings:set_layout_askServerOften");
	        if (toggle != null) toggle.SetIsOnWithoutNotify(askOften);
	    }
	    void tryLoad_layout_askServerOften()
	        => set_layout_askServerOften(PlayerPrefs.GetInt("_layout_askServerOften", 0) == 1);



	    public static bool isLaunchFastWebui { get; private set; }
	    public static void Set_isFastWebui(bool isFastWebui) {
	        isLaunchFastWebui = isFastWebui;
	        PlayerPrefs.SetInt("isLaunchFastWebui", isFastWebui ? 1 : 0); PlayerPrefs.Save();
	    }

 
	    float _settingsOpenedAtUnscaled = -999f;
	    const float SettingsOpenGraceSeconds = 0.35f;

	    void OnButton_OpenSettingsPanel() {
	        var panel = EventsBinder.FindComponent<RectTransform>("Settings:SettingsPanel");
	        if (panel != null) panel.gameObject.SetActive(true);
	        _settingsOpenedAtUnscaled = Time.unscaledTime;
	        var settingsUi = panel != null ? panel.GetComponentInParent<Settings_UI>(true) : null;
	        if (settingsUi == null)
	            settingsUi = FindObjectOfType<Settings_UI>(true);
	        settingsUi?.FixSettingsScrollReadability();
	        // Do not ScrollToEnd(…, false): that jumps to the bottom then animates to the top (flash).
	        SnapSettingsScrollToTop();
	    }

	    void OnButton_OpenHelpSettingsPanel() {
	        var panel = EventsBinder.FindComponent<RectTransform>("Settings:SettingsPanel");
	        if (panel != null) panel.gameObject.SetActive(true);
	        _settingsOpenedAtUnscaled = Time.unscaledTime;
	        var settingsUi = panel != null ? panel.GetComponentInParent<Settings_UI>(true) : null;
	        if (settingsUi == null)
	            settingsUi = FindObjectOfType<Settings_UI>(true);
	        settingsUi?.FixSettingsScrollReadability();
	        SnapSettingsScrollToTop();
	    }

	    static void SnapSettingsScrollToTop() {
	        var autoScroll = EventsBinder.FindComponent<ScrollRect_AutoScroll>("Settings:AutoScroll");
	        if (autoScroll == null) return;
	        var scroll = autoScroll.GetComponentInChildren<ScrollRect>(true);
	        if (scroll == null)
	            scroll = autoScroll.GetComponent<ScrollRect>();
	        if (scroll != null)
	            scroll.verticalNormalizedPosition = 1f;
	    }

	    void OnButton_OpenAddonManager() {
	        // OpenFromMenu: instance path + pending when Tool_AddonSystem still loading + Invoke after Awake subscribe.
	        AddonManager_UI.OpenFromMenu();
	    }


	    void OnButton_RestoreDefaults() {
	        string confirmMsg = $"Restore default settings?\n<b>This will delete ALL the Art icons.</b>";
	        ConfirmPopup_UI.instance.Show(confirmMsg, OnYes, OnNo);
	        void OnYes() {
	            set_useVSync(true);
	            set_targetFrameRate(60);
	            set_brushPrecision_res(1024, skipConfirmPopup: true);
	            set_prompt_textHighlight(true);
	            set_isAllowTooltips(true);
	            set_wireframeColor(new Color(1, 0.74f, 0.471f, 0.902f));
	            set_wireframeOpacity(_default_wireframeOpacity);
	            set_isAlwaysFocusCameraPivot(true);
	            set_avoid_NSFW_generations(false);
	            set_sd_strictMaskIsolation(false);
	            PaintTab_StrictIsolationBrushOptions.SetFlipInvertIsolationMask(false);
	            set_sd_inpaintingMaskInvert(false);
	            set_viewport_in_center(true);
	            set_viewport_isSwapVerticalRibbons(false);
	            set_uvWarpSpeed01(_default_uvWarpSpeed01);
	            set_noiseSpeed(_default_noiseSpeed);
	            set_noiseColor(_default_noiseColor);
	            set_layout_askServerOften(false);
	            set_ignoreCtrl_if_clickSelectingMeshes(false);
	            set_useCtrlScroll_for_WorkflowMode_swaps(false);
	            set_sdGpuDeviceId(-1);
	            set_showExternalProcessWindows(false);
	            set_webUiOpenBrowserOnStartup(false);
	            set_paintUndo_enabled(true);
	            set_paintUndo_maxDepth(8);
	        }
	        void OnNo() { }
	    }


	    void AdjustTargetFramerate() {
	        bool canReduce_FPS = Performance_MGR.instance != null &&
	                             Performance_MGR.instance.isThrottleFPS_whenGenerating() &&
	                             GenerateButtons_UI.isGenerating &&
	                             GenerateButtons_UI.isGeneratingPaused == false;
	        if (canReduce_FPS) {
	            Application.targetFrameRate = _idleFramerate;
	            return;
	        }
	        if (Time.time < 15) {
	            Application.targetFrameRate = _useVSync ? 0 : _targetFrameRate;
	            return;
	        }
	        bool dontThrottle = Time.time < _dontThrottle_any_FPS_until;
	        if (_useVSync) {
	            // Keep targetFrameRate 0 so monitor refresh drives frame rate; do not set _idleFramerate or we bypass VSync.
	            Application.targetFrameRate = 0;
	        } else {
	            Application.targetFrameRate = _hasFocus || dontThrottle ? _targetFrameRate : _idleFramerate;
	        }
	    }


	    void Update() {
	        int targ = Application.targetFrameRate;
	        if (targ != _targetFrameRate && targ != _idleFramerate && !(_useVSync && targ == 0)) {
	            Debug.LogError("Something changed the target frame rate. Only Settings_MGR should do it");
	        }
	        AdjustTargetFramerate();

	        var settingsPanel = EventsBinder.FindComponent<RectTransform>("Settings:SettingsPanel");
	        var colorPicker = EventsBinder.FindComponent<ColorPalette_Panel_UI>("Settings:ColorPicker");

	        Vector2 cursorPos = KeyMousePenInput.cursorScreenPos();
	        bool isPressed = KeyMousePenInput.isLMBpressed();
	        bool isClicked = KeyMousePenInput.isLMBpressedThisFrame() || KeyMousePenInput.isRMBpressedThisFrame() || KeyMousePenInput.isMMBpressedThisFrame();

	        // Click-outside / Escape close must not depend on ColorPicker binding.
	        // Hit rect must stay host-sized (see Settings_UI.ClampSettingsPanelHitRect).
	        // Close only on pointer *release* outside, never during the open-click grace, and never
	        // when the pointer is over the gear/help launchers (they sit outside the panel rect).
	        if (settingsPanel != null && settingsPanel.gameObject.activeInHierarchy) {
	            bool escape = UnityEngine.InputSystem.Keyboard.current != null
	                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
	            if (escape) {
	                settingsPanel.gameObject.SetActive(false);
	            }
	            else if (Time.unscaledTime >= _settingsOpenedAtUnscaled + SettingsOpenGraceSeconds
	                     && (KeyMousePenInput.isLMBreleasedThisFrame()
	                         || KeyMousePenInput.isRMBpressedThisFrame()
	                         || KeyMousePenInput.isMMBpressedThisFrame())) {
	                Canvas canvas = settingsPanel.GetComponentInParent<Canvas>();
	                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
	                    ? canvas.worldCamera
	                    : null;
	                bool isInsidePanel = RectTransformUtility.RectangleContainsScreenPoint(settingsPanel, cursorPos, cam);
	                bool onLauncher = IsPointerOverSettingsLauncher(cursorPos, cam);
	                if (!isInsidePanel && !onLauncher)
	                    settingsPanel.gameObject.SetActive(false);
	            }
	        }

	        if (colorPicker != null && colorPicker._isShowing && isClicked && !isPressed) {
	            var rtf = colorPicker.transform as RectTransform;
	            if (RectTransformUtility.RectangleContainsScreenPoint(rtf, cursorPos) == false) {
	                colorPicker.Hide();
	            }
	        }
	    }

	    /// <summary>
	    /// Gear / help launchers sit outside the Settings panel hit-rect — treat them as non-dismiss.
	    /// </summary>
	    static bool IsPointerOverSettingsLauncher(Vector2 cursorPos, Camera cam) {
	        var ui = FindObjectOfType<Settings_UI>(true);
	        if (ui == null) return false;
	        return ui.IsPointerOverLauncher(cursorPos, cam);
	    }


	    void Awake() {
	        if (instance != null) { DestroyImmediate(this); return; }
	        instance = this;

	        // Note: You must now bind helper components like the panel, color picker, etc., in your Settings_UI script.
	        // Example in Settings_UI.Start():
	        // UIEventBinder.Bind("Settings:SettingsPanel", _settingsPanel_go.GetComponent<RectTransform>());
	        // UIEventBinder.Bind("Settings:ColorPicker", _settings_colorPicker);
	        // UIEventBinder.Bind("Settings:AutoScroll", _autoScroll);
	        // UIEventBinder.Bind("Settings:set_ShadowR_chunkSize_descript_text", _shadowR_chunkSize_descript);

	        StaticEvents.SubscribeUnique("Settings:OpenSettingsPanel", OnButton_OpenSettingsPanel);
	        StaticEvents.SubscribeUnique("Settings:OpenHelpSettingsPanel", OnButton_OpenHelpSettingsPanel);
	        StaticEvents.SubscribeUnique<int>("Settings:set_targetFrameRate", set_targetFrameRate);
	        StaticEvents.SubscribeUnique<float>("Settings:set_prompt_textSize", set_prompt_textSize);
	        StaticEvents.SubscribeUnique("Settings:OnButton_WireframeColor", OnButton_WireframeColor);
	        StaticEvents.SubscribeUnique<float>("Settings:set_wireframeOpacity", set_wireframeOpacity);
	        StaticEvents.SubscribeUnique<float>("Settings:set_uvWarpSpeed01", set_uvWarpSpeed01);
	        StaticEvents.SubscribeUnique<float>("Settings:set_ShadowR_chunkSize", set_ShadowR_chunkSize);
	        // Special lambda handler for brush precision
	        StaticEvents.SubscribeUnique<bool>("Settings:set_brushPrecision_res", val => set_brushPrecision_res(val ? 4096 : 2048));
	        StaticEvents.SubscribeUnique<bool>("Settings:set_prompt_textHighlight", set_prompt_textHighlight);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_useVSync", set_useVSync);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_isAllowTooltips", set_isAllowTooltips);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_isShow_CameraInfoText", set_isShow_CameraInfoText);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_isAlwaysFocusCameraPivot", set_isAlwaysFocusCameraPivot);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_avoid_NSFW_generations", set_avoid_NSFW_generations);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_viewport_in_center", set_viewport_in_center);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_viewport_isSwapVerticalRibbons", set_viewport_isSwapVerticalRibbons);
	        StaticEvents.SubscribeUnique("Settings:OnButton_RestoreDefaults", OnButton_RestoreDefaults);
	        // Same handler as legacy repo; OrReplace avoids stale delegate with Enter Play Mode Without Reload Domain.
	        StaticEvents.SubscribeOrReplace("Settings:OnButton_OpenAddonManager", OnButton_OpenAddonManager);
	        StaticEvents.SubscribeUnique<float>("Settings:set_noiseSpeed", set_noiseSpeed);
	        StaticEvents.SubscribeUnique("Settings:OnButton_NoiseColor", OnButton_NoiseColor);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_layout_askServerOften", set_layout_askServerOften);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_useCtrlScroll_for_WorkflowMode_swaps", set_useCtrlScroll_for_WorkflowMode_swaps);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_ignoreCtrl_if_clickSelectingMeshes", set_ignoreCtrl_if_clickSelectingMeshes);
	        StaticEvents.SubscribeUnique<int>("Settings:set_sdGpuDeviceId", set_sdGpuDeviceId);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_showExternalProcessWindows", set_showExternalProcessWindows);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_webUiOpenBrowserOnStartup", set_webUiOpenBrowserOnStartup);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_sd_strictMaskIsolation", set_sd_strictMaskIsolation);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_sd_strictIsolationFlipMask", set_sd_strictIsolationFlipMask);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_sd_inpaintingMaskInvert", set_sd_inpaintingMaskInvert);
	        StaticEvents.SubscribeUnique<bool>("Settings:set_paintUndo_enabled", set_paintUndo_enabled);
	        StaticEvents.SubscribeUnique<int>("Settings:set_paintUndo_maxDepth", set_paintUndo_maxDepth);
	        tryLoad_useVSync();
	        tryLoad_targetFrameRate();
	        tryLoad_brushPrecision_res();
	        tryLoad_prompt_textHighlight();
	        tryLoad_isShow_CameraInfoText();
	        tryLoad_isAllowTooltips();
	        tryLoad_promptTextSize();
	        tryLoad_wireframeColor();
	        tryLoad_wireframeOpacity();
	        tryLoad_ShadowR_chunkSize();
	        tryLoad_isAlwaysFocusCameraPivot();
	        tryLoad_avoid_NSFW_generations();
	        tryLoad_sd_strictMaskIsolation();
	        tryLoad_sd_inpaintingMaskInvert();
	        tryLoad_viewport_in_center();
	        tryLoad_viewport_isSwapVerticalRibbons();
	        tryLoad_uvWarpSpeed01();
	        tryLoad_noiseSpeed();
	        tryLoad_noiseColor();
	        tryLoad_layout_askServerOften();
	        tryLoad_useCtrlScroll_for_WorkflowMode_swaps();
	        tryLoad_ignoreCtrl_if_clickSelectingMeshes();
	        tryLoad_sdGpuDeviceId();
	        tryLoad_showExternalProcessWindows();
	        tryLoad_webUiOpenBrowserOnStartup();
	        tryLoad_paintUndo_enabled();
	        tryLoad_paintUndo_maxDepth();
	        isLaunchFastWebui = PlayerPrefs.GetInt("isLaunchFastWebui", 0) > 0;
	        _settingsInSessionApplyEnabled = true;
	    }

	    void Start() {
	        QualitySettings.vSyncCount = _useVSync ? 1 : 0;
	        Application.targetFrameRate = _useVSync ? 0 : _targetFrameRate;
	    }

	    void OnApplicationFocus(bool hasFocus) {
	        _hasFocus = hasFocus;
	        if (Keyboard.current != null) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); }
	        if (Mouse.current != null) { InputSystem.QueueStateEvent(Mouse.current, new MouseState()); }
	        if (Pen.current != null) { InputSystem.QueueStateEvent(Pen.current, new PenState()); }
	        Input.ResetInputAxes();
	    }

	    void OnApplicationPause(bool pauseStatus) {
	        OnApplicationFocus(!pauseStatus);
	    }

	    void OnDestroy() {
	        if (instance != this) return;
	        // Important: Unsubscribe from all static events to prevent memory leaks.
	        // Note: A dedicated method should be used for the lambda subscription to enable unsubscribing.
	        // For now, this demonstrates the required pattern.
	        StaticEvents.Unsubscribe("Settings:OpenSettingsPanel", OnButton_OpenSettingsPanel);
	        StaticEvents.Unsubscribe("Settings:OpenHelpSettingsPanel", OnButton_OpenHelpSettingsPanel);
	        StaticEvents.Unsubscribe<int>("Settings:set_targetFrameRate", set_targetFrameRate);
	        StaticEvents.Unsubscribe<float>("Settings:set_prompt_textSize", set_prompt_textSize);
	        StaticEvents.Unsubscribe("Settings:OnButton_WireframeColor", OnButton_WireframeColor);
	        StaticEvents.Unsubscribe<float>("Settings:set_wireframeOpacity", set_wireframeOpacity);
	        StaticEvents.Unsubscribe<float>("Settings:set_uvWarpSpeed01", set_uvWarpSpeed01);
	        StaticEvents.Unsubscribe<float>("Settings:set_ShadowR_chunkSize", set_ShadowR_chunkSize);
	        StaticEvents.Unsubscribe<bool>("Settings:set_prompt_textHighlight", set_prompt_textHighlight);
	        StaticEvents.Unsubscribe<bool>("Settings:set_useVSync", set_useVSync);
	        StaticEvents.Unsubscribe<bool>("Settings:set_isAllowTooltips", set_isAllowTooltips);
	        StaticEvents.Unsubscribe<bool>("Settings:set_isShow_CameraInfoText", set_isShow_CameraInfoText);
	        StaticEvents.Unsubscribe<bool>("Settings:set_isAlwaysFocusCameraPivot", set_isAlwaysFocusCameraPivot);
	        StaticEvents.Unsubscribe<bool>("Settings:set_avoid_NSFW_generations", set_avoid_NSFW_generations);
	        StaticEvents.Unsubscribe<bool>("Settings:set_viewport_in_center", set_viewport_in_center);
	        StaticEvents.Unsubscribe<bool>("Settings:set_viewport_isSwapVerticalRibbons", set_viewport_isSwapVerticalRibbons);
	        StaticEvents.Unsubscribe("Settings:OnButton_RestoreDefaults", OnButton_RestoreDefaults);
	        StaticEvents.Unsubscribe("Settings:OnButton_OpenAddonManager", OnButton_OpenAddonManager);
	        StaticEvents.Unsubscribe<float>("Settings:set_noiseSpeed", set_noiseSpeed);
	        StaticEvents.Unsubscribe("Settings:OnButton_NoiseColor", OnButton_NoiseColor);
	        StaticEvents.Unsubscribe<bool>("Settings:set_layout_askServerOften", set_layout_askServerOften);
	        StaticEvents.Unsubscribe<bool>("Settings:set_useCtrlScroll_for_WorkflowMode_swaps", set_useCtrlScroll_for_WorkflowMode_swaps);
	        StaticEvents.Unsubscribe<bool>("Settings:set_ignoreCtrl_if_clickSelectingMeshes", set_ignoreCtrl_if_clickSelectingMeshes);
	        StaticEvents.Unsubscribe<int>("Settings:set_sdGpuDeviceId", set_sdGpuDeviceId);
	        StaticEvents.Unsubscribe<bool>("Settings:set_showExternalProcessWindows", set_showExternalProcessWindows);
	        StaticEvents.Unsubscribe<bool>("Settings:set_webUiOpenBrowserOnStartup", set_webUiOpenBrowserOnStartup);
	        StaticEvents.Unsubscribe<bool>("Settings:set_sd_strictMaskIsolation", set_sd_strictMaskIsolation);
	        StaticEvents.Unsubscribe<bool>("Settings:set_sd_strictIsolationFlipMask", set_sd_strictIsolationFlipMask);
	        StaticEvents.Unsubscribe<bool>("Settings:set_sd_inpaintingMaskInvert", set_sd_inpaintingMaskInvert);
	        StaticEvents.Unsubscribe<bool>("Settings:set_paintUndo_enabled", set_paintUndo_enabled);
	        StaticEvents.Unsubscribe<int>("Settings:set_paintUndo_maxDepth", set_paintUndo_maxDepth);
	    }
	}
}//end namespace
