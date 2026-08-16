using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System;
using SimpleFileBrowser;

namespace spz {

	//assistant-object, which helps our 'ModelsHandler_3D' to bring a 3d model into the project.
	public class ModelsHandler3D_ImportHelper : MonoBehaviour{
	    static bool IsFbxPath(string path){
		    if(string.IsNullOrEmpty(path)){ return false; }
		    string ext = Path.GetExtension(path);
		    return string.Equals(ext, ".fbx", StringComparison.OrdinalIgnoreCase);
	    }


	    [SerializeField] UDIMs_Helper _udims_helper;
	    [SerializeField] Objs3D_Container o3d;
	    [SerializeField] ModelsHandler_SaveFBX_Helper _saveFBX_helper;
	    GameObject _latestSuccessRoot = null;

	    //used when loading the project from a save file.
	    ModelsHandler_3D_SL _modelsHandler_SL = null;

	    // We remember raw bytes structure, if user loads mesh from file.
	    // Later on, if we decide to save project, we'll just dump these bytes into needed location.
	    // without having to convert our unity meshes into the needed format.
	    public byte[] _modelBytesCache { get; private set; } = null;
	    public string _modelBytesCache_filename { get; private set; } = "";
	    public bool _isImportingModel { get; private set; } = false;
	    /// <summary>Result of the most recently finished import (false if never imported or last run failed).</summary>
	    public bool _lastImportSucceeded { get; private set; } = false;
	    public string _path_recentlyExported { get; private set; } = "";

	    /// <summary>Clears the last dialog/to-path export path so cancel cannot inherit a prior success.</summary>
	    public void ClearRecentlyExportedPath() {
		    _path_recentlyExported = "";
	    }

	    /// <summary>Restore after internal temp writes (e.g. door_temp.fbx) so GO stamp checks keep the real export path.</summary>
	    public void RestoreRecentlyExportedPath( string path ) {
		    _path_recentlyExported = path ?? "";
	    }

	    public Action _Act_onStartedImporting{ get; set; } = null;// isSuccess,What.
	    public Action<bool, GameObject> _Act_onImportComplete { get; set; } = null;// isSuccess,What.
    

	    public bool CanImportFile(string filepath){
	        if (!File.Exists(filepath)){
	            Viewport_StatusText.instance?.ShowStatusText("3d-model file doesn't exist.", false, 1.5f, false);
	            return false;  
	        }
	        return !_isImportingModel;
	    }


	    /// <param name="applyExportAxisBasis">
	    /// False when SPZ is restoring geometry it already owns (project load, the built-in default
	    /// model). Those read the original mesh bytes back off disk, so applying the current EXPORT
	    /// axis preference would let a UI toggle change the shape of an already-saved project.
	    /// </param>
	    public void ImportModel_via_Filepath( string filepath, bool applyExportAxisBasis = true ){

	        if (_isImportingModel) {
		        Debug.LogWarning("[ModelsHandler3D_ImportHelper] ImportModel_via_Filepath refused — already importing.");
		        return;
	        }
	        if (!File.Exists(filepath)) {
		        OnError("3d-model file doesn't exist: " + filepath);
		        return;
	        }

	        // Clear prior success so deferred RPC cannot treat a failed new import as OK.
	        _lastImportSucceeded = false;
	        _isImportingModel = true;

	        // Read the file BEFORE announcing the start: the start callback removes the model currently
	        // in the scene, and this is exactly the path where the read is most likely to fail — SPZ
	        // picks up the exchange FBX as soon as Blender stamps it ready, so the file can still be
	        // locked or mid-flush. Failing after the removal left the user with an empty scene and only
	        // an error message. Nothing is destroyed until the bytes are in hand.
	        try {
		        _modelBytesCache = File.ReadAllBytes(filepath);
		        _modelBytesCache_filename = Path.GetFileName(filepath);
	        } catch (Exception e) {
		        OnError("Could not read file: " + e.Message);
		        return;
	        }

	        // _isImportingModel is already true. If a start listener throws, or StartCoroutine refuses
	        // because this helper's GameObject is inactive, no ImportRoutine ever runs to clear it —
	        // and every later import, CanImportFile and project TryLoad would refuse "already importing".
	        try {
		        _Act_onStartedImporting?.Invoke();

		        // We simulate the progress text here since Assimp is fast/blocking in this implementation
		        Viewport_StatusText.instance?.ShowStatusText($"Importing {Path.GetFileName(filepath)}...", false, 15, true);

		        StartCoroutine(ImportRoutine(filepath, applyExportAxisBasis));
	        } catch (Exception e) {
		        OnError("Could not start the import: " + e.Message);
		        return;
	        }
	    }

