using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	public class MultiView_CamerasFOV : MonoBehaviour
	{
	    [SerializeField] TextMeshProUGUI _cam_FOV_numberText;
	    [SerializeField] SliderUI_Snapping _camera_FOV_slider;

	    bool _fov_isSendingCallback = false;//helps avoid recursion.
	    bool _fovSliderBeingPressed = false;
	    void OnFovSliderPressed(){
	        if(_fovSliderBeingPressed){ return; }
	        if( UserCameras_MGR.instance == null ){ return; }
	        _fovSliderBeingPressed = true;
	        UserCameras_MGR.instance.StartFOV_compensatedAdjustment();
	    }

	    void OnFovSliderReleased(){
	        _fovSliderBeingPressed = false;
	    }

	    void OnFOV_slider(float value){
	        _cam_FOV_numberText.text = Mathf.RoundToInt(value).ToString();
	        // If value is set directly (from code), ensure Pressed/Released callbacks
	        // are still invoked! It's important, to initiate the fov-compensated-adjustment, etc.
	        bool wasntPressed = !_fovSliderBeingPressed;
	        if(wasntPressed){ OnFovSliderPressed(); }
	        _fov_isSendingCallback = true;
	        if( UserCameras_MGR.instance != null )
		        UserCameras_MGR.instance.SetFieldOfView_allCameras(value);
	        _fov_isSendingCallback = false;
	        if(wasntPressed){ OnFovSliderReleased(); }
	    }

	    //if was changed through code, not because of our own fov slider:
	    void OnCameraMGR_FovChanged(float fov){
	        if(_fov_isSendingCallback){ return; }//skip, it's due to our own callback.
	        _camera_FOV_slider.SetSliderValue(fov, invokeCallback:false);
	    }

	    void OnCameraPlacements_Restored(GenData2D genData){
	        if (genData.povInfos.numEnabled==0){ return; }
	        float fov =  genData.povInfos.get_Nth_active_pov(0).camera_fov;
	        _camera_FOV_slider.SetSliderValue(fov, invokeCallback:false);
	        _cam_FOV_numberText.text = Mathf.RoundToInt(fov).ToString();
	    }

	    void Awake(){
	        UserCameras_MGR._Act_OnRestoreCameraPlacements += OnCameraPlacements_Restored;
	        UserCameras_MGR._Act_OnFovChanged += OnCameraMGR_FovChanged;

	        _camera_FOV_slider.onValueChanged.AddListener( OnFOV_slider );
	        EventTrigger.Entry entryDown = new EventTrigger.Entry();
	        entryDown.eventID = EventTriggerType.PointerDown;
	        entryDown.callback.AddListener( (data)=>OnFovSliderPressed() );

	        EventTrigger.Entry entryUp = new EventTrigger.Entry();
	        entryUp.eventID = EventTriggerType.PointerUp;
	        entryUp.callback.AddListener((data) => OnFovSliderReleased());

	        _camera_FOV_slider.GetComponent<EventTrigger>().triggers.Add(entryDown);
	        _camera_FOV_slider.GetComponent<EventTrigger>().triggers.Add(entryUp);

	        EnsureFovFillThumbMarker();
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnEnable() {
	        ApplyThemeTokens();
	    }

	    void Start(){
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        UserCameras_MGR._Act_OnRestoreCameraPlacements -= OnCameraPlacements_Restored;
	        UserCameras_MGR._Act_OnFovChanged -= OnCameraMGR_FovChanged;
	    }

	    void EnsureFovFillThumbMarker() {
	        if (_camera_FOV_slider == null || _camera_FOV_slider.UnitySlider == null) return;
	        var fillThumb = _camera_FOV_slider.UnitySlider.GetComponent<SpzUiThemeNomadFillThumb>();
	        if (fillThumb == null)
	            fillThumb = _camera_FOV_slider.UnitySlider.gameObject.AddComponent<SpzUiThemeNomadFillThumb>();
	        fillThumb.icon = StudioLineIcon.Camera;
	    }

	    void ApplyThemeTokens() {
	        EnsureFovFillThumbMarker();
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            if (_camera_FOV_slider != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_camera_FOV_slider.transform);
	            // Do NOT ApplyNomadSliderChrome on leave — that re-Nomads fill after Restore (FOV litmus).
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_cam_FOV_numberText != null) {
	            // Snapshot first — FOV overlay must not steal slider drag (num-cams litmus).
	            SpzUiThemeOps.ApplyBoundChromeDialValueTmp(_cam_FOV_numberText, t.textPrimary);
	            _cam_FOV_numberText.raycastTarget = false;
	        }
	        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null || tmp == _cam_FOV_numberText) continue;
	            // Captions — ReadableBody (not Compact truncate) so FOV help stays legible beside the dial.
	            SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, t.textPrimary, 11f);
	            tmp.raycastTarget = false;
	        }
	        // Nomad: mustard fill is the slider; Camera icon centered on fill with slight overlay.
	        if (_camera_FOV_slider != null && _camera_FOV_slider.UnitySlider != null)
	            SpzUiThemeOps.ApplyNomadSliderChrome(_camera_FOV_slider.UnitySlider);
	    }

	}
}//end namespace
