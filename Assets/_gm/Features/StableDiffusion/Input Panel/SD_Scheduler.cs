using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace spz {

	[Serializable]
	public class Scheduler{
	    public string name;
	    public string label;
	    public string[] aliases;
	    public float default_rho;
	    public bool need_inner_model;
	}

	[Serializable]
	public class SchedulersList{
	    public Scheduler[] schedulers;
	    public static SchedulersList CreateFromJSON(string jsonString){
	        var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	        return JsonConvert.DeserializeObject<SchedulersList>("{\"schedulers\":" + jsonString + "}", settings);
	    }
	}


	// Sends a GET request to the IP with the Stable Diffusion server.
	// Obtains a response with a collection of available schedulers.
	// Populates its UI dropdown once ready.
	public class SD_Scheduler : MonoBehaviour{
	    public static SD_Scheduler instance { get; private set; } = null;

	    [SerializeField] TMP_Dropdown _schedulers_dropdown;

	    private SchedulersList _listOfSchedulers;
	    public SchedulersList listOfSchedulers => _listOfSchedulers;
	    public Scheduler value => GetSelectedScheduler(); // currently chosen in dropdown

	    // If we loaded from a save-file, we might want to select a scheduler.
	    // If we are not connected, this scheduler won't be in the dropdown.
	    // But we can try to find it as soon as we connect next time.
	    string _preferredScheduler_viaLoad = "";

	    public Scheduler GetSelectedScheduler(){
	        if (_schedulers_dropdown.options.Count == 0){ return null; } // No options in dropdown
	        if (_listOfSchedulers == null || _listOfSchedulers.schedulers == null || _listOfSchedulers.schedulers.Length == 0){ return null; } // No schedulers loaded

	        int selectedIndex = _schedulers_dropdown.value;
	        if (selectedIndex < 0 || selectedIndex >= _listOfSchedulers.schedulers.Length){ return null; } // Out of range protection

	        return _listOfSchedulers.schedulers[selectedIndex];
	    }

	    public string selectedScheduler_name => value?.name ?? "";

	    /// <summary>Agent / MCP: select scheduler by name/label (partial match ok).</summary>
	    public bool TrySelectSchedulerByName(string name, out string resolvedName, out string error){
	        resolvedName = "";
	        error = null;
	        if (string.IsNullOrEmpty(name)){ error = "scheduler name is empty"; return false; }
	        if (_schedulers_dropdown == null || _schedulers_dropdown.options.Count == 0){
	            error = "scheduler dropdown is empty (not connected?)";
	            return false;
	        }
	        string want = name.Trim();
	        int ix = _schedulers_dropdown.options.FindIndex(opt =>
	            string.Equals(opt.text, want, StringComparison.OrdinalIgnoreCase));
	        if (ix < 0){
	            ix = _schedulers_dropdown.options.FindIndex(opt =>
	                opt.text != null && opt.text.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
	        }
	        if (ix < 0){
	            error = "scheduler not in dropdown: " + want;
	            return false;
	        }
	        _schedulers_dropdown.value = ix;
	        _schedulers_dropdown.RefreshShownValue();
	        resolvedName = _schedulers_dropdown.options[ix].text;
	        return true;
	    }

	    public List<string> ListSchedulerNames(){
	        var list = new List<string>();
	        if (_schedulers_dropdown == null) return list;
	        for (int i = 0; i < _schedulers_dropdown.options.Count; i++)
	            list.Add(_schedulers_dropdown.options[i].text);
	        return list;
	    }

	    public void Save(SD_GenSettingsInput_UI fill_this){
	        fill_this.scheduler = new SD_InputSchedulers_SL();
	        fill_this.scheduler.selectedScheduler_name = value?.name ?? "";
	    }

	    public void Load(SD_GenSettingsInput_UI from_this){
	        SD_InputSchedulers_SL s = from_this.scheduler;
	        int newIndex = _schedulers_dropdown.options.FindIndex(opt => opt.text == s.selectedScheduler_name);
	        if(newIndex == -1){
	            _preferredScheduler_viaLoad = s.selectedScheduler_name;
	            return;
	        }
	        _preferredScheduler_viaLoad = ""; // Found, no longer need to search for it.
	        _schedulers_dropdown.value = newIndex;
	    }

    
	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; };
	        instance = this;
	    }


	    void Start(){
	        StartCoroutine( EnsureFetchStarted_crtn() );
	    }

	    IEnumerator EnsureFetchStarted_crtn(){
	        while (Coroutines_MGR.instance == null)
	            yield return null;
	        Coroutines_MGR.instance.StartCoroutine(FetchContinuously());
	    }

	    IEnumerator FetchContinuously(){
	        while (true){
	            if (!Connection_MGR.is_sd_connected){ 
	                yield return new WaitForSeconds(0.211f); 
	                continue; 
	            }
	            Coroutines_MGR.instance.StartCoroutine(GetSchedulers_crtn());
	            Dropdown_LoadedSavedScheduler_maybe();

	            yield return new WaitForSeconds(4f);
	        }
	    }

	    IEnumerator GetSchedulers_crtn(){
	        // Don't send network request to web UI if rendering; it may cause it to get stuck sometimes.
	        if(StableDiffusion_Hub.instance._generating){ yield break; }

	        UnityWebRequest request = UnityWebRequest.Get(Connection_MGR.A1111_SD_API_URL + "/schedulers");
	        try {
	            yield return request.SendWebRequest();

	            bool isBad = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError;
	            if (isBad){
	                yield break;
	            }

	            _listOfSchedulers = SchedulersList.CreateFromJSON(request.downloadHandler.text);
	            Populate_DropdownSchedulers();
	        } finally {
	            request.Dispose();
	        }
	    }

	    void Populate_DropdownSchedulers(){
	        var newOptions = Array.ConvertAll(_listOfSchedulers.schedulers, scheduler => new TMP_Dropdown.OptionData(scheduler.name)).ToList();
	        bool different = newOptions.Count != _schedulers_dropdown.options.Count;

	        for(int i = 0; i < newOptions.Count && !different; ++i){
	            different |= _schedulers_dropdown.options[i].text != newOptions[i].text;
	        }

	        if (different){
	            string previousSelection = _schedulers_dropdown.options.Count > 0 ? _schedulers_dropdown.options[_schedulers_dropdown.value].text : "";
	            _schedulers_dropdown.ClearOptions();
	            _schedulers_dropdown.AddOptions(newOptions);
	            int newIx = Array.FindIndex(_listOfSchedulers.schedulers, scheduler => scheduler.name == previousSelection);
	            if(newIx >= 0){ _schedulers_dropdown.value = newIx; }
	        }

	        _schedulers_dropdown.RefreshShownValue(); // Refresh text and image of currently shown option.
	    }

	    // If we loaded a project file, maybe we couldn't find the required scheduler back then.
	    // So, see if we can find it now, and set dropdown to that value if possible.
	    void Dropdown_LoadedSavedScheduler_maybe(){
	        if(!string.IsNullOrEmpty(_preferredScheduler_viaLoad)){
	            int schedulerIx = _schedulers_dropdown.options.FindIndex(opt => opt.text == _preferredScheduler_viaLoad);
	            if(schedulerIx >= 0){
	                _preferredScheduler_viaLoad = ""; // Found, no longer need to search for it.
	                _schedulers_dropdown.value = schedulerIx;
	            }
	        }
	    }
	}
}//end namespace
