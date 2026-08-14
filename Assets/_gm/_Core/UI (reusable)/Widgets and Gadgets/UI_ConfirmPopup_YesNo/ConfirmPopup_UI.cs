using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

namespace spz {

	public class ConfirmPopup_UI : MonoBehaviour{
	    public static ConfirmPopup_UI instance { get; private set; }

	    [SerializeField] Button _background_button;
	    [SerializeField] TextMeshProUGUI _header;
	    [SerializeField] Button _yes;
	    [SerializeField] Button _no;
	    [SerializeField] TextMeshProUGUI _yesText;
	    [SerializeField] TextMeshProUGUI _noText;
	    Action _act_onYes;
	    Action _act_onNo;
	    bool _alreadyShownOrHidden = false;

	    /// <summary>True while the dimmer/card is active (Yes/No pending).</summary>
	    public bool IsShowing =>
		    _background_button != null && _background_button.gameObject.activeInHierarchy;

	    private void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        // If Addon Manager parked sort for uninstall confirm, cancel restores it.
	        if (_act_onNo != null) {
		        Action cancel = _act_onNo;
		        _act_onYes = null;
		        _act_onNo = null;
		        try { cancel.Invoke(); } catch (Exception ex) {
			        Debug.LogWarning("[ConfirmPopup_UI] OnDestroy cancel: " + ex.Message);
		        }
	        }
	        if (instance == this)
	            instance = null;
	    }

	    void Start(){
	        _yes.onClick.AddListener(OnYesClicked);
	        _no.onClick.AddListener(OnNoClicked);
	        _background_button.onClick.AddListener(OnBackgroundClicked);
	        if(!_alreadyShownOrHidden){ _background_button.gameObject.SetActive(false); }
	    }

	    void Update(){
		    // Only while the dialog is visible — Escape must not steal keys when hidden.
		    if (!IsShowing) return;
	        if(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame){  OnNoClicked(); }
	    }

	    public void Show( string text,  Action onYes,  Action onNo, string yesText="Yes", string noText="No" ){
		    // Re-entrant Show overwrites callbacks. Invoke prior No/cleanup first so Addon Manager
		    // uninstall session restore (manager sort / canvas modes) cannot leak.
		    if (_act_onYes != null || _act_onNo != null) {
			    Action priorCancel = _act_onNo;
			    _act_onYes = null;
			    _act_onNo = null;
			    try { priorCancel?.Invoke(); } catch (Exception ex) {
				    Debug.LogWarning("[ConfirmPopup_UI] Prior cancel on re-Show: " + ex.Message);
			    }
		    }
	        _background_button.gameObject.SetActive(true);
	        _header.text = text;
	        _act_onYes = onYes;
	        _act_onNo = onNo;
	        _yesText.text = yesText;
	        _noText.text = noText;
	        _alreadyShownOrHidden = true;
	        // Re-assert chrome when shown — popup often starts inactive under ThemeChanged.
	        ApplyThemeTokens();
	    }

	    /// <summary>
	    /// Nomad flat dialog: panel shell + solid Yes/No cells (not Unity default light gradient bricks).
	    /// Ensure hit faces so label raycast clears cannot kill Close / Don't Close (exit litmus).
	    /// </summary>
	    void ApplyThemeTokens() {
	        Transform root = _background_button != null ? _background_button.transform : transform;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            SpzUiThemeOps.RestoreBoundChromeUnder(root);
	            if (_yes != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_yes.transform);
	            if (_no != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_no.transform);
	            if (_header != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_header.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        // Dimmer / panel shells — walk host + background (dialog card may be a sibling of the blocker).
	        ThemeShellImagesUnder(root, t);
	        if (!ReferenceEquals(root, transform))
	            ThemeShellImagesUnder(transform, t);
	        if (_header != null)
	            SpzUiThemeOps.ApplyBoundChromeTmp(_header, t.textPrimary, 16f);
	        // Don't Close = neutral; Close = danger (exit confirm).
	        ThemePopupButton(_no, t.controlBg, t.accent, _noText, t);
	        ThemePopupButton(_yes, t.danger, t.accent, _yesText, t);
	    }

	    void ThemeShellImagesUnder(Transform root, SpzUiThemeOps.ThemeTokens t) {
	        if (root == null) return;
	        foreach (var img in root.GetComponentsInChildren<Image>(true)) {
	            if (img == null) continue;
	            if (_yes != null && img.transform.IsChildOf(_yes.transform)) continue;
	            if (_no != null && img.transform.IsChildOf(_no.transform)) continue;
	            if (_yes != null && ReferenceEquals(img, _yes.targetGraphic)) continue;
	            if (_no != null && ReferenceEquals(img, _no.targetGraphic)) continue;
	            string n = img.gameObject.name ?? "";
	            bool isBlocker = _background_button != null
	                && (ReferenceEquals(img.gameObject, _background_button.gameObject)
	                    || n.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0
	                    || n.IndexOf("blocker", StringComparison.OrdinalIgnoreCase) >= 0
	                    || n.IndexOf("overlay", StringComparison.OrdinalIgnoreCase) >= 0);
	            Color fill = isBlocker
	                ? new Color(0f, 0f, 0f, Mathf.Clamp01(t.panelBg.a * 0.72f))
	                : SpzUiThemeOps.ResolvePanelShellColor();
	            SpzUiThemeOps.ApplyBoundChromeGraphic(img, fill);
	            if (!isBlocker)
	                SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	        }
	    }

	    static void ThemePopupButton(Button btn, Color fill, Color accent, TextMeshProUGUI label, SpzUiThemeOps.ThemeTokens t) {
	        if (btn == null) return;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        SpzUiThemeOps.ApplyBoundChromeSelectable(btn, fill, accent);
	        if (btn.targetGraphic is Image face) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
	            face.preserveAspect = false;
	            face.raycastTarget = true;
	        }
	        if (label != null)
	            SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(label, t.textPrimary, 15f);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    void OnYesClicked(){
	        Action act = _act_onYes;
	        _act_onYes = null;
	        _act_onNo = null;
	        act?.Invoke();
	        _background_button.gameObject.SetActive(false);
	    }

	    void OnNoClicked(){
	        Action act = _act_onNo;
	        _act_onYes = null;
	        _act_onNo = null;
	        act?.Invoke();
	        _background_button.gameObject.SetActive(false);
	    }

	    void OnBackgroundClicked(){
	        OnNoClicked();
	    }
	}
}//end namespace
