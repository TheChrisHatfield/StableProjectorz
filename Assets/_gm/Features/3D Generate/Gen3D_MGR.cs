using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace spz {

	public class Gen3D_MGR : MonoBehaviour{
	    public static Gen3D_MGR instance { get; private set; } = null;
    
	    string _layout_str_prev = "";

	    // for example, all the sliders, all the toggles etc, that exist right now.
	    List<Gen3D_InputElement_UI> _known_inputs = new List<Gen3D_InputElement_UI>();
	    public bool any_known_inputs => _known_inputs.Count > 0;


	    // We'll frequently query the server about what it can do.
	    // For example "make_meshes_and_tex", "retexture", etc.
	    // If nothing is returned, please asssume "make_meshes_and_tex".
	    static List<string> _supported_operations = new List<string>(){ "make_meshes_and_tex" };
	    public static IReadOnlyList<string> supported_operations => _supported_operations;
	    public static bool isSupports_retexture() => supported_operations.Contains("retexture");
	    public static bool isSupports_retexture_via_masks() => supported_operations.Contains("retexture_via_masks");
	    public static bool isSupports_make_meshes_and_tex() => supported_operations.Contains("make_meshes_and_tex");

	    public static bool isCanStart_make_meshes_and_tex()
	        => isReadyBasic()  &&  instance._known_inputs.All( i=>i.isReady_ForGenerate("make_meshes_and_tex") );

	    public static bool isCanStart_retexture()
	        => isReadyBasic()  &&  instance._known_inputs.All( i=>i.isReady_ForGenerate("retexture") );

	    // True while retexture gathers icon textures asynchronously, before Gen3D_API.isBusy
	    // turns on — without this a second generate could race into that window.
	    static bool _retexturePrepInFlight = false;

	    static bool isReadyBasic(){
	        if(instance == null){ return false; }

	        var sd = StableDiffusion_Hub.instance;
	        if(sd == null){ return false; }//scenes are probably still loading.
	        if(sd._finalPreparations_beforeGen || sd._generating){ return false; }
        
	        if(Connection_MGR.is_3d_connected==false){ return false; }
        
	        if(Gen3D_API.instance == null){ return false; }
	        if(Gen3D_API.instance.IsServerAvailable==false){ return false; }
	        if(Gen3D_API.instance.isBusy){ return false; }
	        if(_retexturePrepInFlight){ return false; }
	        if(ModelsHandler_3D.instance != null && ModelsHandler_3D.instance._isImportingModel){ return false; }

	        return true;
	    }


	    public bool OnImportedImages_DragAndDrop(List<string> files, Vector2Int screenCoord){
	        foreach(Gen3D_InputElement_UI input in _known_inputs){
	            bool consumed = input.OnDragAndDropImages(files, screenCoord);
	            if (consumed){ return true; }
	        }
	        return false;
	    }


	    //values from all the ui-sliders, ui-text-prompts, ui-image-inputs, etc.
	    Dictionary<string,object> gather_all_ui_inputs(){
	        var dict = new Dictionary<string, object>(); //object could be a list, another dictionary, etc.
	        foreach(var input in _known_inputs){
	            string codeName = input.code_name;
	            object data = input.GetValueData();
	            dict.Add( codeName, data );
	        }
	        return dict;
	    }
    
	    void OnButton_Gen3D(){
	        Trigger3DGeneration();
	    }
	    
	    /// <summary>
	    /// Public method to trigger 3D generation (for add-on API)
	    /// </summary>
	    public bool Trigger3DGeneration(){
	        if( !isCanStart_make_meshes_and_tex() ){ return false; }
	        // FastPath / API can race past button gates — refuse while Cancel chrome still owns a job.
	        if (GenerateButtons_UI.isGenerating){ return false; }
	        if (Gen3D_API.instance == null){ return false; }
	        GenerateButtons_UI.OnConfirmed_StartedGenerate();
	        Dictionary<string,object> all_values = gather_all_ui_inputs();
	        all_values.Add("generate_what", "make_meshes_and_tex");

	        var callbacks = new Gen3D_API.GenerationCallbacks(){
	            onProgress = Gen_OnProgress,
	            onError = Gen_OnError,
	            onComplete = Gen_OnComplete,
	            onDataDownloaded = Gen_OnMeshReady,
	            // Mesh Assimp/UDIM import is async — finish UI when Act_onImportComplete fires.
	            suppressAutoComplete = true,
	        };
	        Gen3D_API.instance.StartGeneration(Gen3D_API.GenerateWhat.make_meshes_and_tex, all_values, callbacks);
	        // Honesty: server-down path invokes onError sync and never starts a coroutine — do not report success.
	        return Gen3D_API.instance != null && Gen3D_API.instance.isBusy;
	    }


	    void OnButton_GenRetexture(){
	        if( !isCanStart_retexture() ){ return; }
	        if( ModelsHandler_3D.instance == null ){ return; }

	        Dictionary<string,object> all_values = gather_all_ui_inputs();
	        all_values.Add("mesh_3d", ModelsHandler_3D.instance.Get_3dModel_asBytes(out string mesh_exten));
	        all_values.Add("mesh_extension", mesh_exten);
	        all_values.Add("generate_what", "retexture");

	        if(isSupports_retexture_via_masks()){
	            if( Art2D_IconsUI_List.instance == null ){
	                GenerateButtons_UI.OnConfirmed_StartedGenerate();
	                GenRetexture_Start2( all_values,  include_paintedMask:false,  udim_albedoTextures_NoOwner:null );
	                return;
	            }
	            // Defer StartedGenerate until merge finishes — else MergeIcons refuse leaves UI generating forever.
	            _retexturePrepInFlight = true;
	            Art2D_IconsUI_List.instance.GetTextures_FromAllIcons( 
	                (List<Texture2D> textures) => {
	                    _retexturePrepInFlight = false;
	                    if (textures == null){
	                        Viewport_StatusText.instance?.ShowStatusText(
	                            "Can't retexture: icon merge was refused (save/export busy).", false, 5f, false);
	                        return;
	                    }
	                    GenerateButtons_UI.OnConfirmed_StartedGenerate();
	                    GenRetexture_Start2(all_values,  include_paintedMask:true,  textures);
	                }
	            );
	        }else{
	            GenerateButtons_UI.OnConfirmed_StartedGenerate();
	            GenRetexture_Start2( all_values,  include_paintedMask:false,  udim_albedoTextures_NoOwner:null );
	        }
	    }


	    void GenRetexture_Start2( Dictionary<string,object> all_values, 
	                              bool include_paintedMask,
	                              List<Texture2D> udim_albedoTextures_NoOwner ){
	        if(udim_albedoTextures_NoOwner!=null && udim_albedoTextures_NoOwner.Count > 0){
	            var base64_imgs = new List<string>();
	            foreach(Texture2D tex in udim_albedoTextures_NoOwner){
	                base64_imgs.Add( TextureTools_SPZ.TextureToBase64(tex) );
	            }
	            all_values.Add("albedo_textures_udims", base64_imgs);
	            udim_albedoTextures_NoOwner.ForEach( t=>DestroyImmediate(t) );
	            udim_albedoTextures_NoOwner.Clear();
	        }

	        if (include_paintedMask && Inpaint_MaskPainter.instance != null){
	            RenderUdims painted_renderUdims = Inpaint_MaskPainter.instance.GetLayerCompositeOrFallback();
	            if (painted_renderUdims?.texArray == null){
	                Viewport_StatusText.instance?.ShowStatusText(
	                    "Retexture: no painted mask available; continuing without painted_mask_udims.", false, 4f, false);
	            } else {
	                RenderTexture painted_texArray  = painted_renderUdims.texArray;
	                List<Texture2D> maskTextures = TextureTools_SPZ.TextureArray_to_Texture2DList(painted_texArray);
	                var base64_imgs = new List<string>();
	                foreach(Texture2D tex in maskTextures){
	                    base64_imgs.Add( TextureTools_SPZ.TextureToBase64(tex) );
	                }
	                all_values.Add("painted_mask_udims", base64_imgs);
	                maskTextures.ForEach( t=>DestroyImmediate(t) );
	                maskTextures.Clear();
	            }
	        }

	        // Callers already fired OnConfirmed_StartedGenerate — a bare NRE here would leave
	        // the Cancel chrome generating forever. Unwind honestly instead.
	        if (Gen3D_API.instance == null){
	            GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	            Viewport_StatusText.instance?.ShowStatusText("Retexture: 3D API unavailable.", false, 5f, false);
	            return;
	        }

	        var callbacks = new Gen3D_API.GenerationCallbacks(){
	            onProgress = Gen_OnProgress,
	            onError = Gen_Retexture_OnError,
	            onComplete = Gen_Retexture_OnComplete,
	            onDataDownloaded = Gen_Retexture_OnDataReady,
	        };
	        Gen3D_API.instance.StartGeneration(Gen3D_API.GenerateWhat.retexture, all_values, callbacks);
	    }


	    Action<bool, GameObject> _pendingMeshImportFinish;

	    void ClearPendingMeshImportFinish(){
	        if (_pendingMeshImportFinish == null) return;
	        ModelsHandler_3D.Act_onImportComplete -= _pendingMeshImportFinish;
	        _pendingMeshImportFinish = null;
	    }

	    void Gen_OnCancel(){
	        ClearPendingMeshImportFinish();
	        // Shared cancel bus — interrupt Gen3D when this API owns a live job (incl. final download).
	        var api = Gen3D_API.instance;
	        if (api != null && api.isBusy) {
	            try {
	                api.CancelGeneration();
	            } finally {
	                // Always finish UI even if API throws — otherwise Cancel button stays stuck.
	                GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled: true);
	            }
	            return;
	        }
	        // API destroyed mid-job: still unwind Cancel chrome in 3D mode (do not steal SD cancel).
	        if (api == null
	            && GenerateButtons_UI.isGenerating
	            && DimensionMode_MGR.instance != null
	            && DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d) {
	            GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled: true);
	        }
	    }
	    void Gen_OnProgress(float val){
	        #if UNITY_EDITOR
	        Debug.Log($"3d progress {val}");
	        #endif
	        if (Viewport_StatusText.instance != null)
	            Viewport_StatusText.instance.ReportProgress(val);
	    }


	//MESH GENERATION CALLBACKS
	    void Gen_OnError(string msg){
	        ClearPendingMeshImportFinish();
	        GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	        Debug.Log("Error: " + msg);
	        Viewport_StatusText.instance?.ShowStatusText("3D generation failed: " + msg, false, 6f, false);
	    }

	    void Gen_OnComplete(){
	        ClearPendingMeshImportFinish();
	        GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:false);
	        Debug.Log("3D generation complete");
	    }

	    void Gen_OnMeshReady(byte[] bytes){
	        // Throw instead of silent return — Gen3D_API converts this into onError so the UI
	        // does not report a successful generation with no mesh imported.
	        if (bytes == null || bytes.Length == 0){
	            throw new InvalidOperationException("server returned an empty mesh");
	        }
	        if (ModelsHandler_3D.instance == null){
	            throw new InvalidOperationException("ModelsHandler_3D unavailable for import");
	        }
	        string tempPath = Path.Combine(Application.temporaryCachePath, $"mesh_trellis.glb");
	        if (File.Exists(tempPath)){  File.Delete(tempPath); }// Clean up temp file
	        File.WriteAllBytes(tempPath, bytes);

	        ClearPendingMeshImportFinish();
	        _pendingMeshImportFinish = (ok, _) => {
	            ClearPendingMeshImportFinish();
	            if (ok) Gen_OnComplete();
	            else Gen_OnError("downloaded mesh could not be imported");
	        };
	        ModelsHandler_3D.Act_onImportComplete += _pendingMeshImportFinish;
	        if (!ModelsHandler_3D.instance.TryImportModelFromFile(tempPath)) {
	            ClearPendingMeshImportFinish();
	            throw new InvalidOperationException("could not start mesh import (busy or invalid file)");
	        }
	    }


	 //RETEXTURING CALLBACKS
	    void Gen_Retexture_OnError(string msg){
	        GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:true);
	        Debug.Log("Retexture Error: " + msg);
	        Viewport_StatusText.instance?.ShowStatusText("3D retexture failed: " + msg, false, 6f, false);
	    }

	    void Gen_Retexture_OnComplete(){
	        GenerateButtons_UI.OnConfirmed_FinishedGenerate(canceled:false);
	        Debug.Log("Retexture complete");
	    }


	    //it will be a dictionary of texture-lists or parameters like booleans.  Might contain:
	    // "albedo_tex_list": base64_images_list
	    // "normals_tex_list": base64_images_list
	    // "specular_tex_list": base64_images_list
	    // "metallic_tex_list": base64_images_list
	    // "ao_tex_list": base64_images_list//ambient occlusion 
	    // bool myOtherParam;//etc.
	    void Gen_Retexture_OnDataReady(byte[] bytes){
	        try {
	            string json = System.Text.Encoding.UTF8.GetString(bytes);
	            var responseData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
        
	            Dictionary<string, List<Texture2D>> textures_dict = new Dictionary<string, List<Texture2D>>();
        
	            foreach (var kvp in responseData) {
	                bool is_tex_list = kvp.Key.ToLower().Contains("tex_list");
                
	                if (is_tex_list  &&  kvp.Value is JArray textureArray){
	                    //if entry is a texture json-list, create a list of textures:
	                    textures_dict[kvp.Key] = new List<Texture2D>();
	                    foreach (var item in textureArray) {
	                        string base64 = item.ToString();
	                        Texture2D texture = TextureTools_SPZ.Base64ToTexture(base64);
	                        if (texture != null) {
	                            textures_dict[kvp.Key].Add(texture);
	                        } 
	                    }
	                }
	                else if (kvp.Value is bool boolValue){// Handle boolean value
	                    Debug.Log($"Received boolean for {kvp.Key}: {boolValue}");
	                }
	            }//end for each kvp

	            foreach(var kvp in textures_dict){
	                List<Texture2D> textures_list = kvp.Value;
	                if (Art2D_IconsUI_List.instance == null){
	                    throw new InvalidOperationException("Art2D icon list unavailable — retexture textures dropped");
	                }
	                Art2D_IconsUI_List.instance.ImportCustomImages(GenerationData_Kind.UvTextures_FromFile, textures_list);
	            }
	        }
	        catch (Exception e){
	            Debug.LogError($"Error processing texture data: {e.Message}\nStack trace: {e.StackTrace}");
	            // Rethrow so Gen3D_API reports onError instead of onComplete claiming success
	            // while every downloaded texture was discarded.
	            throw;
	        }
	    }


	    // Continiously queries the server for what things the server can do.
	    // For example, can it generate meshes or can it re-texture, etc.
	    IEnumerator GetSupportedOperations_Looped_crtn() {
	        while (true){
	            if(Connection_MGR.is_3d_connected){
	                Gen3D_API.instance?.GetSupportedOperations( onSuccess:OnSupportedOperations_Ready,  onError:(error)=>{} );
	            }
	            float secToWait = Settings_MGR.instance.get_layout_askServerOften() ? 0.1f : 3;
	            yield return new WaitForSeconds(secToWait);
	        }
	    }

	    void OnSupportedOperations_Ready(List<string> opers){
	        if(opers == null || opers.Count==0){
	            opers = new List<string>{"make_meshes_and_tex"};//at least this.
	        }
	        _supported_operations = opers;
	    }



	    // Continiously queries the server for UI layout.
	    // If the response changes, we'll re-create the ui elements.
	    IEnumerator GetLayoutUI_Looped_crtn(){
	        while (true){
	            if (Connection_MGR.is_3d_connected){ 
	                string url = $"{Connection_MGR.GEN3D_URL}/download/spz-ui-layout/generation-3d-panel";
	                using (UnityWebRequest www = UnityWebRequest.Get(url))
	                {
	                    yield return www.SendWebRequest();
	                    if (www.result == UnityWebRequest.Result.Success){
	                        On_UI_LayoutReady(www.downloadHandler.text);
	                    }else{
	                        //not connected or a network error. Check if the endpoint exists and VPN is off.
	                    }
	                }
	            }
	            float secToWait = 3;
	            if(Settings_MGR.instance!=null){ 
	                secToWait = Settings_MGR.instance.get_layout_askServerOften() ? 0.1f : 3;
	            }
	            yield return new WaitForSeconds( secToWait );
	        }//end while
	    }


	    void On_UI_LayoutReady(string layout_str){
	        if(layout_str == _layout_str_prev){
	            return; //nothing has changed
	        }
	        if(string.IsNullOrEmpty(layout_str)){
	            //nothing received, maitain the old layout elements for now.
	            //It helps to keep the imported images etc, for comfort.
	            return; 
	        }
	        // NRE here propagates into GetLayoutUI_Looped_crtn and kills the poll loop for the
	        // whole session. Skip (without recording _layout_str_prev) so a later pass retries.
	        if(Gen3D_InputPanelBuilder_UI.instance == null){ return; }
	        _known_inputs = Gen3D_InputPanelBuilder_UI.instance.MakeLayout_from_text(layout_str);
	        _layout_str_prev = layout_str;
	    }


	    void OnOpenCatalogue(){
	        if(Gen3D_Catalogue_UI.instance == null){ return; }
	        Gen3D_Catalogue_UI.instance.Show();
	    }


	    void Update(){
	        bool is3D = DimensionMode_MGR.instance?._dimensionMode == DimensionMode.dim_gen_3d;

	        if(!is3D){ return; }
	        if(KeyMousePenInput.isSomeInputFieldActive()){ return; }//typing in text field, etc
        
	        //see if user used a shortcut:
	        bool do_genMeshesAndTex = isCanStart_make_meshes_and_tex() &&
	                                  KeyMousePenInput.isKey_CtrlOrCommand_pressed() &&
	                                  Input.GetKeyDown(KeyCode.G);

	        bool do_retexture = isCanStart_retexture() &&
	                            KeyMousePenInput.isKey_Shift_pressed() &&
	                            Input.GetKeyDown(KeyCode.G);
	        if(do_genMeshesAndTex){  
	            OnButton_Gen3D();
	            return;
	        }
	        if(do_retexture){
	            OnButton_GenRetexture();
	            return;
	        }
	    }


	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        GenerateButtons_UI.OnGenerate3D_Button += OnButton_Gen3D;
	        GenerateButtons_UI.OnGenerate3D_retexture_Button += OnButton_GenRetexture;
	        GenerateButtons_UI.OnCancelGenerationButton += Gen_OnCancel;

	        StaticEvents.SubscribeUnique("Gen3D_Catalogue:Open", OnOpenCatalogue );
	    }

	    void Start(){
	        StartCoroutine( GetLayoutUI_Looped_crtn() );
	        StartCoroutine( GetSupportedOperations_Looped_crtn() );
	    }

	    void OnDestroy(){
	        GenerateButtons_UI.OnGenerate3D_Button -= OnButton_Gen3D;
	        GenerateButtons_UI.OnGenerate3D_retexture_Button -= OnButton_GenRetexture;
	        GenerateButtons_UI.OnCancelGenerationButton -= Gen_OnCancel;

	        StaticEvents.Unsubscribe("Gen3D_Catalogue:Open", OnOpenCatalogue);
	    }
	}
}//end namespace
