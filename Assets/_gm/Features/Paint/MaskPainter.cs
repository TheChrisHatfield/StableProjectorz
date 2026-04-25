using UnityEngine;
using UnityEngine.InputSystem;

namespace spz {

	public abstract class MaskPainter : MonoBehaviour{
	    [Space(10)]
	    [SerializeField] float _brushResizeDrag_speed = 5; //for shift+rmb dragging, to resize the cursor in viewport
	    [SerializeField] Shader _brushShader;
	    [SerializeField] AnimationCurve _brushSizeScale;
	    [SerializeField] AnimationCurve _pressureBrushSizeCurve;//tablet pressure
	    [SerializeField] AnimationCurve _pressureOpacitySizeCurve;
	    [Space(10)]
	    [SerializeField] Shader _fill_UV_chunks_shader;//for using bucket-fill tool.

	    protected Material _brushMaterial;//for modifying mask.
	    Material _fillUVchunks_mat;//when using bucket fill tool.

	    //a cheap texture for "detecting" + remembering the current stroke of brush.
	    //Cleared to black as soon as left mouse button is released.
	    RenderTexture _prevBrushPath_R8;//from previous frame
	    RenderTexture _currBrushPath_R8;//from current frame.

	    Vector2 _prevPaintPosition;//only updated during painting
	    float _lastBrushSize;
	    bool _isFirstFrameOfStroke = true;

	    /// <summary>Linear01 depth at the click point. Used for depth-distance brush falloff so paint doesn't bleed to far surfaces. </summary>
	    float _clickDepth01 = 0f;
	    static readonly int _ClickDepth01_ID = Shader.PropertyToID("_ClickDepth01");
	    static readonly int _DepthFalloffRange_ID = Shader.PropertyToID("_DepthFalloffRange");
	    static readonly int _StampPosSizeStr_ID = Shader.PropertyToID("_StampPosSizeStr");
	    static readonly int _StampCount_ID = Shader.PropertyToID("_StampCount");
	    static readonly int _BrushAngleRad_ID = Shader.PropertyToID("_BrushAngleRad");
	    static readonly int _BrushRoundness01_ID = Shader.PropertyToID("_BrushRoundness01");
	    static readonly int _SymmetryMode_ID = Shader.PropertyToID("_SymmetryMode");
	    static readonly int _MirrorPrevNewBrushScreenCoord_ID = Shader.PropertyToID("_MirrorPrevNewBrushScreenCoord");
	    static readonly int _SymmetryMirrorAngleDeltaRad_ID = Shader.PropertyToID("_SymmetryMirrorAngleDeltaRad");

	    const int MaxSplotchStamps = 64;
	    readonly Vector4[] _stampPosSizeStr = new Vector4[MaxSplotchStamps];

	    public float visibleBrushSize(){
	        if(_isPainting){ return _lastBrushSize; }
	        return _brushSizeScale.Evaluate(BrushRibbon_UI_Size.GetBrushSize01());
	    }

	    public bool _isPainting { get; private set; } = false;//is mouse currently pressed and are we dragging (painting).


	    public abstract Vector2 getViewportCursorPos01(bool forceMainViewport=false);
	    public abstract Vector2 getViewportSize();

	    /// <summary>Aspect passed to brush shaders (<c>_ScreenAspectRatio</c>): full game window so stamps stay circular when the Game view is non-square (viewport rect alone can mismatch clip-space UVs).</summary>
	    public static float GetGameWindowAspectForBrushShader()
	    {
		    float h = Screen.height;
		    return h > 1e-6f ? Mathf.Max(0.0001f, Screen.width) / h : 1f;
	    }

	    // scale the brush additionally. It's based on the % of main viewport:
	    protected virtual float getBrushExtraScaling_due_viewport() => 1.0f;

	    protected abstract Vector3Int maskResolution();
	    protected abstract bool isAllowedToShow_BrushCursorNow();