	    IEnumerator ImportRoutine(string filepath, bool applyExportAxisBasis)
	    {
	        // Using our custom AssimpLoader wrapper (AssimpNetter)
	        AssimpLoader loader = new AssimpLoader();
	        GameObject loadedGo = null;
	        string error = "";

	        yield return null; // Wait one frame to allow UI to update (show status text)

	        try {
	            loadedGo = loader.Load(filepath, applyExportAxisBasis);
	        } 
	        catch(Exception e) {
	            error = e.Message;
	        }

	        if(loadedGo != null){
	            OnSuccess_AcceptModel(loadedGo);
	        } else {
	            OnError(error);
	        }
	    }


	    void OnError(string errorMsg){
	        string statusMsg = $"Importing failed.\nError: {errorMsg}";
	        Viewport_StatusText.instance?.ShowStatusText(statusMsg, false, 15, true);
	        Resources.UnloadUnusedAssets();
	        // Never leave a deferred project-load SL attached to a failed/aborted import —
	        // the next successful import would otherwise apply the wrong mesh metadata.
	        _modelsHandler_SL = null;
	        _lastImportSucceeded = false;
	        _isImportingModel = false;
	        _Act_onImportComplete?.Invoke(false, null);
	    }


	    void OnSuccess_AcceptModel( GameObject loadedRoot ){
        
	        Resources.UnloadUnusedAssets();
	        if(loadedRoot == null){
		        _modelsHandler_SL = null;
		        _lastImportSucceeded = false;
		        _isImportingModel = false;
		        _Act_onImportComplete?.Invoke(false, null);
		        return;
	        }

	        //set to true again even if was already true (method might have been called separately)
	        _isImportingModel = true;
	        _latestSuccessRoot = loadedRoot;

	        bool success;
	        try {
		        success = o3d.Init(loadedRoot);
	        } catch (Exception ex) {
		        Debug.LogError("[ModelsHandler3D_ImportHelper] Init failed: " + ex.Message);
		        OnError("Model Init failed: " + ex.Message);
		        return;
	        }
	        if(!success){
		        _modelsHandler_SL = null;
		        _lastImportSucceeded = false;
		        _isImportingModel = false;
		        _Act_onImportComplete?.Invoke(false, _latestSuccessRoot);
		        return;
	        }

	        try {
		        _udims_helper.Init_FindAll_UDIMs( o3d.meshes, (pcnt01)=>OnUDIMsProgress01(pcnt01, loadedRoot) );
	        } catch (Exception ex) {
		        Debug.LogError("[ModelsHandler3D_ImportHelper] UDIM scan failed: " + ex.Message);
		        OnError("UDIM scan failed: " + ex.Message);
	        }
	    }


	    void OnUDIMsProgress01(float progress01, GameObject rootObj ){
	        Viewport_StatusText.instance?.ShowStatusText($"Scanning UVs. Progress: {progress01}", false, 2.5f, false);
	        if(progress01<1.0){ return; }
        
	        o3d.meshes.ForEach(sm => sm.TryChange_SelectionStatus(true, out bool isSuccess));
        
	        OnImportComplete();
	        CreateImportedTextures(rootObj); //AFTER the onComplete callbacks
	    }


