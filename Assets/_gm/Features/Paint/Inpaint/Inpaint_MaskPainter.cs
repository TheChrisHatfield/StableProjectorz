using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace spz {

	// =============================================================================
	// LAYER SYSTEM - PAINT TARGET AND DISPLAY (Inpaint_MaskPainter)
	// =============================================================================
	// Paint target: GetPaintTarget() returns active layer Content (from PaintLayerStack_MGR).
	// Display: ApplyColorLayer_To_UV_Textures() uses CompositeVisibleLayersIntoTemp (EntireColorLayer shader, bottom→top)
	// when 2+ layers, else active Content; if no source, TryResolveArtThenSceneThenMeshUvSource (Art tab UV → scene → mesh, skipping self-blits).
	// New-layer injection: subscribes to PaintLayerStack_MGR.OnLayerAdded
	// (OnLayerAdded_InjectScene) to inject scene + layers below into the new layer.
	// =============================================================================

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
	    /// <summary>Multi-layer smudge: scene buffer + visible layers below active; mesh accumulation is optional via <see cref="PaintTab_SmudgeBrushOptions.IncludeUvMeshInLayerSmudge"/> (see <see cref="TryBuildSmudgeUnderTextureForSmudge"/>).</summary>
	    RenderUdims _smudgeUnderActiveTemp;
	    /// <summary>Non-owning wrapper for main Art tab icon UV color; invalidated when <see cref="GenData2D.total_GUID"/> or texture ref changes.</summary>
	    RenderUdims _artIconUvColorWrapper;
	    Guid _artIconUvColorWrapperGenGuid;
	    /// <summary>Last <see cref="GenData2D.total_GUID"/> for the focused UV-painted brush while smudge art routing is armed (updated on Art-tab selection; routing uses kind + commit serial, not GUID equality).</summary>
	    Guid _smudgePreferArtIconUntilLayerPaintGenGuid;
	    /// <summary>When non-negative, smudge may prefer the active Art tab <see cref="GenerationData_Kind.UvPaintedBrush"/> icon only while <see cref="_inpaintLayerColorCommitSerial"/> matches this snapshot (no new layer color milestones / disk paint / collapse since arm).</summary>
	    int _smudgePreferArt_ArmedAtCommitSerial = -1;
	    /// <summary>Bumps on meaningful layer color milestones (once per color brush stroke, bucket fill, collapse, load) so smudge art-routing does not hijack a stack that already carries paint.</summary>
	    int _inpaintLayerColorCommitSerial;
	    bool _inpaintLayerColorSerialBumpedThisBrushStroke;

	    public bool isPaintMaskEmpty { get; private set; } = true;
	    /// <summary>Single-layer paint target (when PaintLayerStack_MGR is not used). When layer stack is used, paint goes to active layer. </summary>
	    public RenderUdims _ObjectUV_brushedColorRGBA { get; private set; }

	    bool _subscribedActiveLayerChanged;
	    bool _loggedDisplaySourceOnce;
	    WorkflowRibbon_CurrMode _trackedWorkflowModeForSceneFlush;
	    /// <summary>True while collapse runs. Skip scene injection so the new layer only gets the composite we copy.</summary>
	    bool _isCollapsingLayers;
	    /// <summary>Set by PaintLayerStack_MGR during collapse to suppress scene injection into the new layer.</summary>
	    public bool IsCollapsingLayers { get => _isCollapsingLayers; set => _isCollapsingLayers = value; }

	    [Tooltip("Min seconds between GPU picks of mesh color under cursor for smudge ring tint.")]
	    [SerializeField] float _smudgeCursorReadMinInterval = 0.07f;
	    [Tooltip("Viewport 01 movement below this skips a new readback (reduces GPU traffic).")]
	    [SerializeField] float _smudgeCursorViewportMoveThresh = 0.0035f;

	    bool _smudgeCursorReadInFlight;
	    Vector2 _smudgeCursorLastReadVp01 = new Vector2(-1f, -1f);
	    float _smudgeCursorLastReadTime;
	    /// <summary>Format passed to <see cref="AsyncGPUReadback.Request"/> for the in-flight smudge cursor sample (decode must match).</summary>
	    GraphicsFormat _smudgeCursorPendingReadbackFormat;

	    bool _valueAssistLiveReadInFlight;
	    Vector2 _valueAssistLiveLastVp01 = new Vector2(-1f, -1f);
	    float _valueAssistLiveLastTime;
	    GraphicsFormat _valueAssistLivePendingFormat;

	    [Tooltip("Auto: contextual Thompson + layer opacity steers underlay policy. With a paint layer stack present, smudge writes are fenced to ActiveLayer.Content; GeneratedMesh applies only when no layer stack is active.")]
	    [SerializeField] SmudgeWriteTargetPreference _smudgeWriteTargetPreference = SmudgeWriteTargetPreference.Auto;

	    SmudgeAdaptiveRouteLock _smudgeRouteLockForStroke;
	    bool _smudgeRouteObsPending;
	    int _smudgeRouteObsBucket;
	    int _smudgeRouteObsArm;
	    float _smudgeStrokeMaxUnscaledDt;
	    /// <summary>Paint <c>Content</c> when adaptive route was locked; invalidated if <see cref="GetPaintTarget"/> changes mid-stroke.</summary>
	    RenderUdims _smudgeStrokeLockedPaintContent;
	    /// <summary>Last destination that had a pre-smudge undo capture this stroke; new <c>RenderUdims</c> triggers another capture.</summary>
	    RenderUdims _smudgeUndoSegmentDest;
	    /// <summary>Pre-stroke undo snapshot for normal (non-smudge) paint — scheduled on the first frame we actually dispatch compute, not <c>isFirstFrameOfStroke</c> (parent clears that flag even when this callback early-outs).</summary>
	    bool _undoPreStrokeScheduledForStroke;

	    // --- Paint target: active layer Content (used by brush stroke application) ---
	    /// <summary>Paint target: active layer Content directly. Compute shader writes strokes into the same
	    /// texture the renderer reads, so GPU command serialization guarantees strokes are visible on the same
	    /// frame they're painted (no Data/Content timing gap). Ensures scene is injected before use.</summary>
	    /// <summary>Public resolver for undo/redo (same as paint target).</summary>
	    public RenderUdims GetPaintTarget_Undo() => GetPaintTarget();

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
		    var active = stack?.ActiveLayer;
		    if (active != null && active.Content != null)
		    {
			    // Per-layer No Color routing: in Inpaint_NoColor mode, strokes go to a separate per-layer mask buffer
			    // (lazy-allocated). This stops No Color paint from polluting the layer's color RGB — switching back to
			    // Color mode no longer reveals "secret" color from strokes the user thought were colorless. OG (no
			    // layer stack) keeps a single buffer, but with layers the user expects mode-correct strokes per layer.
			    if (IsCurrentModeNoColor())
			    {
				    active.EnsureNoColorMaskMatchesContent();
				    if (active.NoColorMask != null)
					    return active.NoColorMask;
			    }
			    return active.Content;
		    }
		    return _ObjectUV_brushedColorRGBA;
	    }

	    static bool IsCurrentModeNoColor()
	        => WorkflowRibbon_UI.instance != null
	           && WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor;
	    public static Action Act_OnPaintStrokeEnd { get; set; } = null;


	    public SoftInpaintingArgs GetArgs_for_SoftInpaint_GenRequest(){

	        var sd = SD_WorkflowOptionsRibbon_UI.instance;
	        if (sd == null || !sd.isSoftInpaint) return null;

	        var entry = new SoftInpaintingArgsEntry{};//keep default values, they don't have much difference (Jul 2024)

	        var sft_args = new SoftInpaintingArgs(){
	            args = new SoftInpaintingArgsEntry[1]{ entry },
	        };
	        return sft_args;
	    }


	    // --- Display / SD bridge: blit paint layers onto accumulation (called from Objects_Renderer_MGR) ---
	    // Single path for both 1 layer and N layers:
	    //   1. Determine `source` (single visible layer: ActiveLayer.Content; multi-layer or hidden sole layer: composite / fallback)
	    //   2. One blit: source → ontoHere (accumulation) via EntireColorLayer_BlitApply shader (handles scaling, UDIMs, etc.)
	    // This replicates the proven single-layer mechanism at any layer count.
	    /// <param name="forStableDiffusionCapture">When true, always composite/blit even if a project save is in progress (save guard would otherwise skip and SD would capture paint-free accumulation).</param>
	    public void ApplyColorLayer_To_UV_Textures( RenderUdims ontoHere, bool forStableDiffusionCapture = false ){
	        if (ontoHere == null) return;
	        if (_blitApplyEntireColorLayer_mat == null)
	        {
		        Debug.LogError("[Inpaint] _blitApplyEntireColorLayer_mat is null – Awake likely crashed. Cannot display paint.");
		        return;
	        }
	        bool isColorless  = WorkflowRibbon_UI.instance != null && WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor;
	        if (!forStableDiffusionCapture && Save_MGR.instance != null && Save_MGR.instance._isSaving){ return; }
	        // Upstream (single-layer): when No Color + few frames before SD send, skip this blit — "Don't blit, keep as is."
	        // Paint-layer path must still run when forStableDiffusionCapture is true so EnsureInpaintColorLayerAppliedForCapture
	        // can composite layers onto accumulation before content-cam ReadPixels (see SD_GenRequests_Helper.Generate_img2img_crtn).
	        bool willSendToSD = StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._finalPreparations_beforeGen;
	        if (isColorless && willSendToSD && !forStableDiffusionCapture)
		        return;

	        EnsureSceneInjectedIntoActiveLayer();
	        var stack = PaintLayerStack_MGR.instance;
	        bool multiLayer = stack != null && stack.Layers != null && stack.Layers.Count > 1;

	        if (multiLayer)
	        {
		        for (int li = 0; li < stack.Layers.Count; li++)
		        {
			        var L = stack.Layers[li];
			        if (L != null && L.Visible)
				        stack.EnsureContentForLayerIfNeeded(L);
		        }
		        EnsureSceneBufferForDisplay();
		        EnsureBottomLayerHasSceneForComposite();
	        }

	        // ---- Determine source: this is the key mechanism. Same variable, same final blit, regardless of layer count. ----
	        RenderUdims source = null;

	        if (multiLayer && stack != null)
	        {
		        // Multi-layer: composite all visible layers into _layerStackCompositeTemp using EntireColorLayer_BlitApply
		        // (the same shader that works for single-layer). We blit each layer onto the composite temp in order,
		        // then use the composite temp as `source` for the final blit — identical to how single-layer uses ActiveLayer.Content.
		        // SD init-image capture must not include NoColor checker/white overlay in RGB.
		        // Keep NoColor for viewport preview, but exclude it when composing accumulation for capture.
		        source = CompositeVisibleLayersIntoTemp(stack, isColorless, includeNoColorMaskInRgb: !forStableDiffusionCapture);
		        if (source == null)
		        {
			        if (!_loggedDisplaySourceOnce)
			        {
				        _loggedDisplaySourceOnce = true;
				        UnityEngine.Debug.LogWarning("[Inpaint] Multi-layer composite produced null source. Falling back to ActiveLayer.Content or scene buffer.");
			        }
			        source = stack.ActiveLayer?.Content;
			        // If every layer is hidden (or none contributed), do not blit the active layer's hidden Content —
			        // let the next line pick _ObjectUV_brushedColorRGBA so the viewport matches smudge fallbacks.
			        var al = stack.ActiveLayer;
			        if (al != null && !al.Visible && source != null && ReferenceEquals(source, al.Content))
				        source = null;
		        }
	        }
	        else if (stack != null && stack.Layers != null && stack.Layers.Count == 1 && stack.ActiveLayer?.Content != null
	                 && stack.ActiveLayer.Visible)
	        {
		        source = stack.ActiveLayer.Content;
	        }

	        if (source == null)
		        source = TryResolveArtThenSceneThenMeshUvSource(ontoHere, ontoHere);

	        if (source == null)
	        {
		        Debug.LogWarning("[Inpaint] Paint not shown on mesh: no paint source. Load a 3D model and paint in the viewport.");
		        return;
	        }

	        // ---- Final blit: identical for 1 layer and N layers. This is the proven single-layer mechanism. ----
	        bool isSmudgeTool = SD_WorkflowOptionsRibbon_UI.instance != null && SD_WorkflowOptionsRibbon_UI.instance.isSmudge;
	        Color brushCol = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.brushColor : Color.black;
	        float sign = Mathf.Sign(_prevStrength);
	        float maxStrength = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity : 1f;

	        // Single-layer fast path: blit Content (always color), then blit NoColorMask if present (always checker).
	        // The shader uses premultiplied "draw on top" blend so the second pass overlays where mask alpha > 0.
	        // Live brush stroke goes to the buffer matching the current mode so visible stroke matches what's stored.
	        var activeLayerForOverlay = stack?.ActiveLayer;
	        bool singleLayerActiveContent = !multiLayer && activeLayerForOverlay != null
	                                         && ReferenceEquals(source, activeLayerForOverlay.Content);
	        bool isNoColorMode = isColorless;

	        // Pass 1: Content as color
	        bool applyBrushToContent = _isPainting && !multiLayer && !isSmudgeTool && !isNoColorMode;
	        _blitApplyEntireColorLayer_mat.SetTexture("_SrcTex", source.texArray);
	        RenderUdims.SetNumUdims(ontoHere, _blitApplyEntireColorLayer_mat);
	        TextureTools_SPZ.SetKeyword_Material(_blitApplyEntireColorLayer_mat, "APPLY_LATEST_BRUSH_TOO", applyBrushToContent);
	        _blitApplyEntireColorLayer_mat.SetTexture("_LatestBrushStroke", _latestBrushStroke_ref);
	        _blitApplyEntireColorLayer_mat.SetColor("_CurrBrushColor", brushCol);
	        _blitApplyEntireColorLayer_mat.SetFloat("_Sign", sign);
	        _blitApplyEntireColorLayer_mat.SetFloat("_MaxPossibleBrushStrength01", maxStrength);
	        // Always 0 here: layer Content is color RGB. Colorless overlay is the second pass below (single-layer)
	        // or already baked per-layer in the composite temp (multi-layer).
	        _blitApplyEntireColorLayer_mat.SetInteger("_isColorlessMask", 0);
	        _blitApplyEntireColorLayer_mat.SetFloat("_TotalOpacity01", 1f);
	        TextureTools_SPZ.Blit(source.texArray, ontoHere.texArray, _blitApplyEntireColorLayer_mat);

	        // Pass 2: per-layer NoColorMask as checker (single-layer preview path only).
	        // During SD capture, mask is sent separately via GetDisposable_ScreenMask; do not tint init-image RGB.
	        if (!forStableDiffusionCapture && singleLayerActiveContent && activeLayerForOverlay.NoColorMask != null)
	        {
		        var ncm = activeLayerForOverlay.NoColorMask;
		        bool applyBrushToNoColor = _isPainting && !isSmudgeTool && isNoColorMode;
		        _blitApplyEntireColorLayer_mat.SetTexture("_SrcTex", ncm.texArray);
		        RenderUdims.SetNumUdims(ontoHere, _blitApplyEntireColorLayer_mat);
		        TextureTools_SPZ.SetKeyword_Material(_blitApplyEntireColorLayer_mat, "APPLY_LATEST_BRUSH_TOO", applyBrushToNoColor);
		        _blitApplyEntireColorLayer_mat.SetTexture("_LatestBrushStroke", _latestBrushStroke_ref);
		        _blitApplyEntireColorLayer_mat.SetColor("_CurrBrushColor", brushCol);
		        _blitApplyEntireColorLayer_mat.SetFloat("_Sign", sign);
		        _blitApplyEntireColorLayer_mat.SetFloat("_MaxPossibleBrushStrength01", maxStrength);
		        _blitApplyEntireColorLayer_mat.SetInteger("_isColorlessMask", 1);
		        _blitApplyEntireColorLayer_mat.SetTexture("_ColorlessCheckerTex", _colorlessMaskChecker_tex);
		        _blitApplyEntireColorLayer_mat.SetFloat("_TotalOpacity01", 1f);
		        TextureTools_SPZ.Blit(ncm.texArray, ontoHere.texArray, _blitApplyEntireColorLayer_mat);
	        }
	    }

	    /// <summary>Composite all visible layers into _layerStackCompositeTemp using the EntireColorLayer_BlitApply shader.
	    /// For each visible layer (bottom to top): blit Content as color (isColorless=0), then blit per-layer
	    /// NoColorMask (if present) on top as checker (isColorless=1). The shader's premultiplied add-on-top blend
	    /// means each pass overlays where it has alpha. Live brush stroke goes to the buffer matching the current
	    /// mode so the active stroke is shown only once and matches what is actually being written. Returns the
	    /// composite temp, or null on failure. (<paramref name="_isColorlessIgnored"/> kept only for back-compat
	    /// of older callers; modes are now per-buffer.)</summary>
	    RenderUdims CompositeVisibleLayersIntoTemp(PaintLayerStack_MGR stack, bool _isColorlessIgnored, bool includeNoColorMaskInRgb = true)
	    {
		    if (stack == null || stack.Layers == null || stack.Layers.Count == 0) return null;

		    RenderUdims firstVis = null;
		    for (int i = 0; i < stack.Layers.Count; i++)
		    {
			    var L = stack.Layers[i];
			    if (L != null && L.Visible && L.Content != null) { firstVis = L.Content; break; }
		    }
		    if (firstVis == null) return null;

		    EnsureLayerStackCompositeTemp(firstVis);
		    if (_layerStackCompositeTemp == null)
		    {
			    UnityEngine.Debug.LogWarning("[Inpaint] Could not create composite temp for multi-layer.");
			    return null;
		    }

		    _layerStackCompositeTemp.ClearTheTextures(Color.clear);

		    bool isSmudgeTool = SD_WorkflowOptionsRibbon_UI.instance != null && SD_WorkflowOptionsRibbon_UI.instance.isSmudge;
		    Color brushCol = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.brushColor : Color.black;
		    float sign = Mathf.Sign(_prevStrength);
		    float maxStrength = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity : 1f;
		    int activeIdx = stack.ActiveLayerIndex;
		    bool isNoColorMode = IsCurrentModeNoColor();

		    int blitCount = 0;
		    for (int i = 0; i < stack.Layers.Count; i++)
		    {
			    var layer = stack.Layers[i];
			    if (!layer.Visible || layer.Content == null) continue;

			    float opacity = Mathf.Clamp01(layer.Opacity);
			    bool isActive = (i == activeIdx);
			    bool activeAndPainting = isActive && _isPainting && !isSmudgeTool;
			    // Live stroke shown on the buffer matching the current mode — what you see is where it is being written.
			    bool brushOnContent  = activeAndPainting && !isNoColorMode;
			    bool brushOnNoColor  = activeAndPainting && isNoColorMode;

			    // Pass A: layer Content as color
			    _blitApplyEntireColorLayer_mat.SetTexture("_SrcTex", layer.Content.texArray);
			    RenderUdims.SetNumUdims(_layerStackCompositeTemp, _blitApplyEntireColorLayer_mat);
			    TextureTools_SPZ.SetKeyword_Material(_blitApplyEntireColorLayer_mat, "APPLY_LATEST_BRUSH_TOO", brushOnContent);
			    _blitApplyEntireColorLayer_mat.SetTexture("_LatestBrushStroke", _latestBrushStroke_ref);
			    _blitApplyEntireColorLayer_mat.SetColor("_CurrBrushColor", brushCol);
			    _blitApplyEntireColorLayer_mat.SetFloat("_Sign", sign);
			    _blitApplyEntireColorLayer_mat.SetFloat("_MaxPossibleBrushStrength01", maxStrength);
			    _blitApplyEntireColorLayer_mat.SetInteger("_isColorlessMask", 0);
			    _blitApplyEntireColorLayer_mat.SetFloat("_TotalOpacity01", opacity);
			    TextureTools_SPZ.Blit(layer.Content.texArray, _layerStackCompositeTemp.texArray, _blitApplyEntireColorLayer_mat);
			    blitCount++;

			    // Pass B: layer NoColorMask as checker (lazy-allocated; may not exist if no No-Color stroke yet)
			    var ncm = layer.NoColorMask;
			    if (ncm == null && isActive && isNoColorMode)
			    {
				    layer.EnsureNoColorMaskMatchesContent();
				    ncm = layer.NoColorMask;
			    }
			    if (includeNoColorMaskInRgb && ncm != null)
			    {
				    _blitApplyEntireColorLayer_mat.SetTexture("_SrcTex", ncm.texArray);
				    RenderUdims.SetNumUdims(_layerStackCompositeTemp, _blitApplyEntireColorLayer_mat);
				    TextureTools_SPZ.SetKeyword_Material(_blitApplyEntireColorLayer_mat, "APPLY_LATEST_BRUSH_TOO", brushOnNoColor);
				    _blitApplyEntireColorLayer_mat.SetTexture("_LatestBrushStroke", _latestBrushStroke_ref);
				    _blitApplyEntireColorLayer_mat.SetColor("_CurrBrushColor", brushCol);
				    _blitApplyEntireColorLayer_mat.SetFloat("_Sign", sign);
				    _blitApplyEntireColorLayer_mat.SetFloat("_MaxPossibleBrushStrength01", maxStrength);
				    _blitApplyEntireColorLayer_mat.SetInteger("_isColorlessMask", 1);
				    _blitApplyEntireColorLayer_mat.SetTexture("_ColorlessCheckerTex", _colorlessMaskChecker_tex);
				    _blitApplyEntireColorLayer_mat.SetFloat("_TotalOpacity01", opacity);
				    TextureTools_SPZ.Blit(ncm.texArray, _layerStackCompositeTemp.texArray, _blitApplyEntireColorLayer_mat);
			    }
		    }

		    if (blitCount == 0)
		    {
			    UnityEngine.Debug.LogWarning("[Inpaint] CompositeVisibleLayersIntoTemp: 0 layers blitted (all hidden or no Content?).");
			    return null;
		    }

		    return _layerStackCompositeTemp;
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

	    void EnsureSmudgeUnderActiveTemp(RenderUdims sameSizeAs)
	    {
		    if (sameSizeAs == null) return;
		    if (_smudgeUnderActiveTemp != null && _smudgeUnderActiveTemp.width == sameSizeAs.width &&
		        _smudgeUnderActiveTemp.height == sameSizeAs.height && _smudgeUnderActiveTemp.UdimsCount == sameSizeAs.UdimsCount)
			    return;
		    _smudgeUnderActiveTemp?.Dispose();
		    _smudgeUnderActiveTemp = new RenderUdims(sameSizeAs.udims_sectors, sameSizeAs.widthHeight,
			    GenData_Masks.colorBrushFormat, GenData_Masks.colorBrushFilter, Color.clear, 0);
	    }

	    static bool SmudgeSameUdimsShape(RenderUdims a, RenderUdims b)
	    {
		    return a != null && b != null && a.texArray != null && b.texArray != null
		           && a.width == b.width && a.height == b.height && a.UdimsCount == b.UdimsCount;
	    }

	    /// <summary>When the layer stack does not supply pixels, same priority as smudge: Art-tab UV wrapper, scene buffer, then mesh accumulation (skipped if same texture as <paramref name="excludeSameTexAs"/> — e.g. blitting accumulation onto itself).</summary>
	    RenderUdims TryResolveArtThenSceneThenMeshUvSource(RenderUdims shapeRef, RenderUdims excludeSameTexAs)
	    {
		    if (shapeRef == null) return null;
		    var artWrap = EnsureArtIconUvColorWrapper();
		    if (artWrap != null && SmudgeSameUdimsShape(shapeRef, artWrap))
			    return artWrap;
		    if (_ObjectUV_brushedColorRGBA != null && SmudgeSameUdimsShape(shapeRef, _ObjectUV_brushedColorRGBA))
			    return _ObjectUV_brushedColorRGBA;
		    var meshAcc = Objects_Renderer_MGR.instance?.accumulationTextures_ref();
		    if (meshAcc != null && SmudgeSameUdimsShape(shapeRef, meshAcc)
		        && (excludeSameTexAs == null || meshAcc.texArray != excludeSameTexAs.texArray))
			    return meshAcc;
		    return null;
	    }

	    /// <summary>When the stack is empty or scene buffer is missing, use mesh accumulation dimensions (if any) so Art-tab / mesh fallbacks match SD output resolution.</summary>
	    RenderUdims ResolveSmudgeFallbackUsingMeshShapeIfNeeded(RenderUdims layerPaintTarget)
	    {
		    var meshForShape = Objects_Renderer_MGR.instance?.accumulationTextures_ref();
		    var shapeRef = layerPaintTarget;
		    if (meshForShape != null && (shapeRef == null || !SmudgeSameUdimsShape(shapeRef, meshForShape)))
			    shapeRef = meshForShape;
		    return TryResolveArtThenSceneThenMeshUvSource(shapeRef, excludeSameTexAs: null);
	    }

	    /// <summary>Call after Bake Colors imports a <see cref="GenerationData_Kind.UvPaintedBrush"/> so smudge targets the UV-painted brush icon(s) until layer color paint clears it; switching the selected Art icon keeps smudge on the new selection while the arm matches.</summary>
	    public void NotifyBakedColorsEvictedToArtIcon()
	    {
		    var icon = Art2D_IconsUI_List.instance?._mainSelectedIcon;
		    if (icon?._genData != null && icon._genData.kind == GenerationData_Kind.UvPaintedBrush) {
			    _smudgePreferArtIconUntilLayerPaintGenGuid = icon._genData.total_GUID;
			    _smudgePreferArt_ArmedAtCommitSerial = _inpaintLayerColorCommitSerial;
		    }
		    else {
			    _smudgePreferArtIconUntilLayerPaintGenGuid = default;
			    _smudgePreferArt_ArmedAtCommitSerial = -1;
		    }
	    }

	    /// <summary>Call after loading a project whose paint layer stack included saved content — prevents smudge from targeting the Art icon over restored layer pixels.</summary>
	    public void NotifyPaintLayersRestoredFromDisk(bool anyLayerHadSavedPaintContent)
	    {
		    ClearSmudgePreferArtIconUntilLayerPaint();
		    // Replace (do not +=) so a prior session’s serial cannot leak across project loads.
		    _inpaintLayerColorCommitSerial = anyLayerHadSavedPaintContent ? 1 : 0;
		    _inpaintLayerColorSerialBumpedThisBrushStroke = false;
	    }

	    void ClearSmudgePreferArtIconUntilLayerPaint()
	    {
		    _smudgePreferArtIconUntilLayerPaintGenGuid = default;
		    _smudgePreferArt_ArmedAtCommitSerial = -1;
	    }

	    void OnMainArtIconSelected_ArmSmudgeToUvPaintedBrushIfLayerStackIdle(IconUI icon)
	    {
		    if (icon?._genData == null || icon._genData.kind != GenerationData_Kind.UvPaintedBrush) return;

		    // No layer color commits yet: arm toward whichever UV-painted brush icon is selected (session start).
		    if (_inpaintLayerColorCommitSerial == 0) {
			    _smudgePreferArtIconUntilLayerPaintGenGuid = icon._genData.total_GUID;
			    _smudgePreferArt_ArmedAtCommitSerial = 0;
			    return;
		    }

		    // After bake (or any prior arm): same commit milestone — follow the newly selected UV-painted brush (multiple icons / merged groups).
		    if (_smudgePreferArt_ArmedAtCommitSerial >= 0
		        && _inpaintLayerColorCommitSerial == _smudgePreferArt_ArmedAtCommitSerial) {
			    _smudgePreferArtIconUntilLayerPaintGenGuid = icon._genData.total_GUID;
		    }
	    }

	    /// <summary>Wraps the selected Art tab icon’s primary UV color texture array for smudge / undo when it matches mesh brush resolution.</summary>
	    public RenderUdims EnsureArtIconUvColorWrapper()
	    {
		    var art = Art2D_IconsUI_List.instance;
		    var icon = art?._mainSelectedIcon;
		    var gen = icon?._genData;
		    if (gen == null) return null;
		    var tr = gen.GetTexture_ref0();
		    if (tr?.texArray == null) return null;
		    var udims = ModelsHandler_3D.instance?._allKnownUdims;
		    if (udims == null || udims.Count == 0 || udims.Count != tr.texArray.volumeDepth)
			    return null;
		    Guid g = gen.total_GUID;
		    if (_artIconUvColorWrapper != null && _artIconUvColorWrapper.texArray == tr.texArray && _artIconUvColorWrapperGenGuid == g)
			    return _artIconUvColorWrapper;
		    _artIconUvColorWrapper?.Dispose();
		    _artIconUvColorWrapper = new RenderUdims(tr.texArray, udims.ToList(), texturesBelongToMe: false);
		    _artIconUvColorWrapperGenGuid = g;
		    return _artIconUvColorWrapper;
	    }

	    /// <summary>After bake-to-art, <see cref="AlignSmudgePaintTargetToActiveLayerContentIfNeeded"/> may keep <c>ActiveLayer.Content</c> when the art UV wrapper is not ready; mesh underlay must still apply so smudge samples visible UV on the empty layer.</summary>
	    bool SmudgePreferArtArmNeedsMeshUnderActiveLayerContent(PaintLayerStack_MGR stack, RenderUdims alignedTarget)
	    {
		    if (stack?.ActiveLayer?.Content == null || alignedTarget == null) return false;
		    var active = stack.ActiveLayer;
		    if (!active.Visible) return false;
		    if (!ReferenceEquals(alignedTarget, active.Content)) return false;
		    if (_smudgePreferArt_ArmedAtCommitSerial < 0) return false;
		    if (_inpaintLayerColorCommitSerial != _smudgePreferArt_ArmedAtCommitSerial) return false;
		    var sel = Art2D_IconsUI_List.instance?._mainSelectedIcon;
		    if (sel?._genData == null || sel._genData.kind != GenerationData_Kind.UvPaintedBrush) return false;
		    var artWrap = EnsureArtIconUvColorWrapper();
		    if (artWrap != null && SmudgeSameUdimsShape(alignedTarget, artWrap)) return false;
		    return true;
	    }

	    /// <summary>Delegates to <see cref="SmudgeStrokeRouter"/> for domain barriers, write destination, and kernel spacing. With a paint layer stack, writes are fenced to <c>ActiveLayer.Content</c>; when no stack, <see cref="SmudgeWriteTargetPreference.GeneratedMesh"/> can target mesh accumulation.</summary>
	    void ResolveSmudgeDestinationAndAccum(RenderUdims layerPaintTarget, PaintLayerStack_MGR stack,
		    out RenderUdims smudgeDest, out RenderUdims smudgeAcc, out PaintUndoNonStackTarget undoNonStackKind,
		    out float smudgeKernelSpacingMultiplier)
	    {
		    smudgeDest = null;
		    smudgeAcc = null;
		    undoNonStackKind = PaintUndoNonStackTarget.InpaintColor;
		    smudgeKernelSpacingMultiplier = 1f;
		    if (layerPaintTarget == null) return;

		    layerPaintTarget = AlignSmudgePaintTargetToActiveLayerContentIfNeeded(layerPaintTarget, stack);

		    if (_smudgeStrokeLockedPaintContent != null
		        && _smudgeRouteLockForStroke != SmudgeAdaptiveRouteLock.Inactive
		        && !ReferenceEquals(layerPaintTarget, _smudgeStrokeLockedPaintContent)) {
			    _smudgeRouteLockForStroke = SmudgeAdaptiveRouteLock.Inactive;
			    _smudgeRouteObsPending = false;
			    _smudgeStrokeLockedPaintContent = null;
		    }

		    var meshAcc = Objects_Renderer_MGR.instance?.accumulationTextures_ref();
		    bool meshOk = meshAcc != null && SmudgeSameUdimsShape(layerPaintTarget, meshAcc);
		    bool layerGate = SmudgeStrokeRouter.LayerSmudgeGateOpen(stack, layerPaintTarget);
		    bool hasLayerStack = stack != null && stack.Layers != null && stack.Layers.Count > 0;
		    // Keep underlay policy consistent with router's layer-stack fence:
		    // GeneratedMesh does not force mesh-style underlay skipping when a layer stack exists.
		    var effectiveWritePreference = _smudgeWriteTargetPreference;
		    if (hasLayerStack && effectiveWritePreference == SmudgeWriteTargetPreference.GeneratedMesh)
			    effectiveWritePreference = SmudgeWriteTargetPreference.LayerStack;
		    bool skipMultiLayerUnder = meshOk && (effectiveWritePreference == SmudgeWriteTargetPreference.GeneratedMesh
		                                         || (effectiveWritePreference == SmudgeWriteTargetPreference.Auto
		                                             && _smudgeRouteLockForStroke == SmudgeAdaptiveRouteLock.PreferMesh));

		    // Single-layer + layer gate: no multi-layer “under” texture pass (router handles underlay policy). Multi-layer + gate: composite layers below / scene; do not skip that path when layerGate is on.
		    bool skipTryBuildForSingleLayerStackOnly = hasLayerStack && layerGate && stack.Layers != null && stack.Layers.Count <= 1;
		    bool includeUvMeshUser = PaintTab_SmudgeBrushOptions.IncludeUvMeshInLayerSmudge;
		    bool includeUvMeshEffective = includeUvMeshUser || SmudgePreferArtArmNeedsMeshUnderActiveLayerContent(stack, layerPaintTarget);
		    RenderUdims preUnder = null;
		    if (!skipMultiLayerUnder && !skipTryBuildForSingleLayerStackOnly && layerGate && stack != null && stack.Layers != null && stack.Layers.Count > 1)
			    preUnder = TryBuildSmudgeUnderTextureForSmudge(layerPaintTarget, stack, includeUvMeshEffective);

		    var artWrap = EnsureArtIconUvColorWrapper();
		    var plan = SmudgeStrokeRouter.Build(layerPaintTarget, stack, meshAcc, artWrap, preUnder,
			    _smudgeWriteTargetPreference, includeUvMeshEffective);
		    smudgeDest = plan.Dest;
		    smudgeAcc = plan.Underlay;
		    undoNonStackKind = plan.UndoKind;
		    smudgeKernelSpacingMultiplier = plan.KernelSpacingMultiplier;
	    }

	    /// <summary>Align smudge sampling/write buffer with what the viewport shows: active <c>Content</c> when the layer stack contributes; otherwise <see cref="TryResolveArtThenSceneThenMeshUvSource"/> (same as <see cref="ApplyColorLayer_To_UV_Textures"/> when no layer composite).</summary>
	    RenderUdims AlignSmudgePaintTargetToActiveLayerContentIfNeeded(RenderUdims layerPaintTarget, PaintLayerStack_MGR stack)
	    {
		    if (layerPaintTarget == null)
			    return null;
		    // No stack or all layers removed: GetPaintTarget() falls back to _ObjectUV_brushedColorRGBA — that buffer can be
		    // stale size vs mesh/Art gen; still run Art → scene → mesh resolution using mesh shape when needed.
		    if (stack == null || stack.Layers == null || stack.Layers.Count <= 0) {
			    var resolvedEmpty = ResolveSmudgeFallbackUsingMeshShapeIfNeeded(layerPaintTarget);
			    return resolvedEmpty ?? layerPaintTarget;
		    }
		    var active = stack.ActiveLayer;
		    var content = active?.Content;
		    if (content == null) {
			    var resolvedNoContent = ResolveSmudgeFallbackUsingMeshShapeIfNeeded(layerPaintTarget);
			    return resolvedNoContent ?? layerPaintTarget;
		    }
		    if (_ObjectUV_brushedColorRGBA == null) {
			    var resolvedNoScene = ResolveSmudgeFallbackUsingMeshShapeIfNeeded(layerPaintTarget);
			    if (resolvedNoScene != null) return resolvedNoScene;
		    }

		    // No visible layer with Content (all hidden or no allocated buffers): SD / Art tab output — same resolver as display.
		    if (!SmudgeStrokeRouter.StackHasAnyVisiblePaintLayer(stack)) {
			    var resolved = TryResolveArtThenSceneThenMeshUvSource(layerPaintTarget, excludeSameTexAs: null);
			    return resolved ?? layerPaintTarget;
		    }

		    // Active layer hidden: same Art → scene → mesh priority (excludeSameTexAs null — smudge may write mesh).
		    if (active != null && !active.Visible && ReferenceEquals(layerPaintTarget, content)) {
			    var resolved = TryResolveArtThenSceneThenMeshUvSource(layerPaintTarget, excludeSameTexAs: null);
			    return resolved ?? layerPaintTarget;
		    }
		    if (!ReferenceEquals(layerPaintTarget, content) && ReferenceEquals(layerPaintTarget, _ObjectUV_brushedColorRGBA))
			    return content;
		    if (_smudgePreferArt_ArmedAtCommitSerial >= 0
		        && _inpaintLayerColorCommitSerial == _smudgePreferArt_ArmedAtCommitSerial) {
			    var sel = Art2D_IconsUI_List.instance?._mainSelectedIcon;
			    if (sel?._genData != null
			        && sel._genData.kind == GenerationData_Kind.UvPaintedBrush
			        && active != null && active.Visible
			        && ReferenceEquals(layerPaintTarget, content)) {
				    var artWrapPrefer = EnsureArtIconUvColorWrapper();
				    if (artWrapPrefer != null && SmudgeSameUdimsShape(layerPaintTarget, artWrapPrefer))
					    return artWrapPrefer;
			    }
		    }
		    return layerPaintTarget;
	    }

	    /// <summary>First smudge frame (Auto only): lock underlay policy (full multi-layer under vs skip) and register bandit pull; does not change write target.</summary>
	    void BeginSmudgeStrokeAdaptiveRoutingIfNeeded(RenderUdims target, PaintLayerStack_MGR stack)
	    {
		    PaintUndo_MGR.EnsureExists();
		    _smudgeStrokeMaxUnscaledDt = Time.unscaledDeltaTime;
		    _smudgeRouteObsPending = false;
		    _smudgeRouteObsBucket = -1;
		    _smudgeRouteLockForStroke = SmudgeAdaptiveRouteLock.Inactive;

		    if (_smudgeWriteTargetPreference != SmudgeWriteTargetPreference.Auto)
			    return;
		    if (!SmudgeStrokeRouter.LayerSmudgeGateOpen(stack, target))
			    return;
		    var meshAcc = Objects_Renderer_MGR.instance?.accumulationTextures_ref();
		    if (meshAcc == null || !SmudgeSameUdimsShape(target, meshAcc))
			    return;

		    float opacity = stack.ActiveLayer != null ? Mathf.Clamp01(stack.ActiveLayer.Opacity) : 1f;
		    var sch = PaintUndo_MGR.instance != null ? PaintUndo_MGR.instance.UndoScheduler : null;
		    float refPx = sch != null ? sch.referencePixelsPerSlice : 512f * 512f;
		    int bucketCount = sch != null ? sch.restoreContextBucketCount : 8;
		    PaintUndo_Scheduler.EvaluateWorkload(target.width, target.height, target.UdimsCount, refPx, out _, out float complexity01, out _);

		    bool preferLayer;
		    int bucket;
		    int chosenArm;
		    if (sch != null) {
			    preferLayer = sch.SelectSmudgeLayerVersusGeneratedMesh(complexity01, target.UdimsCount, opacity, true, out bucket, out chosenArm);
		    } else {
			    bucket = PaintUndo_Scheduler.QuantizeContextBucket(complexity01, target.UdimsCount, bucketCount);
			    preferLayer = opacity >= 0.5f;
			    chosenArm = preferLayer ? 0 : 1;
		    }

		    _smudgeRouteObsBucket = bucket;
		    _smudgeRouteObsArm = chosenArm;
		    _smudgeRouteObsPending = true;
		    _smudgeRouteLockForStroke = preferLayer ? SmudgeAdaptiveRouteLock.PreferLayer : SmudgeAdaptiveRouteLock.PreferMesh;
		    _smudgeStrokeLockedPaintContent = target;
	    }

	    /// <summary>Builds what lies <em>under</em> the active layer for smudge sampling: scene buffer, visible layers with index &lt; active, and optionally mesh accumulation when <paramref name="allowMeshAccumUnder"/> is true.</summary>
	    /// <param name="allowMeshAccumUnder">When false, mesh UV accumulation is not copied into the underlay (scene buffer and lower paint layers still used).</param>
	    RenderUdims TryBuildSmudgeUnderTextureForSmudge(RenderUdims target, PaintLayerStack_MGR stack, bool allowMeshAccumUnder)
	    {
		    if (target == null || stack?.Layers == null || stack.Layers.Count <= 1 || _blitApplyEntireColorLayer_mat == null)
			    return null;

		    EnsureBottomLayerHasSceneForComposite();
		    EnsureSceneBufferForDisplay();

		    bool isColorless = WorkflowRibbon_UI.instance != null && WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor;

		    EnsureSmudgeUnderActiveTemp(target);
		    if (_smudgeUnderActiveTemp == null)
			    return null;

		    int activeIdx = Mathf.Clamp(stack.ActiveLayerIndex, 0, stack.Layers.Count - 1);

		    // Bottom layer: underlay matches single-layer idea — scene snapshot and/or mesh accumulation (not “other layers”).
		    if (activeIdx <= 0)
		    {
			    if (_ObjectUV_brushedColorRGBA != null && SmudgeSameUdimsShape(target, _ObjectUV_brushedColorRGBA))
			    {
				    Graphics.CopyTexture(_ObjectUV_brushedColorRGBA.texArray, _smudgeUnderActiveTemp.texArray);
				    return _smudgeUnderActiveTemp;
			    }
			    if (allowMeshAccumUnder)
			    {
				    var accBottom = Objects_Renderer_MGR.instance?.accumulationTextures_ref();
				    if (accBottom != null && SmudgeSameUdimsShape(target, accBottom))
				    {
					    Graphics.CopyTexture(accBottom.texArray, _smudgeUnderActiveTemp.texArray);
					    return _smudgeUnderActiveTemp;
				    }
			    }
			    return null;
		    }

		    _smudgeUnderActiveTemp.ClearTheTextures(Color.clear);

		    Color brushCol = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.brushColor : Color.black;
		    float sign = Mathf.Sign(_prevStrength);
		    float maxStrength = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity : 1f;

		    int blitCount = 0;
		    for (int i = 0; i < activeIdx; i++)
		    {
			    var layer = stack.Layers[i];
			    if (layer == null || !layer.Visible || layer.Content == null) continue;

			    float opacity = Mathf.Clamp01(layer.Opacity);

			    _blitApplyEntireColorLayer_mat.SetTexture("_SrcTex", layer.Content.texArray);
			    RenderUdims.SetNumUdims(_smudgeUnderActiveTemp, _blitApplyEntireColorLayer_mat);
			    TextureTools_SPZ.SetKeyword_Material(_blitApplyEntireColorLayer_mat, "APPLY_LATEST_BRUSH_TOO", false);
			    _blitApplyEntireColorLayer_mat.SetTexture("_LatestBrushStroke", _latestBrushStroke_ref);
			    _blitApplyEntireColorLayer_mat.SetColor("_CurrBrushColor", brushCol);
			    _blitApplyEntireColorLayer_mat.SetFloat("_Sign", sign);
			    _blitApplyEntireColorLayer_mat.SetFloat("_MaxPossibleBrushStrength01", maxStrength);
			    _blitApplyEntireColorLayer_mat.SetInteger("_isColorlessMask", isColorless ? 1 : 0);
			    if (isColorless) _blitApplyEntireColorLayer_mat.SetTexture("_ColorlessCheckerTex", _colorlessMaskChecker_tex);
			    _blitApplyEntireColorLayer_mat.SetFloat("_TotalOpacity01", opacity);
			    TextureTools_SPZ.Blit(layer.Content.texArray, _smudgeUnderActiveTemp.texArray, _blitApplyEntireColorLayer_mat);
			    blitCount++;
		    }

		    if (blitCount == 0)
		    {
			    if (_ObjectUV_brushedColorRGBA != null && SmudgeSameUdimsShape(target, _ObjectUV_brushedColorRGBA))
				    Graphics.CopyTexture(_ObjectUV_brushedColorRGBA.texArray, _smudgeUnderActiveTemp.texArray);
			    else if (allowMeshAccumUnder)
			    {
				    var accFallback = Objects_Renderer_MGR.instance?.accumulationTextures_ref();
				    if (accFallback != null && SmudgeSameUdimsShape(target, accFallback))
					    Graphics.CopyTexture(accFallback.texArray, _smudgeUnderActiveTemp.texArray);
				    else
					    return null;
			    }
			    else
				    return null;
		    }

		    return _smudgeUnderActiveTemp;
	    }

	    /// <summary>Ensure the scene buffer exists and has the same size as source; create or resize if needed. Does not copy contents.</summary>
	    void EnsureSceneBufferSameSizeAs(RenderUdims source)
	    {
		    if (source == null) return;
		    bool need = _ObjectUV_brushedColorRGBA == null
			    || _ObjectUV_brushedColorRGBA.width != source.width || _ObjectUV_brushedColorRGBA.height != source.height
			    || _ObjectUV_brushedColorRGBA.UdimsCount != source.UdimsCount;
		    if (!need) return;
		    _ObjectUV_brushedColorRGBA?.Dispose();
		    _ObjectUV_brushedColorRGBA = new RenderUdims(source.udims_sectors, source.widthHeight,
			    GenData_Masks.colorBrushFormat, GenData_Masks.masksFilter, Color.clear, 0);
	    }

	    /// <summary>Collapse into scene buffer. Fully synchronous — no coroutine.
	    /// Order: composite → copy to scene buffer → replace layers → copy to new layer. All in one frame.</summary>
	    public bool CollapseLayersIntoScene()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || stack.Layers == null || stack.Layers.Count == 0) return false;
		    RenderUdims firstContent = null;
		    foreach (var l in stack.Layers)
		    {
			    if (l.Visible && l.Content != null) { firstContent = l.Content; break; }
		    }
		    if (firstContent == null)
		    {
			    UnityEngine.Debug.LogWarning("[Inpaint_MaskPainter] CollapseLayersIntoScene: no visible layer with content.");
			    return false;
		    }
		    EnsureLayerStackCompositeTemp(firstContent);
		    if (_layerStackCompositeTemp == null) return false;

		    // 1. Composite all visible layers into temp
		    _isCollapsingLayers = true;
		    stack.CompositeTo(_layerStackCompositeTemp);

		    // 2. Copy composite into scene buffer
		    EnsureSceneBufferSameSizeAs(_layerStackCompositeTemp);
		    if (_ObjectUV_brushedColorRGBA != null)
			    stack.CopyAllSlices(_layerStackCompositeTemp, _ObjectUV_brushedColorRGBA);

		    // 3. Replace stack with one empty layer (injection is skipped because _isCollapsingLayers)
		    stack.ReplaceLayersWithOneEmpty();

		    // 4. Copy composite into the new single layer so display shows it
		    var singleLayer = stack.Layers != null && stack.Layers.Count == 1 ? stack.Layers[0] : null;
		    if (singleLayer != null)
		    {
			    if (singleLayer.Content == null)
				    stack.EnsureContentForLayerIfNeeded(singleLayer);
			    if (singleLayer.Content != null
			        && singleLayer.Content.width == _layerStackCompositeTemp.width
			        && singleLayer.Content.height == _layerStackCompositeTemp.height
			        && singleLayer.Content.UdimsCount == _layerStackCompositeTemp.UdimsCount)
			    {
				    stack.CopyAllSlices(_layerStackCompositeTemp, singleLayer.Content);
				    singleLayer.SyncDataFromContent();
				    singleLayer.HasReceivedSceneInject = true;
				    singleLayer.Name = stack.ConsumeNextDefaultCollapseLayerName();
			    }
		    }
		    _isCollapsingLayers = false;
		    ClearSmudgePreferArtIconUntilLayerPaint();
		    _inpaintLayerColorCommitSerial++;
		    if (Objects_Renderer_MGR.instance != null)
			    Objects_Renderer_MGR.instance.ReRenderAll_soon();
		    UnityEngine.Debug.Log("[Inpaint_MaskPainter] CollapseLayersIntoScene: composite → scene buffer → single enumerated Collapse N layer (synchronous).");
		    return true;
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
		        al.NoColorMask?.ClearTheTextures(Color.clear);
	        }
	        _ObjectUV_brushedColorRGBA?.ClearTheTextures(Color.clear);
	        isPaintMaskEmpty=true;
	        ClearSmudgePreferArtIconUntilLayerPaint();
	        _inpaintLayerColorCommitSerial = 0;
	        _inpaintLayerColorSerialBumpedThisBrushStroke = false;
	    }


	    protected override void OnBucketFill_button(){
	        if (MainViewport_UI.instance?.showing != MainViewport_UI.Showing.UsualView) return;
	        if (WorkflowRibbon_UI.instance == null || !WorkflowRibbon_UI.instance.isMode_using_img2img()) return;
	        var target = GetPaintTarget();
	        if (target == null) return;
	        var sd = SD_WorkflowOptionsRibbon_UI.instance;
	        if (sd == null) return;
	        Color col = sd.brushColor;
	        PaintUndo_MGR.EnsureExists();
	        PaintUndo_MGR.instance?.SchedulePreStrokeCapture(target);
	        OnBucketFill_orDelete_button( col, target.texArray,  visibilTex:null );
	        isPaintMaskEmpty = false;
	        ClearSmudgePreferArtIconUntilLayerPaint();
	        _inpaintLayerColorCommitSerial++;
	    }
	    protected override void OnDelete_button(){//different to ResetPaintMask(), might be only for some isolated mesh.
	        if (MainViewport_UI.instance?.showing != MainViewport_UI.Showing.UsualView) return;
	        var target = GetPaintTarget();
	        if (target == null) return;
	        OnBucketFill_orDelete_button( Color.clear, target.texArray,  visibilTex:null );
	    }

	    protected override bool isAllowedToShow_BrushCursorNow()
	        => MainViewport_UI.instance?.showing == MainViewport_UI.Showing.UsualView
	           && (WorkflowRibbon_UI.instance?.isMode_using_img2img() ?? false);

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

	    public override Vector2 getViewportSize() {
	        var mv = MainViewport_UI.instance;
	        if (mv == null) return Vector2.zero;
	        return mv.mainViewportRect.rect.size;
	    }

	    public override Vector2 getViewportCursorPos01(bool forceMainViewport=false){
	        var mv = MainViewport_UI.instance;
	        if (mv == null) return Vector2.zero;
	        return mv.cursorMainViewportPos01;
	    }

	    protected override void OnUpdateChildren()
	    {
		    UpdateSmudgeHoverCursorSample();
		    UpdateValueAssistLiveCursorSample();
	    }

	    /// <summary>Value Assist live predict: throttled GPU texel under cursor → MLP → quiet ribbon arm.</summary>
	    void UpdateValueAssistLiveCursorSample()
	    {
		    if (!ValuePaintLivePredictor.IsLiveActive) return;
		    if (_valueAssistLiveReadInFlight) return;
		    var sd = SD_WorkflowOptionsRibbon_UI.instance;
		    if (sd == null || sd.isSmudge || !sd.isPositive) return;
		    var workflow = WorkflowRibbon_UI.instance;
		    if (workflow == null || workflow.currentMode() != WorkflowRibbon_CurrMode.Inpaint_Color) return;
		    if (!isAllowedToShow_BrushCursorNow()) return;
		    var mv = MainViewport_UI.instance;
		    if (mv == null || !mv.isCursorHoveringMe()) return;

		    Vector2 vp = getViewportCursorPos01();
		    float thr = _smudgeCursorViewportMoveThresh;
		    if ((vp - _valueAssistLiveLastVp01).sqrMagnitude < thr * thr
		        && Time.unscaledTime - _valueAssistLiveLastTime < _smudgeCursorReadMinInterval)
			    return;

		    RenderUdims sampleSrc = null;
		    // Prefer visible mesh/scene under tip (accum); Content alone is often empty α on first stroke.
		    var orm = Objects_Renderer_MGR.instance;
		    if (orm != null) sampleSrc = orm.accumulationTextures_ref();
		    if (sampleSrc == null || sampleSrc.texArray == null || sampleSrc.udims_sectors == null || sampleSrc.udims_sectors.Count == 0)
			    sampleSrc = GetPaintTarget();
		    if (sampleSrc == null || sampleSrc.texArray == null || sampleSrc.udims_sectors == null || sampleSrc.udims_sectors.Count == 0)
			    return;

		    if (!TryViewportToAccumTexel(vp, sampleSrc, out int slice, out int px, out int py))
			    return;

		    _valueAssistLiveReadInFlight = true;
		    _valueAssistLiveLastVp01 = vp;
		    _valueAssistLiveLastTime = Time.unscaledTime;
		    _valueAssistLivePendingFormat = sampleSrc.texArray.graphicsFormat;

		    AsyncGPUReadback.Request(sampleSrc.texArray, 0, px, 1, py, 1, slice, 1, _valueAssistLivePendingFormat,
			    OnValueAssistLiveReadbackComplete);
	    }

	    void OnValueAssistLiveReadbackComplete(AsyncGPUReadbackRequest req)
	    {
		    if (this == null) return;
		    _valueAssistLiveReadInFlight = false;
		    if (req.hasError) return;
		    if (!ValuePaintLivePredictor.IsLiveActive) return;
		    if (!TryDecodeSmudgeCursorReadback(req, _valueAssistLivePendingFormat, out Color c))
			    return;
		    // Empty paint texel: do NOT fall back to brush color (feeds the model its own output → feedback loop).
		    // Skip this sample; next ticks may hit filled Content or accum (when Content source unavailable).
		    if (c.a < 0.04f)
			    return;
		    if (!ValuePaintLivePredictor.TryPredictFromSurface(c, out _))
			    return;
		    if (ValuePaintLivePredictor.HasLastProposal
		        && ValuePaintLivePredictor.ShouldAnnounceBandChange(ValuePaintLivePredictor.LastProposal.DesiredBin)
		        && Cursor_UI.instance != null)
			    Cursor_UI.instance.SetCursorColor(ValuePaintProposalApplier.GrayForBand(ValuePaintLivePredictor.LastProposal.DesiredBin));
	    }

	    /// <summary>While smudge is active, tint the viewport brush ring from the mesh accumulation color under the cursor (throttled GPU readback).</summary>
	    void UpdateSmudgeHoverCursorSample()
	    {
		    if (Cursor_UI.instance == null) return;
		    var sd = SD_WorkflowOptionsRibbon_UI.instance;
		    if (sd == null || !sd.isSmudge) return;
		    if (_smudgeCursorReadInFlight) return;
		    if (!isAllowedToShow_BrushCursorNow()) return;
		    var mv = MainViewport_UI.instance;
		    if (mv == null || !mv.isCursorHoveringMe()) return;

		    Vector2 vp = getViewportCursorPos01();
		    float thr = _smudgeCursorViewportMoveThresh;
		    if ((vp - _smudgeCursorLastReadVp01).sqrMagnitude < thr * thr
		        && Time.unscaledTime - _smudgeCursorLastReadTime < _smudgeCursorReadMinInterval)
			    return;

		    var orm = Objects_Renderer_MGR.instance;
		    if (orm == null) return;
		    var acc = orm.accumulationTextures_ref();
		    if (acc == null || acc.texArray == null || acc.udims_sectors == null || acc.udims_sectors.Count == 0) return;

		    if (!TryViewportToAccumTexel(vp, acc, out int slice, out int px, out int py))
			    return;

		    _smudgeCursorReadInFlight = true;
		    _smudgeCursorLastReadVp01 = vp;
		    _smudgeCursorLastReadTime = Time.unscaledTime;
		    _smudgeCursorPendingReadbackFormat = acc.texArray.graphicsFormat;

		    AsyncGPUReadback.Request(acc.texArray, 0, px, 1, py, 1, slice, 1, _smudgeCursorPendingReadbackFormat,
			    OnSmudgeCursorReadbackComplete);
	    }

	    static bool TryViewportToAccumTexel(Vector2 viewport01, RenderUdims accum, out int slice, out int px, out int py)
	    {
		    slice = 0;
		    px = py = 0;
		    var cam = UserCameras_MGR.instance?._curr_viewCamera?.myCamera;
		    if (cam == null) return false;
		    if (!PaintSymmetryMesh.TryPreferredRaycast(cam, viewport01, out RaycastHit hit)) return false;

		    Vector2 uv = hit.textureCoord;
		    int sx = Mathf.Max(0, Mathf.CeilToInt(uv.x) - 1);
		    int sy = Mathf.Max(0, Mathf.CeilToInt(uv.y) - 1);
		    float uLoc = uv.x - sx;
		    float vLoc = uv.y - sy;
		    uLoc = Mathf.Repeat(uLoc, 1f);
		    vLoc = Mathf.Repeat(vLoc, 1f);

		    var targetSector = new UDIM_Sector(sx, sy);
		    slice = 0;
		    bool foundSector = false;
		    for (int i = 0; i < accum.udims_sectors.Count; i++)
		    {
			    var sec = accum.udims_sectors[i];
			    if (sec.x == targetSector.x && sec.y == targetSector.y)
			    {
				    slice = i;
				    foundSector = true;
				    break;
			    }
		    }
		    if (!foundSector)
			    return false;
		    if (slice < 0 || slice >= accum.texArray.volumeDepth)
			    return false;

		    px = Mathf.Clamp((int)(uLoc * accum.width), 0, accum.width - 1);
		    py = Mathf.Clamp((int)(vLoc * accum.height), 0, accum.height - 1);
		    return true;
	    }

	    void OnSmudgeCursorReadbackComplete(AsyncGPUReadbackRequest req)
	    {
		    if (this == null) return;
		    _smudgeCursorReadInFlight = false;
		    if (req.hasError) return;
		    if (Cursor_UI.instance == null) return;
		    var sd = SD_WorkflowOptionsRibbon_UI.instance;
		    if (sd == null || !sd.isSmudge) return;

		    Color c;
		    if (TryDecodeSmudgeCursorReadback(req, _smudgeCursorPendingReadbackFormat, out c))
			    Cursor_UI.instance.SetCursorColor(c);
	    }

	    /// <summary>Decode one texel from <see cref="AsyncGPUReadback.Request"/> using the same <paramref name="readbackFormat"/> passed to that API. One typed/native read per callback.</summary>
	    static bool TryDecodeSmudgeCursorReadback(AsyncGPUReadbackRequest req, GraphicsFormat readbackFormat, out Color c)
	    {
		    c = Color.white;
		    try
		    {
			    switch (readbackFormat)
			    {
				    case GraphicsFormat.R32G32B32A32_SFloat:
					    return TryDecodeSmudgeReadback_Rgba32F(req, out c);
				    case GraphicsFormat.R16G16B16A16_SFloat:
					    return TryDecodeSmudgeReadback_Rgba16F(req, out c);
				    case GraphicsFormat.R16G16B16A16_UNorm:
					    return TryDecodeSmudgeReadback_Rgba16UNorm(req, out c);
				    case GraphicsFormat.B8G8R8A8_UNorm:
				    case GraphicsFormat.B8G8R8A8_SRGB:
					    return TryDecodeSmudgeReadback_Bgra8(req, out c);
				    case GraphicsFormat.R8G8B8A8_UNorm:
				    case GraphicsFormat.R8G8B8A8_SRGB:
					    return TryDecodeSmudgeReadback_Rgba8(req, out c);
				    default:
					    return TryDecodeSmudgeReadback_GuessFromBytes(req, out c);
			    }
		    }
		    catch (Exception)
		    {
			    return false;
		    }
	    }

	    static Color ClampRgbForCursorRing(float r, float g, float b)
	    {
		    if (!float.IsFinite(r)) r = 1f;
		    if (!float.IsFinite(g)) g = 1f;
		    if (!float.IsFinite(b)) b = 1f;
		    return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
	    }

	    static bool TryDecodeSmudgeReadback_Rgba32F(AsyncGPUReadbackRequest req, out Color c)
	    {
		    c = Color.white;
		    var f = req.GetData<float>();
		    if (f.Length < 4) return false;
		    c = ClampRgbForCursorRing(f[0], f[1], f[2]);
		    return true;
	    }

	    static bool TryDecodeSmudgeReadback_Rgba16F(AsyncGPUReadbackRequest req, out Color c)
	    {
		    c = Color.white;
		    var bytes = req.GetData<byte>();
		    if (bytes.Length < 8) return false;
		    float r = HalfUShortToFloat((ushort)(bytes[0] | (bytes[1] << 8)));
		    float g = HalfUShortToFloat((ushort)(bytes[2] | (bytes[3] << 8)));
		    float b = HalfUShortToFloat((ushort)(bytes[4] | (bytes[5] << 8)));
		    c = ClampRgbForCursorRing(r, g, b);
		    return true;
	    }

	    static bool TryDecodeSmudgeReadback_Rgba16UNorm(AsyncGPUReadbackRequest req, out Color c)
	    {
		    c = Color.white;
		    var bytes = req.GetData<byte>();
		    if (bytes.Length < 8) return false;
		    float r = (ushort)(bytes[0] | (bytes[1] << 8)) / 65535f;
		    float g = (ushort)(bytes[2] | (bytes[3] << 8)) / 65535f;
		    float b = (ushort)(bytes[4] | (bytes[5] << 8)) / 65535f;
		    c = ClampRgbForCursorRing(r, g, b);
		    return true;
	    }

	    static bool TryDecodeSmudgeReadback_Rgba8(AsyncGPUReadbackRequest req, out Color c)
	    {
		    c = Color.white;
		    var bytes = req.GetData<byte>();
		    if (bytes.Length < 4) return false;
		    c = new Color(bytes[0] / 255f, bytes[1] / 255f, bytes[2] / 255f, 1f);
		    return true;
	    }

	    static bool TryDecodeSmudgeReadback_Bgra8(AsyncGPUReadbackRequest req, out Color c)
	    {
		    c = Color.white;
		    var bytes = req.GetData<byte>();
		    if (bytes.Length < 4) return false;
		    // B, G, R, A
		    c = new Color(bytes[2] / 255f, bytes[1] / 255f, bytes[0] / 255f, 1f);
		    return true;
	    }

	    /// <summary>If format is uncommon, infer layout from byte count (still a single <see cref="AsyncGPUReadbackRequest.GetData{T}"/> call).</summary>
	    static uint SmudgeReadbackU32LE(NativeArray<byte> bytes, int offset)
	    {
		    return (uint)bytes[offset]
		           | ((uint)bytes[offset + 1] << 8)
		           | ((uint)bytes[offset + 2] << 16)
		           | ((uint)bytes[offset + 3] << 24);
	    }

	    static bool TryDecodeSmudgeReadback_GuessFromBytes(AsyncGPUReadbackRequest req, out Color c)
	    {
		    c = Color.white;
		    var bytes = req.GetData<byte>();
		    // Unknown format: avoid treating 8–15 bytes as half (could be RGBA8 + row/alignment padding).
		    if (bytes.Length >= 16)
		    {
			    Span<byte> s = stackalloc byte[4];
			    BinaryPrimitives.WriteUInt32LittleEndian(s, SmudgeReadbackU32LE(bytes, 0));
			    float r = MemoryMarshal.Read<float>(s);
			    BinaryPrimitives.WriteUInt32LittleEndian(s, SmudgeReadbackU32LE(bytes, 4));
			    float g = MemoryMarshal.Read<float>(s);
			    BinaryPrimitives.WriteUInt32LittleEndian(s, SmudgeReadbackU32LE(bytes, 8));
			    float b = MemoryMarshal.Read<float>(s);
			    c = ClampRgbForCursorRing(r, g, b);
			    return true;
		    }
		    if (bytes.Length == 8)
		    {
			    float r = HalfUShortToFloat((ushort)(bytes[0] | (bytes[1] << 8)));
			    float g = HalfUShortToFloat((ushort)(bytes[2] | (bytes[3] << 8)));
			    float b = HalfUShortToFloat((ushort)(bytes[4] | (bytes[5] << 8)));
			    c = ClampRgbForCursorRing(r, g, b);
			    return true;
		    }
		    if (bytes.Length >= 4)
		    {
			    c = new Color(bytes[0] / 255f, bytes[1] / 255f, bytes[2] / 255f, 1f);
			    return true;
		    }
		    return false;
	    }

	    static float HalfUShortToFloat(ushort h)
	    {
		    int sign = h >> 15;
		    int exp = (h >> 10) & 0x1f;
		    int mant = h & 0x3ff;
		    if (exp == 0)
		    {
			    if (mant == 0)
				    return sign != 0 ? -0f : 0f;
			    float m = mant / 1024f;
			    float v = m * Mathf.Pow(2f, -14f);
			    return sign != 0 ? -v : v;
		    }
		    if (exp == 31)
			    return mant != 0 ? float.NaN : (sign != 0 ? float.NegativeInfinity : float.PositiveInfinity);
		    exp = exp - 15 + 127;
		    mant <<= 13;
		    uint bits = (uint)((sign << 31) | (exp << 23) | mant);
		    Span<byte> s = stackalloc byte[4];
		    BinaryPrimitives.WriteUInt32LittleEndian(s, bits);
		    return MemoryMarshal.Read<float>(s);
	    }

	    protected override Vector3Int maskResolution(){
	        int fallBack = GenData_Masks.COLOR_BRUSH_RESOLUTION;
	        if (ModelsHandler_3D.instance == null) return new Vector3Int(fallBack, fallBack, 0);
	        IReadOnlyList<UDIM_Sector> allUdims = ModelsHandler_3D.instance._allKnownUdims;
	        int numSlices = allUdims != null ? allUdims.Count : 0;
	        int w = fallBack, h = fallBack;
	        // Layer RTs must match the mesh accumulation texture. Using only "brush precision" while accumulation
	        // uses scene texture quality breaks multi-layer (several array blits); single-layer often still looked OK due to one scaled blit.
	        if (numSlices > 0 && Objects_Renderer_MGR.instance != null)
	        {
		        // SceneResolution_MGR briefly upscales accumulation to 5k for img2img (and 4k for save composite). If we read that
		        // boosted size here, InitTextures → EnsureResolution reallocates every layer at 5k and clears all paint.
		        int stableBoost = SceneResolution_MGR.PaintLayerSquareSizeDuringImg2ImgAccumBoost;
		        if (stableBoost <= 0)
			        stableBoost = SceneResolution_MGR.PaintLayerSquareSizeDuringSaveCompositeBoost;
		        if (stableBoost > 0)
		        {
			        w = h = stableBoost;
		        }
		        else
		        {
			        var acc = Objects_Renderer_MGR.instance.accumulationTextures_ref();
			        if (acc != null && acc.texArray != null && acc.width > 0 && acc.height > 0 && acc.UdimsCount == numSlices)
			        {
				        w = acc.width;
				        h = acc.height;
			        }
			        else
			        {
				        // Accumulation not allocated yet (e.g. before Objects_Renderer_MGR.Start) — still match scene UV size, not brush-precision-only.
				        int rq = SceneResolution_MGR.resultTexQuality;
				        if (rq > 0) { w = h = rq; }
			        }
		        }
	        }
	        return new Vector3Int(w, h, numSlices);
	    }

	    protected override float getBrushStrength(){//strength [0,1] --> [-1,1]
	        var orib = SD_WorkflowOptionsRibbon_UI.instance;
	        if (orib == null) return 0f;
	        if (orib.isSmudge) return orib.maskBrushOpacity; // smudge always positive
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
	        if (ModelsHandler_3D.instance == null || Objects_Renderer_MGR.instance == null
	            || UserCameras_MGR.instance?._curr_viewCamera == null)
		        return;

	        isPaintMaskEmpty = false;
	        if (isFirstFrameOfStroke) {
		        _prevStrength = suggested_brushStrength;
		        _inpaintLayerColorSerialBumpedThisBrushStroke = false;
	        }
        
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

	        // Smudge: apply weighted-average blur under brush coverage each frame.
	        bool smudgeActive = SD_WorkflowOptionsRibbon_UI.instance != null && SD_WorkflowOptionsRibbon_UI.instance.isSmudge;
	        if (smudgeActive){
	            if (_applyBrushStroke_toUvMask == null)
	                _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);
	            if (_applyBrushStroke_toUvMask != null){
	                var stackForSmudge = PaintLayerStack_MGR.instance;
	                var smudgeTarget = AlignSmudgePaintTargetToActiveLayerContentIfNeeded(target, stackForSmudge);
	                if (isFirstFrameOfStroke)
		                _smudgeUndoSegmentDest = null;
	                if (isFirstFrameOfStroke)
		                BeginSmudgeStrokeAdaptiveRoutingIfNeeded(smudgeTarget, stackForSmudge);
	                else
		                _smudgeStrokeMaxUnscaledDt = Mathf.Max(_smudgeStrokeMaxUnscaledDt, Time.unscaledDeltaTime);
	                ResolveSmudgeDestinationAndAccum(smudgeTarget, stackForSmudge, out RenderUdims smudgeDest, out RenderUdims smudgeAcc,
		                out PaintUndoNonStackTarget smudgeUndoKind, out float smudgeKernelSpacingMul);
	                if (smudgeDest != null && smudgeDest.texArray != null)
	                {
		                bool destSegmentChanged = _smudgeUndoSegmentDest != null && !ReferenceEquals(_smudgeUndoSegmentDest, smudgeDest);
		                // Continuation frames only: first frame already covered by isFirstFrameOfStroke; catches late-ready chunks / no-op first apply.
		                bool needUndoForLateReady = !isFirstFrameOfStroke && _smudgeUndoSegmentDest == null;
		                bool needUndoThisSegment = isFirstFrameOfStroke || destSegmentChanged || needUndoForLateReady;
		                if (needUndoThisSegment && _applyBrushStroke_toUvMask.SmudgeDispatchPreconditionsMet(smudgeDest, currBrushStroke_R8)) {
			                PaintUndo_MGR.EnsureExists();
			                PaintUndo_MGR.instance?.SchedulePreStrokeCapture(smudgeDest, smudgeUndoKind, 0, immediateGpuCopyBeforeMutation: true);
		                }
		                var sdRibbon = SD_WorkflowOptionsRibbon_UI.instance;
		                float smudgeStr = (sdRibbon != null ? sdRibbon.maskBrushOpacity : 1f) * PaintTab_SmudgeBrushOptions.Strength01;
		                float smudgeAngle = PaintTab_SmudgeBrushOptions.AngleDeg;
		                float brushSize = BrushRibbon_UI_Size.GetBrushSize01();
		                // LayerSmudgeGateOpen alone used to force SMUDGE_LAYER_PAINT_ONLY and drop mesh underlay — that prevented
		                // sampling layer-over-accumulation (Objects_Renderer puts UV gen art in accum before layer blit; see ApplyBrushStroke_IntoMask SmudgeCombinedLayerOverAccum).
		                bool layerGateForDest = stackForSmudge != null && stackForSmudge.Layers != null && stackForSmudge.Layers.Count > 0
		                    && SmudgeStrokeRouter.LayerSmudgeGateOpen(stackForSmudge, smudgeDest);
		                bool layerPaintOnly = layerGateForDest && smudgeAcc == null;
		                RenderUdims accForSmudge = smudgeAcc;
		                if (_applyBrushStroke_toUvMask.Apply_smudge_to_ColorBrushTex(currBrushStroke_R8, smudgeStr, brushSize, smudgeDest, accForSmudge, smudgeKernelSpacingMul, smudgeAngle, 1.35f, layerPaintOnly))
			                _smudgeUndoSegmentDest = smudgeDest;
		                Objects_Renderer_MGR.instance?.ReRenderAll_soon();
	                }
	            }
	        }
	        else
	        {
		        // Normal paint: commit curr−prev into the color layer each frame (same idea as Projections_MaskPainter), not only on mouse-up.
		        if (_applyBrushStroke_toUvMask == null)
			        _applyBrushStroke_toUvMask = FindObjectOfType<ApplyBrushStroke_ToUvMask>(true);
		        if (_applyBrushStroke_toUvMask != null
		            && _applyBrushStroke_toUvMask.CanDispatch_ColorBrushTex(currBrushStroke_R8, target))
		        {
			        if (!_undoPreStrokeScheduledForStroke)
			        {
				        PaintUndo_MGR.EnsureExists();
				        PaintUndo_MGR.instance?.SchedulePreStrokeCapture(target);
				        _undoPreStrokeScheduledForStroke = true;
			        }
			        float sign = Mathf.Sign(suggested_brushStrength);
			        float maxStrength = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.maskBrushOpacity : 1f;
			        if (_applyBrushStroke_toUvMask.Apply_into_ColorBrushTex(prevBrushStroke_R8, currBrushStroke_R8, sign, maxStrength, target, useBrushStrokeDelta: true)) {
				        // smart-value-paint: only count verify after a successful UV commit (no false SawApply).
				        ValuePaintProposalApplier.OnColorBrushApplied(target);
				        ClearSmudgePreferArtIconUntilLayerPaint();
				        if (!_inpaintLayerColorSerialBumpedThisBrushStroke) {
					        _inpaintLayerColorSerialBumpedThisBrushStroke = true;
					        _inpaintLayerColorCommitSerial++;
				        }
				        Objects_Renderer_MGR.instance?.ReRenderAll_soon();
			        }
		        }
	        }
	    }


	    protected override void OnFinal_ApplyIncomingVals_intoMask( RenderTexture prevBrushStroke_R8, 
	                                                                RenderTexture currBrushStroke_R8 ){
	        _undoPreStrokeScheduledForStroke = false;
	        // Smudge already applies each frame during drag; just fire stroke end and re-render.
	        bool smudgeActive = SD_WorkflowOptionsRibbon_UI.instance != null && SD_WorkflowOptionsRibbon_UI.instance.isSmudge;
	        if (smudgeActive){
	            var layerPt = GetPaintTarget();
	            if (layerPt != null){
	                layerPt = AlignSmudgePaintTargetToActiveLayerContentIfNeeded(layerPt, PaintLayerStack_MGR.instance);
	                ResolveSmudgeDestinationAndAccum(layerPt, PaintLayerStack_MGR.instance, out RenderUdims smudgeWritten, out _, out _, out _);
	                Objects_Renderer_MGR.instance?.ReRenderAll_soon();
	                if (smudgeWritten != null)
	                {
		                RequestReRenderAfterGpuCommit(smudgeWritten);
		                var al = PaintLayerStack_MGR.instance?.ActiveLayer;
		                if (al != null && smudgeWritten == al.Content)
		                    StartCoroutine(DeferredReRenderAfterStroke());
	                }
	            }
	            if (_smudgeRouteObsPending && PaintUndo_MGR.instance != null) {
		            var sch = PaintUndo_MGR.instance.UndoScheduler;
		            float op = PaintLayerStack_MGR.instance?.ActiveLayer != null
			            ? Mathf.Clamp01(PaintLayerStack_MGR.instance.ActiveLayer.Opacity)
			            : 1f;
		            // Mutually exclusive vs cold-start cutoff (0.5) so mid-opacity isn’t “aligned” for both arms.
		            bool opacityAlign = (_smudgeRouteObsArm == 0 && op >= 0.5f) || (_smudgeRouteObsArm == 1 && op < 0.5f);
		            bool smooth = _smudgeStrokeMaxUnscaledDt <= sch.smudgeRouteSuccessMaxFrameTimeSec;
		            sch.RegisterSmudgeRouteObservation(_smudgeRouteObsBucket, _smudgeRouteObsArm, opacityAlign && smooth);
	            }
	            _smudgeRouteObsPending = false;
	            _smudgeRouteLockForStroke = SmudgeAdaptiveRouteLock.Inactive;
	            _smudgeStrokeLockedPaintContent = null;
	            _smudgeUndoSegmentDest = null;
	            Act_OnPaintStrokeEnd?.Invoke();
	            return;
	        }

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
	        // Stroke pixels are already written each frame via Apply_into_ColorBrushTex(..., useBrushStrokeDelta: true) in OnRenderIntoCurrTex_please.
	        Objects_Renderer_MGR.instance?.ReRenderAll_soon();
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

	        PaintUndo_MGR.EnsureExists();

	        PaintLayerStack_MGR.OnLayerAdded += OnLayerAdded_InjectScene;
	        UnityEngine.Debug.Log("[Inpaint_MaskPainter] Awake complete: OnLayerAdded subscribed, material created.");

	        _trackedWorkflowModeForSceneFlush = WorkflowRibbon_UI.instance != null
		        ? WorkflowRibbon_UI.instance.currentMode()
		        : WorkflowRibbon_CurrMode.ProjectionsMasking;
	        WorkflowRibbon_UI._Act_OnModeChanged += OnWorkflowModeChanged_ClearStaleSceneBuffer;
	        Art2D_IconsUI_List._Act_mainIcon_selected += OnMainArtIconSelected_ArmSmudgeToUvPaintedBrushIfLayerStackIdle;
	        OnMainArtIconSelected_ArmSmudgeToUvPaintedBrushIfLayerStackIdle(Art2D_IconsUI_List.instance?._mainSelectedIcon);

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

	    /// <summary>Copy source into layer's Content (all UDIM slices, no premultiplication). Sets HasReceivedSceneInject.</summary>
	    bool TryInjectIntoLayer(PaintLayer layer, RenderUdims source)
	    {
		    if (layer == null || source == null) return false;
		    if (layer.Content == null) return false;
		    if (source.width != layer.Content.width || source.height != layer.Content.height || source.UdimsCount != layer.Content.UdimsCount) return false;
		    Graphics.CopyTexture(source.texArray, layer.Content.texArray);
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

	    // --- Scene injection: active layer and new-layer (OnLayerAdded) ---
	    /// <summary>Inject scene into the current active layer when it needs a base (no index check). When user adds or selects a layer, that layer becomes active and gets scene so behavior is adaptable, not fixed to bottom.</summary>
	    void EnsureSceneInjectedIntoActiveLayer()
	    {
		    if (_isCollapsingLayers) return; // collapse coroutine will write composite into the new layer; don't overwrite
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack?.ActiveLayer == null || _ObjectUV_brushedColorRGBA == null) return;
		    if (stack.ActiveLayer.Content == null) return;
		    if (stack.ActiveLayer.HasReceivedSceneInject) return;
		    TryInjectSceneIntoLayer(stack.ActiveLayer);
	    }

	    /// <summary>Before compositing, ensure the bottom layer (index 0) has scene injected so it appears in the composite. Called from display path when 2+ layers.</summary>
	    void EnsureBottomLayerHasSceneForComposite()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || _ObjectUV_brushedColorRGBA == null || stack.Layers == null || stack.Layers.Count == 0) return;
		    var bottom = stack.Layers[0];
		    if (bottom.Content == null || bottom.HasReceivedSceneInject) return;
		    TryInjectSceneIntoLayer(bottom);
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

	    /// <summary>When user clicks New Layer: ensure the new layer has Content, then inject the composite of (scene + all layers below) into it. Subscribed in Awake to PaintLayerStack_MGR.OnLayerAdded.</summary>
	    void OnLayerAdded_InjectScene(PaintLayer newLayer)
	    {
		    if (newLayer == null) return;
		    if (_isCollapsingLayers) return; // collapse coroutine will copy composite into the new layer; don't inject
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null) return;

		    int newIndex = -1;
		    for (int i = 0; i < stack.Layers.Count; i++)
			    if (stack.Layers[i] == newLayer) { newIndex = i; break; }
		    if (newIndex < 0) return;

		    UnityEngine.Debug.Log($"[Inpaint_MaskPainter] OnLayerAdded_InjectScene: new layer '{newLayer.Name}' index={newIndex}, injecting composite below into layer (no empty override).");

		    stack.EnsureContentForLayerIfNeeded(newLayer);
		    if (newLayer.Content == null)
		    {
			    UnityEngine.Debug.LogWarning("[Inpaint_MaskPainter] OnLayerAdded_InjectScene: new layer has no Content (resolution not set). Deferring.");
			    StartCoroutine(DeferredEnsureContentForNewLayer(newLayer));
			    return;
		    }

		    // Inject scene + all layers below into this layer so the layer has the data (solved earlier: injection into layer, not overwrite on active).
		    if (_ObjectUV_brushedColorRGBA != null && newIndex > 0)
		    {
			    SyncStackResolutionFromSceneBuffer(stack, _ObjectUV_brushedColorRGBA);
			    stack.CompositeBelowInto(_ObjectUV_brushedColorRGBA, newLayer.Content, newIndex);
			    newLayer.HasReceivedSceneInject = true;
		    }

		    if (Objects_Renderer_MGR.instance != null)
			    Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }

	    /// <summary>Retry ensuring new layer has Content next frame, then inject composite below into it (same as OnLayerAdded_InjectScene).</summary>
	    IEnumerator DeferredEnsureContentForNewLayer(PaintLayer newLayer)
	    {
		    yield return null;
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || newLayer == null) yield break;
		    stack.EnsureContentForLayerIfNeeded(newLayer);
		    if (newLayer.Content == null) yield break;
		    int newIndex = -1;
		    for (int i = 0; i < stack.Layers.Count; i++)
			    if (stack.Layers[i] == newLayer) { newIndex = i; break; }
		    if (newIndex > 0 && _ObjectUV_brushedColorRGBA != null)
		    {
			    SyncStackResolutionFromSceneBuffer(stack, _ObjectUV_brushedColorRGBA);
			    stack.CompositeBelowInto(_ObjectUV_brushedColorRGBA, newLayer.Content, newIndex);
			    newLayer.HasReceivedSceneInject = true;
		    }
		    if (Objects_Renderer_MGR.instance != null)
			    Objects_Renderer_MGR.instance.ReRenderAll_soon();
	    }

	    /// <summary>Ensure stack resolution matches scene buffer so CompositeToOnTopOfBase can create temps; prevents first layer disappearing when 2+ layers.</summary>
	    static void SyncStackResolutionFromSceneBuffer(PaintLayerStack_MGR stack, RenderUdims sceneBuffer)
	    {
		    if (stack == null || sceneBuffer == null) return;
		    int w = sceneBuffer.width;
		    int h = sceneBuffer.height;
		    int slices = sceneBuffer.UdimsCount;
		    if (w <= 0 || h <= 0 || slices <= 0) return;
		    stack.EnsureResolution(new Vector3Int(w, h, slices));
	    }

	    /// <summary>When we have 2+ layers but no scene buffer yet (e.g. user added layer before first paint), create scene buffer and sync stack so we can composite all layers. Called from ApplyColorLayer_To_UV_Textures when multiLayer.</summary>
	    void EnsureSceneBufferForDisplay()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || stack.Layers == null || stack.Layers.Count == 0) return;
		    if (_ObjectUV_brushedColorRGBA != null) return;
		    var res = maskResolution();
		    if (res.x <= 0 || res.y <= 0 || res.z <= 0) return;
		    var allUdims = ModelsHandler_3D.instance != null ? ModelsHandler_3D.instance._allKnownUdims : null;
		    if (allUdims == null || allUdims.Count != res.z) return;
		    _ObjectUV_brushedColorRGBA = new RenderUdims(allUdims, new Vector2Int(res.x, res.y),
			    GenData_Masks.colorBrushFormat, GenData_Masks.masksFilter, Color.clear, 0);
		    if (stack != null)
			    stack.EnsureResolution(new Vector3Int(res.x, res.y, res.z));
	    }

	    /// <summary>Returns the display source: composite of all visible layers when 2+ layers (never active-only); else active Content or scene fallback. Uses the same EntireColorLayer shader as the display path.</summary>
	    public RenderUdims GetLayerCompositeOrFallback()
	    {
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack != null && stack.Layers != null && stack.Layers.Count > 1)
		    {
			    EnsureSceneBufferForDisplay();
			    EnsureBottomLayerHasSceneForComposite();
			    bool isColorless = WorkflowRibbon_UI.instance != null && WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor;
			    var composite = CompositeVisibleLayersIntoTemp(stack, isColorless);
			    if (composite != null) return composite;
			    if (stack.ActiveLayer?.Content != null) return stack.ActiveLayer.Content;
			    if (stack.Layers.Count > 0 && stack.Layers[0].Visible && stack.Layers[0].Content != null)
				    return stack.Layers[0].Content;
		    }
		    // Single layer: if active has a NoColorMask, build a one-layer composite so SD mask capture
		    // sees both buffers (Content + NoColorMask). Returning Content directly here drops No Color
		    // strokes from GetDisposable_ScreenMask and causes wrong/empty masks in img2img.
		    if (stack != null && stack.Layers != null && stack.Layers.Count <= 1 && stack.ActiveLayer?.Content != null
		        && stack.ActiveLayer.Visible)
		    {
			    if (stack.ActiveLayer.NoColorMask != null)
			    {
				    var singleComposite = CompositeVisibleLayersIntoTemp(stack, _isColorlessIgnored: false);
				    if (singleComposite != null) return singleComposite;
			    }
			    var activeContent = stack.ActiveLayer.Content;
			    if (_ObjectUV_brushedColorRGBA == null)
				    return activeContent;
			    if (activeContent.width == _ObjectUV_brushedColorRGBA.width && activeContent.height == _ObjectUV_brushedColorRGBA.height && activeContent.UdimsCount == _ObjectUV_brushedColorRGBA.UdimsCount)
				    return activeContent;
		    }
		    return _ObjectUV_brushedColorRGBA;
	    }

	    /// <summary>After InitTextures creates the scene buffer, inject scene into the current active layer so it has a base (adaptable). If active already has scene or has no Content, fall back to bottom so the composite always has at least one layer with scene.</summary>
	    void InjectSceneIntoAllExistingLayers()
	    {
		    if (_ObjectUV_brushedColorRGBA == null) { UnityEngine.Debug.Log("[Inpaint_MaskPainter] InjectSceneIntoAllExisting: sceneBuf is null, skipping."); return; }
		    var stack = PaintLayerStack_MGR.instance;
		    if (stack == null || stack.Layers == null || stack.Layers.Count == 0) return;
		    // Prefer active layer (e.g. newly added layer is active); otherwise ensure bottom has scene for composite.
		    if (stack.ActiveLayer != null && stack.ActiveLayer.Content != null && !stack.ActiveLayer.HasReceivedSceneInject)
		    {
			    UnityEngine.Debug.Log($"[Inpaint_MaskPainter] InjectSceneIntoAllExisting: injecting scene into active layer '{stack.ActiveLayer.Name}' (index={stack.ActiveLayerIndex}).");
			    if (TryInjectSceneIntoLayer(stack.ActiveLayer)) return;
		    }
		    var bottom = stack.Layers[0];
		    if (bottom != null && bottom.Content != null && !bottom.HasReceivedSceneInject)
		    {
			    UnityEngine.Debug.Log("[Inpaint_MaskPainter] InjectSceneIntoAllExisting: injecting scene into bottom layer (fallback so composite has base).");
			    TryInjectSceneIntoLayer(bottom);
		    }
	    }


	    /// <summary>After non-color/projection workflows, the scene snapshot buffer can hold stale data; clear on mode transitions that should start color-layer compositing from transparent base.</summary>
	    void OnWorkflowModeChanged_ClearStaleSceneBuffer(WorkflowRibbon_CurrMode mode)
	    {
		    var prev = _trackedWorkflowModeForSceneFlush;
		    _trackedWorkflowModeForSceneFlush = mode;
		    bool enteringInpaint = mode == WorkflowRibbon_CurrMode.Inpaint_Color || mode == WorkflowRibbon_CurrMode.Inpaint_NoColor;
		    if (!enteringInpaint) return;
		    bool cameFromProjection = prev == WorkflowRibbon_CurrMode.ProjectionsMasking;
		    bool noColorToColor = prev == WorkflowRibbon_CurrMode.Inpaint_NoColor && mode == WorkflowRibbon_CurrMode.Inpaint_Color;
		    if (!cameFromProjection && !noColorToColor) return;
		    _ObjectUV_brushedColorRGBA?.ClearTheTextures(Color.clear);
	    }

	    protected override void OnDestroy(){
	        WorkflowRibbon_UI._Act_OnModeChanged -= OnWorkflowModeChanged_ClearStaleSceneBuffer;
	        Art2D_IconsUI_List._Act_mainIcon_selected -= OnMainArtIconSelected_ArmSmudgeToUvPaintedBrushIfLayerStackIdle;
	        if (PaintLayerStack_MGR.instance != null)
		        PaintLayerStack_MGR.instance.OnActiveLayerChanged -= OnActiveLayerChanged_EnsureContent;
	        PaintLayerStack_MGR.OnLayerAdded -= OnLayerAdded_InjectScene;
	        _layerStackCompositeTemp?.Dispose();
	        _smudgeUnderActiveTemp?.Dispose();
	        _artIconUvColorWrapper?.Dispose();
	        // Was leaking on shutdown after heavy painting: large legacy color RGBA UV buffer (multi-UDIM)
	        // wasn't disposed here, only reallocated in EnsureBrushedColorBuffer. Held GPU memory + RTs alive
	        // until process end, contributing to slow/hung quit on big projects.
	        _ObjectUV_brushedColorRGBA?.Dispose();
	        _ObjectUV_brushedColorRGBA = null;
	        DestroyImmediate(_blitApplyEntireColorLayer_mat);
	        base.OnDestroy();
	    }

	}
}//end namespace
