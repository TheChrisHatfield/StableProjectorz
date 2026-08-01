using System.Collections.Generic;
using UnityEngine;


namespace spz {

	public class SD_Generate_PayloadMaker : MonoBehaviour{
    
	    public void Create_txt2img_payload( out SD_txt2img_payload payload_,
	                                        out SD_GenRequestArgs_byproducts intermediates_){
	        var input = SD_InputPanel_UI.instance;
	        string samplerName = input.samplers.value?.name??"";
	        string scheduler = input.scheduler.value?.name??"";

	        intermediates_ = new SD_GenRequestArgs_byproducts();

	        string positivePrompt = StableDiffusion_Prompts_UI.instance.positivePrompt;
	        string negativePrompt = StableDiffusion_Prompts_UI.instance.negativePrompt;
	        PostProcess_Prompt(ref positivePrompt, ref negativePrompt);

	        payload_ = new SD_txt2img_payload{
	            prompt = positivePrompt,
	            negative_prompt = negativePrompt,
	            sampler_name = samplerName,
	            scheduler = scheduler,
	            batch_size = Mathf.RoundToInt(input.batch_size),
	            n_iter = Mathf.RoundToInt(input.batch_count),
	            steps = Mathf.RoundToInt(input.sampleSteps_slider.value),
	            cfg_scale = input.CFG_scale_slider.value,
	            width = Mathf.RoundToInt(input.width),
	            height = Mathf.RoundToInt(input.height),
	            seed = input.seed_intField.recentVal > 0 ? input.seed_intField.recentVal : UnityEngine.Random.Range(0, int.MaxValue),//manual (not -1), so we can show it in our icon instead of -1.

	            //webui also wants tiling to be enabled via SD_Options, that are sent separately.
	            //This has to be sent too though (else tiling might remain enabled at current webui version, May 2024)
	            tiling = SD_WorkflowOptionsRibbon_UI.instance.isTileable,

	            // Nov 2024: Turned off, - user will manually press x2 and x4 to upscale images using the img2extra url.
	            //   refiner_checkpoint = SD_Refiner.instance.selectedModel_name,
	            //   refiner_switch_at = SD_Refiner.instance.switchAt01,
	            //   enable_hr = SD_Upscalers.instance.selectedUpscaler_name != "",
	            //   hr_upscaler = SD_Upscalers.instance.selectedUpscaler_name,
	            //   hr_sampler_name = samplerName, //same as the base-model sampler. https://github.com/AUTOMATIC1111/stable-diffusion-webui/issues/8587#issuecomment-1468865769
	            //   hr_scale = SD_Upscalers.instance.upscaleBy,
	            //   hr_second_pass_steps = SD_Upscalers.instance.highresSteps,
	            //   denoising_strength = SD_Upscalers.instance.denoiseStrength,

	            alwayson_scripts = new Dictionary<string,AlwaysOn_Value>(),
	        };
       
	        ControlNet_NetworkArgs ctrlNets_args = SD_ControlNetsList_UI.instance.GetArgs_forGenerationRequest(intermediates_);
	        if (ctrlNets_args.args.Length > 0) {
	            payload_.alwayson_scripts.Add("controlnet", ctrlNets_args);//https://github.com/Mikubill/sd-webui-controlnet/wiki/API#examples-1
	        }
	    }


    
	    /// <summary>Same payload path for traditional (single buffer) and layer system: prompt from UI, init = what's visible (composite), mask from same, denoising = redo slider. SD receives all of this and runs the full diffusion so the model generalizes from the prompt and init, not just copying the init.</summary>
	    public void Create_img2img_payload( bool isMakingBackgrounds,  out SD_img2img_payload payload_, 
	                                        out SD_GenRequestArgs_byproducts intermediates_ ){
	        Texture2D screenMask_skipAntiEdge;
	        Texture2D screenMask_withAntiEdge;
	        Texture2D viewTex;
	        InpaintingFill inpaint_fill;
	        float denoise_strength;
	        img2img_GetTextures_andFill( forceFullWhiteMask:isMakingBackgrounds,  out screenMask_skipAntiEdge, out screenMask_withAntiEdge,
	                                     out viewTex, out inpaint_fill, out denoise_strength);

	        int inpaintMaskInvert01 = 0;
	        // Background / full-white mask paths keep WebUI default (inpaint inside mask). User toggle applies to main viewport img2img only.
	        if (!isMakingBackgrounds && Settings_MGR.instance != null && Settings_MGR.instance.get_sd_inpaintingMaskInvert())
		        inpaintMaskInvert01 = 1;

	        var input = SD_InputPanel_UI.instance;
	        if (input == null){
	            intermediates_ = new SD_GenRequestArgs_byproducts{
	                screenSpaceMask_NE_disposableTex = screenMask_skipAntiEdge,
	                screenSpaceMask_WE_disposableTex = screenMask_withAntiEdge,
	                usualView_disposableTexture = viewTex,
	            };
	            payload_ = new SD_img2img_payload{
	                width = 0, height = 0,
	                init_images = new string[]{ "" },
	                mask = "",
	                alwayson_scripts = new Dictionary<string,AlwaysOn_Value>(),
	            };
	            if (Viewport_StatusText.instance != null)
	                Viewport_StatusText.instance.ShowStatusText(
	                    "img2img aborted: SD input panel not ready.", false, 5f, false);
	            return;
	        }

	        int outW = Mathf.RoundToInt(input.width);
	        int outH = Mathf.RoundToInt(input.height);
	        if (outW <= 0 || outH <= 0){
	            if (viewTex != null){ UnityEngine.Object.Destroy(viewTex); viewTex = null; }
	            if (screenMask_skipAntiEdge != null){
	                UnityEngine.Object.Destroy(screenMask_skipAntiEdge);
	                screenMask_skipAntiEdge = null;
	            }
	            if (screenMask_withAntiEdge != null){
	                UnityEngine.Object.Destroy(screenMask_withAntiEdge);
	                screenMask_withAntiEdge = null;
	            }
	            intermediates_ = new SD_GenRequestArgs_byproducts{
	                screenSpaceMask_NE_disposableTex = null,
	                screenSpaceMask_WE_disposableTex = null,
	                usualView_disposableTexture = null,
	            };
	            payload_ = new SD_img2img_payload{
	                width = 0, height = 0,
	                init_images = new string[]{ "" },
	                mask = "",
	                alwayson_scripts = new Dictionary<string,AlwaysOn_Value>(),
	            };
	            if (Viewport_StatusText.instance != null)
	                Viewport_StatusText.instance.ShowStatusText(
	                    "img2img aborted: invalid width/height in SD input panel.", false, 5f, false);
	            return;
	        }
	        // Pre-Neo / SD1.5 path: keep ContentCam + masks at native capture size for projection.
	        // Neo only needs matching init/mask *encode* sizes — see PrepareImg2ImgEncodePair below.
	        if (viewTex != null && screenMask_skipAntiEdge == null)
	            screenMask_skipAntiEdge = TextureTools_SPZ.CreateSolidColorRGBA32(
	                viewTex.width, viewTex.height, Color.white);

	        intermediates_ =  new SD_GenRequestArgs_byproducts{
	            screenSpaceMask_NE_disposableTex = screenMask_skipAntiEdge,
	            screenSpaceMask_WE_disposableTex = screenMask_withAntiEdge,//so that we can use it later, during projections etc.
	            usualView_disposableTexture   = viewTex,
	        };
        
	        string positivePrompt = StableDiffusion_Prompts_UI.instance != null
	            ? StableDiffusion_Prompts_UI.instance.positivePrompt : "";
	        string negativePrompt = StableDiffusion_Prompts_UI.instance != null
	            ? StableDiffusion_Prompts_UI.instance.negativePrompt : "";
	        PostProcess_Prompt(ref positivePrompt, ref negativePrompt);

	        Texture2D encodeInit;
	        Texture2D encodeMask;
	        Texture2D encodeDisposeA;
	        Texture2D encodeDisposeB;
	        TextureTools_SPZ.PrepareImg2ImgEncodePair(
	            viewTex, screenMask_skipAntiEdge,
	            out encodeInit, out encodeMask, out encodeDisposeA, out encodeDisposeB);
	        // Neo output size must match encode init/mask (ContentCam frustum). Using panel WxH when
	        // capture aspect differs stretches the result and warps projection bake.
	        int payloadW = outW;
	        int payloadH = outH;
	        if (encodeInit != null && encodeInit.width > 0 && encodeInit.height > 0){
	            payloadW = encodeInit.width;
	            payloadH = encodeInit.height;
	        } else if (encodeMask != null && encodeMask.width > 0 && encodeMask.height > 0){
	            payloadW = encodeMask.width;
	            payloadH = encodeMask.height;
	        }
	        string initB64 = TextureTools_SPZ.TextureToBase64(encodeInit);
	        string maskB64 = encodeMask == null ? "" : TextureTools_SPZ.TextureToBase64(encodeMask);
	        if (encodeDisposeA != null) UnityEngine.Object.Destroy(encodeDisposeA);
	        if (encodeDisposeB != null) UnityEngine.Object.Destroy(encodeDisposeB);

	        payload_ = new SD_img2img_payload {
	            prompt = positivePrompt,
	            negative_prompt = negativePrompt,
	            sampler_name = input.samplers?.value?.name??"",
	            scheduler = input.scheduler?.value?.name??"",
	            batch_size = Mathf.RoundToInt(input.batch_size),
	            n_iter = Mathf.RoundToInt(input.batch_count),
	            steps  = Mathf.RoundToInt(input.sampleSteps_slider != null ? input.sampleSteps_slider.value : 0f),
	            cfg_scale = input.CFG_scale_slider != null ? input.CFG_scale_slider.value : 1f,
	            seed   = input.seed_intField != null && input.seed_intField.recentVal>0
	                ? input.seed_intField.recentVal : Random.Range(0, int.MaxValue),

	            width  = payloadW,
	            height = payloadH,

	            // Nov 2024: Turned off, - user will manually press x2 and x4 to upscale images using the img2extra url.
	            //     width = Mathf.RoundToInt(input.width * SD_Upscalers.instance.upscaleBy),
	            //     height = Mathf.RoundToInt(input.height * SD_Upscalers.instance.upscaleBy),
	            //
	            //     webui wants Upscaler for img2img to be sent via Options, which we do separatelly.
	            //     So will be ignored by webui, but will help us later on, inside StableProjectorz.
	            //     enable_hr_spz = SD_Upscalers.instance.selectedUpscaler_name != "",
	            //     hr_scale_spz = SD_Upscalers.instance.upscaleBy,
	            //     
	            //     refiner_checkpoint = SD_Refiner.instance.selectedModel_name,
	            //     refiner_switch_at = SD_Refiner.instance.switchAt01,

	            //webui also wants tiling to be enabled via Options, that are sent separately.
	            //This has to be sent too though (else tiling might remain enabled at current webui version, May 2024)
	            tiling = SD_WorkflowOptionsRibbon_UI.instance != null && SD_WorkflowOptionsRibbon_UI.instance.isTileable, 

	            inpaint_full_res = (int)0,//whole picture always. User can zoom up if they need to.
	            inpainting_mask_invert = inpaintMaskInvert01, //0 = WebUI inpaint masked (white); 1 = inpaint outside mask (Settings).

	            inpaint_full_res_padding = 0, //how many pixels to add to the mask.  Note, in case of entireShape, silhuette was already dilated by correct number of pixels.
	                                          //For brushed masks, padding is undesirable, could mess up around brushed borders in StableProjectorz (in projection shader)
	            include_init_images = true,
	            init_images = new string[]{ initB64 },
	            mask = maskB64, //send the SKIP-anti-edge. Avoids revealing any black untextured areas to SD.
	            alwayson_scripts = new Dictionary<string,AlwaysOn_Value>(),

	            mask_blur = 0,//ZERO. we don't want to add blur - we probably already added to the mask before by our BlurTextures_MGR.
	                          //Might mess up the blending later
	            denoising_strength = denoise_strength,

	            inpainting_fill = (int)inpaint_fill,
	        };

	        // Avoid softInpaint if rendering 'EntireShape' (when we have background active).
	        // We will use LatentNothing, and soft inpaint doesn't work with it.
	        // For more info - see comment inside img2img_GetTextures_andFill().
	        // Klein/Neo: Soft Inpainting scripts often stall or inflate ETA — skip on Flux.2 Klein.
	        SoftInpaintingArgs softInpaint_args =  WorkflowRibbon_UI.instance != null && WorkflowRibbon_UI.instance.is_allow_SoftInpaint()
	            && !StableDiffusion_Hub.IsActiveCheckpointKlein()
	            && !isMakingBackgrounds
	            && inpaint_fill != InpaintingFill.LatentNothing
	            && Inpaint_MaskPainter.instance != null
	                ? Inpaint_MaskPainter.instance.GetArgs_for_SoftInpaint_GenRequest() : null;
	        if (softInpaint_args != null){
	            payload_.alwayson_scripts.Add("Soft Inpainting", softInpaint_args);
	            intermediates_.isScreenMask_forSoftInpaint = true;
	        } else if (StableDiffusion_Hub.IsActiveCheckpointKlein()
	                   && SD_WorkflowOptionsRibbon_UI.instance != null
	                   && SD_WorkflowOptionsRibbon_UI.instance.isSoftInpaint
	                   && Viewport_StatusText.instance != null){
	            Viewport_StatusText.instance.ShowStatusText(
	                "Soft Inpaint skipped on Flux.2 Klein (Neo compatibility).", false, 3f, false);
	        }
	        ControlNet_NetworkArgs ctrlNets_args = SD_ControlNetsList_UI.instance != null
	            ? SD_ControlNetsList_UI.instance.GetArgs_forGenerationRequest(intermediates_) : null;
	        if(ctrlNets_args != null && ctrlNets_args.args != null && ctrlNets_args.args.Length > 0){ 
	            payload_.alwayson_scripts.Add("controlnet", ctrlNets_args);//https://github.com/Mikubill/sd-webui-controlnet/wiki/API#examples-1
	        }
	    }



