using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using UnityEngine.InputSystem.LowLevel;

namespace spz {

	//Shown at the bottom of the interface, always visible to the user
	public class GenerateButtons_Main_UI : GenerateButtons_UI{
	    public static GenerateButtons_Main_UI instance { get; private set; } = null;

	    /// <summary>Viewport left-ribbon Generate Art button (for sibling UI such as full-view toggle).</summary>
	    public RectTransform GenArtButtonRectTransform =>
		    _generateART_button != null ? _generateART_button.transform as RectTransform : null;

	    protected void OnStartedGenerate_cb(){
	        _cancelGeneration_button.gameObject.SetActive(true);
	        _cancelGeneration_button.interactable = true;

	        if(_deleteLast_button!=null){  _deleteLast_button.gameObject.SetActive(false); }
	    }

	    protected void OnFinishedGenerate_cb(bool canceled){
	        if(_cancelGeneration_button.gameObject.activeSelf == false){ 
	            return; //important! helps avoid double invoke (with cancel:true and cancel:false)
	        }
	        _cancelGeneration_button.gameObject.SetActive(false);
	        _cancelGeneration_button.interactable = false;
	        if(_deleteLast_button!=null){  _deleteLast_button.gameObject.SetActive(!canceled); }
	    }

	    protected override void Awake(){
	        if (instance != null){ DestroyImmediate(this.gameObject); return; }
	        instance = this;
	        base.Awake();
	        //subscribe to our base class's action:
	        GenerateButtons_UI._Act_OnGenerate_started += OnStartedGenerate_cb;
	        GenerateButtons_UI._Act_OnGenerate_finished += OnFinishedGenerate_cb;
	        // Sync this newly-created UI instance to current global generation state.
	        if (GenerateButtons_UI.isGenerating){
	            OnStartedGenerate_cb();
	        }else{
	            OnFinishedGenerate_cb(canceled: true);
	        }
	    }
	}


	// Knows about several UI buttons that can initiate generation.
	// Also has button for cancelling the generation.
	//
	// Abstract, because there are several singleton child classes:
	// the Main (always visible) and the Mini (for context menu)
	public abstract class GenerateButtons_UI : MonoBehaviour{

	    [SerializeField] protected Button _cancelGeneration_button;
	    [SerializeField] protected DoubleClickButton_UI _deleteLast_button;
	    [Space(10)]
	    [SerializeField] protected MouseHoverSensor_UI _genArt_button_hoverArea;
	    [SerializeField] protected Button _generateART_button;
	    [SerializeField] protected Button _generateBG_button;
	    [SerializeField] protected Button _generate3D_button;
	    [SerializeField] protected Button _generate3D_retexture_button;

	    // We don't set the .interactable of the buttons themselves, because we want
	    // to still capture their press, if user attempts to click them during "inactive".
	    protected static bool _genArt_button_interactable = false;
	    protected static bool _genBG_button_interactable = false;
	    protected static bool _gen3D_button_interactable = false;
	    protected static bool _gen3D_retex_button_interactable = false;

	    //We also use these actions to notify child classes when someone invoked our static OnStartedGenerate():
	    public static Action _Act_OnGenerate_started {get;set;} = null;
	    public static Action<bool> _Act_OnGenerate_finished {get;set;} = null;//bool is 'isCancelled'

	    public static Action OnCancelGenerationButton { get; set; } = null;
	    public static Action OnDeleteGenerationButton { get; set; } = null;
	    public static Action OnGenerateArtButton { get; set; } = null;
	    public static Action OnGenerateBG_Button { get; set; } = null;
	    public static Action OnGenerate3D_Button { get; set; } = null;
	    public static Action OnGenerate3D_retexture_Button { get; set; } = null;

	    public bool isHovering_GenArtButton   => _genArt_button_hoverArea.isHovering;
                  
	    public bool isShowingCancelGen_button => _cancelGeneration_button.gameObject.activeInHierarchy  &&
	                                             _cancelGeneration_button.interactable;

