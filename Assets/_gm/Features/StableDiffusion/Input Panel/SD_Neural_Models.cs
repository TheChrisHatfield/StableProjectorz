using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;


namespace spz {

	[Serializable]
	public class SDModelItem{
	    public string title;
	    public string model_name;
	    public string hash;
	    public string sha256;
	    public string filename;
	    public string config = null;
	    // Add other fields as needed
	}

	[Serializable]
	public class SDModelsList{
	    public SDModelItem[] models;
	    public static SDModelsList CreateFromJSON(string jsonString){
	        // Use class-type information, to support inheritance of objects:
	        var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	        return JsonConvert.DeserializeObject<SDModelsList>("{\"models\":" + jsonString + "}", settings);
	    }
	}


	public class SD_Neural_Models : MonoBehaviour{
	    public static SD_Neural_Models instance { get; private set; } = null;

	    [SerializeField] TMP_Dropdown _modelsDropdown;
	    [SerializeField] Animation _isLoading_anim;
	    [Space(10)]
	    [SerializeField] SlideOut_Widget_UI _getModes_slideOut;
	    [SerializeField] Button _loadFromDiskButton;
	    bool _loadFromDiskWired;

	    public string selectedModel_name => GetSelectedModel_name();
	    public static Action<SDModelsList> Act_ListOfModelsReceived { get; set; } = null;

    
	    // Can be invoked for performance reasons, for example before the Delighting shadow-removal algorithm.
	    // This is to save VRAM.  Only invoke if needed, because loading the SD model back - takes time.
	    // Model will be automatically loaded into VRAM during next Gen Art.
	    public void UnloadModelCheckpoint(){
	        StartCoroutine( UnloadModelCheckpoint_crtn() );
	    }


	    [Serializable]
	    public class SD_UnloadModelPacket{
	        public string sd_model_checkpoint; //neural net used for Stable Diffusion. For example "ema-pruned-1.5"
	    }

	    IEnumerator UnloadModelCheckpoint_crtn(){
	        UnityWebRequest request = new UnityWebRequest(Connection_MGR.A1111_SD_API_URL + "/unload-checkpoint", "POST");
	        try {
	            var packet = new SD_UnloadModelPacket(){
	                sd_model_checkpoint = selectedModel_name,
	            };
	            string json = JsonUtility.ToJson(packet);
	            byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(json);
	            request.uploadHandler = new UploadHandlerRaw(jsonToSend);
	            request.downloadHandler = new DownloadHandlerBuffer();

	            yield return request.SendWebRequest();

	            bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	            isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	            Debug.Log("UnloadModelCheckpoint() result: " + request.result);
	        } finally {
	            request.Dispose();
	        }
	    }


	    bool _isFetchingModels = false;
	    /// <summary>True when dropdown has a real checkpoint (not empty/none/disconnect placeholder).</summary>
	    public bool HasValidModels => knowsValidModels();
	    bool knowsValidModels(){
	        var options = _modelsDropdown.options;
	        foreach(var o in options){
	            if (string.IsNullOrEmpty(o.text)) { continue; }
	            string txtLower = o.text.ToLower();
	            if (txtLower=="none") { continue; }
	            if (SdDisconnectPlaceholder.IsPlaceholder(txtLower)){ continue; }
	            return true;
	        }
	        return false;
	    }


	    // We remember the time when we pick an entry in the dropdown.
	    // This helps to prevent overwriting our selection (after a current model query that we send periodically).
	    // If we already sent a command to pick a model recently.
	    float _timeOf_SelectedTheModel = -9999;

	    //if we loaded from a save-file, we migth want to select a model.
	    //If we are not connected, this model won't be in the dropdown.
	    //But we can try to find it as soon a we connect next time;
	    string _preferedModelName_viaLoad = "";


	    public void Save(SD_GenSettingsInput_UI settingsSL){
	        settingsSL.neural_models = new SD_InputNeuralModels_SL();
	        settingsSL.neural_models.selectedModel_name = selectedModel_name;
	    }

	    public void Load(SD_GenSettingsInput_UI from_this){
	        SD_InputNeuralModels_SL m = from_this.neural_models;
	        int newIndex = FindIndex_inDropdown( m.selectedModel_name );
	        if(newIndex == -1){
	            _preferedModelName_viaLoad = m.selectedModel_name;
	            return;
	        }
	        _modelsDropdown.value = newIndex;//will invoke our callback, and send JSON to SD.
	    }


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	    }

