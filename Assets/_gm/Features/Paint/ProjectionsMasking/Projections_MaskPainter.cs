using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;

namespace spz {

	//allows us to drag mouse in viewport and "draw" a 2D screen-space mask.
	//This mask gets immideately projected onto a 3d object.
	public class Projections_MaskPainter : MaskPainter{
	    public static Projections_MaskPainter instance { get; private set; }  = null;

	    [SerializeField] Shader _brushShader_noCVTRaster; //without Conservative Raster. For older GPUs.
	    [Space(10)]
	    [SerializeField] ApplyBrushStroke_ToUvMask _applyBrushStroke_toUvMask;

	    float _prevStrength = 0;

	    public static Action Act_OnPaintStrokeEnd { get; set; } = null;


	    void OnMaskInvert_button(){
	        if(MainViewport_UI.instance.showing != MainViewport_UI.Showing.UsualView){ return; }
	        GenData2D genData = getGenData_currentIcon();
	        if(genData==null){ return; }
	        if(genData._masking_utils == null){ return; }
	        if (_applyBrushStroke_toUvMask == null) _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);
	        if (_applyBrushStroke_toUvMask == null){ return; }

	        for(int i=0;  i<genData._masking_utils._ObjectUV_brushedMaskR8.Count;  ++i){
	            RenderUdims br  = genData._masking_utils._ObjectUV_brushedMaskR8[i];
	            RenderUdims vis = genData._masking_utils._ObjectUV_visibilityR8G8[i];
	            if(br==null){ continue; }   
	            _applyBrushStroke_toUvMask.InvertMask(br, vis);
	        }
	        Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }


	    protected override void OnBucketFill_button(){
	        if(MainViewport_UI.instance.showing != MainViewport_UI.Showing.UsualView){ return; }
        
	        GenData2D genData = getGenData_currentIcon();
	        if(genData==null){ return; }
	        if(genData._masking_utils == null){ return; }

	        Color col = SD_WorkflowOptionsRibbon_UI.instance.isPositive? Color.white : Color.black;
	        PaintUndo_MGR.EnsureExists();
	        StartCoroutine(BucketFillWithUndo_Coroutine(genData, col));
	    }

	    /// <summary>True while the main selected art icon still owns this generation data (user did not switch icons / deselect).</summary>
	    bool BucketFillTargetStillCurrent (GenData2D startedWith) =>
	        startedWith != null && getGenData_currentIcon() == startedWith;

	    /// <summary>Capture each POV mask to undo before modifying it. Waits for each capture to finish so later POVs are not snapshotted after fill.</summary>
	    IEnumerator BucketFillWithUndo_Coroutine (GenData2D targetGenData, Color col){
	        var mu = targetGenData._masking_utils;
	        if (mu == null)
		        yield break;

	        for (int i = 0; i < mu._ObjectUV_brushedMaskR8.Count; ++i){
	            if (!BucketFillTargetStillCurrent(targetGenData))
		            yield break;

	            RenderUdims br = mu._ObjectUV_brushedMaskR8[i];
	            RenderUdims vis = mu._ObjectUV_visibilityR8G8[i];
	            if (br == null)
		            continue;

	            PaintUndo_MGR.instance?.SchedulePreStrokeCapture(br, PaintUndoNonStackTarget.ProjectionGenMask, i);
	            while (PaintUndo_MGR.instance != null && PaintUndo_MGR.instance.IsBusy) {
		            if (!BucketFillTargetStillCurrent(targetGenData))
			            yield break;
		            yield return null;
	            }

	            if (!BucketFillTargetStillCurrent(targetGenData))
		            yield break;

	            OnBucketFill_orDelete_button(col, br.texArray, vis.texArray);
	        }
	    }

	    protected override void OnDelete_button(){ 
	        /*not doing anything, to avoid confusion. User should use fill for the masks.*/ 
	    }


	    protected override bool isAllowedToShow_BrushCursorNow(){
	        return MainViewport_UI.instance.showing == MainViewport_UI.Showing.UsualView;
	    }