	    void OnImportComplete(){
	        if(_modelsHandler_SL!=null){  ModelLoaded_complete_ProjectLoad(); }

	        string msg  = "Model loaded successfuly. Press 'F' to focus on it.";
	        float dur = 3f;
	        if (o3d.scaleWasTooLarge_duringImport){
	            msg += " <b>\nBut its scale/units are massive ..or you have distant objects/polygons," +
	                   " inside the FBX.\nIf any camera/rendering issues, or Depth is white" +
	                   " - resize in your 3d software before exporting</b>";
	            dur = 16;
	        }

	        int numUdims = UDIMs_Helper._allKnownUdims.Count;
	        if (numUdims > 1){
	            msg += $"\nUV outside [0,1] range, so using them as {numUdims} UDIMs." +
	                   $"\n<b>Careful:  {numUdims} udims = {numUdims} projectors every Gen Art.  (more VRAM, lower FPS)</b>";
	            dur = 9;
	        }

	        Viewport_StatusText.instance?.ShowStatusText(msg, false, dur, false);
	        _lastImportSucceeded = true;
	        _isImportingModel = false;
	        _Act_onImportComplete?.Invoke(true, _latestSuccessRoot);
	    }


	    void CreateImportedTextures(GameObject rootObj){
	        var diffuse = new List<Texture2D>();
	        var normal = new List<Texture2D>();
	        var scannedTextures = new HashSet<Texture>(); // Prevent duplicates

	        // Scan all renderers in the imported object
	        Renderer[] renderers = rootObj.GetComponentsInChildren<Renderer>(true);

	        foreach(var r in renderers)
	        {
	            foreach(var mat in r.sharedMaterials)
	            {
	                if(mat == null) continue;

	                // Check Standard Unity Shader properties (AssimpLoader maps to these)
	                if(mat.HasProperty("_MainTex"))
	                {
	                    Texture t = mat.mainTexture;
	                    if(t != null && t is Texture2D t2d && !scannedTextures.Contains(t))
	                    {
	                        diffuse.Add(t2d);
	                        scannedTextures.Add(t);
	                    }
	                }

	                if(mat.HasProperty("_BumpMap"))
	                {
	                    Texture t = mat.GetTexture("_BumpMap");
	                    if(t != null && t is Texture2D t2d && !scannedTextures.Contains(t))
	                    {
	                        normal.Add(t2d);
	                        scannedTextures.Add(t);
	                    }
	                }
	            }
	        }

	        if(diffuse.Count > 0){
	            GenData2D_Maker.make_ImportedCustomImages(GenerationData_Kind.UvTextures_FromFile, diffuse);
	        }
	        if(normal.Count > 0){
	            GenData2D_Maker.make_ImportedCustomImages(GenerationData_Kind.UvNormals_FromFile, normal);
	        }
	    }

    
	    void ModelLoaded_complete_ProjectLoad(){

	        SD_3D_Mesh_UniqueIDMaker.OnLoad_ResetIds();
	        o3d.meshID_to_mesh.Clear();
	        o3d.nonSelectedMeshes.Clear();
        
	        for(int i=0; i<o3d.meshes.Count; ++i){
		        if( _modelsHandler_SL.meshes == null || i >= _modelsHandler_SL.meshes.Count ){
			        UnityEngine.Debug.LogWarning("[ModelsHandler3D_ImportHelper] ProjectLoad: mesh SL count mismatch at i=" + i);
			        break;
		        }
	            SD_3D_Mesh_SL sl = _modelsHandler_SL.meshes[i];
	            o3d.meshes[i].Load( sl );
	            o3d.meshID_to_mesh.Add( sl.unique_id, o3d.meshes[i] );//we cleared above, so re-add with a new ID.
	        }

	        HashSet<ushort> selectedMeshesId = new HashSet<ushort>(_modelsHandler_SL.selectedMeshes );
	        for(int i=0; i<o3d.meshes.Count; ++i){
	            bool isSelect =  selectedMeshesId.Contains( o3d.meshes[i].unique_id );
	            bool success;
	            //invokes callbacks etc:
	            o3d.meshes[i].TryChange_SelectionStatus( isSELECT:isSelect, out success,  
	                                                     isDeselectOthers:false, preventDeselect_ifLast:true);
	            if(!isSelect){ o3d.nonSelectedMeshes.Add(o3d.meshes[i]);  }
	        }

	        // Restore model facing. Import/fit left the root at identity; a legacy project (null euler)
	        // gets the old 180° import yaw so it faces as authored, newer saves get their exact rotation.
	        o3d.ApplyLoadedRootRotation( _modelsHandler_SL.currModelRoot_rotationEuler );

	        _modelsHandler_SL = null;
	    }


