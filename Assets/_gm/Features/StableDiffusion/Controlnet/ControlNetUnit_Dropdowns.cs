using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using System.Linq;
using UnityEngine.UI;

namespace spz {

	// Helper-class of the control-net-unit. It contains the UI dropdowns of the unit.
	// These dropdowns are for control-neural-net (model), for the preprocessor, etc.
	public class ControlNetUnit_Dropdowns : MonoBehaviour{
	    [SerializeField] ControlNetUnit_UI _myUnit;
	    [Space(10)]
	    [SerializeField] TMP_Dropdown _preprocessor_dropdown;
	    [SerializeField] TMP_Dropdown _model_dropdown;
	    [SerializeField] TMP_Dropdown _controlType_dropdown;//For presets of "preprocessor + model". not used right now..
	    [Space(10)]
	    [SerializeField] CircleSlider_Snapping_UI _startingControl_step;
	    [SerializeField] ControlNetUnit_ThreshSliders _threshSliders;
	    [SerializeField] GameObject _contextMenu_gameObj;
	    [SerializeField] Toggle _imgToSend_none_toggle;

	    // if we loaded from a save-file, we migth want to select a model.
	    // If we are not connected, this model won't be in the dropdown.
	    // But we can try to find it as soon a we connect next time;
	    string _prefferedPreProcessor_viaLoad = "";
	    string _prefferedModel_viaLoad = "";

	    public void CopyFromAnother(ControlNetUnit_Dropdowns dropdowns){
	        _preprocessor_dropdown.options = dropdowns._preprocessor_dropdown.options;
	        _preprocessor_dropdown.value   = dropdowns._preprocessor_dropdown.value;

	        _model_dropdown.options = dropdowns._model_dropdown.options;
	        _model_dropdown.value = dropdowns._model_dropdown.value;

	        if (_controlType_dropdown != null){
	            _controlType_dropdown.options = dropdowns._controlType_dropdown.options;
	            _controlType_dropdown.value = dropdowns._controlType_dropdown.value;
	        }
	    }

	    public string currPreprocessorName(){
	        if(_preprocessor_dropdown.options.Count == 0){ return "None"; }
	        string chosen = _preprocessor_dropdown.options[_preprocessor_dropdown.value].text;
	        // True Forge Neo lookup is case-sensitive: supported_preprocessors["None"] — lowercase "none" KeyErrors and CN is skipped.
	        if (string.IsNullOrEmpty(chosen) || chosen.Equals("none", System.StringComparison.OrdinalIgnoreCase))
	            return "None";
	        return chosen;
	    }
	    public string currModelName(){
	        if(_model_dropdown.options.Count == 0){ return "None"; }
	        string chosen = _model_dropdown.options[_model_dropdown.value].text;
	        if(SdDisconnectPlaceholder.IsPlaceholder(chosen)){  return "None"; }
	        // Exact None only — Contains("none") false-positives models whose names embed that substring.
	        if (string.IsNullOrEmpty(chosen) || chosen.Equals("none", System.StringComparison.OrdinalIgnoreCase))
	            return "None";
	        return chosen;
	    }

	    public bool is_currPreprocessor_none => currPreprocessorName().ToLower()=="none";
	    public bool is_currModel_none => currModelName().ToLower()=="none";
	    public bool isReferencePreprocessor() => currPreprocessorName().ToLower().Contains("reference");
	    public static bool hasAtLeastSomeModel { get; private set; } = false;

	    /// <summary>Agent / MCP: set model dropdown to None (disable this unit's CN weights).</summary>
	    public bool TrySelectModelNone() => TrySelectModelByName("None", out _, out _);