	    // We always pass false, but KEEP THE ARGUMENT FOR CLARITY. Child classes should never check viewport hovering.
	    // It's only this parent class who checks if the viewport is hovered, ONLY checks it during on mouse down.
	    // - might want to keep painting even if cursor went outside the viewport momentarily.
	    // Useful when adjusting the backgrounds near the viewport border.
	    protected abstract bool isAllowedToPaintNow(bool also_check_viewportHovered);

	    /// <summary>When false, symmetry stays screen-space (e.g. 2D background mask). 3D object / projection painters use mesh raycast symmetry when possible.</summary>
	    protected virtual bool useMeshPaintSymmetry () => true;

    
	    public virtual void ResetPaintMask(){
	        TextureTools_SPZ.ClearRenderTexture(_prevBrushPath_R8, Color.black); //texture might be textureArray!
	        TextureTools_SPZ.ClearRenderTexture(_currBrushPath_R8, Color.black);
	    }


	    public void SetCurrentBrushStroke(Material here, string shaderPropertyName){
	        Texture tex = (_isPainting==false || _currBrushPath_R8==null)?  Texture2D.blackTexture : _currBrushPath_R8;
	        here.SetTexture(shaderPropertyName, tex);
	    }


	    void OnUpdate(){
	        if(isAllowedToShow_BrushCursorNow()){
	            CursorPreviewUI_Reposition();
	        }
	        if(isAllowedToPaintNow(also_check_viewportHovered:false)  &&  MainViewport_UI.instance.isCursorHoveringMe() ){ 
	            OnPointerDown_maybe();
	        }
	        OnDrag_maybe();
	        OnPointerUp_maybe();

	        OnUpdateChildren();
        
	        if(!_isPainting){ return; }
	        Graphics.Blit(_currBrushPath_R8, _prevBrushPath_R8);
	    }

	    protected virtual void OnUpdateChildren(){ }


	    // updates position of the UI element ("outline" of the brush)
	    // This helps the user to see where the brush is about to paint.
	    void CursorPreviewUI_Reposition(){
	        float size01 = BrushRibbon_UI_Size.GetBrushSize01();
	        if(KeyMousePenInput.isKey_Shift_pressed()){
	            Vector2 delta =  KeyMousePenInput.delta_while_RMBpressed( normalizeByScreenDiagonal:true );
	            float predominantAxisValue   = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : delta.y;
	            float mouseMovementMagnitude = Mathf.Abs(predominantAxisValue);
	            float resizeDir   = predominantAxisValue >= 0 ? 1 : -1;
	            float brushResize = mouseMovementMagnitude * resizeDir * _brushResizeDrag_speed;
	            float sliderVal   = Mathf.Clamp(size01 + brushResize, 0.001f, 1);
	            if (BrushRibbon_UI_Size.instance != null) BrushRibbon_UI_Size.instance.SetBrushSize(sliderVal);
	            else if (SD_WorkflowOptionsRibbon_UI.instance != null) SD_WorkflowOptionsRibbon_UI.instance.SetBrushSize(sliderVal);
	        }
	        Cursor_UI.instance.SetCursorThickness(BrushRibbon_UI_Size.GetBrushSize01());
	        Cursor_UI.instance.PositionCursor( _brushSizeScale.Evaluate(BrushRibbon_UI_Size.GetBrushSize01()) );
	    }


