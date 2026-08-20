using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;


namespace spz {

	public enum Generate_RequestingWhat{
	    nothing, somethingCustom, txt2img, img2img, upscale, ctrlnetDetect, Shadow_R_delighting, rembg_backgroundRemoval,
	}

	public class SD_GenRequests_Helper : MonoBehaviour{

	    [SerializeField] SD_Generate_PayloadMaker _payload_maker;
	    [SerializeField] SD_Generate_NetworkSender _generate_sender;
	    [SerializeField] float _generationCooldown = 1.3f;

	    public Texture _latestDepthTex_sent; //for an easier debug.
	    public Texture _latestScreenMask_sent;
	    public bool _latest_isImg2img;
	    public InpaintingFill _latest_Fill;

	    [Header("we sent WIHOUT anti-edge. And this one is used internally in StableProjectorz:")]
	    public Texture _latestUsualView_used;

	    // KEEPING HERE, not in Generations_Dictionary.
	    // Because user might Load image while we are still doing txt2img, etc.
	    // This would create generation for that image and cause that one to be "latest".
	    // We are using this variable only for txt2img and img2img requests:
	    GenData2D _latestGenData = null;

	    public bool _finalPreparations_beforeGen { get; private set; } = false;
	    public Generate_RequestingWhat _isGeneratingWhat { get; private set; } = Generate_RequestingWhat.nothing;//reset to 'nothing' once generation is done.
	    public float _generationCooldownUntil { get; private set; } = -9999; //to prevent relaunching generation too quickly (for example after Interrupting).

	    /// <summary>Active txt2img/img2img/upscale prep+send coroutine — cancel must stop this or it can still POST after interrupt.</summary>
	    Coroutine _activeRequestCrtn = null;
	    bool _cancelRequested = false;


	#if UNITY_EDITOR
	    public bool _dumTextures_toFile;
	    void OnValidate(){
	        if (!_dumTextures_toFile) { return; }
	        _dumTextures_toFile = false;
	        TextureTools_SPZ.EncodeAndSaveTexture(_latestScreenMask_sent as Texture2D, Directory.GetParent(Application.dataPath).FullName + "/_latestMask.png");
	        TextureTools_SPZ.EncodeAndSaveTexture(_latestUsualView_used as Texture2D, Directory.GetParent(Application.dataPath).FullName + "/_latestView.png");
	    }
	#endif


	    public void Generate_txt2Img(bool isMakingBackgrounds,  Action onRequested=null ){
	        _cancelRequested = false;
	        ClearStuckInterruptTimer();
	        if (_activeRequestCrtn != null){ StopCoroutine(_activeRequestCrtn); _activeRequestCrtn = null; }
	        _activeRequestCrtn = StartCoroutine( Generate_txt2Img_crtn(isMakingBackgrounds, onRequested) );
	    }

	    public void Generate_img2img(bool isMakingBackgrounds,  Action onRequested=null ){
	        _cancelRequested = false;
	        ClearStuckInterruptTimer();
	        if (_activeRequestCrtn != null){ StopCoroutine(_activeRequestCrtn); _activeRequestCrtn = null; }
	        _activeRequestCrtn = StartCoroutine( Generate_img2img_crtn(isMakingBackgrounds, onRequested) );
	    }


	    public void Upscale_img2extra(float upscaleBy,  GenData2D genData_canBeNull=null, 
	                                  Texture2D imgForSending=null, Action onRequested=null){
	        _cancelRequested = false;
	        ClearStuckInterruptTimer();
	        if (_activeRequestCrtn != null){ StopCoroutine(_activeRequestCrtn); _activeRequestCrtn = null; }
	        _activeRequestCrtn = StartCoroutine( Upscale_img2extra_crtn(upscaleBy, genData_canBeNull, imgForSending, onRequested) );
	    }

    
	    public void Submit_CtrlnetDetectRequest( SD_ControlnetDetect_payload payload, 
	                                             Action<SD_ControlnetDetect_Response> onDetected ){
	        _cancelRequested = false;
	        ClearStuckInterruptTimer();
	        if (_activeRequestCrtn != null){ StopCoroutine(_activeRequestCrtn); _activeRequestCrtn = null; }
	        _activeRequestCrtn = StartCoroutine( Submit_CtrlDetect_crtn(payload, onDetected) );
	    }

    
	    public bool SubmitCustomWorkflow( Generate_RequestingWhat what, bool sendPayload,  
	                                      SD_img2img_payload payload, Action<UnityWebRequest> onProgress, 
	                                      Action<UnityWebRequest> onCompleted ){
	        if(_isGeneratingWhat != Generate_RequestingWhat.nothing){ return false; }//already busy
	        // A prior custom interrupt can leave cancel armed if Done never ran; do not inherit it.
	        _cancelRequested = false;
	        ClearStuckInterruptTimer();
	        _isGeneratingWhat = what;
	        if (sendPayload && payload!=null){
	            _generate_sender.Send_GenerateRequest(payload, onProgress, onCompleted);
	        }
	        return true;
	    }

