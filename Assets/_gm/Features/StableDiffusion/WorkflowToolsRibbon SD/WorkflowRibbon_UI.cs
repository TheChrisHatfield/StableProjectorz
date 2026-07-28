using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	public enum WorkflowRibbon_CurrMode{
	    ProjectionsMasking,
	    Inpaint_Color,
	    Inpaint_NoColor,
	    TotalObject,
	    WhereEmpty,
	    AntiShade,//not used at the moment
	}

	public enum InpaintingFill{ Fill=0, Original=1, LatentNoise=2, LatentNothing=3, }


	public class WorkflowRibbon_UI : MonoBehaviour {
	    public static WorkflowRibbon_UI instance { get; private set; } = null;

	    [SerializeField] RectTransform _ribbonHoverZone;
	    [SerializeField] RectTransform _ribbonNoHoverZone;
	    [Space(10)]
	    [SerializeField] GameObject _holderGO_turnMeOnOff;
	    [SerializeField] WorkflowRibbon_ProjMask_UI _projMasking;
	    [SerializeField] WorkflowRibbon_Colors_UI _coloring;
	    [SerializeField] WorkflowRibbon_NoColor_UI _colorless;
	    [SerializeField] WorkflowRibbon_EntireObject_UI _entireObj;
	    [SerializeField] WorkflowRibbon_WhereEmpty_UI _WhereEmpty_UI;
	    [SerializeField] WorkflowRibbon_AntiShade_UI _AntiShade_UI;

	    bool _skipShortcutHint = false;
	    int _shortcutHint_numShown = 0;

	    public static Action<WorkflowRibbon_CurrMode> _Act_OnModeChanged { get; set; } = null;
	    public static Action Act_onBakeColors_button { get; set; } = null;

	    public bool isHoveredByCursor { get; private set; } = false;
	    public bool isPressedByCursor { get; private set; }//maybe no longer hovered, but still dragging one of our sliders.

	    public bool has_brushed_mask()
	        =>Inpaint_MaskPainter.instance.isPaintMaskEmpty==false;

	    public bool has_background_mask() 
	        => ArtBG_IconsUI_List.instance.hasBackground(considerGradientColors:true);


	    // If there is a background (always generates whole silhuette) or brushed.
	    // if we are in the workflow mode which always generates mask on its own.
	    public bool has_auto_mask(){
	        switch (currentMode()){
	            case WorkflowRibbon_CurrMode.WhereEmpty:
	            case WorkflowRibbon_CurrMode.TotalObject:
	                return true;
	            default:
	                return false;
	        }
	    }

	    public bool allowed_to_showBrushMask(){
	        switch (currentMode()){
	            case WorkflowRibbon_CurrMode.Inpaint_Color:
	            case WorkflowRibbon_CurrMode.Inpaint_NoColor:
	                return true;
	            default:
	                return false;
	        }
	    }

	    public bool isMode_using_img2img(){
	        switch (currentMode()){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking: return false;
	            default: return true;
	        }
	    }

	    public bool is_allow_SoftInpaint(){
	        switch (currentMode()){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking: //for example, dealing with backgrounds etc.
	            case WorkflowRibbon_CurrMode.WhereEmpty:
	            case WorkflowRibbon_CurrMode.AntiShade:
	                return false;
	            default:
	                return true;
	        }
	    }

	    public InpaintingFill Get_InpaintFill(){
	        // If we are sending entire EMPTY silhuette, use LatentNothing (for WhereEmpty or Backgrounds etc).
	        // For such empty silhuettes, LatentNoise gives bad quality - Sept 2024.
	        // For such empty silhuettes, Original doesn't work too. Even at 100% denoise it still looks under mask,
	        // and gives dark results.
	        // Even if we add noise here to the ViewTex, around silhuette and use Original, - LatentNothing still wins.
	        // Soft inpaint makes stuff pale, so we will use usual inpaint.  'Nothing' looks better than Original with usual Inpaint.
	        switch (currentMode()){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking:
	                return InpaintingFill.LatentNothing; //for example, dealing with backgrounds etc.

	            case WorkflowRibbon_CurrMode.Inpaint_Color:
	                return InpaintingFill.Original;
                
	            case WorkflowRibbon_CurrMode.Inpaint_NoColor:
	                return InpaintingFill.Original;

	            case WorkflowRibbon_CurrMode.TotalObject:
	                return InpaintingFill.Original;

	            case WorkflowRibbon_CurrMode.WhereEmpty:
	                return InpaintingFill.LatentNothing;

	            case WorkflowRibbon_CurrMode.AntiShade:
	                return InpaintingFill.LatentNothing;
                
	            default:
	                return InpaintingFill.LatentNothing;
	        }
	    }


	    public WorkflowRibbon_CurrMode currentMode(){
	        if(_projMasking.isOn){ return WorkflowRibbon_CurrMode.ProjectionsMasking;  }
	        // No Color before Color: if both toggles were ever left true (no ToggleGroup), inpaint mask must match No Color when that mode is selected.
	        if(_colorless.isOn){   return WorkflowRibbon_CurrMode.Inpaint_NoColor;  }
	        if(_coloring.isOn){    return WorkflowRibbon_CurrMode.Inpaint_Color;  }
	        if(_entireObj.isOn){   return WorkflowRibbon_CurrMode.TotalObject;  }
	        if(_WhereEmpty_UI.isOn){ return WorkflowRibbon_CurrMode.WhereEmpty; }
	        if(_AntiShade_UI.isOn){  return WorkflowRibbon_CurrMode.AntiShade; }
	        return WorkflowRibbon_CurrMode.ProjectionsMasking;
	    }


	    bool _isSettingCurrentMode = false;
	    public void Set_CurrentMode(WorkflowRibbon_CurrMode mode, bool playAttentionAnim=false){
	        if(_isSettingCurrentMode){ return; }//avoid recursion
	        _isSettingCurrentMode = true;

	        IWorkflowModeToggle toggle = null;
	        switch (mode){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking: toggle = _projMasking; break;
	            case WorkflowRibbon_CurrMode.Inpaint_Color:    toggle = _coloring; break;
	            case WorkflowRibbon_CurrMode.Inpaint_NoColor: toggle = _colorless; break;
	            case WorkflowRibbon_CurrMode.TotalObject:  toggle = _entireObj; break;
	            case WorkflowRibbon_CurrMode.WhereEmpty: toggle = _WhereEmpty_UI; break;
	            case WorkflowRibbon_CurrMode.AntiShade: toggle = _AntiShade_UI; break;
	            default: break;
	        }
	        _projMasking.SetOffWithoutNotify();
	        _coloring.SetOffWithoutNotify();
	        _colorless.SetOffWithoutNotify();
	        _entireObj.SetOffWithoutNotify();
	        _WhereEmpty_UI.SetOffWithoutNotify();
	        _AntiShade_UI.SetOffWithoutNotify();
	        toggle.EnableToggle(playAttentionAnim);

	        _Act_OnModeChanged?.Invoke(mode);
	        _isSettingCurrentMode = false;
	    }


    
	    void OnToggle_ValueChanged(IWorkflowModeToggle tog){
	        Set_CurrentMode( GetMode_from_Toggle(tog), playAttentionAnim:false );
	        ApplyThemeTokens();
	        if (WorkflowRibbon_ProjMask_UI.didShowHint_thisFrame()){ return; }
	        if (WorkflowRibbon_Colors_UI.didShowHint_thisFrame()){ return; }
	        if (WorkflowRibbon_NoColor_UI.didShowHint_thisFrame()){ return; }//to avoid showing our own hint. (theirs is more important and rare).
	        if (DimensionMode_MGR.instance._dimensionMode != DimensionMode.dim_sd){ return; }

	        if (_skipShortcutHint){ return; }

	        if(_shortcutHint_numShown < 4){
	            _shortcutHint_numShown++;
	            string modifier_key =  Settings_MGR.instance.get_useCtrlScroll_for_WorkflowMode_swaps() ? "Ctrl" : "Shift";
	            Viewport_StatusText.instance.ShowStatusText($"{modifier_key} + Mouse Scroll Wheel to change mode easier :)", false, 2, false);
	        }
	    }

	    WorkflowRibbon_CurrMode GetMode_from_Toggle( IWorkflowModeToggle tog ){
	        if(ReferenceEquals(tog,_projMasking)){ return WorkflowRibbon_CurrMode.ProjectionsMasking;  }
	        if(ReferenceEquals(tog,_coloring)){   return WorkflowRibbon_CurrMode.Inpaint_Color;  }
	        if(ReferenceEquals(tog,_colorless)){   return WorkflowRibbon_CurrMode.Inpaint_NoColor;  }
	        if(ReferenceEquals(tog,_entireObj)){   return WorkflowRibbon_CurrMode.TotalObject;  }
	        if(ReferenceEquals(tog,_WhereEmpty_UI)){ return WorkflowRibbon_CurrMode.WhereEmpty; }
	        if(ReferenceEquals(tog,_AntiShade_UI)){ return WorkflowRibbon_CurrMode.AntiShade; }
	        return WorkflowRibbon_CurrMode.ProjectionsMasking;
	    }

	    IWorkflowModeToggle Get_Toggle_of_currentMode(){
	        switch (currentMode()){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking: return _projMasking; break;
	            case WorkflowRibbon_CurrMode.Inpaint_Color: return _coloring; break;
	            case WorkflowRibbon_CurrMode.Inpaint_NoColor: return _colorless; break;
	            case WorkflowRibbon_CurrMode.TotalObject: return _entireObj; break;
	            case WorkflowRibbon_CurrMode.WhereEmpty: return _WhereEmpty_UI; break;
	            case WorkflowRibbon_CurrMode.AntiShade: return _AntiShade_UI; break;
	            default: Debug.Assert(false, $"unknown mode in {nameof(Get_Toggle_of_currentMode)}"); break;
	        }
	        return _projMasking;
	    }

  
	    void OnBrushStrokeEnd(){
	        //Play animation, so user won't be confused if they were trying to erase/add:
	        Animation anim = (Get_Toggle_of_currentMode() as Component).GetComponent<Animation>();
	        if(anim==null){ return; }
	        anim.Play();
	    }


	    void EarlyUpdate(){
	        Check_if_Hovered();
	        Scroll_to_ChangeMode_maybe();
	        // Keep content camera rendering when in img2img mode so "what to send" (ControlNet) and capture show current scene + layers. Restores visual indicator that data is being pushed through.
	        if (UserCameras_Permissions.contentCam_keepRendering != null)
	            UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.ContentUserCam, this, isLock: isMode_using_img2img());
	    }

	    void Check_if_Hovered(){
        
	        isHoveredByCursor  =  RectTransformUtility.RectangleContainsScreenPoint( _ribbonHoverZone, KeyMousePenInput.cursorScreenPos());
	        isHoveredByCursor &= !RectTransformUtility.RectangleContainsScreenPoint(_ribbonNoHoverZone, KeyMousePenInput.cursorScreenPos());

	        if (isHoveredByCursor && KeyMousePenInput.isLMBpressedThisFrame()){
	            isPressedByCursor = true;
	        }
	        if(KeyMousePenInput.isLMBpressed()==false){
	            isPressedByCursor = false;
	        }
	    }


	    void Scroll_to_ChangeMode_maybe(){
	        if (KeyMousePenInput.isFileBrowserOpen()) { return; }
	        //we either Shift+Scroll or Ctrl+Scroll, depends on the preferences:
	        bool use_ctrl = Settings_MGR.instance.get_useCtrlScroll_for_WorkflowMode_swaps();
	        if (use_ctrl){ 
	            if(KeyMousePenInput.isKey_CtrlOrCommand_pressed() == false){ return; }
	        }else { 
	            if(KeyMousePenInput.isKey_Shift_pressed() == false){ return; }
	        }

	        if(Input.mouseScrollDelta.y == 0){ return; }

	        RectTransform curr = (Get_Toggle_of_currentMode() as Component).transform as RectTransform;
	        int num = curr.parent.childCount;

	        int nextIx = curr.GetSiblingIndex();

	        while (true){//keep reducing/increasing the index until we get to the child that has the toggle:
	            if(Input.mouseScrollDelta.y < 0){
	                nextIx++;
	                if(nextIx >= num){ nextIx = 0; }
	            }else{
	                nextIx--;
	                if(nextIx < 0){ nextIx = num-1; }
	            }
	            var tog = curr.parent.GetChild(nextIx).GetComponent<IWorkflowModeToggle>();
	            if (tog == null){ continue; } //some children (bg or frame) aren't toggles, skip them.

	            _skipShortcutHint = true;
	            Set_CurrentMode(GetMode_from_Toggle(tog));
	            _skipShortcutHint = false;
	            break;
	            //toggleGroup will untoggle the old one.
	        }
	    }


	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        Projections_MaskPainter.Act_OnPaintStrokeEnd += OnBrushStrokeEnd;
	        Inpaint_MaskPainter.Act_OnPaintStrokeEnd += OnBrushStrokeEnd;

	        _projMasking.onValueChanged+= isOn=>{if(isOn){ OnToggle_ValueChanged(_projMasking); }};
	        _coloring.onValueChanged   += isOn=>{if(isOn){ OnToggle_ValueChanged(_coloring); }};
	        _colorless.onValueChanged  += isOn=>{if(isOn){ OnToggle_ValueChanged(_colorless); }};
	        _entireObj.onValueChanged  += isOn=>{if(isOn){ OnToggle_ValueChanged(_entireObj);} };
	        _WhereEmpty_UI.onValueChanged += isOn=>{ if(isOn){ OnToggle_ValueChanged(_WhereEmpty_UI); }};
	        _AntiShade_UI.onValueChanged += isOn=>{ if(isOn){ OnToggle_ValueChanged(_AntiShade_UI); }};
        
	        _coloring.onBakeColors_button += ()=> Act_onBakeColors_button?.Invoke();
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void Start(){
	         EarlyUpdate_callbacks_MGR.instance.onEarlyUpdate3 += EarlyUpdate;
	         ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.ContentUserCam, this, isLock: false);
	        if (instance == this)
	            instance = null;
	    }

	    /// <summary>Themes known workflow mode toggles and selected states only.</summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            if (_holderGO_turnMeOnOff != null)
	                SpzUiThemeOps.RestoreAuthoredGraphic(_holderGO_turnMeOnOff.GetComponent<Image>());
	            RestoreWorkflowModeAuthored(_projMasking as MonoBehaviour);
	            RestoreWorkflowModeAuthored(_coloring as MonoBehaviour);
	            RestoreWorkflowModeAuthored(_colorless as MonoBehaviour);
	            RestoreWorkflowModeAuthored(_entireObj as MonoBehaviour);
	            RestoreWorkflowModeAuthored(_WhereEmpty_UI as MonoBehaviour);
	            RestoreWorkflowModeAuthored(_AntiShade_UI as MonoBehaviour);
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        ThemeModeToggle(_projMasking as MonoBehaviour, _projMasking != null && _projMasking.isOn, StudioLineIcon.Camera, t);
	        ThemeModeToggle(_coloring as MonoBehaviour, _coloring != null && _coloring.isOn, StudioLineIcon.Brush, t);
	        ThemeModeToggle(_colorless as MonoBehaviour, _colorless != null && _colorless.isOn, StudioLineIcon.Drop, t);
	        ThemeModeToggle(_entireObj as MonoBehaviour, _entireObj != null && _entireObj.isOn, StudioLineIcon.Mesh, t);
	        ThemeModeToggle(_WhereEmpty_UI as MonoBehaviour, _WhereEmpty_UI != null && _WhereEmpty_UI.isOn, StudioLineIcon.Expand, t);
	        ThemeModeToggle(_AntiShade_UI as MonoBehaviour, _AntiShade_UI != null && _AntiShade_UI.isOn, StudioLineIcon.Layers, t);
	        if (_holderGO_turnMeOnOff != null) {
	            var holderImg = _holderGO_turnMeOnOff.GetComponent<Image>();
	            if (holderImg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(holderImg, t.panelBg);
	        }
	    }

	    static void RestoreWorkflowModeAuthored(MonoBehaviour modeUi) {
	        if (modeUi == null) return;
	        SpzUiThemeOps.RestoreBoundChromeUnder(modeUi.transform);
	    }

	    /// <summary>Nomad sculpt strip: solid cell + line icon above Roboto label (BoundChrome only).</summary>
	    static void ThemeModeToggle(MonoBehaviour modeUi, bool selected, StudioLineIcon glyph, SpzUiThemeOps.ThemeTokens t) {
	        if (modeUi == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome)
	            return;
	        var toggle = modeUi.GetComponentInChildren<Toggle>(true);
	        if (toggle != null) {
	            Color normal = selected ? t.tabActive : t.controlBg;
	            SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, normal, t.accent);
	            if (toggle.targetGraphic is Image tgImg)
	                SpzUiThemeOps.ApplyRoundedControlSprite(tgImg, markEligible: true);
	            // Authored checkmark is a beveled ON plate — selection = flat fill only (Brush/Multiview parity).
	            if (toggle.graphic is Image check && check != toggle.targetGraphic)
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(check);
	            foreach (var img in toggle.GetComponentsInChildren<Image>(true)) {
	                if (img == null || img == toggle.targetGraphic) continue;
	                if (SpzUiThemeOps.IsToggleCheckmarkGraphic(img)) {
	                    SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	                    continue;
	                }
	                string n = img.gameObject.name ?? "";
	                if (n == "MonolithActiveBar" || n == "MonolithLineIcon") continue;
	                if (n.Equals("Checkmark", StringComparison.OrdinalIgnoreCase)
	                    || n.IndexOf("pressed", StringComparison.OrdinalIgnoreCase) >= 0
	                    || n.Equals("tick", StringComparison.OrdinalIgnoreCase))
	                    SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	            }
	        }
	        else {
	            var img = modeUi.GetComponent<Image>();
	            if (img != null) {
	                SpzUiThemeOps.ApplyBoundChromeGraphic(img, selected ? t.tabActive : t.controlBg);
	                SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	            }
	        }
	        Transform iconOwner = toggle != null ? toggle.transform : modeUi.transform;
	        SpzUiThemeOps.ApplyNomadStackedToolCell(
	            iconOwner,
	            glyph,
	            t.textPrimary,
	            20f,
	            tmp => !IsExcludedWorkflowLabel(tmp.transform, modeUi.transform));
	    }

	    static bool IsExcludedWorkflowLabel(Transform label, Transform modeRoot) {
	        if (label == null || modeRoot == null) return true;
	        Transform t = label;
	        while (t != null && t != modeRoot) {
	            string cn = t.name ?? "";
	            if (StartsWithToken(cn, "Option") || StartsWithToken(cn, "Hover")
	                || StartsWithToken(cn, "Panel") || StartsWithToken(cn, "Slide"))
	                return true;
	            t = t.parent;
	        }
	        return false;
	    }

	    static bool StartsWithToken(string name, string token) {
	        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(token)) return false;
	        if (!name.StartsWith(token, StringComparison.OrdinalIgnoreCase)) return false;
	        return name.Length == token.Length || !char.IsLetterOrDigit(name[token.Length]);
	    }

    
	    public void Save(StableProjectorz_SL spz){
	        spz.sd_workflowRibbon = spz.sd_workflowRibbon??new SD_WorkflowRibbon_SL();
	        spz.sd_workflowRibbon.workflowMode = currentMode().ToString();
	    }

	    public void Load(StableProjectorz_SL spz){
	        string modeStr = spz.sd_workflowRibbon?.workflowMode ?? "";
	        object mode;
	        bool parsed = System.Enum.TryParse(typeof(WorkflowRibbon_CurrMode), modeStr, out mode);
	        {
	            WorkflowRibbon_CurrMode val = parsed? (WorkflowRibbon_CurrMode)mode : WorkflowRibbon_CurrMode.ProjectionsMasking;
	            Set_CurrentMode(val);
	        }
	    }


	}
}//end namespace