	    void OnPointerDown_maybe(){
	        if(!KeyMousePenInput.isLMBpressedThisFrame()){ return; }
	        if(isDoingSomethingElse()){ return; }

	        Vector3Int textureRes = maskResolution();
	        if (textureRes.z <= 0)
	        {
		        return;
	        }
	        _isPainting = true;
	        initTextures_Maybe(textureRes.x, textureRes.y, textureRes.z);
	        if (_currBrushPath_R8 == null)
	        {
		        _isPainting = false;
		        return;
	        }
        
	        float brushSize    = _brushSizeScale.Evaluate(BrushRibbon_UI_Size.GetBrushSize01());
	        float brushOpacity = SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity;

	        AffectByPressure(ref brushSize, ref brushOpacity);
	        brushSize = Mathf.Max(0.001f, brushSize);

	        // NOTICE: don't clear if User holds Shift, to continue drawing a straight line.
	        // Had we cleared here, there would be spots on the joints (between the old and the new brush stroke).
	        // So we will only clear in PaintOnTexture().
	        Vector2 pointInViewport01 = getViewportCursorPos01();

	        bool shift = KeyMousePenInput.isKey_Shift_pressed();
	             shift = false;//DISABLE FOR NOW (Jan 2025) - doesn't look good, and on the other hand, might mess up someone's paiting.
	        _isFirstFrameOfStroke = !shift;//if shift, don't reset position (like in photoshop)

	        if (_isFirstFrameOfStroke){
	            _prevPaintPosition = pointInViewport01;
	            _lastBrushSize = brushSize;
	            TextureTools_SPZ.ClearRenderTexture(_prevBrushPath_R8, Color.black);
	            TextureTools_SPZ.ClearRenderTexture(_currBrushPath_R8, Color.black);//Might be textureArray!
	            _clickDepth01 = SampleDepthAtCursor(pointInViewport01);
	        }
	    }

	    /// <summary>Linear 0–1 depth at the cursor (0 = camera, 1 = far). Must match shader's Linear01Depth() for depth-limit falloff.</summary>
	    float SampleDepthAtCursor(Vector2 viewportPos01)
	    {
	        Camera cam = UserCameras_MGR.instance?._curr_viewCamera?.myCamera;
	        if (cam == null) return 0f;
	        Ray ray = cam.ViewportPointToRay(new Vector3(viewportPos01.x, viewportPos01.y, 0));
	        if (Physics.Raycast(ray, out RaycastHit hit, cam.farClipPlane))
	        {
	            // Unity: WorldToViewportPoint().z is "world units from the camera" = linear distance.
	            // Divide by farClipPlane to get linear 0–1 depth matching shader Linear01Depth().
	            float distFromCam = cam.WorldToViewportPoint(hit.point).z;
	            return Mathf.Clamp01(distFromCam / cam.farClipPlane);
	        }
	        return 0f;
	    }


	    void AffectByPressure(ref float size_, ref float opacity_){
       
	        if (KeyMousePenInput.isLMBpressed(checkOnlyPen:true) == false){ return; }//tablet not used.
	        if (Pen.current == null){ return; }//no pen device (e.g. tablet not detected by Unity Input System).

	        float penPressure01 = Pen.current.pressure.ReadValue();

	        switch (SD_WorkflowOptionsRibbon_UI.instance.tabletPressureMode){
	            case TabletPressureMode.AffectSize: 
	                size_ *= _pressureBrushSizeCurve.Evaluate(penPressure01);
	                if(KeyMousePenInput.isLMBreleasedThisFrame()){ size_ = 0; }
	                break;

	            case TabletPressureMode.AffectOpacity: 
	                opacity_ *= _pressureOpacitySizeCurve.Evaluate(penPressure01);
	                if(KeyMousePenInput.isLMBreleasedThisFrame()){ opacity_ = 0; }
	                break;

	            case TabletPressureMode.AffectBoth: 
	                size_ *= _pressureBrushSizeCurve.Evaluate(penPressure01);
	                opacity_ *= _pressureOpacitySizeCurve.Evaluate(penPressure01);
	                if(KeyMousePenInput.isLMBreleasedThisFrame()){ size_=0;  opacity_ = 0; }
	                break;

	            case TabletPressureMode.AffectNone:
	            default:
	                break;
	        }

	    }


	    void OnPointerUp_maybe(){
	        if(!KeyMousePenInput.isLMBreleasedThisFrame()){ return; }
	        if (!_isPainting){ return; }//possibly clicked somewhere else in StableProjectorz, etc.

	        if(_currBrushPath_R8 != null){ 
	            //To finalize the brush stroke, apply the brush stroke to the mask:
	            OnFinal_ApplyIncomingVals_intoMask(_prevBrushPath_R8, _currBrushPath_R8);
	        }
	        _isPainting = false;
	        _isFirstFrameOfStroke = true;//Reset when the brush stroke ends
	    }


