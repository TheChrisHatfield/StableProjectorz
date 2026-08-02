using System;
using System.Linq;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace spz {

	public class SD_ControlNetsList_UI : MonoBehaviour{
	    public static SD_ControlNetsList_UI instance { get; private set; } = null;

	    [SerializeField] Transform _controlUnits_parent;
	    [SerializeField] ControlNetUnit_UI _controlNet_PREFAB;
	    [SerializeField] List<ControlNetUnit_UI> _controlNet_units;

	    Coroutines_MGR crtnMgr => Coroutines_MGR.instance;


	    public static string API_URL => Connection_MGR.A1111_IP_AND_PORT + "/controlnet";

	    /// <summary>
	    /// Neo/Forge UI default when <c>/controlnet/settings</c> is absent and sysinfo Config is still 0
	    /// (forge-neo-swap R3). Matches Forge Neo / reForge <c>control_net_unit_count</c> default.
	    /// </summary>
	    public const int DefaultCtrlNetUnitCountWhenUnknown = 3;

	    //will be fetched from network, via API json:
	    public CTRLnets_ModelList _models { get; private set; }  = new CTRLnets_ModelList();
	    public CTRLnets_PreprocessorsList _preprocessors_list { get; private set; }  = new CTRLnets_PreprocessorsList();
	    public ControlTypesResponse _net_types { get; private set; }  = new ControlTypesResponse(); //not used at the moment.

	    public int numTotalUnitsExisting() => _controlNet_units.Count;
	    public int numActiveUnits() => _controlNet_units.Count(u => u.isActivated);
	    
	    /// <summary>
	    /// Get ControlNet unit by index (for add-on API)
	    /// </summary>
	    public ControlNetUnit_UI GetUnit(int index) {
	        if (index < 0 || index >= _controlNet_units.Count) return null;
	        return _controlNet_units[index];
	    }
	    public bool Has_Active_Inpainting_CTRLUnit() =>  _controlNet_units.Any( u=> u.isActivated && u.isForInpaint() );

	    public bool Has_Depth_CTRLUnit(bool onlyActive, bool only_if_validModel){
	        foreach(var u in _controlNet_units){
	            if(!u.isForDepth()){ continue; }
	            if(onlyActive && !u.isActivated){ continue; }
	            if(only_if_validModel && !IsUnitModelValidForActiveCheckpoint(u)){ continue; }
	            return true;
	        }
	        return false;
	    }

	    public bool Has_Normals_CTRLUnit(bool onlyActive, bool only_if_validModel){
	        foreach(var u in _controlNet_units){
	            if(!u.isForNormals()){ continue; }
	            if(onlyActive && !u.isActivated){ continue; }
	            if(only_if_validModel && !IsUnitModelValidForActiveCheckpoint(u)){ continue; }
	            return true;
	        }
	        return false;
	    }

	    /// <summary>
	    /// Model None is invalid for depth/normals gates. Family-mismatched weights (e.g. SD1.5 CN on
	    /// Klein) are also invalid — GetArgs skips them, so the gate must not treat them as ready.
	    /// </summary>
	    static bool IsUnitModelValidForActiveCheckpoint(ControlNetUnit_UI u){
	        if (u == null || u.is_currModel_none) return false;
	        string sdCkpt = null;
	        try { sdCkpt = SD_InputPanel_UI.instance?.models?.selectedModel_name; } catch { /* */ }
	        return !ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(u.currModelName(), sdCkpt);
	    }

	    public int Num_Active_Reference_CTRLUnit() => _controlNet_units.Count( u=>u.isActivated && u.isReferencePreprocessor() );

	    /// <summary>
	    /// Agent / MCP: set every unit model dropdown to None and clear Depth/ContentCam/CustomFile
	    /// what-to-send so Klein img2img co-opt is not left armed after a "clear CN" / Klein preset.
	    /// Returns how many units had their model set to None.
	    /// </summary>
	    public int ClearAllUnitModelsToNone(){
	        int n = 0;
	        if (_controlNet_units == null) return 0;
	        for (int i = 0; i < _controlNet_units.Count; i++){
	            var u = _controlNet_units[i];
	            if (u == null || u.dropdowns == null) continue;
	            if (u.dropdowns.TrySelectModelNone()) n++;
	            // Model None + leftover Depth/ContentCam/CustomFile would still force Klein img2img.
	            if (u._whatImageToSend == WhatImageToSend_CTRLNET.Depth
	                || u._whatImageToSend == WhatImageToSend_CTRLNET.ContentCam
	                || u._whatImageToSend == WhatImageToSend_CTRLNET.CustomFile){
	                u.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.None, allowOpenFileDialog: false);
	            }
	        }
	        return n;
	    }

	    /// <summary>
	    /// Klein Gen Art layout (structure probe): no Fun-Union ControlNet — mesh Depth is fed as
	    /// img2img init (model None). Optional CustomFile on unit 1 for style later / RefControl.
	    /// Returns true when unit 0 was armed for Depth img2img co-opt.
	    /// </summary>
	    public bool TryApplyKleinControlNetLayout(out string fluxCnResolved, out string initSourceLabel){
	        fluxCnResolved = "";
	        initSourceLabel = "";
	        if (_controlNet_units == null || _controlNet_units.Count == 0) return false;

	        // Fun-Controlnet-Union does not lock Klein-4B — clear CN weights on all units.
	        ClearAllUnitModelsToNone();

	        var u0 = GetUnit(0);
	        bool armedDepth = false;
	        if (u0 != null){
	            if (u0.dropdowns != null) u0.dropdowns.TrySelectModelNone();
	            bool setDepth = u0.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.Depth, allowOpenFileDialog: false);
	            if (setDepth){
	                u0.TrySetActivated(true);
	                armedDepth = u0.isActivated && u0._whatImageToSend == WhatImageToSend_CTRLNET.Depth
	                    && u0.is_currModel_none;
	                if (armedDepth) initSourceLabel = "Depth";
	            }
	        }

	        // Keep a loaded CustomFile on unit 1 (style ref) but model None — Depth wins peek order for init.
	        ControlNetUnit_UI customUnit = null;
	        for (int i = 0; i < _controlNet_units.Count; i++){
	            var u = _controlNet_units[i];
	            if (u != null && u.HasLoadedCustomFileBitmap()){ customUnit = u; break; }
	        }
	        if (customUnit != null){
	            var styleUnit = GetUnit(1) ?? customUnit;
	            if (styleUnit != customUnit && customUnit == u0){
	                // Depth owns unit 0 — move style bitmap handling to unit 1 if present.
	                styleUnit = GetUnit(1);
	            }
	            if (styleUnit != null && styleUnit != u0){
	                if (styleUnit.dropdowns != null) styleUnit.dropdowns.TrySelectModelNone();
	                // Re-arm CustomFile only if this unit already holds the bitmap or is unit 1 after copy.
	                if (styleUnit.HasLoadedCustomFileBitmap()
	                    || (customUnit != styleUnit && customUnit.HasLoadedCustomFileBitmap())){
	                    if (!styleUnit.HasLoadedCustomFileBitmap() && customUnit != styleUnit)
	                        styleUnit.CopyFromAnother(customUnit);
	                    styleUnit.dropdowns?.TrySelectModelNone();
	                    styleUnit.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.CustomFile, allowOpenFileDialog: false);
	                    // Do not activate style unit for init — Depth on u0 is the structure source.
	                    styleUnit.TrySetActivated(false);
	                }
	            }
	            // Restore Depth on u0 after any CopyFromAnother side effects.
	            if (u0 != null){
	                u0.dropdowns?.TrySelectModelNone();
	                bool setDepth = u0.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.Depth, allowOpenFileDialog: false);
	                if (setDepth){
	                    u0.TrySetActivated(true);
	                    armedDepth = u0.isActivated && u0._whatImageToSend == WhatImageToSend_CTRLNET.Depth
	                        && u0.is_currModel_none;
	                    initSourceLabel = armedDepth ? "Depth" : "";
	                } else {
	                    armedDepth = false;
	                    initSourceLabel = "";
	                }
	            }
	        }

	        if (string.IsNullOrEmpty(initSourceLabel) && TryPeekKleinImg2ImgInitSource(out _, out string label))
	            initSourceLabel = label;
	        // Ensure depth RT is allocated before readiness checks (same-frame prepare/gen).
	        if (armedDepth){
	            object lockOwner = this;
	            UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: true);
	            try { Update_callbacks_MGR.content_depthRender?.Invoke(); }
	            finally {
	                UserCameras_Permissions.LockOrUnlock_ByType(CameraTexType.DepthUserCamera, lockOwner, isLock: false);
	            }
	        }
	        // Prefer live peek over optimistic flags — callers must not see armed when init is missing.
	        if (armedDepth && !HasKleinImg2ImgInitSource())
	            armedDepth = false;
	        return armedDepth;
	    }

	    /// <summary>
	    /// Flux.2 Klein: find an activated ControlNet unit (model None) whose "what to send"
	    /// is Depth, CustomFile, or ContentCam and return a disposable RGB copy for img2img init.
	    /// Prefers mesh Depth (structure) over CustomFile (style) over ContentCam.
	    /// Encode path Crop-and-Resizes into the screen-mask frustum so projection bake stays
	    /// viewport-aligned. Collapsed/disabled units and units with a real CN model are ignored.
	    /// </summary>
	    public bool TryGetDisposableKleinImg2ImgInit(out Texture2D tex, out int unitIndex, out string sourceLabel){
	        tex = null;
	        unitIndex = -1;
	        sourceLabel = "";
	        if (_controlNet_units == null) return false;

	        if (TryPickKleinInit(WhatImageToSend_CTRLNET.Depth, out tex, out unitIndex, out sourceLabel))
	            return true;
	        if (TryPickKleinInit(WhatImageToSend_CTRLNET.CustomFile, out tex, out unitIndex, out sourceLabel))
	            return true;
	        if (TryPickKleinInit(WhatImageToSend_CTRLNET.ContentCam, out tex, out unitIndex, out sourceLabel))
	            return true;
	        return false;
	    }

	    /// <summary>
	    /// Fetch only the peeked Klein init kind (Depth / CustomFile / ContentCam).
	    /// Avoids silently substituting CustomFile when Depth capture fails.
	    /// </summary>
	    public bool TryGetDisposableKleinImg2ImgInitForLabel(string sourceLabel, out Texture2D tex, out int unitIndex){
	        tex = null;
	        unitIndex = -1;
	        if (_controlNet_units == null || string.IsNullOrEmpty(sourceLabel)) return false;
	        WhatImageToSend_CTRLNET want;
	        if (string.Equals(sourceLabel, "Depth", System.StringComparison.Ordinal))
	            want = WhatImageToSend_CTRLNET.Depth;
	        else if (string.Equals(sourceLabel, "CustomFile", System.StringComparison.Ordinal))
	            want = WhatImageToSend_CTRLNET.CustomFile;
	        else if (string.Equals(sourceLabel, "ContentCam", System.StringComparison.Ordinal))
	            want = WhatImageToSend_CTRLNET.ContentCam;
	        else
	            return false;
	        return TryPickKleinInit(want, out tex, out unitIndex, out _);
	    }

	    public bool HasKleinImg2ImgInitSource(){
	        return TryPeekKleinImg2ImgInitSource(out _, out _);
	    }

	    /// <summary>
	    /// Activated Klein co-opt unit armed for CustomFile but with no bitmap loaded.
	    /// </summary>
	    public bool HasArmedEmptyKleinCustomFile(){
	        if (_controlNet_units == null) return false;
	        for (int i = 0; i < _controlNet_units.Count; i++){
	            var u = _controlNet_units[i];
	            if (u == null || !u.isActivated) continue;
	            if (u._whatImageToSend != WhatImageToSend_CTRLNET.CustomFile) continue;
	            if (!u.is_currModel_none) continue;
	            if (u.IsKleinImg2ImgInitSource()) continue; // has valid bitmap
	            return true;
	        }
	        return false;
	    }

	    /// <summary>
	    /// Describes the preferred Klein init source without allocating a texture.
	    /// Same preference order as TryGetDisposableKleinImg2ImgInit (Depth, CustomFile, ContentCam).
	    /// </summary>
	    public bool TryPeekKleinImg2ImgInitSource(out int unitIndex, out string sourceLabel){
	        unitIndex = -1;
	        sourceLabel = "";
	        if (_controlNet_units == null) return false;
	        if (TryPeekKleinInit(WhatImageToSend_CTRLNET.Depth, out unitIndex, out sourceLabel))
	            return true;
	        if (TryPeekKleinInit(WhatImageToSend_CTRLNET.CustomFile, out unitIndex, out sourceLabel))
	            return true;
	        if (TryPeekKleinInit(WhatImageToSend_CTRLNET.ContentCam, out unitIndex, out sourceLabel))
	            return true;
	        return false;
	    }

	    bool TryPeekKleinInit(WhatImageToSend_CTRLNET want, out int unitIndex, out string sourceLabel){
	        unitIndex = -1;
	        sourceLabel = "";
	        for (int i = 0; i < _controlNet_units.Count; i++){
	            var u = _controlNet_units[i];
	            if (u == null || u._whatImageToSend != want) continue;
	            if (!u.IsKleinImg2ImgInitSource()) continue;
	            unitIndex = i;
	            if (want == WhatImageToSend_CTRLNET.Depth) sourceLabel = "Depth";
	            else if (want == WhatImageToSend_CTRLNET.CustomFile) sourceLabel = "CustomFile";
	            else sourceLabel = "ContentCam";
	            return true;
	        }
	        return false;
	    }

	    bool TryPickKleinInit(WhatImageToSend_CTRLNET want, out Texture2D tex, out int unitIndex, out string sourceLabel){
	        tex = null;
	        unitIndex = -1;
	        sourceLabel = "";
	        for (int i = 0; i < _controlNet_units.Count; i++){
	            var u = _controlNet_units[i];
	            if (u == null || u._whatImageToSend != want) continue;
	            Texture2D got = u.TryGetDisposableKleinImg2ImgInit(out string label);
	            if (got == null) continue;
	            tex = got;
	            unitIndex = i;
	            sourceLabel = label;
	            return true;
	        }
	        return false;
	    }

	    public List<string> curentModels_of_DepthOrNormal_units(){
	        var names = new List<string>();
	        for(int i=0; i<_controlNet_units.Count; ++i){
	            var unit = _controlNet_units[i];
	            if(!unit.isActivated){ continue; }
            
	            bool isForDepth = _controlNet_units[i].isForDepth();
	            bool isForNorms = _controlNet_units[i].isForNormals();
	            if(!isForDepth && !isForNorms){ continue; }

	            string n = _controlNet_units[i].currModelName();
	            if(string.IsNullOrEmpty(n)){ continue; }
	            names.Add(n);
	        }
	        return names;
	    }


	    /// <summary>
	    /// Swap family-mismatched CN weights (e.g. leftover SD1.5 depth on XL) to a compatible model
	    /// before payload build so Gen Art does not silently drop depth.
	    /// Klein-4B: no compatible CN — disarm models to None so UI matches skipped alwayson payload.
	    /// </summary>
	    public int TryHealFamilyMismatchedModels(){
	        if (_controlNet_units == null) return 0;
	        string sdCkpt = null;
	        try { sdCkpt = SD_InputPanel_UI.instance?.models?.selectedModel_name; } catch { /* */ }
	        if (string.IsNullOrEmpty(sdCkpt)) return 0;

	        // Klein: Fun-Union / any CN weight is mismatch — clear model, keep Depth what-to-send for img2img.
	        if (SD_OptionsPacket.CheckpointNeedsKleinModules(sdCkpt)){
	            int cleared = 0;
	            for (int i = 0; i < _controlNet_units.Count; i++){
	                var u = _controlNet_units[i];
	                if (u == null || u.dropdowns == null || u.is_currModel_none) continue;
	                if (!u.dropdowns.TrySelectModelNone()) continue;
	                cleared++;
	            }
	            // Switching Klein↔XL must not leave Gen Art gated with no init (heal cleared Fun-Union).
	            if (!HasKleinImg2ImgInitSource()){
	                var u0 = GetUnit(0);
	                if (u0 != null){
	                    u0.dropdowns?.TrySelectModelNone();
	                    if (u0.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.Depth, allowOpenFileDialog: false)){
	                        u0.TrySetActivated(true);
	                        cleared = Mathf.Max(cleared, 1);
	                    }
	                }
	            }
	            return cleared;
	        }

	        string[] models = _models != null ? _models.model_list : null;
	        if (models == null || models.Length == 0) return 0;

	        string replacement = ControlNetUnit_Dropdowns.FindPreferredDepthModelName(models);
	        if (string.IsNullOrEmpty(replacement)) return 0;
	        // Refuse a "heal" that would still mismatch (e.g. Klein with only SD1.5 depth installed).
	        if (ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(replacement, sdCkpt))
	            return 0;

	        int healed = 0;
	        for (int i = 0; i < _controlNet_units.Count; i++){
	            var u = _controlNet_units[i];
	            if (u == null || !u.isActivated || u.dropdowns == null) continue;
	            // Capture role before model swap — Fun-Union names lack "depth"/"norm" substrings.
	            bool wasDepth = u.isForDepth();
	            bool wasNorms = u.isForNormals();
	            // Only depth/normals gates participate in Gen Art projection — leave other CN slots alone.
	            if (!wasDepth && !wasNorms) continue;
	            if (u.is_currModel_none) continue;
	            if (!ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(u.currModelName(), sdCkpt))
	                continue;
	            if (!u.dropdowns.TrySelectModelByName(replacement, out _, out _)) continue;
	            if (wasDepth){
	                u.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.Depth, allowOpenFileDialog: false);
	            } else if (wasNorms){
	                u.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.Normals, allowOpenFileDialog: false);
	            }
	            healed++;
	        }
	        return healed;
	    }

	    // Provides a summary of the current settings for All ControlNet units,
	    // so that we can send a Generate request to stable diffusion.
	    // We can use what's already in 'intermediates' arg, or actually add stuff to it.
	    // NOTICE: some unit might refuse to participate (if some conditions are not met). 
	    // If so, the array inside the args will be shorter.
	    public ControlNet_NetworkArgs GetArgs_forGenerationRequest( SD_GenRequestArgs_byproducts intermediates ){

	        TryHealFamilyMismatchedModels();

	        var args_ofValid_units = new List<ControlNetUnit_NetworkArgs>();
	        if (_controlNet_units == null){
	            return new ControlNet_NetworkArgs{ args = args_ofValid_units.ToArray() };
	        }

	        int numUnits = _controlNet_units.Count;
	        for(int i=0; i<numUnits; ++i){
	            var unit = _controlNet_units[i];
	            if (unit == null) continue;
	            ControlNetUnit_NetworkArgs arg = unit.GetArgs_forGenerationRequest(intermediates);
	            if(arg!=null){ args_ofValid_units.Add(arg); }
	        }
	        ControlNet_NetworkArgs cnArgs = new ControlNet_NetworkArgs{
	            args = args_ofValid_units.ToArray(),
	        };
	        return cnArgs;
	    }


	    public void DoForEvery_CtrlUnit( Action<ControlNetUnit_UI,int> act_unitAndIndex ){
	        for(int i=0; i<_controlNet_units.Count; ++i){
	            ControlNetUnit_UI unit = _controlNet_units[i];
	            act_unitAndIndex( unit, i);
	        }
	    }

	    public void Save(StableProjectorz_SL spz){
	        spz.controlNetUnits_panel = new ControlNetUnits_Panel_SL();
	        spz.controlNetUnits_panel.ctrl_units = new List<ControlNetUnit_SL>();

	        for(int i=0; i<_controlNet_units.Count; ++i){
	            var unit_sl = new ControlNetUnit_SL();
	            _controlNet_units[i].Save(i, unit_sl, spz.filepath_dataDir);
	            spz.controlNetUnits_panel.ctrl_units.Add(unit_sl);
	        }
	    }

	    public void Load(StableProjectorz_SL spz){
	        if (spz.controlNetUnits_panel == null || spz.controlNetUnits_panel.ctrl_units == null) return;
	        //remove any old unit:
	        EnsureExact_num_CTRLnets(0, instantDestroy_excess:true);
	        //load new units:
	        List<ControlNetUnit_SL> unitsSL = spz.controlNetUnits_panel.ctrl_units;
	        EnsureExact_num_CTRLnets( unitsSL.Count, instantDestroy_excess:true );

	        for (int i=0; i<unitsSL.Count; ++i){
	             _controlNet_units[i].Load(unitsSL[i], spz.filepath_dataDir);
	        }
	    }


	    void Awake(){
	        if (instance != null){ DestroyImmediate(this);return; }
	        instance = this;
	        crtnMgr.StartCoroutine( FetchContiniously() );
	    }


	    IEnumerator FetchContiniously(){
        
	        DEBUG_FetchContiniously(0);

	        while (true){
	            if (!Connection_MGR.is_sd_connected){ 
	                yield return new WaitForSeconds(0.25f); 
	                continue; 
	            }
	            DEBUG_FetchContiniously(1);
            
	            yield return crtnMgr.StartCoroutine( Fetch_WebuiInfo_crtn() );
	            yield return new WaitForSeconds(3f);

	            DEBUG_FetchContiniously(2);
	        }
	    }

	    IEnumerator Fetch_WebuiInfo_crtn(){
	        DEBUG_FetchInfo(0);

	        DEBUG_FetchInfo(1);
	      //models list:
	        bool success = false;
	        System.Action<bool,string> onResult =  (isSuccess,text) => { 
	            success=isSuccess;
	            _models = CTRLnets_ModelList.CreateFromJSON(text);
	        };
	        yield return crtnMgr.StartCoroutine(FetchData_crtn(API_URL+"/model_list?update=true", onResult));
	        if (!success){ yield break; }

	        DEBUG_FetchInfo(2);
	        //modules list:
	        success = false;
	        onResult =  (isSuccess,text) => { 
	            success=isSuccess;
	            _preprocessors_list = CTRLnets_PreprocessorsList.CreateFromJSON(text);
	        };
	        yield return crtnMgr.StartCoroutine(FetchData_crtn(API_URL+"/module_list?alias_names=false", onResult));
	        if (!success){ yield break; }

	        DEBUG_FetchInfo(3);

	        //control net types:
	        success = false;
	        onResult =  (isSuccess,text) => { 
	            success=isSuccess;
	            _net_types = ControlTypesResponse.CreateFromJSON(text);
	        }; 
	        yield return crtnMgr.StartCoroutine(FetchData_crtn(API_URL+"/control_types", onResult));
	        //COMMENTED OUT KEPT FOR PRECAUTION:
	        //Some people had 404 for /control_types  (these are just presets of model+preprocessor, bulletpoints).
	        //But I'm not relying on them, so don't break and continue as if nothing happened:
	        //   if (!success){ yield break; }

	        DEBUG_FetchInfo(4);

	        int num_ctrlnetUnits = 0;
	        System.Action<int> on_set_numUnits = (int num)=>{ num_ctrlnetUnits=num; };
	        yield return crtnMgr.StartCoroutine( FetchData_numCtrlUnits(on_set_numUnits) );
	        // Resolve never returns 0 under normal Neo/Forge defaults; keep guard for safety.
	        if(num_ctrlnetUnits==0){
	            UnityEngine.Debug.LogWarning("[ControlNet] Unit count resolved to 0; skipping EnsureExact this pass (will retry on next fetch).");
	            yield break;
	        }

	        EnsureExact_num_CTRLnets( num_ctrlnetUnits, instantDestroy_excess:true );

	        DEBUG_FetchInfo(8, _controlNet_units.Count.ToString());
	        _controlNet_units.ForEach(u => u.OnRefresh_WebuiInfo_Complete());
	    }


	    // Coroutine to handle the web request
	    IEnumerator FetchData_crtn( string url,  Action<bool,string> onResult ){
	        //Don't send network request to webui if rendering, else it seems to stuck it sometimes.
	        if(StableDiffusion_Hub.instance._generating){ yield break; }

	        DEBUG_FetchData(0);
	        UnityWebRequest request = UnityWebRequest.Get(url);
	        yield return request.SendWebRequest();

	        bool isBad = request.result == UnityWebRequest.Result.ConnectionError;
	            isBad |= request.result == UnityWebRequest.Result.ProtocolError;
	        if (isBad){
	            DEBUG_FetchData(1, request.error);
	            onResult?.Invoke(false, "");
	        }else{
	            DEBUG_FetchData(2, request.downloadHandler.text + "\n\n");
	            onResult?.Invoke(true, request.downloadHandler.text);
	        }
	    }


	    // Attempts to get parameter 'number of control units', from webui.
	    // Tries legacy A1111 GET /controlnet/settings; Neo/reForge omit that route (404 expected).
	    // Then sysinfo Config; brief retry for race; then keep existing units or Neo default 3 (forge-neo-swap R3).
	    IEnumerator FetchData_numCtrlUnits( System.Action<int> on_set_numUnits ){

	        DEBUG_FetchInfo(5);
	        bool settingsOk = false;
	        int settingsUnits = 0;

	        System.Action<bool,string> onResult =  (isSuccess,text) => { 
	            settingsOk=isSuccess;
	            if (!isSuccess){ return; }
	            CTRLnets_Settings settings = CTRLnets_Settings.CreateFromJSON(text);
	            settingsUnits = settings != null ? settings.num_units() : 0;
	        };

	        yield return crtnMgr.StartCoroutine(FetchData_crtn(API_URL+"/settings", onResult));
	        if (settingsOk && settingsUnits > 0){
	            on_set_numUnits( settingsUnits );
	            DEBUG_FetchInfo(6, "success");
	            yield break; 
	        }
	        if (settingsOk && settingsUnits <= 0){
	            UnityEngine.Debug.LogWarning("[ControlNet] /controlnet/settings returned 0 units; falling through to sysinfo/default.");
	        } else {
	            // Neo: missing /settings is expected — do not treat as “no ControlNet.”
	            UnityEngine.Debug.Log("[ControlNet] /controlnet/settings unavailable (expected on Forge Neo / reForge); using sysinfo/default.");
	        }

	        int sysinfoUnits = 0;
	        if (SD_SysInfo_MGR.instance != null && SD_SysInfo_MGR.instance.sysInfo != null
	            && SD_SysInfo_MGR.instance.sysInfo.Config != null) {
	            sysinfoUnits = SD_SysInfo_MGR.instance.sysInfo.Config.num_units();
	        }
	        // Sysinfo polls every ~5s; CN fetch can win the race with Config still zero.
	        for (int attempt = 0; attempt < 6 && sysinfoUnits <= 0; attempt++) {
	            yield return new WaitForSeconds(0.5f);
	            if (SD_SysInfo_MGR.instance == null || SD_SysInfo_MGR.instance.sysInfo?.Config == null)
	                continue;
	            sysinfoUnits = SD_SysInfo_MGR.instance.sysInfo.Config.num_units();
	        }

	        int existing = _controlNet_units != null ? _controlNet_units.Count : 0;
	        int resolved = ResolveCtrlNetUnitCount(settingsOk, settingsUnits, sysinfoUnits, existing);
	        on_set_numUnits( resolved );
	        DEBUG_FetchInfo(7, resolved.ToString());
	    }

	    /// <summary>
	    /// Pure unit-count resolver for Neo/Forge/A1111 (forge-neo-swap R3). Prefer settings &gt;0,
	    /// then sysinfo &gt;0, then keep existing UI units, else Neo default.
	    /// </summary>
	    public static int ResolveCtrlNetUnitCount(bool settingsOk, int settingsUnits, int sysinfoUnits, int existingUnits) {
	        if (settingsOk && settingsUnits > 0)
	            return settingsUnits;
	        if (sysinfoUnits > 0)
	            return sysinfoUnits;
	        if (existingUnits > 0)
	            return existingUnits;
	        return DefaultCtrlNetUnitCountWhenUnknown;
	    }


	    void EnsureExact_num_CTRLnets(int wantedNum, bool instantDestroy_excess){
	        DEBUG_EnsureCount(0);
	        int count = _controlNet_units.Count;
	        int excess = count - wantedNum;
	        if(excess==0){
	            DEBUG_EnsureCount(1);
	            return; //all good.
	        }

	        bool hadExcess = destroyExcess(count, excess, instantDestroy_excess);
	        if(hadExcess){ return; }

	        DEBUG_EnsureCount(3, Mathf.Abs(excess).ToString() + " adding new ones" );

	        for(int i=0; i<Mathf.Abs(excess); ++i){
	            var unit = Instantiate(_controlNet_PREFAB, _controlUnits_parent);
	            _controlNet_units.Add(unit);
	        }
	    }


	    bool destroyExcess(int count, int excess, bool instantDestroy_excess){
	        if(excess<=0){ return false;}
	        //too many:
	        DEBUG_EnsureCount(2, excess.ToString()+ " removing redundant ones");
	        for (int i=0; i<excess; ++i){ 
	            if(instantDestroy_excess){
	                DestroyImmediate(_controlNet_units[count-i-1].gameObject); 
	            }else{
	                Destroy(_controlNet_units[count-i-1].gameObject);
	            }
	        }
	        _controlNet_units.RemoveRange(count-excess, excess);
	        return true;
	    }


    
	    void DEBUG_FetchContiniously(int KeyIx, string suffix=""){
	        #if SP_VERBOSE_CTRLNET_DEBUG
	        Dictionary<int, string> dict = new Dictionary<int, string>(){
	            {0, "SD_ControlNetsList_UI::FetchContiniously() CTRLNets List FetchContiniously entered"},
	            {1, "SD_ControlNetsList_UI::FetchContiniously() CTRLNets List starting the RefreshInfo_fromNet_crnt"},
	            {2, "\n\n\n\n\n\n\n\n\n\n" },
	        };
	        Debug.Log(dict[KeyIx] + suffix);
	        #endif
	    }


	    void DEBUG_FetchInfo(int KeyIx, string suffix=""){
	        #if SP_VERBOSE_CTRLNET_DEBUG
	        Dictionary<int, string> dict = new Dictionary<int, string>(){
	            {0, "\n\n--SD_ControlNetsList_UI::FetchInfo() CTRLNetsList entered RefreshInfo_fromNet_crnt()\n\n"},
	            {1, "\n\n--SD_ControlNetsList_UI::FetchInfo() CTRLNetsList going to fetch models"},
	            {2, "\n\n--SD_ControlNetsList_UI::FetchInfo() CTRLNetsList going to fetch modules" },
	            {3, "\n\n--SD_ControlNetsList_UI::FetchInfo() CTRLNetsList going to fetch control types" },
	            {4, "\n\n--SD_ControlNetsList_UI::FetchInfo() CTRLNetsList going to fetch settings" },
	            {5, "\n\n--SD_ControlNetsList_UI::FetchInfo() FetchData_numCtrlUnits() started."  },
	            {6, "\n\n--SD_ControlNetsList_UI::FetchInfo() FetchData_numCtrlUnits() legacy a1111 queried: "  },
	            {7, "\n\n--SD_ControlNetsList_UI::FetchInfo() FetchData_numCtrlUnits() Forge queried. "  },
	            {8, "\n\n--SD_ControlNetsList_UI::FetchInfo() _controlNet_units.Count: "  },
	        };
	        Debug.Log(dict[KeyIx] + suffix);
	        #endif
	    }


	    void DEBUG_FetchData(int KeyIx, string suffix=""){
	        #if SP_VERBOSE_CTRLNET_DEBUG
	        Dictionary<int, string> dict = new Dictionary<int, string>(){
	            {0, "------SD_ControlNetsList_UI::FetchData() CTRLNET going to fetch info"},
	            {1, "------SD_ControlNetsList_UI::FetchData() Error: "},
	            {2, "------SD_ControlNetsList_UI::FetchData() CTRLNET obtained info:\n\n" },
	        };
	        Debug.Log(dict[KeyIx] + suffix);
	        #endif
	    }


	    void DEBUG_EnsureCount(int KeyIx, string suffix=""){
	        #if SP_VERBOSE_CTRLNET_DEBUG
	        Dictionary<int, string> dict = new Dictionary<int, string>(){
	            {0, "------SD_ControlNetsList_UI::EnsureCount() EnsureExact_num_CTRLnets entered"},
	            {1, "------SD_ControlNetsList_UI::EnsureCount() excess 0, all good"},
	            {2, $"------SD_ControlNetsList_UI::EnsureCount() too many "},
	            {3, $"------SD_ControlNetsList_UI::EnsureCount() too few "},
	        };
	        Debug.Log(dict[KeyIx] + suffix);
	        #endif
	    }
	}




	//response to  GET /model_list?update=true

	    [Serializable]
	    public class CTRLnets_ModelList{
	        public string[] model_list;
	        public static CTRLnets_ModelList CreateFromJSON(string jsonString){
	            // Use class-type information, to support inheritance of objects:
	            var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	            return JsonConvert.DeserializeObject<CTRLnets_ModelList>(jsonString, settings);
	        }
	    }


	//response to GET /module_list?alias_names=false

	    [System.Serializable]
	    public class CTRLnets_SliderDetail{
	        public string name;
	        public float value;
	        public float min;
	        public float max;
	        public float step;
	    }

	    [System.Serializable]
	    public class CTRLnets_ModuleDetail{
	        public bool model_free;
	        public CTRLnets_SliderDetail[] sliders;
	    }

	    [System.Serializable]
	    public class CTRLnets_PreprocessorsList{
	        public string[] module_list;
	        public Dictionary<string, CTRLnets_ModuleDetail> module_detail;
	        public static CTRLnets_PreprocessorsList CreateFromJSON(string jsonString){
	            // Use class-type information, to support inheritance of objects:
	            var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	            return JsonConvert.DeserializeObject<CTRLnets_PreprocessorsList>(jsonString, settings);
	        }
	    }


	//response to AFTER GET /control_types

	    public class ControlTypeDetails
	    {
	        [JsonProperty("module_list")]
	        public string[] module_list;
	        [JsonProperty("model_list")]
	        public string[] model_list;
	        [JsonProperty("default_option")]
	        public string default_option;
	        [JsonProperty("default_model")]
	        public string default_model;
	    }

	    public class ControlTypesResponse
	    {
	        [JsonProperty("control_types")]
	        public Dictionary<string, ControlTypeDetails> control_types;
	        public static ControlTypesResponse CreateFromJSON(string jsonString){
	            try{
	                // Use class-type information, to support inheritance of objects:
	                var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	                return JsonConvert.DeserializeObject<ControlTypesResponse>(jsonString, settings);
	            }
	            catch (Exception e){ // Catching a more general exception
	                UnityEngine.Debug.LogError("Exception during JSON deserialization: " + e.Message);
	                return null;
	            }
	        }
	    }


	//response to GET /settings
	    [System.Serializable]
	    public class CTRLnets_Settings{
	        public int control_net_unit_count; //how many actual NET UNITS, not the model types.
	        public int control_net_max_models_num; //older variant of the count, some users had it.
	        public int num_units(){ 
	            return Mathf.Max(control_net_unit_count, control_net_max_models_num); 
	        }

	        public static CTRLnets_Settings CreateFromJSON(string jsonString){
	            // Use class-type information, to support inheritance of objects:
	            var settings = new JsonSerializerSettings{ TypeNameHandling = TypeNameHandling.Auto, };
	            return JsonConvert.DeserializeObject<CTRLnets_Settings>(jsonString, settings);
	        }
	    }
}//end namespace
