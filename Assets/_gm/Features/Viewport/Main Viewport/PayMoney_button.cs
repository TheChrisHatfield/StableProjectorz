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

	    /// <summary>
	    /// Nomad: flat cell + compact label. Do not use strip UpperCase/tracking — it overflows
	    /// into the Settings gear and reads as "SETTINGS / THAN" overlay soup.
	    /// </summary>
	    void ApplyThemeTokens() {
	        if (_button == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(_button.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        // Ensure hit face first — ApplySolidSquareChrome alone no-ops when targetGraphic is null,
	        // then ApplyBoundChromeTmp clears label raycasts → dead thank-you click under Nomad.
	        SpzUiThemeOps.ApplyBoundChromeSelectable(_button, t.controlBg, t.accent);
	        foreach (var tmp in _button.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            // Compact — BoundChromeTmp (~10 tracking) still spills into Settings gear.
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_button);
	    }
	}
}//end namespace
