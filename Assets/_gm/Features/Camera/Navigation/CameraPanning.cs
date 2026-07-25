using UnityEngine;

namespace spz {

	public class CameraPanning : MonoBehaviour{

	    [SerializeField] float _cameraPan_Speed = 4;
	    [SerializeField] View_UserCamera _myViewCam;
	    [SerializeField] AnimationCurve _panSpeed01_viaAspect;

	    //there can be several camerase (with our script).
	    public static CameraPanning _theCurrentlyPanning { get; private set; } = null;

	    public static float _haveBeenPanningFor = 0; //0 if not panning, else keeps increasing every frame.

	    // Lock distance for one drag so pan scale stays stable across selected objects.
	    float _lockedPanDistance = 0f;
	    bool _haveLockedPanDistance = false;

	    // Captured at MMB-down: the world point under the cursor at the moment pan started.
	    // Re-projected each frame to drive the POV-pin lock in multi-view, so the pin tracks
	    // the SAME point on the mesh through the whole drag (no per-frame target re-pick / jitter).
	    Vector3 _panAnchorWorld;
	    bool _havePanAnchorWorld;

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
	            _lockedPanDistance = ResolvePanDistanceScale();
	            _haveLockedPanDistance = true;
	            UserCameras_MGR.instance.LockNavigationCamera(UserCameras_MGR.instance.ix_specificViewCam(_myViewCam), this);
	            _havePanAnchorWorld = ModelsHandler_3D.instance != null
	                && ModelsHandler_3D.instance.TryGetNavigationReferenceWorldPoint(_myViewCam, out _panAnchorWorld);
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
	        if (_theCurrentlyPanning == this && UserCameras_MGR.instance != null) {
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

	        transform.Translate(moveInput, Space.Self);
	        LockPovNumberToNavReference_WhenMultiview();
	    }

	    /// <summary>
	    /// In multi-view, MMB-panning slides the camera in world space but the POV number pin is tied to
	    /// <c>perspectiveCenter01</c> (the projection / pin anchor in the inner viewport). Without an update,
	    /// the number drifts off the character it belongs to.
	    ///
	    /// Implementation: re-project the SAME world point captured at MMB-down (<see cref="_panAnchorWorld"/>)
	    /// into <see cref="MainViewport_UI.innerViewportRect"/> 01 each frame. Using a fixed anchor (not a
	    /// per-frame nav-reference re-pick under the cursor) is what removes the small jump/jitter at the
	    /// start of the pan: the cursor stays at one screen position while the world under it slides, so
	    /// re-picking each frame would chase a moving target.
	    /// ROLLBACK NOTE: remove this call (and _panAnchorWorld captures) to restore the static pin while panning.
	    /// </summary>
	    void LockPovNumberToNavReference_WhenMultiview() {
		    if (UserCameras_MGR.instance == null || MainViewport_UI.instance == null) { return; }
		    if (UserCameras_MGR.instance.numActiveViewCameras() <= 1) { return; }
		    if (!_havePanAnchorWorld) { return; }
		    int camIx = UserCameras_MGR.instance.ix_specificViewCam(_myViewCam);
		    if (camIx < 0) { return; }
		    var pins = CamerasMGR_PinsZone_UI.instance;
		    if (pins != null && pins.IsDraggingThisCameraPin(camIx)) { return; }
		    if (_myViewCam.myCamera == null) { return; }
		    // OG pattern: pin centers live in viewport-01 spaces, never window pixels. Camera frame 01
		    // (render-matched) -> perspective-center 01 via the exact inverse of ShiftPerspectiveCenter.
		    // WorldToScreenPoint was wrong here: its pixel rect is the whole game window, so with side
		    // panels open the digits parked offset from their sub-view (and hover drove the wrong camera).
		    Vector3 vp = _myViewCam.WorldToViewportPoint_RenderMatched(_panAnchorWorld);
		    if (vp.z < 0f) { return; }
		    Vector2 p01 = _myViewCam.CameraFrame01_to_PerspectiveCenter01(new Vector2(vp.x, vp.y));
		    p01.x = Mathf.Clamp01(p01.x);
		    p01.y = Mathf.Clamp01(p01.y);
		    UserCameras_MGR.instance.Set_ProjMatrixCenter_ofCamera(camIx, p01);
		    pins?.RepositionPinUIFromPovData(camIx);
	    }

		    float ResolvePanDistanceScale(){
	        float refDist = 0f;
	        Vector3 refPt = transform.position;
	        bool hasRef = ModelsHandler_3D.instance != null
	            && ModelsHandler_3D.instance.TryGetNavigationReferenceWorldPoint(_myViewCam, out refPt);
	        if (hasRef) {
	            refDist = (transform.position - refPt).magnitude;
	        }

	        Vector3 centerOfSelection = ModelsHandler_3D.instance != null
	            ? ModelsHandler_3D.instance.GetTotalBounds_ofSelectedMeshes().center
	            : transform.position;
	        float unionDist = (transform.position - centerOfSelection).magnitude;

	        // Preserve per-target behavior but keep enough range when secondary objects are closer.
	        float dist = hasRef ? Mathf.Max(refDist, unionDist * 0.7f) : unionDist;
	        return Mathf.Max(dist, 0.01f);
	    }

	    void Start(){
	        Update_callbacks_MGR.navigation += OnUpdate;
	    }

	    void OnDestroy(){
	        Update_callbacks_MGR.navigation -= OnUpdate;
	    }
	}
}//end namespace
