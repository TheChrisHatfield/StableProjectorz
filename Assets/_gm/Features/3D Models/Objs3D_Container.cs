using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace spz {

	// Helper class of the 'ModelsHandler_3D'.
	// Contains all SD_3D_Mesh objects currently in the scene, and allows to iterate them.
	public class Objs3D_Container : MonoBehaviour{
	    //There can be only 1 model, but it can consist of several meshes:
	    public float currModelRoot_scaleAfterImport { get; set; }//<--for example, 0.001
	    public GameObject currModelRootGO { get; set; } = null;

	    public List<SD_3D_Mesh> meshes { get; set; } = new List<SD_3D_Mesh>();
	    public List<Renderer> renderers { get; set; } = new List<Renderer>();

	    public List<SD_3D_Mesh> selectedMeshes { get; set; } = new List<SD_3D_Mesh>();
	    public List<Renderer> selectedRenderers { get; set; } = new List<Renderer>();
	    public List<SD_3D_Mesh> nonSelectedMeshes { get; set; } = new List<SD_3D_Mesh>();

	    // valid only for the duration of our DoForIsolatedMeshes(). Empty when outside of it.
	    public IReadOnlyList<SD_3D_Mesh> isolatedMeshes { get; set; } = new List<SD_3D_Mesh>();
	    public IReadOnlyList<Renderer> isolatedRenderers { get; set; } = new List<Renderer>();


	    public Dictionary<ushort, SD_3D_Mesh> meshID_to_mesh = new Dictionary<ushort, SD_3D_Mesh>();

	    public bool scaleWasTooLarge_duringImport{get; private set;} = false;

	    public string currModelRootGO_name() => currModelRootGO?.name ?? "";

	    // Each mesh has an 16-bit integer that it generates during its Awake().
	    // We can find all neeeded meshes, given their ids.
	    public List<SD_3D_Mesh> getMeshes_by_uniqueIDs( List<ushort> unique_ids ){
	        var found = new List<SD_3D_Mesh>();
	        for(int i=0; i<unique_ids.Count; ++i){
	            ushort id = unique_ids[i];
	            SD_3D_Mesh mesh = null;
	            meshID_to_mesh.TryGetValue(id, out mesh);
	            if(mesh == null){ continue; }
	            found.Add(mesh);
	        }
	        return found;
	    }


	    // While this function is working, the 'isolatedMeshes' and 'isolatedRenderers' list become active.
	    // And are allowed to be can be accessed by anyone
	    public void DoForIsolatedMeshes( IReadOnlyList<SD_3D_Mesh> isolateAndEnable,  Action doSomething ){
	        //only show requred meshes, hide the rest:
	        var wasEnabled = new List<bool>();
	        for(int i=0; i<meshes.Count; ++i){  
	            wasEnabled.Add(meshes[i]._isVisible);
	            meshes[i].ToggleRender(false); 
	        }
	        foreach(var m in isolateAndEnable){ 
	            m.ToggleRender(true); }

	        isolatedMeshes    = isolateAndEnable;
	        isolatedRenderers = isolateAndEnable.Select(m=>m._meshRenderer).ToList();

	        // A throw from the user instruction used to skip the restore below, leaving the model
	        // isolated — every mesh outside the isolated set stays hidden with no way back short of a
	        // reimport, and isolatedMeshes stays pointing at the caller's list.
	        try {
	            doSomething();//do user instruction
	        } finally {
	            // NOTICE: new list, NOT clear. (might have been pointing to someone's list)
	            isolatedMeshes    = new List<SD_3D_Mesh>();
	            isolatedRenderers = new List<Renderer>();

	            RestoreVisibility(wasEnabled);
	        }
	    }

	    public void DoForAllMeshes_EvenIfHidden( Action doSomething ){
	        var wasEnabled = new List<bool>();
	        for(int i=0; i< meshes.Count; ++i){  
	            wasEnabled.Add(meshes[i]._isVisible);
	            meshes[i].ToggleRender(true); 
	        }
	        // Without the finally, a throw leaves every hidden mesh forced visible.
	        try {
	            doSomething();//do user instruction
	        } finally {
	            RestoreVisibility(wasEnabled);
	        }
	    }

	    /// <summary>
	    /// Put visibility back the way <paramref name="wasEnabled"/> recorded it. The restore loops used
	    /// to Add to that same list while reading it, which doubled its length every call; the reads
	    /// happened to still land on the saved values, so it went unnoticed. Clamped to both lengths in
	    /// case the user instruction added or removed meshes.
	    /// </summary>
	    void RestoreVisibility( List<bool> wasEnabled ){
	        int n = Mathf.Min(meshes.Count, wasEnabled.Count);
	        for(int i=0; i<n; ++i){
	            meshes[i].ToggleRender(wasEnabled[i]);
	        }
	    }


	    /// <summary>
	    /// Litmus: SPZ fits imported meshes so max AABB edge ≈ this many Unity units
	    /// (see <see cref="RescaleModel_fitIntoVolume"/>). Blender's default cube is 2m edge;
	    /// GO export undoes the fit so that cube returns to ~2m in Blender.
	    /// </summary>
	    public const float SpzFitTargetMaxDimension = 3.0f;
	    public const float BlenderDefaultCubeEdgeMeters = 2.0f;

	    /// <summary>
	    /// Facing yaw restored for <b>legacy</b> projects only. Projects saved before we persisted model
	    /// orientation (<see cref="ModelsHandler_3D_SL.currModelRoot_rotationEuler"/> == null) were
	    /// authored while import applied this yaw, and the <c>.spz</c> stores no model rotation of its
	    /// own — so those files load 180° off unless we re-apply it. Fresh imports are <b>not</b> yawed
	    /// (see <see cref="RescaleModel_fitIntoVolume"/>); their orientation is saved explicitly instead.
	    /// </summary>
	    public const float SpzLegacyImportYawDegrees = 180f;

	    /// <summary>
	    /// Apply the saved model-root orientation after import/fit. <paramref name="localEulerOrNull"/>
	    /// null means a legacy project (no stored rotation) → restore <see cref="SpzLegacyImportYawDegrees"/>;
	    /// otherwise apply the stored Euler exactly. Call after <see cref="RescaleModel_fitIntoVolume"/>,
	    /// which resets rotation to identity.
	    /// </summary>
	    public void ApplyLoadedRootRotation( float[] localEulerOrNull ){
		    if( currModelRootGO == null ){ return; }
		    if( localEulerOrNull != null && localEulerOrNull.Length == 3 ){
			    currModelRootGO.transform.localRotation =
				    Quaternion.Euler( localEulerOrNull[0], localEulerOrNull[1], localEulerOrNull[2] );
		    }else{
			    currModelRootGO.transform.localRotation = Quaternion.Euler( 0f, SpzLegacyImportYawDegrees, 0f );
		    }
	    }

	    // please don't change to much, to avoid depth-precision issues with projections or painting.
	    // Remember that we were tyring to fit the model into small volume when  ModelsHandler3D_ImportHelper.AcceptModel()
	    // via doing RescaleModel_fitIntoVolume().
	    public void ChangeScaleEntireModel(float new_globalScale){
	        if(currModelRootGO == null){ return; }
	        currModelRootGO.transform.localScale =  Vector3.one*new_globalScale*EffectiveFitScale();
	    }

	    /// <summary>Import fit factor (never 0). User global scale is localScale / this.</summary>
	    public float EffectiveFitScale(){
		    float f = currModelRoot_scaleAfterImport;
		    return f > 1e-8f ? f : 1f;
	    }

	    /// <summary>
	    /// User scale slider factor (1 = as fitted). Survives GO export because we write authoring size = mesh × this.
	    /// </summary>
	    public float GetUserGlobalScale(){
		    if( currModelRootGO == null ) return 1f;
		    float fit = EffectiveFitScale();
		    // Uniform fit is applied on all axes; read X.
		    return currModelRootGO.transform.localScale.x / fit;
	    }

	    /// <summary>
	    /// Temporarily remove the import fit-to-volume scale so FBX writes Blender/authoring meters
	    /// (default-cube litmus). Keeps user global scale. Call restore after export.
	    /// </summary>
	    public bool TryBeginFbxExportAuthoringScale( out Vector3 restoreLocalScale ){
		    restoreLocalScale = Vector3.one;
		    if( currModelRootGO == null ) return false;
		    Transform t = currModelRootGO.transform;
		    restoreLocalScale = t.localScale;
		    float fit = EffectiveFitScale();
		    if( Mathf.Abs( fit - 1f ) < 1e-6f ) return false;
		    t.localScale = restoreLocalScale / fit;
		    return true;
	    }

	    public void EndFbxExportAuthoringScale( Vector3 restoreLocalScale ){
		    if( currModelRootGO == null ) return;
		    currModelRootGO.transform.localScale = restoreLocalScale;
	    }

	    //this will prevent issues with depth-testing (when applying projections, painting, etc etc).
	    /// <remarks>
	    /// Rotation stays identity: <see cref="AssimpLoader"/> imports with <c>MakeLeftHanded</c>, which
	    /// mirrors z and already lands a right-handed Y-up source (Blender/OBJ/glTF) in Unity orientation.
	    /// A blanket 180° yaw here made every imported model face away from the viewport, so the FBX
	    /// writer mirrors the same z axis instead of compensating with yaw.
	    /// </remarks>
	    void RescaleModel_fitIntoVolume(){
	        // Always clear prior fit so a failed/empty Init cannot leave a stale factor for GO export undo.
	        currModelRoot_scaleAfterImport = 1f;
	        currModelRootGO.transform.rotation = Quaternion.identity;
	        currModelRootGO.transform.localScale = Vector3.one;//important, before calculating the bounds. Else their sizes would be affected.
	        currModelRootGO.transform.position = Vector3.zero;

	        Renderer[] renderer =  currModelRootGO.GetComponentsInChildren<Renderer>(); //MeshRenderer or SkinnedMeshRenderer
	        if(renderer.Length == 0){ return; }

	        Bounds totalBounds = renderer[0].bounds;
	        for(int i=1; i<renderer.Length; ++i){
	            totalBounds.Encapsulate(renderer[i].bounds);
	        }
	        //excessively large meshes might not scale correctly. Might warn user later.
	        //This might be helpful if user included some "distant light", etc into the FBX, which will mess up the auto-depth.
	        float maxDimension = Mathf.Max(totalBounds.size.x, totalBounds.size.y, totalBounds.size.z);
	        scaleWasTooLarge_duringImport = maxDimension>1001;

	        // Degenerate / empty bounds: do not divide by zero (Inf/NaN breaks export undo + painting depth).
	        if( maxDimension < 1e-8f ){
		        currModelRoot_scaleAfterImport = 1f;
		        currModelRootGO.transform.localScale = Vector3.one;
		        currModelRootGO.transform.position = Vector3.zero;
		        UnityEngine.Debug.LogWarning("[Objs3D_Container] fit-to-volume skipped: mesh bounds were empty/degenerate.");
		        return;
	        }

	        float scaleFactor = SpzFitTargetMaxDimension/maxDimension;
	        currModelRoot_scaleAfterImport = scaleFactor;

	        currModelRootGO.transform.localScale =  Vector3.one*scaleFactor;
	        currModelRootGO.transform.position -= totalBounds.center*scaleFactor;
	    }


	    //box that encapsulates all mesh renderers.
	    public Bounds GetTotalBounds_ofSelectedMeshes(){
	        if (selectedMeshes.Count == 0){ return new Bounds(); }

	        Bounds bounds = selectedMeshes[0].bounds;
	        for (int i=1; i<selectedMeshes.Count; ++i){
	            bounds.Encapsulate(selectedMeshes[i].bounds);
	        }
	        return bounds;
	    }


	    public bool Init(GameObject newRootGO){
	        currModelRootGO = newRootGO;

	        if(newRootGO== null){
	            Viewport_StatusText.instance?.ShowStatusText("Problem loading a 3d-model. Looks like it's empty.", false, 2.5f, false);
	            return false; 
	        }
	        currModelRootGO.transform.SetParent(transform);
	        RescaleModel_fitIntoVolume();
	        Init_MeshesFromCurrGO();
	        return true;
	    }
    

	    void Init_MeshesFromCurrGO(){
	        Debug.Assert(meshes.Count==0, "meshes should have been despawned + cleared before my Init");

	        Renderer[] renderComponents = currModelRootGO.GetComponentsInChildren<Renderer>();//both MeshRenderer and SkinnedMeshRenderer
        
	        for(int i=0; i<renderComponents.Length; ++i){
	            var sdMesh = renderComponents[i].gameObject.AddComponent<SD_3D_Mesh>();
	            this.meshes.Add(sdMesh);
	            this.meshID_to_mesh.Add(sdMesh.unique_id, sdMesh);
	            this.renderers.Add( renderComponents[i] );
	        }
	    }
	}
}//end namespace