	    void Start(){
	        _modelsDropdown.onValueChanged.AddListener( OnDropdown_EntryPicked );
	        StartCoroutine( EnsureFetchStarted_crtn() );
	        SD_Options_Fetcher.Act_onOptionsRetrieved += OnOptionsReceived;
	        SD_Options_Fetcher.Act_onWillSendOptions_AmmendPlz += OnWillSendOptions_AmmendPlz;
	        SD_Options_Fetcher.Act_OnSendOptions_done += OnSendOptions_done;
	        EnsureLoadFromDiskButton();
	    }

	    void OnDestroy(){
	        SD_Options_Fetcher.Act_onOptionsRetrieved -= OnOptionsReceived;
	        SD_Options_Fetcher.Act_onWillSendOptions_AmmendPlz -= OnWillSendOptions_AmmendPlz;
	        SD_Options_Fetcher.Act_OnSendOptions_done -= OnSendOptions_done;
	        if (ReferenceEquals(instance, this))
	            instance = null;
	    }

	    void EnsureLoadFromDiskButton(){
	        if (_loadFromDiskButton == null)
	            _loadFromDiskWired = false;
	        if (_loadFromDiskWired && _loadFromDiskButton != null) return;
	        if (_getModes_slideOut == null) return;
	        if (_loadFromDiskButton == null)
	            _loadFromDiskButton = SD_WeightFileImport.EnsureFromDiskButton(
	                _getModes_slideOut, SD_WeightFileImport.Kind.Checkpoint, BrowseAndImportCheckpoint);
	        if (_loadFromDiskButton == null) return;
	        _loadFromDiskButton.onClick.RemoveListener(BrowseAndImportCheckpoint);
	        _loadFromDiskButton.onClick.AddListener(BrowseAndImportCheckpoint);
	        _loadFromDiskWired = true;
	    }

	    public void BrowseAndImportCheckpoint(){
	        SD_WeightFileImport.BrowseAndImport(SD_WeightFileImport.Kind.Checkpoint);
	    }

	    /// <summary>Kick an immediate /sd-models poll (e.g. after local weight copy).</summary>
	    public void RequestImmediateModelsFetch(){
	        if (_isFetchingModels) return;
	        if (Coroutines_MGR.instance == null) return;
	        Coroutines_MGR.instance.StartCoroutine(GetModels_crtn());
	    }

	    /// <summary>Prefer this checkpoint after list refresh (also selects immediately if already listed).</summary>
	    public void PreferModelWhenAvailable(string name){
	        if (string.IsNullOrEmpty(name)) return;
	        if (_modelsDropdown == null) {
	            _preferedModelName_viaLoad = NormalizeCheckpointPrefer(name);
	            return;
	        }
	        string want = NormalizeCheckpointPrefer(name);
	        _preferedModelName_viaLoad = want;
	        // Exact stem match only — IndexOf would pick flux-2… when wanting "flux".
	        int ix = _modelsDropdown.options.FindIndex(opt =>
	            opt.text != null
	            && string.Equals(NormalizeCheckpointPrefer(opt.text), want, StringComparison.OrdinalIgnoreCase));
	        if (ix >= 0) {
	            _preferedModelName_viaLoad = "";
	            if (_modelsDropdown.value == ix) {
	                // Same entry (e.g. overwrite of active checkpoint) — value= won't fire onValueChanged.
	                _timeOf_SelectedTheModel = Time.time;
	                EnsureKleinSdVaeSelected(GetSelectedModel_name());
	                SD_Options_Fetcher.instance?.SubmitOptions_Asap();
	            } else {
	                _modelsDropdown.value = ix;
	            }
	            _modelsDropdown.RefreshShownValue();
	        }
	    }

	    static string NormalizeCheckpointPrefer(string name){
	        if (string.IsNullOrEmpty(name)) return "";
	        string n = name.Trim().Replace('\\', '/');
	        string exten = Path.GetExtension(n);
	        if (exten.IndexOf(".safetensors", StringComparison.OrdinalIgnoreCase) >= 0
	            || exten.IndexOf(".ckpt", StringComparison.OrdinalIgnoreCase) >= 0
	            || exten.IndexOf(".pt", StringComparison.OrdinalIgnoreCase) >= 0
	            || exten.IndexOf(".pth", StringComparison.OrdinalIgnoreCase) >= 0) {
	            string dir = Path.GetDirectoryName(n)?.Replace('\\', '/') ?? "";
	            string stem = Path.GetFileNameWithoutExtension(n);
	            return string.IsNullOrEmpty(dir) ? stem : (dir + "/" + stem);
	        }
	        return n;
	    }

