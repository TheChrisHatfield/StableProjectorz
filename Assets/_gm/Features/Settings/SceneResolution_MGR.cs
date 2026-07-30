using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	public class SceneResolution_MGR : MonoBehaviour{
	    [Space(10)]
	    [SerializeField] TextMeshProUGUI _save_texResQuality_text;
	    [SerializeField] Button _sub_texResolutionQuality;
	    [SerializeField] Button _add_texResolutionQuality;
	    [SerializeField] Toggle _textureFilterPoint_toggle;
	    [SerializeField] Toggle _textureFilterBilinear_toggle;

	    int _memorized_res = -1;//usually used before we reduce resolution, during generation, to help performance.
	    int _memorized_res_b4_projectSave;
	    int _memorized_res_b4_pixels_boost;//when generating we INCREASE resolution to 5k for small moment, to avoid blurring when inpainting.

	    //determines resolution/quality of texture that we'll save to file.
	    // Also affects the quality of projections (ProjectionCameras.instance.accomulation_RT)
	    public static int resultTexQuality { get; private set; } = 2048;
	    public bool _isSavingProject_keepResolution4k { get; private set; } = false;
	    public bool _isWillGenArt_keepResolution5k { get; private set; } = false;
	    public static FilterMode resultTexFilterMode { get; private set; } = FilterMode.Bilinear;

	    /// <summary>While accumulation is temporarily upscaled for img2img capture, <see cref="Inpaint_MaskPainter.maskResolution"/> must keep using this square size for layer RTs — otherwise <c>InitTextures</c> calls <c>EnsureResolution</c> at the boosted size and wipes all paint.</summary>
	    public static int PaintLayerSquareSizeDuringImg2ImgAccumBoost { get; private set; } = -1;
	    /// <summary>Same idea as <see cref="PaintLayerSquareSizeDuringImg2ImgAccumBoost"/> for the brief 4k bump during project save composite.</summary>
	    public static int PaintLayerSquareSizeDuringSaveCompositeBoost { get; private set; } = -1;

	    /// <summary>Set by the latest <see cref="OnWillRequest_img2img"/> only when that invocation actually changed prep (not a no-op duplicate while already boosted).
	    /// Used so <see cref="RevertImg2ImgAccumBoostIfPreRequestFailed"/> does not undo an in-flight request's prep.</summary>
	    public static bool LastImg2imgWillAppliedPrep { get; private set; }

	    public bool HasMemorizeRes() 
	        => _memorized_res != -1;
    

	    public void RevertRes_from_Memorized(){
	        if(!HasMemorizeRes()){ return; }
	        OnAdd_texResolutionQuality(true, force_pickThisRes:_memorized_res);
	        _memorized_res = -1;
	    }


	    public void OnAdd_texResolutionQuality(bool increase, int force_pickThisRes=-1, bool memorize_before=false){
	        if(memorize_before){
	            _memorized_res = resultTexQuality;
	        }
	        switch (resultTexQuality){
	            case 32:  resultTexQuality =  increase?48:32;  break; //low res is needed for pixelated styl
	            case 48:  resultTexQuality =  increase?64:32;  break; //(point-filtering). Gradual decrease near low vals.
	            case 64:  resultTexQuality =  increase?96:48;  break;
	            case 96:  resultTexQuality =  increase?128:64;  break;
	            case 128:  resultTexQuality =  increase?192:96;  break;
	            case 192:  resultTexQuality =  increase?256:128;  break;
	            case 256:  resultTexQuality =  increase?384:192;  break;
	            case 384:  resultTexQuality =  increase?512:256;  break;
	            case 512:  resultTexQuality =  increase?768:384;  break;
	            case 768:  resultTexQuality =  increase?1024:512;  break;
	            case 1024:  resultTexQuality =  increase?2048:768;  break;
	            case 2048:  resultTexQuality =  increase?3072:1024;  break;
	            case 3072:  resultTexQuality =  increase?4096:2048;  break;
	            case 4096:  resultTexQuality =  increase?5120:3072;  break;
	            case 5120:  resultTexQuality =  increase?6144:4096;  break;
	            case 6144:  resultTexQuality =  increase?7168:5120;  break;
	            case 7168:  resultTexQuality =  increase?8192:6144;  break;
	            case 8192:  resultTexQuality =  increase?8192:7168;  break;
	        }
	        resultTexQuality = force_pickThisRes>0? force_pickThisRes :  Mathf.Clamp(resultTexQuality, 32, 4096*2);
	        Objects_Renderer_MGR.instance.Resize_AccumulationTexture( resultTexQuality );
	        string abbr = textureRes_to_abbreviation();
	        _save_texResQuality_text.text = "SAVE " + abbr;
	    } 


	    string textureRes_to_abbreviation(){
	        string qualityTxt = resultTexQuality>=32 ? "32" : "?";
	        qualityTxt = resultTexQuality>=48 ? "48" : qualityTxt;
	        qualityTxt = resultTexQuality>=64 ? "64" : qualityTxt;
	        qualityTxt = resultTexQuality>=96 ? "96" : qualityTxt;
	        qualityTxt = resultTexQuality>=128 ? "128" : qualityTxt;
	        qualityTxt = resultTexQuality>=192 ? "192" : qualityTxt;
	        qualityTxt = resultTexQuality>=256 ? "256" : qualityTxt;
	        qualityTxt = resultTexQuality>=500 ? "0.5K": qualityTxt;
	        qualityTxt = resultTexQuality>=1000 ? "1K" : qualityTxt;
	        qualityTxt = resultTexQuality>=2000 ? "2K" : qualityTxt;
	        qualityTxt = resultTexQuality>=3000 ? "3K" : qualityTxt;
	        qualityTxt = resultTexQuality>=4000 ? "4K" : qualityTxt;
	        qualityTxt = resultTexQuality>=5000 ? "5K" : qualityTxt;
	        qualityTxt = resultTexQuality>=6000 ? "6K" : qualityTxt;
	        qualityTxt = resultTexQuality>=7000 ? "7K" : qualityTxt;
	        qualityTxt = resultTexQuality>=8000 ? "8K" : qualityTxt;
	        return qualityTxt;
	    }


	    void OnTextureFilterMode_Toggle(bool isOn, Toggle toggle){
	        bool allToggles_off =  _textureFilterPoint_toggle.isOn==false && _textureFilterBilinear_toggle==false;
        
	        if(allToggles_off){//disallow the total disabling. One toggle must remain on:
	            toggle.SetIsOnWithoutNotify(toggle.isOn);
	        }
	        if(!isOn){ RefreshFilterToggleChrome(); return; }
	        Objects_Renderer_MGR.instance.ReRenderAll_soon();
	        // Toggle-group doesn't work on these toggles because some might be hidden (slide out panel is concealed).
	        // So, untoggling them manually:
	        if(toggle == _textureFilterPoint_toggle){  
	            _textureFilterBilinear_toggle.isOn = false;  
	            resultTexFilterMode=FilterMode.Point;
	        }
	        if(toggle == _textureFilterBilinear_toggle){  
	            _textureFilterPoint_toggle.isOn = false; 
	            resultTexFilterMode=FilterMode.Bilinear; 
	        }
	        RefreshFilterToggleChrome();
	    }


	    //NOTICE: very important, because we will send a view of our scene to stableDiffusion img2img.
	    // It is crucial that we send the highest-res possible, not 1k or 2k.
	    // Otherwise, SD will preserve it around the faded-borders of the mask.
	    // And we would be projecting such returned result into our 1k or 2k accumulation-texture.
	    // This would cause quality to degrade with each generation inside that inpaint location.
	    void OnWillRequest_img2img(){
	        // Second Will while the first img2img coroutine is still preparing would overwrite memorized res and
	        // confuse revert / OnRequested; accumulation is already boosted — skip.
	        if (_isWillGenArt_keepResolution5k){
		        LastImg2imgWillAppliedPrep = false;
		        return;
	        }
	        LastImg2imgWillAppliedPrep = true;
	        _isWillGenArt_keepResolution5k = true;
	        _memorized_res_b4_pixels_boost = resultTexQuality;
	        if (PaintLayerSquareSizeDuringImg2ImgAccumBoost < 0 && resultTexQuality > 0)
		        PaintLayerSquareSizeDuringImg2ImgAccumBoost = resultTexQuality;
	        if(resultTexQuality >= 5120){ return; }
	        OnAdd_texResolutionQuality(true, force_pickThisRes:5120);//while generating, ensure at least 5k res.
	    }

	    /// <summary>Call after img2img/upscale payload is sent (same moment as <c>_Act_img2img_requested</c>).
	    /// Invoked from <see cref="StableDiffusion_Hub"/> <em>before</em> the multicast event so other subscribers cannot block or preempt this cleanup.</summary>
	    public static void ApplyImg2ImgResolutionCleanupAfterPayloadSent(){
	        var mgr = Object.FindObjectOfType<SceneResolution_MGR>(true);
	        mgr?.ApplyImg2ImgResolutionCleanupAfterPayloadSent_Instance();
	    }

	    void ApplyImg2ImgResolutionCleanupAfterPayloadSent_Instance(){
	        // No-op if nothing applied this session (avoids forcing resolution from stale _memorized_res_b4_pixels_boost, e.g. default 0).
	        if (!_isWillGenArt_keepResolution5k && PaintLayerSquareSizeDuringImg2ImgAccumBoost < 0 && !LastImg2imgWillAppliedPrep){ return; }
	        // Restore accumulation before clearing the layer-RT hold; otherwise maskResolution() can briefly
	        // see 5k accumulation with no stable override and EnsureResolution wipes paint.
	        OnAdd_texResolutionQuality(false, force_pickThisRes:_memorized_res_b4_pixels_boost);
	        PaintLayerSquareSizeDuringImg2ImgAccumBoost = -1;
	        _isWillGenArt_keepResolution5k = false;
	        LastImg2imgWillAppliedPrep = false;
	    }

	    /// <summary><see cref="OnWillRequest_img2img"/> runs before the coroutine's <c>Start_GenerationRequest</c>.
	    /// If that start fails, <see cref="ApplyImg2ImgResolutionCleanupAfterPayloadSent"/> never runs — call this from the failure path
	    /// so accumulation and flags are not left at the 5k prep state.</summary>
	    public static void RevertImg2ImgAccumBoostIfPreRequestFailed(){
	        var mgr = Object.FindObjectOfType<SceneResolution_MGR>(true);
	        mgr?.RevertImg2ImgAccumBoostIfPreRequestFailed_Instance();
	    }

	    void RevertImg2ImgAccumBoostIfPreRequestFailed_Instance(){
	        if (!_isWillGenArt_keepResolution5k && PaintLayerSquareSizeDuringImg2ImgAccumBoost < 0){ return; }
	        OnAdd_texResolutionQuality(false, force_pickThisRes:_memorized_res_b4_pixels_boost);
	        PaintLayerSquareSizeDuringImg2ImgAccumBoost = -1;
	        _isWillGenArt_keepResolution5k = false;
	        LastImg2imgWillAppliedPrep = false;
	    }
    
	    void OnWillMake_FinalCompositeImg(){
	        _isSavingProject_keepResolution4k = true;
	        _memorized_res_b4_projectSave = resultTexQuality;
	        if (PaintLayerSquareSizeDuringSaveCompositeBoost < 0 && resultTexQuality > 0)
		        PaintLayerSquareSizeDuringSaveCompositeBoost = resultTexQuality;
	        if(resultTexQuality >= 4096){ return; }
	        OnAdd_texResolutionQuality(true, force_pickThisRes:4096);//while saving, ensure at least 4k res.
	    }

	    void OnMade_FinalCompositeImg(){
	        OnAdd_texResolutionQuality(false, force_pickThisRes:_memorized_res_b4_projectSave);
	        PaintLayerSquareSizeDuringSaveCompositeBoost = -1;
	        _isSavingProject_keepResolution4k = false;
	    }


	    public void Save(StableProjectorz_SL spz){
	        spz.sceneResolution = new SceneResolution_SL();
	        spz.sceneResolution.scene_texResolution = resultTexQuality;
	        spz.sceneResolution.scene_texFilterMode = resultTexFilterMode.ToString();
	    }

	    public void Load(StableProjectorz_SL spz){
	        if (spz.sceneResolution == null) return;
	        int newRes = spz.sceneResolution.scene_texResolution;
	        bool isIncreaseRes =  newRes>= resultTexQuality;
	        OnAdd_texResolutionQuality(isIncreaseRes, force_pickThisRes:newRes);
        
	        bool isPoint = spz.sceneResolution.scene_texFilterMode.ToLower().Contains("point");
	        Toggle filterToggle = isPoint? _textureFilterPoint_toggle : _textureFilterBilinear_toggle;
	        OnTextureFilterMode_Toggle(true, filterToggle);
	    }


	    void Awake(){
	        EventsBinder.Bind_Clickable_to_event(nameof(SceneResolution_MGR), this);

	        _sub_texResolutionQuality.onClick.AddListener( ()=>OnAdd_texResolutionQuality(increase:false) );
	        _add_texResolutionQuality.onClick.AddListener( ()=>OnAdd_texResolutionQuality(increase:true) );

	        _textureFilterPoint_toggle.onValueChanged.AddListener( isOn=>OnTextureFilterMode_Toggle(isOn, _textureFilterPoint_toggle) );
	        _textureFilterBilinear_toggle.onValueChanged.AddListener( isOn=>OnTextureFilterMode_Toggle(isOn, _textureFilterBilinear_toggle) );
	        _save_texResQuality_text.text = "SAVE " + textureRes_to_abbreviation();

	        StableDiffusion_Hub._Act_img2img_willRequest += OnWillRequest_img2img;

	        ProjectSaveLoad_Helper._onWillMake_FinalCompositeImg += OnWillMake_FinalCompositeImg;
	        ProjectSaveLoad_Helper._onMade_FinalCompositeImg += OnMade_FinalCompositeImg;

	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	    }

	    /// <summary>Colors SAVE Nx / +/- / point-bilinear chrome from the active palette.</summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            UnwindBoundChrome(_save_texResQuality_text != null
	                ? _save_texResQuality_text.GetComponentInParent<Button>()
	                : null);
	            UnwindBoundChrome(_sub_texResolutionQuality);
	            UnwindBoundChrome(_add_texResolutionQuality);
	            UnwindBoundChrome(_textureFilterPoint_toggle);
	            UnwindBoundChrome(_textureFilterBilinear_toggle);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_save_texResQuality_text != null) {
	            var saveBtn = _save_texResQuality_text.GetComponentInParent<Button>();
	            // Solid-square litmus (same path as all Nomad BoundChrome selectables).
	            if (saveBtn != null) {
	                SpzUiThemeOps.EnsureSelectableHitFace(saveBtn);
	                SpzUiThemeOps.ApplyBoundChromeSelectable(saveBtn, t.success, t.accent);
	            }
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(_save_texResQuality_text, t.textPrimary, 11f);
	            if (saveBtn != null)
	                SpzUiThemeOps.ClearNonFaceRaycastsForTheme(saveBtn);
	        }
	        // Prefab +/- may ship with null targetGraphic — Ensure before ClearNonFace.
	        if (_sub_texResolutionQuality != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_sub_texResolutionQuality);
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_sub_texResolutionQuality, t.controlBg, t.accent);
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_sub_texResolutionQuality);
	        }
	        if (_add_texResolutionQuality != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_add_texResolutionQuality);
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_add_texResolutionQuality, t.controlBg, t.accent);
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_add_texResolutionQuality);
	        }
	        ThemeFilterToggle(_textureFilterPoint_toggle, t);
	        ThemeFilterToggle(_textureFilterBilinear_toggle, t);
	    }

	    static void UnwindBoundChrome(Selectable sel) {
	        if (sel == null) return;
	        SpzUiThemeOps.RestoreBoundChromeUnder(sel.transform);
	    }

	    /// <summary>Re-tint Point/Bilinear fills from current isOn (BoundChrome only).</summary>
	    public void RefreshFilterToggleChrome() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) return;
	        var t = SpzUiThemeOps.Active;
	        ThemeFilterToggle(_textureFilterPoint_toggle, t);
	        ThemeFilterToggle(_textureFilterBilinear_toggle, t);
	    }

	    static void ThemeFilterToggle(Toggle tgl, SpzUiThemeOps.ThemeTokens t) {
	        if (tgl == null) return;
	        Color fill = tgl.isOn
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	        // Point/Bilinear are tool radios with bevel Checkmark plates (raycastTarget=1) — flat path.
	        SpzUiThemeOps.ThemeFlatToolToggle(tgl, fill, t.accent, t.textPrimary);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(tgl);
	    }
	}
}//end namespace
