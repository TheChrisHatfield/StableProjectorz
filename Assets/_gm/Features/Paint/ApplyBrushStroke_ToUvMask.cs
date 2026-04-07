using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace spz {

	//keep applying the brush stroke to uv_mask (only applies difference between prev and curr brushstroke)
	public class ApplyBrushStroke_ToUvMask : MonoBehaviour {

	    [SerializeField] ComputeShader _brushStroke_intoMask;
	    [SerializeField] ComputeShader _brushStroke_intoMaskPovs;
	    [SerializeField] ComputeShader _invertMask_shader; //used when user presses InvertMaskTool button.

	    RenderTexture _smudgeCopyRT;
	    RenderTexture _smudgeAccumCopyRT;


	    //used when user presses InvertMaskTool button.
	    public void InvertMask(RenderUdims invertThis, RenderUdims visibilityTexture){
	        int kernel = _invertMask_shader.FindKernel("CSMain");

	        _invertMask_shader.SetTexture(kernel, "_InputOutput", invertThis.texArray);
	        _invertMask_shader.SetTexture(kernel, "_Visibility_R8G8", visibilityTexture.texArray);

	        Vector3Int grps = invertThis.CalcGroups_for_ComputeShader();
	        _invertMask_shader.Dispatch(kernel, grps.x, grps.y, grps.z);
	    }


	    public void Apply_intoMask2D( RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8, 
	                                  float sign,  float maxPossibleBrushStrength01,  RenderUdims destin ){

	    }


	    public void Apply_into_ColorBrushTex( RenderTexture prevBrushStroke_R8, RenderTexture currBrushStroke_R8,
	                                          float sign, float maxPossibleBrushStrength01, RenderUdims destin ){
	        int kernel = _brushStroke_intoMask.FindKernel("CSMain");

	        TextureTools_SPZ.SetKeyword_ComputeShader(_brushStroke_intoMask, "BLEND_RGBA_ONCE", true);

	        Color paintColor = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.brushColor : Color.black;
	        _brushStroke_intoMask.SetVector("_PaintColor", paintColor);
	        _brushStroke_intoMask.SetTexture(kernel, "_CurrBrushStroke_R8", currBrushStroke_R8);//only 'curr', 'previous' is not needed.

	        _brushStroke_intoMask.SetFloat("_Sign", sign); //to know if erasing or adding.
	        _brushStroke_intoMask.SetFloat("_MaxPossibleBrushStrength01", maxPossibleBrushStrength01);
	        _brushStroke_intoMask.SetTexture(kernel, "_PaintedMask", destin.texArray);

	        RenderTexture chunksTex = Objects_Renderer_MGR.instance.chunksTexture_ref().texArray;
	        _brushStroke_intoMask.SetTexture(kernel, "_UV_Chunks_R8", chunksTex);

	        Vector4 chunks_scale = new Vector4(chunksTex.width/(float)currBrushStroke_R8.width, chunksTex.height/(float)currBrushStroke_R8.height, 0,0);
	        _brushStroke_intoMask.SetVector("_UV_Chunks_scale", chunks_scale);

	        Vector3Int grps = destin.CalcGroups_for_ComputeShader();
	        _brushStroke_intoMask.Dispatch(kernel, grps.x, grps.y, grps.z);
	    }


	    public void Apply_into_MaskUtils( RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8, 
	                                      float sign, GenData_Masks maskUtils,  int povIx ){
	        Debug.Assert(sign==-1 || sign==1);
	        bool isSingleView = maskUtils.numPOV==1;
	        if (isSingleView){
	            Apply_intoMask_singleView(prevBrushStroke_R8, currBrushStroke_R8, sign, maskUtils, povIx);
	        }else {//multiview:
	            Apply_intoMask_multiView(prevBrushStroke_R8, currBrushStroke_R8, sign, maskUtils, povIx);
	        }
	    }


	    //keep applying the brush stroke to uv_mask (only applies difference between prev and curr brushstroke)
	    void Apply_intoMask_singleView( RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8,  
	                                    float sign,  GenData_Masks utils, int povIx ){
	        RenderUdims uvMask = utils._ObjectUV_brushedMaskR8[0];//NOTICE, single-view, so using 0 for POV.
	        RenderUdims visibil = utils._ObjectUV_visibilityR8G8[0];
	        Assert_TexturesSameSize( new List<RenderTexture>(){prevBrushStroke_R8, currBrushStroke_R8, 
	                                                           uvMask.texArray, visibil.texArray} );

	        TextureTools_SPZ.SetKeyword_ComputeShader(_brushStroke_intoMask, "BLEND_RGBA_ONCE", false);
	        int kernel = _brushStroke_intoMask.FindKernel("CSMain");
	        _brushStroke_intoMask.SetTexture(kernel, "_Visibility_R8G8", visibil.texArray);

	        _brushStroke_intoMask.SetTexture(kernel, "_PrevBrushStroke_R8", prevBrushStroke_R8);
	        _brushStroke_intoMask.SetTexture(kernel, "_CurrBrushStroke_R8", currBrushStroke_R8);
	        _brushStroke_intoMask.SetFloat("_Sign", sign); //to know if erasing or adding.
	        _brushStroke_intoMask.SetTexture(kernel, "_PaintedMask", uvMask.texArray);

	        RenderTexture chunksTex = Objects_Renderer_MGR.instance.chunksTexture_ref().texArray;
	        _brushStroke_intoMask.SetTexture(kernel, "_UV_Chunks_R8", chunksTex);

	        Vector4 chunks_scale = new Vector4(chunksTex.width/(float)currBrushStroke_R8.width, chunksTex.height/(float)currBrushStroke_R8.height, 0,0);
	        _brushStroke_intoMask.SetVector("_UV_Chunks_scale", chunks_scale);

	        Vector3Int grps = uvMask.CalcGroups_for_ComputeShader();
	        _brushStroke_intoMask.Dispatch(kernel, grps.x, grps.y, grps.z);
	    }


	    void Apply_intoMask_multiView( RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8,  
	                                   float sign, GenData_Masks utils,  int povIx ){

	        CameraTools.Toggle_numPOVs_Keywords(_brushStroke_intoMaskPovs, utils.numPOV); 
        
	        int kernel = _brushStroke_intoMaskPovs.FindKernel("CSMain");
	        SetMasks_MultiPov(prevBrushStroke_R8, currBrushStroke_R8, utils, povIx, kernel);

	        _brushStroke_intoMaskPovs.SetTexture(kernel, "_PrevBrushStroke_R8", prevBrushStroke_R8);
	        _brushStroke_intoMaskPovs.SetTexture(kernel, "_CurrBrushStroke_R8", currBrushStroke_R8);
	        _brushStroke_intoMaskPovs.SetFloat("_Sign", sign); //to know if erasing or adding.

	        Vector3Int grps =  ComputeShaders_MGR.calcNumGroups(prevBrushStroke_R8);
	        _brushStroke_intoMaskPovs.Dispatch(kernel, grps.x, grps.y, grps.z);
	    }
     

	    void SetMasks_MultiPov(RenderTexture prevBrushStroke_R8,  RenderTexture currBrushStroke_R8,
	                           GenData_Masks utils, int povIx, int kernel){
        
	        var alphabet = new List<string>(){ "A", "B", "C", "D", "E", "F" };
	        int maskIx = 0;
	        int alphabetIx = 0;
	        for(int i=0; i<utils._ObjectUV_brushedMaskR8.Count; ++i){
	            RenderTexture uvMask  = utils._ObjectUV_brushedMaskR8[i]?.texArray;
	            RenderTexture visibil = utils._ObjectUV_visibilityR8G8[i]?.texArray;
	            if(uvMask==null){ continue;}

	            Assert_TexturesSameSize(new List<RenderTexture>(){ prevBrushStroke_R8, currBrushStroke_R8,
	                                                               uvMask, visibil});
	            bool isPainted =  maskIx==povIx;
	            string maskName =  isPainted? "_PaintedMask" : "_NonPaintedMask"+alphabet[alphabetIx];
	            string visibilName = isPainted? "_PaintedMask_Visibil" : "_NonPaintedMask_Visibil"+alphabet[alphabetIx];

	            _brushStroke_intoMaskPovs.SetTexture(kernel, maskName, uvMask);
	            _brushStroke_intoMaskPovs.SetTexture(kernel, visibilName, visibil);
	            maskIx += 1;
	            alphabetIx += isPainted ? 0 : 1;
	        }
	    }


	    static bool SmudgeLayerAndAccumSameShape(RenderUdims layer, RenderUdims accum){
		    if (layer == null || accum == null || layer.texArray == null || accum.texArray == null) return false;
		    return layer.width == accum.width && layer.height == accum.height && layer.UdimsCount == accum.UdimsCount;
	    }

	    /// <summary>Call before <see cref="SchedulePreStrokeCapture"/> for smudge so undo is not captured when dispatch will no-op (missing chunks / RTs).</summary>
	    public bool SmudgeDispatchPreconditionsMet(RenderUdims destin, RenderTexture currBrushStroke_R8) {
		    if (destin == null || destin.texArray == null || currBrushStroke_R8 == null) return false;
		    var orm = Objects_Renderer_MGR.instance;
		    if (orm == null || orm.chunksTexture_ref()?.texArray == null) return false;
		    EnsureSmudgeCopies(destin.texArray);
		    return _smudgeCopyRT != null && _smudgeAccumCopyRT != null;
	    }

	    /// <summary>Applies smudge: reads layer + mesh accumulation (SD UV results / under-paint) combined per texel, blurs, writes active layer.
	    /// Call each frame during drag when smudge mode is active. Returns false if UV chunks or temps are unavailable (no GPU write).</summary>
	    static readonly int _SmudgeAngleRadId = Shader.PropertyToID("_SmudgeAngleRad");
	    static readonly int _SmudgeDirBoostId = Shader.PropertyToID("_SmudgeDirBoost");

	    /// <param name="smudgeAngleDeg">0° = +X in UV texel space; favors smearing along this direction in the neighbor kernel.</param>
	    /// <param name="smudgeDirBoost">0 = isotropic smudge; higher = stronger directional bias (typical 1–2).</param>
	    public bool Apply_smudge_to_ColorBrushTex( RenderTexture currBrushStroke_R8,
	                                                float smudgeStrength01, float brushSize01, RenderUdims destin,
	                                                RenderUdims meshAccumulationForSmudge = null,
	                                                float kernelSpacingMultiplier = 1f,
	                                                float smudgeAngleDeg = 0f,
	                                                float smudgeDirBoost = 1.35f ){
	        if (destin == null || destin.texArray == null || currBrushStroke_R8 == null) return false;

	        var orm = Objects_Renderer_MGR.instance;
	        RenderTexture chunksTex = orm != null ? orm.chunksTexture_ref()?.texArray : null;
	        if (chunksTex == null) return false;

	        EnsureSmudgeCopies(destin.texArray);
	        if (_smudgeCopyRT == null || _smudgeAccumCopyRT == null) return false;

	        Graphics.CopyTexture(destin.texArray, _smudgeCopyRT);
	        if (SmudgeLayerAndAccumSameShape(destin, meshAccumulationForSmudge))
		        Graphics.CopyTexture(meshAccumulationForSmudge.texArray, _smudgeAccumCopyRT);
	        else
		        TextureTools_SPZ.ClearRenderTexture(_smudgeAccumCopyRT, Color.clear, clearColor: true, clearDepth: false);

	        int kernel = _brushStroke_intoMask.FindKernel("CSSmudge");

	        TextureTools_SPZ.SetKeyword_ComputeShader(_brushStroke_intoMask, "BLEND_RGBA_ONCE", true);
	        TextureTools_SPZ.SetKeyword_ComputeShader(_brushStroke_intoMask, "SMUDGE_MODE", true);

	        _brushStroke_intoMask.SetTexture(kernel, "_CurrBrushStroke_R8", currBrushStroke_R8);
	        _brushStroke_intoMask.SetTexture(kernel, "_PaintedMask", destin.texArray);
	        _brushStroke_intoMask.SetTexture(kernel, "_SmudgeSourceCopy", _smudgeCopyRT);
	        _brushStroke_intoMask.SetTexture(kernel, "_SmudgeAccumCopy", _smudgeAccumCopyRT);
	        _brushStroke_intoMask.SetFloat("_MaxPossibleBrushStrength01", smudgeStrength01);
	        // >0: favor neighbors similar to brush-center (hover) color — edge-aware smudge. 0 = pure distance blur.
	        _brushStroke_intoMask.SetFloat("_SmudgeAdaptiveColorSigma", 10f);

	        float kernelSpacing = Mathf.Max(1f, brushSize01 * destin.width * 0.04f) * Mathf.Max(0.25f, kernelSpacingMultiplier);
	        _brushStroke_intoMask.SetFloat("_SmudgeKernelSpacing", kernelSpacing);
	        _brushStroke_intoMask.SetInt("_SmudgeTexWidth", destin.width);
	        _brushStroke_intoMask.SetInt("_SmudgeTexHeight", destin.height);
	        _brushStroke_intoMask.SetFloat(_SmudgeAngleRadId, smudgeAngleDeg * Mathf.Deg2Rad);
	        _brushStroke_intoMask.SetFloat(_SmudgeDirBoostId, Mathf.Max(0f, smudgeDirBoost));

	        _brushStroke_intoMask.SetTexture(kernel, "_UV_Chunks_R8", chunksTex);
	        Vector4 chunks_scale = new Vector4(chunksTex.width/(float)currBrushStroke_R8.width,
	                                            chunksTex.height/(float)currBrushStroke_R8.height, 0,0);
	        _brushStroke_intoMask.SetVector("_UV_Chunks_scale", chunks_scale);

	        Vector3Int grps = destin.CalcGroups_for_ComputeShader();
	        _brushStroke_intoMask.Dispatch(kernel, grps.x, grps.y, grps.z);

	        TextureTools_SPZ.SetKeyword_ComputeShader(_brushStroke_intoMask, "SMUDGE_MODE", false);
	        TextureTools_SPZ.SetKeyword_ComputeShader(_brushStroke_intoMask, "BLEND_RGBA_ONCE", false);
	        return true;
	    }

	    void EnsureSmudgeCopies(RenderTexture layerSource){
	        bool ok = _smudgeCopyRT != null && _smudgeAccumCopyRT != null
	                  && _smudgeCopyRT.width == layerSource.width
	                  && _smudgeCopyRT.height == layerSource.height
	                  && _smudgeCopyRT.volumeDepth == layerSource.volumeDepth;
	        if (ok) return;

	        if (_smudgeCopyRT != null){ _smudgeCopyRT.Release(); DestroyImmediate(_smudgeCopyRT); }
	        if (_smudgeAccumCopyRT != null){ _smudgeAccumCopyRT.Release(); DestroyImmediate(_smudgeAccumCopyRT); }
	        _smudgeCopyRT = new RenderTexture(layerSource.descriptor);
	        _smudgeCopyRT.enableRandomWrite = false;
	        _smudgeCopyRT.Create();
	        _smudgeAccumCopyRT = new RenderTexture(layerSource.descriptor);
	        _smudgeAccumCopyRT.enableRandomWrite = false;
	        _smudgeAccumCopyRT.Create();
	    }

	    void OnDestroy(){
	        if (_smudgeCopyRT != null){ _smudgeCopyRT.Release(); DestroyImmediate(_smudgeCopyRT); }
	        if (_smudgeAccumCopyRT != null){ _smudgeAccumCopyRT.Release(); DestroyImmediate(_smudgeAccumCopyRT); }
	    }


	    void Assert_TexturesSameSize(List<RenderTexture> rts){
	        #if !UNITY_EDITOR
	           return;
	        #endif
	        int width  = rts[0].width;
	        int height = rts[0].height;
	        bool correct = rts.All(t=> t.width==width && t.height==height);
	        Debug.Assert(correct, "Textures need to be of the same size in ApplyBrushStroke_ToUvMask");
	    }
    
	}
}//end namespace
