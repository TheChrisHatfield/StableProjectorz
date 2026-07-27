using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	public class PayMoney_button : MonoBehaviour{
	    [SerializeField] Button _button;
    
	    void Awake(){
	        if (_button == null)
	            _button = GetComponent<Button>();
	        if (_button != null)
	            _button.onClick.AddListener(OnButtonPressed);
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void Start() {
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	    }

	    void OnButtonPressed(){
	        Application.OpenURL("https://stableprojectorz.com/thanks/");
	    }

	    /// <summary>Nomad: flat control cell + strip label (not magenta chrome plate).</summary>
	    void ApplyThemeTokens() {
	        if (_button == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(_button.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        SpzUiThemeOps.ApplyBoundChromeSelectable(_button, t.controlBg, t.accent);
	        if (_button.targetGraphic is Image img) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	            img.preserveAspect = false;
	        }
	        foreach (var tmp in _button.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp != null)
	                SpzUiThemeOps.ApplyBoundChromeStripLabelTmp(tmp, t.textPrimary, 12f);
	        }
	    }
	}
}//end namespace
