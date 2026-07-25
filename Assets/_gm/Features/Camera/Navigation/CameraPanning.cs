using UnityEngine;

namespace spz {

	public class CameraPanning : MonoBehaviour{

	    [SerializeField] float _cameraPan_Speed = 4;
	    [SerializeField] View_UserCamera _myViewCam;
	    [SerializeField] AnimationCurve _panSpeed01_viaAspect;

	    //there can be several camerase (with our script).
	    public static CameraPanning _theCurrentlyPanning { get; private set; } = null;

	    public static float _haveBeenPanningFor = 0; //0 if not panning, else keeps increasing every frame.

	    // Lock distance for one drag so pan scale stays stable (same idea as standard/single-view).
	    float _lockedPanDistance = 0f;
	    bool _haveLockedPanDistance = false;

	    // World point under cursor at MMB-down (owning column). Used for pan *distance* and a
	    // one-shot pin snap on release — NOT for live perspective-center updates during the drag
	    // (that double-moved the frustum vs standard translate-only pan).
	    Vector3 _panAnchorWorld;
	    bool _havePanAnchorWorld;

	    void OnApplicationFocus(bool focus){
	        // Same class of bug as CameraOrbit/CameraMove/CameraDolly: after a file dialog or
	        // alt-tab mid-MMB-pan, _theCurrentlyPanning + sticky nav lock stayed held and blocked
	        // further multi-view pan ownership until a matching EndPanDrag ran.
	        EndPanDrag();
	    }

	    void OnUpdate(){
	        StartMoveRotate_ifCan();
	        MoveRotate();
	    }

	     void StartMoveRotate_ifCan(){
	        bool pressedThisFrame  = KeyMousePenInput.isMMBpressedThisFrame();
	        bool hovering =  MainViewport_UI.instance.isCursorHoveringMe();

	        // ROLLBACK NOTE (if multi-view MMB+pin behavior must revert):
	        // Previously pan only ran in MultiView "editing" mode and only for _curr_viewCamera:
	        //   bool isEditing = MultiView_Ribbon_UI.instance._isEditingMode; if (!isEditing) return;
	        //   if (UserCameras_MGR.instance._curr_viewCamera != _myViewCam) return;
	        // That blocked MMB free-pan whenever multiple view cameras were visible; CamerasMGR_PinsZone_UI
	        // then always interpreted MMB as the nearest camera pin. Match orbit/dolly: use NearestToCursor,
	        // and yield MMB to pin-drag only when the cursor is within the pin's grab radius (see PinsZone).

	        if (UserCameras_MGR.instance == null) { return; }
	        if (UserCameras_MGR.instance.NearestToCursor() != _myViewCam) { return; }

	        var pins = CamerasMGR_PinsZone_UI.instance;
	        if (pins != null) {
	            if (pins.IsDraggingViewPin) { return; }
	            if (pressedThisFrame && pins.MmbDownWouldGrabNearestPin()) { return; }
	        }

	        if(_theCurrentlyPanning != null){ return; }

	        if(DimensionMode_MGR.instance.is_3d_navigation_allowed == false){ return; }

		        if(pressedThisFrame && hovering){ 
	            _theCurrentlyPanning = this;
	            _haveBeenPanningFor = 0;
	            UserCameras_MGR.instance.LockNavigationCamera(UserCameras_MGR.instance.ix_specificViewCam(_myViewCam), this);
	            _havePanAnchorWorld = ModelsHandler_3D.instance != null
	                && ModelsHandler_3D.instance.TryGetNavigationReferenceWorldPoint(_myViewCam, out _panAnchorWorld);
	            _lockedPanDistance = ResolvePanDistanceScale();
	            _haveLockedPanDistance = true;
	        }
	    }


	    void MoveRotate(){
	        if(_theCurrentlyPanning!=this){ return; }
	        // COMMENTED OUT, KEPT FOR PRECAUTION. 
	        // Users have specifically mentioned Alt+MMB for panning.
	        // if (KeyMousePenInput.isKey_alt_pressed()){
	        //   _theCurrentlyPanning=null; return; }//doing something else.

	        if(KeyMousePenInput.isKey_CtrlOrCommand_pressed()){ 
	            EndPanDrag(); return; }//doing something else.

	        if(KeyMousePenInput.isMMBpressed()==false){  
	            EndPanDrag(); return; }
	        Pan();
	        _haveBeenPanningFor += Time.deltaTime;
	    }