	    /// <summary>True when screen point is over Model dropdown, visible slide-out, or this panel.</summary>
	    public bool ScreenPointHitsOwnership(Vector2 screenPoint){
	        Camera cam = UiCameraFor(transform);
	        if (transform is RectTransform root
	            && RectTransformUtility.RectangleContainsScreenPoint(root, screenPoint, cam))
	            return true;
	        if (_modelsDropdown != null
	            && _modelsDropdown.transform is RectTransform dd
	            && RectTransformUtility.RectangleContainsScreenPoint(dd, screenPoint, cam))
	            return true;
	        if (_getModes_slideOut != null && _getModes_slideOut.isShowing
	            && _getModes_slideOut.transform is RectTransform slide
	            && RectTransformUtility.RectangleContainsScreenPoint(slide, screenPoint, cam))
	            return true;
	        return false;
	    }

	    static Camera UiCameraFor(Transform t){
	        var canvas = t != null ? t.GetComponentInParent<Canvas>() : null;
	        if (canvas == null) return null;
	        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
	        return canvas.worldCamera;
	    }

	    IEnumerator EnsureFetchStarted_crtn(){
	        while (Coroutines_MGR.instance == null)
	            yield return null;
	        Coroutines_MGR.instance.StartCoroutine( FetchContiniously_crtn() );
	    }

	    void Update(){
	        if (!_loadFromDiskWired)
	            EnsureLoadFromDiskButton();
	        if (_getModes_slideOut == null || _modelsDropdown == null) return;
	        _getModes_slideOut._dontAutoHide = true;
	        _getModes_slideOut.Toggle_if_Different( _modelsDropdown.IsExpanded );
	    }


	    IEnumerator FetchContiniously_crtn(){
	        while (true){
	            if (!Connection_MGR.is_sd_connected){ 
	                yield return new WaitForSeconds(0.211f); 
	                continue; 
	            }
	            if (!_isFetchingModels){
	                if (Coroutines_MGR.instance == null){
	                    yield return null;
	                    continue;
	                }
	                yield return Coroutines_MGR.instance.StartCoroutine( GetModels_crtn() );
	            }
	            dropdown_LoadedSavedModel_maybe();
	            yield return new WaitForSeconds(3f);
	        }
	    }


	    void OnOptionsReceived(SD_OptionsPacket options){
	        //to avoid overwriting with potentially old value if I recently sent "Set Current Model" to SD:
	        float elapsedSinceSendModel =  Time.time - _timeOf_SelectedTheModel;
	        if(elapsedSinceSendModel < 10){
	            return; 
	        }
        
	        string activeModel_name = StripExtensions(options.sd_model_checkpoint); 

	        int modelIndex = FindIndex_inDropdown(activeModel_name);
	        if(modelIndex >= 0){ 
	            _modelsDropdown.SetValueWithoutNotify(modelIndex);
	            return;
	        }
	        //else, not found in our list. This can happen during start.
        
	        if( string.IsNullOrEmpty(activeModel_name)  &&  knowsValidModels()){
	            //during first launch, webui might have '' as model in browser. 
	            //So we need to ignore this '' entry, and instead set a correct model as soon as we can:
	            SD_Options_Fetcher.instance.SubmitOptions_Asap();
	        }else{
	            _modelsDropdown.options.Add( new TMP_Dropdown.OptionData(activeModel_name) );
	            _modelsDropdown.SetValueWithoutNotify( _modelsDropdown.options.Count-1 );
	        }
	    }


	    string StripExtensions( string modelName ){
	        string exten = Path.GetExtension(modelName);
	        // Use .Contains not ==
	        // Because extension can contain the some number like ".safetensors [12381248]":
	        if(exten.Contains(".safetensors") || exten.Contains(".pth")){ 
	            return Path.GetFileNameWithoutExtension(modelName); 
	        }
	        //NOTICE, it has no extension. Don't use Path.GetFileNameWithoutExtension here.
	        // otherwise you will have issues with names such as `sd_xl_base_1.0`
	        return modelName;
	    }


