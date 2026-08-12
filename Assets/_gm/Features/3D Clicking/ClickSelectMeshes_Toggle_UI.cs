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

	    void OnEnable() {
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
	            ApplyActiveBar(_selectMode_toggle.transform, selected: false, SpzUiThemeOps.Active.accent);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        // Match left-ribbon wireframe/DEP: flat dark cell + accent edge bar when ON
	        // (0.14 fill alone is nearly invisible on Nomad controlBg).
	        Color normal = _selectMode_toggle.isOn
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	        SpzUiThemeOps.EnsureSelectableHitFace(_selectMode_toggle);
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
	        foreach (var tmp in _selectMode_toggle.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary);
	            SpzUiThemeOps.HideAuthoredGraphicForTheme(tmp);
	        }
	        ApplyActiveBar(_selectMode_toggle.transform, _selectMode_toggle.isOn, t.accent);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_selectMode_toggle);
	    }

	    /// <summary>Same edge bar as LeftRibbon DEP/INSIDE so ON state reads under Nomad.</summary>
	    static void ApplyActiveBar(Transform owner, bool selected, Color accent) {
	        if (owner == null) return;
	        Transform bar = SpzUiThemeOps.FindDirectChildIncludingInactive(owner, "MonolithActiveBar");
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome || !selected) {
	            if (bar != null) bar.gameObject.SetActive(false);
	            return;
	        }
	        bool created = false;
	        if (bar == null) {
	            var go = new GameObject("MonolithActiveBar", typeof(RectTransform));
	            go.transform.SetParent(owner, false);
	            bar = go.transform;
	            var image = go.AddComponent<Image>();
	            image.raycastTarget = false;
	            created = true;
	        }
	        var rt = bar as RectTransform;
	        rt.anchorMin = new Vector2(0f, 0.2f);
	        rt.anchorMax = new Vector2(0f, 0.8f);
	        rt.pivot = new Vector2(0f, 0.5f);
	        rt.offsetMin = new Vector2(0f, 0f);
	        rt.offsetMax = new Vector2(2f, 0f);
	        var img = bar.GetComponent<Image>();
	        img.sprite = null;
	        img.type = Image.Type.Simple;
	        img.color = accent;
	        bar.gameObject.SetActive(true);
	        if (created)
	            bar.SetAsLastSibling();
	    }
	}
}//end namespace
