using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

namespace spz {

	public class Gen3D_WorkflowOptionsRibbon_UI : MonoBehaviour{
	    public static Gen3D_WorkflowOptionsRibbon_UI instance { get; private set; } = null;

	    [Space(10)]
	    [SerializeField] CanvasGroup _wholePanel_canvGrp;
	    [Space(10)]
	    [SerializeField] CircleSlider_Snapping_UI _rembg_backgroundThresh;
	    [SerializeField] TextMeshProUGUI _rembg_backgroundTxt;
	    [Space(10)]
	    [SerializeField] CircleSlider_Snapping_UI _rembg_foregroundThresh;
	    [SerializeField] TextMeshProUGUI _rembg_foregroundTxt;
	    [Space(10)]
	    [SerializeField] Toggle _showAlphaOnly_toggle;
	    [SerializeField] Toggle _makeScreenshots_toggle;
	    [SerializeField] Animation _takeScreenshots_toggleAnim;
	    [SerializeField] Button _rembg_button;
	    [SerializeField] Gen3D_BrushRibbon_UI_Direction _direction;
	    [Space(10)]
	    [SerializeField] Shader _rgba_to_a_shader;

	    Material _rgba_to_a_mat;
	    GenData2D _currentlyProcessed_genData = null;

	    public bool _brush_isPositive =>_direction.isPositive; //positive negative
	    public bool _is_can_adjust_BG => _makeScreenshots_toggle.isOn==false;
	    public bool _is_can_take_screenshots => _makeScreenshots_toggle.isOn;
	    public bool _isShowAlphaOnly_toggle => _showAlphaOnly_toggle.isOn;

	    public Action<bool> Act_AllowTakeScreenshots { get; set; } = null;


	    void OnButton_RemBG(){
	        var bgIcon = ArtBG_IconsUI_List.instance?._mainSelectedIcon;
	        if(bgIcon == null){
	            string msg = "Please import and select a background image, in the Art (BG) panel first.";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 5, false);
	            return; 
	        }
	        var genData = bgIcon._genData; 
	        if(genData == null){
	            string msg = "Please import and select a background image, in the Art (BG) panel first.";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 5, false);
	            return; 
	        }
	        _currentlyProcessed_genData = genData;

