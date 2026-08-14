using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using SimpleFileBrowser;
using Newtonsoft.Json;
using System.IO;
using UnityEngine.Experimental.Rendering;

namespace spz {

	public class ProjectSaveLoad_Helper : MonoBehaviour {

	    public static Action _onWillMake_FinalCompositeImg { get; set; } = null;
	    public static Action _onMade_FinalCompositeImg { get; set; } = null;

	    Coroutine _finalComposite_crtn;
	    /// <summary>True while <see cref="Save_FinalCompositeTexture_crtn"/> is actually running (not a stale handle after StopAllCoroutines).</summary>
	    bool _finalCompositeActive;

	    // What user used, to save the project. 
	    // We can re-use it for the next saving, so user doesn't have to type it again.
	    string _last_saveFilepath = "";
	    /// <summary>True from SaveProject start through dialog/cancel/write — blocks overlapping Ctrl+S / RPC.</summary>
	    bool _projectSaveInFlight;
	    /// <summary>Set true only after JSON serialize succeeds for the current SaveProject attempt.</summary>
	    public bool LastProjectSaveSucceeded { get; private set; }

	    /// <summary>
	    /// Get last saved project filepath (for add-on API)
	    /// </summary>
	    public string GetLastSaveFilepath() => _last_saveFilepath;

	    public bool IsProjectSaveInFlight => _projectSaveInFlight;


	    public void SaveProject( Action<string> saveFinalTex,  Action<string> onResultMessage){
	        // Do not StopAllCoroutines while headless export owns final-composite / _isSaving —
	        // that orphans texture encode and makes deferred RPC report false success.
	        var sm = Save_MGR.instance;
	        if (_projectSaveInFlight) {
		        onResultMessage?.Invoke("Can't save project while another save dialog/write is already in progress.");
		        saveFinalTex?.Invoke(null);
		        return;
	        }
	        if (sm != null && sm._isSaving) {
		        onResultMessage?.Invoke("Can't save project while an export/save is still writing textures.");
		        saveFinalTex?.Invoke(null);
		        return;
	        }
	        if (sm != null && sm._isLoading) {
		        onResultMessage?.Invoke("Can't save project while a load is still in progress.");
		        saveFinalTex?.Invoke(null);
		        return;
	        }
	        _projectSaveInFlight = true;
	        LastProjectSaveSucceeded = false;
	        StopAllCoroutines();
	        StartCoroutine(SaveProj_crtn(saveFinalTex, onResultMessage));
	    }


	    /// <summary>
	    /// Starts final-composite then invokes <paramref name="saveFinalTex"/>.
	    /// Returns <c>false</c> if an in-flight composite already owns a save/export — callers that
	    /// already set <see cref="Save_MGR._isSaving"/> must clear it (otherwise the flag sticks forever).
	    /// </summary>
	    public bool Save_FinalCompositeTexture(Action saveFinalTex){
	        if(_finalComposite_crtn != null || _finalCompositeActive){
		        // Stopping a live in-flight composite orphans Export3D_with_textures_ToPath's texture encode while
		        // CoRespondWhenProjectSaveIdle still waits on shared _isSaving.
		        var sm = Save_MGR.instance;
		        if (sm != null && sm._isSaving && _finalCompositeActive) {
			        UnityEngine.Debug.LogWarning(
				        "[ProjectSaveLoad_Helper] Refusing to restart final-composite while a save/export is in progress.");
			        return false;
		        }
		        // Stale handle (e.g. StopAllCoroutines left a non-null ref): clear and start so this export is not orphaned.
		        if (_finalComposite_crtn != null) {
			        try { StopCoroutine(_finalComposite_crtn); } catch { /* already stopped */ }
			        _finalComposite_crtn = null;
		        }
		        _finalCompositeActive = false;
		        if (sm != null && sm._isSaving) {
			        UnityEngine.Debug.LogWarning(
				        "[ProjectSaveLoad_Helper] Cleared stale final-composite handle so in-progress export can continue.");
		        }
	        }
	        _finalComposite_crtn = StartCoroutine( Save_FinalCompositeTexture_crtn(saveFinalTex) );
	        return true;
	    }


