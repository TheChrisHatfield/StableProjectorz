using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	public enum TabletPressureMode { 
	    AffectSize, 
	    AffectOpacity, 
	    AffectBoth, 
	    AffectNone, 
	}

	// Helps the 'BrushRibbon_UI' component.
	// Only deals with what a drawing-tablet should affect.
	public class BrushRibbon_UI_PressureMode : MonoBehaviour{

	    [SerializeField] BrushRibbon_UI_Hardness _hardness;
	    [SerializeField] SlideOut_Widget_UI _optionsSlideOut;
	    [Space(10)]
	    [SerializeField] Toggle _size_toggle;
	    [SerializeField] Toggle _opacity_toggle;
	    [SerializeField] Toggle _both_toggle;
	    [SerializeField] Toggle _none_toggle;

	    public Toggle SizeToggle => _size_toggle;
	    public Toggle OpacityToggle => _opacity_toggle;
	    public Toggle BothToggle => _both_toggle;
	    public Toggle NoneToggle => _none_toggle;

	    public TabletPressureMode _mode { get; private set; } = TabletPressureMode.AffectBoth;

	    /// <summary>True when <paramref name="tmp"/> is a pressure-mode letter label (N/B/S/O).</summary>
	    public bool OwnsLabel(TMP_Text tmp) {
	        if (tmp == null) return false;
	        return IsUnder(tmp, _size_toggle) || IsUnder(tmp, _opacity_toggle)
	            || IsUnder(tmp, _both_toggle) || IsUnder(tmp, _none_toggle);
	    }

	    static bool IsUnder(TMP_Text tmp, Toggle toggle) {
	        return toggle != null && tmp.transform.IsChildOf(toggle.transform);
	    }


	    void OnHardnessHovered()
	        => _optionsSlideOut.Toggle_if_Different(true);


	    void OnToggle(Toggle tog, bool isOn){
	        if(!isOn){ return; }
	        if(tog == _size_toggle){ _mode = TabletPressureMode.AffectSize; }
	        if(tog == _opacity_toggle){ _mode = TabletPressureMode.AffectOpacity; }
	        if(tog == _both_toggle){ _mode = TabletPressureMode.AffectBoth; }
	        if(tog == _none_toggle){ _mode = TabletPressureMode.AffectNone; }
	        BrushRibbon_UI.instance?.NotifyPressureModeChromeChanged();
	    }


	     // User toggled a setting to change the arrangement of the UI panels.
	    // We need to adjust the slide-outs, so that they still make sense:
	    void OnSettings_ToolRibbonSwapped(bool isSwapped){
	        if(_optionsSlideOut.isFlipped() == isSwapped){ return; }
	        _optionsSlideOut.Flip_if_possible();
	    }


	    void Awake(){
	        _hardness.onHovered += OnHardnessHovered;
	        _size_toggle.onValueChanged.AddListener( isOn=>OnToggle(_size_toggle, isOn) );
	        _opacity_toggle.onValueChanged.AddListener( isOn=>OnToggle(_opacity_toggle, isOn) );
	        _both_toggle.onValueChanged.AddListener( isOn=>OnToggle(_both_toggle, isOn) );
	        _none_toggle.onValueChanged.AddListener( isOn=>OnToggle(_none_toggle,isOn) );
	    }

	    void Start(){
	        Settings_MGR._Act_verticalRibbonsSwapped += OnSettings_ToolRibbonSwapped;
	        OnSettings_ToolRibbonSwapped( Settings_MGR.instance.get_viewport_isSwapVerticalRibbons() );
	    }

	    void OnDestroy(){
	        if(_hardness!= null){
	            _hardness.onHovered -= OnHardnessHovered;
	        }
	    }
	}
}//end namespace
