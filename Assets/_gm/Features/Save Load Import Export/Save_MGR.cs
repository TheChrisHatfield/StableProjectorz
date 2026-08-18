using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using SimpleFileBrowser;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem;
using System.IO;
using System.Linq;
using System.Threading.Tasks;


namespace spz {

	public class SaveStatus{//allows for coroutine to keep looping until becomes true.
	    public bool isComplete = false;
	}

	//capable of saving Albedo, AmbientOcclusion textures to disk.
	public class Save_MGR : MonoBehaviour{
	    public static Save_MGR instance { get; private set; } = null;

	    [SerializeField] ProjectSaveLoad_Helper _saveLoad_helper;

	    public bool _isSaving { get; private set; } = false;
	    public bool _isLoading { get; private set; } = false;
	    /// <summary>True when the last projection/view texture dialog export chose a path and finished (not cancel).</summary>
	    public bool LastTextureDialogExportSucceeded { get; private set; } = false;
	    public ProjectSaveLoad_Helper SaveLoadHelper => _saveLoad_helper;

	    // Thompson frame-budget scheduler (mirrors PaintUndo_Scheduler): spreads the export's blocking
	    // GPU readback / encode work across frames so the app stays responsive instead of freezing.
	    readonly ExportFrameScheduler _exportScheduler = new ExportFrameScheduler();

	    // Generous upper bound on cross-frame dilation. Real dilation is a few dozen frames even at 4K,
	    // so this only trips when the dilation manager stops reporting completion at all.
	    const float DilationWatchdogSeconds = 60f;

	    class _EncodeInFlight { public Task task; public Texture2D tex; public string fp; }


	    public void MergeIcons( Action<Dictionary<Texture2D,UDIM_Sector>> onHaveAlbedo,  bool oldIcons_survive=false ){
	        if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't merge icons while another save/export is still writing.", false, 5f, false );
		        onHaveAlbedo?.Invoke(null);
		        return;
	        }
	        _isSaving = true;

	        if( !_saveLoad_helper.Save_FinalCompositeTexture( OnReady1 ) ){
		        _isSaving = false;
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't merge icons while another save/export is still writing.", false, 5f, false );
		        // Callers (GetTextures_FromAllIcons / Gen3D retexture) wait forever without a callback.
		        onHaveAlbedo?.Invoke(null);
		        return;
	        }

	        void OnReady1() => StartCoroutine( WaitForRenderAll_crtn(skipAO_blit:true, OnReady2) );
     
	        void OnReady2(){//save + ensure albedo won't be deleted, - we'll keep using it in new generation:
	            Save_Mesh_Textures(OnHaveAlbedo, "", isDilate: false, forbid_albedoDelete: true,
		            onComplete: _ => ClearSavingIfStillHeld());
	        }

	        void ClearSavingIfStillHeld(){
		        // Save_Mesh_Textures catch path only runs onComplete — never leave MergeIcons sticky.
		        if (_isSaving) _isSaving = false;
	        }

	        void OnHaveAlbedo( Dictionary<Texture2D,UDIM_Sector> albedoDict ){
	            try {
	                var mgr = GenData2D_Archive.instance;
	                if (mgr == null) {
	                    onHaveAlbedo?.Invoke(albedoDict);
	                    return;
	                }
	                var uvTex  = mgr.FindAll_GenData_ofKind( GenerationData_Kind.UvTextures_FromFile );
	                var uvBrush= mgr.FindAll_GenData_ofKind( GenerationData_Kind.UvPaintedBrush );
	                var prTex  = mgr.FindAll_GenData_ofKind( GenerationData_Kind.SD_ProjTextures );
	                var allTex = uvTex.Union(prTex).Union(uvBrush);
	                if(oldIcons_survive == false){ 
	                    foreach (GenData2D genDat in allTex){  mgr.DisposeGenerationData(genDat.total_GUID);  }
	                }
	                onHaveAlbedo?.Invoke(albedoDict);
	            } finally {
	                _isSaving = false;
	            }
	        };
	    }


	    bool IsProjectSaveDialogOrWriteInFlight() {
		    return _saveLoad_helper != null && _saveLoad_helper.IsProjectSaveInFlight;
	    }

	    public void DoSaveProject() {
		    DoSaveProject(null);
	    }

	    public void DoSaveProject(string filepath){
	        // Must not set _isSaving before SaveProject: that helper refuses while _isSaving and
	        // would invoke saveFinalTex(null) without ever clearing a flag we already set (self-deadlock).
	        if( _isSaving ){
		        if( Viewport_StatusText.instance != null ){
			        Viewport_StatusText.instance.ShowStatusText(
				        "Can't save project while an export/save is still writing textures.", false, 5f, false );
		        }
		        return;
	        }
	        if( _saveLoad_helper != null && _saveLoad_helper.IsProjectSaveInFlight ){
		        if( Viewport_StatusText.instance != null ){
			        Viewport_StatusText.instance.ShowStatusText(
				        "Can't save project while another save dialog/write is already in progress.", false, 5f, false );
		        }
		        return;
	        }
	        if( _isLoading ){
		        if( Viewport_StatusText.instance != null ){
			        Viewport_StatusText.instance.ShowStatusText(
				        "Can't save project while a load is still in progress.", false, 5f, false );
		        }
		        return;
	        }
	        if( ModelsHandler_3D.instance != null && ModelsHandler_3D.instance._isImportingModel ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't save project while a mesh import is still in progress.", false, 5f, false );
		        return;
	        }
	        if( Gen3D_API.instance != null && Gen3D_API.instance.isBusy ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't save project while Gen3D is busy.", false, 5f, false );
		        return;
	        }
	        if( StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._generating ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't save project while generating.", false, 5f, false );
		        return;
	        }

	        Action<string> onResultMessage = msg => {
		        if( Viewport_StatusText.instance != null )
			        Viewport_StatusText.instance.ShowStatusText(msg, false, 6, false);
	        };
	        _saveLoad_helper.SaveProject( filepath, onReady1, onResultMessage );
        
	        void onReady1(string path) {
		        if( string.IsNullOrEmpty( path ) ){
			        // Cancelled dialog or busy refuse — never claimed _isSaving.
			        return;
		        }
		        _isSaving = true;
		        OnSaveProjTextures_PathChosen(path, isDilate:true, onReady2);
	        }

	        void onReady2(){
	            //after saving, Unpress any ctrl, alt etc. Else unity might keep thinking they are still pressed:
	            _isSaving = false;
	            StartCoroutine(ResetCtrlKey_AfterLoadSave());
	        }
	    }