	    protected override bool isAllowedToPaintNow( bool also_check_viewportHovered ){
	        bool isAllowed =  MainViewport_UI.instance?.showing == MainViewport_UI.Showing.UsualView;
	             isAllowed &= DimensionMode_MGR.instance?._dimensionMode == DimensionMode.dim_sd;
	             isAllowed &= MultiView_Ribbon_UI.instance?._isEditingMode?? false;
	             isAllowed &= !SD_WorkflowOptionsRibbon_UI.instance?.IsEyeDropperMagnified?? false;
	             isAllowed &= !ClickSelect_Meshes_MGR.instance?._isSelectMode?? false;
	             isAllowed &= !GlobalClickBlocker.isLocked();
	        if (also_check_viewportHovered){
	            isAllowed &= MainViewport_UI.instance?.isCursorHoveringMe()?? false;
	        }
	        return isAllowed;
	    }


	    //are we fine-tuning one of the textures of the provided generation, or not:
	    public bool isPainting_in_Generation(GenData2D genData){
	        var artList = Art2D_IconsUI_List.instance;
	        if (artList == null){ return false; }
        
	        IconUI icon =  artList._mainSelectedIcon;
	        if (icon == null){ return false; }

	        return genData == icon._genData;
	    }


	    public override Vector2 getViewportSize(){
	        return MainViewport_UI.instance.mainViewportRect.rect.size;
	    }

	    public override Vector2 getViewportCursorPos01(bool forceMainViewport=false){
	        return MainViewport_UI.instance.cursorMainViewportPos01;
	    }

	    protected override Vector3Int maskResolution(){
	        IReadOnlyList<UDIM_Sector> allUdims = ModelsHandler_3D.instance._allKnownUdims;
	        int numSlices = allUdims.Count;
	        return new Vector3Int( GenData_Masks.MASK_RESOLUTION,  GenData_Masks.MASK_RESOLUTION,  numSlices);
	    }

	    protected override float getBrushStrength(){//strength [0,1] --> [-1,1]
	        var orib = SD_WorkflowOptionsRibbon_UI.instance;
	        if (KeyMousePenInput.isPenEraserPressed()) return -orib.maskBrushOpacity;
	        if (KeyMousePenInput.isPenTipPressed()) return orib.maskBrushOpacity;
	        return orib.maskBrushOpacity * (orib.isPositive?1:-1);
	    }


	    protected override void InitTextures( int width,  int height,  int numSlices, 
	                                          out RenderTexture prevBrushPath_,  out RenderTexture currBrushPath_){
	        prevBrushPath_ = null;
	        currBrushPath_ = null;
	        if (numSlices <= 0 || width <= 0 || height <= 0)
		        return;

	        prevBrushPath_ = TextureTools_SPZ.CreateTextureArray( new Vector2Int(width,height), GraphicsFormat.R8_UNorm, 
	                                                             FilterMode.Bilinear, numSlices, depthBits:0);

	        currBrushPath_ = TextureTools_SPZ.CreateTextureArray( new Vector2Int(width,height), GraphicsFormat.R8_UNorm, 
	                                                             FilterMode.Bilinear, numSlices, depthBits:0);
	        TextureTools_SPZ.ClearRenderTexture(prevBrushPath_, Color.black);
	        TextureTools_SPZ.ClearRenderTexture(currBrushPath_, Color.black);
	    }