	    void OnDrag_maybe(){
	        if(!KeyMousePenInput.isLMBpressed()){ return; }
	        if(!_isPainting){ return; }
	        if(isDoingSomethingElse()){ return; }
	        PaintOnTexture();
	    }

	    bool isDoingSomethingElse(){
	        if (KeyMousePenInput.isKey_alt_pressed()){ return true; }
	        // Ctrl no longer blocks stroke start here: ClickSelect_Meshes_MGR._isSelectMode already suppresses painting
	        // when Ctrl+mesh-select is active; treating Ctrl as "busy" left inpaint unable to paint after eyedropper if Ctrl
	        // was still held (swatch workflow never held Ctrl).
	        if (Images_ImportHelper.instance != null && Images_ImportHelper.instance.isImporting){ return true; }
	        if (WorkflowRibbon_UI.instance == null || MainViewport_UI.instance == null || SD_WorkflowOptionsRibbon_UI.instance == null)
		        return true;

	        bool is_img2img        = WorkflowRibbon_UI.instance.isMode_using_img2img();
	        bool isProjectionsMask = WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.ProjectionsMasking;

	        bool correctMode  = (this as Inpaint_MaskPainter) != null  &&  is_img2img;
	             correctMode |= (this as Projections_MaskPainter)!=null  &&  isProjectionsMask;
	             correctMode |= (this as Background_Painter)!=null;

	             correctMode &= MainViewport_UI.instance.showing == MainViewport_UI.Showing.UsualView;

	        // Block paint strokes while undo/redo restore runs (same global undo stack for inpaint, background, projection masks).
	        if (PaintUndo_MGR.instance != null && PaintUndo_MGR.instance.BlocksNewStroke
	            && (this is Inpaint_MaskPainter || this is Background_Painter || this is Projections_MaskPainter))
		        return true;

	        return !correctMode;
	    }


	    protected abstract float getBrushStrength();

