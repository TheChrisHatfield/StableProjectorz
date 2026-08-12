using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// A mini-preview of a controlnet,
	// to be shown below text prompts of the Input Panel.
	public class ControlNetUnit_Thumb_UI : MonoBehaviour{

	    [SerializeField] RectTransform rectTransform;
	    [Space(10)]
	    [SerializeField] RawImage _rawImg;
	    [SerializeField] CircleSlider_Snapping_UI _depthContrast_slider;
	    [SerializeField] CircleSlider_Snapping_UI _depthBrightness_slider;
	    [SerializeField] Material _ui_material;//knows how to show grayscale if the texture is R-channel depth.
	    [Space(10)]
	    [SerializeField] GameObject _plusIcon;
	    [SerializeField] AspectRatioFitter _aspectFitter;
	    [SerializeField] MouseClickSensor_UI _clickSensor;
	    [SerializeField] Button _closeButton;
	    [Space(10)]
	    [SerializeField] Image _frame;
	    [SerializeField] GameObject _clickMe_text;

	    Material _ui_material_cpy = null;//knows how to show grayscale if the texture is R-channel depth.

	    bool _isOn;
	    CameraTexType _texType;//kept as variable, so that we can Unlock UserCameras_Permissions if we are destroyed.

	    public ControlNetUnits_ThumbsList_UI _myOwnerList { get; private set; }
	    public static Action<ControlNetUnit_Thumb_UI> _Act_OnUnitThumb_Pressed { get; set; } = null;
	    public ControlNetUnit_UI _myUnit { get; private set; } = null;



	    public void OnUpdate(){
	        if(_myUnit.isActivated && !_isOn){  OnOpenButton(notifyTheUnit:false);  }
	        else if(!_myUnit.isActivated && _isOn){  OnCloseButton(notifyTheUnit:false); }

	        bool isDepthImage = _myUnit._whatImageToSend == WhatImageToSend_CTRLNET.Depth;
	        TextureTools_SPZ.SetKeyword_Material(_ui_material_cpy, "SHOW_R_CHANNEL_ONLY", isDepthImage);

	        if(_myUnit.isActivated){
	        _texType = UserCameras_Permissions.convert(_myUnit._whatImageToSend);
	            UserCameras_Permissions.LockOrUnlock_ByType(_texType, this, isLock: true);
	            Texture tex =  _myUnit.visibleTexture_ref;
	            _rawImg.texture = tex;
	            _ui_material_cpy.SetTexture("_MainTex", tex);
	            if (tex != null){  _aspectFitter.aspectRatio = tex.width/(float)tex.height;  }
	        }else{
	            UserCameras_Permissions.LockOrUnlock_ByType(_texType, this, isLock:false);
	            _rawImg.texture = null;
	            _ui_material_cpy.SetTexture("_MainTex", null);
	            _rawImg.enabled = false;//so that we see the background
	        }
        
	        _plusIcon.gameObject.SetActive(!_myUnit.isActivated);
	        ShowFrame_maybe();

	        bool showSliders =  _myOwnerList._clickedThumb==this  &&  
	                             _myUnit.isActivated  &&  _myUnit.isForDepth();
	        _depthContrast_slider.transform.parent.gameObject.SetActive( showSliders );
	        _depthBrightness_slider.transform.parent.gameObject.SetActive( showSliders );
        
	        if(showSliders){
	            _depthContrast_slider.SetSliderValue(LeftRibbon_UI.instance.depthContrast, false);
	            _depthBrightness_slider.SetSliderValue(LeftRibbon_UI.instance.depthBrightness, false);
	        }
	    }


	    void ShowFrame_maybe(){
	        _frame.gameObject.SetActive(_myOwnerList._clickedThumb == this);
	        ApplyThemeTokens();

	        bool hovers = false;
	        Vector2 cursorPos = KeyMousePenInput.cursorScreenPos();
	        hovers = RectTransformUtility.RectangleContainsScreenPoint(rectTransform, cursorPos);

	        bool showText =  _myUnit.isActivated  &&  hovers  &&  _myOwnerList.isShowing_previewPanel == false;
	        _clickMe_text.SetActive(showText); 
	    }
    

	    void OnThumbPressed(int mouseButton){
	        if(!_isOn){  OnOpenButton(notifyTheUnit:true);  }
	        _Act_OnUnitThumb_Pressed?.Invoke(this);
	    }

	    void OnCloseButton(bool notifyTheUnit){
	        _isOn = false;
	        _rawImg.enabled = false;//so that we see the background
	        _myUnit.GetComponent<CollapsableSection_UI>().OpenOrCloseSelf(false, 0.2f);
	        _closeButton.gameObject.SetActive(false);
	        UserCameras_Permissions.LockOrUnlock_ByType(_texType, this, isLock: false);
	    }

	    void OnOpenButton(bool notifyTheUnit){
	        _isOn = true;
	        _rawImg.enabled = true;
	        _myUnit.GetComponent<CollapsableSection_UI>().OpenOrCloseSelf(true, 0.2f);
	        _closeButton.gameObject.SetActive(true);
	    }

	    public void Init(ControlNetUnits_ThumbsList_UI ownerList,  ControlNetUnit_UI myUnit){
	        _myOwnerList = ownerList;
	        _myUnit = myUnit;
	        _isOn = _myUnit.isActivated;
	        if(_myUnit.isActivated){ OnOpenButton(notifyTheUnit:false); }
	        if(!_myUnit.isActivated){ OnCloseButton(notifyTheUnit:false); }
	    }

	    void OnDepthContrast_Slider(float value01){
	        LeftRibbon_UI.instance.SetDepthContrast01_fromCode(value01);
	    }

	    void OnDepthBrightness_Slider(float value01){
	        LeftRibbon_UI.instance.SetDepthBrightness01_fromCode(value01);
	    }


	    void Awake(){
	        _ui_material_cpy = new Material(_ui_material);
	        _ui_material = null;//to avoid mistakes. Use the _cpy from now on.
	        _rawImg.material = _ui_material_cpy;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            if (_closeButton != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_closeButton.transform);
	            // Dial leave self-silos BoundChrome (gen path: thumb depth after Leave Nomad).
	            if (_depthContrast_slider != null)
	                _depthContrast_slider.ApplyThemeTokens(Color.white, Color.white);
	            if (_depthBrightness_slider != null)
	                _depthBrightness_slider.ApplyThemeTokens(Color.white, Color.white);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_frame != null) {
	            bool selected = _myOwnerList != null && _myOwnerList._clickedThumb == this;
	            SpzUiThemeOps.ApplyBoundChromeGraphic(_frame, selected
	                ? Color.Lerp(t.tabActive, t.accent, 0.65f)
	                : t.controlBg);
	            SpzUiThemeOps.ApplyRoundedControlSprite(_frame, markEligible: true);
	        }
	        // Close disables the unit from the thumbs strip (gen path). Prefab may rely on label hits;
	        // Ensure face before any label raycast clears (CommandRibbon/SAVE litmus).
	        if (_closeButton != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_closeButton);
	            // Prefab close often uses X/icon as targetGraphic — SolidSquare blanks the glyph.
	            if (_closeButton.targetGraphic is Image closeGlyph
	                && closeGlyph.sprite != null
	                && closeGlyph.preserveAspect
	                && !UiRuntimeSprites.IsSolidRect(closeGlyph.sprite)) {
	                SpzUiThemeOps.ApplyBoundChromeIconTint(closeGlyph, t.danger);
	            } else {
	                SpzUiThemeOps.ApplyBoundChromeSelectable(_closeButton, t.controlBg, t.danger);
	                if (_closeButton.targetGraphic is Image closeFace) {
	                    SpzUiThemeOps.ApplyRoundedControlSprite(closeFace, markEligible: true);
	                    closeFace.preserveAspect = false;
	                }
	            }
	            foreach (var tmp in _closeButton.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
	                if (tmp != null)
	                    SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	            }
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_closeButton);
	        }
	        if (_plusIcon != null) {
	            foreach (var img in _plusIcon.GetComponentsInChildren<Image>(true)) {
	                if (img == null) continue;
	                SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(img);
	                SpzUiThemeOps.ApplyLineIconTint(img);
	            }
	        }
	        if (_clickMe_text != null) {
	            foreach (var tmp in _clickMe_text.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
	                if (tmp == null) continue;
	                SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textMuted, 11f);
	                tmp.raycastTarget = false;
	            }
	        }
	        // Thumb-local depth dials (shown when selected) — same Pass27 hit-face path as left-ribbon dials.
	        if (_depthContrast_slider != null)
	            _depthContrast_slider.ApplyThemeTokens(t.accent, t.textPrimary);
	        if (_depthBrightness_slider != null)
	            _depthBrightness_slider.ApplyThemeTokens(t.accent, t.textPrimary);
	    }

	    void Start(){
	        _clickSensor._onMouseClick += OnThumbPressed;
	        _closeButton.onClick.AddListener(()=>OnCloseButton(notifyTheUnit:true));

	        _depthContrast_slider.SetSliderValue(LeftRibbon_UI.instance.depthContrast, invokeCallback:false);
	        _depthContrast_slider.onValueChanged.AddListener( OnDepthContrast_Slider );
	        _depthBrightness_slider.onValueChanged.AddListener( OnDepthBrightness_Slider );
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        DestroyImmediate(_ui_material_cpy);
	        UserCameras_Permissions.LockOrUnlock_ByType(_texType, this, isLock: false);
	    }
	}
}//end namespace