	    IEnumerator SaveProj_crtn( Action<string> saveFinalTexs,  Action<string> onResultMessage ){
	        try {
	        string defaultName = _last_saveFilepath == "" ? "SPZ_Project" : Path.GetFileNameWithoutExtension(_last_saveFilepath);
        
	        // CHANGED: Using SimpleFileBrowser Coroutine pattern for saving.
	        // Sets up filter for .spz files
	        FileBrowser.SetFilters(true, new FileBrowser.Filter("Project", "spz"));
	        FileBrowser.SetDefaultFilter("spz");

	        // Wait for the dialog to close
	        yield return FileBrowser.WaitForSaveDialog(FileBrowser.PickMode.Files, false, null, defaultName, "Save Project", "Save");

	        if (!FileBrowser.Success){
	            onResultMessage?.Invoke("Didn't save - no file selected.");
	            saveFinalTexs?.Invoke(null);
	            yield break;
	        }

	        string saveFile = FileBrowser.Result[0];

	        if (StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._generating) {
	            onResultMessage?.Invoke("Can't save while generating.");
	            saveFinalTexs?.Invoke(null);
	            yield break;
	        }

	        // NOTE: SimpleFileBrowser automatically handles the "Overwrite?" popup internally, 
	        // so the manual ConfirmPopup_UI logic is removed here to prevent double prompts.

	        var spz = new StableProjectorz_SL();
	        spz.filepath_with_exten = saveFile;
	        spz.filepath_dataDir = CreateDataDir(saveFile);

	        spz.sd_genSettingsInput = new SD_GenSettingsInput_UI();
	        spz.generate3D_inputs = new Generate3D_Inputs_SL();

	        string resultMessage = null;
	        System.Exception gatherEx = null;
	        // No yield inside try/catch (CS1626) — gather+serialize sync only, then restore on throw.
	        try {
	            Performance_MGR.instance?.Save(spz);
	            LeftRibbon_UI.instance?.Save(spz);
	            UserCameras_MGR.instance?.Save(spz);

	            ProjectorCameras_MGR.instance?.Save(spz);
	            SD_InputPanel_UI.instance?.Save(spz.sd_genSettingsInput);
	            StableDiffusion_Prompts_UI.instance?.Save(spz.sd_genSettingsInput);

	            WorkflowRibbon_UI.instance?.Save(spz);
	            SD_WorkflowOptionsRibbon_UI.instance?.Save(spz);
	            if (BrushRibbon_UI.instance != null)
	                BrushRibbon_UI.instance.Save(spz);
	            if (ColorPalette_MGR.instance != null)
	                ColorPalette_MGR.instance.Save(spz);
	            Gen3D_WorkflowOptionsRibbon_UI.instance?.Save(spz);

	            GenData2D_Archive.instance?.Save(spz);
	            SD_ControlNetsList_UI.instance?.Save(spz);
	            ModelsHandler_3D.instance?.Save(spz);
	            ModelsHandler_3D_UI.instance?.Save(spz);
	            SkyboxColorButtons_UI_MGR.instance?.Save(spz);
	            Art2D_IconsUI_List.instance?.Save(spz);
	            ArtBG_IconsUI_List.instance?.Save(spz);
	            Connection_MGR.instance?.Save(spz);
	            if (PaintLayerStack_MGR.instance != null)
	                PaintLayerStack_MGR.instance.Save(spz);

	            Serialize_SPZ_toJSON(saveFile, spz, out resultMessage);
	            LastProjectSaveSucceeded = resultMessage != null
		            && resultMessage.StartsWith("Saved the project", StringComparison.Ordinal);
	            CommitOrRestoreDataDir(spz.filepath_dataDir, LastProjectSaveSucceeded);
	        } catch (System.Exception ex) {
	            gatherEx = ex;
	        }
	        if (gatherEx != null) {
	            CommitOrRestoreDataDir(spz.filepath_dataDir, false);
	            LastProjectSaveSucceeded = false;
	            onResultMessage?.Invoke("Didn't save - " + gatherEx.Message);
	            saveFinalTexs?.Invoke(null);
	            yield break;
	        }
	        if (!LastProjectSaveSucceeded) {
	            onResultMessage?.Invoke(resultMessage);
	            saveFinalTexs?.Invoke(null);
	            yield break;
	        }
	        // Do not report "Saved" yet — final composite / mesh textures may still fail below.

	        // Now, save the final composite-texture, combinging all projections.
	        // This is important, in case the spz file gets corrupted. At least the user will have the png:
	        Action onSaveFinalTex =  ()=>saveFinalTexs( spz.filepath_dataDir + "/FINAL_COMPOSITE_4K.png" );
        
	        bool compositeSkippedBusy = false;
	        if(_finalComposite_crtn != null){
		        var smBusy = Save_MGR.instance;
		        if (smBusy != null && smBusy._isSaving) {
			        compositeSkippedBusy = true;
			        UnityEngine.Debug.LogWarning(
				        "[ProjectSaveLoad_Helper] Project save: final-composite already owned by an in-progress export; skipping restart.");
		        } else {
			        StopCoroutine(_finalComposite_crtn);
			        _finalComposite_crtn = StartCoroutine( Save_FinalCompositeTexture_crtn(onSaveFinalTex) );
			        yield return _finalComposite_crtn;
		        }
	        } else {
		        _finalComposite_crtn = StartCoroutine( Save_FinalCompositeTexture_crtn(onSaveFinalTex) );
		        yield return _finalComposite_crtn;
	        }

	        if (compositeSkippedBusy)
		        resultMessage += " (JSON saved; final composite skipped — another export was writing.)";
	        onResultMessage?.Invoke(resultMessage);

	        _last_saveFilepath = saveFile;
	        _onMade_FinalCompositeImg?.Invoke();
	        } finally {
		        _projectSaveInFlight = false;
	        }
	    }