	    public void SaveCachedMesh_toFile(string pathWithExten=null, Action<string> afterMeshWritten=null, Action onCancelledOrFailed=null){
	        if(_modelBytesCache == null){
		        onCancelledOrFailed?.Invoke();
		        return;
	        }
        
	        // Define local callback to handle saving (replaces previous flow with confirmation popup)
	        void onComplete(string path){
	            if(string.IsNullOrEmpty(path)){
		            onCancelledOrFailed?.Invoke();
		            return;
	            }
	            try {
		            var dir = Path.GetDirectoryName(path);
		            if (!string.IsNullOrEmpty(dir)) {
			            Directory.CreateDirectory( dir );
		            }
		            File.WriteAllBytes(path, _modelBytesCache);
	            } catch (Exception ex) {
		            UnityEngine.Debug.LogWarning("[ModelsHandler3D_ImportHelper] SaveCachedMesh_toFile failed: " + ex.Message);
		            onCancelledOrFailed?.Invoke();
		            return;
	            }
	            // Stamp path only after a successful write — otherwise export _isSaving never clears.
	            _path_recentlyExported = path;
	            Viewport_StatusText.instance?.ShowStatusText("Exported the mesh to\n"+path, false, 5, false);
	            afterMeshWritten?.Invoke(path);
	        }

	        if(string.IsNullOrEmpty(pathWithExten)){//allow user to select directory manually
	            string fname = Path.GetFileNameWithoutExtension(_modelBytesCache_filename);
	            string exten = Path.GetExtension(_modelBytesCache_filename).TrimStart('.'); // SimpleFileBrowser needs "obj", not ".obj"

	            FileBrowser.SetFilters(true, new FileBrowser.Filter("3D Model", exten));
	            FileBrowser.SetDefaultFilter(exten);

	            FileBrowser.ShowSaveDialog((paths) => {
	                if(paths != null && paths.Length > 0) onComplete(paths[0]);
	                else onCancelledOrFailed?.Invoke();
	            }, () => onCancelledOrFailed?.Invoke(), FileBrowser.PickMode.Files, false, null, fname, "Save Mesh", "Save");
	        }
	        else{
	            // Path provided directly (e.g. from script or known location), check existing handled by OS or caller logic mostly
	            // But here we just save directly as requested.
	            onComplete(pathWithExten);
	        }
	    }


