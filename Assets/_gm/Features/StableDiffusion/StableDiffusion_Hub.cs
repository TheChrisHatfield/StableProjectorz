using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using UnityEngine.Networking;



namespace spz {

	public class StableDiffusion_Hub : MonoBehaviour {
	    public static StableDiffusion_Hub instance { get; private set; } = null;
    
	    [SerializeField] SD_GenRequests_Helper _genRequest_helper;

	    public bool _finalPreparations_beforeGen         => _genRequest_helper._finalPreparations_beforeGen;//few frames before sending for AI generations.
	    public Generate_RequestingWhat _isGeneratingWhat => _genRequest_helper._isGeneratingWhat;//reset to 'nothing' once generation is done.
	    public bool _generating                          => _genRequest_helper._isGeneratingWhat != Generate_RequestingWhat.nothing;
	    public float _generationCooldownUntil            => _genRequest_helper._generationCooldownUntil;

	    public static Action _Act_img2img_willRequest { get; set; } = null;
	    public static Action<GenData2D> _Act_img2img_requested { get; set; } = null;

       
	    void OnExportFinalTex_Button(bool isDilate){//dilation allows to "spread" the texture outwards from uv-chunks. Helps to avoid seams.
	        Save_MGR.instance.SaveProjectionTextures(isDilate);
	    }

	    void OnExportViewTextures_Button(){//save whatever the camera is observing (view,depth,normals,etc)
	        Save_MGR.instance.SaveViewTextures();
	    }


	    public void isCanGenerate(out bool canGenArt_, out bool canGenBG_){
	        bool isOnCooldown =  Time.unscaledTime < _generationCooldownUntil;
	        bool isConnected  =  Connection_MGR.is_sd_connected;
	        bool gen3dBusy = Gen3D_API.instance != null && Gen3D_API.instance.isBusy;

	        canGenBG_  =  !isOnCooldown  &&  !_generating  &&  isConnected && !gen3dBusy;
	        canGenArt_ =  !isOnCooldown  &&  !_generating  &&  isConnected && !gen3dBusy;
	        // Flux.2 Klein: Gen Art uses ImageStitch structure (not Fun-Union). Button enable = Klein ckpt;
	        // fail-closed structure attach runs on Deny/payload (HasMeshDepthRt alone stays false until a
	        // depth lock holder exists — would permanently disable Gen Art).
	        bool kleinReady = IsActiveCheckpointKlein();
	        canGenArt_ &= has_Depth_or_Norm_or_RefOnly() || kleinReady;
	    }


	    public bool DenyWithMessage_ifCantGenerate(bool allow_without_controlnets){
	        if(AmbientOcclusion_Baker.instance != null && AmbientOcclusion_Baker.instance.isGeneratingAO){
	            Viewport_StatusText.instance?.ShowStatusText("Can't Generate images while Baking AO. Please wait", false, 2, true);
	            return true; 
	        }
	        if (Gen3D_API.instance != null && Gen3D_API.instance.isBusy){
	            Viewport_StatusText.instance?.ShowStatusText("Can't Generate images while 3D generation is running. Please wait", false, 2, true);
	            return true;
	        }
	        if (ModelsHandler_3D.instance != null && ModelsHandler_3D.instance._isImportingModel){
	            Viewport_StatusText.instance?.ShowStatusText("Can't Generate images while Loading 3D Model file. Please wait", false, 2, true);
	            return true;
	        }
	        if (Connection_MGR.is_sd_connected == false){ 
	            // OG health: actionable black-window guidance (dropdown captions stay on DisplayText).
	            Viewport_StatusText.instance?.ShowStatusText(SdDisconnectPlaceholder.StatusText, false, 2, true);
	            return true;
	        }
	        bool klein = IsActiveCheckpointKlein();
	        if(allow_without_controlnets==false && !klein && ControlNetUnit_UI.hasAtLeastSomeModel == false){
	            bool flux2DevEmpty = false;
	            try {
	                string sd = SD_InputPanel_UI.instance != null
	                    ? SD_InputPanel_UI.instance.models?.selectedModel_name : null;
	                flux2DevEmpty = SD_OptionsPacket.CheckpointLooksFlux2Dev(sd);
	            } catch { /* */ }
	            string msg = flux2DevEmpty
	                ? "Can't Generate yet: FLUX.2-dev needs Fun-Union ControlNet.\nCTRL NETS → open a unit → Download More → Fun-Union (or the big download button)."
	                : "Can't Generate images yet. You need to download a Depth Control Net.\nGo to ControlNet tab, open unit, and download it.";
	            CommandRibbon_UI.instance.Attention_toCtrlNetButton();
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 5, true);
	            return true;
	        }
	        if (ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels){
	            Viewport_StatusText.instance.ShowStatusText("Can't Generate images while ControlNet downloads a model. Please wait.", false, 2, true);
	            return true;
	        }
	        if(_generating){
	            Viewport_StatusText.instance.ShowStatusText("Can't Generate — a generation is already in progress (or cancelling).", false, 2, true);
	            return true;
	        }
	        if(_finalPreparations_beforeGen){
	            Viewport_StatusText.instance.ShowStatusText("Can't Generate — still preparing the last request.", false, 2, true);
	            return true;
	        }
	        if (Time.unscaledTime < _generationCooldownUntil){
	            Viewport_StatusText.instance.ShowStatusText("Can't Generate yet — brief cooldown after cancel/finish.", false, 2, true);
	            return true;
	        }