	    public void Create_upscale_payload( Texture2D imgForSending, float upscaleBy, 
	                                        out SD_img2extra_payload payload_){
	        make_upscale_payload(imgForSending, upscaleBy, out payload_);
	    }

	    // requests a view texture and uses that for upscaing
	    public void Create_upscale_payload( float upscaleBy, 
	                                        out SD_img2extra_payload payload_, 
	                                        out SD_GenRequestArgs_byproducts byproducts){
	        if (Objects_Renderer_MGR.instance != null)
		        Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
	        Texture2D viewTex = UserCameras_MGR.instance.camTextures.GetDisposable_ContentCamTexture();
	        make_upscale_payload(viewTex, upscaleBy, out payload_);
	        byproducts = new SD_GenRequestArgs_byproducts();
	        byproducts.usualView_disposableTexture = viewTex;

	        var painter  = Inpaint_MaskPainter.instance;
	        Texture2D screenMask_skipAntiEdge_;
	        Texture2D screenMask_withAntiEdge_;

	        // If we are in projectionMask, request full white mask. Otherwise it would remain black.
	        // Else, proceed with whatever is needed (TotalObj with anti-edge, etc).
	        var currMode = WorkflowRibbon_UI.instance.currentMode();
	        bool forceFullWhite =  currMode== WorkflowRibbon_CurrMode.ProjectionsMasking;
	        painter.GetDisposable_ScreenMask( forceFullWhite:forceFullWhite,
	                                          out screenMask_skipAntiEdge_, out screenMask_withAntiEdge_);

	        byproducts.screenSpaceMask_NE_disposableTex = screenMask_skipAntiEdge_;
	        byproducts.screenSpaceMask_WE_disposableTex = screenMask_withAntiEdge_;//so that we can use it later, during projections etc.
	    }