	    void EndPanDrag(){
	        if (_theCurrentlyPanning != this) { return; }
	        // After a translate-only drag, snap the POV digit once so it stays on the asset
	        // without having shifted the frustum during the gesture (standard-mode feel).
	        SnapPovPinToPanAnchor_WhenMultiview();
	        if (UserCameras_MGR.instance != null) {
		        UserCameras_MGR.instance.ClearNavigationCameraLock(this);
	        }
	        _theCurrentlyPanning = null;
	        _haveBeenPanningFor = 0;
	        _haveLockedPanDistance = false;
	        _havePanAnchorWorld = false;
	    }


	    void Pan(){
	        float distToMeshes = _haveLockedPanDistance ? _lockedPanDistance : ResolvePanDistanceScale();

	        // Reading values from Keyboard or another device if needed. -1* to invert it (for dragging)
	        Vector2 delta = -1 * KeyMousePenInput.delta_while_MMBpressed();

	        float fov = _myViewCam.contentCam.myCamera.fieldOfView;
	        float aspectRatio = _myViewCam.contentCam.cameraAspect;

	        // Calculate the FOV scaling factor
	        float fovRatio = fov / 90f; // Ratio of current FOV to 90 degrees
	        float fovScale = Mathf.Pow(2f, fovRatio) - 1f; // Exponential scaling factor

	        // Combine the FOV and aspect ratio scaling factors
	        float combinedScale = fovScale * _panSpeed01_viaAspect.Evaluate(aspectRatio);

	        Vector3 moveInput = new Vector3(delta.x, delta.y, 0);
	        moveInput *= _cameraPan_Speed * distToMeshes * combinedScale;

	        // Standard / OG pan: camera translate ONLY. Do not Set_ProjMatrixCenter here —
	        // live perspective-center updates compounded with Translate and made multi-view
	        // MMB feel offset vs single-asset standard mode.
	        transform.Translate(moveInput, Space.Self);
	    }

	    /// <summary>
	    /// One-shot after MMB release: park the POV digit on the pan-start world point.
	    /// ROLLBACK NOTE: prior code called this every pan frame (LockPovNumber…); that was the
	    /// multi-view vs standard offset. Re-enable only the per-frame path if digit drift mid-pan
	    /// is preferred over cursor-matched pan feel.
	    /// </summary>
	    void SnapPovPinToPanAnchor_WhenMultiview() {
		    if (UserCameras_MGR.instance == null || MainViewport_UI.instance == null) { return; }
		    if (UserCameras_MGR.instance.numActiveViewCameras() <= 1) { return; }
		    if (!_havePanAnchorWorld) { return; }
		    int camIx = UserCameras_MGR.instance.ix_specificViewCam(_myViewCam);
		    if (camIx < 0) { return; }
		    var pins = CamerasMGR_PinsZone_UI.instance;
		    if (pins != null && pins.IsDraggingThisCameraPin(camIx)) { return; }
		    if (_myViewCam.myCamera == null) { return; }
		    Vector3 vp = _myViewCam.WorldToViewportPoint_RenderMatched(_panAnchorWorld);
		    if (vp.z < 0f) { return; }
		    Vector2 p01 = _myViewCam.CameraFrame01_to_PerspectiveCenter01(new Vector2(vp.x, vp.y));
		    p01.x = Mathf.Clamp01(p01.x);
		    p01.y = Mathf.Clamp01(p01.y);
		    UserCameras_MGR.instance.Set_ProjMatrixCenter_ofCamera(camIx, p01);
		    pins?.RepositionPinUIFromPovData(camIx);
	    }

	    float ResolvePanDistanceScale(){
	        // Match standard/OG scale when possible: distance from camera to the point under the
	        // cursor (this object), else selection bounds center (OG CameraPanning).
	        if (_havePanAnchorWorld) {
		        return Mathf.Max((transform.position - _panAnchorWorld).magnitude, 0.01f);
	        }
	        if (ModelsHandler_3D.instance == null) { return 0.01f; }
	        Vector3 centerOfSelection = ModelsHandler_3D.instance.GetTotalBounds_ofSelectedMeshes().center;
	        return Mathf.Max((transform.position - centerOfSelection).magnitude, 0.01f);
	    }

	    void Start(){
	        Update_callbacks_MGR.navigation += OnUpdate;
	    }

	    void OnDestroy(){
	        EndPanDrag();
	        Update_callbacks_MGR.navigation -= OnUpdate;
	    }

	    void OnDisable(){
	        // Multi-view can deactivate this camera mid-MMB-pan; clear panner + sticky nav lock.
	        EndPanDrag();
	    }
	}
}//end namespace