	    public static bool isGenerating { get; private set; } = false;
	    public static bool isGeneratingPaused { get; private set; } = false;//for example if made previews and user needs to resume.


	    // STATIC. Invokes a protected STATIC Action, so that ALL child classes realize they need to do something.
	    // This makes sure we won't forget to invoke it on all child classes.
	    // Just invoke this OnStartedGenerate() on this abstract class, and all child instances will take care.
	    public static void OnConfirmed_StartedGenerate(){ 
	        isGenerating = true;
	        isGeneratingPaused = false;
	        _Act_OnGenerate_started?.Invoke();
	    }

	    public static void OnConfirmed_GeneratePaused(bool is_paused){
	        isGeneratingPaused = is_paused;//for example if made previews and user will need to resume rest of generation soon.
	    }

	    public static void OnConfirmed_FinishedGenerate(bool canceled){
	        isGenerating = false;
	        isGeneratingPaused = false;
	        _Act_OnGenerate_finished?.Invoke(canceled);
	    }


    
	    void OnDeleteLastButton(){
	        //if hidden don't proceed. We only want to delete the most recent generation.
	        //Not the ones that were before it, to avoid complicating things for the user.
	        if(_deleteLast_button.gameObject.activeSelf==false){ return; }
	        OnDeleteGenerationButton?.Invoke();
	        _deleteLast_button.gameObject.SetActive(false);
	    }

	    void OnButton_GenArt_if_allowed(){
	        // Soft "_genArt_button_interactable" only drives face alpha / tooltips.
	        // Always invoke Hub so DenyWithMessage matches Ctrl+G (OG comment: still capture presses when inactive).
	        OnGenerateArtButton?.Invoke();
	    }

	    void OnButton_GenBG_if_allowed(){
	        OnGenerateBG_Button?.Invoke();
	    }

	    void OnButton_Gen3D_if_allowed(){
	        if(_gen3D_button_interactable == false){ return; }
	        OnGenerate3D_Button?.Invoke();
	    }

	    void OnButton_Gen3D_Retexture_if_allowed(){
	        if(_gen3D_retex_button_interactable == false){ return; }
	        OnGenerate3D_retexture_Button?.Invoke();
	    }



	    void OnDimensionChanged(DimensionMode dim){
	        switch (dim){
	            case DimensionMode.dim_uv:
	            case DimensionMode.dim_sd:
	                _generateART_button.gameObject.SetActive(true);
	                _generateBG_button.gameObject.SetActive(true);
	                _generate3D_button.gameObject.SetActive(false);//false
	                _generate3D_retexture_button.gameObject.SetActive(false);//false
	                break;
	            case DimensionMode.dim_gen_3d:
	                _generateART_button.gameObject.SetActive(false);
	                _generateBG_button.gameObject.SetActive(false);
	                _generate3D_button.gameObject.SetActive(true);//true
	                _generate3D_retexture_button.gameObject.SetActive(true);//true
	                break;
	        }
	    }

	    void Refresh_is_interactable(){
	        if(StableDiffusion_Hub.instance == null){ return; }
	        if(DimensionMode_MGR.instance == null){ return; }

	        // GEN ART / BG must not wait on Gen3D_MGR — OG early-return left soft-interactable stuck false
	        // (and clicks silent) whenever the 3D generate scene/manager was not yet awake.
	        StableDiffusion_Hub.instance.isCanGenerate(out _genArt_button_interactable, out _genBG_button_interactable);
	        _genArt_button_interactable &= DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_sd  &&
	                                       isGenerating==false  &&  isGeneratingPaused == false;
        
	        _genBG_button_interactable  &= DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_sd  &&
	                                       isGenerating==false  &&  isGeneratingPaused==false;

	        if(Gen3D_MGR.instance == null) {
	            _gen3D_button_interactable = false;
	            _gen3D_retex_button_interactable = false;
	            return;
	        }

	        _gen3D_button_interactable  = Gen3D_MGR.isCanStart_make_meshes_and_tex() && Gen3D_MGR.isSupports_make_meshes_and_tex() && 
	                                      DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d  &&//3d
	                                      isGenerating==false  &&  isGeneratingPaused==false;

	        _gen3D_retex_button_interactable = Gen3D_MGR.isCanStart_retexture() && Gen3D_MGR.isSupports_retexture() && 
	                                           DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d  &&//3d
	                                           isGenerating==false  &&  isGeneratingPaused==false;
	    }