	    public void SaveDefaultDoor_toFile(string pathWithExten=null, Action<string> afterMeshWritten=null, Action onCancelledOrFailed=null){
        
	        void PerformSave(string path){
	             path = Path.ChangeExtension(path, "fbx");
	            if (o3d == null || o3d.currModelRootGO == null) {
		            UnityEngine.Debug.LogWarning("[ModelsHandler3D_ImportHelper] SaveDefaultDoor_toFile: no current model root GO; cannot re-export FBX scene.");
		            onCancelledOrFailed?.Invoke();
		            return;
	            }
	            // Undo SPZ fit-to-volume for DCC round-trip (Blender default-cube litmus).
	            // User global scale is preserved; only the import shrink/grow is removed.
	            bool undidFit = o3d.TryBeginFbxExportAuthoringScale( out var restoreScale );
	            bool wrote;
	            try {
		            wrote = _saveFBX_helper.SaveModels(path, o3d.currModelRootGO);
	            } finally {
		            if( undidFit ){
			            o3d.EndFbxExportAuthoringScale( restoreScale );
		            }
	            }
	            if (!wrote) {
		            UnityEngine.Debug.LogWarning("[ModelsHandler3D_ImportHelper] SaveDefaultDoor_toFile: FBX export failed: " + path);
		            onCancelledOrFailed?.Invoke();
		            return;
	            }
	            _path_recentlyExported = path;
	            afterMeshWritten?.Invoke(path);
	        }

	        if(string.IsNullOrEmpty(pathWithExten)){//allow user to select directory manually
	            FileBrowser.SetFilters(true, new FileBrowser.Filter("FBX", "fbx"));
	            FileBrowser.SetDefaultFilter("fbx");
            
	            FileBrowser.ShowSaveDialog((paths) => {
	                if(paths != null && paths.Length > 0) PerformSave(paths[0]);
	                else onCancelledOrFailed?.Invoke();
	            }, () => onCancelledOrFailed?.Invoke(), FileBrowser.PickMode.Files, false, null, "StableProjectorz_door", "Save Door", "Save");
	        }
	        else{
	            PerformSave(pathWithExten);
	        }
	    }


	    /// <summary>Non-interactive export: write the current model to the given full path (cached original format, or FBX of scene root).</summary>
	    public void ExportModelToPath( string absolutePath ){
		    if( string.IsNullOrEmpty(absolutePath) ){
			    return;
		    }
		    // Clear stale path so callers cannot treat a previous export as this request's result.
		    _path_recentlyExported = "";
		    // Blender 4+ rejects ASCII FBX. For any explicit .fbx target, always re-export
		    // from the current scene through the Unity FBX writer helper (binary-preferred),
		    // instead of writing cached imported bytes verbatim.
		    if( IsFbxPath( absolutePath ) ){
			    if( o3d != null && o3d.currModelRootGO != null ){
				    SaveDefaultDoor_toFile( absolutePath );
				    return;
			    }
			    // Fallback: if no live model root exists but cached bytes do, still export those bytes
			    // so automation does not fail hard (may still be ASCII depending on source file).
			    if( _modelBytesCache != null ){
				    UnityEngine.Debug.LogWarning("[ModelsHandler3D_ImportHelper] ExportModelToPath: no live model root; writing cached bytes for .fbx path.");
				    SaveCachedMesh_toFile( absolutePath );
				    return;
			    }
			    UnityEngine.Debug.LogWarning("[ModelsHandler3D_ImportHelper] ExportModelToPath: no live model root and no cached mesh bytes.");
			    return;
		    }
		    if( _modelBytesCache != null ){
			    SaveCachedMesh_toFile( absolutePath );
		    }else{
			    SaveDefaultDoor_toFile( absolutePath );
		    }
	    }