	    IEnumerator Save_FinalCompositeTexture_crtn( Action saveFinalTex ){
	        _finalCompositeActive = true;
	        try {
		        _onWillMake_FinalCompositeImg?.Invoke();

		            yield return null;//allows any temporary resolution adjustments to occur and be noticed by cameras.
		            yield return null;

		            saveFinalTex();
        
		            while (Save_MGR.instance != null && Save_MGR.instance._isSaving){ yield return null; }

	        } finally {
		        _finalComposite_crtn = null;
		        _finalCompositeActive = false;
	        }

	        _onMade_FinalCompositeImg?.Invoke();
	    }
    
    
	    /// <summary>Set true only after a load dialog completed with a successful CreateFromJSON + apply.</summary>
	    public bool LastProjectLoadSucceeded { get; private set; }

	    /// <summary>Call when a deferred mesh import after load fails — RPC/socket must not keep reporting load ok.</summary>
	    public void NoteDeferredImportOutcome(bool ok){
		    if (!ok) LastProjectLoadSucceeded = false;
	    }

	    // CHANGED: Method signature updated to use Callback Action<string> instead of 'out string',
	    // because SimpleFileBrowser operates asynchronously.
	    public void LoadProject( Action<string> onResult ){
	        LastProjectLoadSucceeded = false;
        
	        FileBrowser.SetFilters(true, new FileBrowser.Filter("Project", "spz"));
	        FileBrowser.SetDefaultFilter("spz");

	        FileBrowser.ShowLoadDialog((paths) => {
	            try {
	            if (paths == null || paths.Length == 0){
	                onResult?.Invoke("Load cancelled — no file selected.");
	                return;
	            }

	            string spzFilepath = paths[0];

	            if (StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._generating){
	                onResult?.Invoke("Can't Load while generating.");
	                return;
	            }
	            string json = File.ReadAllText(spzFilepath);
	            if (string.IsNullOrEmpty(json)){
	                onResult?.Invoke("Error loading the project file. It's empty");
	                return;
	            }

	            string resultMessage_;
	            StableProjectorz_SL spz = StableProjectorz_SL.CreateFromJSON(json, out resultMessage_);
	            spz?.update_dataDir_toCurrent(spzFilepath);
	            if(spz == null){
	                onResult?.Invoke(resultMessage_ ?? "Error loading the project file. The file is corrupted, or an unsupported version");
	                return;
	            }
	            Performance_MGR.instance?.Load(spz);
	            LeftRibbon_UI.instance?.Load(spz);
	            UserCameras_MGR.instance?.Load(spz);

	            ModelsHandler_3D.instance?.Load(spz);//befores the projector cameras
	            ModelsHandler_3D_UI.instance?.Load(spz);
	            ProjectorCameras_MGR.instance?.Load(spz);
	            SD_InputPanel_UI.instance?.Load(spz);
	            if (spz.sd_genSettingsInput != null)
	                StableDiffusion_Prompts_UI.instance?.Load( spz.sd_genSettingsInput );

	            //Jan 2025 not saving for now, because the layout is dynamically created from a text string
	            //TrellisInputTabs_MGR_UI.instance.Load(spz.generate3D_inputs, spz.filepath_dataDir);

	            WorkflowRibbon_UI.instance?.Load(spz);
	            SD_WorkflowOptionsRibbon_UI.instance?.Load(spz);
	            if (BrushRibbon_UI.instance != null && spz.brush_MGR != null)
	                BrushRibbon_UI.instance.Load(spz);
	            if (ColorPalette_MGR.instance != null && spz.colorPalette != null)
	                ColorPalette_MGR.instance.Load(spz);
	            Gen3D_WorkflowOptionsRibbon_UI.instance?.Load(spz);

	            GenData2D_Archive.instance?.Load(spz);
	            SD_ControlNetsList_UI.instance?.Load(spz);
	            SkyboxColorButtons_UI_MGR.instance?.Load(spz);
	            Art2D_IconsUI_List.instance?.Load(spz);
	            ArtBG_IconsUI_List.instance?.Load(spz);
	            Connection_MGR.instance?.Load(spz);
	            if (spz.paintLayerStack != null)
	            {
	                if (PaintLayerStack_MGR.instance == null)
	                {
	                    var go = new GameObject("PaintLayerStack_MGR_Runtime");
	                    go.AddComponent<PaintLayerStack_MGR>();
	                }
	                PaintLayerStack_MGR.instance.Load(spz);
	            }
	            else
		            Inpaint_MaskPainter.instance?.NotifyPaintLayersRestoredFromDisk(false);
	            //2D BACKGROUND mgr?

	            UserCameras_MGR.instance?.OnAfter_AllLoaded();
	            ProjectorCameras_MGR.instance?.OnAfterLoadedAll();
	            GenData2D_Archive.instance?.OnAfter_AllLoaded(spz);
	            Art2D_IconsUI_List.instance?.OnAfter_AllLoaded();
	            ArtBG_IconsUI_List.instance?.OnAfter_AllLoaded();

	            Objects_Renderer_MGR.instance?.ReRenderAll_soon();

	            // Same as SaveProj: so GetProjectDataDirOrSession / SPZ GO exchange use this project, not a prior save or session folder.
	            _last_saveFilepath = spzFilepath;
	            LastProjectLoadSucceeded = true;
            
	            onResult?.Invoke(resultMessage_);
	            }
	            catch (System.Exception ex) {
	                LastProjectLoadSucceeded = false;
	                onResult?.Invoke("Error loading the project: " + ex.Message);
	            }

	        }, 
	        () => {
	             // Cancelled
	             onResult?.Invoke("Load Cancelled");
	        },
	        FileBrowser.PickMode.Files, false, null, null, "Load Project", "Load");
	    }