	    void PaintOnTexture(){
	        float brushSize =  _brushSizeScale.Evaluate(BrushRibbon_UI_Size.GetBrushSize01());
	        float suggested_brushOpacity = getBrushStrength();
        
	        AffectByPressure(ref brushSize, ref suggested_brushOpacity);

	        // Raw cursor only for stroke direction, spacing path, and symmetry — scatter jitter applies to stamp positions only.
	        Vector2 cursorRaw01 = getViewportCursorPos01();
	        float scale = getBrushExtraScaling_due_viewport();
	        float scatterMul = BrushRibbon_UI_Size.GetBrushScatterJitterMul();

	        var brushSizeVec =  new Vector4(_lastBrushSize,brushSize,0,0) * scale;
	        if (_isFirstFrameOfStroke){  brushSizeVec.z = 1.0f;  }

	        float spacing01 = BrushRibbon_UI_Size.GetBrushSpacing01();
	        float angleDeg = BrushRibbon_UI_Size.GetBrushAngleDeg();
	        if (BrushRibbon_UI_Size.GetBrushTipFollowsStroke() && !_isFirstFrameOfStroke)
	        {
		        Vector2 strokeDelta = cursorRaw01 - _prevPaintPosition;
		        if (strokeDelta.sqrMagnitude > 1e-12f)
			        angleDeg = Mathf.Repeat(angleDeg + Mathf.Atan2(strokeDelta.y, strokeDelta.x) * Mathf.Rad2Deg, 360f);
	        }
	        float roundness01 = BrushRibbon_UI_Size.GetBrushRoundness01();
	        _brushMaterial.SetFloat(_BrushAngleRad_ID, angleDeg * Mathf.Deg2Rad);
	        _brushMaterial.SetFloat(_BrushRoundness01_ID, roundness01 > 0f ? roundness01 : 1f);

	        bool symXOn = BrushRibbon_UI_Size.GetPaintSymmetryXOn();
	        Camera paintCam = UserCameras_MGR.instance?._curr_viewCamera?.myCamera;
	        // Mesh splotch symmetry: twins are appended in C# (shader splotch uses _SymmetryMode 0; see BrushEffects.cginc). If we
	        // place up to MaxSplotchStamps primary stamps, stampCount is already 64 and the mirror loop is skipped — no twin side.
	        bool meshSplotchCSharpDupe = symXOn && useMeshPaintSymmetry() && paintCam != null;
	        int maxPrimaryStampsForSplotch = meshSplotchCSharpDupe ? MaxSplotchStamps / 2 : MaxSplotchStamps;

	        int stampCount = 0;
	        if (spacing01 > 0.001f && !_isFirstFrameOfStroke)
	        {
	            float step = spacing01 * brushSize * scale;
	            if (step < 0.001f) step = 0.001f;
	            Vector2 from = _prevPaintPosition;
	            Vector2 to = cursorRaw01;
	            float dist = Vector2.Distance(from, to);
	            if (dist >= step)
	            {
	                int n = Mathf.Min(maxPrimaryStampsForSplotch, Mathf.FloorToInt(dist / step) + 1);
	                for (int k = 0; k < n && stampCount < maxPrimaryStampsForSplotch; k++)
	                {
	                    float t = (n > 1) ? (k / (float)n) : 0f;
	                    Vector2 pos = Vector2.Lerp(from, to, t);
	                    float sizeK = Mathf.Lerp(_lastBrushSize, brushSize, t) * scale;
	                    if (scatterMul > 0f)
	                    {
		                    float jStamp = scatterMul * Mathf.Max(sizeK, 0.0001f);
		                    pos += (Vector2)UnityEngine.Random.insideUnitCircle * jStamp;
		                    pos.x = Mathf.Clamp01(pos.x);
		                    pos.y = Mathf.Clamp01(pos.y);
	                    }
	                    _stampPosSizeStr[stampCount] = new Vector4(pos.x, pos.y, sizeK, Mathf.Abs(suggested_brushOpacity));
	                    stampCount++;
	                }
	            }
	        }

	        Vector2 brushEndpointForRender01 = cursorRaw01;
	        if (scatterMul > 0f && stampCount == 0)
	        {
		        float jr = scatterMul * brushSize * scale;
		        brushEndpointForRender01 = cursorRaw01 + (Vector2)UnityEngine.Random.insideUnitCircle * jr;
		        brushEndpointForRender01.x = Mathf.Clamp01(brushEndpointForRender01.x);
		        brushEndpointForRender01.y = Mathf.Clamp01(brushEndpointForRender01.y);
	        }

	        if (!symXOn) {
		        _brushMaterial.SetFloat(_SymmetryMode_ID, 0f);
		        _brushMaterial.SetVector(_MirrorPrevNewBrushScreenCoord_ID, Vector4.zero);
		        _brushMaterial.SetFloat(_SymmetryMirrorAngleDeltaRad_ID, 0f);
	        } else if (stampCount > 0) {
		        bool allowMeshReflection = useMeshPaintSymmetry() && paintCam != null;
		        if (allowMeshReflection) {
			        // Mesh symmetry for splotches: duplicate mirrored twins in C# (shader splotch path only supports screen mirror mode 1).
			        int origCount = stampCount;
			        for (int k = 0; k < origCount && stampCount < MaxSplotchStamps; k++) {
				        Vector2 c = new Vector2(_stampPosSizeStr[k].x, _stampPosSizeStr[k].y);
				        if (!PaintSymmetryMesh.TryMirrorViewportPoint(paintCam, c, out Vector2 mc, true))
					        mc = PaintSymmetryMesh.ScreenMirrorViewportUV(c);
				        _stampPosSizeStr[stampCount++] = new Vector4(mc.x, mc.y, _stampPosSizeStr[k].z, _stampPosSizeStr[k].w);
			        }
			        _brushMaterial.SetFloat(_SymmetryMode_ID, 0f);
			        _brushMaterial.SetVector(_MirrorPrevNewBrushScreenCoord_ID, Vector4.zero);
			        _brushMaterial.SetFloat(_SymmetryMirrorAngleDeltaRad_ID, 0f);
		        } else {
			        // Screen symmetry for splotches: let shader mirror centers so mirrored angle delta is applied for directional tips.
			        _brushMaterial.SetFloat(_SymmetryMode_ID, 1f);
			        _brushMaterial.SetVector(_MirrorPrevNewBrushScreenCoord_ID, Vector4.zero);
			        _brushMaterial.SetFloat(_SymmetryMirrorAngleDeltaRad_ID,
				        PaintSymmetryMesh.ComputeScreenMirrorAngleDelta(_prevPaintPosition, cursorRaw01));
		        }
	        } else {
		        PaintSymmetryMesh.SetMaterialSymmetry(_brushMaterial, paintCam, _prevPaintPosition, cursorRaw01, symXOn,
			        useMeshPaintSymmetry() && paintCam != null);
	        }

	        _brushMaterial.SetVectorArray(_StampPosSizeStr_ID, _stampPosSizeStr);
	        _brushMaterial.SetInt(_StampCount_ID, stampCount);

	        _brushMaterial.SetVector("_PrevNewBrushScreenCoord", new Vector4(_prevPaintPosition.x, _prevPaintPosition.y, brushEndpointForRender01.x, brushEndpointForRender01.y)); 
	        _brushMaterial.SetVector("_BrushSize_andFirstFrameFlag", brushSizeVec );
	        _brushMaterial.SetFloat("_ScreenAspectRatio", GetGameWindowAspectForBrushShader());
	        _brushMaterial.SetFloat(_ClickDepth01_ID, _clickDepth01);
	        float depthRange = SD_WorkflowOptionsRibbon_UI.instance != null
	            ? SD_WorkflowOptionsRibbon_UI.instance.brushDepthLimit01
	            : 0f;
	        _brushMaterial.SetFloat(_DepthFalloffRange_ID, depthRange);
        
	        OnRenderIntoCurrTex_please( _prevBrushPath_R8, _currBrushPath_R8, _isFirstFrameOfStroke, suggested_brushOpacity);

	        _prevPaintPosition = cursorRaw01;
	        _lastBrushSize = brushSize;
	        _isFirstFrameOfStroke = false;
	    }