	    public void Save( StableProjectorz_SL spz ){
	        string fp_relativeToDataDir;

	        if (_modelBytesCache != null){
	            fp_relativeToDataDir = _modelBytesCache_filename; 
	            string fp = Path.Combine(spz.filepath_dataDir, fp_relativeToDataDir);
	            bool wrote = false;
	            Exception writeEx = null;
	            SaveCachedMesh_toFile(fp,
		            afterMeshWritten: _ => { wrote = true; },
		            onCancelledOrFailed: () => { writeEx = new InvalidOperationException("cached mesh write failed"); });
	            // Path-provided SaveCachedMesh is synchronous.
	            if (!wrote) {
		            throw writeEx ?? new InvalidOperationException("cached mesh write failed for project save");
	            }
	        }else{
	            if (o3d == null || o3d.currModelRootGO == null) {
		            throw new InvalidOperationException("no model root to write into project _Data");
	            }
	            fp_relativeToDataDir = o3d.currModelRootGO.name + ".fbx";
	            string fp = Path.Combine(spz.filepath_dataDir, fp_relativeToDataDir);
	            // Orientation is restored from the SL on load (ApplyLoadedRootRotation), so do not bake
	            // the root rotation into this FBX too — that would double-apply on reload. Write the
	            // authoring pose (identity) and restore the live rotation afterwards.
	            Transform rootT = o3d.currModelRootGO.transform;
	            Quaternion savedRootRot = rootT.localRotation;
	            rootT.localRotation = Quaternion.identity;
	            bool wrote;
	            try {
		            wrote = _saveFBX_helper.SaveModels(fp, o3d.currModelRootGO);
	            } finally {
		            rootT.localRotation = savedRootRot;
	            }
	            if (!wrote) {
		            throw new InvalidOperationException("FBX mesh write failed for project save: " + fp);
	            }
	        }
	        spz.modelsHandler3D.currModelRootGO = fp_relativeToDataDir;
	    }


	    public void Load(ModelsHandler_3D_SL sl, string dataDir){
	        if (!TryLoad(sl, dataDir, out var error)) {
		        Debug.LogWarning("[ModelsHandler3D_ImportHelper] Project mesh Load refused: " + error);
	        }
	    }

	    /// <summary>
	    /// Attach project mesh SL only when a new import can actually start.
	    /// Otherwise a concurrent import's completion would apply the wrong SL.
	    /// </summary>
	    public bool TryLoad(ModelsHandler_3D_SL sl, string dataDir, out string error){
	        error = null;
	        if (sl == null) {
		        error = "project mesh payload is null";
		        return false;
	        }
	        if (_isImportingModel) {
		        error = "another mesh import is already in flight";
		        return false;
	        }
	        if (string.IsNullOrEmpty(dataDir) || string.IsNullOrEmpty(sl.currModelRootGO)) {
		        error = "project mesh path is missing";
		        return false;
	        }
	        string fp = Path.Combine(dataDir, sl.currModelRootGO);
	        if (!File.Exists(fp)) {
		        error = "3d-model file doesn't exist: " + fp;
		        return false;
	        }
	        _modelsHandler_SL = sl;
	        // Restoring the project's own mesh bytes: never re-interpret them through the current export
	        // axis preference, or reopening a project after changing that preference returns a differently
	        // oriented mesh while the saved paint/UV data still describes the original one.
	        ImportModel_via_Filepath(fp, applyExportAxisBasis: false);
	        if (!_isImportingModel) {
		        // ImportModel_via_Filepath cleared itself via OnError without starting.
		        _modelsHandler_SL = null;
		        error = "mesh import failed to start";
		        return false;
	        }
	        return true;
	    }


	    void Start(){
	        // Check for child objects (default door logic)
	        GameObject root = transform.childCount>0 ? transform.GetChild(0).gameObject : null;
	        if(root != null)
	        {
	            OnSuccess_AcceptModel(root);
	        }
	        // NOTE: Configuration options that were previously here are now handled by AssimpLoader.
        
	        // 1. "Our inpaint-brush shader needs very smooth model, which has no sharp creases.
	        //    It allows it to fade out the brushing near borders. So 180 and generate."
	        //    -> This is now handled inside AssimpLoader using PostProcessSteps.GenerateSmoothNormals.
        
	        // 2. "ImportNormals = true; ImportTangents = true;"
	        //    -> AssimpLoader uses PostProcessSteps.CalculateTangentSpace.

	        // 3. "ImportBlendShapes = false;// Morpher/BlendShapes glitch the importer"
	        //    -> AssimpLoader logic currently ignores BlendShapes, replicating this behavior.
	    }
	}
}//end namespace
