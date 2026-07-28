using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// references a toggle on the ui-ribbon, for entering "Object Selection" mode.
	// Invokes event when OnToggled(), via StaticEvents.
	public class ClickSelectMeshes_Toggle_UI : MonoBehaviour{

	    [SerializeField] Toggle _selectMode_toggle;
	    [SerializeField] Animation _selectMode_toggleAnim;

	    public void PlayAnim(){
	        _selectMode_toggleAnim.Play();
	    }

	    public void SetIsOnWithoutNotify(bool isToggleOn){ 
	        // FIX: Use SetIsOnWithoutNotify to prevent triggering the 'onValueChanged' event loop
	        _selectMode_toggle.SetIsOnWithoutNotify(isToggleOn);
	    }

	    void OnToggled(bool isOn){
	        StaticEvents.Invoke(nameof(ClickSelectMeshes_Toggle_UI)+"_toggle", isOn);
	        ApplyThemeTokens();
	    }

	    void Awake(){
	        EventsBinder.Bind_Clickable_to_event(nameof(ClickSelectMeshes_Toggle_UI), this);
	        _selectMode_toggle.onValueChanged.AddListener(OnToggled);
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void Start(){
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        _selectMode_toggle?.onValueChanged.RemoveListener(OnToggled);
	    }

	    void ApplyThemeTokens() {
	        if (_selectMode_toggle == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(_selectMode_toggle.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        // Match left-ribbon wireframe/DEP: flat dark cell, not chrome/gold plate.
	        Color normal = _selectMode_toggle.isOn
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	        SpzUiThemeOps.ApplyBoundChromeSelectable(_selectMode_toggle, normal, t.accent);
	        if (_selectMode_toggle.targetGraphic is Image bg)
	            SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
	        if (_selectMode_toggle.graphic is Image check && check != _selectMode_toggle.targetGraphic)
	            SpzUiThemeOps.HideAuthoredGraphicForTheme(check);
	        foreach (var img in _selectMode_toggle.GetComponentsInChildren<Image>(true)) {
	            if (img == null || img == _selectMode_toggle.targetGraphic) continue;
	            string n = img.gameObject.name ?? "";
	            if (n == "MonolithActiveBar" || n == "MonolithLineIcon") continue;
	            if (n.Equals("Checkmark", System.StringComparison.OrdinalIgnoreCase)
	                || n.IndexOf("pressed", System.StringComparison.OrdinalIgnoreCase) >= 0
	                || n.Equals("tick", System.StringComparison.OrdinalIgnoreCase))
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	        }
	        SpzUiThemeOps.ApplyControlLineIcon(_selectMode_toggle.transform, StudioLineIcon.Cursor, 20f);
	    }
	}
}//end namespace