	        if (!klein && has_Depth_or_Norm_or_RefOnly()==false){
	            bool flux2Dev = false;
	            try {
	                string sd = SD_InputPanel_UI.instance != null
	                    ? SD_InputPanel_UI.instance.models?.selectedModel_name : null;
	                flux2Dev = SD_OptionsPacket.CheckpointLooksFlux2Dev(sd);
	            } catch { /* */ }
	            string msg = flux2Dev
	                ? "To generate with FLUX.2-dev you need Fun-Union ControlNet (Depth).\nCTRL NETS → Download More → Fun-Union, then assign Depth / restart after install."
	                : "To generate projections you need  a Depth or a Normals Control Net." +
	                  "\nGo to ControlNet tab, open a unit and assign depth or normals";
	            CommandRibbon_UI.instance.Attention_toCtrlNetButton();
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 5, true);
	            return true;
	        }
	        // Empty CustomFile only blocks when there is no other valid Klein pixel init.
	        if (klein && allow_without_controlnets==false
	            && SD_ControlNetsList_UI.instance != null
	            && SD_ControlNetsList_UI.instance.HasArmedEmptyKleinCustomFile()
	            && !SD_ControlNetsList_UI.instance.HasKleinImg2ImgInitSource()){
	            string msg = "Can't Generate: a ControlNet unit is set to CustomFile but no image is loaded.\nLoad a file or switch What-to-send to ContentCam / None.";
	            CommandRibbon_UI.instance.Attention_toCtrlNetButton();
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 5, true);
	            return true;
	        }
	        // Klein Gen Art requires mesh depth structure (ImageStitch), not Depth-as-init.
	        if (klein && allow_without_controlnets==false
	            && !SD_KleinStructureChannel.CanCaptureMeshDepth()){
	            string msg = "Flux.2 Klein Gen Art needs mesh depth structure (3D geometry depth RT).\nEnsure a mesh is loaded and depth cameras can render.";
	            CommandRibbon_UI.instance.Attention_toCtrlNetButton();
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 5, true);
	            return true;
	        }
	        // ImageStitch encodes depth through sd_vae — None / wrong VAE silently drops structure lock.
	        if (klein && allow_without_controlnets==false){
	            // Always Ensure+SubmitOptions so Neo gets sd_vae even when UI already shows Klein VAE.
	            bool hasKleinVae = SD_Neural_Models.EnsureKleinSdVaeSelected();
	            if (!hasKleinVae){
	                string msg = "Flux.2 Klein Gen Art needs VAE flux2_klein_4b_vae.safetensors.\nSelect it in the SD VAE dropdown (sd_vae=None breaks depth structure).";
	                Viewport_StatusText.instance.ShowStatusText(msg, false, 6, true);
	                return true;
	            }
	        }
	        return false;
	    }

	    /// <summary>True when the SD dropdown is Flux.2 Klein — Gen Art may skip depth/normals CN.</summary>
	    public static bool IsActiveCheckpointKlein(){
	        try {
	            string sd = SD_InputPanel_UI.instance != null ? SD_InputPanel_UI.instance.models?.selectedModel_name : null;
	            return SD_OptionsPacket.CheckpointNeedsKleinModules(sd);
	        } catch {
	            return false;
	        }
	    }

    
	    bool has_Depth_or_Norm_or_RefOnly(){
	        var cnList = SD_ControlNetsList_UI.instance;
	        if (cnList == null) return false;

	        // Heal before gating so leftover SD1.5/XL weights on FLUX.2-dev (or XL) can swap to a
	        // compatible model while Fun-Union/depth is installed — otherwise Deny never reaches GetArgs.
	        cnList.TryHealFamilyMismatchedModels();

	        bool is_img2img   =  WorkflowRibbon_UI.instance.isMode_using_img2img();
	        // Ignore means GetArgs skips depth/normals — do not require a phantom inactive unit's model.
	        if (is_img2img && SD_WorkflowOptionsRibbon_UI.instance.ignoreDepthOrNormals)
	            return true;

	        // Valid model must be on an *active* depth/normals unit (same unit GetArgs will send).
	        // onlyActive:false previously let inactive Fun-Union satisfy the gate while the active
	        // Depth unit stayed on None → Gen Art with empty alwayson ControlNet.
	        bool hasDepth = cnList.Has_Depth_CTRLUnit(
	            onlyActive: true, only_if_validModel: true);
	        bool hasNormals = cnList.Has_Normals_CTRLUnit(
	            onlyActive: true, only_if_validModel: true);

	        bool hasReferenceOnly = cnList.Num_Active_Reference_CTRLUnit() > 0;
	        return hasDepth || hasNormals || hasReferenceOnly;
	    }



	    /// <summary>
	    /// Public method to trigger generation (wrapper for OnGenerateButton)
	    /// </summary>
	    public void Generate(bool isMakingBackgrounds) {
		    OnGenerateButton(isMakingBackgrounds);
	    }

	    void OnGenerateButton(bool isMakingBackgrounds){
	        if( DenyWithMessage_ifCantGenerate(allow_without_controlnets:isMakingBackgrounds) ){ return; }

	        bool isMode_Img2Img = WorkflowRibbon_UI.instance.isMode_using_img2img();
	        bool hasAutoMask    = WorkflowRibbon_UI.instance.has_auto_mask();
	        bool hasBrushedMask = WorkflowRibbon_UI.instance.has_brushed_mask();
	        bool hasBackground  = WorkflowRibbon_UI.instance.has_background_mask();
	        bool hasBackgroundColors = SkyboxBackground_MGR.instance.isGradientColorClear==false;

	        bool do_img2Img  =  isMode_Img2Img && (hasAutoMask || hasBrushedMask);
	             do_img2Img |=  hasBackground || hasBackgroundColors;
	        // Flux.2 Klein Gen Art MUST stay on txt2img.
	        // Neo flux2.get_learned_conditioning prepends img2img ini_latent ahead of ImageStitch
	        // ref_latents → [init, style, depth], which breaks RefControl order ([style, depth]).
	        // CustomFile/ContentCam belong in ImageStitch style (image1), never as img2img init.
	        // Gen BG keeps normal img2img routing (no structure RefControl contract).
	        bool kleinGenArt = !isMakingBackgrounds && IsActiveCheckpointKlein();
	        if (kleinGenArt)
	            do_img2Img = false;

	        if(do_img2Img){
	            _Act_img2img_willRequest?.Invoke();
	            _genRequest_helper.Generate_img2img(isMakingBackgrounds, OnAfterRequested_img2img);
	        }else {
	            _genRequest_helper.Generate_txt2Img(isMakingBackgrounds);
	        }
	    }


	    public void ManuallyUpscale(float upscaleBy, Guid textureGuid_insideGen, GenData2D fromGen_canBeNull=null){
	        if(DenyWithMessage_ifCantGenerate(allow_without_controlnets:true)){ return; }

	        GenData_TextureRef texRef = fromGen_canBeNull?.GetTexture_ref(textureGuid_insideGen);
	        if(fromGen_canBeNull!=null && texRef.tex2D==null){
	            Viewport_StatusText.instance.ShowStatusText("Can't upscale composite images, only separate ones", false, 2, true);
	            return;
	        }
	        _Act_img2img_willRequest?.Invoke();
	        _genRequest_helper.Upscale_img2extra( upscaleBy, fromGen_canBeNull, 
	                                              texRef.tex2D, OnAfterRequested_img2img );
	    }


	    public void ManuallyUpscale_View(float upscaleBy){
	        if (DenyWithMessage_ifCantGenerate(allow_without_controlnets: true)){ return; }
	        _Act_img2img_willRequest?.Invoke();
	        _genRequest_helper.Upscale_img2extra( upscaleBy, genData_canBeNull:null, 
	                                              imgForSending:null, OnAfterRequested_img2img );
	    }

    
	    void OnAfterRequested_img2img(){
	        // Before other listeners: restore scene / layer-RT resolution state (must not depend on delegate order or exceptions in UI subscribers).
	        SceneResolution_MGR.ApplyImg2ImgResolutionCleanupAfterPayloadSent();
	        Guid guid = GenData2D_Archive.instance.latestGeneration_GUID;
	        GenData2D latestData = GenData2D_Archive.instance.GenerationGUID_toData(guid);
	        _Act_img2img_requested?.Invoke(latestData);
	    }


	    // For example, detect depth from an image, via zoedepth preprocessor.
	    // In case of en error, onDetected arg will contain null.
	    public void ManuallyControlnetDetect( SD_ControlnetDetect_payload payload, 
	                                          Action<SD_ControlnetDetect_Response> onDetected ){
	        _genRequest_helper.Submit_CtrlnetDetectRequest(payload, onDetected);
	    }


	    public bool SubmitCustomWorkflow( Generate_RequestingWhat what,  bool sendPayload=false, SD_img2img_payload payload = null, 
	                                      Action<UnityWebRequest> onProgress = null,  Action<UnityWebRequest> onCompleted = null )
	        => _genRequest_helper.SubmitCustomWorkflow(what, sendPayload, payload, onProgress, onCompleted);


	    public void MarkCustomWorkflow_Done()
	        => _genRequest_helper.MarkCustomWorkflow_Done();

	    /// <summary>Interrupt HTTP gen even for <c>somethingCustom</c> (HDR spheres) — Hub cancel bus otherwise early-returns.</summary>
	    public void ForceInterruptIncludingCustom() {
	        if (_genRequest_helper == null) return;
	        _genRequest_helper.RequestHttpInterruptOnly();
	    }


	    public void OnStopGenerate_Button(){
	        //see if it's stopping for a generation that we initiated:
	        bool isMine = _isGeneratingWhat == Generate_RequestingWhat.txt2img ||
	                      _isGeneratingWhat == Generate_RequestingWhat.img2img ||
	                      _isGeneratingWhat == Generate_RequestingWhat.upscale ||
	                      _isGeneratingWhat == Generate_RequestingWhat.ctrlnetDetect;
	        if(!isMine){ return; }//we don't care about this signal, someone else will handle it.
	        _genRequest_helper.OnStopGenerate_Button();
	    }
    
    

	    public void OnDeleteLast_Button(){
	        var guid = GenData2D_Archive.instance.latestGeneration_GUID;
	        if(guid == default){ return; }

	        //make sure we show the pannel where we've just deleted:
	        GenData2D genData =  GenData2D_Archive.instance.GenerationGUID_toData(guid);
	        if(genData != null){ 
	            switch (genData.kind){
	                case GenerationData_Kind.Unknown:
	                case GenerationData_Kind.TemporaryDummyNoPics:
	                case GenerationData_Kind.SD_ProjTextures:
	                case GenerationData_Kind.UvTextures_FromFile:
	                case GenerationData_Kind.UvNormals_FromFile:
	                case GenerationData_Kind.AmbientOcclusion:
	                    CommandRibbon_UI.instance.clickArtList_toggle_manual();//merely shows the ui panel
	                    break;
	                case GenerationData_Kind.SD_Backgrounds:
	                case GenerationData_Kind.BgNormals_FromFile:
	                default:
	                    CommandRibbon_UI.instance.clickArtBGList_toggle_manual();//merely shows the ui panel
	                    break;
	            }
	        }
	        //finally, dispose the genData, which will notify arts list, etc, and will delete the ui icons:
	        GenData2D_Archive.instance.DisposeGenerationData(guid);
	    }


	    //just loading a texture from file, without putting it into 'generations_dictionary' yet.
	    //You can do it yourself if you want
	    public void Load_image_fromUserFile(string filepath, out Texture2D texture_takeOwnership_){
	        texture_takeOwnership_ = null;
	        if(File.Exists(filepath) == false){ return; }
	         byte[] fileData = File.ReadAllBytes(filepath);
	        Texture2D tex = new Texture2D(2, 2);
	        // Load the image data into the texture (size will be set automatically)
	        if(tex.LoadImage(fileData) == false){ return;}
	        texture_takeOwnership_ = tex;
	    }

    
	    void Update(){
	        if(KeyMousePenInput.isSomeInputFieldActive()){ return; }//typing in text field, etc

	        bool isSD = DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_sd;

	        if (isSD){ 
	            if(Input.GetKeyDown(KeyCode.G) && KeyMousePenInput.isKey_CtrlOrCommand_pressed()){
	                OnGenerateButton(isMakingBackgrounds:false);
	            }
	            if(Input.GetKeyDown(KeyCode.G) && KeyMousePenInput.isKey_Shift_pressed()){
	                OnGenerateButton(isMakingBackgrounds: true);
	            }
	            if(Input.GetKeyDown(KeyCode.V) && KeyMousePenInput.isKey_CtrlOrCommand_pressed()){
	                ManuallyUpscale_View(upscaleBy:2);
	            }
	            if(Input.GetKeyDown(KeyCode.V) && KeyMousePenInput.isKey_Shift_pressed()){
	                ManuallyUpscale_View(upscaleBy:4);
	            }
	        }
	    }


	    void Awake(){
	        if (instance != null){ DestroyImmediate(this);return; }
	        instance = this;
	    }

	    void Start(){
	        GenerateButtons_UI.OnDeleteGenerationButton += OnDeleteLast_Button;
	        GenerateButtons_UI.OnCancelGenerationButton += OnStopGenerate_Button;
	        GenerateButtons_UI.OnGenerateArtButton += ()=>OnGenerateButton(isMakingBackgrounds: false);
	        GenerateButtons_UI.OnGenerateBG_Button += ()=>OnGenerateButton(isMakingBackgrounds:true);
        
	        SD_Upscalers.OnGenUpscaleVisible_ButtonX2 += ()=>ManuallyUpscale_View(2);
	        SD_Upscalers.OnGenUpscaleVisible_ButtonX4 += ()=>ManuallyUpscale_View(4);

	        ExportSave_UI_MGR.OnExportFinalTex_Button += ()=>OnExportFinalTex_Button(isDilate:true);
	        ExportSave_UI_MGR.OnExportFinalTex_NoDilate_Button += ()=>OnExportFinalTex_Button(isDilate:false);
	        ExportSave_UI_MGR.OnExportViews_Button += ()=>OnExportViewTextures_Button();
	    }

	    void OnDestroy(){
	        GenerateButtons_UI.OnDeleteGenerationButton -= OnDeleteLast_Button;
	        GenerateButtons_UI.OnCancelGenerationButton -= OnStopGenerate_Button;
	        SD_Upscalers.OnGenUpscaleVisible_ButtonX2 = null;
	        SD_Upscalers.OnGenUpscaleVisible_ButtonX4 = null;
	        GenerateButtons_UI.OnGenerateArtButton = null;
	        GenerateButtons_UI.OnGenerateBG_Button = null;
	    }
	}

}//end namespace