	    void RefreshColors_of_GenArt_buttons(){
	        ApplyGenButtonFace(_generateART_button, _genArt_button_interactable);
	        ApplyGenButtonFace(_generateBG_button, _genBG_button_interactable);
	        ApplyGenButtonFace(_generate3D_button, _gen3D_button_interactable);
	        ApplyGenButtonFace(_generate3D_retexture_button, _gen3D_retex_button_interactable);
	    }

	    static void ApplyGenButtonFace(Button btn, bool interactable) {
	        if (btn == null || btn.image == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreAuthoredGraphic(btn.image);
	            var restored = btn.image.color;
	            restored.a = interactable ? 1f : 0.5f;
	            btn.image.color = restored;
	            return;
	        }
	        // Soft-disable alpha only — do not rewrite RGB every Update (that kills ColorTint hover/press).
	        var c = btn.image.color;
	        float targetA = interactable ? 1f : 0.5f;
	        if (Mathf.Abs(c.a - targetA) > 0.001f) {
	            c.a = targetA;
	            btn.image.color = c;
	        }
	    }

	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            RestoreGenButton(_generateART_button);
	            RestoreGenButton(_generateBG_button);
	            RestoreGenButton(_generate3D_button);
	            RestoreGenButton(_generate3D_retexture_button);
	            RestoreGenButton(_cancelGeneration_button);
	            if (_deleteLast_button != null) {
	                var delBtn = _deleteLast_button.GetComponent<Button>();
	                RestoreGenButton(delBtn);
	            }
	            RefreshColors_of_GenArt_buttons();
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        ThemeGenButton(_generateART_button, t);
	        ThemeGenButton(_generateBG_button, t);
	        ThemeGenButton(_generate3D_button, t);
	        ThemeGenButton(_generate3D_retexture_button, t);
	        ThemeDockChromeButton(_cancelGeneration_button, t.danger, t, labelSize: 12f);
	        if (_deleteLast_button != null)
	            ThemeDockChromeButton(_deleteLast_button.GetComponent<Button>(), t.controlBg, t, labelSize: 12f);
	        RefreshColors_of_GenArt_buttons();
	    }

	    static void ThemeDockChromeButton(Button btn, Color fill, SpzUiThemeOps.ThemeTokens t, float labelSize) {
	        if (btn == null) return;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        if (btn.targetGraphic == null) return;
	        SpzUiThemeOps.ApplyBoundChromeSelectable(btn, fill, t.accent);
	        if (btn.targetGraphic is Image face) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
	            face.preserveAspect = false;
	            face.raycastTarget = true;
	        }
	        foreach (var img in btn.GetComponentsInChildren<Image>(true)) {
	            if (img == null || img == btn.targetGraphic) continue;
	            string n = img.gameObject.name ?? "";
	            if (n.IndexOf("triangle", System.StringComparison.OrdinalIgnoreCase) >= 0)
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	        }
	        var label = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
	        if (label != null)
	            SpzUiThemeOps.ApplyBoundChromeNarrowDockLabelTmp(label, t.textPrimary, labelSize);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    static void ThemeGenButton(Button btn, SpzUiThemeOps.ThemeTokens t) {
	        ThemeDockChromeButton(btn, t.controlBg, t, labelSize: 14f);
	    }