	    public void MarkCustomWorkflow_Done(){
	        _isGeneratingWhat = Generate_RequestingWhat.nothing;
	        // HDR / rembg cancel arms RequestHttpInterruptOnly which sets this true; leave it sticky
	        // and the next prep coroutine can abort before Generate_* clears the flag.
	        _cancelRequested = false;
	    }

	    /// <summary>POST /interrupt without full Gen-Art FinishTheInterrupt UI (custom workflows clear busy themselves).</summary>
	    public void RequestHttpInterruptOnly(){
	        _cancelRequested = true;
	        ClearStuckInterruptTimer();
	        _generate_sender.Send_StopGenerateRequest(() => {
	            // Do not call OnFinishTheInterrupt — custom owners already finished their UI.
	            // Only drop the sticky cancel so a later txt2img prep is not born cancelled.
	            _cancelRequested = false;
	        });
	        // If /interrupt never settles, still clear the flag (custom path has no OnFinish timer).
	        _finishTheInterrupt_ifStuck_crtn = StartCoroutine(ClearCancelFlagIfStuck(10f));
	    }

	    IEnumerator ClearCancelFlagIfStuck(float graceDelay){
	        yield return new WaitForSeconds(graceDelay);
	        _finishTheInterrupt_ifStuck_crtn = null;
	        _cancelRequested = false;
	    }



	    IEnumerator Generate_txt2Img_crtn(bool isMakingBackgrounds, Action onRequested = null) {
        
	        if(!Start_GenerationRequest(Generate_RequestingWhat.txt2img)){
	            _activeRequestCrtn = null;
	            yield break;
	        }

	        UserCameras_Permissions.Force_KeepRenderingCameras(true);
	        try {
	        //for inpaint to apply itself, etc. (or to avoid checker pattern if had No-Color Mask)
	        Objects_Renderer_MGR.instance?.ReRenderAll_soon();

	            for (int i=0; i<3; ++i){
	                if (_cancelRequested){
	                    AbortPrepAfterCancel();
	                    yield break;
	                }
	                yield return null;
	            }//give time for cameras to render the target textures.
	            if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }
            
	            GenerationData_Kind genData_kind = isMakingBackgrounds? GenerationData_Kind.SD_Backgrounds 
	                                                                  : GenerationData_Kind.SD_ProjTextures;
	            SD_txt2img_payload payload;
	            SD_GenRequestArgs_byproducts intermediates;
	            _payload_maker.Create_txt2img_payload(out payload, out intermediates, isMakingBackgrounds);

	            if (_cancelRequested){
	                intermediates?.Dispose();
	                AbortPrepAfterCancel();
	                yield break;
	            }

	            if (intermediates != null && intermediates.kleinStructureAttachFailed){
	                intermediates.Dispose();
	                _finalPreparations_beforeGen = false;
	                _isGeneratingWhat = Generate_RequestingWhat.nothing;
	                _activeRequestCrtn = null;
	                yield break;
	            }

	            _generate_sender.Send_GenerateRequest(payload, OnProgressResponse, OnGeneratedResult);

	            _latestGenData = GenData2D_Maker.make_txt2img(payload, intermediates, genData_kind);

	            Finalize_GenerationRequest( payload.width,  payload.height,  payload.n_iter,  
	                                        payload.batch_size, "txt2img" );
            
	        RememberSentVals_forDebug(intermediates,  isImg2Img:false );
	        _activeRequestCrtn = null;
	        onRequested?.Invoke();
	        } finally {
	            UserCameras_Permissions.Force_KeepRenderingCameras(false);
	        }
	    }


