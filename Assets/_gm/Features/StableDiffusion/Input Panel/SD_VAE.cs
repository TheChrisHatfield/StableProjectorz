using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System;


namespace spz {

	[System.Serializable]
	public class SD_VaeItem{
	    public string model_name;
	    public string filename;
	    // Add other fields as needed
	}

	[System.Serializable]
	public class SD_VAEList{
	    public SD_VaeItem[] vae_models;
	    public static SD_VAEList CreateFromJSON(string jsonString){
	        // Use class-type information, to support inheritance of objects:
	        var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	        return JsonConvert.DeserializeObject<SD_VAEList>("{\"vae_models\":" + jsonString + "}", settings);
	    }
	}


	public class SD_VAE : MonoBehaviour{
	    public static SD_VAE instance { get; private set; } = null;

	    [SerializeField] TMP_Dropdown _vaeDropdown;
	    [SerializeField] Animation _isLoading_anim;
	    [Space(10)]
	    [SerializeField] SlideOut_Widget_UI _getMore_slideout;
	    [SerializeField] Button _loadFromDiskButton;
	    bool _loadFromDiskWired;

	    public string selectedVAE_name => GetSelectedVAE_name();
	    bool _isFetchingVAEs = false;

	    bool knowsValidVAE(){
	        var options = _vaeDropdown.options;
	        foreach(var o in options){
	            if (string.IsNullOrEmpty(o.text)){ continue; }
	            string txtLower = o.text.ToLower();
	            if (SdDisconnectPlaceholder.IsPlaceholder(txtLower)){ continue; }
	            //btw, 'None' is a valid VAE.
	            return true;
	        }
	        return false;
	    }

	    // We remember the time when we pick an entry in the dropdown.
	    // This helps to prevent overwriting our selection (after a current model query that we send periodically).
	    // If we already sent a command to pick a model recently.
	    float _timeOf_SelectedTheVAE  = -9999;

	    //if we loaded from a save-file, we migth want to select a VAE.
	    //If we are not connected, this VAE won't be in the dropdown.
	    //But we can try to find it as soon a we connect next time;
	    string _preferedVAEname_viaLoad = ""; 


	    public void Save(SD_GenSettingsInput_UI settingsSL){
	        settingsSL.neural_vae = new SD_NeuralVAE_SL();
	        settingsSL.neural_vae.selectedVAE_name = selectedVAE_name;
	    }

	    public void Load(SD_GenSettingsInput_UI from_this){
	        SD_NeuralVAE_SL v = from_this.neural_vae;
	        int newIndex = Find_index_inDropdown( v.selectedVAE_name );
	        if(newIndex == -1){
	            _preferedVAEname_viaLoad = v.selectedVAE_name;
	            return;
	        }
	        _vaeDropdown.value = newIndex;//will invoke our callback, and send JSON to SD.
	    }

	    void Awake(){
	        if (instance != null) { DestroyImmediate(this); return; }
	        instance = this;
	    }

	    void Start(){
	        _vaeDropdown.onValueChanged.AddListener( OnDropdown_EntryPicked );
	        // Boot order: Coroutines_MGR may not exist yet — retry until ready (same class as ConnectionPanel).
	        StartCoroutine( EnsureFetchLoopStarted_crtn() );
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
	        if (_getMore_slideout == null) return;
	        if (_loadFromDiskButton == null)
	            _loadFromDiskButton = SD_WeightFileImport.EnsureFromDiskButton(
	                _getMore_slideout, SD_WeightFileImport.Kind.Vae, BrowseAndImportVAE);
	        if (_loadFromDiskButton == null) return;
	        _loadFromDiskButton.onClick.RemoveListener(BrowseAndImportVAE);
	        _loadFromDiskButton.onClick.AddListener(BrowseAndImportVAE);
	        _loadFromDiskWired = true;
	    }

	    public void BrowseAndImportVAE(){
	        SD_WeightFileImport.BrowseAndImport(SD_WeightFileImport.Kind.Vae);
	    }