	    static void RestoreGenButton(Button btn) {
	        if (btn != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(btn.transform);
	    }

	    void UpdateTooltips_GenButtons(Button genArt, Button genBG, Button gen3D, Button gen3D_retex){
	        var tooltip_art = genArt.GetComponent<CanShowTooltip_UI>();
	        var tooltip_bg  = genBG.GetComponent<CanShowTooltip_UI>();
	        var tooltip_3d  = gen3D.GetComponent<CanShowTooltip_UI>();
	        var tooltip_3d_retex = gen3D_retex.GetComponent<CanShowTooltip_UI>();
	        string msg_genArt = "<b>Ctrl + G</b> to generate\nfaster, without clicks.";
	        string msg_genBG = "<b>Shift + G</b> to generate\nfaster, without clicks.";
	        string msg_gen3D = "<b>Ctrl + G</b> to generate\nfaster, without clicks.";
	        string msg_gen3D_retex = "Re-Texture the existing mesh, with the current settings.\nOnly available if your 3D generator supports this.";
	        if (!_genArt_button_interactable){
	            msg_genArt = "To generate projections, enable one\nControlNetUnit with Depth or Normals model.\n(See <b>CTRL NETS</b> tab)";
	        }
	        if (!_genBG_button_interactable){
	            msg_genBG = "To generate projections, enable one\nControlNetUnit with Depth or Normals model.\n(See <b>CTRL NETS</b> tab)";
	        }
	        if (!Connection_MGR.is_sd_connected){
	            msg_genBG = msg_genArt = SdDisconnectPlaceholder.TooltipText;
	        }

	        if(!Connection_MGR.is_3d_connected){
	            msg_gen3D = "Not connected to a 3D-generator black window yet.\nUse the button at the top to launch a 3D-generator, or find" +
	                        "\nand double-click the file 'run-fp16.bat' or 'run.bat'.\nFor the connection see bottom right corner.";
	            msg_gen3D_retex = msg_gen3D;
	        }
	        tooltip_art.set_overrideMessage(msg_genArt);
	        tooltip_bg.set_overrideMessage(msg_genBG);
	        tooltip_3d.set_overrideMessage(msg_gen3D);
	        tooltip_3d_retex.set_overrideMessage(msg_gen3D_retex);
	    }


	    protected virtual void Update(){
	        Refresh_is_interactable();
	        RefreshColors_of_GenArt_buttons();
	        UpdateTooltips_GenButtons(_generateART_button, _generateBG_button, _generate3D_button, _generate3D_retexture_button);
	    }

	    protected virtual void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        DimensionMode_MGR._Act_OnDimensionChanged -= OnDimensionChanged;
	    }

	    protected virtual void Awake(){
	        _cancelGeneration_button.onClick.AddListener( ()=>OnCancelGenerationButton?.Invoke() );

	        if (_deleteLast_button != null){
	            _deleteLast_button.onConfirmedClick += OnDeleteLastButton;
	        }
	        _generateART_button.onClick.AddListener( OnButton_GenArt_if_allowed );
	        _generateBG_button.onClick.AddListener( OnButton_GenBG_if_allowed );
	        _generate3D_button.onClick.AddListener( OnButton_Gen3D_if_allowed );
	        _generate3D_retexture_button.onClick.AddListener( OnButton_Gen3D_Retexture_if_allowed );

	        DimensionMode_MGR._Act_OnDimensionChanged += OnDimensionChanged;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	        // Keep local visuals consistent without mutating global generation state during late init.
	        if(isGenerating){
	            _cancelGeneration_button.gameObject.SetActive(true);
	            _cancelGeneration_button.interactable = true;
	            if(_deleteLast_button != null){ _deleteLast_button.gameObject.SetActive(false); }
	        }else{
	            _cancelGeneration_button.gameObject.SetActive(false);
	            _cancelGeneration_button.interactable = false;
	            if(_deleteLast_button != null){ _deleteLast_button.gameObject.SetActive(false); }
	        }
	    }
    
	}
}//end namespace
