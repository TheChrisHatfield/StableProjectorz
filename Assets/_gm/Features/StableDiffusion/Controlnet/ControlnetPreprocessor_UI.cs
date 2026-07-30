using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Linq;

namespace spz {

	public class ControlnetPreprocessor_UI : MonoBehaviour
	{
	    [SerializeField] ControlNetUnit_UI _unit;
	    [SerializeField] ControlNetUnit_Dropdowns _dropdowns;
	    [Space(10)]
	    [SerializeField] MouseHoverSensor_UI _preprocessorRes_hoverMe;
	    [SerializeField] SlideOut_Widget_UI _preprocessorRes_slideOut;
	    [SerializeField] Toggle _preprocessorRes_05;
	    [SerializeField] Toggle _preprocessorRes_1;
	    [SerializeField] Toggle _preprocessorRes_15; // x1.5 from the largest dimension in the Input panel
	    [SerializeField] Toggle _preprocessorRes_2;

	    bool _wasCreatedViaLoad = false;

	    public bool isReferencePreprocessor() => _dropdowns.isReferencePreprocessor();
	    public string currPreprocessorName() => _dropdowns.currPreprocessorName();
	    public bool is_currPreprocessor_none => _dropdowns.is_currPreprocessor_none;

	    public float get_processor_res(){
	        Vector2 widthHeight = SD_InputPanel_UI.instance.widthHeight();
	        int maxDim = Mathf.RoundToInt(  Mathf.Max(widthHeight.x, widthHeight.y)  );
	        if(_preprocessorRes_05.isOn){ return Mathf.RoundToInt(maxDim*0.5f); }
	        if(_preprocessorRes_1.isOn){  return Mathf.RoundToInt(maxDim*1); }
	        if(_preprocessorRes_15.isOn){ return Mathf.RoundToInt(maxDim*1.5f); }
	        if(_preprocessorRes_2.isOn){  return Mathf.RoundToInt(maxDim*2); }
	        return 512;
	    }


	    void OnPreprocessorResHover(bool isStoppedHover){ 
	        if(!isStoppedHover){ 
	            _preprocessorRes_slideOut.Toggle_if_Different(true); 
	            return; 
	        }
	    }

	    void OnPreprocessorToggle(Toggle tog, bool isOn){
	        if(!isOn){ return; }
	        string txt = tog.GetComponentInChildren<TextMeshProUGUI>().text;
	        _preprocessorRes_hoverMe.GetComponentInChildren<TextMeshProUGUI>().text =  "res <size=80%>x</size>" + txt;
	        if (_unit != null)
	            _unit.RefreshBoundChromeSelection();
	    }


	    void Awake(){
	        _preprocessorRes_hoverMe.onSurfaceEnter += (cursor)=>OnPreprocessorResHover(isStoppedHover:false);
	        _preprocessorRes_hoverMe.onSurfaceExit  += (cursor)=>OnPreprocessorResHover(isStoppedHover:true);
	        _preprocessorRes_05.onValueChanged.AddListener( (isOn)=>OnPreprocessorToggle(_preprocessorRes_05,isOn)  );
	        _preprocessorRes_1.onValueChanged.AddListener(  (isOn)=>OnPreprocessorToggle(_preprocessorRes_1, isOn)  );
	        _preprocessorRes_15.onValueChanged.AddListener( (isOn)=>OnPreprocessorToggle(_preprocessorRes_15,isOn)  );
	        _preprocessorRes_2.onValueChanged.AddListener(  (isOn)=>OnPreprocessorToggle(_preprocessorRes_2, isOn)  );
	        if(_wasCreatedViaLoad==false){//checks if wasn't spawned by a project-save file.
	            _preprocessorRes_1.isOn = true;
	        }
	    }


	    public void Save(ControlNetUnit_SL unit_sl){
	        Save_PreprocessorRes(unit_sl);
	    }

	    public void Load(ControlNetUnit_SL unit_sl){
	        _wasCreatedViaLoad = true;
	        Load_PreprocessorRes(unit_sl);
	    }

	    void Save_PreprocessorRes( ControlNetUnit_SL unit_sl ){
	        if( _preprocessorRes_05.isOn ){
	            unit_sl.preprocessorRes_factor = 0.5f;
	        }else if(_preprocessorRes_1.isOn){
	            unit_sl.preprocessorRes_factor = 1.0f;
	        }else if(_preprocessorRes_15.isOn){
	            unit_sl.preprocessorRes_factor = 1.5f;
	        }else{
	            unit_sl.preprocessorRes_factor = 2;
	        }
	    }

