using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace spz {

	public static class TextureTools_SPZ{
    
	    public static Texture2D Base64ToTexture(string base64,  FilterMode filterMode=FilterMode.Bilinear){
	        Texture2D texture = new Texture2D(2, 2); // Create a new Texture (size does not matter, because LoadImage will replace with incoming image)
	        byte[] decodedBytes = Convert.FromBase64String(base64);
	        if (texture.LoadImage(decodedBytes)){
	            return texture;
	        }
	        return null;
	    }

	    public static string TextureToBase64( Texture2D texture){
	        if(texture == null){ return ""; }
	        byte[] imageBytes = texture.EncodeToPNG();
	        return Convert.ToBase64String(imageBytes);
	    }

	    /// <summary>CPU-readable solid fill (e.g. full-white img2img mask when painter capture failed).</summary>
	    public static Texture2D CreateSolidColorRGBA32(int width, int height, Color color){
	        if (width <= 0 || height <= 0) return null;
	        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
	        var c32 = (Color32)color;
	        var pixels = new Color32[width * height];
	        for (int i = 0; i < pixels.Length; i++)
	            pixels[i] = c32;
	        tex.SetPixels32(pixels);
	        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
	        return tex;
	    }

	    /// <summary>
	    /// Stretch-resize to exact WxH (CPU-readable RGBA32). Does not destroy <paramref name="src"/>.
	    /// When size already matches, returns <paramref name="src"/> (not a copy).
	    /// </summary>
	    public static Texture2D ResizeTexture2D_Exact_KeepSrc(Texture2D src, int width, int height){
	        if (src == null) return null;
	        if (width <= 0 || height <= 0) return null;
	        if (src.width == width && src.height == height) return src;
	        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
	        Graphics.Blit(src, rt);
	        var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
	        resized.filterMode = FilterMode.Bilinear;
	        var prev = RenderTexture.active;
	        RenderTexture.active = rt;
	        resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
	        resized.Apply(updateMipmaps: false, makeNoLongerReadable: false);
	        RenderTexture.active = prev;
	        RenderTexture.ReleaseTemporary(rt);
	        return resized;
	    }

	    /// <summary>
	    /// Stretch-resize to exact WxH (CPU-readable RGBA32). Destroys <paramref name="src"/> when a new texture is created.
	    /// Prefer <see cref="ResizeTexture2D_Exact_KeepSrc"/> for Neo encode copies so projection byproducts stay native.
	    /// </summary>
	    public static Texture2D ResizeTexture2D_Exact_DestroySrc(Texture2D src, int width, int height){
	        if (src == null) return null;
	        if (width <= 0 || height <= 0) return src;
	        if (src.width == width && src.height == height) return src;
	        var resized = ResizeTexture2D_Exact_KeepSrc(src, width, height);
	        UnityEngine.Object.Destroy(src);
	        return resized;
	    }

	    /// <summary>
	    /// ControlNet-style "Crop and Resize" / InnerFit: uniform scale to cover WxH, center-crop.
	    /// Does not destroy <paramref name="src"/>. Same size returns <paramref name="src"/>.
	    /// </summary>
	    public static Texture2D FitTexture2D_CropAndResize_KeepSrc(Texture2D src, int width, int height){
	        if (src == null) return null;
	        if (width <= 0 || height <= 0) return null;
	        if (src.width == width && src.height == height) return src;

	        float srcAspect = src.width / (float)src.height;
	        float dstAspect = width / (float)height;
	        float scaleX = 1f, scaleY = 1f, offX = 0f, offY = 0f;
	        if (srcAspect > dstAspect){
	            // Source wider than target — crop left/right.
	            scaleX = dstAspect / srcAspect;
	            offX = (1f - scaleX) * 0.5f;
	        } else if (srcAspect < dstAspect){
	            // Source taller — crop top/bottom.
	            scaleY = srcAspect / dstAspect;
	            offY = (1f - scaleY) * 0.5f;
	        }

	        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
	        Graphics.Blit(src, rt, new Vector2(scaleX, scaleY), new Vector2(offX, offY));
	        var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
	        resized.filterMode = FilterMode.Bilinear;
	        var prev = RenderTexture.active;
	        RenderTexture.active = rt;
	        resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
	        resized.Apply(updateMipmaps: false, makeNoLongerReadable: false);
	        RenderTexture.active = prev;
	        RenderTexture.ReleaseTemporary(rt);
	        return resized;
	    }

	    /// <summary>
	    /// Neo Flux/Klein requires init+mask same pixel size (uniform scale). Build encode refs without
	    /// mutating projection byproducts. Prefer screen-mask size (viewport frustum). Init is fitted with
	    /// Crop-and-Resize (not stretch) so CustomFile aspect is preserved. Caller must Destroy
	    /// <paramref name="disposeA"/> / <paramref name="disposeB"/> when non-null.
	    /// </summary>
	    public static void PrepareImg2ImgEncodePair(
	        Texture2D srcInit, Texture2D srcMask,
	        out Texture2D encodeInit, out Texture2D encodeMask,
	        out Texture2D disposeA, out Texture2D disposeB){
	        encodeInit = srcInit;
	        encodeMask = srcMask;
	        disposeA = null;
	        disposeB = null;
	        if (srcInit == null || srcMask == null) return;
	        if (srcInit.width == srcMask.width && srcInit.height == srcMask.height) return;

	        int tw = srcMask.width;
	        int th = srcMask.height;
	        if (tw <= 0 || th <= 0){
	            tw = srcInit.width;
	            th = srcInit.height;
	        }
	        if (srcInit.width != tw || srcInit.height != th){
	            disposeA = FitTexture2D_CropAndResize_KeepSrc(srcInit, tw, th);
	            encodeInit = disposeA;
	        }
	        if (srcMask.width != tw || srcMask.height != th){
	            disposeB = ResizeTexture2D_Exact_KeepSrc(srcMask, tw, th);
	            encodeMask = disposeB;
	        }
	    }


	    // forceAlpha1_ifSingleChannel: can be super important, for depth.
	    // StableDiffusion distorts things out when depth has alpha other than 1 (Aug 2024)
	    public static string TextureToBase64( RenderTexture renderTexture, bool forceAlpha1_ifSingleChannel = true){
	        if(renderTexture == null){ return ""; }
	        bool is_1_channel = GetChannelCount(renderTexture)==1;
	        Texture2D texture2D = is_1_channel? R_to_RGBA_Texture2D(renderTexture, forceAlpha1:forceAlpha1_ifSingleChannel, forceFullWhite:false)
	                                          : RenderTextureToTexture2D(renderTexture);
	        string base64 = TextureToBase64(texture2D);
	        Texture2D.DestroyImmediate( texture2D );
	        return base64;
	    }

	    public static Texture2D[] Create_SubImages( Texture2D combinedTexture, int batchSize ){
	        //Stable Diffusion puts several images into the same texture.
	        //Here we calculate how many rows and columns it will arrange them into.
	        int numRows, numCols;
	        switch (batchSize){
	            case 1: numRows = numCols = 1; break;
	            case 2: numRows = 1; numCols = 2; break;
	            case 3: case 4: numRows = 2; numCols = 2; break;
	            case 5: case 6: numRows = 2; numCols = 3; break;
	            case 7: case 8: numRows = 3; numCols = 3; break;
	            default: numRows = numCols = 1; break; // Default to a 1x1 grid for unexpected batch sizes
	        }
	        int singleImageWidth  = combinedTexture.width / numCols;
	        int singleImageHeight = combinedTexture.height/ numRows;
        
	        Texture2D[] subTextures = new Texture2D[batchSize];

	        for (int i=0; i<batchSize; i++){// Extract each sub-image
	            int row = i / numCols;
	            int col = i % numCols;
	            Texture2D newTex = new Texture2D(singleImageWidth, singleImageHeight);
	            newTex.SetPixels(combinedTexture.GetPixels( col*singleImageWidth,  (numRows-1-row)*singleImageHeight, 
	                                                        singleImageWidth,  singleImageHeight) );
	            newTex.Apply();
	            subTextures[i] = newTex;
	        }
	        return subTextures;
	    }


	    public static void Clone_Tex2D(Texture2D from, ref Texture2D dest_){
	        if(dest_!=null){ GameObject.DestroyImmediate(dest_); }
	        dest_ = null;
	        if(from==null){ return; }
	        dest_ = new Texture2D(from.width, from.height, from.format, mipChain:from.mipmapCount>1 );
	        Graphics.CopyTexture(from, dest_);
	    }

	    public static Texture2D Clone_Tex2D(Texture2D from){
	        if(from==null){ return null; }
	        var dest = new Texture2D(from.width, from.height, from.format, mipChain:from.mipmapCount>1 );
	        Graphics.CopyTexture(from, dest);
	        return dest;
	    }

	    public static RenderTexture Clone_RenderTex(RenderTexture from, bool copyContents=true){
	        if(from==null){ return null; }
	        var dest = new RenderTexture( from.descriptor );
	        if(copyContents){ Graphics.Blit(from, dest); }
	        return dest;
	    }


	    public static RenderTexture CreateTextureArray( Vector2Int widthHeight, GraphicsFormat format, 
	                                                    FilterMode filter, int numSlices, int depthBits=0 ){
	        var arr = new RenderTexture(widthHeight.x, widthHeight.y, depth:depthBits, format, mipCount:1);
	        arr.dimension = TextureDimension.Tex2DArray;
	        arr.volumeDepth = numSlices;
	        arr.useMipMap = false;
	        arr.enableRandomWrite = true;
	        arr.filterMode = filter;
	        arr.Create();
	        return arr;
	    }

    
	    // Deletes 'fromHere' and gives you 'result', which is thinner by 1 slice.
	    // Notice, if there was only one slice, 'fromHere' will be destroyed, and both textures will be null.
	    public static void TextureArray_RemoveSlice(ref RenderTexture fromHere, int sliceIx_toRemove){
	        Debug.Assert(fromHere.dimension == TextureDimension.Tex2DArray);
	        Debug.Assert(sliceIx_toRemove < fromHere.volumeDepth);
    
	        if(fromHere.volumeDepth == 1){ //last slice
	            Texture.DestroyImmediate(fromHere);
	            fromHere = null;
	            return;
	        }
	        var descriptor =  fromHere.descriptor;
	            descriptor.volumeDepth =  descriptor.volumeDepth-1;
	        RenderTexture newTexture = new RenderTexture(descriptor);

	        Material mat = StaticShaders_MGR.instance.TextureArrayRemoveSlice_mat;

	        RenderUdims.SetNumUdims(isUsingArray:true,  newTexture.volumeDepth,  mat);
	        mat.SetInteger("_SliceToSkip", sliceIx_toRemove);
	        mat.SetInteger("_NumSlicesOld", fromHere.volumeDepth);

	        Graphics.Blit(fromHere, newTexture, mat);
	        Texture.DestroyImmediate(fromHere);
	        fromHere = newTexture;
	    }
    

	    public static void TextureArray_Fill_N_Slices( RenderTexture texArr,  IReadOnlyList<Texture2D> texture2DList,  
	                                                   int arr_start_sliceIx ){
	       #if UNITY_EDITOR
	            Debug.Assert(texArr != null, "Texture array is null.");
	            Debug.Assert(texArr.dimension == TextureDimension.Tex2DArray, "Texture array dimension is not Tex2DArray.");
	            Debug.Assert(arr_start_sliceIx >= 0 && arr_start_sliceIx < texArr.volumeDepth, "Start slice index is out of range.");
	            Debug.Assert(arr_start_sliceIx + texture2DList.Count <= texArr.volumeDepth, "Not enough slices in the texture array.");
	            Debug.Assert(texture2DList != null && texture2DList.Count > 0, "Texture2D list is null or empty.");
	            //NOTICE: width and height of textures in list is allowed to differ from the width and height of the texture-array :)
	       #endif
	        var fillMat = StaticShaders_MGR.instance.TextureArrayFillSlices_mat;
	        int newSlices = texture2DList.Count;
	        int s = 0;
	        while(s < newSlices){
	            //assign the next group of textures to material (max 16, because the shader offers 16 uniforms)
	            int insert = Mathf.Min(16, texArr.volumeDepth-s-arr_start_sliceIx);
	            for(int i=0; i<insert; ++i){
	                fillMat.SetTexture($"_MainTex{i}", texture2DList[s+i]);
	            }
	            fillMat.SetInteger("_NumInsertSlices", insert);
	            fillMat.SetInteger("_InsertSlicesStart", s);
	            //and blit, to write them into slice of array-tex:
	            TextureTools_SPZ.Blit(null, texArr, fillMat);
	            s+=insert;
	        }
	    }

	    public static void ClearSpecificSlices(RenderTexture texArr, List<uint> slicesToClear, Color clearColor){
	      #if UNITY_EDITOR
	        string methodName = nameof(ClearSpecificSlices);
	        Debug.Assert(texArr.dimension == TextureDimension.Tex2DArray, $"{methodName}: Texture array dimension is not Tex2DArray.");
	        bool allSlicesValid =  slicesToClear.Any(s => s<0 || s>texArr.volumeDepth) == false;
	        Debug.Assert(allSlicesValid, $"{methodName}: some slice ixs are out of bounds");
	      #endif
	        ComputeShader sh = StaticShaders_MGR.instance.TexArray_ClearSlicesSparse_shader;
	        int kernel = sh.FindKernel("CSMain");

	        sh.SetTexture(kernel, "_TextureArray", texArr);
	        sh.SetVector("_ClearColor", clearColor);
	        // Create and set buffer for ixs of slices to be cleared:
	        ComputeBuffer slicesBuffer = new ComputeBuffer(slicesToClear.Count, sizeof(uint));
	            slicesBuffer.SetData(slicesToClear);

	            sh.SetBuffer(kernel, "_SlicesToClear", slicesBuffer);
	            sh.SetInt("_NumSlicesToClear", slicesToClear.Count);

	            Vector3Int grps = ComputeShaders_MGR.calcNumGroups(texArr);
	            sh.Dispatch(kernel, grps.x, grps.y, 1); //<--force z to be 1, will iterate slices inside the compute shader.
	        slicesBuffer.Release();
	    }


	    //Please ensure you will destroy the Texture2D once you no longer needed it.
	    public static Texture2D RenderTextureToTexture2D(RenderTexture renderTexture){
	        if (renderTexture == null){ return null; }
	        Debug.Assert(GetChannelCount(renderTexture) == 4, $"your texture should have 4 channels, else use {nameof(R_to_RGBA_Texture2D)}");
	        Debug.Assert(renderTexture.volumeDepth == 1, $"{nameof(RenderTextureToTexture2D)} doesn't work with texture arrays, " +
	                                                     $"use {nameof(TextureArray_to_Texture2DList)} instead");
	        Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, 
	                                            renderTexture.graphicsFormat, 0, TextureCreationFlags.None);
	        texture2D.anisoLevel = renderTexture.anisoLevel;
	        texture2D.filterMode = renderTexture.filterMode;
	        RenderTexture.active = renderTexture;
	        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
	        texture2D.Apply();
	        RenderTexture.active = null; // Reset the active RenderTexture
	        return texture2D;
	    }

    
	    //makes a Texture2D that has 4 channels. Alpha is kept as other channels, or as 1.
	    public static Texture2D R_to_RGBA_Texture2D(RenderTexture texR8, bool forceAlpha1, bool forceFullWhite){
	        Debug.Assert(GetChannelCount(texR8)==1, "your texture should probably have single R channel");
	        var desc = texR8.descriptor;
	        desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;// destination has four channels.
	        RenderTexture tempRT = RenderTexture.GetTemporary(desc);
	        Material mat = StaticShaders_MGR.instance.R_to_RGBA_mat;
	        mat.SetFloat("_ForceAlpha1", forceAlpha1?1:0);
	        mat.SetFloat("_ForceFullWhite", forceFullWhite?1:0);
	        Graphics.Blit(texR8, tempRT, mat);
	        Texture2D tex = TextureTools_SPZ.RenderTextureToTexture2D(tempRT);//black and white representation of depth.
	        RenderTexture.ReleaseTemporary(tempRT);
	        return tex;
	    }


	    public enum TexArr_toTex2dList_arg { Usual, RRR1 }

	    //NOTICE: don't forget to destroy the returned Texture2D entries when you no longer need them.
	    public static List<Texture2D> TextureArray_to_Texture2DList( RenderTexture textureArray, 
	                                                                 TexArr_toTex2dList_arg arg = TexArr_toTex2dList_arg.Usual )
	    {
	        if (textureArray == null || textureArray.dimension != TextureDimension.Tex2DArray){
	            return null;
	        }
	        GraphicsFormat dest_format = GraphicsFormat.R8G8B8A8_UNorm;
	        int slices = textureArray.volumeDepth;
	        List<Texture2D> texture2DList = new List<Texture2D>(slices);

	        var wh =  new Vector2Int(textureArray.width, textureArray.height);
	        // prevent resolutions lower than 2048:  users might want pixelated look (Point-Filter).
	        // It will be fine even at 64 res, but Windows will auto-preview such small textures as blurry.
	        // (even though programs just need to use point filter on the png).
	        // Many users won't anticipate/know this, and would complain about the blurry preview. So, keep texture at least 2048 to 
	        if(SceneResolution_MGR.resultTexFilterMode == FilterMode.Point){ 
	            wh =  wh.x < 2048?  new Vector2Int(2048, 2048) : wh;
	        }

	        RenderTexture tempRT = RenderTexture.GetTemporary(wh.x, wh.y, 0, dest_format);
	           tempRT.filterMode = SceneResolution_MGR.resultTexFilterMode;

	        RenderTexture.active = tempRT;

	        // Same hazard as the budgeted variant below: a throw inside the readback loop (allocation
	        // failure at 4K, bad format) would leave RenderTexture.active pointing at a temp RT that is
	        // never returned to the pool, so later renders and readbacks land on the wrong target and
	        // export black or garbled textures.
	        try {
	            Material mat = StaticShaders_MGR.instance.TextureArrayReadSlice_mat;
	            mat.SetTexture("_MainTex", textureArray);

	            RenderUdims.SetNumUdims(true, textureArray.volumeDepth, mat);
	            TextureTools_SPZ.SetKeyword_Material(mat, "RRR1", arg==TexArr_toTex2dList_arg.RRR1);
	            TextureTools_SPZ.SetKeyword_Material(mat, "SAMPLER_POINT", SceneResolution_MGR.resultTexFilterMode==FilterMode.Point);

	            for (int i=0; i<slices; i++){
	                mat.SetInteger("_SliceIx", i);
	                Graphics.Blit(null, tempRT, mat);
	                Texture2D texture2D = new Texture2D( wh.x,  wh.y,  dest_format, textureArray.mipmapCount, TextureCreationFlags.None);
	                texture2D.filterMode = SceneResolution_MGR.resultTexFilterMode;

	                texture2D.ReadPixels(new Rect(0, 0, wh.x, wh.y), 0, 0);

	                texture2D.Apply();
	                texture2DList.Add(texture2D);
	            }
	        } finally {
	            RenderTexture.active = null;
	            RenderTexture.ReleaseTemporary(tempRT);
	        }
	        return texture2DList;
	    }


	    /// <summary>
	    /// Same output as <see cref="TextureArray_to_Texture2DList"/> (identical blit/keywords/min-2048 upscale) but the
	    /// per-slice GPU readback stall is spread across frames under an <see cref="ExportFrameScheduler"/> budget, so a
	    /// many-UDIM export no longer freezes the app on a single frame. Slices are appended to <paramref name="outList"/>.
	    /// Drive it with <c>StartCoroutine</c>. <paramref name="onProgress01"/> reports 0..1 completion of this stage.
	    /// </summary>
	    public static IEnumerator TextureArray_to_Texture2DList_Budgeted(
	        RenderTexture textureArray, List<Texture2D> outList, ExportFrameScheduler sched,
	        Action<float> onProgress01 = null, TexArr_toTex2dList_arg arg = TexArr_toTex2dList_arg.Usual)
	    {
	        if (outList == null){ yield break; }
	        if (textureArray == null || textureArray.dimension != TextureDimension.Tex2DArray){
	            onProgress01?.Invoke(1f);
	            yield break;
	        }
	        // Resolve the blit material BEFORE allocating the temp RT: dereferencing a missing
	        // StaticShaders_MGR after the allocation would leak a full-size render target.
	        Material mat = StaticShaders_MGR.instance != null
	            ? StaticShaders_MGR.instance.TextureArrayReadSlice_mat : null;
	        if (mat == null){
	            Debug.LogWarning("[TextureTools_SPZ] Budgeted readback: TextureArrayReadSlice_mat unavailable.");
	            onProgress01?.Invoke(1f);
	            yield break;
	        }

	        GraphicsFormat dest_format = GraphicsFormat.R8G8B8A8_UNorm;
	        int slices = textureArray.volumeDepth;

	        var wh = new Vector2Int(textureArray.width, textureArray.height);
	        if(SceneResolution_MGR.resultTexFilterMode == FilterMode.Point){
	            wh = wh.x < 2048 ? new Vector2Int(2048, 2048) : wh;
	        }

	        RenderTexture tempRT = RenderTexture.GetTemporary(wh.x, wh.y, 0, dest_format);
	        tempRT.filterMode = SceneResolution_MGR.resultTexFilterMode;
	        RenderTexture.active = tempRT;

	        sched?.BeginSession(wh.x, wh.y, slices);

	        // Unlike the single-frame variant this spans many frames, so it can be stopped midway
	        // (owner disabled, project load, throw during readback). Without try/finally the temp RT
	        // — up to 64MB at 4K — would never return to the pool and RenderTexture.active would stay
	        // pointing at it, once per abandoned export.
	        try {
	            int i = 0;
	            while (i < slices){
	                // The source belongs to the live accumulation, which a paint stroke or a finished
	                // generation can rebuild while this coroutine is yielded. Reading a destroyed array
	                // would write garbage slices into a file the user thinks is their texture.
	                if (textureArray == null){
	                    Debug.LogWarning("[TextureTools_SPZ] Budgeted readback: source array went away mid-export; stopping at slice " + i + ".");
	                    yield break;
	                }
	                if (sched != null){
	                    sched.BeginTick(Time.deltaTime);
	                    sched.GetFrameBudget(slices - i, out float budgetMs, out int maxUnits);
	                    float start = Time.realtimeSinceStartup;
	                    int did = 0;
	                    while (did < maxUnits && i < slices){
	                        if (did > 0 && (Time.realtimeSinceStartup - start) * 1000f >= budgetMs) break;
	                        ReadBackSlice(i);
	                        ++i; ++did;
	                    }
	                    float hitchMs = Mathf.Max(0f, (Time.deltaTime - (1f / 60f)) * 1000f);
	                    sched.RegisterObservation(hitchMs, did);
	                } else {
	                    ReadBackSlice(i);
	                    ++i;
	                }
	                onProgress01?.Invoke(slices <= 0 ? 1f : i / (float)slices);
	                yield return null;
	            }
	        } finally {
	            RenderTexture.active = null;
	            RenderTexture.ReleaseTemporary(tempRT);
	        }
	        onProgress01?.Invoke(1f);

	        void ReadBackSlice(int sliceIx){
	            // Re-bind every slice instead of once before the loop. This material is shared global
	            // state and the loop now spans frames by design, so anything else that reads a texture
	            // array in between — a finished generation, an icon merge, the single-frame variant of
	            // this call — rebinds _MainTex and the keywords, and every remaining slice would be
	            // sampled from that other array. A few SetX calls are nothing beside a full-size blit.
	            mat.SetTexture("_MainTex", textureArray);
	            RenderUdims.SetNumUdims(true, textureArray.volumeDepth, mat);
	            SetKeyword_Material(mat, "RRR1", arg==TexArr_toTex2dList_arg.RRR1);
	            SetKeyword_Material(mat, "SAMPLER_POINT", SceneResolution_MGR.resultTexFilterMode==FilterMode.Point);
	            mat.SetInteger("_SliceIx", sliceIx);
	            Graphics.Blit(null, tempRT, mat);
	            Texture2D texture2D = new Texture2D(wh.x, wh.y, dest_format, textureArray.mipmapCount, TextureCreationFlags.None);
	            texture2D.filterMode = SceneResolution_MGR.resultTexFilterMode;
	            texture2D.ReadPixels(new Rect(0, 0, wh.x, wh.y), 0, 0);
	            texture2D.Apply();
	            outList.Add(texture2D);
	        }
	    }


	    public static void Texture2DList_to_TextureArray( ref RenderTexture texArr_refillOrRecreateMe, 
	                                                      IReadOnlyList<Texture2D> texture2DList,  int depth_numBits = 0, 
	                                                      GraphicsFormat format = GraphicsFormat.R8G8B8A8_UNorm){
	        if (texture2DList == null || texture2DList.Count == 0){
	            if(texArr_refillOrRecreateMe != null){ Texture.DestroyImmediate( texArr_refillOrRecreateMe); }
	            texArr_refillOrRecreateMe = null;
	            return;
	        }
	        int oldSlices = texArr_refillOrRecreateMe?.volumeDepth?? -9999;
	        int newSlices = texture2DList.Count;
	        var filter    = texArr_refillOrRecreateMe?.filterMode ?? FilterMode.Bilinear;
	        var wh = new Vector2Int(texture2DList[0].width, texture2DList[0].height);

	        // see if we need to destroy arrayTextuer and create another one, or can reuse already existing one:
	        bool recreate =   texArr_refillOrRecreateMe==null  ||  oldSlices!=newSlices 
	                        || wh.x != texArr_refillOrRecreateMe.width 
	                        || wh.y != texArr_refillOrRecreateMe.height;
	        if(recreate){
	            if(texArr_refillOrRecreateMe != null){ Texture.DestroyImmediate( texArr_refillOrRecreateMe);}
	            texArr_refillOrRecreateMe = CreateTextureArray( wh, format, filter, newSlices, depth_numBits);
	        }
	        TextureArray_Fill_N_Slices(texArr_refillOrRecreateMe, texture2DList, arr_start_sliceIx:0);
	    }


	    public static void TextureArray_EnsureSufficient( ref RenderTexture texArr_refillOrRecreateMe, 
	                                                      Vector2Int size, int numSlices_atLeast,  int depth_numBits = 0, 
	                                                      GraphicsFormat format = GraphicsFormat.R8G8B8A8_UNorm){
	        int oldSlices = texArr_refillOrRecreateMe?.volumeDepth?? -9999;
	        var filter    = texArr_refillOrRecreateMe?.filterMode ?? FilterMode.Bilinear;
	        var wh = new Vector2Int(size.x, size.y);

	        // see if we need to destroy arrayTextuer and create another one, or can reuse already existing one:
	        bool recreate =   texArr_refillOrRecreateMe==null  ||  oldSlices<numSlices_atLeast 
	                        || wh.x != texArr_refillOrRecreateMe.width 
	                        || wh.y != texArr_refillOrRecreateMe.height;
	        if(recreate){
	            if(texArr_refillOrRecreateMe != null){ Texture.DestroyImmediate( texArr_refillOrRecreateMe);}
	            texArr_refillOrRecreateMe = CreateTextureArray(wh, format, filter, numSlices_atLeast, depth_numBits);
	        }
	    }



	    //RenderTextureToTexture2D() can cause massive fps spike, so this function is alternative, helps to remove fps lag.
	    public static void RenderTexture_to_Texture2D_Async(RenderTexture renderTexture, Action<Texture2D> callback){
	        if (renderTexture == null){
	            callback(null);
	            return;
	        }
	        Debug.Assert(renderTexture.volumeDepth == 1, $"{nameof(RenderTexture_to_Texture2D_Async)} doesn't work with texture arrays, " +
	                                                     $"use {nameof(TextureArray_to_Texture2DList)} instead");
	        // Request an asynchronous readback from the GPU
	        AsyncGPUReadback.Request(renderTexture, 0, TextureFormat.RGBA32, (request) =>{
	            if (request.hasError){
	                Debug.LogError("GPU readback error detected.");
	                callback(null);
	                return;
	            }
	            // Create a new Texture2D and fill it with the readback data
	            Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
	            texture2D.filterMode = renderTexture.filterMode;
	            texture2D.LoadRawTextureData(request.GetData<uint>()); // Load data into the texture
	            texture2D.Apply();
	            callback(texture2D);// Invoke the callback with the created Texture2D
	        });
	    }


	    //RenderTextureToTexture2D() can cause massive fps spike, so this function is alternative, helps to remove fps lag.
	    public static void RenderTexture_to_Texture2DList_Async(RenderUdims udims, Action<List<Texture2D>> callback){
	        if (udims == null || udims.texArray == null){
	            callback(new List<Texture2D>());
	            return;
	        }
	        int udimsCount = udims.UdimsCount;
	        List<Texture2D> textures = new List<Texture2D>(udimsCount);
	        for (int i=0; i<udimsCount; ++i){ textures.Add(null); }

	        int responsesDone = 0;
	        for (int i=0; i<udimsCount; ++i){
	            int sliceIx = i;
	            AsyncGPUReadback.Request( udims.texArray, 0, 0, udims.width, 0, udims.height, sliceIx, 1, 
	                                      udims.texArray.graphicsFormat,  (request)=>{Request(request, sliceIx);} );
	        }

	        void Request(AsyncGPUReadbackRequest request, int sliceIx){
	            if (request.hasError){
	                Debug.LogError($"GPU readback error detected for slice {sliceIx}.");
	                textures[sliceIx] = null;
	            }else{
	                Texture2D texture2D =  new Texture2D(udims.width, udims.height, udims.texArray.graphicsFormat,  
	                                                        udims.texArray.mipmapCount, TextureCreationFlags.None );
	                texture2D.filterMode = udims.texArray.filterMode;
	                texture2D.LoadRawTextureData(request.GetData<byte>());
	                texture2D.Apply();
	                textures[sliceIx] = texture2D;
	            }
	            responsesDone++;
	            if (responsesDone >= udimsCount)
	                callback(textures);
	        }
	    }

	    /// <summary>Like <see cref="RenderTexture_to_Texture2DList_Async"/> but limits concurrent slice readbacks (sliding window). Use for heavy UDIM stacks to reduce driver/GPU spikes; <paramref name="maxInflight"/> ≤ 0 or ≥ slice count uses the parallel implementation.</summary>
	    public static void RenderTexture_to_Texture2DList_Async_Staggered(RenderUdims udims, int maxInflight, Action<List<Texture2D>> callback){
	        if (udims == null || udims.texArray == null){
	            callback(new List<Texture2D>());
	            return;
	        }
	        int n = udims.UdimsCount;
	        if (n <= 0){
	            callback(new List<Texture2D>());
	            return;
	        }
	        if (maxInflight <= 0 || maxInflight >= n){
	            RenderTexture_to_Texture2DList_Async(udims, callback);
	            return;
	        }

	        var textures = new List<Texture2D>(n);
	        for (int i = 0; i < n; ++i) textures.Add(null);

	        int responsesDone = 0;
	        int nextToSubmit = 0;
	        int inFlight = 0;

	        void SubmitNext(){
	            while (inFlight < maxInflight && nextToSubmit < n){
	                int sliceIx = nextToSubmit++;
	                inFlight++;
	                AsyncGPUReadback.Request(udims.texArray, 0, 0, udims.width, 0, udims.height, sliceIx, 1,
	                    udims.texArray.graphicsFormat, (request) => OnSliceRequest(request, sliceIx));
	            }
	        }

	        void OnSliceRequest(AsyncGPUReadbackRequest request, int sliceIx){
	            if (request.hasError){
	                Debug.LogError($"GPU readback error detected for slice {sliceIx}.");
	                textures[sliceIx] = null;
	            }else{
	                Texture2D texture2D = new Texture2D(udims.width, udims.height, udims.texArray.graphicsFormat,
	                    udims.texArray.mipmapCount, TextureCreationFlags.None);
	                texture2D.filterMode = udims.texArray.filterMode;
	                texture2D.LoadRawTextureData(request.GetData<byte>());
	                texture2D.Apply();
	                textures[sliceIx] = texture2D;
	            }
	            inFlight--;
	            responsesDone++;
	            if (responsesDone >= n)
	                callback(textures);
	            else
	                SubmitNext();
	        }

	        SubmitNext();
	    }

    
	    public static void ClearRenderTexture(RenderTexture rt, Color col, bool clearColor=true, bool clearDepth=true){
	        if(rt == null){ return; }
	        RenderTexture currentActiveRT = RenderTexture.active;
	        // Using Graphics.SetRenderTarget() instead of 'RenderTexture.active'
	        // This way we can clear Texture Arrays.  1 to set the whole texture array as a render target.
	        // https://docs.unity3d.com/2020.1/Documentation/Manual/class-Texture2DArray.html
	        Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, depthSlice:-1);
	        GL.Clear(clearDepth:clearDepth, clearColor:clearColor, col);
	        RenderTexture.active = currentActiveRT;
	    }

   


	    //isTemporary: did we originally obtain rt from RenderTexture.GetTemporary(). 
	    //False if we created it manually, via  '= new RenderTexture()'
	    public static void Dispose_RT(ref RenderTexture rt, bool isTemporary){
	        if(rt == null){ return; }
	        if(RenderTexture.active == rt){ RenderTexture.active = null; }

	        if(isTemporary){ RenderTexture.ReleaseTemporary(rt); rt = null; return; }
	        rt.Release();
	        GameObject.DestroyImmediate(rt);
	        rt = null;
	    }

	    public static int GetChannelCount(Texture tex){
	        GraphicsFormat format = tex.graphicsFormat;
	        string formatString = format.ToString();
	        int channelCount = Regex.Matches(formatString, @"[RGBA]").Count;
	        return channelCount;
	    }


	    public static void SetKeywordGlobal_Shader( string keyword, bool isEnable){
	        if(isEnable){ Shader.EnableKeyword(keyword);  }
	        else{ Shader.DisableKeyword(keyword); }
	    }

	    public static void SetKeyword_ComputeShader(ComputeShader sh, string keyword, bool isEnable){
	        if(isEnable){ sh.EnableKeyword(keyword);  }
	        else{ sh.DisableKeyword(keyword); }
	    }

	    public static void SetKeyword_Material( Material mat, string keyword, bool isEnable){
	        if(isEnable){ mat.EnableKeyword(keyword);  }
	        else{ mat.DisableKeyword(keyword); }
	    }


	    // Allows to blit between 2D and array2D textures (those that have slices kinda like a stack).
	    // To use Graphics.Blit() we need shader to have Properties block, with _MainTex defined as '2D'.
	    //
	    // A) Without Properties block Blit+mat won't work and the "from" texture won't know where to plug.
	    // B) With properties block we must decide between 2D and 2DArray inside the shader.
	    //
	    // So, to make this easier, we don't use Properties block in shader,
	    // and instead manually set _SrcTex in here.
	    public static void Blit(Texture src, RenderTexture dest, Material mat=null){
	        if(mat == null){ 
	            Graphics.Blit(src,dest);
	            return;
	        }
	        bool noSource = src==null;
	        bool isArray  = dest.dimension == TextureDimension.Tex2DArray;
        
	        if(noSource && isArray){
	            var descriptor = dest.descriptor;
	            descriptor.width = 2;
	            descriptor.height= 2;
	            src = RenderTexture.GetTemporary(descriptor);
	        }
	        mat.SetTexture("_SrcTex", src);
	        Graphics.Blit(src, dest, mat);
	        //NOTICE: For texture arrays, do (src,dest,mat).
	        //        For texture arrays DON'T DO (null,dest,mat)
	        //        otherwise arrays will ignore all slices except 0.
	        if(noSource && isArray){  RenderTexture.ReleaseTemporary(src as RenderTexture); }
	    }


	    public static void EncodeAndSaveTexture(Texture2D texture, string filePath){
	        if(texture == null || string.IsNullOrEmpty(filePath)){ return; }

	        byte[] bytes;
	        string exten = Path.GetExtension(filePath).ToLower();

	        if (exten == ".png"){
	            bytes = texture.EncodeToPNG();
	        }
	        else if (exten == ".jpg"){
	            bytes = texture.EncodeToJPG();
	        }
	        else if (exten == ".tga"){
	            bytes = texture.EncodeToTGA();
	        }
	        else if (exten == ""){//windows version of File browser has bug where it applies to extension.
	            bytes = texture.EncodeToPNG();//in that case, we'll default to png, to support transparency.
	            filePath += ".png";         //Otherwise, user need to explicitly mention extension if filename.
	        }
	        else{
	            // Headless/RPC export has no viewport status bar; an unguarded singleton here turned an
	            // "unsupported format" message into an exception that aborted the whole export coroutine.
	            Viewport_StatusText.instance?.ShowStatusText("Saving texture failed - Unsupported file format",
	                                                          false, 5, progressVisibility:false);
	            Debug.LogWarning("[TextureTools_SPZ] Unsupported texture format for: " + filePath);
	            return;
	        }
	        if(bytes != null){ File.WriteAllBytes(filePath, bytes); }
	    }


	    /// <summary>
	    /// Raw-pixel snapshot of a texture plus its destination, so the expensive PNG/JPG encode and
	    /// <see cref="File.WriteAllBytes"/> can run on a worker thread instead of freezing the app during export.
	    /// Build it on the main thread via <see cref="CaptureEncodeJob"/>, run it anywhere via <see cref="RunEncodeJobToDisk"/>.
	    /// </summary>
	    public class TextureEncodeJob{
	        public byte[] rawPixels;
	        public GraphicsFormat format;
	        public int width;
	        public int height;
	        public string filePath;
	        public string extension;
	    }

	    /// <summary>Main-thread: copy the texture's raw pixels + metadata so it can be encoded off-thread. Returns null if unusable.</summary>
	    public static TextureEncodeJob CaptureEncodeJob(Texture2D texture, string filePath){
	        if(texture == null || string.IsNullOrEmpty(filePath)){ return null; }
	        string exten = Path.GetExtension(filePath).ToLower();
	        if(exten == ""){ // Windows FileBrowser can drop the extension; default to png (keeps transparency).
	            filePath += ".png";
	            exten = ".png";
	        }
	        if(exten != ".png" && exten != ".jpg" && exten != ".tga"){
	            return null; // Unsupported off-thread format; caller should fall back to the sync encoder.
	        }
	        byte[] raw;
	        try {
	            raw = texture.GetRawTextureData<byte>().ToArray();
	        } catch (Exception e) {
	            Debug.LogWarning("[TextureTools_SPZ] CaptureEncodeJob: GetRawTextureData failed, caller should fall back: " + e.Message);
	            return null;
	        }
	        if(raw == null || raw.Length == 0){ return null; }
	        return new TextureEncodeJob{
	            rawPixels = raw,
	            format = texture.graphicsFormat,
	            width = texture.width,
	            height = texture.height,
	            filePath = filePath,
	            extension = exten,
	        };
	    }

	    /// <summary>Thread-safe: encode the captured raw pixels and write to disk. Safe to call from a background thread.</summary>
	    public static void RunEncodeJobToDisk(TextureEncodeJob job){
	        if(job == null || job.rawPixels == null || string.IsNullOrEmpty(job.filePath)){ return; }
	        byte[] bytes;
	        if(job.extension == ".jpg"){
	            bytes = ImageConversion.EncodeArrayToJPG(job.rawPixels, job.format, (uint)job.width, (uint)job.height);
	        }
	        else if(job.extension == ".tga"){
	            bytes = ImageConversion.EncodeArrayToTGA(job.rawPixels, job.format, (uint)job.width, (uint)job.height);
	        }
	        else {
	            bytes = ImageConversion.EncodeArrayToPNG(job.rawPixels, job.format, (uint)job.width, (uint)job.height);
	        }
	        if(bytes != null){ File.WriteAllBytes(job.filePath, bytes); }
	    }


	    public static Texture2D[] LoadTextures_FromDir(string directoryPath, bool createDir_iIf_missing = true){
	        if (!Directory.Exists(directoryPath)){
	            if (createDir_iIf_missing){
	                try{
	                    Directory.CreateDirectory(directoryPath);
	                    Debug.Log($"Created directory: {directoryPath}");
	                }catch (Exception e){
	                    Debug.LogError($"Error creating directory {directoryPath}: {e.Message}");
	                    return new Texture2D[0];
	                }
	            }else{
	                Debug.LogError($"Directory does not exist: {directoryPath}");
	                return new Texture2D[0];
	            }
	        }

	        try{
	            return GetTexs();
	        }
	        catch (Exception e){
	            Debug.LogError($"Error accessing directory {directoryPath}: {e.Message}");
	            return new Texture2D[0];
	        }

	        Texture2D[] GetTexs(){
	            string[] supportedExtensions = new[] { "*.png", "*.jpg", "*.jpeg", "*.tga" };
	            var files = supportedExtensions
	            .SelectMany(ext => Directory.GetFiles(directoryPath, ext))
	            .OrderBy(f => f); //sort files by name

	            return files.Select(filePath => {
	                try{
	                    byte[] fileData = File.ReadAllBytes(filePath);
	                    Texture2D texture = new Texture2D(2, 2);
	                    if (texture.LoadImage(fileData)){
	                        texture.name = Path.GetFileNameWithoutExtension(filePath);
	                        return texture;
	                    }else{
	                        Debug.LogError($"Failed to load image: {filePath}");
	                        UnityEngine.Object.DestroyImmediate(texture);
	                        return null;
	                    }
	                }
	                catch (Exception e){
	                    Debug.LogError($"Error loading file {filePath}: {e.Message}");
	                    return null;
	                }
	            }).Where(texture => texture != null).ToArray();
	        }
	    }//end LoadTexturesFromDirectory()


	    public static List<Texture2D> LoadTextures_FromFiles(List<string> filepaths_with_exten){
	        if (filepaths_with_exten == null || filepaths_with_exten.Count == 0){
	            return new List<Texture2D>();
	        }

	        var texList = new List<Texture2D>();
 
	        foreach (string filepath in filepaths_with_exten){
	            if (string.IsNullOrEmpty(filepath)){ continue; }
     
	            string extension = Path.GetExtension(filepath).ToLower();
	            if (!new[] { ".png", ".jpg", ".tga" }.Contains(extension)){
	                Debug.LogError($"Unsupported format: {extension} for {filepath}");
	                continue;
	            }

	            try{
	                var tex2D = new Texture2D(2, 2);
	                if (tex2D.LoadImage(File.ReadAllBytes(filepath))){
	                    tex2D.name = Path.GetFileNameWithoutExtension(filepath);
	                    texList.Add(tex2D);
	                } else {
	                    Debug.LogError($"Failed to load image: {filepath}");
	                    GameObject.DestroyImmediate(tex2D);
	                }
	            }
	            catch (Exception e){
	                Debug.LogError($"Error loading {filepath}: {e.Message}");
	            }
	        }
	        return texList;
	    }//end()
	}
}//end namespace