	    /// <summary>Kick an immediate VAE list poll (e.g. after local weight copy).</summary>
	    public void RequestImmediateVAEFetch(){
	        if (_isFetchingVAEs) return;
	        if (Coroutines_MGR.instance == null) return;
	        Coroutines_MGR.instance.StartCoroutine(GetVAEs_crtn());
	    }

	    /// <summary>Prefer this VAE after list refresh (also selects immediately if already listed).</summary>
	    public void PreferVAEWhenAvailable(string name){
	        if (string.IsNullOrEmpty(name)) return;
	        string want = name.Trim();
	        _preferedVAEname_viaLoad = want;
	        if (_vaeDropdown == null || _vaeDropdown.options.Count == 0) return;
	        string wantStem = Path.GetFileNameWithoutExtension(want);
	        // Exact filename or stem only — avoid loose IndexOf false positives.
	        int ix = _vaeDropdown.options.FindIndex(opt =>
	            opt.text != null && (
	                string.Equals(opt.text, want, StringComparison.OrdinalIgnoreCase)
	                || string.Equals(Path.GetFileNameWithoutExtension(opt.text), wantStem, StringComparison.OrdinalIgnoreCase)));
	        if (ix < 0) return;
	        _preferedVAEname_viaLoad = "";
	        if (_vaeDropdown.value == ix) {
	            _timeOf_SelectedTheVAE = Time.time;
	            SD_Options_Fetcher.instance?.SubmitOptions_Asap();
	        } else {
	            _vaeDropdown.value = ix;
	        }
	        _vaeDropdown.RefreshShownValue();
	    }