	    // If we loaded a project file, maybe we couldn't find required model back then.
	    // So, see if we can find it now, and set dropdown to that value if possible.
	    void dropdown_LoadedSavedModel_maybe(){
	        bool wantsLoaded = string.IsNullOrEmpty(_preferedModelName_viaLoad) == false;
	        if(!wantsLoaded){ return; }
	        if (_modelsDropdown == null || _modelsDropdown.options.Count == 0){ return; }
	        string want = NormalizeCheckpointPrefer(_preferedModelName_viaLoad);
	        int modelIndex = _modelsDropdown.options.FindIndex(opt =>
	            opt.text != null
	            && string.Equals(NormalizeCheckpointPrefer(opt.text), want, StringComparison.OrdinalIgnoreCase));
	        if(modelIndex>=0){ 
	            _preferedModelName_viaLoad = "";//found, no longer need to search for it.
	            _modelsDropdown.value = modelIndex;//will invoke our callback, and send JSON to SD.
	        }
	    }


    
	    IEnumerator GetModels_crtn(){
	        //Don't send network request to webui if rendering, else it seems to stuck it sometimes.
	        var hub = StableDiffusion_Hub.instance;
	        if(hub == null || hub._generating){ yield break; }

	        _isFetchingModels = true;
	        try {
	            UnityWebRequest request = UnityWebRequest.Get(Connection_MGR.A1111_SD_API_URL + "/sd-models");
	            try {
	                yield return request.SendWebRequest();

	                bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	                    isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	                if (!isBad){
	                    SDModelsList listOfModels = SDModelsList.CreateFromJSON(request.downloadHandler.text);
	                    Act_ListOfModelsReceived?.Invoke(listOfModels);
	                    Populate_Dropdown(listOfModels);
	                }
	            } finally {
	                request.Dispose();
	            }
	        } finally {
	            _isFetchingModels = false;
	        }
	    }

    
	    void Populate_Dropdown(SDModelsList listOfModels){
	        //extract into list the names without extensions:
	        var newOptions  =  new List<TMP_Dropdown.OptionData>( listOfModels.models.Length );
	        for(int i=0; i<listOfModels.models.Length; ++i){
	            string model_name = StripExtensions(listOfModels.models[i].model_name);
	            newOptions.Add( new TMP_Dropdown.OptionData(model_name) );
	        }
	        //compare the new names with existing:
	        bool different  =  newOptions.Count != _modelsDropdown.options.Count;

	        for(int i=0; i<newOptions.Count; ++i){
	            different |= _modelsDropdown.options[i].text != newOptions[i].text;
	            if(different){ break; }
	        }

	        if(different){
	            string previousSelection = _modelsDropdown.options.Count>0 ? _modelsDropdown.options[_modelsDropdown.value].text : "";
	            _modelsDropdown.ClearOptions();
	            _modelsDropdown.AddOptions(newOptions);
	            int newIndex = _modelsDropdown.options.FindIndex(opt => opt.text==previousSelection);
	            if (newIndex >= 0){  _modelsDropdown.value = newIndex;  }// doing 'dropdown.value ='   will invoke a callback.
	        }

	        _modelsDropdown.RefreshShownValue();//refresh text and image of currently shown option.
	    }

    
	    int FindIndex_inDropdown(string activeModel_name){
	        if (_modelsDropdown.options.Count==0){ return -1; }
	        return _modelsDropdown.options.FindIndex( opt=> opt.text==activeModel_name );
	    }


	    string GetSelectedModel_name(){
	        if (_modelsDropdown.options.Count == 0){ return ""; }
	        string chosen = _modelsDropdown.options[_modelsDropdown.value].text;
	        if (SdDisconnectPlaceholder.IsPlaceholder(chosen)){ return ""; }
	        return chosen;
	    }

	    public List<string> ListCheckpointNames(){
	        var list = new List<string>();
	        if (_modelsDropdown == null) return list;
	        for (int i = 0; i < _modelsDropdown.options.Count; i++)
	            list.Add(_modelsDropdown.options[i].text);
	        return list;
	    }

	    /// <summary>Agent / MCP: select checkpoint by dropdown label or basename (e.g. flux-2-klein-4b).</summary>
	    public bool TrySelectModelByName(string name, out string resolvedName, out string error){
	        resolvedName = "";
	        error = null;
	        if (string.IsNullOrEmpty(name)){ error = "checkpoint name is empty"; return false; }
	        if (_modelsDropdown == null || _modelsDropdown.options.Count == 0){
	            error = "SD model dropdown is empty (not connected?)";
	            return false;
	        }
	        string want = NormalizeCheckpointPrefer(name.Trim());
	        int ix = _modelsDropdown.options.FindIndex(opt =>
	            opt.text != null
	            && string.Equals(NormalizeCheckpointPrefer(opt.text), want, StringComparison.OrdinalIgnoreCase));
	        if (ix < 0){
	            error = "checkpoint not in dropdown: " + want;
	            return false;
	        }
	        _modelsDropdown.value = ix;
	        _modelsDropdown.RefreshShownValue();
	        resolvedName = GetSelectedModel_name();
	        EnsureKleinSdVaeSelected(resolvedName);
	        SD_Options_Fetcher.instance?.SubmitOptions_Asap();
	        return true;
	    }