	    IEnumerator Generate_img2img_crtn(bool isMakingBackgrounds,  Action onRequested=null){
        
	        if( !Start_GenerationRequest(Generate_RequestingWhat.img2img) ){
	            if (SceneResolution_MGR.LastImg2imgWillAppliedPrep)
		            SceneResolution_MGR.RevertImg2ImgAccumBoostIfPreRequestFailed();
	            _activeRequestCrtn = null;
	            yield break;
	        }

	        //for inpaint to apply itself, etc. (or to avoid checker pattern if had No-Color Mask)
	        Objects_Renderer_MGR.instance?.ReRenderAll_soon();

	        UserCameras_Permissions.Force_KeepRenderingCameras(true);
	        try {
	            for(int i=0; i<3; ++i){
	                if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }
	                yield return null;
	            }//give time for cameras to render the target textures.
	            if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }

	            // Apply visible layer paint to mesh; img2img_GetTextures_andFill calls EnsureInpaint again right before capture.
	            if (!isMakingBackgrounds && Objects_Renderer_MGR.instance != null)
	                Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
	            // Wait until after rendering so OnUpdate/ProcessMeshes cannot clear accumulation after EnsureInpaint but before ReadPixels.
	            yield return new WaitForEndOfFrame();
	            if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }

	            GenerationData_Kind genData_kind = isMakingBackgrounds? GenerationData_Kind.SD_Backgrounds 
	                                                                   : GenerationData_Kind.SD_ProjTextures;
	            SD_img2img_payload payload;
	            SD_GenRequestArgs_byproducts intermediates;
	            _payload_maker.Create_img2img_payload(isMakingBackgrounds, out payload, out intermediates);

	            if (_cancelRequested){
	                intermediates?.Dispose();
	                AbortPrepAfterCancel();
	                yield break;
	            }

	            // Empty init_images crashes / no-ops Neo img2img (e.g. ContentCam capture failed after Klein force).
	            bool structureFailed = intermediates != null && intermediates.kleinStructureAttachFailed;
	            if (payload == null
	                || payload.init_images == null
	                || payload.init_images.Length == 0
	                || string.IsNullOrEmpty(payload.init_images[0])
	                || structureFailed){
	                intermediates?.Dispose();
	                _finalPreparations_beforeGen = false;
	                _isGeneratingWhat = Generate_RequestingWhat.nothing;
	                if (SceneResolution_MGR.LastImg2imgWillAppliedPrep)
		                SceneResolution_MGR.RevertImg2ImgAccumBoostIfPreRequestFailed();
	                if (Viewport_StatusText.instance != null && !structureFailed)
	                    Viewport_StatusText.instance.ShowStatusText(
	                        "img2img aborted: missing init image (ContentCam/CustomFile capture failed).",
	                        false, 5f, false);
	                _activeRequestCrtn = null;
	                yield break;
	            }

	            _generate_sender.Send_GenerateRequest(payload, OnProgressResponse, OnGeneratedResult);

	            _latestGenData = GenData2D_Maker.make_img2img(payload, intermediates, genData_kind);

	            Finalize_GenerationRequest( payload.width,  payload.height,  payload.n_iter,  
	                                        payload.batch_size, "img2img" );

	        RememberSentVals_forDebug(intermediates,  isImg2Img:true,  (InpaintingFill)payload.inpainting_fill );
	        _activeRequestCrtn = null;
	        onRequested?.Invoke();
	        } finally {
	            UserCameras_Permissions.Force_KeepRenderingCameras(false);
	        }
	    }


	    IEnumerator Upscale_img2extra_crtn( float upscaleBy, GenData2D fromGen_canBeNull=null, 
	                                        Texture2D tex2D=null,  Action onRequested=null ){
	        if(!Start_GenerationRequest(Generate_RequestingWhat.upscale)){
	            if (SceneResolution_MGR.LastImg2imgWillAppliedPrep)
		            SceneResolution_MGR.RevertImg2ImgAccumBoostIfPreRequestFailed();
	            _activeRequestCrtn = null;
	            yield break;
	        }

	        if(fromGen_canBeNull == null){ //genData not provided, render the scene to submit the ViewTexture for upscale.
	            UserCameras_Permissions.Force_KeepRenderingCameras(true);
	            try {
	            if (Objects_Renderer_MGR.instance != null)
	                Objects_Renderer_MGR.instance?.ReRenderAll_soon();
	            for(int i=0; i<3; ++i){
	                if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }
	                yield return null;
	            }//give time for cameras to render the target textures.
	            if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }
	            if (Objects_Renderer_MGR.instance != null)
	                Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
	            yield return new WaitForEndOfFrame();//same ordering as img2img: avoid capture before end-of-frame render after layer sync
	            if (_cancelRequested){ AbortPrepAfterCancel(); yield break; }
	            } finally {
	                // Always unlock — success path previously leaked Force_KeepRenderingCameras(true).
	                UserCameras_Permissions.Force_KeepRenderingCameras(false);
	            }
	        }

	        SD_GenRequestArgs_byproducts intermediates = null;
	        SD_img2extra_payload payload = null;

	        if(fromGen_canBeNull != null){
	            _payload_maker.Create_upscale_payload(tex2D, upscaleBy, out payload);
	        }else{
	            _payload_maker.Create_upscale_payload(upscaleBy, out payload, out intermediates);
	        }

	        if (_cancelRequested){
	            intermediates?.Dispose();
	            AbortPrepAfterCancel();
	            yield break;
	        }

	        _generate_sender.Send_GenerateRequest(payload, OnProgressResponse, OnGeneratedResult);
        
	        _latestGenData = GenData2D_Maker.make_img2extra(payload, fromGen_canBeNull, intermediates);

	        Finalize_GenerationRequest( payload.rslt_imageWidths,  payload.rslt_imageHeights, 
	                                    1, 1, "img2extra", noSdxlAdvice:true );

	        RememberSentVals_forDebug(null,  isImg2Img:true,  InpaintingFill.Original);//to reset previous values.
	        _activeRequestCrtn = null;
	        onRequested?.Invoke();
	    }

    

	    //for example, detecting depth from art image (zoedepth)
	    IEnumerator Submit_CtrlDetect_crtn( SD_ControlnetDetect_payload payload, 
	                                        Action<SD_ControlnetDetect_Response> onDetected ){
        
	        if(_isGeneratingWhat!=Generate_RequestingWhat.nothing){
	            _activeRequestCrtn = null;
	            yield break;
	        }
	        _cancelRequested = false;
	        _isGeneratingWhat = Generate_RequestingWhat.ctrlnetDetect;

	        bool finishedUi = false;
	        try {
	            _generate_sender.Send_GenerateRequest(payload, OnDone);

	            Finalize_GenerationRequest( payload.width_spz, payload.height_spz,
	                                        1, 1, "ctrlDetect", noSdxlAdvice: true);

	            bool isError = false;
	            SD_ControlnetDetect_Response response = null;

	            void OnDone(UnityWebRequest req){
	                if (_cancelRequested){
	                    isError = true;
	                    response = null;
	                    return;
	                }
	                if(Finish_if_ResultError(req)){
	                    isError = true;
	                    response = null;
	                    return;
	                }
	                try {
	                    string json = req.downloadHandler != null ? req.downloadHandler.text : "";
	                    var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	                    response = JsonConvert.DeserializeObject<SD_ControlnetDetect_Response>(json, settings);
	                    if (response == null)
	                        isError = true;
	                } catch (Exception ex) {
	                    Debug.LogWarning("[SD_GenRequests_Helper] ctrlDetect parse failed: " + ex.Message);
	                    isError = true;
	                    response = null;
	                }
	            }

	            while(response == null && !isError && !_cancelRequested){ yield return null;  }

	            if (_cancelRequested){
	                yield break; // OnStop already finished UI
	            }

	            GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled: isError);
	            finishedUi = true;
	            if (!isError)
	                onDetected?.Invoke(response);
	        } finally {
	            // Always reopen Gen Art / hub — parse hang or StopCoroutine must not leave ctrlnetDetect sticky.
	            _isGeneratingWhat = Generate_RequestingWhat.nothing;
	            _activeRequestCrtn = null;
	            if (!finishedUi && !_cancelRequested)
	                GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled: true);
	        }
	    }


	    void RememberSentVals_forDebug( SD_GenRequestArgs_byproducts intermediates, 
	                                    bool isImg2Img,  InpaintingFill fill = InpaintingFill.Original ){
	        _latestDepthTex_sent   = intermediates?.depth_disposableTex;
	        _latestScreenMask_sent = intermediates?.screenSpaceMask_WE_disposableTex;
	        _latestUsualView_used  = intermediates?.usualView_disposableTexture;
	        _latest_isImg2img = isImg2Img;
	        _latest_Fill = fill;
	    }


    
	    bool Start_GenerationRequest( Generate_RequestingWhat what ){
	        if(_isGeneratingWhat!=Generate_RequestingWhat.nothing){ return false; }//still waiting for a previous request to complete.
	        _isGeneratingWhat = what;
	        _finalPreparations_beforeGen = true;

	        var inp = SD_InputPanel_UI.instance;

	        // GetSelectedModel_name returns "" for disconnect placeholders (never null).
	        if (string.IsNullOrEmpty(inp.models.selectedModel_name)){
	            _isGeneratingWhat = Generate_RequestingWhat.nothing;
	            _finalPreparations_beforeGen = false;
	            // Socket may already be up while dropdown still shows only the disconnect placeholder.
	            // OG health: black-window StatusText when truly disconnected; DisplayText when socket is up but no checkpoint yet.
	            string msg = !Connection_MGR.is_sd_connected
	                ? SdDisconnectPlaceholder.StatusText
	                : !inp.models.HasValidModels
	                    ? SdDisconnectPlaceholder.DisplayText
	                    : "No Models detected in the Input panel. Enter PlayMode only after WebUI was launched";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 10, progressVisibility:false);
	            return false;//no models available. User should try clicking the refresh button next to dropdown.
	        }
	        if(inp.samplers?.value == null){
	            _isGeneratingWhat = Generate_RequestingWhat.nothing;
	            _finalPreparations_beforeGen = false;
	            Viewport_StatusText.instance.ShowStatusText("No Samplers detected in the Input panel", false, 10, progressVisibility:false);
	            return false; //no samplers available.
	        }
	        return true;
	    }


	    //requestCategory: txt2img, img2img, etc.
	    void Finalize_GenerationRequest( int width, int height, int n_iter, int batch_size, string requestCategory, 
	                                     bool noSdxlAdvice=false ){
	        string statusMsg = $"Generating {width} x {height} images <b>({requestCategory})</b>.  Num: {n_iter}x{batch_size}";
	        try {
	            if (!noSdxlAdvice){
	                apppend_sdxl_ctrlnet_advice_maybe( ref statusMsg );
	                append_sdxl_size_advice_maybe(ref statusMsg );
	            }
	        } catch (System.Exception e) {
	            UnityEngine.Debug.LogWarning("[SD_GenRequests_Helper] XL advice skipped: " + e.Message);
	        }
	        Viewport_StatusText.instance.ReportProgress(0);
	        Viewport_StatusText.instance.ShowStatusText(statusMsg, false, 999999, progressVisibility:true );

	        GenerateButtons_UI.OnConfirmed_StartedGenerate();

	        _finalPreparations_beforeGen = false;
	        _isGeneratingWhat = _isGeneratingWhat;//kept the SAME (waiting for results).
	    }



	    //check the names of the selected models.
	    //If base input model contains XL in its name, then we want all the Depth or Normal ctrlNetUnits to have XL in their name as well.
	    void apppend_sdxl_ctrlnet_advice_maybe(ref string currMsg_){
	        if (SD_InputPanel_UI.instance == null || SD_InputPanel_UI.instance.models == null) return;
	        if (SD_ControlNetsList_UI.instance == null) return;
	        string sd_model = SD_InputPanel_UI.instance.models.selectedModel_name ?? "";
	        List<string> ctrl_models = SD_ControlNetsList_UI.instance.curentModels_of_DepthOrNormal_units();
	        if (ctrl_models == null) return;

	        bool sd_likely_sdxl = sd_model.ToLower().Contains("xl");
	        bool mismatch=false;
        
	        for(int i=0; i<ctrl_models.Count; ++i){
	            string unitModelName = ctrl_models[i] ?? "";
	            bool ok =  sd_likely_sdxl == unitModelName.ToLower().Contains("xl");
	            if(ok){ continue; }
	            mismatch=true; 
	            break;
	        }
	        if(!mismatch){ return; }
	        currMsg_ += sd_likely_sdxl? "\nCareful: your Input Model name mentions <b>XL</b>, but some CTRL Nets don't."
	                                   :"\nCareful: your Input Model name doesn't mention <b>XL</b>, but some CTRL Nets do.";
	    }


	    void append_sdxl_size_advice_maybe(ref string currMsg_){
	        var inp = SD_InputPanel_UI.instance;
	        if (inp == null || inp.models == null) return;
	        string sd_model = inp.models.selectedModel_name ?? "";
	        bool sd_likely_sdxl = sd_model.ToLower().Contains("xl");
	        if(!sd_likely_sdxl){ return; }
	        if(inp.width > 768 || inp.height > 768){ return; }
	        currMsg_ += "\nCareful: your Input Model name mentions <b>XL</b>, but Width and Height is less than 1024.";
	    }


	    void OnProgressResponse( UnityWebRequest request ){

	        Objects_Renderer_MGR.instance?.ReRenderAll_soon();

	        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError){
	            Viewport_StatusText.instance.ReportProgress(0);
	            Viewport_StatusText.instance.ShowStatusText("Error fetching progress: "+request.error,  false,  5,  progressVisibility:false );
	            return;
	        }
	        // Deserialize the JSON response to the ProgressResponse class
	        // Use class-type information, to support inheritance of objects:
	        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, };
	        SD_Generate_ProgressResponse progressResponse 
	            = JsonConvert.DeserializeObject<SD_Generate_ProgressResponse>(request.downloadHandler.text, settings);

	        if(progressResponse==null){ return; }//ComfyUI doesn't return progress (Forge and A1111 would).


	        float progressTotal = Mathf.Clamp01(progressResponse.progress);
	        Viewport_StatusText.instance.ReportProgress( progressTotal );

	        int percent = Mathf.RoundToInt(progressTotal * 100f);
	        int stepNow = progressResponse.state != null ? progressResponse.state.sampling_step : 0;
	        int stepMax = progressResponse.state != null ? progressResponse.state.sampling_steps : 0;
	        int etaSec = Mathf.Max(0, Mathf.RoundToInt(progressResponse.eta_relative));

	        string progressMsg = stepMax > 0
	            ? $"Generating... {percent}% ({stepNow}/{stepMax})  ETA: {etaSec}s"
	            : $"Generating... {percent}%  ETA: {etaSec}s";

	        bool isTextETA = false;
	        Viewport_StatusText.instance.ShowStatusText(progressMsg, isTextETA, textVisibleDur:999999, progressVisibility:true );

	        // Current preview frame can be empty for some backends at parts of generation.
	        // Keep ETA/progress UI updates above, and only gate preview texture updates here.
	        if(progressResponse.current_image == null || progressResponse.current_image==""){ return; }
	        if (progressResponse.state == null) { return; }

	        //using ? in case SD had exception
	        _latestGenData?.Update_PendingImages( progressResponse.state.job_no,  progressResponse.current_image ); 
	    }



	    void OnGeneratedResult( UnityWebRequest result){

	        // Late HTTP after cancel must not bake Gen Art or report a successful finish.
	        if (_cancelRequested){
	            _latestGenData?.Complete_PendingImages(null);
	            OnFinishTheInterrupt();
	            return;
	        }

	        if(Finish_if_ResultError(result)){
	            GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	            return;
	        }

	        // Use class-type information, to support inheritance of objects:
	        string json = result.downloadHandler.text;
	        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, };
	        SD_txt2imgResponse response = JsonConvert.DeserializeObject<SD_txt2imgResponse>(json, settings);

	        // Klein: reject depth-plate false success before projection bake / success finish.
	        // Complete_PendingImages(null) is a no-op — dispose pending GenData like interrupt.
	        if (RejectKleinDepthLikeResult(response)){
	            GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	            if (GenData2D_Archive.instance != null)
	                GenData2D_Archive.instance.OnTerminatedGeneration(_latestGenData);
	            _latestGenData = null;
	            _isGeneratingWhat = Generate_RequestingWhat.nothing;
	            _generationCooldownUntil = Time.unscaledTime + _generationCooldown;
	            if (Objects_Renderer_MGR.instance != null)
	                Objects_Renderer_MGR.instance?.ReRenderAll_soon();
	            Viewport_StatusText.instance.ShowStatusText(
	                "Klein Gen Art rejected: Neo result looks like depth plate (structure channel, not albedo).",
	                false, 8, progressVisibility:false);
	            return;
	        }

	        GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:false);

	        _latestGenData?.Complete_PendingImages( response.images ); //using ? in case SD had exception

	        // fal FLUX drops negative_prompt — surface honesty when Cloud Inference marked it ignored.
	        if (Connection_MGR.is_cloud_inference
	            && response != null
	            && !string.IsNullOrEmpty(response.info)
	            && response.info.IndexOf("negative_prompt_ignored", StringComparison.Ordinal) >= 0
	            && Viewport_StatusText.instance != null){
	            Viewport_StatusText.instance.ShowStatusText(
	                "Cloud Inference: negative prompt was ignored (FLUX has no negatives).",
	                false, 5f, false);
	        }

	        // Ensure new Gen Art is visible in viewport: clear Solo for all, and ensure this generation's group is not hidden. Then request re-render (and again after 2 frames so projection picks up the new texture).
	        Guid latestGuid = _latestGenData != null ? _latestGenData.total_GUID : default;
	        _latestGenData = null;

	        if (Art2D_IconsUI_List.instance != null){
	            Art2D_IconsUI_List.instance.disable_IsSolo_inAllGroups( anotherGroupRemains_asSolo: false, sendEventsToIcons: true );
	            if (latestGuid != default)
	                Art2D_IconsUI_List.instance.EnsureGenerationVisibleInViewport( latestGuid );
	        }

	        Objects_Renderer_MGR.instance?.ReRenderAll_soon();
	        StartCoroutine( ReRenderAgainAfterFrames( 2 ) );

	        int numGenerations = PlayerPrefs.GetInt("numArtGenerated", 0);
	        string genCompleted_text = numGenerations < 20?  "Done! :)   Go to <b>Art</b> Tab, and right click the icon, to adjust."
	                                                       : "Generation Completed";
	        PlayerPrefs.SetInt("numArtGenerated", numGenerations+1);
	        PlayerPrefs.Save();
	        Viewport_StatusText.instance.ShowStatusText(genCompleted_text, false, 4, progressVisibility:false);

	        _isGeneratingWhat = Generate_RequestingWhat.nothing;
	    }

	    /// <summary>
	    /// When Klein Gen Art used mesh-depth structure, block bake if Neo returned a depth-like plate.
	    /// </summary>
	    bool RejectKleinDepthLikeResult(SD_txt2imgResponse response){
	        if (!StableDiffusion_Hub.IsActiveCheckpointKlein()) return false;
	        if (_latestDepthTex_sent == null) return false;
	        if (response?.images == null || response.images.Length == 0
	            || string.IsNullOrEmpty(response.images[0])) return false;

	        Texture2D result = null;
	        try {
	            string b64 = response.images[0];
	            // Neo/A1111 usually send raw base64; tolerate data-URI if present.
	            if (b64.StartsWith("data:image/", System.StringComparison.OrdinalIgnoreCase)){
	                int comma = b64.IndexOf(',');
	                if (comma >= 0 && comma + 1 < b64.Length) b64 = b64.Substring(comma + 1);
	            }
	            result = TextureTools_SPZ.Base64ToTexture(b64);
	            if (result == null) return false;
	            Texture2D depth = _latestDepthTex_sent as Texture2D;
	            if (depth == null) return false;
	            bool reject = SD_KleinStructureChannel.LooksLikeDepthPlate(result, depth, out float diff01);
	            KleinStructureTrace.Set("similarity_to_depth", diff01);
	            KleinStructureTrace.Set("bake_allowed", !reject);
	            if (reject)
	                KleinStructureTrace.Set("reject_reason", "result_looks_like_depth_plate");
	            return reject;
	        } catch (System.Exception){
	            // Fail open on compare errors — do not strand Gen Art completion.
	            return false;
	        } finally {
	            if (result != null) UnityEngine.Object.Destroy(result);
	        }
	    }


	    bool Finish_if_ResultError( UnityWebRequest result ){
	        string json = "";
	        if(result!=null && result.downloadHandler != null){ 
	            json = result.downloadHandler.text; 
	        }
	        bool err =  result.result==UnityWebRequest.Result.ConnectionError 
	                 || result.result==UnityWebRequest.Result.ProtocolError;

	        if(err || json==""){
	            var jsonLow = json.ToLower();
	            json += jsonLow.Contains("cannot be multiplied") || jsonLow.Contains("server error") ?  
	                        " ..Maybe you are mixing SDXL model with SD 1.5 Controlnet?"
	                        : "";
	            Viewport_StatusText.instance.ShowStatusText("Error: " + json, false, 15, progressVisibility:false);
	            _latestGenData?.Complete_PendingImages( null ); //using ? in case SD had exception
	            _isGeneratingWhat = Generate_RequestingWhat.nothing;
	            return true; 
	        }
        
	        if(json == "{}"){
	            OnFinishTheInterrupt();
	            _latestGenData?.Complete_PendingImages(null); //using ? in case SD had exception
	            _isGeneratingWhat = Generate_RequestingWhat.nothing;
	            return true;
	        }
	        return false; //no errors, all as expected
	    }

    

	    public void OnStopGenerate_Button(){
	        _cancelRequested = true;
	        bool prepOnlyAbort = false;
	        if (_activeRequestCrtn != null){
	            StopCoroutine(_activeRequestCrtn);
	            _activeRequestCrtn = null;
	            // Prep may never have POSTed — clear flags now so DenyWithMessage cannot stick on forever.
	            if (_finalPreparations_beforeGen){
	                AbortPrepAfterCancel();
	                prepOnlyAbort = true;
	            }
	        }
	        ClearStuckInterruptTimer();
	        if (prepOnlyAbort) {
	            // No server job to interrupt — do not arm a 10s FinishTheInterrupt that can kill the next gen.
	            _cancelRequested = false;
	            GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	            Viewport_StatusText.instance.ShowStatusText("Cancelled before request was sent.", false, 3, progressVisibility: false);
	            return;
	        }
	        _generate_sender.Send_StopGenerateRequest(() => {
	            // Interrupt settled (or failed) — finish UI now so Gen Art stays blocked until then.
	            if (_cancelRequested)
	                OnFinishTheInterrupt();
	        });
	        float gracePeriod = 10;//wait at least 10 sec from server. If no response, then our coroutine will perform clean-up.
	        _finishTheInterrupt_ifStuck_crtn = StartCoroutine( FinishTheInterrupt_ifStuck(gracePeriod) );
	        // Do not call OnConfirmed_FinishedGenerate here: that re-enabled Gen Art while the old
	        // job could still be finishing. UI stays busy until OnFinishTheInterrupt.
	        Viewport_StatusText.instance.ShowStatusText("Cancelling the generation...", false, gracePeriod, progressVisibility: false);
	    }


	    IEnumerator ReRenderAgainAfterFrames(int frames){
	        for(int i = 0; i < frames; i++)
	            yield return null;
	        if (Objects_Renderer_MGR.instance != null)
	            Objects_Renderer_MGR.instance?.ReRenderAll_soon();
	    }

	    Coroutine _finishTheInterrupt_ifStuck_crtn = null;
	    void ClearStuckInterruptTimer(){
	        if (_finishTheInterrupt_ifStuck_crtn == null) return;
	        StopCoroutine(_finishTheInterrupt_ifStuck_crtn);
	        _finishTheInterrupt_ifStuck_crtn = null;
	    }
	    IEnumerator FinishTheInterrupt_ifStuck(float graceDelay=10){
	        yield return new WaitForSeconds(graceDelay);
	        _finishTheInterrupt_ifStuck_crtn = null;//set null BEFORE finishInterrupt().
	        // New generate cleared _cancelRequested — do not tear down the live job.
	        if (!_cancelRequested) yield break;
	        OnFinishTheInterrupt();
	    }


	    void AbortPrepAfterCancel(){
	        _finalPreparations_beforeGen = false;
	        _isGeneratingWhat = Generate_RequestingWhat.nothing;
	        UserCameras_Permissions.Force_KeepRenderingCameras(false);
	        if (SceneResolution_MGR.LastImg2imgWillAppliedPrep)
		        SceneResolution_MGR.RevertImg2ImgAccumBoostIfPreRequestFailed();
	        _activeRequestCrtn = null;
	        _generationCooldownUntil = Time.unscaledTime + _generationCooldown;
	    }


	    void OnFinishTheInterrupt(){
	        // Interrupt settled callback and the stuck timer can both fire; only the first cancel
	        // in-flight may finish. Also: never tear down a live gen that cleared _cancelRequested.
	        if (!_cancelRequested)
	            return;
	        if (Objects_Renderer_MGR.instance != null)
	            Objects_Renderer_MGR.instance?.ReRenderAll_soon();

	        ClearStuckInterruptTimer();
	        if (_activeRequestCrtn != null){
	            StopCoroutine(_activeRequestCrtn);
	            _activeRequestCrtn = null;
	        }
	        GenData2D_Archive.instance.OnTerminatedGeneration(_latestGenData);
	        _latestGenData = null;
	        _finalPreparations_beforeGen = false;
	        _isGeneratingWhat = Generate_RequestingWhat.nothing;
	        _cancelRequested = false;
	        _generationCooldownUntil = Time.unscaledTime + _generationCooldown;
	        GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	        Viewport_StatusText.instance.ShowStatusText("Interrupted the generation.", false, 3, progressVisibility: false);
	    }

	}
}//end namespace
