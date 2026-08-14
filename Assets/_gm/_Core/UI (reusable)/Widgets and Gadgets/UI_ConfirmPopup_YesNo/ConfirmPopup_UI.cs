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

	    struct RectTransformState {
		    public RectTransform rt;
		    public Vector2 anchorMin;
		    public Vector2 anchorMax;
		    public Vector2 offsetMin;
		    public Vector2 offsetMax;
		    public Vector2 pivot;
		    public Vector3 localScale;
		    public Quaternion localRotation;
	    }

	    Canvas _parkedManagerCanvas;
	    int _parkedManagerSort;
	    CanvasSortState[] _elevatedPopupStates;
	    RectTransformState _savedRootRt;
	    RectTransformState _savedBackgroundRt;
	    bool _hasSavedRootRt;
	    bool _hasSavedBackgroundRt;
	    bool _elevationActive;
	    /// <summary>Ignore dimmer clicks until the opening pointer is released (same-click dismiss).</summary>
	    bool _suppressBackgroundDismissUntilPointerUp;
	    /// <summary>Hard cap so a missing Mouse/Pen device cannot freeze the whole app behind the dimmer.</summary>
	    float _suppressBackgroundUntilUnscaled = -1f;
	    const float SuppressBackgroundMaxSec = 0.45f;
	    const int ConfirmOverlaySortBase = 50000;

	    /// <summary>True while the dimmer/card is active (Yes/No pending).</summary>
	    public bool IsShowing =>
		    _background_button != null && _background_button.gameObject.activeInHierarchy;

	    /// <summary>True when the visible prompt is the Exit "Close the program?" dialog.</summary>
	    public bool IsCloseProgramPrompt =>
		    IsShowing && _header != null
		    && !string.IsNullOrEmpty(_header.text)
		    && _header.text.IndexOf("Close the program?", StringComparison.Ordinal) >= 0;

	    private void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        // Do not invoke user onNo here — that falsely reports "Uninstall cancelled" on scene unload.
	        _act_onYes = null;
	        _act_onNo = null;
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
		    if (_suppressBackgroundDismissUntilPointerUp) {
			    if (Time.unscaledTime >= _suppressBackgroundUntilUnscaled || !IsAnyPrimaryPointerDown())
				    _suppressBackgroundDismissUntilPointerUp = false;
		    }
	        bool esc =
		        (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
#pragma warning disable CS0618
		        || Input.GetKeyDown(KeyCode.Escape);
#pragma warning restore CS0618
	        if (esc)
		        OnNoClicked();
	    }

	    static bool IsAnyPrimaryPointerDown() {
		    var mouse = Mouse.current;
		    if (mouse != null && mouse.leftButton.isPressed)
			    return true;
		    var pen = Pen.current;
		    if (pen != null && pen.tip.isPressed)
			    return true;
		    var touch = Touchscreen.current;
		    if (touch != null && touch.primaryTouch.press.isPressed)
			    return true;
#pragma warning disable CS0618
		    try {
			    if (Input.GetMouseButton(0))
				    return true;
		    } catch { /* ignore */ }
#pragma warning restore CS0618
		    return false;
	    }

	    public void Show( string text,  Action onYes,  Action onNo, string yesText="Yes", string noText="No" ){
		    // Re-entrant Show: drop prior acts silently (do not invoke onNo — that falsely reported Uninstall cancelled).
		    if (_act_onYes != null || _act_onNo != null) {
			    Debug.Log("[ConfirmPopup_UI] Replacing open confirm (prior acts discarded, not cancelled).");
			    _act_onYes = null;
			    _act_onNo = null;
		    }
		    RestoreElevation();
		    ElevateForModalShow();
	        _background_button.gameObject.SetActive(true);
		    EnsureClickableLayout();
	        _header.text = text;
	        _act_onYes = onYes;
	        _act_onNo = onNo;
	        _yesText.text = yesText;
	        _noText.text = noText;
	        _alreadyShownOrHidden = true;
		    _suppressBackgroundDismissUntilPointerUp = true;
		    _suppressBackgroundUntilUnscaled = Time.unscaledTime + SuppressBackgroundMaxSec;
		    if (_yes != null) {
			    _yes.interactable = true;
			    _yes.enabled = true;
			    _yes.transform.SetAsLastSibling();
		    }
		    if (_no != null) {
			    _no.interactable = true;
			    _no.enabled = true;
		    }
		    if (_background_button != null) {
			    _background_button.interactable = true;
			    _background_button.enabled = true;
		    }
	        ApplyThemeTokens();
		    Canvas.ForceUpdateCanvases();
	    }

	    /// <summary>
	    /// Hide without invoking Yes/No; restore Addon Manager sort / popup canvases.
	    /// Used by Addon Manager Close and Exit (never leave an elevated dimmer up).
	    /// </summary>
	    public void AbortAndRestoreUi() {
		    _act_onYes = null;
		    _act_onNo = null;
		    _suppressBackgroundDismissUntilPointerUp = false;
		    _suppressBackgroundUntilUnscaled = -1f;
		    RestoreElevation();
		    if (_background_button != null)
			    _background_button.gameObject.SetActive(false);
	    }

	    /// <summary>
	    /// Scene root is authored scale 0 / zero size (hidden). Nested background is World Space @ 1500,
	    /// which loses to AddonManager Overlay @ 32767. While showing: stretch root, convert canvases to
	    /// Overlay above the manager, and put the dimmer canvas above the shell so Yes/No receive clicks.
	    /// </summary>
	    void ElevateForModalShow() {
		    Canvas mgr = FindActiveAddonManagerCanvas();
		    if (mgr != null && mgr.gameObject.activeInHierarchy) {
			    _parkedManagerCanvas = mgr;
			    _parkedManagerSort = mgr.sortingOrder;
			    mgr.sortingOrder = 100;
		    }

		    var rootRt = transform as RectTransform;
		    if (rootRt != null) {
			    _savedRootRt = CaptureRt(rootRt);
			    _hasSavedRootRt = true;
			    // Authored hide uses scale 0 + zero rect — Overlay needs a real fullscreen root.
			    rootRt.localScale = Vector3.one;
			    rootRt.localRotation = Quaternion.identity;
			    rootRt.anchorMin = Vector2.zero;
			    rootRt.anchorMax = Vector2.one;
			    rootRt.offsetMin = Vector2.zero;
			    rootRt.offsetMax = Vector2.zero;
			    rootRt.pivot = new Vector2(0.5f, 0.5f);
		    }

		    if (_background_button != null) {
			    var bgRt = _background_button.transform as RectTransform;
			    if (bgRt != null) {
				    _savedBackgroundRt = CaptureRt(bgRt);
				    _hasSavedBackgroundRt = true;
				    bgRt.localScale = Vector3.one;
				    bgRt.localRotation = Quaternion.identity;
				    bgRt.anchorMin = Vector2.zero;
				    bgRt.anchorMax = Vector2.one;
				    bgRt.offsetMin = Vector2.zero;
				    bgRt.offsetMax = Vector2.zero;
			    }
		    }

		    var canvases = GetComponentsInChildren<Canvas>(true);
		    _elevatedPopupStates = new CanvasSortState[canvases.Length];
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
			    // Background (dimmer + Window + Yes/No) must sort above the shell canvas.
			    bool isBackgroundCanvas = _background_button != null
				    && c.gameObject == _background_button.gameObject;
			    c.sortingOrder = isBackgroundCanvas
				    ? ConfirmOverlaySortBase + 10
				    : ConfirmOverlaySortBase + i;
			    c.enabled = true;
			    if (c.GetComponent<GraphicRaycaster>() == null)
				    c.gameObject.AddComponent<GraphicRaycaster>();
		    }
		    _elevationActive = true;
	    }

	    void EnsureClickableLayout() {
		    if (_background_button == null) return;
		    var window = _background_button.transform.Find("Window");
		    if (window != null) {
			    window.gameObject.SetActive(true);
			    window.SetAsLastSibling();
			    var wrt = window as RectTransform;
			    if (wrt != null && wrt.localScale.sqrMagnitude < 1e-6f)
				    wrt.localScale = Vector3.one;
		    }
		    if (_yes != null)
			    _yes.transform.SetAsLastSibling();
		    if (_no != null && _yes != null)
			    _no.transform.SetSiblingIndex(Mathf.Max(0, _yes.transform.GetSiblingIndex() - 1));
	    }

	    static RectTransformState CaptureRt(RectTransform rt) {
		    return new RectTransformState {
			    rt = rt,
			    anchorMin = rt.anchorMin,
			    anchorMax = rt.anchorMax,
			    offsetMin = rt.offsetMin,
			    offsetMax = rt.offsetMax,
			    pivot = rt.pivot,
			    localScale = rt.localScale,
			    localRotation = rt.localRotation
		    };
	    }

	    static void RestoreRt(RectTransformState s) {
		    if (s.rt == null) return;
		    s.rt.anchorMin = s.anchorMin;
		    s.rt.anchorMax = s.anchorMax;
		    s.rt.offsetMin = s.offsetMin;
		    s.rt.offsetMax = s.offsetMax;
		    s.rt.pivot = s.pivot;
		    // Never restore authored scale 0 — CanvasScaler + next Show need a usable root.
		    s.rt.localScale = s.localScale.sqrMagnitude < 1e-6f ? Vector3.one : s.localScale;
		    s.rt.localRotation = s.localRotation;
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
		    if (_hasSavedRootRt) {
			    RestoreRt(_savedRootRt);
			    _hasSavedRootRt = false;
		    }
		    if (_hasSavedBackgroundRt) {
			    RestoreRt(_savedBackgroundRt);
			    _hasSavedBackgroundRt = false;
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
		    _suppressBackgroundDismissUntilPointerUp = false;
	        RestoreElevation();
	        act?.Invoke();
	        _background_button.gameObject.SetActive(false);
	    }

	    void OnNoClicked(){
	        Action act = _act_onNo;
	        _act_onYes = null;
	        _act_onNo = null;
		    _suppressBackgroundDismissUntilPointerUp = false;
	        RestoreElevation();
	        act?.Invoke();
	        _background_button.gameObject.SetActive(false);
	    }

	    void OnBackgroundClicked(){
		    if (_suppressBackgroundDismissUntilPointerUp) return;
	        OnNoClicked();
	    }
	}
}//end namespace