	    public void DoLoadProject() {
		    DoLoadProject(null);
	    }

	    public void DoLoadProject(string filepath){
	        // Align with FastPath_API.LoadProject — Ctrl+L / UI must not load over an in-flight export
	        // or open a second dialog that clears _isLoading while the first load still runs.
	        // Also refuse while Save Project dialog is open (_projectSaveInFlight before _isSaving).
	        if( _isSaving || _isLoading || IsProjectSaveDialogOrWriteInFlight() ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        _isLoading
				        ? "Load already in progress."
				        : "Can't load while a save/export is still writing.",
			        false, 5f, false );
		        return;
	        }
	        if( ModelsHandler_3D.instance != null && ModelsHandler_3D.instance._isImportingModel ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't load while a mesh import is still in progress.", false, 5f, false );
		        return;
	        }
	        if( Gen3D_API.instance != null && Gen3D_API.instance.isBusy ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't load while Gen3D is busy.", false, 5f, false );
		        return;
	        }
	        if( StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._generating ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't load while generating.", false, 5f, false );
		        return;
	        }

	        _isLoading = true;
        
	        // CHANGED: LoadProject is now Async, so we use a callback instead of 'out string'
	        Action<string> onLoaded = (resultMessage_) => {
	            try {
	                Viewport_StatusText.instance?.ShowStatusText(resultMessage_, false, 6, false);
	                //after loading, Unpress any ctrl, alt etc. Else unity might keep thinking they are still pressed:
	                StartCoroutine( ResetCtrlKey_AfterLoadSave() );
	            } finally {
	                // Mesh Load kicks ImportModel_via_Filepath async — keep _isLoading until import idle
	                // so socket/RPC does not report success while the model is still loading.
	                var mh = ModelsHandler_3D.instance;
	                if (mh != null && mh._isImportingModel) {
		                StartCoroutine(CoClearLoadingWhenImportIdle());
	                } else {
		                _isLoading = false;
	                }
	            }
	        };
	        if (!string.IsNullOrEmpty(filepath))
		        _saveLoad_helper.LoadProjectFromPath(filepath, onLoaded);
	        else
		        _saveLoad_helper.LoadProject(onLoaded);
	    }

	    IEnumerator CoClearLoadingWhenImportIdle(){
		    float timeoutSec = 120f;
		    float elapsed = 0f;
		    while (ModelsHandler_3D.instance != null
		           && ModelsHandler_3D.instance._isImportingModel
		           && elapsed < timeoutSec) {
			    elapsed += Time.unscaledDeltaTime;
			    yield return null;
		    }
		    var mh = ModelsHandler_3D.instance;
		    bool importOk = mh == null || (!mh._isImportingModel && mh._lastImportSucceeded);
		    // If still importing past timeout, treat as failed for honesty flags.
		    if (mh != null && mh._isImportingModel)
			    importOk = false;
		    _saveLoad_helper?.NoteDeferredImportOutcome(importOk);
		    _isLoading = false;
	    }

	    IEnumerator ResetCtrlKey_AfterLoadSave(){
	        yield return null;
	        yield return null;
	        if( Keyboard.current != null )
		        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
	        if( Mouse.current != null )
		        InputSystem.QueueStateEvent(Mouse.current, new MouseState());
	        if( Pen.current != null )
		        InputSystem.QueueStateEvent(Pen.current, new PenState());
	        Input.ResetInputAxes();//for legacy input system (Input.GetKey etc)
	    }



	    /// <summary>
	    /// Opens a save dialog, writes the mesh, then runs the texture pipeline for that path.
	    /// Returns true once the dialog/export flow was started; false if busy or no mesh handler.
	    /// Texture writes wait until a real path is chosen (cancel clears <see cref="_isSaving"/>).
	    /// </summary>
	    public bool Export3D_with_textures(){
	        if( ModelsHandler_3D.instance==null ){
		        return false;
	        }
	        if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
		        UnityEngine.Debug.LogWarning("[Save_MGR] Export3D_with_textures: refused — another save/export is in progress.");
		        return false;
	        }
	        ModelsHandler_3D.instance.ClearRecentlyExportedPath();
	        _isSaving = true;

	        ModelsHandler_3D.instance.ExportModel(
		        afterMeshWritten: path => {
			        if( string.IsNullOrEmpty( path ) || !File.Exists( path ) ){
				        _isSaving = false;
				        if( Viewport_StatusText.instance!=null ){
					        Viewport_StatusText.instance.ShowStatusText( "Export: mesh file not written.", false, 5f, false );
				        }
				        return;
			        }
			        // remove .obj or fbx — images use default .png beside the mesh:
			        string path_exported3D = Path.ChangeExtension( path, null );
			        if( !_saveLoad_helper.Save_FinalCompositeTexture( OnReady1 ) ){
				        _isSaving = false;
				        if( Viewport_StatusText.instance!=null ){
					        Viewport_StatusText.instance.ShowStatusText(
						        "Export: busy composing textures — try again.", false, 5f, false );
				        }
				        return;
			        }
			        void OnReady1() => StartCoroutine( WaitForRenderAll_crtn( skipAO_blit: true, OnReady2 ) );
			        // The mesh at this path was just overwritten; its maps must be too, or a repeat
			        // export leaves the fresh mesh sitting beside the previous run's textures.
			        void OnReady2() => Save_Mesh_Textures( onHaveAlbedo:null, path_exported3D, isDilate: true,
				        forbid_albedoDelete:false, onComplete:OnComplete, overwriteExisting:true );
			        void OnComplete( bool _ ) => _isSaving = false;
		        },
		        onCancelledOrFailed: () => {
			        _isSaving = false;
		        }
	        );
	        return true;
	    }

	    /// <summary>
	    /// Same as <see cref="Export3D_with_textures"/> but writes the mesh to <paramref name="meshFilePath"/> (no file dialog). Textures use the same base path (extension stripped).
	    /// Returns <c>true</c> if the mesh write was recorded and the rest of the pipeline was started; <c>false</c> on failure to produce a mesh path.
	    /// </summary>
	    public bool Export3D_with_textures_ToPath( string meshFilePath ){
		    if( string.IsNullOrEmpty( meshFilePath ) ){
			    return false;
		    }
		    var mh = ModelsHandler_3D.instance;
		    if( mh==null ){
			    return false;
		    }
		    if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
			    UnityEngine.Debug.LogWarning("[Save_MGR] Export3D_with_textures_ToPath: refused — another save/export is in progress.");
			    return false;
		    }
		    // Callers (in-app GO add-on, TCP/HTTP) may target an exchange folder that does not exist yet.
		    try {
			    string targetDir = Path.GetDirectoryName( meshFilePath );
			    if( !string.IsNullOrEmpty( targetDir ) && !Directory.Exists( targetDir ) )
				    Directory.CreateDirectory( targetDir );
		    } catch( Exception ex ) {
			    UnityEngine.Debug.LogWarning( "[Save_MGR] Export3D_with_textures_ToPath: could not create target dir: " + ex.Message );
			    return false;
		    }
		    // Drop stale ready sidecar before rewriting FBX so Blender cannot auto-import a half-finished export.
		    TryDeleteSpzGoExchangeReadyStamp( meshFilePath );
		    _isSaving = true;
		    mh.ExportModelToPath( meshFilePath );
		    string path_exported3D = mh._path_recentlyExported;
		    if( string.IsNullOrEmpty( path_exported3D ) || !File.Exists( path_exported3D ) ){
			    _isSaving = false;
			    TryDeleteSpzGoExchangeReadyStamp( meshFilePath );
			    if( Viewport_StatusText.instance!=null ){
				    Viewport_StatusText.instance.ShowStatusText( "Export: mesh file not written.", false, 5f, false );
			    }
			    return false;
		    }
		    path_exported3D = Path.ChangeExtension( path_exported3D, null );
		    string meshPathForStamp = mh._path_recentlyExported;
		    if( !_saveLoad_helper.Save_FinalCompositeTexture( OnReady1 ) ){
			    _isSaving = false;
			    TryDeleteSpzGoExchangeReadyStamp( meshFilePath );
			    if( Viewport_StatusText.instance!=null ){
				    Viewport_StatusText.instance.ShowStatusText(
					    "Export: busy composing textures — try again.", false, 5f, false );
			    }
			    return false;
		    }
		    void OnReady1() => StartCoroutine( WaitForRenderAll_crtn( skipAO_blit:true, OnReady2 ) );
		    // Exchange export: the FBX is rewritten in place every run, so the maps must land on the
		    // same names too. Uniquing them is what made repeat exports stack files in the exchange
		    // folder and let Blender re-apply a texture from an earlier export.
		    void OnReady2() => Save_Mesh_Textures( onHaveAlbedo:null, path_exported3D, isDilate: true,
			    forbid_albedoDelete:false, onComplete:OnComplete, overwriteExisting:true );
		    void OnComplete( bool texturesWritten ) {
			    // The stamp is the other application's cue to auto-import "mesh + maps". Writing it after a
			    // texture stage that bailed (no accumulation textures) hands the DCC an untextured mesh and
			    // calls it a finished export, so leave the stamp off and say what happened instead.
			    if( texturesWritten ){
				    // Stamp before clearing _isSaving so waiters (native UI / TCP) never race an absent sidecar.
				    TryWriteSpzGoExchangeReadyStamp( meshPathForStamp );
			    } else {
				    TryDeleteSpzGoExchangeReadyStamp( meshPathForStamp );
				    if( Viewport_StatusText.instance!=null ){
					    Viewport_StatusText.instance.ShowStatusText(
						    "Export: mesh written, textures were not — not marking it ready.", false, 6f, false );
				    }
			    }
			    _isSaving = false;
		    }
		    return true;
	    }

	    /// <summary>
	    /// Sidecar next to exchange FBX so Blender's SPZ GO watcher can auto-import after Export.
	    /// Name: <c>{basename}.spz_go_ready</c> (e.g. from_spz.spz_go_ready).
	    /// </summary>
	    public static string ResolveSpzGoExchangeReadyStampPath( string meshFilePath ){
		    if( string.IsNullOrEmpty( meshFilePath ) ) return null;
		    try {
			    // Prefer the FBX path actually written (export may normalize extension).
			    string stampMeshPath = meshFilePath;
			    var mh = ModelsHandler_3D.instance;
			    if( mh != null && !string.IsNullOrEmpty( mh._path_recentlyExported ) )
				    stampMeshPath = mh._path_recentlyExported;
			    string dir = Path.GetDirectoryName( stampMeshPath );
			    string baseName = Path.GetFileNameWithoutExtension( stampMeshPath );
			    if( string.IsNullOrEmpty( dir ) || string.IsNullOrEmpty( baseName ) ) return null;
			    return Path.Combine( dir, baseName + ".spz_go_ready" );
		    } catch( Exception ex ) {
			    UnityEngine.Debug.LogWarning( "[Save_MGR] SPZ GO ready stamp path: " + ex.Message );
			    return null;
		    }
	    }

	    public static bool SpzGoExchangeReadyStampExists( string meshFilePath ){
		    string stamp = ResolveSpzGoExchangeReadyStampPath( meshFilePath );
		    return !string.IsNullOrEmpty( stamp ) && File.Exists( stamp );
	    }

	    public static void TryDeleteSpzGoExchangeReadyStamp( string meshFilePath ){
		    if( string.IsNullOrEmpty( meshFilePath ) ) return;
		    try {
			    string dir = Path.GetDirectoryName( meshFilePath );
			    string baseName = Path.GetFileNameWithoutExtension( meshFilePath );
			    if( string.IsNullOrEmpty( dir ) || string.IsNullOrEmpty( baseName ) ) return;
			    string stamp = Path.Combine( dir, baseName + ".spz_go_ready" );
			    if( File.Exists( stamp ) ){
				    File.Delete( stamp );
				    UnityEngine.Debug.Log( "[Save_MGR] SPZ GO exchange ready stamp cleared: " + stamp );
			    }
		    } catch( Exception ex ) {
			    UnityEngine.Debug.LogWarning( "[Save_MGR] SPZ GO ready stamp delete failed: " + ex.Message );
		    }
	    }
	    public static void TryWriteSpzGoExchangeReadyStamp( string meshFilePath ){
		    if( string.IsNullOrEmpty( meshFilePath ) ) return;
		    try {
			    string dir = Path.GetDirectoryName( meshFilePath );
			    string baseName = Path.GetFileNameWithoutExtension( meshFilePath );
			    if( string.IsNullOrEmpty( dir ) || string.IsNullOrEmpty( baseName ) ) return;
			    string stamp = Path.Combine( dir, baseName + ".spz_go_ready" );
			    long size = 0;
			    double mtime = 0;
			    if( File.Exists( meshFilePath ) ) {
				    var fi = new FileInfo( meshFilePath );
				    size = fi.Length;
				    mtime = fi.LastWriteTimeUtc.Subtract( new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc ) ).TotalSeconds;
			    }
			    float fitScale = 1f;
			    float userScale = 1f;
			    var container = UnityEngine.Object.FindObjectOfType<Objs3D_Container>();
			    if( container != null && container.currModelRootGO != null ){
				    fitScale = container.EffectiveFitScale();
				    userScale = container.GetUserGlobalScale();
			    }
			    var inv = System.Globalization.CultureInfo.InvariantCulture;
			    File.WriteAllText( stamp,
				    "spz_go_ready=1\n"
				    + "mesh=" + Path.GetFileName( meshFilePath ) + "\n"
				    + "size=" + size + "\n"
				    + "mtime_utc=" + mtime.ToString( "R", inv ) + "\n"
				    + "written_utc=" + DateTime.UtcNow.ToString( "o" ) + "\n"
				    + "scale_undid_fit=1\n"
				    + "fit_scale=" + fitScale.ToString( "R", inv ) + "\n"
				    + "user_scale=" + userScale.ToString( "R", inv ) + "\n"
				    + "spz_fit_target=" + Objs3D_Container.SpzFitTargetMaxDimension.ToString( "R", inv ) + "\n"
				    + "blender_default_cube_edge=" + Objs3D_Container.BlenderDefaultCubeEdgeMeters.ToString( "R", inv ) + "\n" );
			    UnityEngine.Debug.Log( "[Save_MGR] SPZ GO exchange ready stamp: " + stamp );
		    } catch( Exception ex ) {
			    UnityEngine.Debug.LogWarning( "[Save_MGR] SPZ GO ready stamp failed: " + ex.Message );
		    }
	    }


	    public void Save2DArt_ExactPath(Texture2D saveMe, string pathAbs, bool destroyTex){
	        // Do not clear an in-flight 3D/export: this path always sets _isSaving false when done.
	        if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't save icon while a save/export is still writing.", false, 5f, false );
		        return;
	        }
	        _isSaving = true;
	        TextureTools_SPZ.EncodeAndSaveTexture(saveMe, pathAbs);
	        if(destroyTex){  DestroyImmediate(saveMe);  }
	        _isSaving = false;
	    }


	    public void Save2DArt( Dictionary<Texture2D,UDIM_Sector> saveMe, bool destroyTexs){
	        // Same shared _isSaving as SaveViewTextures: cancel of this dialog must not clear an export.
	        if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't save icon while a save/export is still writing.", false, 5f, false );
		        return;
	        }
	        _isSaving = true;
	        string defaultName = "Tex_StableProjectorz";
	        GetBasePathForTextures(defaultName, OnReady);
        
	        void OnReady(string file){
	            try {
	                OnBasePathForTextures_Chosen(file, saveMe, destroyTexs);
	            } finally {
	                _isSaving = false;
	            }
	        }
	    }

	    public void SaveViewTextures(){ //save whatever the camera is observing (view,depth,normals,etc)
	        // Do not clobber an in-flight 3D/export save: cancel of this dialog would clear shared _isSaving.
	        if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't export view textures while a save/export is still writing.", false, 5f, false );
		        return;
	        }
	        LastTextureDialogExportSucceeded = false;
	        _isSaving = true;
	        string defaultName = "Tex_StableProjectorz";
	        GetBasePathForTextures(defaultName, onComplete:(path) => OnSaveViewTextures_PathChosen(path, () => {
		        _isSaving = false;
	        }));
	    }


	    //dilation allows to "spread" the texture outwards from uv-chunks. Helps to avoid seams.
	    public void SaveProjectionTextures(bool isDilate){
	        if( _isSaving || IsProjectSaveDialogOrWriteInFlight() ){
		        Viewport_StatusText.instance?.ShowStatusText(
			        "Can't export projection textures while a save/export is still writing.", false, 5f, false );
		        return;
	        }
	        LastTextureDialogExportSucceeded = false;
	        _isSaving = true;
	        string defaultName = "Tex_StableProjectorz";
	        GetBasePathForTextures( defaultName, onComplete:(path)=>OnSaveProjTextures_PathChosen(path,isDilate, () => {
		        _isSaving = false;
	        }));
	    }

    
	    void GetBasePathForTextures( string defaultName,  Action<string> onComplete ){
	        // CHANGED: Using SimpleFileBrowser Async pattern.
	        // NOTE: SimpleFileBrowser automatically handles the "Overwrite?" popup, so ConfirmPopup_UI logic is removed.
        
	        FileBrowser.SetFilters(true, new FileBrowser.Filter("Images", "png", "jpg", "tga"));
	        FileBrowser.SetDefaultFilter("png");

	        FileBrowser.ShowSaveDialog( (paths) => {
	            if(paths == null || paths.Length == 0){
	                onComplete(null);
	                return;
	            }
	            onComplete(paths[0]);
	        },
	        () => {
	             onComplete(null);
	        },
	        FileBrowser.PickMode.Files, false, null, defaultName, "Save Resulting Model Texture", "Save");
	    }
    

	    void OnSaveViewTextures_PathChosen( string basePath, Action onComplete ){
	        if(string.IsNullOrEmpty(basePath)){
		        LastTextureDialogExportSucceeded = false;
		        onComplete?.Invoke();
		        return;
	        }
        
	        StartCoroutine( WaitForRenderAll_crtn(skipAO_blit:false, onReady) );

	        void onReady(){
	            bool ok = false;
	            try {
		            Save_ViewTextures(basePath);
		            ok = true;
	            } catch (System.Exception e) {
		            UnityEngine.Debug.LogError("[Save_MGR] Save_ViewTextures failed: " + e.Message);
	            } finally {
		            LastTextureDialogExportSucceeded = ok;
		            onComplete?.Invoke();
	            }
	        }
	    }


	    void OnSaveProjTextures_PathChosen( string basePath, bool isDilate, Action onComplete ){
	        if(string.IsNullOrEmpty(basePath)){
		        LastTextureDialogExportSucceeded = false;
		        onComplete?.Invoke();
		        return;
	        }
        
	        StartCoroutine( WaitForRenderAll_crtn(skipAO_blit:true, onReady) );
        
	        void onReady() => Save_Mesh_Textures(null, basePath, isDilate, forbid_albedoDelete:false,
		        onComplete: _ => {
			        // Path was chosen and encode finished; cancel already returned above with success false.
			        LastTextureDialogExportSucceeded = true;
			        onComplete?.Invoke();
		        });
	    }

    
	    void OnBasePathForTextures_Chosen( string filepath,  Dictionary<Texture2D,UDIM_Sector> saveMe, bool destroyTexs ){
	        if (string.IsNullOrEmpty(filepath)){
	            // Dialog cancel / empty path — still dispose caller-owned disposable Texture2Ds.
	            if (destroyTexs && saveMe != null){
	                foreach (var kvp in saveMe){
	                    if (kvp.Key != null) DestroyImmediate(kvp.Key);
	                }
	            }
	            return;
	        }

	        filepath = MakeUniquePath(filepath,suffix:"");
	        if (saveMe != null)
		        EncodeAndSaveTextures(saveMe, filepath);

	        Viewport_StatusText.instance?.ShowStatusText("Saved to "+ filepath.Replace("\\", "\\\\"), 
	                                                    false, 10, progressVisibility: false);
	        if(destroyTexs && saveMe != null){  
	            foreach(var kvp in saveMe){ DestroyImmediate(kvp.Key); }
	        }
	    }


	    /// <summary>
	    /// Where a texture goes next to <paramref name="basePath"/>, with no uniquing. Used when the
	    /// caller has just overwritten a mesh at that path and the maps must land on the matching names.
	    /// </summary>
	    string ComposeTexturePath(string basePath, string suffix){
	        if (string.IsNullOrEmpty(basePath)){ return ""; }
	        string dir = Path.GetDirectoryName(basePath);
	        if (string.IsNullOrEmpty(dir))
		        dir = ".";
	        return Path.Combine(dir, Path.GetFileNameWithoutExtension(basePath) + suffix + Path.GetExtension(basePath));
	    }


	    string MakeUniquePath(string basePath, string suffix){
	        if (string.IsNullOrEmpty(basePath)){ return ""; }

	        string dir = Path.GetDirectoryName(basePath);
	        string filenameWithoutExtension = Path.GetFileNameWithoutExtension(basePath);
	        string extension = Path.GetExtension(basePath);
	        if (string.IsNullOrEmpty(dir))
		        dir = ".";

	        // First candidate: base + optional suffix. If that exists, append " 2", " 3", …
	        string baseFilename = $"{filenameWithoutExtension}{suffix}";
	        string candidate = ComposeTexturePath(basePath, suffix);
	        if (!File.Exists(candidate))
		        return candidate;
	        for (int n = 2; n < 10000; n++) {
		        candidate = Path.Combine(dir, $"{baseFilename} {n}{extension}");
		        if (!File.Exists(candidate))
			        return candidate;
	        }
	        return Path.Combine(dir, $"{baseFilename} {System.Guid.NewGuid():N}{extension}");
	    }


	    //ask all user-cameras (and projections) to re-render. Wait few frames until all is complete.
	    IEnumerator WaitForRenderAll_crtn(bool skipAO_blit, Action onReady){
	        try {
	            UserCameras_Permissions.Force_KeepRenderingCameras(true);

	            if (Objects_Renderer_MGR.instance != null){
	                Objects_Renderer_MGR.instance.ReRenderAll_soon();
	                Objects_Renderer_MGR.instance._skip_AO_blit = skipAO_blit;
	            }
	            for(int i=0; i<3; ++i){ yield return null; }
	            if (Objects_Renderer_MGR.instance != null)
	                Objects_Renderer_MGR.instance._skip_AO_blit = false;
        
	            UserCameras_Permissions.Force_KeepRenderingCameras(false);
	        } finally {
	            // Always complete so callers clear _isSaving even if renderer was null mid-boot.
	            onReady?.Invoke();
	        }
	    }


	    void Save_ViewTextures(string basePath){
	        string pathContent = MakeUniquePath(basePath, "_Content");
	        string pathDepth   = MakeUniquePath(basePath, "_Depth");
	        string pathNormals = MakeUniquePath(basePath, "_Normals");
	        string pathVertex  = MakeUniquePath(basePath, "_VertCols");

	        var camTex = UserCameras_MGR.instance != null ? UserCameras_MGR.instance.camTextures : null;
	        if (camTex == null) {
		        UnityEngine.Debug.LogWarning("[Save_MGR] Save_ViewTextures: UserCameras_MGR/camTextures missing.");
		        return;
	        }

	        Texture2D content = camTex.GetDisposable_ContentCamTexture();
	        Texture2D depth  = camTex.GetDisposable_DepthTexture();
	        Texture2D normals = camTex.GetDisposable_NormalsTexture();
	        Texture2D vertCols = camTex.GetDisposable_VertexColorsTexture();
        
	        encodeSaveDestroy(content, pathContent);
	        encodeSaveDestroy(depth, pathDepth);
	        encodeSaveDestroy(normals, pathNormals);
	        encodeSaveDestroy(vertCols, pathVertex);

	        void encodeSaveDestroy(Texture2D tex, string path){
	            if(tex == null){ return; }
	            TextureTools_SPZ.EncodeAndSaveTexture(tex, path);
	            DestroyImmediate(tex);
	        }
	        Viewport_StatusText.instance?.ShowStatusText("Saved to "+ basePath.Replace("\\", "\\\\"), 
	                                                     false, 10, progressVisibility:false);
	    }


	    // Fire-and-forget entry (unchanged signature): all callers pass callbacks + rely on _isSaving,
	    // so the actual work now runs as a frame-budgeted coroutine that keeps the app responsive.
	    /// <param name="overwriteExisting">
	    /// True when the maps accompany a mesh we just rewrote at a fixed path. The mesh write always
	    /// overwrites, so uniquing the textures desynchronises them: the second export leaves a fresh
	    /// from_spz.fbx beside a stale from_spz.png plus a new "from_spz 2.png", and Blender picks up
	    /// whichever it finds. False for "save textures as…" dialogs, where clobbering the user's own
	    /// files would be the bug.
	    /// </param>
	    /// <param name="onComplete">
	    /// Always runs. The flag is <c>true</c> only when the texture stage ran all the way through; an
	    /// early bail (no accumulation textures) or a throw reports <c>false</c> so callers that publish
	    /// the export to another application do not announce maps that were never written.
	    /// </param>
	    void Save_Mesh_Textures( Action<Dictionary<Texture2D,UDIM_Sector>> onHaveAlbedo=null,
	                            string save_to_basePath="",  bool isDilate=false,
	                            bool forbid_albedoDelete = false,  Action<bool> onComplete=null,
	                            bool overwriteExisting = false){
	        StartCoroutine( Save_Mesh_Textures_crtn(onHaveAlbedo, save_to_basePath, isDilate, forbid_albedoDelete, onComplete, overwriteExisting) );
	    }

	    IEnumerator Save_Mesh_Textures_crtn( Action<Dictionary<Texture2D,UDIM_Sector>> onHaveAlbedo,
	                                         string save_to_basePath, bool isDilate,
	                                         bool forbid_albedoDelete, Action<bool> onComplete,
	                                         bool overwriteExisting ){
	        bool albedoCallbackDelivered = false;
	        bool completedTextureStage = false;
	        bool isFileExport = !string.IsNullOrEmpty(save_to_basePath);
	        bool showedProgress = false;
	        Dictionary<Texture2D,UDIM_Sector> albedo = null;
	        Dictionary<Texture2D,UDIM_Sector> ao = null;

	        // Only try/finally around the yielding body (CS1626: no yield inside try/catch). Risky lookups
	        // are isolated in non-yielding helpers that never throw, so control flow here stays clean.
	        try {
	            RenderUdims albedoUdims = TryGetAlbedoUdims();
	            if (albedoUdims == null || albedoUdims.texArray == null){
	                onHaveAlbedo?.Invoke(null);
	                albedoCallbackDelivered = true;
	                yield break; // finally still runs onComplete
	            }

	            if (isFileExport){
	                Viewport_StatusText.instance?.ShowStatusText("Exporting textures…", false, 9999f, progressVisibility:true);
	                Viewport_StatusText.instance?.ReportProgress(0f);
	                showedProgress = true;
	            }

	            // Stage 1 — dilation, chunked across frames (was isRunInstantly:true = a single frozen frame).
	            if (isDilate && TextureDilation_MGR.instance != null){
	                bool dilateDone = false;
	                int numDilationIters = Mathf.Max(albedoUdims.width, albedoUdims.height) / 16;
	                var dilationArg = new DilationArg(albedoUdims.texArray, numDilationIters, DilateByChannel.A, _ => dilateDone = true);
	                dilationArg.bordersWiderBlur = true;
	                dilationArg.isRunInstantly = false; // spread the hundreds of blits over frames
	                TextureDilation_MGR.instance.Dillate(dilationArg);
	                // Watchdog: Dillate completes on TextureDilation_MGR's OWN coroutine, so a throw in its
	                // preliminaries, or that manager being disabled/destroyed mid-export, leaves the callback
	                // unfired. Waiting unconditionally would strand the export busy forever (spinner on screen,
	                // _isSaving never cleared, no further export until restart). Dilation only widens UV-edge
	                // bleed, so on timeout we log and continue with undilated — but still correct — textures.
	                float dilateDeadline = Time.realtimeSinceStartup + DilationWatchdogSeconds;
	                while (!dilateDone){
	                    if (TextureDilation_MGR.instance == null || Time.realtimeSinceStartup > dilateDeadline){
	                        UnityEngine.Debug.LogWarning(
	                            "[Save_MGR] Texture dilation never reported completion; continuing export undilated.");
	                        break;
	                    }
	                    if (showedProgress) Viewport_StatusText.instance?.ReportProgress(0.15f);
	                    yield return null;
	                }
	            }
	            if (showedProgress) Viewport_StatusText.instance?.ReportProgress(0.30f);

	            // Stage 2 — GPU readback per UDIM slice, budgeted (was one synchronous ReadPixels burst).
	            var slices = new List<Texture2D>();
	            yield return StartCoroutine( TextureTools_SPZ.TextureArray_to_Texture2DList_Budgeted(
	                albedoUdims.texArray, slices, _exportScheduler,
	                p => { if (showedProgress) Viewport_StatusText.instance?.ReportProgress(0.30f + 0.25f * Mathf.Clamp01(p)); } ) );

	            albedo = new Dictionary<Texture2D, UDIM_Sector>();
	            // Slices and sectors are expected 1:1. Upstream indexed sectors by slice and threw on any
	            // mismatch, which here would abort the coroutine after onHaveAlbedo — leaking every slice
	            // and reporting a finished export that wrote no files. Clamp instead, but say so loudly:
	            // a silent clamp would drop UDIM tiles with no trace.
	            List<UDIM_Sector> sectors = albedoUdims.udims_sectors;
	            int sectorCount = sectors != null ? sectors.Count : 0;
	            int pairCount = Mathf.Min(slices.Count, sectorCount);
	            if (sectorCount != slices.Count){
	                UnityEngine.Debug.LogWarning("[Save_MGR] UDIM slice/sector mismatch: " + slices.Count
	                    + " slice(s) vs " + sectorCount + " sector(s); exporting " + pairCount + ".");
	            }
	            for (int i = 0; i < pairCount; ++i){ albedo.Add(slices[i], sectors[i]); }
	            for (int i = pairCount; i < slices.Count; ++i){ if (slices[i] != null) Texture.DestroyImmediate(slices[i]); }
	            bool albedo_destroyWhenDone = !forbid_albedoDelete;

	            ao = TryGetAO(out bool ao_destroyWhenDone);

	            onHaveAlbedo?.Invoke(albedo);
	            albedoCallbackDelivered = true;

	            // Stage 3 — encode + write to disk off the main thread, per texture, budgeted.
	            if (isFileExport){
	                string pathAlbedo = overwriteExisting ? ComposeTexturePath(save_to_basePath, "")
	                                                     : MakeUniquePath(save_to_basePath, "");
	                string pathAO = overwriteExisting ? ComposeTexturePath(save_to_basePath, "_AO")
	                                                  : MakeUniquePath(save_to_basePath, "_AO");
	                yield return StartCoroutine( EncodeAndSaveTextures_crtn(albedo, pathAlbedo, 0.55f, 0.80f) );
	                yield return StartCoroutine( EncodeAndSaveTextures_crtn(ao, pathAO, 0.80f, 1.0f) );
	                Viewport_StatusText.instance?.ShowStatusText("Saved to "+ pathAlbedo.Replace("\\", "\\\\"),
	                                                             false, 10, progressVisibility:false);
	            }

	            if (albedo_destroyWhenDone && albedo != null){ foreach (var kvp in albedo){ if (kvp.Key != null) Texture.DestroyImmediate(kvp.Key); } }
	            if (ao_destroyWhenDone && ao != null){        foreach (var kvp in ao){ if (kvp.Key != null) Texture.DestroyImmediate(kvp.Key); } }
	            completedTextureStage = true;
	        } finally {
	            // MergeIcons and similar callers only pass onHaveAlbedo — without this they hang busy forever.
	            if (!albedoCallbackDelivered) onHaveAlbedo?.Invoke(null);
	            if (showedProgress){
	                Viewport_StatusText.instance?.ReportProgress(1f);
	                Viewport_StatusText.instance?.SetProgressVisible(false);
	            }
	            onComplete?.Invoke(completedTextureStage);
	        }
	    }


	    // Encode + write each UDIM texture on a worker thread (raw pixels snapshotted on the main thread first),
	    // spread across frames with progress p0..p1. Falls back to the synchronous encoder if a platform can't
	    // encode off-thread. Blocks nothing on the main thread except cheap per-texture pixel snapshots.
	    /// <summary>
	    /// Splits a texture destination into "name without image extension" + "image extension".
	    /// Export hands us a base path built by stripping the MESH extension, so whatever Path reads as
	    /// an extension is usually part of the model's own name: "robot_v1.2.fbx" arrives as
	    /// "robot_v1.2", and treating ".2" as the format makes every encoder reject it — the export then
	    /// finishes, stamps itself ready, and writes no textures at all. Only honour a format we can
	    /// actually encode; otherwise keep the whole name and write PNG beside the mesh.
	    /// </summary>
	    static void SplitTexturePath( string path, out string pathBeforeExten, out string exten ){
	        exten = Path.GetExtension(path) ?? "";
	        bool isImageExten = exten.Equals(".png", StringComparison.OrdinalIgnoreCase)
	                         || exten.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
	                         || exten.Equals(".tga", StringComparison.OrdinalIgnoreCase);
	        string baseName = isImageExten ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
	        if (!isImageExten){ exten = ".png"; }
	        string dir = Path.GetDirectoryName(path);
	        pathBeforeExten = string.IsNullOrEmpty(dir) ? baseName : Path.Combine(dir, baseName);
	    }


	    IEnumerator EncodeAndSaveTextures_crtn( Dictionary<Texture2D,UDIM_Sector> textures, string path,
	                                            float p0, float p1, bool skipUdimSuffix_if_1_texture = true ){
	        if (textures == null || textures.Count == 0){
	            Viewport_StatusText.instance?.ReportProgress(p1);
	            yield break;
	        }
	        SplitTexturePath(path, out string pathBeforeExten, out string exten);

	        bool canUseIx    = textures.Count > 1;
	        bool canUseUdims = textures.Count > 1 || !skipUdimSuffix_if_1_texture;
	        int ix = 0;
	        int total = textures.Count;
	        var pending = new List<_EncodeInFlight>();

	        foreach (var kvp in textures){
	            Texture2D tex = kvp.Key;
	            UDIM_Sector val = kvp.Value;
	            string suffix = "";
	            if (val.isNonDefault && canUseUdims){ suffix = "_" + val.ToString(); }
	            else if (canUseIx){ suffix = " " + ix; }
	            string fp = pathBeforeExten + suffix + exten;

	            var job = TextureTools_SPZ.CaptureEncodeJob(tex, fp);
	            if (job != null){
	                pending.Add(new _EncodeInFlight{ task = Task.Run(() => TextureTools_SPZ.RunEncodeJobToDisk(job)), tex = tex, fp = job.filePath });
	            } else {
	                TextureTools_SPZ.EncodeAndSaveTexture(tex, fp); // sync fallback (unsupported format / unreadable tex)
	            }
	            ++ix;
	            Viewport_StatusText.instance?.ReportProgress(Mathf.Lerp(p0, p1, ix / (float)total));
	            yield return null;
	        }

	        // Wait for the off-thread encodes without blocking the main thread.
	        bool anyRunning = true;
	        while (anyRunning){
	            anyRunning = false;
	            for (int i = 0; i < pending.Count; ++i){ if (!pending[i].task.IsCompleted){ anyRunning = true; break; } }
	            if (anyRunning) yield return null;
	        }
	        // Rare: off-thread encode unsupported on this platform → retry synchronously so the file still lands.
	        for (int i = 0; i < pending.Count; ++i){
	            if (pending[i].task.IsFaulted && pending[i].tex != null){
	                UnityEngine.Debug.LogWarning("[Save_MGR] Off-thread encode failed, retrying sync: "
	                    + pending[i].task.Exception?.GetBaseException().Message);
	                TextureTools_SPZ.EncodeAndSaveTexture(pending[i].tex, pending[i].fp);
	            }
	        }
	        Viewport_StatusText.instance?.ReportProgress(p1);
	    }


	    RenderUdims TryGetAlbedoUdims(){
	        if (Objects_Renderer_MGR.instance == null){
	            UnityEngine.Debug.LogWarning("[Save_MGR] TryGetAlbedoUdims: Objects_Renderer_MGR missing.");
	            return null;
	        }
	        RenderUdims albedo = Objects_Renderer_MGR.instance.accumulationTextures_ref();
	        if (albedo == null || albedo.texArray == null){
	            UnityEngine.Debug.LogWarning("[Save_MGR] TryGetAlbedoUdims: accumulation textures missing.");
	            return null;
	        }
	        return albedo;
	    }

	    Dictionary<Texture2D,UDIM_Sector> TryGetAO( out bool destroyWhenDone ){
	        destroyWhenDone = false;
	        try {
	            GenData2D ao_genData = GenData2D_Archive.instance != null
	                ? GenData2D_Archive.instance.Find_GenData_ofKind(GenerationData_Kind.AmbientOcclusion, search_lastToFirst:true)
	                : null;
	            IconUI ao_iconUI = ao_genData == null || Art2D_IconsUI_List.instance == null
	                ? null
	                : Art2D_IconsUI_List.instance.GetIcon_of_GenerationGroup(ao_genData.total_GUID, 0);
	            if (AmbientOcclusion_Baker.instance != null)
	                return AmbientOcclusion_Baker.instance.getDisposable_AO_texture( ao_iconUI, out destroyWhenDone );
	        } catch (System.Exception e) {
	            UnityEngine.Debug.LogWarning("[Save_MGR] TryGetAO failed: " + e.Message);
	        }
	        return null;
	    }


    void EncodeAndSaveTextures( Dictionary<Texture2D,UDIM_Sector> textures,  string path, 
	                                bool skipUdimSuffix_if_1_texture = true ){
	        if (textures == null || textures.Count == 0) return;
	        SplitTexturePath(path, out string pathBeforeExten, out string exten);

	        bool canUseIx    = textures.Count>1;
	        bool canUseUdims = textures.Count>1 || !skipUdimSuffix_if_1_texture;
	        int ix = 0;//will use index if udim sectors are empty, and if more than one texture.

	        foreach(var kvp in textures){
	            Texture2D tex  = kvp.Key;
	            UDIM_Sector val = kvp.Value;
	            string suffix = "";
	            if(val.isNonDefault && canUseUdims){ suffix =  "_" + val.ToString(); }
	            else if (canUseIx){  suffix  =  " " + ix; }
	            TextureTools_SPZ.EncodeAndSaveTexture(tex, pathBeforeExten+suffix+exten);
	            ++ix;
	        }
	    }


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	    }
	    void Start(){
	        ExportSave_UI_MGR.OnSaveProject_Button += DoSaveProject;
	        ExportSave_UI_MGR.OnLoadProject_Button += DoLoadProject;
	        ExportSave_UI_MGR.OnExport3D_Button += () => { Export3D_with_textures(); };
	    }
	    void Update(){
	        if(Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.S)){  DoSaveProject();  }
	        if(Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L)){  DoLoadProject();  }
	    }

	}
}//end namespace
