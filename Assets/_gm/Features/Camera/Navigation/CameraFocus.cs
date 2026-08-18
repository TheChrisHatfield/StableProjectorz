using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace spz {

	//can fly camera towards the nearest selected 3D mesh.
	public class CameraFocus : MonoBehaviour{
	              public float restorationDur => _restorationDur;
	    [SerializeField] float _restorationDur = 0.4f;
	    [SerializeField] View_UserCamera _view_camera_inParent;

	    Transform _tempPivot;
	    Transform _myParent_durStart;
	    Coroutine _lerpFlyCamera_crtn = null;

	    //whatever our Focus-coroutine wants to do. Helps to respect the overal order of execution.
	    //(otherwise FOV of 1 + focus will cause the depth camera to flicker).
	    Action _corotineCode_run_durUpdate;
                                       
	    public static Action<CameraFocus, Vector3> _Act_onFocused { get; set; } = null; //vec3 is center of mesh bounds.



	/// <summary>True while a focus / restore lerp owns this camera (it is reparented onto the temp pivot).</summary>
	public bool isFlyingCamera => _lerpFlyCamera_crtn != null;

	// interruptCurrentFly: retarget an in-flight lerp instead of dropping the request. Callers that only
	// restore a saved POV keep the old behaviour; repeatable controls (viewport axis gizmo) opt in so a
	// second click is not silently ignored.
	public void Restore_CameraPlacement(CameraPovInfo povInfo, Vector3 selectedModel_pos, bool interruptCurrentFly=false){
        
	        if(_lerpFlyCamera_crtn!=null){
	            if(!interruptCurrentFly){ return; }//avoids initiating it twice, because might already be reparented, etc.
	            StopCurrentLerpAndRestoreParent();
	        }
	        if(Coroutines_MGR.instance == null){ return; }
        
	        Vector3 destinPos    = povInfo.camera_pos;
	        Quaternion destinRot = povInfo.camera_rot;
	        Vector3 orbitAround  = selectedModel_pos;
	        _lerpFlyCamera_crtn = Coroutines_MGR.instance.StartCoroutine( LerpFlyCamera_crtn(destinPos, orbitAround, destinRot, 
	                                                                                        dur:_restorationDur, skip_and_onlyDoEvent:false) );
	    }


	    //invoked every update, but you can manually invoke it (+specify forceTheFocus)
	    public void Focus_Selection_maybe(bool forceTheFocus=false,  bool dontFly_onlyDoEvent=false){
	        if(_lerpFlyCamera_crtn!=null){
	            // Import/recenter requests should win over an in-flight restore/orbit lerp so newly loaded meshes
	            // can still snap/focus to center (e.g. camera-icon restore + quick generate/import sequence).
	            if(forceTheFocus){
	                StopCurrentLerpAndRestoreParent();
	            }else{
	                return; //avoids initiating it twice during regular input
	            }
	        }

	        if(!forceTheFocus  &&  (Keyboard.current == null || Keyboard.current.fKey.wasPressedThisFrame==false)){ return; }
	        if(!forceTheFocus  &&  KeyMousePenInput.isSomeInputFieldActive()){ return; }//maybe typing a prompt
	        if(KeyMousePenInput.isKey_CtrlOrCommand_pressed()){ return; }
	        if(KeyMousePenInput.isKey_Shift_pressed()){ return; }
	        if(DimensionMode_MGR.instance.is_3d_navigation_allowed == false){  return; }

	        IReadOnlyList<SD_3D_Mesh> selected = ModelsHandler_3D.instance.selectedMeshes;
	        if(selected == null || selected.Count==0){ return; }
	        // Prefer the mesh under the main-viewport cursor (sub-viewport in multiview too), so "F" doesn't
	        // only frame the union of all selected AABBs—which feels like the first/older object after adding another mesh.
	        Bounds totalBounds = default;
	        bool haveBounds = false;
	        if (MainViewport_UI.instance != null) {
	            ushort underCursor = ClickSelect_Meshes_MGR.SampleMeshIdAtViewport01_static(
	                MainViewport_UI.instance.cursorMainViewportPos01);
	            if (underCursor != 0) {
	                SD_3D_Mesh m = ModelsHandler_3D.instance.getMesh_byUniqueID(underCursor);
	                if (m != null) {
	                    totalBounds = m.bounds;
	                    haveBounds = true;
	                }
	            }
	        }
	        if (!haveBounds) {
	            totalBounds = ModelsHandler_3D.instance.GetTotalBounds_ofSelectedMeshes();
	        }
	        FlyToFrameBounds(totalBounds, dontFly_onlyDoEvent);
	    }

	    /// <summary>
	    /// Frame an explicit world-space <paramref name="totalBounds"/> (e.g. the whole scene from the viewport
	    /// gizmo lantern, not just the selection). Same gating and animated selfie-stick lerp as
	    /// <see cref="Focus_Selection_maybe"/>, but the caller supplies the bounds so callers can frame all meshes.
	    /// </summary>
	    public void Frame_Bounds_maybe(Bounds totalBounds, bool forceTheFocus=false,  bool dontFly_onlyDoEvent=false){
	        if(_lerpFlyCamera_crtn!=null){
	            if(forceTheFocus){
	                StopCurrentLerpAndRestoreParent();
	            }else{
	                return;
	            }
	        }

	        // forceTheFocus is the lantern / import path — do not require F, and do not drop the request
	        // because the user is holding Ctrl/Shift from a paint or orbit modifier.
	        if(!forceTheFocus){
	            if(Keyboard.current == null || Keyboard.current.fKey.wasPressedThisFrame==false){ return; }
	            if(KeyMousePenInput.isSomeInputFieldActive()){ return; }
	            if(KeyMousePenInput.isKey_CtrlOrCommand_pressed()){ return; }
	            if(KeyMousePenInput.isKey_Shift_pressed()){ return; }
	        }
	        var dim = DimensionMode_MGR.instance;
	        if(dim != null && dim.is_3d_navigation_allowed == false){  return; }

	        FlyToFrameBounds(totalBounds, dontFly_onlyDoEvent);
	    }

	    void FlyToFrameBounds(Bounds totalBounds, bool dontFly_onlyDoEvent){
	        if(Coroutines_MGR.instance == null || _view_camera_inParent == null || _view_camera_inParent.myCamera == null){
	            return;
	        }
	        Vector3 boundsCenter  = totalBounds.center;

	        float distanceToModel = CalcDistanceToModel(totalBounds);

	        Vector3 pivotToCamera_dir = (transform.position - boundsCenter).normalized;
	        // If the camera sits exactly on the bounds center, the direction collapses to zero (NaN rotation) — back off
	        // along -forward so the frame still produces a valid placement.
	        if (pivotToCamera_dir.sqrMagnitude < 1e-8f) { pivotToCamera_dir = -transform.forward; }
	        Vector3 destinationPosition = boundsCenter + pivotToCamera_dir*distanceToModel;

	        Quaternion destinRot = Quaternion.LookRotation( -pivotToCamera_dir ); //Important! NEGATIVE, towards the center

	        _lerpFlyCamera_crtn = Coroutines_MGR.instance.StartCoroutine(  LerpFlyCamera_crtn( destin:destinationPosition,  boundsCenter:boundsCenter,
	                                                                                           destinRot,  dur:0.35f,  dontFly_onlyDoEvent));
	    }


	    float CalcDistanceToModel(Bounds bounds){
	        //Position the camera at a distance away from the bounds center,
	        //to ensure it's not inside them, for the math to work:
	        Vector3 originalPosition = transform.position;
	        Quaternion originalRotation = transform.rotation;

	            float safeDistance = bounds.extents.magnitude + 10f; // Adjust the value as needed
	            transform.position = bounds.center - safeDistance * Vector3.forward;
	            float distanceToModel = bounds.extents.magnitude / Mathf.Tan(_view_camera_inParent.myCamera.fieldOfView*0.5f*Mathf.Deg2Rad);

	        transform.position = originalPosition;
	        transform.rotation = originalRotation;
	        return distanceToModel;
	    }



	    // destin:  pos where camera should fly towards
	    //
	    // boundsCenter:  the final look-at coord.
	    //
	    // camDestinRotation: where camera will look. Needed, because camera might look
	    // to the side of the model sometimes, not necessarily towards orbitAroundPoint.
	    IEnumerator LerpFlyCamera_crtn( Vector3 destin,  Vector3 boundsCenter,  Quaternion camDestinRotation,  
	                                    float dur,  bool skip_and_onlyDoEvent ){
	        _Act_onFocused?.Invoke(this, boundsCenter);
	        if(skip_and_onlyDoEvent){ yield break; }
        
	        Quaternion pivotFromRot, pivotToRot;//rotation of our parent (like a selfie-stick)
	        Quaternion myFromRot = transform.rotation;//our own rotation (we will be at the end of the stick)
	        Quaternion myToRot = camDestinRotation;
	        {
	            _tempPivot.position = boundsCenter;
	            _tempPivot.LookAt(transform, Vector3.up);
	            pivotFromRot = _tempPivot.rotation.normalized;

	            _tempPivot.transform.LookAt(destin, Vector3.up);
	            pivotToRot = _tempPivot.rotation.normalized;

	            _tempPivot.rotation = pivotFromRot;
	        }

	        _tempPivot.transform.SetParent(transform.parent);//important if Camera_MGR reparented us elsewhere.
	        transform.SetParent(_tempPivot, worldPositionStays:true);

	                float fromDist =  ( transform.position - _tempPivot.position ).magnitude;
	                float targDist =  ( destin - _tempPivot.position ).magnitude;

	        float startTime = Time.unscaledTime;
	        while(true){
	            float elapsed01 = (Time.unscaledTime - startTime) / dur;
            
	            _corotineCode_run_durUpdate =  ()=>{
	                    elapsed01 = Mathf.Clamp01(elapsed01);
	                    float lerpFactor01 =  Mathf.SmoothStep(0, 1, elapsed01);
            
	                    //spin the parent pivot (kinda like a selfie stick)
	                    _tempPivot.rotation = Quaternion.Slerp(pivotFromRot,pivotToRot,lerpFactor01).normalized;

	                    //Now adjust the local position of the camera, by sliding it towards/away from the parent:
	                    float localZ = Mathf.Lerp(fromDist, targDist, lerpFactor01);
	                    transform.localPosition = new Vector3(0, 0, localZ);

	                    {//Now adjust the local rotation of this camera (not of the parent). Try to smothely look towards parent:
	                        transform.rotation = Quaternion.Slerp(myFromRot, myToRot, lerpFactor01).normalized;
	                    }
	            };

	            yield return null;
	            if(elapsed01 == 1){ break; }
	        }//end while

	        _corotineCode_run_durUpdate?.Invoke();//make sure that final iter runs fine.
	        _corotineCode_run_durUpdate = null;

	        transform.SetParent(_myParent_durStart, worldPositionStays:true);
	        _lerpFlyCamera_crtn = null;
	    }

	    void StopCurrentLerpAndRestoreParent(){
	        if(_lerpFlyCamera_crtn != null){
	            if(Coroutines_MGR.instance != null){
	                Coroutines_MGR.instance.StopCoroutine(_lerpFlyCamera_crtn);
	            }
	            _lerpFlyCamera_crtn = null;
	        }
	        _corotineCode_run_durUpdate = null;
	        if(transform != null && _myParent_durStart != null){
	            transform.SetParent(_myParent_durStart, worldPositionStays:true);
	        }
	    }



	    void OnUser_loaded3Dmodel(GameObject go){
	        // If the game has just started, keep the default camera placement. 
	        // But still dispatch the event, to give 'CameraOrbit_ClickPivot' etc a chance to init to a default location.
	        Focus_Selection_maybe(forceTheFocus:true, dontFly_onlyDoEvent:Time.time<0.5f);
	    }

     
	    void OnStartEditMode_MultiView(MultiView_StartEditMode_Args args){
	        Transform forcedParent = transform.parent;
	        if(_lerpFlyCamera_crtn != null){  transform.SetParent(_tempPivot, worldPositionStays:true);  } 
	        _tempPivot.SetParent(forcedParent, worldPositionStays: true);
	    }

	    void OnStop2EditMode_MultiView(){
	        Transform forcedParent = transform.parent;
	        if(_lerpFlyCamera_crtn !=null){  transform.SetParent(_tempPivot, worldPositionStays: true);  }
	        _tempPivot.SetParent(forcedParent, worldPositionStays:true);
	    }

    
	    void OnUpdate(){
	        _corotineCode_run_durUpdate?.Invoke();//do whatever our Focus-coroutine wants to do. Needed for order of execution.
	        View_UserCamera nearestCam = UserCameras_MGR.instance.NearestToCursor();
	        if(nearestCam != _view_camera_inParent){ return; }
	        if((MainViewport_UI.instance?.isCursorHoveringMe() ?? false) == false){ return; }
	        Focus_Selection_maybe();
	    }



	    void Awake(){
	        ModelsHandler_3D.Act_onImported += OnUser_loaded3Dmodel;
	        _myParent_durStart = transform.parent;
	        _tempPivot = new GameObject("camera's temporaryPivot").transform;
	        MultiView_Ribbon_UI.OnStartEditMode += OnStartEditMode_MultiView;
	        MultiView_Ribbon_UI.OnStop2_EditMode += OnStop2EditMode_MultiView;
	    }

	    void Start(){
	        Update_callbacks_MGR.navigation += OnUpdate;
	    }


	    void OnDestroy(){
	        ModelsHandler_3D.Act_onImported -= OnUser_loaded3Dmodel;
	        MultiView_Ribbon_UI.OnStartEditMode -= OnStartEditMode_MultiView;
	        MultiView_Ribbon_UI.OnStop2_EditMode -= OnStop2EditMode_MultiView;
	        if (_tempPivot != null){ Destroy(_tempPivot.gameObject);  }

	        // Must be -=, never =. This delegate is shared with CameraOrbit/Panning/Dolly/Move, so an
	        // assignment here drops all of them and leaves our destroyed OnUpdate as the only handler.
	        Update_callbacks_MGR.navigation -= OnUpdate;
	    }

	}
}//end namespace