	    protected override void OnRenderIntoCurrTex_please( RenderTexture prevBrushStroke_R8, RenderTexture currBrushStroke_R8,
	                                                        bool isFirstFrameOfStroke, float suggested_brushStrength ){
	        GenData2D genData = getGenData_currentIcon();
	        if(genData==null){ return; }
	        if(genData._masking_utils==null){ return; }

	        if(isFirstFrameOfStroke){ _prevStrength = suggested_brushStrength; }
        
	        int povIx =  MultiView_Ribbon_UI.instance.currentPovIx;
	        if (povIx < 0 || povIx >= genData._masking_utils._ObjectUV_visibilityR8G8.Count) return;
        
	        RenderUdims visibil =  genData._masking_utils._ObjectUV_visibilityR8G8[povIx];
	        if (visibil == null) return;
	        RenderUdims.SetNumUdims(visibil, _brushMaterial);

	        _brushMaterial.SetTexture("_ProjVisibility", visibil.texArray );
	        _brushMaterial.SetTexture("_PrevBrushPathTex", prevBrushStroke_R8);
	        Texture2D stamp = BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback();
	        _brushMaterial.SetTexture("_BrushStamp", stamp); 
	        _brushMaterial.SetVector("_BrushStrength", new Vector4(_prevStrength,suggested_brushStrength,0,0));
	        bool isErasing = suggested_brushStrength < 0;
	        _brushMaterial.SetFloat("_BrushStampStronger", isErasing? 2 : 0.0f);

	        bool isMultiView = genData._masking_utils.numPOV>1;
	        bool fadeByNormal = !isMultiView && SD_WorkflowOptionsRibbon_UI.instance.isPositive;
	        _brushMaterial.SetFloat("_FadeByNormal", fadeByNormal? 1 : 0);

	        var selectedMeshes = ModelsHandler_3D.instance.selectedMeshes;
	        Objects_Renderer_MGR.instance.EquipMaterial_on_Specific( selectedMeshes, _brushMaterial );

	        UserCameras_MGR.instance._curr_viewCamera.RenderImmediate_Arr( renderIntoHere:currBrushStroke_R8,  ignore_nonSelected_meshes:true,
	                                                                       _brushMaterial,  useClearingColor:false,  Color.clear, dontFrustumCull:true);
	        float sign = Mathf.Sign(suggested_brushStrength);
	        if (_applyBrushStroke_toUvMask == null)
	            _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);
	        if (_applyBrushStroke_toUvMask != null) {
	            var mu = genData._masking_utils;
	            // Single-POV only: Apply_intoMask_multiView can write every POV mask in one dispatch (shrink others).
	            // Undo is one snapshot per stack entry; capturing only the active POV would leave other masks wrong on restore.
	            if (isFirstFrameOfStroke && mu != null && mu.numPOV == 1 && mu._ObjectUV_brushedMaskR8 != null
	                && povIx >= 0 && povIx < mu._ObjectUV_brushedMaskR8.Count) {
	                var uvMask = mu._ObjectUV_brushedMaskR8[povIx];
	                if (uvMask != null && uvMask.texArray != null) {
	                    PaintUndo_MGR.EnsureExists();
	                    PaintUndo_MGR.instance?.SchedulePreStrokeCapture(uvMask, PaintUndoNonStackTarget.ProjectionGenMask, povIx);
	                }
	            }
	            _applyBrushStroke_toUvMask.Apply_into_MaskUtils(prevBrushStroke_R8, currBrushStroke_R8,  sign,  genData._masking_utils, povIx);
	        }
	        else
	            Debug.LogWarning("[Projections_MaskPainter] ApplyBrushStroke_ToUvMask not found. Mask will not persist.");

	        _prevStrength = suggested_brushStrength;
	        Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }


	    protected override void OnFinal_ApplyIncomingVals_intoMask( RenderTexture prevBrushStroke_R8, RenderTexture currBrushStroke_R8 ){
	        Act_OnPaintStrokeEnd?.Invoke();
	    }
    

	    GenData2D getGenData_currentIcon(){
	        var artList = Art2D_IconsUI_List.instance;
	        if(artList == null){ return null; }
        
	        IconUI icon =  artList._mainSelectedIcon;
	        if(icon == null){ return null; }

	        return icon._genData;
	    }


	    protected override void Awake(){
	      #if UNITY_EDITOR
	        if(UnityEditor.EditorApplication.isPlaying==false){ return; }
	      #endif
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        if (_applyBrushStroke_toUvMask == null)
	            _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);

	        BrushRibbon_UI_InvertMask.onClicked += OnMaskInvert_button;
	        BrushRibbon_UI_BucketFill._Act_onClicked += OnBucketFill_button;
	        BrushRibbon_UI_DeleteButton.onClicked += OnDelete_button;

	        if (Pen.current != null  &&  Pen.current.deviceId != Pen.InvalidDeviceId){
	            Viewport_StatusText.instance.ShowStatusText($"Drawing Tablet '{Pen.current.displayName}' detected, "
	                                                        +$"will use pressure when brushing.", false, 5, progressVisibility:false );
	        }

	        base.Awake();

	        PaintUndo_MGR.EnsureExists();

	        if (SystemInfo.supportsConservativeRaster == false){
	            DestroyImmediate(base._brushMaterial);
	            base._brushMaterial = new Material(_brushShader_noCVTRaster);
	        }
	    }

	    protected override void OnDestroy(){
	        BrushRibbon_UI_InvertMask.onClicked -= OnMaskInvert_button;
	        BrushRibbon_UI_BucketFill._Act_onClicked -= OnBucketFill_button;
	        BrushRibbon_UI_DeleteButton.onClicked -= OnDelete_button;
	        base.OnDestroy();
	    }
	}
}//end namespace