	    protected abstract void OnRenderIntoCurrTex_please( RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8, 
	                                                        bool isFirstFrameOfStroke,  float suggested_brushStrength );

    
	    //To finalize the brush stroke, apply the brush stroke to the mask:
	    protected abstract void OnFinal_ApplyIncomingVals_intoMask( RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8 );


	    bool initTextures_Maybe( int width, int height, int numSlices ){
	        bool all_ok =  _currBrushPath_R8 != null 
	                       && _currBrushPath_R8.width==width  &&  _currBrushPath_R8.height==height
	                       && _currBrushPath_R8.volumeDepth==numSlices;
	        if (all_ok)
	        {
		        OnAfterInitTexturesMaybe(width, height, numSlices);
		        return false;
	        }
	        TextureTools_SPZ.Dispose_RT(ref _prevBrushPath_R8, isTemporary:false);
	        TextureTools_SPZ.Dispose_RT(ref _currBrushPath_R8, isTemporary:false);
	        InitTextures(width, height, numSlices, out _prevBrushPath_R8, out _currBrushPath_R8);
	        OnAfterInitTexturesMaybe(width, height, numSlices);
	        return true;
	    }

	    /// <summary>Called after initTextures_Maybe (whether textures were (re)created or not). Override to e.g. sync layer stack resolution so the default layer has content before first stroke.</summary>
	    protected virtual void OnAfterInitTexturesMaybe(int width, int height, int numSlices) { }

	    protected abstract void InitTextures( int width,  int height,  int numSlices, 
	                                          out RenderTexture prevBrushPath_,  out RenderTexture currBrushPath_);

	    protected virtual void On_3dModel_Imported(GameObject go){
	        Vector3Int textureRes = maskResolution();
	        bool did_init = initTextures_Maybe(textureRes.x, textureRes.y, textureRes.z);
	        if(!did_init){ ResetPaintMask(); }
	    }


