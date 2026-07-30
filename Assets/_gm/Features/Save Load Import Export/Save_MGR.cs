using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using SimpleFileBrowser;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem;
using System.IO;
using System.Linq;


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


	    public void MergeIcons( Action<Dictionary<Texture2D,UDIM_Sector>> onHaveAlbedo,  bool oldIcons_survive=false ){
	        _isSaving = true;

	        _saveLoad_helper.Save_FinalCompositeTexture( OnReady1 );

	        void OnReady1() => StartCoroutine( WaitForRenderAll_crtn(skipAO_blit:true, OnReady2) );
     
	        void OnReady2(){//save + ensure albedo won't be deleted, - we'll keep using it in new generation:
	            Save_Mesh_Textures(OnHaveAlbedo, "", isDilate: false, forbid_albedoDelete: true);
	        }

	        void OnHaveAlbedo( Dictionary<Texture2D,UDIM_Sector> albedoDict ){
	            var mgr = GenData2D_Archive.instance;
	            var uvTex  = mgr.FindAll_GenData_ofKind( GenerationData_Kind.UvTextures_FromFile );
	            var uvBrush= mgr.FindAll_GenData_ofKind( GenerationData_Kind.UvPaintedBrush );
	            var prTex  = mgr.FindAll_GenData_ofKind( GenerationData_Kind.SD_ProjTextures );
	            var allTex = uvTex.Union(prTex).Union(uvBrush);
	            if(oldIcons_survive == false){ 
	                foreach (GenData2D genDat in allTex){  mgr.DisposeGenerationData(genDat.total_GUID);  }
	            }
	            onHaveAlbedo(albedoDict);
	            _isSaving = false;
	        };
	    }


	    public void DoSaveProject(){
	        // Must not set _isSaving before SaveProject: that helper refuses while _isSaving and
	        // would invoke saveFinalTex(null) without ever clearing a flag we already set (self-deadlock).
	        if( _isSaving ){
		        if( Viewport_StatusText.instance != null ){
			        Viewport_StatusText.instance.ShowStatusText(
				        "Can't save project while an export/save is still writing textures.", false, 5f, false );
		        }
		        return;
	        }

	        Action<string> onResultMessage = msg => {
		        if( Viewport_StatusText.instance != null )
			        Viewport_StatusText.instance.ShowStatusText(msg, false, 6, false);
	        };
	        _saveLoad_helper.SaveProject( onReady1, onResultMessage );
        
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

	    public void DoLoadProject(){
	        _isLoading = true;
        
	        // CHANGED: LoadProject is now Async, so we use a callback instead of 'out string'
	        _saveLoad_helper.LoadProject( (resultMessage_) => {
	            Viewport_StatusText.instance?.ShowStatusText(resultMessage_, false, 6, false);
	            _isLoading = false;
	            //after loading, Unpress any ctrl, alt etc. Else unity might keep thinking they are still pressed:
	            StartCoroutine( ResetCtrlKey_AfterLoadSave() );
	        });
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
	        if( _isSaving ){
		        UnityEngine.Debug.LogWarning("[Save_MGR] Export3D_with_textures: refused — another save/export is in progress.");
		        return false;
	        }
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
			        _saveLoad_helper.Save_FinalCompositeTexture( OnReady1 );
			        void OnReady1() => StartCoroutine( WaitForRenderAll_crtn( skipAO_blit: true, OnReady2 ) );
			        void OnReady2() => Save_Mesh_Textures( onHaveAlbedo:null, path_exported3D, isDilate: true,
				        forbid_albedoDelete:false, onComplete:OnComplete );
			        void OnComplete() => _isSaving = false;
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
		    if( _isSaving ){
			    UnityEngine.Debug.LogWarning("[Save_MGR] Export3D_with_textures_ToPath: refused — another save/export is in progress.");
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
		    _saveLoad_helper.Save_FinalCompositeTexture( OnReady1 );
		    void OnReady1() => StartCoroutine( WaitForRenderAll_crtn( skipAO_blit:true, OnReady2 ) );
		    void OnReady2() => Save_Mesh_Textures( onHaveAlbedo:null, path_exported3D, isDilate: true, forbid_albedoDelete:false, onComplete:OnComplete );
		    void OnComplete() {
			    // Stamp before clearing _isSaving so waiters (native UI / TCP) never race an absent sidecar.
			    TryWriteSpzGoExchangeReadyStamp( meshPathForStamp );
			    _isSaving = false;
		    }
		    return true;
	    }

	    /// <summary>
	    /// Sidecar next to exchange FBX so Blender's SPZ GO watcher can auto-import after Export.
	    /// Name: <c>{basename}.spz_go_ready</c> (e.g. from_spz.spz_go_ready).
	    /// </summary>
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
	        _isSaving = true;
	        TextureTools_SPZ.EncodeAndSaveTexture(saveMe, pathAbs);
	        if(destroyTex){  DestroyImmediate(saveMe);  }
	        _isSaving = false;
	    }


	    public void Save2DArt( Dictionary<Texture2D,UDIM_Sector> saveMe, bool destroyTexs){
	        _isSaving = true;
	        string defaultName = "Tex_StableProjectorz";
	        GetBasePathForTextures(defaultName, OnReady);
        
	        void OnReady(string file){
	            OnBasePathForTextures_Chosen(file, saveMe, destroyTexs);
	            _isSaving=false;
	        }
	    }

	    public void SaveViewTextures(){ //save whatever the camera is observing (view,depth,normals,etc)
	        _isSaving = true;
	        string defaultName = "Tex_StableProjectorz";
	        GetBasePathForTextures(defaultName, onComplete:(path) => OnSaveViewTextures_PathChosen(path,OnReady));
	        void OnReady() =>_isSaving=false;
	    }


	    //dilation allows to "spread" the texture outwards from uv-chunks. Helps to avoid seams.
	    public void SaveProjectionTextures(bool isDilate){
	        _isSaving = true;
	        string defaultName = "Tex_StableProjectorz";
	        GetBasePathForTextures( defaultName, onComplete:(path)=>OnSaveProjTextures_PathChosen(path,isDilate,OnReady) );
        
	        void OnReady()=> _isSaving = false;
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
		        onComplete?.Invoke();
		        return;
	        }
        
	        StartCoroutine( WaitForRenderAll_crtn(skipAO_blit:false, onReady) );

	        void onReady(){ 
	            Save_ViewTextures(basePath);
	            onComplete?.Invoke();
	        }
	    }


	    void OnSaveProjTextures_PathChosen( string basePath, bool isDilate, Action onComplete ){
	        if(string.IsNullOrEmpty(basePath)){
		        onComplete?.Invoke();
		        return;
	        }
        
	        StartCoroutine( WaitForRenderAll_crtn(skipAO_blit:true, onReady) );
        
	        void onReady() => Save_Mesh_Textures(null, basePath, isDilate, forbid_albedoDelete:false, onComplete);
	    }

    
	    void OnBasePathForTextures_Chosen( string filepath,  Dictionary<Texture2D,UDIM_Sector> saveMe, bool destroyTexs ){
	        if (string.IsNullOrEmpty(filepath)){ return; }

	        filepath = MakeUniquePath(filepath,suffix:"");
	        EncodeAndSaveTextures(saveMe, filepath);

	        Viewport_StatusText.instance.ShowStatusText("Saved to "+ filepath.Replace("\\", "\\\\"), 
	                                                    false, 10, progressVisibility: false);
	        if(destroyTexs){  
	            foreach(var kvp in saveMe){ DestroyImmediate(kvp.Key); }
	        }
	    }
    

	    string MakeUniquePath(string basePath, string suffix){
	        if (string.IsNullOrEmpty(basePath)){ return ""; }

	        string dir = Path.GetDirectoryName(basePath);
	        string filenameWithoutExtension = Path.GetFileNameWithoutExtension(basePath);
	        string extension = Path.GetExtension(basePath);

	        //make it unique:
	        string baseFilename = $"{filenameWithoutExtension}{suffix}";
	        return Path.Combine(dir, baseFilename + extension);
	    }


	    //ask all user-cameras (and projections) to re-render. Wait few frames until all is complete.
	    IEnumerator WaitForRenderAll_crtn(bool skipAO_blit, Action onReady){
	        UserCameras_Permissions.Force_KeepRenderingCameras(true);

	        Objects_Renderer_MGR.instance.ReRenderAll_soon();
	        Objects_Renderer_MGR.instance._skip_AO_blit = skipAO_blit;
	        for(int i=0; i<3; ++i){ yield return null; }
	        Objects_Renderer_MGR.instance._skip_AO_blit = false;
        
	        UserCameras_Permissions.Force_KeepRenderingCameras(false);
	        onReady();
	    }


	    void Save_ViewTextures(string basePath){
	        string pathContent = MakeUniquePath(basePath, "_Content");
	        string pathDepth   = MakeUniquePath(basePath, "_Depth");
	        string pathNormals = MakeUniquePath(basePath, "_Normals");
	        string pathVertex  = MakeUniquePath(basePath, "_VertCols");

	        Texture2D content = UserCameras_MGR.instance.camTextures.GetDisposable_ContentCamTexture();
	        Texture2D depth  = UserCameras_MGR.instance.camTextures.GetDisposable_DepthTexture();
	        Texture2D normals = UserCameras_MGR.instance.camTextures.GetDisposable_NormalsTexture();
	        Texture2D vertCols = UserCameras_MGR.instance.camTextures.GetDisposable_VertexColorsTexture();
        
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


	    void Save_Mesh_Textures( Action<Dictionary<Texture2D,UDIM_Sector>> onHaveAlbedo=null,  
	                            string save_to_basePath="",  bool isDilate=false,
	                            bool forbid_albedoDelete = false,  Action onComplete=null){
	        Dictionary<Texture2D,UDIM_Sector> albedo;
	        Dictionary<Texture2D,UDIM_Sector> ao;
	        bool albedo_destroyWhenDone;
	        bool ao_destroyWhenDone;
	        Get_ProjectionsDict(isDilate, out albedo, out ao, out albedo_destroyWhenDone, out ao_destroyWhenDone);
	        albedo_destroyWhenDone =  forbid_albedoDelete?false : albedo_destroyWhenDone;

	        if(albedo==null && ao==null){
	            onHaveAlbedo?.Invoke(null);
	            onComplete?.Invoke();
	            return;
	        }
	        onHaveAlbedo?.Invoke(albedo);

	        string pathAlbedo=null, pathAO=null;

	        if( save_to_basePath!=""){
	            pathAlbedo = MakeUniquePath(save_to_basePath, "");
	            pathAO = MakeUniquePath(save_to_basePath, "_AO");
	            EncodeAndSaveTextures(albedo, pathAlbedo);
	            EncodeAndSaveTextures(ao, pathAO);
	            Viewport_StatusText.instance?.ShowStatusText("Saved to "+ pathAlbedo.Replace("\\", "\\\\"), 
	                                                         false, 10, progressVisibility:false);
	        }
	        //cleanup:
	        if(albedo_destroyWhenDone){ foreach(var kvp in albedo){Texture.DestroyImmediate(kvp.Key);}  }
	        if(ao_destroyWhenDone){     foreach(var kvp in ao){Texture.DestroyImmediate(kvp.Key);}   }
	        onComplete?.Invoke();
	    }



	    void Get_ProjectionsDict( bool isDilate, out Dictionary<Texture2D,UDIM_Sector> albedo_,  
	                                             out Dictionary<Texture2D,UDIM_Sector> ambientOcclusion_,
	                                             out bool albedo_destroyWhenDone_,  out bool ao_destroyWhenDone_){

	        RenderUdims albedo = Objects_Renderer_MGR.instance.accumulationTextures_ref();

	        //Dilate (spread out) the texture around the uv-chunks/islands. This hides seams between them.
	        //check because maybe user doesn't want dilation (maybe they want to see uv islands:
	        if (isDilate){
	            int numDilationIters = Mathf.Max(albedo.width, albedo.height) / 16;  //for exmaple  2048 --> 128 pixels dilated.
	            var dilationArg = new DilationArg(albedo.texArray, numDilationIters, DilateByChannel.A, null);
	            dilationArg.bordersWiderBlur = true;
	            dilationArg.isRunInstantly = true;
	            TextureDilation_MGR.instance.Dillate(dilationArg);
	        }
	        //NOTICE: Convert albedo to texture AFTER dilate. Because dilate works while it's in tex-array form.
	        List<Texture2D> tex2D_list = TextureTools_SPZ.TextureArray_to_Texture2DList(albedo.texArray);
	        albedo_ = new Dictionary<Texture2D, UDIM_Sector>();
	        for(int i=0; i<tex2D_list.Count; ++i){  albedo_.Add(tex2D_list[i], albedo.udims_sectors[i]);  }
	        albedo_destroyWhenDone_ = true;

	        GenData2D ao_genData =  GenData2D_Archive.instance.Find_GenData_ofKind(GenerationData_Kind.AmbientOcclusion, search_lastToFirst:true);
	        IconUI ao_iconUI   = ao_genData==null? null : Art2D_IconsUI_List.instance.GetIcon_of_GenerationGroup(ao_genData.total_GUID, 0);
	        ambientOcclusion_  = AmbientOcclusion_Baker.instance.getDisposable_AO_texture( ao_iconUI, out ao_destroyWhenDone_ );
	    }


	    void EncodeAndSaveTextures( Dictionary<Texture2D,UDIM_Sector> textures,  string path, 
	                                bool skipUdimSuffix_if_1_texture = true ){
	        string pathBeforeExten = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
	        string exten = Path.GetExtension(path);

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
	        ExportSave_UI_MGR.OnExport3D_Button += Export3D_with_textures;
	    }
	    void Update(){
	        if(Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.S)){  DoSaveProject();  }
	        if(Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.L)){  DoLoadProject();  }
	    }

	}
}//end namespace
