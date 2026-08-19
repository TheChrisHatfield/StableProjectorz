using UnityEngine;
using UnityEngine.InputSystem;

namespace spz {

	//Flies towards and away from nearest selected 3D mesh.
	public class CameraDolly : MonoBehaviour{

	    [SerializeField] float _zoomSpeed = 0.3f;
	    [SerializeField] View_UserCamera _myViewCam;

	    bool _allowZoom = false;
	    public bool _isZooming { get; private set; } = false;
	    float _actualSpeed;//recomputed based on distance to object, at exact single moment when we start to drag.


	    private void OnApplicationFocus(bool focus){
	        //important! Feb 2024 user told that after opening File window they couldn't
	        //manipulate camera anymore, even after focusing the stable projectorz
	        // Only clear sticky dolly on focus *loss* — clearing on focus-gain aborted a fresh Alt+RMB zoom.
	        if (!focus) {
	            _allowZoom = false;
	            _isZooming = false;
	        }
	    }


	    void OnUpdate(){
	        var cams = UserCameras_MGR.instance;
	        var dims = DimensionMode_MGR.instance;
	        if (cams == null || dims == null) {
	            _allowZoom = false;
	            _isZooming = false;
	            return;
	        }
	        View_UserCamera nearestCam = cams.NearestToCursor();
	        bool navAllowed = dims.is_3d_navigation_allowed;

	        if (nearestCam != _myViewCam || !navAllowed){
	            _allowZoom = false;
	            _isZooming = false;
	            return; 
	        }
	        StartDolly_maybe();
	        StopDollyMaybe();
	        ZoomCameraMaybe();
	    }

	    void StartDolly_maybe(){
	        bool hovering   =  (MainViewport_UI.instance?.isCursorHoveringMe() ?? false);
	        if(!hovering){ return; }
	        // Arm on Alt+RMB held — not only RMB-down. Move fly starts on bare RMB and clears
	        // _allowZoom the same frame (StopDollyMaybe, no Alt yet); pressing Alt afterward while
	        // RMB is still held never re-armed, so Alt+RMB dolly after fly was dead.
	        // Do NOT LockNavigationCamera here: bare RMB is CameraMove fly. Alt+RMB zoom rides
	        // the same camera Move already locked; scroll-wheel zoom is instantaneous.
	        if(KeyMousePenInput.isRMBpressed() && KeyMousePenInput.isKey_alt_pressed()){
	            _allowZoom=true;
	        }
	    }


	    void StopDollyMaybe(){
	        bool hasCtrl      = KeyMousePenInput.isKey_CtrlOrCommand_pressed();
	        float mouseScroll = hasCtrl || KeyMousePenInput.isFileBrowserOpen() ? 0 : Mouse.current.scroll.ReadValue().y;

	        bool hasLeftAlt = KeyMousePenInput.isKey_alt_pressed();
	        bool hasRMB     = KeyMousePenInput.isRMBpressed();

	        bool dontZoom  =  !_allowZoom || !hasLeftAlt || !hasRMB; 
	             dontZoom &=  mouseScroll==0 || (MainViewport_UI.instance?.isCursorHoveringMe() ?? false)==false;

	        if(dontZoom){
	            _allowZoom = false;
	            _isZooming = false;
	            return; 
	        }
	    }


	    void ZoomCameraMaybe(){
	        Vector2 delta = KeyMousePenInput.delta_while_RMBpressed();
	        float mouseMovementMagnitude;
	        float zoomDirection;

	        //settings might use either of those modifiers do do something else:
	        bool hasCtrl      = KeyMousePenInput.isKey_CtrlOrCommand_pressed();
	        bool hasShift      = KeyMousePenInput.isKey_Shift_pressed();

	        float mouseScroll = (hasCtrl||hasShift||KeyMousePenInput.isFileBrowserOpen())? 0 : Mouse.current.scroll.ReadValue().y;

	        bool hasMouseScroll = mouseScroll!=0  &&  (MainViewport_UI.instance?.isCursorHoveringMe() ?? false);
        
	        if (!_allowZoom && !hasMouseScroll){ return; }

	        if (!_isZooming){
	            _isZooming = true;
	            AdjustZoomSpeed();
	        }

	        // Use mouse wheel if there's scroll input, else use delta from right mouse button
	        if (mouseScroll != 0) {
	            mouseMovementMagnitude = Mathf.Abs(mouseScroll);
	            zoomDirection = mouseScroll >= 0 ? 1 : -1;
	            zoomDirection /= 100.0f;//important! scaling down, but only for the scroll wheel.
	        } else {
	            //users asked to invert vertical zoom jul 2024, so outputing delta.x and -delta.y
	            float predominantAxisValue = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : -delta.y;
	            mouseMovementMagnitude = Mathf.Abs(predominantAxisValue);
	            zoomDirection = predominantAxisValue >= 0 ? 1 : -1;
	        }

	        float mouseZoom = mouseMovementMagnitude * zoomDirection * _actualSpeed;
	        transform.Translate(Vector3.forward * mouseZoom, Space.Self);
	    }


	    void AdjustZoomSpeed(){
	        if (ModelsHandler_3D.instance == null){ 
	            _actualSpeed=_zoomSpeed; 
	            return; //scenes are probaly still loading.
	        }
	        float distanceToTarget;
	        if (ModelsHandler_3D.instance.TryGetNavigationReferenceWorldPoint(_myViewCam, out var refPt)) {
	            distanceToTarget =  (refPt - transform.position).magnitude;
	        } else {
	            Bounds bounds  = ModelsHandler_3D.instance.GetTotalBounds_ofSelectedMeshes();
	            distanceToTarget =  (bounds.center - transform.position).magnitude;
	        }
	        // Adjust the zoom speed based on distance
	        _actualSpeed = _zoomSpeed*distanceToTarget;
	    }

    
	    void Start(){
	        _myViewCam = GetComponentInParent<View_UserCamera>();
	        Update_callbacks_MGR.navigation += OnUpdate;
	    }

	    void OnDestroy()
	    {
	        _allowZoom = false;
	        _isZooming = false;
	        Update_callbacks_MGR.navigation -= OnUpdate;
	    }

	    void OnDisable(){
	        _allowZoom = false;
	        _isZooming = false;
	    }


	}
}//end namespace