	    // A folder with the same name as the project-file, but with _Data suffix.
	    // we can store all necessary things in that directory. Textures, etc.
	    string _pendingDataDirBackup;

	    string CreateDataDir(string project_file){
	        _pendingDataDirBackup = null;
	        var directory = Path.GetDirectoryName(project_file);
	        var filenameWithoutExtension = Path.GetFileNameWithoutExtension(project_file);
	        var newDirectoryPath = Path.Combine(directory, filenameWithoutExtension + "_Data");
        
	        if (Directory.Exists(newDirectoryPath)){
	            // Move aside instead of wipe-first so a failed JSON write can restore textures.
	            string bak = newDirectoryPath + "__spz_bak";
	            try {
	                if (Directory.Exists(bak))
	                    Directory.Delete(bak, true);
	                Directory.Move(newDirectoryPath, bak);
	                _pendingDataDirBackup = bak;
	            } catch (System.Exception ex) {
	                UnityEngine.Debug.LogWarning(
	                    "[ProjectSaveLoad_Helper] Could not stage _Data backup; falling back to wipe: " + ex.Message);
	                foreach (var file in Directory.GetFiles(newDirectoryPath)){  File.Delete(file);  }
	                foreach (var dir in Directory.GetDirectories(newDirectoryPath)){  Directory.Delete(dir, true);  }
	            }
	        }
	        if (!Directory.Exists(newDirectoryPath))
	            Directory.CreateDirectory(newDirectoryPath);
	        return newDirectoryPath;
	    }

	    void CommitOrRestoreDataDir(string dataDir, bool saveSucceeded){
	        if (string.IsNullOrEmpty(_pendingDataDirBackup)) return;
	        string bak = _pendingDataDirBackup;
	        _pendingDataDirBackup = null;
	        try {
	            if (saveSucceeded) {
	                if (Directory.Exists(bak))
	                    Directory.Delete(bak, true);
	            } else {
	                if (!string.IsNullOrEmpty(dataDir) && Directory.Exists(dataDir))
	                    Directory.Delete(dataDir, true);
	                if (Directory.Exists(bak))
	                    Directory.Move(bak, dataDir);
	            }
	        } catch (System.Exception ex) {
	            UnityEngine.Debug.LogWarning(
	                "[ProjectSaveLoad_Helper] CommitOrRestoreDataDir failed: " + ex.Message);
	        }
	    }

