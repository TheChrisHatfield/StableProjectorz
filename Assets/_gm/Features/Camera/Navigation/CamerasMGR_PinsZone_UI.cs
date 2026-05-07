using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace spz {

	// Controls the UI rectangle where we can drag around 2d points.
	// Each point (a pin) adjusts the projectionMatrix of its corresponding viewCamera.
	// Each pin corresponds the "center of perspective" for its corresponding camera
	public class CamerasMGR_PinsZone_UI : MonoBehaviour {
	    public static CamerasMGR_PinsZone_UI instance{get;private set;}= null;

	    // parent of all the pins. Allows us to turn them all off at once
	    // while each pin can still keep its activeSelf unchanged
	    [SerializeField] Transform _noEditMode_enabledGO;
	    [SerializeField] Transform _editMode_disabledGO;//cameras (except the 'current') are parented here when Editing mode is on
	    [Space(10)]
	    [SerializeField] List<GameObject> _cameraPins;
	    [Space(10)]
	    [SerializeField] Color _pinColor;
	    [SerializeField] Color _nearestPin_color;
	    [SerializeField] CamerasMGR_POVdefaults_UI _pinsDefaults;

	    GameObject _draggedPin = null;
	    int _draggedPinIx = -1;
	    Vector2 _draggedPin_cursorOffset;
	    float _flyControlsHint_recentTime = -999; //when did we print the 'helper-status-text' reminding user they can use WASD.

	    // MMB: only when cursor is within this radius (px) of the *nearest* pin in screen space.
	    // ROLLBACK NOTE: Before 2026-04, any MMB near the viewport grabbed the nearest pin (no distance gate);
	    //   re-remove this field and the MMB+radius line in GrabPin_maybe to restore that behavior.
	    [SerializeField] float _mmbPinGrabRadiusPx = 64f;

	    int NumVisiblePins(){ return _cameraPins.Count(p=>p.gameObject.activeInHierarchy); }

	    /// <summary>True while a camera pin is being moved (LMB or MMB drag).</summary>
	    public bool IsDraggingViewPin => _draggedPin != null;

	    /// <summary>True if the user is currently dragging the pin for <paramref name="cameraIndex"/>.</summary>
	    public bool IsDraggingThisCameraPin(int cameraIndex) {
		    return _draggedPin != null && _draggedPinIx == cameraIndex;
	    }

	    /// <summary>
	    /// Re-apply one pin's UI anchors from <see cref="UserCameras_MGR.get_viewCams_PovInfos"/>
	    /// (same as <see cref="UpdatePins_to_Locations"/>) so POV numbers move immediately after
	    /// <see cref="UserCameras_MGR.Set_ProjMatrixCenter_ofCamera"/> (e.g. MMB pan lock in multi-view).
	    /// </summary>
	    public void RepositionPinUIFromPovData(int cameraIndex) {
		    if (_cameraPins == null || cameraIndex < 0 || cameraIndex >= _cameraPins.Count) { return; }
		    if (UserCameras_MGR.instance == null) { return; }
		    var povInfos = UserCameras_MGR.instance.get_viewCams_PovInfos();
		    if (povInfos == null || cameraIndex >= povInfos.Count) { return; }
		    CameraPovInfo inf = povInfos[cameraIndex];
		    RectTransform pinRectTr = _cameraPins[cameraIndex].transform as RectTransform;
		    if (pinRectTr == null) { return; }
		    Vector2 center01 = inf.perspectiveCenter01;
		    pinRectTr.anchorMin = center01;
		    pinRectTr.anchorMax = center01;
		    pinRectTr.anchoredPosition = Vector2.zero;
	    }

	    /// <summary>
	    /// True if a middle-mouse *press this frame* would be consumed by the nearest camera pin, not <see cref="CameraPanning"/>.
	    /// Kept in sync with <see cref="GrabPin_maybe"/> preconditions; see also <see cref="_mmbPinGrabRadiusPx"/> rollback note.
	    /// </summary>
	    public bool MmbDownWouldGrabNearestPin() {
	        if (KeyMousePenInput.isMMBpressedThisFrame() == false) { return false; }
	        if (MainViewport_UI.instance == null || !MainViewport_UI.instance.isCursorHoveringMe()) { return false; }
	        if (KeyMousePenInput.isKey_CtrlOrCommand_pressed() || KeyMousePenInput.isKey_Shift_pressed()) { return false; }
	        if (KeyMousePenInput.isRMBpressed()) { return false; }
	        if (NumVisiblePins() == 0) { return false; }
	        if (DimensionMode_MGR.instance == null) { return false; }
	        if (DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_uv) { return false; }
	        // Mirror GrabPin_maybe: LMB+mode combinations block pin; do not let MmbDown "reserve" a grab that will not run.
	        bool lmb = KeyMousePenInput.isLMBpressed();
	        var dim = DimensionMode_MGR.instance._dimensionMode;
	        bool isMultiViewEditing = MultiView_Ribbon_UI.instance != null && MultiView_Ribbon_UI.instance._isEditingMode;
	        if (lmb && dim == DimensionMode.dim_gen_3d) { return false; }
	        if (lmb && dim == DimensionMode.dim_sd && !isMultiViewEditing) { return false; }

	        int nearestPinIx = FindNearestPin();
	        if (nearestPinIx < 0) { return false; }
	        return IsCursorWithinMmbGrabRadiusToPin(nearestPinIx);
	    }

	    bool IsCursorWithinMmbGrabRadiusToPin(int pinIx) {
	        if (pinIx < 0 || _cameraPins == null || pinIx >= _cameraPins.Count) { return false; }
	        var pinGO = _cameraPins[pinIx];
	        if (pinGO == null || pinGO.activeInHierarchy == false) { return false; }
	        float r = _mmbPinGrabRadiusPx;
	        var pinPos = (Vector2)pinGO.transform.position;
	        float sqr = Vector2.SqrMagnitude(pinPos - KeyMousePenInput.cursorScreenPos());
	        return sqr <= r * r;
	    }

	    //gives ability to initialize when scenes load.
	    public static System.Action OnStartInvoked { get; set; } = null;


	    public void OnOrderPinsButton(){
	        List<CameraPovInfo> povInfos =  UserCameras_MGR.instance.get_viewCams_PovInfos();
	        _pinsDefaults.OnOrderPinsButton(povInfos);
	    }

	    void OnToggledViewCamera(int cameraIx, bool isOn){
	        _cameraPins[cameraIx].gameObject.SetActive(isOn);
	    }

	    void OnCameraPlacements_Restored(GenData2D genData){
	        List<CameraPovInfo> povs = genData.povInfos.povs.ToList();//copy, just in case
	        List<int> ixs_to_instantly = null;
	        _pinsDefaults.Lerp_to_SpecificDestinations( povs, ixs_to_instantly);
	    }


	    void OnEditMode_Started(MultiView_StartEditMode_Args args){
	        _cameraPins.ForEach( p=>p.transform.SetParent(_editMode_disabledGO, worldPositionStays:true) );

	        List<CameraPovInfo> newPovInfos =  UserCameras_MGR.instance.get_viewCams_PovInfos();
	        // we might have received OnStartEditMode() callback before Cameras_MGR.
	        // So we need to manually ensure that all cameraPovInfos are flagged as disabled,
	        // except for single info of the camera that is the 'current' one:
	        for(int i=0; i<newPovInfos.Count; ++i){
	            bool isEnable  = i == UserCameras_MGR.instance.ix_currentViewCam ? true : false;
	            newPovInfos[i] = newPovInfos[i].Clone(wasEnabled_override:isEnable);
	        }

	        //just one entry active, so will center it on the screen:
	        _pinsDefaults.OnOrderPinsButton(newPovInfos);
	    }


	    void OnEditMode_Stopped( MultiView_StopEdit_Args howToRestore ){
	        _cameraPins.ForEach( p=>p.transform.SetParent(_noEditMode_enabledGO, worldPositionStays:true) );
	    }


	    void Update(){
	        ResizeSelf_to_InnerViewport();
	        UpdatePins_to_Locations();

	        GrabPin_maybe();
	        DropPin_maybe();
	        DragPin_maybe();


	        bool hoverMainView = MainViewport_UI.instance.isCursorHoveringMe();
	        bool isMMB         = KeyMousePenInput.isMMBpressed();
	        bool noUsual       = MainViewport_UI.instance.showing != MainViewport_UI.Showing.UsualView;

	        bool canShowNumbers  =  hoverMainView  ||  _draggedPin != null;
	             canShowNumbers &= (noUsual || isMMB);//show numbers when  inpainting only when user pans using MMB
	        if (canShowNumbers){
	            _cameraPins.ForEach(p=>p.GetComponentInChildren<FadeOutUnlessPersist_UI>().FadeInThisFrame());
	        }
	    }


	    void ResizeSelf_to_InnerViewport(){
	        Vector3 innerViewportPos = MainViewport_UI.instance.innerViewportRect.position;
	        Vector2 innerViewportSize =  MainViewport_UI.instance.innerViewportRect.rect.size;
	        RectTransform myRectTrsf = transform as RectTransform;
	        myRectTrsf.position = innerViewportPos;
	        myRectTrsf.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerViewportSize.x);
	        myRectTrsf.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, innerViewportSize.y);
	    }


	    //if user resized viewport, we might need to re-align the pins to their correct location.
	    void UpdatePins_to_Locations(){
	        List<CameraPovInfo> povInfos = UserCameras_MGR.instance?.get_viewCams_PovInfos();
	        if(povInfos==null){ return; }//scenes are probably loading.

	        for(int i=0; i<povInfos.Count; ++i){
	            CameraPovInfo inf = povInfos[i];
	            RectTransform pinRectTr =  _cameraPins[i].transform as RectTransform;
	            Vector2 center01    = inf.perspectiveCenter01;
	            pinRectTr.anchorMin = center01;
	            pinRectTr.anchorMax = center01;
	            pinRectTr.anchoredPosition = Vector2.zero;
	        }
	    }

	    void GrabPin_maybe(){
	        bool isHoveringViewport = MainViewport_UI.instance.isCursorHoveringMe();
	        bool isMMBpressed = KeyMousePenInput.isMMBpressed();
	        bool isLMBpressed = KeyMousePenInput.isLMBpressed();
	        bool isMousePressed = KeyMousePenInput.isMMBpressedThisFrame() || KeyMousePenInput.isLMBpressedThisFrame();
	        bool areModifiersPressed = KeyMousePenInput.isKey_CtrlOrCommand_pressed() || KeyMousePenInput.isKey_Shift_pressed();
	        //COMMENTED OUT, KEPT FOR PRECAUTION. Some people expect alt+MMB to work for panning:
	        //  areModifiersPressed |= KeyMousePenInput.isKey_alt_pressed(); 
	        bool isRMBpressed = KeyMousePenInput.isRMBpressed();
	        bool is_dimension_3d = DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_gen_3d;
	        bool is_dimension_sd = DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_sd;
	        bool is_dimension_uv = DimensionMode_MGR.instance._dimensionMode == DimensionMode.dim_uv;
	        bool isMultiViewEditing = MultiView_Ribbon_UI.instance != null && MultiView_Ribbon_UI.instance._isEditingMode;

	        if(!isHoveringViewport){return;}
	        if(!isMousePressed){ return; }
	        if(areModifiersPressed){ return; }//possibly zooming, orbiting, etc.
	        if(isRMBpressed){ return; }
	        if(NumVisiblePins()==0){ return; }// If all are hidden, return. During editing mode we will use Pan script on the camera.
	                                          // This way, we'll make it possible to pan further than main view window allows.
	        int nearestPinIx = FindNearestPin();
	        if(nearestPinIx < 0){ return;}
	        // LMB is for paint / mesh tools / screenshots in these modes — pin drag used MMB (same idea as dim_gen_3d).
	        if(isLMBpressed && is_dimension_3d){ return; }
	        if(isLMBpressed && is_dimension_sd && !isMultiViewEditing){ return; }
	        if(is_dimension_uv){ return; }//no draggnig of pins during inspection of UV.
	        if (KeyMousePenInput.isMMBpressedThisFrame() && !IsCursorWithinMmbGrabRadiusToPin(nearestPinIx)) { return; }

	        OnPinGrabbed(nearestPinIx, isMMBpressed);
	    }


	    void DropPin_maybe(){
	        bool isMMBpressed = KeyMousePenInput.isMMBpressed();
	        bool isLMBpressed = KeyMousePenInput.isLMBpressed();
	        if(_draggedPin == null || isMMBpressed || isLMBpressed){ return; }

	        bool wasLMB = KeyMousePenInput.isLMBreleasedThisFrame();
	        OnPinDropped(wasLMB);
	    }


	    public int FindNearestPin(){
	        float smallestDist = float.MaxValue;
	        Vector2 cursorPos = KeyMousePenInput.cursorScreenPos();
	        int nearestPinIx = -1;
	        for(int i=0; i<_cameraPins.Count; ++i){
	            GameObject pinGO = _cameraPins[i];
	            if(pinGO.activeInHierarchy==false){ continue; }
	            Vector2 pinPos = pinGO.transform.position;
	            float dist =  Vector2.SqrMagnitude(cursorPos - pinPos);
	            if(dist > smallestDist){ continue; }
	            smallestDist = dist;
	            nearestPinIx = i;
	        }
	        return nearestPinIx;
	    }


	#region dragging
	    void DragPin_maybe(){
	        if(_draggedPin == null){ return; }

	        Vector2 pinScreenPos =  KeyMousePenInput.cursorScreenPos() + _draggedPin_cursorOffset;

	        var vp = MainViewport_UI.instance;
	        Vector2 localPoint;
	        RectTransformUtility.ScreenPointToLocalPointInRectangle(vp.innerViewportRect, pinScreenPos, null, out localPoint);
	        Vector2 cursorPos01 = NormalizedPositionInRect_unclamped(vp.innerViewportRect.rect, localPoint);
        
	        bool isOutsideViewport =  cursorPos01.x>1.1 || cursorPos01.x<-0.1 || cursorPos01.y>1.1 || cursorPos01.y<-0.1;
	        float recentReminderElapsed = Time.time - _flyControlsHint_recentTime;
        
	        if(isOutsideViewport && recentReminderElapsed>15){
	            _flyControlsHint_recentTime = Time.time;
	            Viewport_StatusText.instance.ShowStatusText("Keep Viewports on screen.\nInstead, hold RightMouse + WASD or QE, to fly.  F to focus", false, 4, false);
	        }
	        //we must not allow the perspective center to be outside the [0,1] range.
	        //Otherwise leads to issues with depth, visibility of objects etc:
	        cursorPos01.x = Mathf.Clamp(cursorPos01.x, 0, 1);
	        cursorPos01.y = Mathf.Clamp(cursorPos01.y, 0, 1);

	        UserCameras_MGR.instance?.Set_ProjMatrixCenter_ofCamera(_draggedPinIx, cursorPos01);
	        _draggedPin.transform.position = pinScreenPos;
	    }

	    void OnPinGrabbed(int sensor_ix, bool isMiddleMouseButton){
	        if(sensor_ix == _draggedPinIx){ return; }
	        if (KeyMousePenInput.isRMBpressed()){ return; }
	        if (KeyMousePenInput.isKey_CtrlOrCommand_pressed()){ return; }
	        if (KeyMousePenInput.isKey_Shift_pressed()){ return; }
	        //NOTICE: allow alt+MMB (some people expect this combination for panning)
	        if (KeyMousePenInput.isKey_alt_pressed() && !isMiddleMouseButton){ return; }
	        _draggedPin = _cameraPins[sensor_ix];
	        _draggedPinIx = sensor_ix;
	        _pinsDefaults.EnsureNotLerping();
	        _draggedPin_cursorOffset = (Vector2)_draggedPin.transform.position - KeyMousePenInput.cursorScreenPos();
	    }

	    void OnPinDropped(bool isLeftMouseButton){
	        int pinIx = _draggedPinIx;
	        _draggedPin = null;
	        _draggedPinIx = -1;
	    }

	    Vector2 NormalizedPositionInRect_unclamped(Rect rect, Vector2 localPoint){
	        // Calculate normalized positions without clamping
	        float normalizedX = (localPoint.x - rect.xMin) / rect.width;
	        float normalizedY = (localPoint.y - rect.yMin) / rect.height;
	        return new Vector2(normalizedX, normalizedY);
	    }
	#endregion

	    void OnWillFocus(CameraFocus whoFocused, Vector3 boundsCenter){
	        for(int i=0; i<_cameraPins.Count; ++i){
            
	            Vector2 pinScreenPos = _cameraPins[i].transform.position;
	            var vp = MainViewport_UI.instance;
	            Vector2 localPoint;
	            RectTransformUtility.ScreenPointToLocalPointInRectangle(vp.innerViewportRect, pinScreenPos, null, out localPoint);
	            Vector2 cursorPos01 = Rect.PointToNormalized(vp.innerViewportRect.rect, localPoint);

	            UserCameras_MGR.instance.Set_ProjMatrixCenter_ofCamera(i, cursorPos01);
	            _cameraPins[i].transform.position = pinScreenPos;
	        }
	    }


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this.gameObject); return; }
	        instance = this;

	        _editMode_disabledGO.gameObject.SetActive(false);

	        UserCameras_MGR._Act_OnTogledViewCamera += OnToggledViewCamera;
	        UserCameras_MGR._Act_OnRestoreCameraPlacements += OnCameraPlacements_Restored;

	        MultiView_Ribbon_UI.OnStartEditMode += OnEditMode_Started;
	        MultiView_Ribbon_UI.OnStop1_EditMode += OnEditMode_Stopped;

	        for (int i=0; i<_cameraPins.Count; ++i){
	            int i_cpy = i;
	            _cameraPins[i].gameObject.SetActive(i==0);//only first pin is enabled
	            _cameraPins[i].GetComponent<CanvasGroup>().alpha = 0;
	        }
	        CameraFocus._Act_onFocused += OnWillFocus;
	        FullView_OuterPanel_Chrome_Binder.SyncChromeToDriver();
	    }//end()

	    void Start(){
	        OnStartInvoked?.Invoke();
	    }
     
	}
}//end namespace