	        var rembg_arg = new Rembg_PythonRunner.Rembg_arg{
	            backgroundThresh_0_255 =  Mathf.RoundToInt(255 * _rembg_backgroundThresh.value/(float)_rembg_backgroundThresh.max),
	            foregroundThresh_0_255 =  Mathf.RoundToInt(255 * _rembg_foregroundThresh.value/(float)_rembg_foregroundThresh.max),
	            input =  new List<Texture2D>{ bgIcon.texture0().tex2D },
	            destroyInputTextures_whenDone = false,
	            onReady = OnBackgroundRemoved,
	        };
	        Rembg_PythonRunner.instance.RemoveBackground_Rembg(rembg_arg);
	    }

	    void OnBackgroundRemoved( List<Texture2D> texs ){
	        if(texs == null || texs.Count==0){ return; }

	        // extract the alpha channel from the returned textures (only one should have been returned).
	        // Use this alpha channel as the new mask of the BG image:
	        _rgba_to_a_mat.SetTexture("_MainTex", texs[0]);
	        RenderTexture dest_mask = _currentlyProcessed_genData._masking_utils._ObjectUV_brushedMaskR8[0].texArray;
	        TextureTools_SPZ.Blit( null, dest_mask, _rgba_to_a_mat);

	        texs.ForEach( t=>DestroyImmediate(t) );
	    }


	    void OnMakeScreenshotsToggle(bool isOn){
	        if(isOn){ _direction.Hide(); }
	        else{ _direction.Show(); }
	        Act_AllowTakeScreenshots?.Invoke(isOn);
	        if(isOn){ 
	            Viewport_StatusText.instance.ShowStatusText("Left-Drag will make Screenshots", false, 2, false);
	        }
	    }


	    void FadeWholePanel(){
	        bool show = DimensionMode_MGR.instance != null
	            && DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d;
	        UiCanvasGroupModeStrip.Tick(_wholePanel_canvGrp, show, 7f);
        
	        if(_showAlphaOnly_toggle != null && _showAlphaOnly_toggle.isOn
	           && (_wholePanel_canvGrp == null || !_wholePanel_canvGrp.gameObject.activeSelf)){
	            _showAlphaOnly_toggle.isOn = false;
	        }
	    }


	    void Shortcuts_maybe(){
	        bool isDim3D =  DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d;
	        if(!isDim3D){ return; }
	        if (KeyMousePenInput.isSomeInputFieldActive()) { return; }

	        if(Input.GetKeyDown(KeyCode.A) && KeyMousePenInput.isRMBpressed()==false){
	            _showAlphaOnly_toggle.isOn = !_showAlphaOnly_toggle.isOn;
	        }
	        if(Input.GetKeyDown(KeyCode.B) && KeyMousePenInput.isKey_CtrlOrCommand_pressed()){
	            OnButton_RemBG();
	        }
	    }


	    void Refresh_SliderTexts(){
	        int value     = Mathf.RoundToInt(_rembg_backgroundThresh.value);
	        string valStr = value < 100? value.ToString() : $"<size=90%>{value}</size>";
	        _rembg_backgroundTxt.text = valStr;

	        value  = Mathf.RoundToInt(_rembg_foregroundThresh.value);
	        valStr = value < 100? value.ToString() : $"<size=90%>{value}</size>";
	        _rembg_foregroundTxt.text = valStr;
	    }
    

	    void Refresh_ScreenshotToggle(){
	         //always force as off unless we are in 3D.  Helps to untoggle it when we are no longer in the 3d.
	        _makeScreenshots_toggle.isOn &=  DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d; ;

	        // doing it here, without callback from the 'Act_OnPaintStrokeEnd' callback of the 'Background_Painter'.
	        // That's because must animate even if background doesn't exist (and we can't brush it):
	        bool wantScreenshot = _is_can_take_screenshots &&
	                                  KeyMousePenInput.isLMBreleasedThisFrame() &&
	                                  KeyMousePenInput.isKey_alt_pressed() == false &&
	                                  KeyMousePenInput.isKey_CtrlOrCommand_pressed() == false &&
	                                  MainViewport_UI.instance.isCursorHoveringMe();
	        if(wantScreenshot && Screenshot_MGR.instance.isPrefferCaptureSnippets()==false){
	            string msg = "Screenshots are possible only if a 3D generator is connected.";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 3, false);
	        }
	    }


	    void OnSomeScreenshotTaken(bool isBecauseMouseDragged)
	        => _takeScreenshots_toggleAnim.Play(); //little bouncing animation, so that user can remember that they are still painting.


	    void Update(){
	        FadeWholePanel();
	        Refresh_ScreenshotToggle();
	        Shortcuts_maybe();
	        Refresh_SliderTexts();
	    }

	    public void Save(StableProjectorz_SL spz){
	        spz.gen3D_WorkflowOptionsRibbon = new Gen3D_WorkflowOptionsRibbon_SL();
	        spz.gen3D_WorkflowOptionsRibbon.rembg_backgroundThresh = _rembg_backgroundThresh.value;
	        spz.gen3D_WorkflowOptionsRibbon.rembg_foregroundThresh = _rembg_foregroundThresh.value;
	    }

	    public void Load(StableProjectorz_SL spz){
	        if (spz.gen3D_WorkflowOptionsRibbon == null) return;
	        _rembg_backgroundThresh.SetSliderValue(spz.gen3D_WorkflowOptionsRibbon.rembg_backgroundThresh, true);
	        _rembg_foregroundThresh.SetSliderValue(spz.gen3D_WorkflowOptionsRibbon.rembg_foregroundThresh, true);
	    }

	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;

	        _rembg_button.onClick.AddListener( OnButton_RemBG );
	        //Begin the toggle as false, but true->false, to trigger the callback:
	        _makeScreenshots_toggle.onValueChanged.AddListener( OnMakeScreenshotsToggle );
	        _makeScreenshots_toggle.isOn = true;
	        _makeScreenshots_toggle.isOn = false;
        
	        _rgba_to_a_mat = new Material(_rgba_to_a_shader);

	        WireOptionToggleChromeRefresh(_showAlphaOnly_toggle);
	        WireOptionToggleChromeRefresh(_makeScreenshots_toggle);
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void WireOptionToggleChromeRefresh(Toggle toggle) {
	        if (toggle == null) return;
	        toggle.onValueChanged.RemoveListener(OnOptionToggleChromeChanged);
	        toggle.onValueChanged.AddListener(OnOptionToggleChromeChanged);
	    }

	    void OnOptionToggleChromeChanged(bool _) => RefreshOptionToggleChrome();

	    /// <summary>Re-tint Alpha/Screenshots fills from current isOn (BoundChrome only).</summary>
	    public void RefreshOptionToggleChrome() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) return;
	        var t = SpzUiThemeOps.Active;
	        ThemeToggle(_showAlphaOnly_toggle, t);
	        ThemeToggle(_makeScreenshots_toggle, t);
	    }

	    void Start(){
	        Screenshot_MGR._Act_OnScreenshot += OnSomeScreenshotTaken;
	    }

	    void OnEnable() {
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        if (_showAlphaOnly_toggle != null)
	            _showAlphaOnly_toggle.onValueChanged.RemoveListener(OnOptionToggleChromeChanged);
	        if (_makeScreenshots_toggle != null)
	            _makeScreenshots_toggle.onValueChanged.RemoveListener(OnOptionToggleChromeChanged);
	        Screenshot_MGR._Act_OnScreenshot -= OnSomeScreenshotTaken;
	        DestroyImmediate(_rgba_to_a_mat);
	        if (instance == this)
	            instance = null;
	    }

	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            if (_wholePanel_canvGrp != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_wholePanel_canvGrp.transform);
	            RestoreCircle(_rembg_backgroundThresh);
	            RestoreCircle(_rembg_foregroundThresh);
	            RestoreGraphic(_rembg_backgroundTxt);
	            RestoreGraphic(_rembg_foregroundTxt);
	            RestoreSelectable(_showAlphaOnly_toggle);
	            RestoreSelectable(_makeScreenshots_toggle);
	            RestoreSelectable(_rembg_button);
	            if (_direction != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_direction.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_wholePanel_canvGrp != null) {
	            var panelImg = _wholePanel_canvGrp.GetComponent<Image>();
	            if (panelImg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(panelImg, t.panelBg);
	            SpzUiThemeOps.ApplyBoundChromeRolesUnder(_wholePanel_canvGrp.transform, new SpzUiThemeRoleMatrixOptions {
	                PreferFlatToolToggles = true,
	                Exclude = c => {
	                    if (c is TextMeshProUGUI tmp && (
	                            ReferenceEquals(tmp, _rembg_backgroundTxt)
	                            || ReferenceEquals(tmp, _rembg_foregroundTxt)))
	                        return true;
	                    if (_rembg_button != null && c is Button b && ReferenceEquals(b, _rembg_button))
	                        return true;
	                    if (_rembg_button != null && c.transform != null
	                        && (c.transform == _rembg_button.transform
	                            || c.transform.IsChildOf(_rembg_button.transform)))
	                        return true;
	                    // BrushRibbon owns Gen3D direction strip — RolesUnder SolidSquare blanks it after BrushRibbon themes.
	                    if (c.GetComponentInParent<BrushRibbon_UI_Direction>(true) != null) return true;
	                    return false;
	                },
	            });
	        }
	        ThemeTmp(_rembg_backgroundTxt, t);
	        ThemeTmp(_rembg_foregroundTxt, t);
	        ThemeCircle(_rembg_backgroundThresh, t);
	        ThemeCircle(_rembg_foregroundThresh, t);
	        ThemeToggle(_showAlphaOnly_toggle, t);
	        ThemeToggle(_makeScreenshots_toggle, t);
	        if (_rembg_button != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_rembg_button);
	            // Icon-as-face rembg — SolidSquare blanks the glyph (Gen3D Soft litmus).
	            if (SpzUiThemeOps.IsAuthoredIconFace(_rembg_button.targetGraphic)) {
	                if (_rembg_button.targetGraphic is Image rembgFace)
	                    SpzUiThemeOps.ApplyBoundChromeIconTint(rembgFace, t.iconTint);
	            } else {
	                SpzUiThemeOps.ApplyBoundChromeSelectable(_rembg_button, t.controlBg, t.accent);
	            }
	            foreach (var tmp in _rembg_button.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	                if (tmp != null)
	                    SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, t.textPrimary, 11f);
	            }
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_rembg_button);
	        }
	    }

	    static void RestoreSelectable(Selectable s) {
	        if (s != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(s.transform);
	    }

	    static void RestoreGraphic(Graphic g) => SpzUiThemeOps.RestoreAuthoredGraphic(g);

	    static void RestoreCircle(CircleSlider_Snapping_UI slider) {
	        if (slider != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(slider.transform);
	    }

	    static void ThemeTmp(TextMeshProUGUI tmp, SpzUiThemeOps.ThemeTokens t) {
	        if (tmp == null) return;
	        // Rembg thresh numerals — DialValue so strip tracking does not steal dial hits (SD Soft litmus).
	        SpzUiThemeOps.ApplyBoundChromeDialValueTmp(tmp, t.textPrimary, 14f);
	        tmp.raycastTarget = false;
	    }

	    static void ThemeCircle(CircleSlider_Snapping_UI slider, SpzUiThemeOps.ThemeTokens t) {
	        if (slider != null)
	            slider.ApplyThemeTokens(t.accent, t.textPrimary);
	    }

	    static void ThemeToggle(Toggle toggle, SpzUiThemeOps.ThemeTokens tokens) {
	        if (toggle == null) return;
	        Color face = toggle.isOn
	            ? Color.Lerp(tokens.tabActive, tokens.accent, 0.45f)
	            : tokens.controlBg;
	        // Alpha/Screenshots are bevel tool radios — flat fill + hide Checkmark plate (not checkbox silo).
	        SpzUiThemeOps.ThemeFlatToolToggle(toggle, face, tokens.accent, tokens.textPrimary);
	    }

	}
}//end namespace