	    /// <summary>True when screen point is over VAE dropdown, visible slide-out, or this panel.</summary>
	    public bool ScreenPointHitsOwnership(Vector2 screenPoint){
	        Camera cam = UiCameraFor(transform);
	        if (transform is RectTransform root
	            && RectTransformUtility.RectangleContainsScreenPoint(root, screenPoint, cam))
	            return true;
	        if (_vaeDropdown != null
	            && _vaeDropdown.transform is RectTransform dd
	            && RectTransformUtility.RectangleContainsScreenPoint(dd, screenPoint, cam))
	            return true;
	        if (_getMore_slideout != null && _getMore_slideout.isShowing
	            && _getMore_slideout.transform is RectTransform slide
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

	    IEnumerator EnsureFetchLoopStarted_crtn(){
	        while (Coroutines_MGR.instance == null)
	            yield return null;
	        Coroutines_MGR.instance.StartCoroutine( FetchContiniously_crtn() );
	    }


	    void Update(){
	        if (!_loadFromDiskWired)
	            EnsureLoadFromDiskButton();
	        if (_getMore_slideout == null || _vaeDropdown == null) return;
	        _getMore_slideout._dontAutoHide = true;
	        _getMore_slideout.Toggle_if_Different(_vaeDropdown.IsExpanded);
	    }


	    IEnumerator FetchContiniously_crtn(){
	        while (true){
	            if (!Connection_MGR.is_sd_connected){ 
	                yield return new WaitForSeconds(0.211f); 
	                continue; 
	            }

	            if (!_isFetchingVAEs){
	                if (Coroutines_MGR.instance == null){
	                    yield return null;
	                    continue;
	                }
	                yield return Coroutines_MGR.instance.StartCoroutine( GetVAEs_crtn() );
	            }

	            dropdown_LoadedSavedVAE_maybe();

	            yield return new WaitForSeconds(3f);
	        }
	    }


	    int _fetched_num_times = 0;
	    IEnumerator GetVAEs_crtn(){
	        //Don't send network request to webui if rendering, else it seems to stuck it sometimes.
	        var hub = StableDiffusion_Hub.instance;
	        if(hub == null || hub._generating){ yield break; }

	        _isFetchingVAEs = true;
	        try {
	            // Forge/Neo-family: prefer /sd-vae then fall back to /sd-modules (some Forge builds only expose modules).
	            // Legacy A1111: alternate between endpoints across polls.
	            _fetched_num_times++;
	            string url_vae = Connection_MGR.A1111_SD_API_URL + "/sd-vae";
	            string url_modules = Connection_MGR.A1111_SD_API_URL + "/sd-modules";
	            bool forgeFamily = SD_SysInfo_MGR.instance != null && SD_SysInfo_MGR.instance.isForgeFamilyWebui_detected();
	            string urlPrimary = forgeFamily || (_fetched_num_times % 2 == 0) ? url_vae : url_modules;
	            string urlFallback = urlPrimary == url_vae ? url_modules : url_vae;

	            UnityWebRequest request = UnityWebRequest.Get(urlPrimary);
	            yield return request.SendWebRequest();

	            bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	                isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	            if (isBad) {
	                request.Dispose();
	                request = UnityWebRequest.Get(urlFallback);
	                yield return request.SendWebRequest();
	                isBad = request.result == UnityWebRequest.Result.ConnectionError;
	                isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	            }
	            if (!isBad){
	                SD_VAEList list_of_VAE = SD_VAEList.CreateFromJSON(request.downloadHandler.text);
	                Populate_Dropdown(list_of_VAE);
	            }
	        } finally {
	            _isFetchingVAEs = false;
	        }
	    }


	    void OnOptionsReceived(SD_OptionsPacket options){
	        //to avoid overwriting with potentially old value if I recently sent "Set Current Model" to SD:
	        float elapsedSinceSendModel =  Time.time - _timeOf_SelectedTheVAE ;
	        if(elapsedSinceSendModel < 10){
	            return; 
	        }
	        string activeVAE_name = options.sd_vae;
	        int vaeIndex = Find_index_inDropdown(activeVAE_name);
	        if(vaeIndex >= 0){
	            _vaeDropdown.SetValueWithoutNotify(vaeIndex);
	            return;
	        }
	        //else, not found in our list. This can happen during start:

	        if (string.IsNullOrEmpty(activeVAE_name)  &&  knowsValidVAE()){
	            //during first launch, webui might have '' as VAE in browser. 
	            //So we need to ignore this '' entry, and instead set a correct model as soon as we can:
	            SD_Options_Fetcher.instance.SubmitOptions_Asap();
	        }
	        else { 
	            _vaeDropdown.options.Add( new TMP_Dropdown.OptionData(activeVAE_name) );
	            _vaeDropdown.SetValueWithoutNotify(_vaeDropdown.options.Count - 1);
	        }
	    }


	    string ExtractSubstringBeforeLastDot(string input){
	        int lastDotIndex = input.LastIndexOf('.');
	        if (lastDotIndex != -1){
	            return input.Substring(0, lastDotIndex);
	        }else{
	            return input; // Returning the original string if no dot is found
	        }
	    } 


	    // If we loaded a project file, maybe we couldn't find required VAE back then.
	    // So, see if we can find it now, and set dropdown to that value if possible.
	    void dropdown_LoadedSavedVAE_maybe(){
	        bool wantsLoaded = string.IsNullOrEmpty(_preferedVAEname_viaLoad) == false;
	        if(!wantsLoaded){ return; }
	        if (_vaeDropdown == null || _vaeDropdown.options.Count == 0){ return; }
	        string want = _preferedVAEname_viaLoad.Trim();
	        string wantStem = Path.GetFileNameWithoutExtension(want);
	        int vaeIndex = _vaeDropdown.options.FindIndex(opt =>
	            opt.text != null && (
	                string.Equals(opt.text, want, StringComparison.OrdinalIgnoreCase)
	                || string.Equals(Path.GetFileNameWithoutExtension(opt.text), wantStem, StringComparison.OrdinalIgnoreCase)));
	        if(vaeIndex >= 0){ 
	            _preferedVAEname_viaLoad = "";//found, no longer need to search for it.
	            _vaeDropdown.value = vaeIndex;//will invoke our callback, and send JSON to SD.
	        }
	    }


	    void Populate_Dropdown(SD_VAEList list_of_VAE){
	        var newOptions  =  Array.ConvertAll(list_of_VAE.vae_models, model => new TMP_Dropdown.OptionData(model.model_name)).ToList();
	         if(!newOptions.Exists(opt => opt.text.ToLower() == "none")){  newOptions.Insert(0, new TMP_Dropdown.OptionData("None")); }
	         if(!newOptions.Exists(opt => opt.text == "Automatic")){  newOptions.Insert(1, new TMP_Dropdown.OptionData("Automatic")); }
	         //about the 'Automatic' https://github.com/AUTOMATIC1111/stable-diffusion-webui/discussions/12857#discussion-5573080

	        bool different  =  newOptions.Count != _vaeDropdown.options.Count;

	        for(int i = 0; i < newOptions.Count; ++i){
	            different |= _vaeDropdown.options[i].text != newOptions[i].text;
	            if(different){ break; }
	        }

	        if(different){
	            string previousSelection = _vaeDropdown.options.Count > 0 ? _vaeDropdown.options[_vaeDropdown.value].text : "";
	            _vaeDropdown.ClearOptions();
	            _vaeDropdown.AddOptions(newOptions);
	            int newIndex = _vaeDropdown.options.FindIndex(opt => opt.text == previousSelection);
	            if (newIndex >= 0){  _vaeDropdown.value = newIndex;  }// doing 'dropdown.value ='   will invoke a callback.
	        }

	        _vaeDropdown.RefreshShownValue();//refresh text and image of currently shown option.
	    }

    
	    int Find_index_inDropdown(string activeVAE_name){
	        if (_vaeDropdown.options.Count == 0){ return -1; }
	        return _vaeDropdown.options.FindIndex( opt => opt.text == activeVAE_name );
	    }

    
	    string GetSelectedVAE_name(){
	        if (_vaeDropdown.options.Count == 0){ return ""; }
	        string chosen = _vaeDropdown.options[_vaeDropdown.value].text;
	        if (SdDisconnectPlaceholder.IsPlaceholder(chosen)){ return ""; }
	        return chosen;
	    }

	    /// <summary>Agent / MCP: select VAE by dropdown label (partial match ok).</summary>
	    public bool TrySelectVAEByName(string name, out string resolvedName, out string error){
	        resolvedName = "";
	        error = null;
	        if (string.IsNullOrEmpty(name)){ error = "VAE name is empty"; return false; }
	        if (_vaeDropdown == null || _vaeDropdown.options.Count == 0){
	            error = "VAE dropdown is empty (not connected?)";
	            return false;
	        }
	        string want = name.Trim();
	        int ix = _vaeDropdown.options.FindIndex(opt =>
	            string.Equals(opt.text, want, StringComparison.OrdinalIgnoreCase));
	        if (ix < 0){
	            ix = _vaeDropdown.options.FindIndex(opt =>
	                opt.text != null && opt.text.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
	        }
	        if (ix < 0){
	            error = "VAE not in dropdown: " + want;
	            return false;
	        }
	        _vaeDropdown.value = ix;
	        _vaeDropdown.RefreshShownValue();
	        resolvedName = GetSelectedVAE_name();
	        return true;
	    }

	    public List<string> ListVAENames(){
	        var list = new List<string>();
	        if (_vaeDropdown == null) return list;
	        for (int i = 0; i < _vaeDropdown.options.Count; i++)
	            list.Add(_vaeDropdown.options[i].text);
	        return list;
	    }

	    void OnDropdown_EntryPicked(int ix){
	        string chosen = GetSelectedVAE_name();
	        if(chosen == ""){ return; }
	        SD_Options_Fetcher.instance.SubmitOptions_Asap();

	        _timeOf_SelectedTheVAE  =  Time.time;
	        ActivateDropdown(false);
	    }


	    void OnSendOptions_done(UnityWebRequest.Result rslt, string msg){
	        ActivateDropdown(true);
	    }


	    void ActivateDropdown(bool isActivate){
	        _vaeDropdown.interactable = isActivate;
	        _isLoading_anim.gameObject.SetActive(!isActivate);
	        if (!isActivate){
	            _isLoading_anim.Play();
	        }else{
	            _isLoading_anim.Stop();
	        }
	    }

	    void OnWillSendOptions_AmmendPlz(SD_OptionsPacket opt){
	        if(!knowsValidVAE()){ return; }
	        string selectedVAE_name = GetSelectedVAE_name();
	        if(selectedVAE_name == ""){ return; }
	        opt.sd_vae = selectedVAE_name;
	    }
	}
}//end namespace