	    protected abstract void OnBucketFill_button();
	    protected abstract void OnDelete_button();//different to ResetPaintMask(), might be only for some isolated mesh.

	    //fill, but only uv chunks of currently selected (isolated) meshes.
	    protected void OnBucketFill_orDelete_button( Color fillColor, RenderTexture dest, RenderTexture visibilTex=null){
	        if (dest == null) return;
	        if (UserCameras_MGR.instance?._curr_viewCamera == null || TextureDilation_MGR.instance == null || Objects_Renderer_MGR.instance == null)
	            return;

	        _fillUVchunks_mat.SetColor("_COL_UVCH_Color", fillColor);
	        _fillUVchunks_mat.SetTexture("_ProjVisibility", visibilTex);
	        TextureTools_SPZ.SetKeyword_Material(_fillUVchunks_mat, "USE_VISIBIL_TEX", visibilTex!=null);
	        TextureTools_SPZ.SetKeyword_Material(_fillUVchunks_mat, "VERTEX_COLORS", false);

	        RenderUdims.SetNumUdims(UDIMs_Helper._allSelectedUdims, _fillUVchunks_mat);

	        //render into temp, dilate it (expand borders), and paste into dest:
	        RenderTexture destTemp = new RenderTexture(dest.descriptor);
	        TextureTools_SPZ.ClearRenderTexture(destTemp, Color.clear);

	        // NOT using clear color;  Ignore non-selected.
	        // NOT using frustum cull: even if camera is looking at the object, remember that we are going to render into UVs.
	        // This would likely cause the camera to ignore the object.
	        UserCameras_MGR.instance._curr_viewCamera.RenderImmediate_Arr( destTemp,  ignore_nonSelected_meshes:true,
	                                                                       _fillUVchunks_mat,  useClearingColor:false,//NOT clearing.
	                                                                       Color.clear,  dontFrustumCull:true );
	        var dilRule = TextureTools_SPZ.GetChannelCount(dest)==4? DilateByChannel.A
	                                                              : DilateByChannel.R;
	        // ONLY DILATE BY 1 TEXEL.
	        // 2 is already too much, it would creep through seams of nearby uv islands
	        // and be on various objects in Catacombs mesh (Oct 2024)
	        var dilArg  = new DilationArg( destTemp,  numberOfTexelsExpand:1,
	                                       dilRule,  null,  isRunInstantly:true );
	        TextureDilation_MGR.instance.Dillate(dilArg);

	        TextureTools_SPZ.Blit(destTemp, dest);
	        DestroyImmediate(destTemp);

	        Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }


	    protected virtual void Update(){}


	    protected virtual void Awake(){
	        _brushMaterial    = new Material(_brushShader);
	        _fillUVchunks_mat = new Material(_fill_UV_chunks_shader);
	        ModelsHandler_3D.Act_onImported += On_3dModel_Imported;
        
	        BrushRibbon_UI_BucketFill._Act_onClicked += OnBucketFill_button;
	        BrushRibbon_UI_DeleteButton.onClicked    += OnDelete_button;
	    }

	    protected virtual void Start(){
	        Update_callbacks_MGR.brushing += OnUpdate;
	    }


	    protected virtual void OnDestroy(){
	        BrushRibbon_UI_BucketFill._Act_onClicked -= OnBucketFill_button;
	        BrushRibbon_UI_DeleteButton.onClicked    -= OnDelete_button;

	        DestroyImmediate(_brushMaterial);
	        DestroyImmediate(_fillUVchunks_mat);
	        TextureTools_SPZ.Dispose_RT(ref _prevBrushPath_R8, isTemporary:false);
	        TextureTools_SPZ.Dispose_RT(ref _currBrushPath_R8, isTemporary:false);

	        Update_callbacks_MGR.brushing -= OnUpdate;
	        ModelsHandler_3D.Act_onImported -= On_3dModel_Imported;
	    }
	}
}//end namespace