	    /// <summary>Select preprocessor by name (partial match). Flux2 Union expects None + ready control image.</summary>
	    public bool TrySelectPreprocessorByName(string name, out string resolvedName, out string error){
	        resolvedName = "";
	        error = null;
	        if (string.IsNullOrEmpty(name)){ error = "Preprocessor name is empty"; return false; }
	        if (_preprocessor_dropdown == null){ error = "ControlNet preprocessor dropdown missing"; return false; }
	        string want = name.Trim();
	        if (_preprocessor_dropdown.options.Count == 0){ error = "ControlNet preprocessor dropdown empty"; return false; }
	        int ix = _preprocessor_dropdown.options.FindIndex(o =>
	            o.text != null && string.Equals(o.text, want, StringComparison.OrdinalIgnoreCase));
	        // Do not IndexOf-match "None" — would hit any module whose name embeds "none".
	        if (ix < 0 && !want.Equals("None", StringComparison.OrdinalIgnoreCase)){
	            ix = _preprocessor_dropdown.options.FindIndex(o =>
	                o.text != null && o.text.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
	        }
	        if (ix < 0){ error = "Preprocessor not in dropdown: " + want; return false; }
	        _preprocessor_dropdown.value = ix;
	        _preprocessor_dropdown.RefreshShownValue();
	        resolvedName = currPreprocessorName();
	        return true;
	    }

	    /// <summary>Agent / MCP: select ControlNet model by name (partial match). Use "None" to clear.</summary>
	    public bool TrySelectModelByName(string name, out string resolvedName, out string error){
	        resolvedName = "";
	        error = null;
	        if (string.IsNullOrEmpty(name)){ error = "ControlNet model name is empty"; return false; }
	        if (_model_dropdown == null){ error = "ControlNet model dropdown missing"; return false; }
	        string want = name.Trim();
	        if (_model_dropdown.options.Count == 0 && want.Equals("None", StringComparison.OrdinalIgnoreCase)){
	            _model_dropdown.options.Insert(0, new TMP_Dropdown.OptionData("None"));
	        }
	        if (_model_dropdown.options.Count == 0){ error = "ControlNet model dropdown empty"; return false; }
	        if (want.Equals("None", StringComparison.OrdinalIgnoreCase)
	            && !_model_dropdown.options.Exists(o => o.text != null && o.text.Equals("None", StringComparison.OrdinalIgnoreCase))){
	            _model_dropdown.options.Insert(0, new TMP_Dropdown.OptionData("None"));
	        }
	        int ix = _model_dropdown.options.FindIndex(o =>
	            o.text != null && string.Equals(o.text, want, StringComparison.OrdinalIgnoreCase));
	        if (ix < 0){
	            ix = FindIndex_matchingBaseName(_model_dropdown.options, want);
	        }
	        // Do not IndexOf-match "None" — would hit models whose names embed "none" (e.g. nonexistent_*).
	        if (ix < 0 && !want.Equals("None", StringComparison.OrdinalIgnoreCase)){
	            ix = _model_dropdown.options.FindIndex(o =>
	                o.text != null && o.text.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
	        }
	        if (ix < 0){
	            error = "ControlNet model not in dropdown: " + want;
	            return false;
	        }
	        _model_dropdown.value = ix;
	        _model_dropdown.RefreshShownValue();
	        resolvedName = currModelName();
	        return true;
	    }


    
	    //'castToLowercase' is important, some users have models with _Depth_ instead of _depth_ in their name
	    public bool HasString(string substr, bool castToLowercase=true){
	        substr = castToLowercase? substr.ToLower() : substr;

	        if (_preprocessor_dropdown.options.Count > 0){
	            var preprocessor = _preprocessor_dropdown.options[_preprocessor_dropdown.value];
	            string prep_name =  castToLowercase?  preprocessor.text.ToLower() : preprocessor.text;
	            if(prep_name.Contains(substr)){ return true; }
	        }
	        if (_model_dropdown.options.Count > 0){
	            var model = _model_dropdown.options[_model_dropdown.value];
	            string model_name = castToLowercase ? model.text.ToLower() : model.text;
	            if(model_name.Contains(substr)){ return true; }
	        }
	        return false;
	    }


	    public void OnRefreshInfo_Complete( out bool isNeedDownloadMandatoryModel_ ){
	        bool pickDepth_ifWasNone = false;
	        UpdateDropdown( _preprocessor_dropdown,  SD_ControlNetsList_UI.instance._preprocessors_list.module_list,
	                        pickDepth_ifWasNone,  ref _prefferedPreProcessor_viaLoad );

	        // First populate only: prefer a family-matching depth CN when the list was empty.
	        // Do NOT treat user-chosen "None" as empty — refreshes were re-forcing depth and blocking disable.
	        // Klein: FindPreferredDepthModelIndex returns -1. FLUX.2-dev: prefers Fun-Union via same helper.
	        pickDepth_ifWasNone = true;
	        // 'None' model is allowed with preprocessor 'reference_only' (Apr 2024).
	        pickDepth_ifWasNone &= currPreprocessorName().ToLower().Contains("ref")==false;

	        UpdateDropdown( _model_dropdown,  SD_ControlNetsList_UI.instance._models.model_list,
	                        pickDepth_ifWasNone,  ref _prefferedModel_viaLoad);

	        //When StableProjectorz was launched for the first time, there are no models at all.
	        //In this case, show big button, prompting the user to install a first model for this ControlNetUnit:
	        hasAtLeastSomeModel = _model_dropdown.options.Count > 0;

	        bool mustDownload  =  _model_dropdown.options.Count == 0;
	             mustDownload |=  _model_dropdown.options.Count==1 && _model_dropdown.options[0].text.ToLower()=="none";
	             mustDownload |= ControlNetUnit_DownloadHelper.isSomeUnit_downloadingModels;

	        _model_dropdown.gameObject.SetActive(!mustDownload);
	        isNeedDownloadMandatoryModel_ = mustDownload;
	    }


	    void UpdateDropdown( TMP_Dropdown dropdown,  string[] choices,  bool pickDepth_ifWasNone,  ref string prefferedVal_via_Load_ ){
	        string prevChoice =  dropdown.options.Count==0 ? "" : dropdown.options[dropdown.value].text;
	        int prevIx = dropdown.value;
        
	        //ensure 'none' option exists. Users might need it (for "ReferenceOnly"), and some webui don't return it explicitly.
	        //NOTICE: A1111 uses lowercase 'none' ('None' isn't working).
	        var newOptions = choices.Select(c=>new TMP_Dropdown.OptionData(c)).ToList();
	        if(!newOptions.Exists(opt => opt.text.ToLower()=="none")){ newOptions.Insert(0, new TMP_Dropdown.OptionData("None")); }

	        dropdown.ClearOptions();
	        dropdown.AddOptions(newOptions);

	        //reset index if old index no logner leads to the same option.
	        if(newOptions.Count > 0){
	            bool changed =  (prevIx>=dropdown.options.Count) ||  (prevChoice != dropdown.options[prevIx].text);
	            if(changed){
	                // Hash suffix in Forge names (e.g. "name [abcd1234]") can change across refresh;
	                // recover by basename so we do not silently fall back to None / wrong family.
	                int recovered = FindIndex_matchingBaseName(dropdown.options, prevChoice);
	                dropdown.value = recovered >= 0 ? recovered : 0;
	            }
	            else{ dropdown.SetValueWithoutNotify(prevIx); }

	            // Only auto-pick depth on first fill (no prior selection). Explicit "None" must stick.
	            pickDepth_ifWasNone &= string.IsNullOrEmpty(prevChoice);
	            if(pickDepth_ifWasNone){//if we didn't have a value, ensure the dropdown defaults to 'Depth', rather than to 'None'.
	                // Prefer XL depth when the active checkpoint looks like SDXL — first "*depth*" is often SD1.5 and causes Forge "cannot be multiplied" / SPZ "incorrect ControlNet" hint.
	                // Map through option text: dropdown may have inserted lowercase "none" at index 0 while `choices` did not.
	                int choiceIx = FindPreferredDepthModelIndex(choices);
	                if (choiceIx >= 0 && choiceIx < choices.Length) {
	                    string pickName = choices[choiceIx];
	                    int optIx = dropdown.options.FindIndex(o => o.text == pickName);
	                    if (optIx < 0)
	                        optIx = FindIndex_matchingBaseName(dropdown.options, pickName);
	                    if (optIx >= 0){
	                        dropdown.value = optIx;
	                        // Fun-Union names lack "depth" — arm Depth send so Gen Art depth gate / isForDepth() work.
	                        MaybeArmDepthSendForFlux2Pick(pickName);
	                    }
	                }
	            }
	        }

	        dropdown_LoadSavedVal_maybe(dropdown, ref prefferedVal_via_Load_);
	        dropdown.RefreshShownValue();
	    }


	    /// <summary>Pick a depth ControlNet that matches the active SD checkpoint family (SDXL vs SD1.5 vs Flux2).</summary>
	    static int FindPreferredDepthModelIndex(string[] choices){
	        if (choices == null || choices.Length == 0) return -1;

	        bool wantXl = false;
	        string sd = null;
	        try {
	            sd = SD_InputPanel_UI.instance != null ? SD_InputPanel_UI.instance.models?.selectedModel_name : null;
	            wantXl = CheckpointLooksXl(sd);
	        } catch { /* dropdown refresh can run before input panel is ready */ }

	        // Klein-4B: Fun-Union / Flux2 CN does not lock geometry — ImageStitch structure instead.
	        if (SD_OptionsPacket.CheckpointNeedsKleinModules(sd))
	            return -1;
	        // FLUX.2-dev: prefer Fun-Union (or any Flux2 CN) on first populate / heal path.
	        if (SD_OptionsPacket.CheckpointLooksFlux2Dev(sd))
	            return FindPreferredFlux2ModelIndex(choices);

	        int anyDepth = -1, bestXl = -1, bestNonXl = -1;
	        for (int i = 0; i < choices.Length; i++){
	            string c = choices[i] ?? "";
	            if (c.IndexOf("depth", StringComparison.OrdinalIgnoreCase) < 0) continue;
	            if (anyDepth < 0) anyDepth = i;
	            if (ControlNetModelLooksXl(c)){ if (bestXl < 0) bestXl = i; }
	            else if (bestNonXl < 0) bestNonXl = i;
	        }

	        if (wantXl && bestXl >= 0) return bestXl;
	        if (!wantXl && bestNonXl >= 0) return bestNonXl;
	        return anyDepth;
	    }

	    /// <summary>Prefer Fun-Controlnet-Union (2602 if present), else any Flux2 ControlNet name.</summary>
	    public static int FindPreferredFlux2ModelIndex(string[] choices){
	        if (choices == null || choices.Length == 0) return -1;
	        int anyFlux = -1, bestFun = -1, bestFun2602 = -1;
	        for (int i = 0; i < choices.Length; i++){
	            if (!ControlNetModelLooksFlux2(choices[i])) continue;
	            if (anyFlux < 0) anyFlux = i;
	            string n = (choices[i] ?? "").ToLowerInvariant();
	            if (n.Contains("fun-controlnet") || n.Contains("fun_controlnet")){
	                if (n.Contains("2602")){ bestFun2602 = i; break; }
	                if (bestFun < 0) bestFun = i;
	            }
	        }
	        if (bestFun2602 >= 0) return bestFun2602;
	        return bestFun >= 0 ? bestFun : anyFlux;
	    }

	    void MaybeArmDepthSendForFlux2Pick(string pickName){
	        if (_myUnit == null || string.IsNullOrEmpty(pickName)) return;
	        if (!ControlNetModelLooksFlux2(pickName)) return;
	        string sd = null;
	        try { sd = SD_InputPanel_UI.instance?.models?.selectedModel_name; } catch { /* */ }
	        if (!SD_OptionsPacket.CheckpointLooksFlux2Dev(sd)) return;
	        _myUnit.TrySetWhatImageToSend(WhatImageToSend_CTRLNET.Depth, allowOpenFileDialog: false);
	        TrySelectPreprocessorByName("None", out _, out _);
	    }

	    public static string FindPreferredDepthModelName(string[] choices){
	        int ix = FindPreferredDepthModelIndex(choices);
	        return ix >= 0 && choices != null ? choices[ix] : null;
	    }

	    public static bool CheckpointLooksXl(string checkpointName){
	        if (string.IsNullOrEmpty(checkpointName)) return false;
	        string n = checkpointName.ToLowerInvariant().Replace('\\', '/');
	        // Avoid bare IndexOf("xl") — false-positives "exllama", random "…xl…" stems.
	        if (n.Contains("sdxl") || n.Contains("juggernautxl") || n.Contains("diffusers_xl"))
	            return true;
	        if (n.Contains("_xl_") || n.Contains("-xl-") || n.Contains("/xl/") || n.Contains(" xl"))
	            return true;
	        // Trailing XL token: "modelXL", "model_xl", "model-xl" (not mid-string "exllama").
	        if (n.EndsWith("xl") || n.EndsWith("_xl") || n.EndsWith("-xl"))
	            return true;
	        return false;
	    }

	    public static bool ControlNetModelLooksXl(string cnModelName){
	        if (string.IsNullOrEmpty(cnModelName)) return false;
	        string n = cnModelName.ToLowerInvariant();
	        if (n.Contains("sd15") || n.Contains("sd1.5") || n.Contains("v11")) return false;
	        return n.Contains("sdxl") || n.Contains("diffusers_xl") || n.Contains("_xl_") || n.Contains("-xl-");
	    }

	    /// <summary>Flux.2 / Fun-Controlnet weights (depth-capable). Requires flux2 or fun-controlnet marker.</summary>
	    public static bool ControlNetModelLooksFlux2(string cnModelName){
	        if (string.IsNullOrEmpty(cnModelName)) return false;
	        string n = cnModelName.ToLowerInvariant().Replace('\\', '/');
	        if (n.Equals("none")) return false;
	        // Do not match bare "controlnet-union" — too broad vs InstantX / other unions.
	        return n.Contains("flux.2") || n.Contains("flux2")
	            || n.Contains("fun-controlnet") || n.Contains("fun_controlnet");
	    }

	    /// <summary>True when CN weight family does not match active SD checkpoint (Neo crashes: y is None).</summary>
	    public static bool IsControlNetCheckpointFamilyMismatch(string cnModelName, string checkpointName){
	        if (string.IsNullOrEmpty(cnModelName) || cnModelName.Equals("None", StringComparison.OrdinalIgnoreCase))
	            return false;
	        if (string.IsNullOrEmpty(checkpointName)) return false;
	        // Flux.2 Klein-4B: no alwayson ControlNet (Fun-Union ineffective). Structure via ImageStitch.
	        if (SD_OptionsPacket.CheckpointNeedsKleinModules(checkpointName))
	            return true;
	        // Fun-Union / Flux2 CN pairs with FLUX.2-dev on Forge Neo.
	        if (ControlNetModelLooksFlux2(cnModelName))
	            return !SD_OptionsPacket.CheckpointLooksFlux2Dev(checkpointName);
	        // FLUX.2-dev does not accept SD1.5/XL CN (both look "non-XL" under CheckpointLooksXl).
	        if (SD_OptionsPacket.CheckpointLooksFlux2Dev(checkpointName))
	            return true;
	        return ControlNetModelLooksXl(cnModelName) != CheckpointLooksXl(checkpointName);
	    }


	    static string ControlNetModelBaseName(string name){
	        if (string.IsNullOrEmpty(name)) return "";
	        int bracket = name.LastIndexOf('[');
	        if (bracket > 0) name = name.Substring(0, bracket).TrimEnd();
	        return name.Trim();
	    }


	    static int FindIndex_matchingBaseName(List<TMP_Dropdown.OptionData> options, string wanted){
	        if (options == null || string.IsNullOrEmpty(wanted)) return -1;
	        int exact = options.FindIndex(opt => opt.text == wanted);
	        if (exact >= 0) return exact;
	        string baseWanted = ControlNetModelBaseName(wanted);
	        if (string.IsNullOrEmpty(baseWanted)) return -1;
	        return options.FindIndex(opt =>
	            string.Equals(ControlNetModelBaseName(opt.text), baseWanted, StringComparison.OrdinalIgnoreCase));
	    }


	    void dropdown_LoadSavedVal_maybe(TMP_Dropdown dropdown, ref string prefferedVal_via_Load_){
	        // Check if there is a value we'd prefer to select,
	        // if we Loaded a saved project-file recently:
	        bool wantLoaded = string.IsNullOrEmpty(prefferedVal_via_Load_)==false;
	        if (!wantLoaded){ return; }

	        string wantedVal = prefferedVal_via_Load_;
	        int ix = FindIndex_matchingBaseName(dropdown.options, wantedVal);
	        if(ix>=0){
	            dropdown.value = ix;
	            prefferedVal_via_Load_ = "";//found, no longer need to search for it.
	        }
	    }


	    void OnModelDropdown_ValueChanged(int ix){
	        _threshSliders.OnUnitAltered();

	        string modelText = _model_dropdown.options[ix].text;
	        // Flux2 Fun-Union: control image is often already processed — drop legacy depth_* preprocessors.
	        // Do not force Depth send here (user may want Canny/Pose/CustomFile); first-fill arms Depth via MaybeArmDepthSendForFlux2Pick.
	        try {
	            string sd = SD_InputPanel_UI.instance != null ? SD_InputPanel_UI.instance.models?.selectedModel_name : null;
	            // Klein never keeps Fun-Union (family mismatch); only FLUX.2-dev needs None here.
	            if (ControlNetModelLooksFlux2(modelText)
	                && SD_OptionsPacket.CheckpointLooksFlux2Dev(sd))
	                TrySelectPreprocessorByName("None", out _, out _);
	        } catch { /* input panel may be unset */ }
	        if (modelText.ToLower().Contains("xl_depth")){
	            string msg = "SDXL depth can make Low-Poly-Wireframe renders.  If so, fix it by blurring the Depth:" +
	                        "\nhover the Depth Button (next to the wireframe button) and use its sliders.";
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 8, false);
	        }
	        // If dropdown changed, check if we are doing inpaint.
	        // For inpaint 99% of time we DON'T want to send an image.
	        // This one is very easy to forget, so always do so here automatically, whenever dropdown changes.
	        //   Illyasviel: "You do not need to add image to ControlNet."
	        //   https://github.com/Mikubill/sd-webui-controlnet/discussions/1143#discussion-5155255
	        if(_myUnit.isForInpaint()==false){ return; }
	        bool contextWasActive = _contextMenu_gameObj.activeSelf;
	        _contextMenu_gameObj.SetActive(true);//enableds ToggleGroup while we flick one of the toggles.
	        _imgToSend_none_toggle.isOn = true;
	        _contextMenu_gameObj.SetActive(contextWasActive);
	        Viewport_StatusText.instance.ShowStatusText("For Inpant controlnet we DON'T want to send an image. "
	                                                   +"\nIllyasviel said: 'You do not need to add image to ControlNet.'", false, 6, false);
	    }


	    void OnPreprocessorDropdown_ValChanged(int optionIx){
	        Adjust_others_if_me_referenceOnly();
	        _threshSliders.OnUnitAltered();
	    }//end()


	    string _latestPreprocessorName = "";
	    void Adjust_others_if_me_referenceOnly(){
	        bool isForReference = currPreprocessorName().ToLower().Contains("reference");
	        bool wasForReference = _latestPreprocessorName.ToLower().Contains("reference");
	        _latestPreprocessorName = currPreprocessorName();
        
	        if(wasForReference == isForReference){ return; }//remains the same.

	        bool recoverOriginalVals = !isForReference;
	        if(recoverOriginalVals && SD_ControlNetsList_UI.instance.Num_Active_Reference_CTRLUnit()>0){//NOTICE >0
	            return;//there are other controlnets that have reference.
	        }

	        if(!recoverOriginalVals && SD_ControlNetsList_UI.instance.Num_Active_Reference_CTRLUnit()>1){//NOTICE >1
	            return;//there are other controlnets that have reference, we already adjusted values via them.
	        }

	        // Change the StartingStep values of any Depth or Normal controlnet units.
	        // (either set to greater value, or restore back to zero).
        
	        bool didAdjustVals = false;
	        SD_ControlNetsList_UI.instance.DoForEvery_CtrlUnit( Adjust_StartingStep );

	        void Adjust_StartingStep( ControlNetUnit_UI u,  int u_ix ){
	            if(u == _myUnit){ return; }
	            if(!u.isForDepth() && !u.isForNormals()){ return; }

	            float val =  u.dropdowns._startingControl_step.value;
	            if(!recoverOriginalVals && val == 0.0f){ 
	                val =  0.28f;
	                didAdjustVals = true;
	            }
	            if(recoverOriginalVals){
	                val =  0.0f;
	                didAdjustVals = true;
	            }
	            u.dropdowns._startingControl_step.SetSliderValue( val, invokeCallback:false );
	        }//end act()

	        if(!didAdjustVals){  return; }
        
	        string msg = recoverOriginalVals ? $"You removed <b>Reference</b> preprocessor ...<b>StartStep</b> of <b>Depth</b> and <b>Normal</b> CTRL Nets restored to 0 :)" 
	                                         : 
	                                           $"Picked <b>Reference</b> preprocessor ...<b>StartStep</b> of <b>Depth</b> and <b>Normal</b> CTRL Nets was changed to {0.28} :)" +
	                                           $"\nOtherwise, earlier Depth-controlling usually ruins the reference contribution.";
	        Viewport_StatusText.instance.ShowStatusText(msg, textIsETA_number:false, 7, progressVisibility:false);
	    }


	    void OnSomeUnit_StartDownloadModel(ControlNetUnit_DownloadHelper who) => _model_dropdown.gameObject.SetActive(false);//keep dropdown hidden
	    void OnSomeUnit_StopDownloadModel(ControlNetUnit_DownloadHelper who) => _model_dropdown.gameObject.SetActive(true);


	    public void Save( ControlNetUnit_SL unit_sl ){
	        unit_sl.hasAtLeastSomeModel = hasAtLeastSomeModel;
	        unit_sl.neural_model  = _model_dropdown.options.Count>0? _model_dropdown.options[_model_dropdown.value].text : "";
	        unit_sl.preProcessor  = _preprocessor_dropdown.options.Count>0? _preprocessor_dropdown.options[_preprocessor_dropdown.value].text : "";
	    }

	    public void Load( ControlNetUnit_SL unit_sl ){
	        Load_Dropdown_ifCan(_preprocessor_dropdown, unit_sl.preProcessor, ref _prefferedPreProcessor_viaLoad);
	        Load_Dropdown_ifCan(_model_dropdown, unit_sl.neural_model, ref _prefferedModel_viaLoad);
	    }

	    void Load_Dropdown_ifCan(TMP_Dropdown dropdown, string wantedVal, ref string preferredVal_viaLoad_){
	         int ixInDropdown = dropdown.options.FindIndex( opt => opt.text== wantedVal);

	        if(ixInDropdown >= 0){
	            dropdown.value = ixInDropdown;
	            preferredVal_viaLoad_ = "";
	        }else {//dropdown doesn't contain such a value, remember it for later, when dropdown will be refreshed:
	            preferredVal_viaLoad_ = wantedVal;
	            // But also, if the list is empty, set the model anyway.
	            // That's because we don't need to send JSON to SD, when changing control net model.
	            // We can pretend we have it, so that the list doesn't remain 'none':
	            if(dropdown.options.Count==0){
	                var options = new List<TMP_Dropdown.OptionData>(){ new TMP_Dropdown.OptionData() };
	                options[0].text = preferredVal_viaLoad_;
	                dropdown.AddOptions(options);
	                dropdown.value = 0;
	            }
	        }
	        dropdown.RefreshShownValue();
	    }

	    void Awake(){
	        ControlNetUnit_DownloadHelper._onSomeUnit_startedDownloadModel += OnSomeUnit_StartDownloadModel;
	        ControlNetUnit_DownloadHelper._onSomeUnit_stoppedDownloadModel += OnSomeUnit_StopDownloadModel;
	        _model_dropdown.onValueChanged.AddListener( OnModelDropdown_ValueChanged );
	        _preprocessor_dropdown.onValueChanged.AddListener( OnPreprocessorDropdown_ValChanged );
	        /* _controlType_dropdown.onValueChanged.AddListener( (int ix)=>OnModelDropdown_ValueChanged(_controlType_dropdown) ); */
	    }

	    void OnDestroy(){
	        ControlNetUnit_DownloadHelper._onSomeUnit_startedDownloadModel -= OnSomeUnit_StartDownloadModel;
	        ControlNetUnit_DownloadHelper._onSomeUnit_stoppedDownloadModel -= OnSomeUnit_StopDownloadModel;
	    }

	}
}//end namespace
