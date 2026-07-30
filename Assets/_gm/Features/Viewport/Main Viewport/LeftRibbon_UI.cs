using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	//has tab-buttons that allow us to flick between different panels and preview regimes.
	public class LeftRibbon_UI : MonoBehaviour {
	    public static LeftRibbon_UI instance { get; private set; } = null;

	    [SerializeField] ButtonToggle_UI _toggleWireframe;
	    [SerializeField] Toggle _toggleDepthMode_button;
	    [SerializeField] CircleSlider_Snapping_UI _depthContrast_slider;
	    [SerializeField] CircleSlider_Snapping_UI _depthBrightness_slider;
	    [SerializeField] CircleSlider_Snapping_UI _depthBlur_StepSize_slider;
	    [SerializeField] CircleSlider_Snapping_UI _depthSharpBlur_slider;
	    [SerializeField] CircleSlider_Snapping_UI _depthBlurFinal_StepSize_slider;
	    [SerializeField] Toggle _depthFinalBlur_Inside_toggle;
	    [SerializeField] TextMeshProUGUI _depthContrast_text;
	    [SerializeField] TextMeshProUGUI _depthBrightness_text;
	    [SerializeField] TextMeshProUGUI _depthBlur_stepSize_text;
	    [SerializeField] TextMeshProUGUI _depthBlurFinal_stepSize_text;
	    [SerializeField] TextMeshProUGUI _depthSmartBlur_text;
	    [Space(10)]
	    [SerializeField] SlideOut_Widget_UI _depth_slideOut_panel;
	    [SerializeField] GameObject _depthSlideOut_antiClick_surf;
	    Color _themeAccent = Color.white;
	    bool _lastNomadChrome;
	    bool _lastDepthModeOn;
	    bool _lastFinalBlurInsideOn;
	    bool _lastWireframePressed;

	    public bool isShowWireframe_onSelected => _toggleWireframe.isPressed;
	    public float depthContrast => _depthContrast_slider.value;
	    public float depthBrightness => _depthBrightness_slider.value;
	    public float depthBlur_StepSize => _depthBlur_StepSize_slider.value;
	    public float depthSharpBlur => _depthSharpBlur_slider.value;
	    public float depthBlurFinal_StepSize => _depthBlurFinal_StepSize_slider.value;
	    public bool depthFinalBlur_Inside => _depthFinalBlur_Inside_toggle.isOn;

    
	    public void SetDepthContrast01_fromCode(float value01){
	        bool invokeCallback =  Mathf.Approximately(_depthContrast_slider.value, value01) == false;
	        _depthContrast_slider.SetSliderValue(value01, invokeCallback);
	    }

	    public void SetDepthBrightness01_fromCode(float value01){
	        bool invokeCallback =  Mathf.Approximately(_depthBrightness_slider.value, value01) == false;
	        _depthBrightness_slider.SetSliderValue(value01, invokeCallback);
	    }


	    void Update(){
	        UdpateDepthSliderText();
	        SyncNomadChromeSelectionIfChanged();

	        // COMMENTED OUT, KEPT FOR PRECAUTION. Allow user to do it from anywhere, without hovering the viewport:
	        //    if(MainViewport_UI.instance.isCursorHoveringMe() == false){ return; }
	        if (KeyMousePenInput.isSomeInputFieldActive()){ return; }//maybe typing text, etc

	        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.W)){
	            _toggleWireframe.ForceSameValueAs(!_toggleWireframe.isPressed);
	        }
	    }


	    void UdpateDepthSliderText(){
	        _depthContrast_text.text       = Mathf.RoundToInt(_depthContrast_slider.value*100).ToString();
	        _depthBrightness_text.text     = Mathf.RoundToInt(_depthBrightness_slider.value*100).ToString();
	        _depthBlur_stepSize_text.text  =  _depthBlur_StepSize_slider.value.ToString("0.0");
	        _depthBlurFinal_stepSize_text.text  = _depthBlurFinal_StepSize_slider.value.ToString("0.0");
        
	        _depthSmartBlur_text.text = _depthSharpBlur_slider.value.ToString("0.0");
	    }


	    int _numWarningsSoFar = 1;
	    float _nextWarnTime = -9999;
	    void OnDepthStepSlider(float val){
	        //produce performance warning but only for high resolutions:
	        if(SD_InputPanel_UI.instance.widthHeight().x <= 1024){ return; }
	        if(SD_InputPanel_UI.instance.widthHeight().y <= 1024){ return; }
	        if (Time.time < _nextWarnTime){ return; }
	        Viewport_StatusText.instance.ShowStatusText("Keeping blur as 0 might save performance.", false, 5, false);
	        _nextWarnTime = Time.time + 20*_numWarningsSoFar;
	        _numWarningsSoFar++;
	    }

	    // User toggled a setting to change the arrangement of the UI panels.
	    // We need to adjust the slide-outs, so that they still make sense:
	    void OnSettings_ToolRibbonSwapped(bool isSwapped){
	        if (_depth_slideOut_panel == null) return;
	        if(_depth_slideOut_panel.isFlipped() == isSwapped){ return; }
	        _depth_slideOut_panel.Flip_if_possible();
	    }


	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;

	        EarlyUpdate_callbacks_MGR.instance.onEarlyUpdate3 += EarlyUpdate;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	    }

	    void Start(){
	        _toggleDepthMode_button.onValueChanged.AddListener( OnToggleDepthMode_button );
	        _depthBlur_StepSize_slider.onValueChanged.AddListener( OnDepthStepSlider );
	        Settings_MGR._Act_verticalRibbonsSwapped += OnSettings_ToolRibbonSwapped;
	        OnSettings_ToolRibbonSwapped( Settings_MGR.instance.get_viewport_isSwapVerticalRibbons() );
	        ApplyThemeTokens();
	    }


	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        if (EarlyUpdate_callbacks_MGR.instance != null){
	            EarlyUpdate_callbacks_MGR.instance.onEarlyUpdate3 -= EarlyUpdate;
	        }
	        Settings_MGR._Act_verticalRibbonsSwapped -= OnSettings_ToolRibbonSwapped;
	        if (instance == this)
	            instance = null;
	    }

	    /// <summary>Themes known prefab-owned left-ribbon controls + the Depth Options slide-out menu.</summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            // Leaving BoundChrome: restore ribbon AND Depth menu (menu is not under this transform).
	            RestoreLeftRibbonAuthoredChrome();
	            RestoreDepthOptionsMenuChrome();
	            SpzUiThemeOps.RefreshScaledLayoutGroupsUnder(transform);
	            if (_depth_slideOut_panel != null)
	                SpzUiThemeOps.RefreshScaledLayoutGroupsUnder(_depth_slideOut_panel.transform);
	            SnapshotNomadChromeSelection();
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        _themeAccent = t.accent;
	        ThemeToggle(_toggleDepthMode_button, t);
	        ThemeToggle(_depthFinalBlur_Inside_toggle, t);
	        ThemeTmp(_depthContrast_text, t.textPrimary);
	        ThemeTmp(_depthBrightness_text, t.textPrimary);
	        ThemeTmp(_depthBlur_stepSize_text, t.textPrimary);
	        ThemeTmp(_depthBlurFinal_stepSize_text, t.textPrimary);
	        ThemeTmp(_depthSmartBlur_text, t.textPrimary);
	        ThemeWireframe(t);
	        ThemeCircleSlider(_depthContrast_slider, t);
	        ThemeCircleSlider(_depthBrightness_slider, t);
	        ThemeCircleSlider(_depthBlur_StepSize_slider, t);
	        ThemeCircleSlider(_depthSharpBlur_slider, t);
	        ThemeCircleSlider(_depthBlurFinal_StepSize_slider, t);
	        ThemeDepthOptionsMenu(t);
	        SnapshotNomadChromeSelection();
	    }

	    void RestoreLeftRibbonAuthoredChrome() {
	        SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	        if (_toggleWireframe != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(_toggleWireframe.transform);
	        // Hide Monolith bars created for Nomad selection chrome.
	        foreach (Transform t in GetComponentsInChildren<Transform>(true)) {
	            if (t != null && t.name == "MonolithActiveBar")
	                t.gameObject.SetActive(false);
	        }
	    }

	    /// <summary>
	    /// Depth Options slide-out is wired by reference but lives under the viewport hierarchy —
	    /// not under <see cref="LeftRibbon_UI"/> — so ribbon-only Restore leaves Nomad dials/labels stuck.
	    /// </summary>
	    void RestoreDepthOptionsMenuChrome() {
	        if (_depth_slideOut_panel != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(_depth_slideOut_panel.transform);
	        // Explicit dial leave even if panel ref is missing / hierarchy remapped.
	        LeaveCircleSlider(_depthContrast_slider);
	        LeaveCircleSlider(_depthBrightness_slider);
	        LeaveCircleSlider(_depthBlur_StepSize_slider);
	        LeaveCircleSlider(_depthSharpBlur_slider);
	        LeaveCircleSlider(_depthBlurFinal_StepSize_slider);
	    }

	    static void LeaveCircleSlider(CircleSlider_Snapping_UI slider) {
	        if (slider == null) return;
	        // ApplyThemeTokens self-silos when !ShouldRecolorBoundChrome.
	        slider.ApplyThemeTokens(Color.white, Color.white);
	    }

	    /// <summary>
	    /// Theme Depth Options panel shell + headers on every BoundChrome apply (theme→theme token refresh).
	    /// Skips dial Images/TMP — those go through <see cref="ThemeCircleSlider"/>.
	    /// </summary>
	    void ThemeDepthOptionsMenu(SpzUiThemeOps.ThemeTokens t) {
	        if (_depth_slideOut_panel == null) return;
	        Transform root = _depth_slideOut_panel.transform;
	        Color shell = SpzUiThemeOps.ResolvePanelShellColor();
	        foreach (var img in root.GetComponentsInChildren<Image>(true)) {
	            if (img == null) continue;
	            if (img.GetComponentInParent<CircleSlider_Snapping_UI>(true) != null) continue;
	            if (SpzUiThemeOps.IsUiMaskGraphic(img)) continue;
	            if (img.type == Image.Type.Filled) continue;
	            if (SpzUiThemeOps.IsToggleCheckmarkGraphic(img)) continue;
	            string n = img.gameObject.name ?? "";
	            if (n.IndexOf("background", System.StringComparison.OrdinalIgnoreCase) < 0)
	                continue;
	            SpzUiThemeOps.ApplyBoundChromeGraphic(img, shell);
	            SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	        }
	        foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            if (tmp.GetComponentInParent<CircleSlider_Snapping_UI>(true) != null) continue;
	            // Blur-inside toggle label is handled by ThemeToggle strip metrics.
	            if (tmp.GetComponentInParent<Toggle>(true) != null) continue;
	            if (tmp.GetComponentInParent<Button>(true) != null)
	                SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	            else {
	                SpzUiThemeOps.ApplyBoundChromeTmp(tmp, t.textPrimary);
	                tmp.characterSpacing = 0f;
	            }
	        }
	        foreach (var btn in root.GetComponentsInChildren<Button>(true)) {
	            if (btn == null) continue;
	            // Compact labels clear TMP hits — without a wired face, Depth Options buttons go dead under Nomad.
	            SpzUiThemeOps.EnsureSelectableHitFace(btn);
	            if (btn.targetGraphic != null && btn.targetGraphic.color.a >= 0.08f)
	                SpzUiThemeOps.ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	        }
	        foreach (var lg in root.GetComponentsInChildren<LayoutGroup>(true))
	            SpzUiThemeOps.ApplyScaledLayoutGroup(lg);
	    }

	    /// <summary>
	    /// Selection can change without ThemeChanged; only resync chrome when Nomad is active or when leaving it.
	    /// Avoids SetAsLastSibling / full retheme every frame.
	    /// </summary>
	    void SyncNomadChromeSelectionIfChanged() {
	        bool bound = SpzUiThemeOps.ShouldRecolorBoundChrome;
	        if (!bound) {
	            if (_lastNomadChrome) {
	                // Full retheme restores fill colors + hides bars. Hiding bars alone left gold
	                // selectable fills if ThemeChanged aborted before SnapshotNomadChromeSelection.
	                ApplyThemeTokens();
	            }
	            return;
	        }

	        bool depthOn = _toggleDepthMode_button != null && _toggleDepthMode_button.isOn;
	        bool blurOn = _depthFinalBlur_Inside_toggle != null && _depthFinalBlur_Inside_toggle.isOn;
	        bool wireOn = _toggleWireframe != null && _toggleWireframe.isPressed;
	        bool selectionChanged = !_lastNomadChrome
	            || depthOn != _lastDepthModeOn
	            || blurOn != _lastFinalBlurInsideOn
	            || wireOn != _lastWireframePressed;
	        if (!selectionChanged)
	            return;

	        var t = SpzUiThemeOps.Active;
	        _themeAccent = t.accent;
	        if (depthOn != _lastDepthModeOn || !_lastNomadChrome)
	            ThemeToggle(_toggleDepthMode_button, t);
	        if (blurOn != _lastFinalBlurInsideOn || !_lastNomadChrome)
	            ThemeToggle(_depthFinalBlur_Inside_toggle, t);
	        if (wireOn != _lastWireframePressed || !_lastNomadChrome)
	            ThemeWireframe(t);
	        SnapshotNomadChromeSelection();
	    }

	    void SnapshotNomadChromeSelection() {
	        _lastNomadChrome = SpzUiThemeOps.ShouldRecolorBoundChrome;
	        _lastDepthModeOn = _toggleDepthMode_button != null && _toggleDepthMode_button.isOn;
	        _lastFinalBlurInsideOn = _depthFinalBlur_Inside_toggle != null && _depthFinalBlur_Inside_toggle.isOn;
	        _lastWireframePressed = _toggleWireframe != null && _toggleWireframe.isPressed;
	    }

	    void ThemeWireframe(SpzUiThemeOps.ThemeTokens t) {
	        if (_toggleWireframe == null) return;
	        Color wireNormal = FlatToolFill(_toggleWireframe.isPressed, t);
	        var btn = _toggleWireframe.GetComponent<Button>();
	        if (btn != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(btn);
	            SpzUiThemeOps.ApplyBoundChromeSelectable(btn, wireNormal, t.accent);
	            ApplyFlatToolColorBlock(btn);
	            if (btn.targetGraphic is Image btnImg) {
	                SpzUiThemeOps.ApplyRoundedControlSprite(btnImg, markEligible: true);
	                SpzUiThemeOps.FlattenToolFaceImage(btnImg);
	            }
	        }
	        else {
	            var img = _toggleWireframe.GetComponent<Image>();
	            if (img != null) {
	                SpzUiThemeOps.ApplyBoundChromeGraphic(img, wireNormal);
	                SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
	                SpzUiThemeOps.FlattenToolFaceImage(img);
	            }
	        }
	        SpzUiThemeOps.ApplyControlLineIcon(_toggleWireframe.transform, StudioLineIcon.Wireframe, 20f);
	        ApplyActiveBar(_toggleWireframe.transform, _toggleWireframe.isPressed, t.accent);
	        if (btn != null)
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    /// <summary>
	    /// Flat tool cell: dark fill always (no chrome/gold plate). Selected = subtle accent mix + side bar.
	    /// </summary>
	    static Color FlatToolFill(bool selected, SpzUiThemeOps.ThemeTokens t) {
	        return selected
	            ? Color.Lerp(t.controlBg, t.accent, 0.14f)
	            : t.controlBg;
	    }

	    /// <summary>
	    /// DEP / inside: flat face fills the slot (no gold bevel plate, no background peeking).
	    /// Selection = FlatToolFill + thin accent bar — never gold checkmark overlay.
	    /// </summary>
	    static void ThemeToggle(Toggle toggle, SpzUiThemeOps.ThemeTokens t) {
	        if (toggle == null) return;
	        Color normal = FlatToolFill(toggle.isOn, t);
	        SpzUiThemeOps.EnsureSelectableHitFace(toggle);
	        SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, normal, t.accent);
	        ApplyFlatToolColorBlock(toggle);
	        if (toggle.targetGraphic is Image bg) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(bg, markEligible: true);
	            SpzUiThemeOps.FlattenToolFaceImage(bg);
	        }
	        // Authored checkmark is the gold beveled plate when ON — hide it under Nomad.
	        if (toggle.graphic is Image check && check != toggle.targetGraphic)
	            SpzUiThemeOps.HideAuthoredGraphicForTheme(check);
	        foreach (var img in toggle.GetComponentsInChildren<Image>(true)) {
	            if (img == null || img == toggle.targetGraphic) continue;
	            string n = img.gameObject.name ?? "";
	            if (n == "MonolithActiveBar" || n == "MonolithLineIcon") continue;
	            if (n.Equals("Checkmark", System.StringComparison.OrdinalIgnoreCase)
	                || n.IndexOf("pressed", System.StringComparison.OrdinalIgnoreCase) >= 0
	                || n.Equals("tick", System.StringComparison.OrdinalIgnoreCase))
	                SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
	        }
	        foreach (var tmp in toggle.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            // DEP / INSIDE: strip tracking (18) wraps past the flat selected face (Soft litmus).
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	        }
	        ApplyActiveBar(toggle.transform, toggle.isOn, t.accent);
	        // Never mass-clear when targetGraphic is null (CommandRibbon / SAVE litmus).
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);
	    }

	    /// <summary>ColorTint must not gold-multiply the face when selected (reads as beveled chrome).</summary>
	    static void ApplyFlatToolColorBlock(Selectable sel) {
	        if (sel == null) return;
	        var cb = sel.colors;
	        cb.normalColor = Color.white;
	        cb.highlightedColor = Color.white;
	        cb.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
	        cb.selectedColor = Color.white;
	        cb.disabledColor = new Color(1f, 1f, 1f, 0.4f);
	        cb.colorMultiplier = 1f;
	        sel.colors = cb;
	    }

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
	        // Only reorder on create — SetAsLastSibling every frame fights sibling layout/clicks.
	        if (created)
	            bar.SetAsLastSibling();
	    }

	    static void ThemeTmp(TextMeshProUGUI tmp, Color color) {
	        if (tmp == null) return;
	        // Depth numerals — DialValue + no raycast so overflow cannot steal dial hits (CN/gen path).
	        SpzUiThemeOps.ApplyBoundChromeDialValueTmp(tmp, color, 14f);
	        tmp.raycastTarget = false;
	    }

	    static void ThemeCircleSlider(CircleSlider_Snapping_UI slider, SpzUiThemeOps.ThemeTokens t) {
	        if (slider == null) return;
	        // Ownership-root apply: fill + value text only (never retint dial scaffolding Images).
	        slider.ApplyThemeTokens(t.accent, t.textPrimary);
	    }


	    void EarlyUpdate(){
	        if (_toggleDepthMode_button == null || _depth_slideOut_panel == null) return;
	        Vector2 cursorPos = KeyMousePenInput.cursorScreenPos();
	        RectTransform depthRect = _toggleDepthMode_button.transform as RectTransform;

	        //keep showing the slide out panel if we are viewing the depth:
	        bool isShowingDepth = MainViewport_UI.instance.showing == MainViewport_UI.Showing.Depth;
	        _depth_slideOut_panel._dontAutoHide = isShowingDepth;
	        if (_depthSlideOut_antiClick_surf != null)
	            _depthSlideOut_antiClick_surf.SetActive(!isShowingDepth); //else overlaps controlnet/Art panel.

	        bool contains =  RectTransformUtility.RectangleContainsScreenPoint(depthRect, cursorPos);
	             contains |= isShowingDepth;

	        if (contains  && _depth_slideOut_panel.isShowing == false){ 
	            _depth_slideOut_panel.Toggle_if_Different(true); 
	        }
	    }


	    void OnToggleDepthMode_button(bool isOn){
	        MainViewport_UI.instance.ToggleShowDepth(isOn);
	    }

    
	    public void Save( StableProjectorz_SL spz ){
	        var trSL = new MainViewWindow_ToolsRibbon_SL();
	         spz.mainViewWindow_ToolsRibbon = trSL;
	        trSL.isShowWireframe  = isShowWireframe_onSelected;
	        trSL.depthContrast = _depthContrast_slider.value;
	        trSL.depthBrightness = _depthBrightness_slider.value;
	        trSL.depthBlur_stepSize = depthBlur_StepSize;
	        trSL.depthBlurFinal_stepSize = depthBlurFinal_StepSize;
	        trSL.depth_sharpBlur  = depthSharpBlur;
	        trSL.depth_finalBlur_inside = depthFinalBlur_Inside;
	    }

	    public void Load( StableProjectorz_SL spz ){
	        MainViewWindow_ToolsRibbon_SL trSL = spz.mainViewWindow_ToolsRibbon;
	        if (trSL != null) {
	            _toggleWireframe.ForceSameValueAs( trSL.isShowWireframe );

	            _depthContrast_slider.SetSliderValue( trSL.depthContrast, true);
	            _depthBrightness_slider.SetSliderValue( trSL.depthBrightness, true);

	            _depthBlur_StepSize_slider.SetSliderValue(trSL.depthBlur_stepSize, true);
	            _depthSharpBlur_slider.SetSliderValue( trSL.depth_sharpBlur, true );

	            _depthBlurFinal_StepSize_slider.SetSliderValue(trSL.depthBlurFinal_stepSize, true);
	            _depthFinalBlur_Inside_toggle.isOn = trSL.depth_finalBlur_inside;
	        }

	        // Project load must return to textured viewport. Nomad DEP chrome + FileBrowser click-through
	        // can leave "Black Background (SD Depth)" on — reads as black silhouette / "backwards" mesh.
	        EnsureDepthPreviewOff();
	        if (isActiveAndEnabled)
	            StartCoroutine(CoEnsureDepthPreviewOffNextFrame());
	    }

	    /// <summary>Force DEP off and UsualView (safe if already off).</summary>
	    public void EnsureDepthPreviewOff() {
	        if (_toggleDepthMode_button != null)
	            _toggleDepthMode_button.SetIsOnWithoutNotify(false);
	        if (MainViewport_UI.instance != null)
	            MainViewport_UI.instance.ToggleShowDepth(false);
	        SnapshotNomadChromeSelection();
	    }

	    IEnumerator CoEnsureDepthPreviewOffNextFrame() {
	        yield return null;
	        EnsureDepthPreviewOff();
	    }

	}
}//end namespace
