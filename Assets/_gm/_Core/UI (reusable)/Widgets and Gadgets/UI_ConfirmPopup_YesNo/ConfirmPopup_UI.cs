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

	    struct CanvasSortState {
		    public Canvas canvas;
		    public int sortingOrder;
		    public bool overrideSorting;
		    public RenderMode renderMode;
	    }

	    Canvas _parkedManagerCanvas;
	    int _parkedManagerSort;
	    Transform _popupRoot;
	    Vector3 _popupRootScale;
	    CanvasSortState[] _elevatedPopupStates;
	    bool _elevationActive;

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
	        if (_act_onNo != null) {
		        Action cancel = _act_onNo;
		        _act_onYes = null;
		        _act_onNo = null;
		        try { cancel.Invoke(); } catch (Exception ex) {
			        Debug.LogWarning("[ConfirmPopup_UI] OnDestroy cancel: " + ex.Message);
		        }
	        }
	        RestoreElevation();
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
		    if (!IsShowing) return;
	        if(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame){  OnNoClicked(); }
	    }

	    public void Show( string text,  Action onYes,  Action onNo, string yesText="Yes", string noText="No" ){
		    if (_act_onYes != null || _act_onNo != null) {
			    Action priorCancel = _act_onNo;
			    _act_onYes = null;
			    _act_onNo = null;
			    try { priorCancel?.Invoke(); } catch (Exception ex) {
				    Debug.LogWarning("[ConfirmPopup_UI] Prior cancel on re-Show: " + ex.Message);
			    }
		    }
		    RestoreElevation();
		    ElevateAboveAddonManagerIfOpen();
	        _background_button.gameObject.SetActive(true);
	        _header.text = text;
	        _act_onYes = onYes;
	        _act_onNo = onNo;
	        _yesText.text = yesText;
	        _noText.text = noText;
	        _alreadyShownOrHidden = true;
	        ApplyThemeTokens();
	    }

	    /// <summary>
	    /// AddonManager_Canvas is Overlay @ 32767; ConfirmPopup nested World Space (~1500) loses raycasts.
	    /// Drop manager sort and force this popup to Screen Space Overlay while showing.
	    /// </summary>
	    void ElevateAboveAddonManagerIfOpen() {
		    Canvas mgr = FindActiveAddonManagerCanvas();
		    if (mgr == null || !mgr.gameObject.activeInHierarchy)
			    return;

		    _parkedManagerCanvas = mgr;
		    _parkedManagerSort = mgr.sortingOrder;
		    mgr.sortingOrder = 100;

		    _popupRoot = transform;
		    _popupRootScale = _popupRoot.localScale;
		    if (_popupRoot.localScale.sqrMagnitude < 1e-6f)
			    _popupRoot.localScale = Vector3.one;

		    var canvases = GetComponentsInChildren<Canvas>(true);
		    _elevatedPopupStates = new CanvasSortState[canvases.Length];
		    const int baseOrder = 40000;
		    for (int i = 0; i < canvases.Length; i++) {
			    var c = canvases[i];
			    _elevatedPopupStates[i] = new CanvasSortState {
				    canvas = c,
				    sortingOrder = c != null ? c.sortingOrder : 0,
				    overrideSorting = c != null && c.overrideSorting,
				    renderMode = c != null ? c.renderMode : RenderMode.ScreenSpaceOverlay
			    };
			    if (c == null) continue;
			    c.renderMode = RenderMode.ScreenSpaceOverlay;
			    c.overrideSorting = true;
			    c.sortingOrder = baseOrder + i;
			    c.enabled = true;
			    if (c.GetComponent<GraphicRaycaster>() == null)
				    c.gameObject.AddComponent<GraphicRaycaster>();
		    }
		    _elevationActive = true;
	    }

	    void RestoreElevation() {
		    if (!_elevationActive) return;
		    _elevationActive = false;
		    if (_elevatedPopupStates != null) {
			    for (int i = 0; i < _elevatedPopupStates.Length; i++) {
				    var s = _elevatedPopupStates[i];
				    if (s.canvas == null) continue;
				    s.canvas.sortingOrder = s.sortingOrder;
				    s.canvas.overrideSorting = s.overrideSorting;
				    s.canvas.renderMode = s.renderMode;
			    }
			    _elevatedPopupStates = null;
		    }
		    if (_popupRoot != null) {
			    _popupRoot.localScale = _popupRootScale;
			    _popupRoot = null;
		    }
		    if (_parkedManagerCanvas != null) {
			    _parkedManagerCanvas.sortingOrder = _parkedManagerSort;
			    _parkedManagerCanvas = null;
		    }
	    }

	    static Canvas FindActiveAddonManagerCanvas() {
		    var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		    for (int i = 0; i < canvases.Length; i++) {
			    var c = canvases[i];
			    if (c == null || c.gameObject == null) continue;
			    if (c.gameObject.name != "AddonManager_Canvas") continue;
			    return c;
		    }
		    return null;
	    }

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
	        ThemeShellImagesUnder(root, t);
	        if (!ReferenceEquals(root, transform))
	            ThemeShellImagesUnder(transform, t);
	        if (_header != null)
	            SpzUiThemeOps.ApplyBoundChromeTmp(_header, t.textPrimary, 16f);
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
	        RestoreElevation();
	        act?.Invoke();
	        _background_button.gameObject.SetActive(false);
	    }

	    void OnNoClicked(){
	        Action act = _act_onNo;
	        _act_onYes = null;
	        _act_onNo = null;
	        RestoreElevation();
	        act?.Invoke();
	        _background_button.gameObject.SetActive(false);
	    }

	    void OnBackgroundClicked(){
	        OnNoClicked();
	    }
	}
}//end namespace