	    /// <summary>
	    /// Klein ImageStitch / RefControl encode refs through the SD VAE dropdown.
	    /// forge_additional_modules alone is not enough — sd_vae=None breaks depth structure.
	    /// Always queues options sync so Neo receives sd_vae even when the dropdown already
	    /// shows Klein VAE (reselect does not fire onValueChanged).
	    /// </summary>
	    public static bool EnsureKleinSdVaeSelected(string checkpointName = null){
	        if (string.IsNullOrEmpty(checkpointName))
	            checkpointName = instance != null ? instance.GetSelectedModel_name() : null;
	        if (!SD_OptionsPacket.CheckpointNeedsKleinModules(checkpointName)) return false;
	        var vae = SD_VAE.instance;
	        if (vae == null) return false;
	        string cur = vae.selectedVAE_name ?? "";
	        bool selected = cur.IndexOf("flux2_klein_4b_vae", StringComparison.OrdinalIgnoreCase) >= 0;
	        if (!selected){
	            // Prefer exact module filename, then stem.
	            selected = vae.TrySelectVAEByName(SD_OptionsPacket.KleinVaeModule, out _, out _)
	                || vae.TrySelectVAEByName("flux2_klein_4b_vae", out _, out _);
	        }
	        if (selected)
	            SD_Options_Fetcher.instance?.SubmitOptions_Asap();
	        return selected;
	    }



	    void OnWillSendOptions_AmmendPlz(SD_OptionsPacket payload){
	        if(!knowsValidModels()){ return; }
	        string selectedModel_name = GetSelectedModel_name();
	        if(selectedModel_name == ""){ return; }
	        payload.sd_model_checkpoint = selectedModel_name;
	        // Flux.2 Klein checkpoint is DiT-only — Neo needs Qwen3 TE + Flux2 VAE as additional modules.
	        if (SD_OptionsPacket.CheckpointNeedsKleinModules(selectedModel_name)){
	            payload.forge_additional_modules = new[] {
	                SD_OptionsPacket.KleinTextEncoderModule,
	                SD_OptionsPacket.KleinVaeModule,
	            };
	            // Force outbound sd_vae even if UI briefly shows None (ImageStitch needs it).
	            string vaeName = SD_VAE.instance != null ? SD_VAE.instance.selectedVAE_name : null;
	            if (!string.IsNullOrEmpty(vaeName)
	                && vaeName.IndexOf("flux2_klein_4b_vae", StringComparison.OrdinalIgnoreCase) >= 0)
	                payload.sd_vae = vaeName;
	            else
	                payload.sd_vae = SD_OptionsPacket.KleinVaeModule;
	        } else {
	            // Leaving Klein without clearing modules leaves Qwen3/VAE stuck on SD1.5/XL loads.
	            payload.forge_additional_modules = System.Array.Empty<string>();
	        }
	    }


	    void OnDropdown_EntryPicked(int ix){
	        string selectedModel = GetSelectedModel_name();
	        if(selectedModel == ""){ return; }
	        EnsureKleinSdVaeSelected(selectedModel);
	        SD_Options_Fetcher.instance.SubmitOptions_Asap();

	        // Klein ↔ SD1.5/XL: swap leftover ControlNet weights that would be skipped at generate time.
	        try {
	            SD_ControlNetsList_UI.instance?.TryHealFamilyMismatchedModels();
	        } catch { /* CN list may be unavailable during early boot */ }

	        _timeOf_SelectedTheModel =  Time.time;
	        ActivateDropdown(false);
	    }

	    void OnSendOptions_done(UnityWebRequest.Result rslt, string msg){
	        ActivateDropdown(true);
	    }

	    void ActivateDropdown(bool isActivate){
	        _modelsDropdown.interactable = isActivate;
	        _isLoading_anim.gameObject.SetActive(!isActivate);
	        if (!isActivate){
	            _isLoading_anim.Play();
	        }else{
	            _isLoading_anim.Stop();
	        }
	    }
	}
}//end namespace
