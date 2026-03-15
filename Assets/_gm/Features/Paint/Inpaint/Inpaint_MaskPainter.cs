using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace spz {

	//allows us to drag mouse in viewport and "draw" a 2D screen-space mask.
	public class Inpaint_MaskPainter : MaskPainter{
	    public static Inpaint_MaskPainter instance { get; private set; } = null;

	    [Space(10)]
	    [SerializeField] Texture _colorlessMaskChecker_tex;
	    [Space(10)]
	    [SerializeField] Shader _brushShader_noCVTRaster; //without Conservative Raster. For older GPUs.
	    [SerializeField] ApplyBrushStroke_ToUvMask _applyBrushStroke_toUvMask;
	    [SerializeField] Shader _blitApplyEntireColorLayer_shader;
	    [Space(10)]
	    [SerializeField] Inpaint_ScreenMasker _inpaintScreenMasker;
	    [Space(10)]
	    [SerializeField] float _hoverFadeIn_speed = 7;//how soon brushed color becomes visible, when we hover mainViewport
	    [SerializeField] float _hoverFadeOut_speed = 0.5f;

	    float _prevStrength = 0;
	    RenderTexture _latestBrushStroke_ref;//doesn't belong to us, belongs to parent class

	    Material _blitApplyEntireColorLayer_mat;
	    RenderUdims _layerStackCompositeTemp; // when using layer stack, composite output for blit

	    public bool isPaintMaskEmpty { get; private set; } = true;
	    /// <summary>Single-layer paint target (when PaintLayerStack_MGR is not used). When layer stack is used, paint goes to active layer. </summary>
	    public RenderUdims _ObjectUV_brushedColorRGBA { get; private set; }

	    bool _subscribedActiveLayerChanged;
	    bool _loggedDisplaySourceOnce;

	    /// <summary>Paint target: active layer Content directly. Compute shader writes strokes into the same
	    /// texture the renderer reads, so GPU command serialization guarantees strokes are visible on the same
	    /// frame they're painted (no Data/Content timing gap). Ensures scene is injected before use.</summary>
	    RenderUdims GetPaintTarget()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack != null && !_subscribedActiveLayerChanged)
		    {
			    _subscribedActiveLayerChanged = true;
			    stack.OnActiveLayerChanged += OnActiveLayerChanged_EnsureContent;
		    }
		    if (stack != null && stack.ActiveLayer != null && stack.ActiveLayerRenderUdims == null)
		    {
			    var res = maskResolution();
			    if (res.x > 0 && res.y > 0 && res.z > 0)
				    stack.EnsureResolution(res);
		    }
		    EnsureSceneInjectedIntoActiveLayer();
		    var layerContent = stack?.ActiveLayerRenderUdims;
		    if (layerContent != null)
			    return layerContent;
		    return _ObjectUV_brushedColorRGBA;
	    }
	    public static Action Act_OnPaintStrokeEnd { get; set; } = null;


	    public SoftInpaintingArgs GetArgs_for_SoftInpaint_GenRequest(){

	        if(!SD_WorkflowOptionsRibbon_UI.instance.isSoftInpaint){ return null; }

	        var entry = new SoftInpaintingArgsEntry{};//keep default values, they don't have much difference (Jul 2024)

	        var sft_args = new SoftInpaintingArgs(){
	            args = new SoftInpaintingArgsEntry[1]{ entry },
	        };
	        return sft_args;
	    }


	    // Takes all of the color that we've brushed, and applies into accumulation texture.
	    // This is typically done as the final step during projection-cams-renders (not during painting).
	    public void ApplyColorLayer_To_UV_Textures( RenderUdims ontoHere ){
        
	        if (_blitApplyEntireColorLayer_mat == null)
	        {
		        Debug.LogError("[Inpaint] _blitApplyEntireColorLayer_mat is null – Awake likely crashed. Cannot display paint.");
		        return;
	        }
	        bool isColorless  = WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor;
	        bool willSendToSD = StableDiffusion_Hub.instance._finalPreparations_beforeGen;
	        if(isColorless && willSendToSD){ return; }//Don't blit, keep as is.
	        if(Save_MGR.instance._isSaving){ return; }//Never blit colors if saving. Or merging icons into one, because no ctrl+z.

	        // Scene is injected into active layer for painting. Display: when 2+ layers use composite of all visible (so layer 1 stays visible on layer 2); when 1 layer use active Content so paint never disappears (no composite timing gap).
	        EnsureSceneInjectedIntoActiveLayer();
	        RenderUdims source = null;
	        float layerOpacity01 = 1f;
	        var stack = PaintLayerStack_MGR.instance;
	        bool useComposite = stack != null && _ObjectUV_brushedColorRGBA != null && stack.Layers != null && stack.Layers.Count > 1;
	        if (useComposite)
	        {
		        EnsureLayerStackCompositeTemp(_ObjectUV_brushedColorRGBA);
		        if (_layerStackCompositeTemp != null)
		        {
			        stack.CompositeToOnTopOfBase(_ObjectUV_brushedColorRGBA, _layerStackCompositeTemp);
			        source = _layerStackCompositeTemp;
			        layerOpacity01 = 1f;
		        }
	        }
	        if (source == null && stack != null && stack.ActiveLayer?.Content != null && _ObjectUV_brushedColorRGBA != null)
	        {
		        var activeContent = stack.ActiveLayer.Content;
		        if (activeContent.width == _ObjectUV_brushedColorRGBA.width && activeContent.height == _ObjectUV_brushedColorRGBA.height && activeContent.UdimsCount == _ObjectUV_brushedColorRGBA.UdimsCount)
			        source = activeContent;
	        }
	        if (source == null)
	        {
		        source = _ObjectUV_brushedColorRGBA;
		        layerOpacity01 = 1f;
	        }

	        if (source == null)
	        {
		        Debug.LogWarning("[Inpaint] Paint not shown on mesh: no paint source (color buffer and layer stack empty or null). Load a 3D model and paint in the viewport.");
		        return;
	        }

	        if (!_loggedDisplaySourceOnce)
	        {
		        _loggedDisplaySourceOnce = true;
		        bool usedComposite = (stack != null && source == _layerStackCompositeTemp);
		        UnityEngine.Debug.Log($"[Inpaint_MaskPainter] Display source: {(usedComposite ? "COMPOSITE (all visible layers)" : (stack != null && stack.ActiveLayer?.Content == source ? "ACTIVE LAYER ONLY" : "FALLBACK (_ObjectUV_brushedColorRGBA)"))}. Stack={stack != null}, Layers={(stack?.Layers?.Count ?? 0)}, ActiveIdx={(stack?.ActiveLayerIndex ?? -1)}.");
	        }

	        _blitApplyEntireColorLayer_mat.SetTexture("_SrcTex", source.texArray);
	        RenderUdims.SetNumUdims( ontoHere, _blitApplyEntireColorLayer_mat );

	        TextureTools_SPZ.SetKeyword_Material(_blitApplyEntireColorLayer_mat, "APPLY_LATEST_BRUSH_TOO", _isPainting);
	        _blitApplyEntireColorLayer_mat.SetTexture("_LatestBrushStroke", _latestBrushStroke_ref);
	        Color brushCol = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.brushColor : Color.black;
	        _blitApplyEntireColorLayer_mat.SetColor("_CurrBrushColor", brushCol);
	        float sign = Mathf.Sign(_prevStrength);
	        _blitApplyEntireColorLayer_mat.SetFloat("_Sign", sign);
	        float maxStrength = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity : 1f;
	        _blitApplyEntireColorLayer_mat.SetFloat("_MaxPossibleBrushStrength01", maxStrength);

	        _blitApplyEntireColorLayer_mat.SetInteger("_isColorlessMask", isColorless?1:0);
	        if(isColorless){  _blitApplyEntireColorLayer_mat.SetTexture("_ColorlessCheckerTex", _colorlessMaskChecker_tex); }

	        _blitApplyEntireColorLayer_mat.SetFloat("_TotalOpacity01", layerOpacity01);

	        TextureTools_SPZ.Blit( source.texArray,  ontoHere.texArray,  _blitApplyEntireColorLayer_mat );
	    }

	    void EnsureLayerStackCompositeTemp(RenderUdims sameSizeAs)
	    {
		    if (sameSizeAs == null) return;
		    if (_layerStackCompositeTemp != null && _layerStackCompositeTemp.width == sameSizeAs.width &&
		        _layerStackCompositeTemp.height == sameSizeAs.height && _layerStackCompositeTemp.UdimsCount == sameSizeAs.UdimsCount)
			    return;
		    _layerStackCompositeTemp?.Dispose();
		    _layerStackCompositeTemp = new RenderUdims( sameSizeAs.udims_sectors, sameSizeAs.widthHeight,
			    GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter, Color.clear, 0 );
	    }


	    // expensive! only use during manual baking/extraction of colors, not every frame
	    public List<Texture2D> ExtractColorLayer_as_UV_texture2D(out List<UDIM_Sector> udims_sectors_){
	        RenderUdims src = GetLayerCompositeOrFallback();
	        if (src == null) { udims_sectors_ = new List<UDIM_Sector>(); return new List<Texture2D>(); }
	        udims_sectors_ = src.udims_sectors.ToList();
	        return TextureTools_SPZ.TextureArray_to_Texture2DList(src.texArray);
	    }


	    public RenderTexture ScreenMask_ContentRT_ref(bool withAntiEdge)
	        => _inpaintScreenMasker.ScreenMask_ContentRT_ref(withAntiEdge);


	    // Expensive, invoked when we need a texture to send to StableDiffusion.
	    // Returns two textures, without anti-edge (sent to stableDiffusion, to avoid showing it colors through the gaps).
	    // and with anti-edge (used internally in stableProjectorz during projections)
	    public void GetDisposable_ScreenMask( bool forceFullWhite, out Texture2D skipAntiEdge_, out Texture2D withAntiEdge_ ){
	        RenderUdims forMask = GetLayerCompositeOrFallback();
	        if (forMask == null) { skipAntiEdge_ = null; withAntiEdge_ = null; return; }
	        _inpaintScreenMasker.RenderScreenMask_maybe(forMask, mustRender:true);

	        RenderTexture skipAntiEdgeRT = ScreenMask_ContentRT_ref(withAntiEdge: false);
	        RenderTexture antiEdgeRT     = ScreenMask_ContentRT_ref(withAntiEdge: true);
	        skipAntiEdge_ =  TextureTools_SPZ.R_to_RGBA_Texture2D( skipAntiEdgeRT,  forceAlpha1:true,  forceFullWhite:forceFullWhite );
	        withAntiEdge_ =  TextureTools_SPZ.R_to_RGBA_Texture2D( antiEdgeRT,  forceAlpha1:true,  forceFullWhite:forceFullWhite );
	    }


	    public override void ResetPaintMask(){
	        base.ResetPaintMask();
	        if (PaintLayerStack_MGR.instance != null && PaintLayerStack_MGR.instance.ActiveLayer != null)
	        {
		        var al = PaintLayerStack_MGR.instance.ActiveLayer;
		        al.Content?.ClearTheTextures(Color.clear);
		        al.Data?.ClearTheTextures(Color.clear);
	        }
	        _ObjectUV_brushedColorRGBA?.ClearTheTextures(Color.clear);
	        isPaintMaskEmpty=true;
	    }


	    protected override void OnBucketFill_button(){
	        if(MainViewport_UI.instance.showing != MainViewport_UI.Showing.UsualView){ return; }
	        if(WorkflowRibbon_UI.instance.isMode_using_img2img() == false){ return; }
	        var target = GetPaintTarget();
	        if (target == null) return;
	        Color col = SD_WorkflowOptionsRibbon_UI.instance.brushColor;
	        OnBucketFill_orDelete_button( col, target.texArray,  visibilTex:null );
	        isPaintMaskEmpty = false;
	    }
	    protected override void OnDelete_button(){//different to ResetPaintMask(), might be only for some isolated mesh.
	        if(MainViewport_UI.instance.showing != MainViewport_UI.Showing.UsualView){ return; }
	        var target = GetPaintTarget();
	        if (target == null) return;
	        OnBucketFill_orDelete_button( Color.clear, target.texArray,  visibilTex:null );
	    }

	    protected override bool isAllowedToShow_BrushCursorNow()
	        => MainViewport_UI.instance.showing == MainViewport_UI.Showing.UsualView
	           && WorkflowRibbon_UI.instance.isMode_using_img2img();

	    protected override bool isAllowedToPaintNow( bool also_check_viewportHovered ){
	        bool isAllowed =  MainViewport_UI.instance?.showing == MainViewport_UI.Showing.UsualView;
	             isAllowed &= DimensionMode_MGR.instance?._dimensionMode == DimensionMode.dim_sd;
	             isAllowed &= WorkflowRibbon_UI.instance?.isMode_using_img2img() ?? false;
	             // Inpaint brush does not require multi-view edit mode; that is for projection-mask editing. Allow painting on active layer whenever view/workflow are correct.
	             isAllowed &= !SD_WorkflowOptionsRibbon_UI.instance?.IsEyeDropperMagnified ?? false;
	             isAllowed &= !ClickSelect_Meshes_MGR.instance?._isSelectMode?? false;
	             isAllowed &= !GlobalClickBlocker.isLocked();
	        if (also_check_viewportHovered){
	            isAllowed &= MainViewport_UI.instance?.isCursorHoveringMe()?? false;
	        }
	        return isAllowed;
	    }

	    public override Vector2 getViewportSize()
	        => MainViewport_UI.instance.mainViewportRect.rect.size;

	    public override Vector2 getViewportCursorPos01(bool forceMainViewport=false)
	        =>MainViewport_UI.instance.cursorMainViewportPos01;

	    protected override Vector3Int maskResolution(){
	        if (ModelsHandler_3D.instance == null) return new Vector3Int(GenData_Masks.COLOR_BRUSH_RESOLUTION, GenData_Masks.COLOR_BRUSH_RESOLUTION, 0);
	        IReadOnlyList<UDIM_Sector> allUdims = ModelsHandler_3D.instance._allKnownUdims;
	        int numSlices = allUdims != null ? allUdims.Count : 0;
	        return new Vector3Int( GenData_Masks.COLOR_BRUSH_RESOLUTION,  GenData_Masks.COLOR_BRUSH_RESOLUTION,  numSlices);
	    }

	    protected override float getBrushStrength(){//strength [0,1] --> [-1,1]}
	        var orib = SD_WorkflowOptionsRibbon_UI.instance;
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

	        if (PaintLayerStack_MGR.instance != null)
		        PaintLayerStack_MGR.instance.EnsureResolution(new Vector3Int(width, height, numSlices));

	        // Guarantee single-layer color buffer when we have a model: use same UDIM source as maskResolution()
	        // so GetPaintTarget() always has a fallback and strokes never commit to null.
	        if (numSlices > 0)
	        {
		        bool needColorBuf = _ObjectUV_brushedColorRGBA == null
			        || _ObjectUV_brushedColorRGBA.width != width || _ObjectUV_brushedColorRGBA.height != height
			        || _ObjectUV_brushedColorRGBA.UdimsCount != numSlices;
		        if (needColorBuf)
		        {
			        _ObjectUV_brushedColorRGBA?.Dispose();
			        // Same source as maskResolution() so Count is always equal to numSlices when model is loaded.
			        IReadOnlyList<UDIM_Sector> allUdims = ModelsHandler_3D.instance != null ? ModelsHandler_3D.instance._allKnownUdims : null;
			        if (allUdims != null && allUdims.Count == numSlices)
			        {
				        _ObjectUV_brushedColorRGBA = new RenderUdims( allUdims, new Vector2Int(width, height),
				                                                      GenData_Masks.colorBrushFormat,  GenData_Masks.masksFilter,
				                                                      Color.clear,  depthBits:0 );
			        }
			        else
				        Debug.LogWarning("[Inpaint] Could not create paint color buffer: UDIM count mismatch (numSlices=" + numSlices + ", udims=" + (allUdims?.Count ?? -1) + "). Ensure a 3D model is loaded.");
		        }
	        }
	        // Scene buffer now exists; inject into any layers that were auto-created before it existed (Layer 1 from Awake).
	        InjectSceneIntoAllExistingLayers();
	    }


	    protected override void OnRenderIntoCurrTex_please( RenderTexture prevBrushStroke_R8, RenderTexture currBrushStroke_R8,
	                                                        bool isFirstFrameOfStroke, float suggested_brushStrength ){
	        var target = GetPaintTarget();
	        if (target == null)
	        {
		        Debug.LogWarning("[Inpaint] Paint target is null. Ensure a 3D model is loaded. LayerStack="
		                         + (PaintLayerStack_MGR.instance != null) + " ActiveLayer="
		                         + (PaintLayerStack_MGR.instance?.ActiveLayerRenderUdims != null)
		                         + " ColorBuf=" + (_ObjectUV_brushedColorRGBA != null));
		        return;
	        }
	        isPaintMaskEmpty = false;
	        if(isFirstFrameOfStroke){ _prevStrength = suggested_brushStrength; }
        
	        RenderUdims.SetNumUdims(target, _brushMaterial);

	        _brushMaterial.SetFloat("_ExtraVisibility", 1);
	        _brushMaterial.SetFloat("_FadeByNormal", 0);
	        _brushMaterial.SetTexture("_PrevBrushPathTex", prevBrushStroke_R8);
	        Texture2D stamp = BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback();
	        _brushMaterial.SetTexture("_BrushStamp", stamp); 
	        _brushMaterial.SetVector("_BrushStrength", new Vector4(_prevStrength,suggested_brushStrength,0,0));

	        var selectedMeshes = ModelsHandler_3D.instance.selectedMeshes;
	        Objects_Renderer_MGR.instance.EquipMaterial_on_Specific( selectedMeshes, _brushMaterial );

	        UserCameras_MGR.instance._curr_viewCamera.RenderImmediate_Arr( renderIntoHere:currBrushStroke_R8,  ignore_nonSelected_meshes:true,
	                                                                       _brushMaterial,  useClearingColor:false,  Color.clear, dontFrustumCull:true);
	        _prevStrength = suggested_brushStrength;
	        _latestBrushStroke_ref = currBrushStroke_R8;
	    }


	    protected override void OnFinal_ApplyIncomingVals_intoMask( RenderTexture prevBrushStroke_R8, 
	                                                                RenderTexture currBrushStroke_R8 ){
	        var target = GetPaintTarget();
	        if (target == null)
	            target = _ObjectUV_brushedColorRGBA;
	        if (target == null) {
	            Debug.LogWarning("Inpaint_MaskPainter: no paint target. Ensure a 3D model is loaded and click in viewport to paint.");
	            return;
	        }
	        if (_applyBrushStroke_toUvMask == null)
	            _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);
	        if (_applyBrushStroke_toUvMask == null)
	        {
	            Debug.LogError("Inpaint_MaskPainter: ApplyBrushStroke_ToUvMask not found. Brush strokes will not persist on the model.");
	            return;
	        }
	        float sign =  Mathf.Sign(_prevStrength);
	        float maxStrength = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity : 1f;

	        _applyBrushStroke_toUvMask.Apply_into_ColorBrushTex( prevBrushStroke_R8, currBrushStroke_R8, sign,  maxStrength,  target );
	        // Compute writes to Content. When we use composite (2+ layers), the composite can be built before the GPU finishes; defer re-render so stroke appears.
	        Objects_Renderer_MGR.instance.ReRenderAll_soon();
	        RequestReRenderAfterGpuCommit(target);
	        var activeLayer = PaintLayerStack_MGR.instance?.ActiveLayer;
	        if (activeLayer != null && target == activeLayer.Content)
		        StartCoroutine(DeferredReRenderAfterStroke());
	        Act_OnPaintStrokeEnd?.Invoke();
	    }

	    /// <summary>When painting into a layer, request re-render again after 2 frames so the composite (if used) is rebuilt after the compute has finished; fixes paint disappearing with 2+ layers.</summary>
	    IEnumerator DeferredReRenderAfterStroke()
	    {
		    yield return null;
		    yield return null;
		    if (Objects_Renderer_MGR.instance != null)
			    Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }

	    /// <summary>After the GPU finishes the compute dispatch, request another re-render to guarantee
	    /// the display is fully up-to-date (covers edge cases where the initial re-render is processed
	    /// before the GPU finishes the compute work on some drivers).</summary>
	    void RequestReRenderAfterGpuCommit(RenderUdims writtenTarget) {
	        if (writtenTarget?.texArray == null) return;
	        RenderTexture rt = writtenTarget.texArray;
	        AsyncGPUReadback.Request(rt, 0, req => {
	            if (req.hasError) return;
	            if (Objects_Renderer_MGR.instance != null)
	                Objects_Renderer_MGR.instance.ReRenderAll_soon();
	        });
	    }

    
	    protected override void Awake(){
	      #if UNITY_EDITOR
	        if(UnityEditor.EditorApplication.isPlaying==false){ return; }
	      #endif
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        UnityEngine.Debug.Log("[Inpaint_MaskPainter] Awake: instance set, subscribing to events.");
	        if (_applyBrushStroke_toUvMask == null)
	            _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);

	        try {
	            if (Pen.current != null  &&  Pen.current.deviceId != Pen.InvalidDeviceId){
	                if (Viewport_StatusText.instance != null)
	                    Viewport_StatusText.instance.ShowStatusText($"Drawing Tablet '{Pen.current.displayName}' detected, "
	                                                            +$"will use pressure when brushing.", false, 5, progressVisibility:false );
	                else
	                    UnityEngine.Debug.Log($"[Inpaint_MaskPainter] Drawing Tablet '{Pen.current.displayName}' detected (StatusText not yet ready).");
	            }
	        } catch (System.Exception e) {
	            UnityEngine.Debug.LogWarning("[Inpaint_MaskPainter] Pen detection failed (non-fatal): " + e.Message);
	        }
	        _blitApplyEntireColorLayer_mat = new Material(_blitApplyEntireColorLayer_shader);

	        base.Awake();

	        PaintLayerStack_MGR.OnLayerAdded += OnLayerAdded_InjectScene;
	        UnityEngine.Debug.Log("[Inpaint_MaskPainter] Awake complete: OnLayerAdded subscribed, material created.");

	        if (SystemInfo.supportsConservativeRaster == false){
	            DestroyImmediate(base._brushMaterial); //secretly swap the parent's material with a more suitable one
	            base._brushMaterial = new Material(_brushShader_noCVTRaster);
	        }
	    }

	    /// <summary>Blit static scene (_ObjectUV_brushedColorRGBA) into a layer's Content if dimensions match.
	    /// Sets HasReceivedSceneInject so we never overwrite user paint. Does NOT touch Data (strokes go
	    /// directly into Content, so Data is not part of the paint path).</summary>
	    bool TryInjectSceneIntoLayer(PaintLayer layer)
	    {
		    return TryInjectIntoLayer(layer, _ObjectUV_brushedColorRGBA);
	    }

	    /// <summary>Blit source into layer's Content if dimensions match. Sets HasReceivedSceneInject. Used for new layers so they get the same base as the previous layer or scene.</summary>
	    bool TryInjectIntoLayer(PaintLayer layer, RenderUdims source)
	    {
		    if (layer == null || source == null) return false;
		    if (layer.Content == null) return false;
		    if (source.width != layer.Content.width || source.height != layer.Content.height || source.UdimsCount != layer.Content.UdimsCount) return false;
		    Graphics.Blit(source.texArray, layer.Content.texArray);
		    layer.HasReceivedSceneInject = true;
		    UnityEngine.Debug.Log($"[Inpaint_MaskPainter] TryInjectIntoLayer: injected into '{layer.Name}' ({layer.Content.width}x{layer.Content.height}), source={(source == _ObjectUV_brushedColorRGBA ? "scene" : "layer")}.");
		    return true;
	    }

	    /// <summary>Optional: source to copy into a layer (e.g. duplicate layer). Add Layer does not use this—new layers stay empty and we stream the layer below at display time (CompositeToOnTopOfBase) to save RAM.</summary>
	    RenderUdims GetInjectionSourceForNewLayer(PaintLayerStack_MGR stack, PaintLayer newLayer)
	    {
		    if (stack == null || newLayer == null) return _ObjectUV_brushedColorRGBA;
		    if (stack.Layers == null || stack.Layers.Count < 2) return _ObjectUV_brushedColorRGBA;
		    int newIndex = -1;
		    for (int i = 0; i < stack.Layers.Count; i++)
			    if (stack.Layers[i] == newLayer) { newIndex = i; break; }
		    if (newIndex <= 0) return _ObjectUV_brushedColorRGBA;
		    var previous = stack.Layers[newIndex - 1];
		    if (previous?.Content != null) return previous.Content;
		    return _ObjectUV_brushedColorRGBA;
	    }

	    /// <summary>Ensure the bottom layer (index 0) has scene so it has a base. Upper layers are delta-only—we do not inject scene into them; they are streamed at display via composite to save RAM.</summary>
	    void EnsureSceneInjectedIntoActiveLayer()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack?.ActiveLayer == null || _ObjectUV_brushedColorRGBA == null) return;
		    // Only the bottom layer gets the scene buffer; layers 2+ stay empty (stream at display).
		    if (stack.ActiveLayerIndex != 0) return;
		    if (stack.ActiveLayer.Content == null) return;
		    if (stack.ActiveLayer.HasReceivedSceneInject) return;
		    TryInjectSceneIntoLayer(stack.ActiveLayer);
	    }

	    void InjectSceneIntoActiveLayer()
	    {
		    EnsureSceneInjectedIntoActiveLayer();
	    }

	    /// <summary>When user clicks/selects a layer, ensure that layer has Content and (for bottom layer) scene data. Fallback: ensure Content is allocated before injecting.</summary>
	    void OnActiveLayerChanged_EnsureContent()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack?.ActiveLayer != null && stack.ActiveLayer.Content == null)
			    stack.EnsureContentForLayerIfNeeded(stack.ActiveLayer);
		    InjectSceneIntoActiveLayer();
	    }

	    /// <summary>When user clicks New Layer: ensure the new layer has a Content buffer (allocated, clear). We do NOT copy the layer below—display streams it by compositing at render time (saves RAM, scales better).</summary>
	    void OnLayerAdded_InjectScene(PaintLayer newLayer)
	    {
		    if (newLayer == null) return;
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null) return;

		    UnityEngine.Debug.Log($"[Inpaint_MaskPainter] OnLayerAdded_InjectScene: new layer '{newLayer.Name}' (stream-by-composite, no copy). activeIdx={stack.ActiveLayerIndex}, totalLayers={stack.Layers?.Count ?? 0}.");

		    // Ensure new layer has a Content buffer (empty). No data copy—layer below is streamed at display via CompositeToOnTopOfBase.
		    stack.EnsureContentForLayerIfNeeded(newLayer);
		    if (newLayer.Content == null)
		    {
			    UnityEngine.Debug.LogWarning("[Inpaint_MaskPainter] OnLayerAdded_InjectScene: new layer has no Content (resolution not set). Deferring buffer allocation.");
			    StartCoroutine(DeferredEnsureContentForNewLayer(newLayer));
			    return;
		    }
		    // New layer Content is clear; paint on this layer will write only deltas. Composite = base + layer1 + layer2 (streamed).
		    if (Objects_Renderer_MGR.instance != null)
			    Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }

	    /// <summary>Retry ensuring new layer has Content next frame (edge case: resolution not set yet). Still no copy—stream at display.</summary>
	    IEnumerator DeferredEnsureContentForNewLayer(PaintLayer newLayer)
	    {
		    yield return null;
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || newLayer == null) yield break;
		    stack.EnsureContentForLayerIfNeeded(newLayer);
		    if (newLayer.Content != null && Objects_Renderer_MGR.instance != null)
			    Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }

	    /// <summary>Returns the display source: composite of all visible layers when 2+ layers (matches viewport), else active Content or scene fallback.</summary>
	    public RenderUdims GetLayerCompositeOrFallback()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack != null && _ObjectUV_brushedColorRGBA != null && stack.Layers != null && stack.Layers.Count > 1)
		    {
			    EnsureLayerStackCompositeTemp(_ObjectUV_brushedColorRGBA);
			    if (_layerStackCompositeTemp != null)
			    {
				    stack.CompositeToOnTopOfBase(_ObjectUV_brushedColorRGBA, _layerStackCompositeTemp);
				    return _layerStackCompositeTemp;
			    }
		    }
		    if (stack != null && stack.ActiveLayer?.Content != null && _ObjectUV_brushedColorRGBA != null)
		    {
			    var activeContent = stack.ActiveLayer.Content;
			    if (activeContent.width == _ObjectUV_brushedColorRGBA.width && activeContent.height == _ObjectUV_brushedColorRGBA.height && activeContent.UdimsCount == _ObjectUV_brushedColorRGBA.UdimsCount)
				    return activeContent;
		    }
		    return _ObjectUV_brushedColorRGBA;
	    }

	    /// <summary>After InitTextures creates the scene buffer, inject scene only into the bottom layer (index 0). Upper layers are left empty and streamed at display time.</summary>
	    void InjectSceneIntoAllExistingLayers()
	    {
		    if (_ObjectUV_brushedColorRGBA == null) { UnityEngine.Debug.Log("[Inpaint_MaskPainter] InjectSceneIntoAllExisting: sceneBuf is null, skipping."); return; }
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || stack.Layers == null || stack.Layers.Count == 0) return;
		    var bottom = stack.Layers[0];
		    UnityEngine.Debug.Log("[Inpaint_MaskPainter] InjectSceneIntoAllExisting: injecting scene into bottom layer only (stream-by-composite for rest).");
		    TryInjectSceneIntoLayer(bottom);
	    }


	    protected override void OnDestroy(){
	        if (PaintLayerStack_MGR.instance != null)
		        PaintLayerStack_MGR.instance.OnActiveLayerChanged -= OnActiveLayerChanged_EnsureContent;
	        PaintLayerStack_MGR.OnLayerAdded -= OnLayerAdded_InjectScene;
	        _layerStackCompositeTemp?.Dispose();
	        DestroyImmediate(_blitApplyEntireColorLayer_mat);
	        base.OnDestroy();
	    }

	}
}//end namespace