	    void Serialize_SPZ_toJSON(string file, StableProjectorz_SL spz, out string resultMessage_){
	        var settings = new JsonSerializerSettings{
	            Formatting = Formatting.Indented,
	            TypeNameHandling = TypeNameHandling.Auto, //automatically resolve inheritance/abstract classes.
	            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,//skip cyclical references (Color.linear.linear.linear etc)
	        };
	        string json = JsonConvert.SerializeObject(spz, settings);
	        try{
	            File.WriteAllText(file, json);
	        }catch(IOException ex){
	            resultMessage_ = "Didn't save - " + ex.Message;
	            return;
	        }
	        resultMessage_ = "Saved the project to " + file;
	    }

    
	    public static void Save_Tex2D_To_DataFolder( Texture2D texture2D,  string dataFolder, string pathInDataFolder ){
	        if(texture2D == null){ return; }
	        string filePath = Path.Combine(dataFolder, pathInDataFolder);
	        // Save Texture2D as PNG
	        byte[] bytes = texture2D.EncodeToPNG();
	        File.WriteAllBytes(filePath, bytes);
	        //don't destroy Texture2D, it was provided to us.
	    }


	    public static void Save_RT_To_DataFolder( RenderTexture rt, string dataFolder, string pathInDataFolder ){
	        if(rt == null){ return; }
	        Texture2D texture2D = TextureTools_SPZ.RenderTextureToTexture2D(rt);
	        Save_Tex2D_To_DataFolder(texture2D, dataFolder, pathInDataFolder);
	        GameObject.DestroyImmediate(texture2D);// Clean up, we created it.
	    }


	    public static Texture2D Load_Texture2D_from_DataFolder( string dataFolder, string pathInDataFolder, 
	                                                            GraphicsFormat rtFormat, GraphicsFormat format, 
	                                                            Action<RenderTexture> onBeforeCreate=null,
	                                                            Material blitMat=null){
	        if(string.IsNullOrEmpty(dataFolder)){ return null; }
	        if(string.IsNullOrEmpty(pathInDataFolder)){ return null; }
	        string path = Path.Combine(dataFolder, pathInDataFolder);
	        if (!File.Exists(path)){
	            Debug.LogError($"File not found at {path}");
	            return null;
	        }
	        // Load the image into a temporary texture
	        Texture2D tex2D_temp = new Texture2D(2, 2);
	        if (!tex2D_temp.LoadImage( File.ReadAllBytes(path) )){
	            Debug.LogError($"Failed to load texture at {path}");
	            Destroy(tex2D_temp);
	            return null; // Early return on load failure
	        }
	        // Create a RenderTexture with the desired format
	        RenderTexture rt = new RenderTexture(tex2D_temp.width, tex2D_temp.height, 0, rtFormat, 0);
	        onBeforeCreate?.Invoke(rt);
	        rt.Create();

	        if(blitMat==null){  Graphics.Blit(tex2D_temp, rt);  }
	        else{  Graphics.Blit(tex2D_temp, rt, blitMat);  }

	        // Now, transfer the RenderTexture content to a new Texture2D
	        Texture2D tex2D_result = new Texture2D(tex2D_temp.width, tex2D_temp.height, format, 0, TextureCreationFlags.None);
	        RenderTexture.active = rt;
	        tex2D_result.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
	        tex2D_result.Apply();

	        // Clean up
	        RenderTexture.active = null; // Reset the active RenderTexture
	        Destroy(rt);
	        Destroy(tex2D_temp);

	        return tex2D_result;
	    }


	    public static RenderTexture Load_RT_from_DataFolder( string dataFolder,  string pathInDataFolder, 
	                                                         GraphicsFormat rtFormat,  Action<RenderTexture> onBeforeCreate = null, 
	                                                         Material blitMat=null ){
	        if(string.IsNullOrEmpty(dataFolder)){ return null; }
	        if(string.IsNullOrEmpty(pathInDataFolder)){ return null; }
	        string path = Path.Combine(dataFolder, pathInDataFolder);
	        if (!File.Exists(path)){
	            Debug.LogError($"File not found at {path}");
	            return null;
	        }
	        // Load the image into a temporary texture
	        Texture2D tempTex2D = new Texture2D(2, 2);
	        if (!tempTex2D.LoadImage( File.ReadAllBytes(path) )){
	            Debug.LogError($"Failed to load texture at {path}");
	            Destroy(tempTex2D);
	            return null; // Early return on load failure
	        }
	        // Create a RenderTexture with the desired format
	        RenderTexture rt = new RenderTexture(tempTex2D.width, tempTex2D.height, 0, rtFormat, 0);
	        onBeforeCreate?.Invoke(rt);
	        rt.Create();

	        if(blitMat==null){  Graphics.Blit(tempTex2D, rt);  }
	        else{  Graphics.Blit(tempTex2D, rt, blitMat);  }

	        Destroy(tempTex2D);
	        return rt;
	    }

	}
}//end namespace