	    void make_upscale_payload(Texture2D tex, float upscaleBy, out SD_img2extra_payload payload_){
	        var imgEntry = new SD_Img2Extra_Image(){  
	            data = TextureTools_SPZ.TextureToBase64(tex), 
	            name = "0"
	        };
	        string upscaler_name = SD_Upscalers.instance.selectedUpscaler_name;
	               upscaler_name = string.IsNullOrEmpty(upscaler_name) ? "R-ESRGAN 4x+" : upscaler_name;

	        payload_ = new SD_img2extra_payload{
	            upscaling_resize = upscaleBy,
	            upscaler_1 = upscaler_name,
	            imageList = new List<SD_Img2Extra_Image>{ imgEntry },
	            rslt_imageWidths  = Mathf.RoundToInt(tex.width * upscaleBy),
	            rslt_imageHeights = Mathf.RoundToInt(tex.height * upscaleBy),
	        };
	    }

    
	    // -------------------------------------------------------------------------
	    // Traditional img2img flow (no layer system): base UV + projections + single
	    // paint buffer + lighting/AO → accumulation → content camera captures that →
	    // init_images + mask + denoise + PROMPT sent to SD. Color/redo/Generate use that path.
	    // With layers: same pipeline. Bridge collapses every VISIBLE layer into one
	    // composite, we capture that as init_images, mask from same composite, redo
	    // slider = denoising_strength, prompt from UI. So the generative model receives
	    // prompt + init + mask + denoising and runs the full diffusion (generalizes),
	    // not just reproducing the init. Denoising is clamped to a minimum >0 when
	    // using Original fill so SD always runs at least a little diffusion.
	    // -------------------------------------------------------------------------