	    void Load_PreprocessorRes( ControlNetUnit_SL unit_sl ){
	        Toggle tog = null;
	        if(unit_sl.preprocessorRes_factor <= 0.6){
	            tog = _preprocessorRes_05;
	        }else if(unit_sl.preprocessorRes_factor <= 1.1){
	            tog = _preprocessorRes_1;
	        }else if(unit_sl.preprocessorRes_factor <= 1.6){
	            tog = _preprocessorRes_15;
	        }else{
	            tog = _preprocessorRes_2;
	        }//manually invoke the callback, if our Awake function wasn't invoked yet:
	        tog.isOn = true;
	        OnPreprocessorToggle(tog, true);
	    }

	    public void CopyFromAnother(ControlnetPreprocessor_UI other){
	        _preprocessorRes_05.isOn = other._preprocessorRes_05.isOn;
	        _preprocessorRes_1.isOn  = other._preprocessorRes_1.isOn;
	        _preprocessorRes_15.isOn = other._preprocessorRes_15.isOn;
	        _preprocessorRes_2.isOn  = other._preprocessorRes_2.isOn;
	    }

	    /// <summary>True when <paramref name="toggle"/> is a preprocessor resolution radio (.5 / 1 / 1.5 / 2).</summary>
	    public bool OwnsResToggle(Toggle toggle) {
	        if (toggle == null) return false;
	        return ReferenceEquals(toggle, _preprocessorRes_05)
	            || ReferenceEquals(toggle, _preprocessorRes_1)
	            || ReferenceEquals(toggle, _preprocessorRes_15)
	            || ReferenceEquals(toggle, _preprocessorRes_2);
	    }

	    /// <summary>
	    /// Nomad flat chrome for the "res xN" hover chip (not a Button — unit Button scan missed it)
	    /// and resolution slide-out radios.
	    /// </summary>
	    public void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            if (_preprocessorRes_hoverMe != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_preprocessorRes_hoverMe.transform);
	            if (_preprocessorRes_slideOut != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_preprocessorRes_slideOut.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        ThemeResTriggerChip(t);
	        ThemeResRadio(_preprocessorRes_05, t);
	        ThemeResRadio(_preprocessorRes_1, t);
	        ThemeResRadio(_preprocessorRes_15, t);
	        ThemeResRadio(_preprocessorRes_2, t);
	    }

	    void ThemeResTriggerChip(SpzUiThemeOps.ThemeTokens t) {
	        if (_preprocessorRes_hoverMe == null) return;
	        var face = _preprocessorRes_hoverMe.GetComponent<Image>();
	        if (face != null) {
	            // Peach 9-slice bevel → flat Nomad control (gen/SAVE litmus). Keep raycast for hover.
	            SpzUiThemeOps.ApplyBoundChromeGraphic(face, t.controlBg);
	            SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
	            face.preserveAspect = false;
	            face.raycastTarget = true;
	        }
	        foreach (var img in _preprocessorRes_hoverMe.GetComponentsInChildren<Image>(true)) {
	            if (img == null || img == face) continue;
	            if (img.GetComponentInParent<Toggle>(true) != null) continue;
	            if (_preprocessorRes_slideOut != null
	                && img.transform.IsChildOf(_preprocessorRes_slideOut.transform))
	                continue;
	            string n = img.gameObject.name ?? "";
	            if (n.IndexOf("pressed", System.StringComparison.OrdinalIgnoreCase) >= 0)
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	        }
	        foreach (var tmp in _preprocessorRes_hoverMe.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            if (tmp.GetComponentInParent<Toggle>(true) != null) continue;
	            if (_preprocessorRes_slideOut != null
	                && tmp.transform.IsChildOf(_preprocessorRes_slideOut.transform))
	                continue;
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 12f);
	            tmp.raycastTarget = false;
	        }
	    }

	    static void ThemeResRadio(Toggle toggle, SpzUiThemeOps.ThemeTokens t) {
	        if (toggle == null) return;
	        Color fill = toggle.isOn
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	        // ThemeFlatToolToggle already applies CompactToolLabel — do not re-ApplyBoundChromeTmp
	        // (that restores Nomad tracking ~10 and undoes Soft no-wrap litmus on .5/1/1.5/2).
	        SpzUiThemeOps.ThemeFlatToolToggle(toggle, fill, t.accent, t.textPrimary);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);
	    }
	}
}//end namespace
