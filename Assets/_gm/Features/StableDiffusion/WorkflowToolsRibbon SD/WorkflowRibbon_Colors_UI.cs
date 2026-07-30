using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	public class WorkflowRibbon_Colors_UI : MonoBehaviour, IWorkflowModeToggle{
	    [SerializeField] Toggle _toggle;
	    [SerializeField] Animation _anim;
	    [SerializeField] SlideOut_Widget_UI _options_slideOut;
	    [SerializeField] MouseHoverSensor_UI _options_mouseHover;
	    [Space(10)]
	    [SerializeField] Button _bakeColors_button;//to extract brushed paint into a separate icon.
    
	    public bool isOn => _toggle.isOn;
	    public Action<bool> onValueChanged { get; set; } = null;
	    public Action onBakeColors_button { get; set; } = null;


	    bool _isDoingCallback = false;

	    float _next_hintTime  = 0;
	    int _num_hintsShown = 0;
	    int _hints_spacing = 15;

	    static int _latestHintShown_frame = 0;
	    public static bool didShowHint_thisFrame(){ return _latestHintShown_frame==Time.frameCount;}


	    public void EnableToggle(bool playAttentionAnim =false){
	        _toggle.isOn = true;
	        if(playAttentionAnim){ _anim.Play(); }
	        ShowHint_maybe();
	    }

	    public void SetOffWithoutNotify(){
	        _toggle.SetIsOnWithoutNotify(false);
	    }

	    void ShowHint_maybe(){
	        if(Time.time < _next_hintTime){ return; }
	        if(_num_hintsShown > 3){ return; }
	        if(DimensionMode_MGR.instance._dimensionMode != DimensionMode.dim_sd){ return; }
	        string msg = "Color-Inpaint:  GenArt will respect the colors according to the Re-do slider." +
	                     "\nRight click for color pallete.  Alt+Click to sample a color.  1,2,3 etc for Brush Strength.";
	        Viewport_StatusText.instance.ShowStatusText(msg, false, 6, false);
	        _num_hintsShown++;
	        _next_hintTime = Time.time + _hints_spacing*_num_hintsShown;
	        _latestHintShown_frame = Time.frameCount;
	    }

	    void OnValueChanged(bool isOn){
	        if(_isDoingCallback){ return; }//avoid recursion
	        _isDoingCallback = true;
	        onValueChanged?.Invoke(isOn);
	        _isDoingCallback = false;
	    }

	    void OnButton_BakeColors(){
	        onBakeColors_button?.Invoke();
	    }


	    // User toggled a setting to change the arrangement of the UI panels.
	    // We need to adjust the slide-outs, so that they still make sense:
	    void OnSettings_ToolRibbonSwapped(bool isSwapped){
	        if(_options_slideOut.isFlipped() == isSwapped){ return; }
	        _options_slideOut.Flip_if_possible();
	    }


	    void Update(){
	        if(isOn  &&  _options_mouseHover.isHovering){
	            _options_slideOut.Toggle_if_Different(true);
	        }
	        if (!isOn){
	            _options_slideOut.Toggle_if_Different(false);
	        }
	    }

	    void Awake(){
	        _toggle.onValueChanged.AddListener( OnValueChanged );
	        _bakeColors_button.onClick.AddListener( OnButton_BakeColors );
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void Start(){
	        Settings_MGR._Act_verticalRibbonsSwapped += OnSettings_ToolRibbonSwapped;
	        OnSettings_ToolRibbonSwapped(Settings_MGR.instance.get_viewport_isSwapVerticalRibbons());
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        Settings_MGR._Act_verticalRibbonsSwapped -= OnSettings_ToolRibbonSwapped;
	    }

	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            if (_options_slideOut != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_options_slideOut.transform);
	            if (_bakeColors_button != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_bakeColors_button.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_options_slideOut != null) {
	            var root = _options_slideOut.transform;
	            var panelImg = root.GetComponent<Image>();
	            if (panelImg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(panelImg, t.panelBg);
	            foreach (var img in root.GetComponentsInChildren<Image>(true)) {
	                if (img == null || img == panelImg) continue;
	                string n = img.gameObject.name ?? "";
	                if (n.IndexOf("Slide", System.StringComparison.OrdinalIgnoreCase) >= 0
	                    || n.IndexOf("Panel", System.StringComparison.OrdinalIgnoreCase) >= 0
	                    || n.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0)
	                    SpzUiThemeOps.ApplyBoundChromeGraphic(img, t.panelBg);
	            }
	            // Selectables before Compact — Compact clears label raycasts (bake/options dead clicks).
	            foreach (var btn in root.GetComponentsInChildren<Button>(true)) {
	                if (btn != null)
	                    SpzUiThemeOps.ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
	            }
	            foreach (var tog in root.GetComponentsInChildren<Toggle>(true)) {
	                if (tog != null)
	                    SpzUiThemeOps.ApplyBoundChromeSelectable(tog, t.controlBg, t.accent);
	            }
	            foreach (var tmp in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
	                if (tmp == null) continue;
	                if (tmp.GetComponentInParent<Button>(true) != null
	                    || tmp.GetComponentInParent<Toggle>(true) != null)
	                    SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	                else {
	                    SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary);
	                    tmp.characterSpacing = 0f;
	                }
	            }
	        }
	        if (_bakeColors_button != null) {
	            SpzUiThemeOps.ApplyBoundChromeSelectable(_bakeColors_button, t.controlBg, t.accent);
	            var bakeLabel = _bakeColors_button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
	            if (bakeLabel != null)
	                SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(bakeLabel, t.textPrimary, 11f);
	        }
	    }
	}
}//end namespace