	    // If we have bg, we will produce screenMask of the entireShape.
	    void img2img_GetTextures_andFill( bool forceFullWhiteMask, 
	                                      out Texture2D screenMask_skipAntiEdge_, out Texture2D screenMask_withAntiEdge_,
	                                      out Texture2D viewTex_, 
	                                      out InpaintingFill inpaint_fill_,  
	                                      out float denoiseStrength_ ){
	        var camerasMGR = UserCameras_MGR.instance;
	        var painter    = Inpaint_MaskPainter.instance;

	        screenMask_skipAntiEdge_ = null;
	        screenMask_withAntiEdge_ = null;
	        viewTex_ = null;
	        inpaint_fill_ = InpaintingFill.Original;
	        denoiseStrength_ = 0.5f;

	        if (camerasMGR == null || camerasMGR.camTextures == null || painter == null
	            || WorkflowRibbon_UI.instance == null){
	            if (Viewport_StatusText.instance != null)
	                Viewport_StatusText.instance.ShowStatusText(
	                    "img2img aborted: cameras/mask painter/workflow not ready.", false, 5f, false);
	            return;
	        }

	        // Peek Klein CN init before any ReadPixels so CustomFile can skip ContentCam entirely,
	        // and ContentCam uses a single post-Ensure capture (not a duplicate from TryGet).
	        int kleinUnitIx = -1;
	        string kleinSrcLabel = "";
	        bool kleinFromCn = !forceFullWhiteMask
	            && StableDiffusion_Hub.IsActiveCheckpointKlein()
	            && SD_ControlNetsList_UI.instance != null
	            && SD_ControlNetsList_UI.instance.TryPeekKleinImg2ImgInitSource(out kleinUnitIx, out kleinSrcLabel);
	        bool kleinUsesCustomFile = kleinFromCn
	            && string.Equals(kleinSrcLabel, "CustomFile", System.StringComparison.Ordinal);

	        if (kleinUsesCustomFile){
	            if (!SD_ControlNetsList_UI.instance.TryGetDisposableKleinImg2ImgInit(
	                    out viewTex_, out kleinUnitIx, out kleinSrcLabel) || viewTex_ == null){
	                kleinFromCn = false;
	                if (!forceFullWhiteMask && Objects_Renderer_MGR.instance != null)
		                Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
	                viewTex_ = camerasMGR.camTextures.GetDisposable_ContentCamTexture();
	            }
	        } else {
	            // Apply layer stack immediately before content-cam capture so init matches the viewport
	            // (Objects_Renderer can rebuild accumulation in OnUpdate the same frame).
	            if (!forceFullWhiteMask && Objects_Renderer_MGR.instance != null)
		            Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
	            viewTex_ = camerasMGR.camTextures.GetDisposable_ContentCamTexture();
	        }

	        painter.GetDisposable_ScreenMask( forceFullWhite:forceFullWhiteMask, 
	                                          out screenMask_skipAntiEdge_, out screenMask_withAntiEdge_ );
        
	        inpaint_fill_ = WorkflowRibbon_UI.instance.Get_InpaintFill();
        
	        // Same as traditional: when Original (Color/NoColor), use redo slider so SD runs diffusion and uses the prompt. Enforce minimum >0 so we never send 0 (which would make SD return the init image unchanged).
	        if (inpaint_fill_ == InpaintingFill.Original)
	        {
		        float redo = SD_WorkflowOptionsRibbon_UI.instance != null ? SD_WorkflowOptionsRibbon_UI.instance.denoisingStrength : 0.5f;
		        denoiseStrength_ = Mathf.Max(redo, 0.01f);
	        }
	        else
		        denoiseStrength_ = 1.0f;

	        // Klein CN co-opt: Original + redo denoise so LatentNothing (ProjectionsMasking) does not wipe the ref.
	        // ContentCam init is the single post-Ensure capture above — do not TryGet again.
	        if (kleinFromCn && viewTex_ != null){
	            inpaint_fill_ = InpaintingFill.Original;
	            float redo = SD_WorkflowOptionsRibbon_UI.instance != null
	                ? SD_WorkflowOptionsRibbon_UI.instance.denoisingStrength : 0.45f;
	            denoiseStrength_ = Mathf.Clamp(Mathf.Max(redo, 0.15f), 0.15f, 0.85f);
	            // Full-white only for ProjectionsMasking (silhouette bake). WhereEmpty/TotalObject
	            // auto-masks must stay — !has_brushed_mask() used to wipe them.
	            if (WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.ProjectionsMasking){
	                if (screenMask_skipAntiEdge_ != null) UnityEngine.Object.Destroy(screenMask_skipAntiEdge_);
	                if (screenMask_withAntiEdge_ != null) UnityEngine.Object.Destroy(screenMask_withAntiEdge_);
	                painter.GetDisposable_ScreenMask(forceFullWhite: true,
	                    out screenMask_skipAntiEdge_, out screenMask_withAntiEdge_);
	            }
	            if (Viewport_StatusText.instance != null){
	                Viewport_StatusText.instance.ShowStatusText(
	                    $"Klein img2img init from ControlNet {kleinUnitIx} ({kleinSrcLabel}), denoise {denoiseStrength_:0.00}.",
	                    false, 4f, false);
	            }
	        }
	    }



	    void PostProcess_Prompt(ref string positive, ref string negative){
	        if (Settings_MGR.instance == null) return;
	        negative += Settings_MGR.instance.get_avoid_NSFW_generations() ? 
	                       ", NSFW, sex, porn, penis, vagina"//don't allow (adding to negative prompt)
	                      : ""; //allow
	    }
	}
}//end namespace
