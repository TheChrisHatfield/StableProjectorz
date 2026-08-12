using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

namespace spz {

	public enum DimensionMode{
	    dim_uv, // texture-coordinate inspection
	    dim_sd, // stable diffusion texturing
	    dim_gen_3d, // 3d generation (Trellis, etc)
	}

	public class DimensionMode_MGR : MonoBehaviour{
	    public static DimensionMode_MGR instance { get; private set; } = null;

	    [SerializeField] Animation _mainChoice_anim;
	    [SerializeField] MouseHoverSensor_UI _mainChoiceHoverSurf;
	    [SerializeField] TextMeshProUGUI _mainChoice_text;
	    [Space(10)]
	    [SerializeField] Animator _choicesPanel_anim;
	    [SerializeField] RectTransform _choicesPanel_rectTransf;
	    [Space(10)]
	    [SerializeField] float _choiceHover_AnimSpeed = 15;
	    [Space(10)]
	    [SerializeField] Button _3d_choice_button;
	    [SerializeField] MouseHoverSensor_UI _3d_choice_sensor;
	    [Space(10)]
	    [FormerlySerializedAs("_2d_choice_button")][SerializeField] Button _sd_choice_button;
	    [SerializeField] MouseHoverSensor_UI _2d_choice_sensor;
	    [Space(10)]
	    [SerializeField] Button _uv_choice_button;
	    [SerializeField] MouseHoverSensor_UI _uv_choice_sensor;
	    [Space(10)]
	    [SerializeField] Button _bg_choice_button;
	    [SerializeField] MouseHoverSensor_UI _bg_choice_sensor;
	    [Space(10)]
	    [SerializeField] Color _inactiveColor = new Color(0.59f, 0.54f, 0.63f, 1);
	    [SerializeField] Color _activeColor = Color.white;

	    Vector3 _choice_originalScale = Vector3.one;

	    bool _ishowingChoicePanel;

	    Coroutine _showHidePanel_crtn = null;

	    /// <summary>Authored choice-panel scale unused for flip magnitude — Animator owns scale; we only own X sign.</summary>
	    bool _choicesFanFlipped;
	    bool _choicesPanelRaycasterAuthoredIgnoreReversed;
	    bool _capturedChoicesPanelRaycaster;

	    public static Action<DimensionMode> _Act_OnDimensionChanged { get; set; } = null;


	    public DimensionMode _dimensionMode { get; private set; } = DimensionMode.dim_sd;

	    //true if camera is around to fly around the 3D scene, or false if should remain at the same location.
	    public bool is_3d_navigation_allowed => _dimensionMode != DimensionMode.dim_uv;

	    /// <summary>
	    /// Visual SD/3D/UV circle (accounts for Main Choice Holder scale). Used by FULL/SRN dock
	    /// clearance so the Gen Art stack does not climb under this disc.
	    /// </summary>
	    public RectTransform MainChoiceVisualRect {
	        get {
	            if (_mainChoiceHoverSurf != null && _mainChoiceHoverSurf.transform is RectTransform hoverRt)
	                return hoverRt;
	            return transform as RectTransform;
	        }
	    }

	    /// <summary>
	    /// Open SD/3D/UV fan rect for FULL/SRN clearance (includes mirrored fullscreen footprint).
	    /// Show_ChoicePanel hide settles at ~0.35 scale (not 0) — do not treat that as open.
	    /// </summary>
	    public bool TryGetOpenChoicesPanelVisualRect(out RectTransform choicesRt) {
	        choicesRt = null;
	        if (_choicesPanel_rectTransf == null)
	            return false;
	        if (!_choicesPanel_rectTransf.gameObject.activeInHierarchy)
	            return false;
	        float ax = Mathf.Abs(_choicesPanel_rectTransf.localScale.x);
	        // Closed rest ≈0.35; open settles at 1. While dismissing, keep clearance until below mid-scale.
	        if (!_ishowingChoicePanel) {
	            if (ax < 0.55f)
	                return false;
	        } else if (ax < 0.2f) {
	            return false;
	        }
	        choicesRt = _choicesPanel_rectTransf;
	        return true;
	    }


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this.gameObject); return; }
	        instance = this;
	        if (_3d_choice_button != null) {
	            var sensor = _3d_choice_button.GetComponentInParent<MouseHoverSensor_UI>();
	            if (sensor != null) {
	                sensor.onSurfaceEnter += p=>OnSurfaceEnter(_3d_choice_button, p);
	                sensor.onSurfaceExit += p=>OnSurfaceExit(_3d_choice_button, p);
	            }
	            _3d_choice_button.onClick.AddListener( ()=>OnButtonPressed(_3d_choice_button) );
	            if (_3d_choice_button.transform.parent != null)
	                _choice_originalScale = _3d_choice_button.transform.parent.localScale;
	        }
	        if (_sd_choice_button != null) {
	            var sensor = _sd_choice_button.GetComponentInParent<MouseHoverSensor_UI>();
	            if (sensor != null) {
	                sensor.onSurfaceEnter += p=>OnSurfaceEnter(_sd_choice_button, p);
	                sensor.onSurfaceExit += p=>OnSurfaceExit(_sd_choice_button, p);
	            }
	            _sd_choice_button.onClick.AddListener( ()=>OnButtonPressed(_sd_choice_button) );
	        }
	        if (_uv_choice_button != null) {
	            var sensor = _uv_choice_button.GetComponentInParent<MouseHoverSensor_UI>();
	            if (sensor != null) {
	                sensor.onSurfaceEnter += p=>OnSurfaceEnter(_uv_choice_button, p);
	                sensor.onSurfaceExit += p=>OnSurfaceExit(_uv_choice_button, p);
	            }
	            _uv_choice_button.onClick.AddListener( ()=>OnButtonPressed(_uv_choice_button) );
	        }
	        if (_bg_choice_button != null) {
	            var sensor = _bg_choice_button.GetComponentInParent<MouseHoverSensor_UI>(includeInactive:true);
	            if (sensor != null) {
	                sensor.onSurfaceEnter += p=>OnSurfaceEnter(_bg_choice_button, p);
	                sensor.onSurfaceExit += p=>OnSurfaceExit(_bg_choice_button, p);
	            }
	            _bg_choice_button.onClick.AddListener( ()=>OnButtonPressed(_bg_choice_button) );
	        }
	        if (_choicesPanel_anim != null) {
	            _choicesPanel_anim.SetBool("ShowPanel", false);
	            // Prefab scale starts at 1; without a tick FULL/SRN clearance thinks the fan is open.
	            _choicesPanel_anim.Update(0f);
	        }
	        SnapChoicesPanelScaleToClosedRestIfNeeded();
	        // Prefab leaves choice panel active; block hits until hover opens the fan.
	        SetChoicesPanelRaycastsEnabled(false);
	    }

	    void SnapChoicesPanelScaleToClosedRestIfNeeded() {
	        if (_choicesPanel_rectTransf == null || _ishowingChoicePanel)
	            return;
	        Vector3 s = _choicesPanel_rectTransf.localScale;
	        float ax = Mathf.Abs(s.x);
	        // Hide end key is 0.35; if still at authored 1, snap closed so clearance is correct pre-hover.
	        if (ax > 0.55f) {
	            float sign = _choicesFanFlipped ? -1f : 1f;
	            s.x = sign * 0.35f;
	            s.y = 0.35f;
	            s.z = 0.35f;
	            _choicesPanel_rectTransf.localScale = s;
	        }
	    }

	    void Start(){
	        _Act_OnDimensionChanged?.Invoke(_dimensionMode);
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ViewportFullViewOnScreen_Driver.ActiveChanged += OnFullViewActiveChanged;
	        Settings_MGR._Act_verticalRibbonsSwapped += OnVerticalRibbonsSwapped;
	        ApplyThemeTokens();
	        SyncChoicesFanSideForLayout();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        ViewportFullViewOnScreen_Driver.ActiveChanged -= OnFullViewActiveChanged;
	        Settings_MGR._Act_verticalRibbonsSwapped -= OnVerticalRibbonsSwapped;
	        if (_choicesFanFlipped)
	            ApplyChoicesFanFlip(false);
	        if (instance == this)
	            instance = null;
	    }

	    void OnFullViewActiveChanged(bool _) => SyncChoicesFanSideForLayout();
	    void OnVerticalRibbonsSwapped(bool _) => SyncChoicesFanSideForLayout();

	    /// <summary>
	    /// Authored fan opens left into the skeleton SD column. When that column is hidden (FULL SRN /
	    /// OPEN RIGHT) — or when vertical ribbons are swapped onto the opposite edge — mirror the
	    /// choice panel so satellites open the other way instead of covering the mesh.
	    /// </summary>
	    void SyncChoicesFanSideForLayout() {
	        bool leftColumnHidden = ViewportFullViewOnScreen_Driver.ShouldHideMirroredLeftColumnContent();
	        bool ribbonsSwapped = Settings_MGR.instance != null
	            && Settings_MGR.instance.get_viewport_isSwapVerticalRibbons();
	        bool wantFlip = leftColumnHidden || ribbonsSwapped;
	        if (wantFlip == _choicesFanFlipped)
	            return;
	        ApplyChoicesFanFlip(wantFlip);
	    }

	    void ApplyChoicesFanFlip(bool flip) {
	        if (_choicesPanel_rectTransf == null)
	            return;
	        if (flip == _choicesFanFlipped)
	            return;
	        // Do NOT assign absolute localScale here: Show_ChoicePanel.anim drives scale 0.35→1.
	        // LateUpdate EnforceChoicesFanScaleSign keeps X sign after the Animator writes.
	        _choicesFanFlipped = flip;
	        // Prefab GraphicRaycaster has ignoreReversedGraphics=true — with scale.x<0 hits die (SD/3D/UV dead).
	        ApplyChoicesPanelRaycasterForMirror(flip);
	        ApplyChoiceLabelMirrorState(flip);
	        // Same-frame sign — don't wait for LateUpdate (one-frame wrong-side flash on FULL enter).
	        EnforceChoicesFanScaleSign();
	    }

	    /// <summary>
	    /// Animator writes positive scale each frame; re-apply mirror sign without changing magnitude.
	    /// </summary>
	    void EnforceChoicesFanScaleSign() {
	        if (_choicesPanel_rectTransf == null)
	            return;
	        Vector3 s = _choicesPanel_rectTransf.localScale;
	        float ax = Mathf.Abs(s.x);
	        if (ax < 1e-5f)
	            return; // hide anim may zero scale — leave alone
	        float wantX = _choicesFanFlipped ? -ax : ax;
	        if (Mathf.Approximately(s.x, wantX))
	            return;
	        s.x = wantX;
	        _choicesPanel_rectTransf.localScale = s;
	    }

	    void ApplyChoicesPanelRaycasterForMirror(bool flip) {
	        var raycaster = _choicesPanel_rectTransf != null
	            ? _choicesPanel_rectTransf.GetComponent<GraphicRaycaster>()
	            : null;
	        if (raycaster == null)
	            return;
	        if (!_capturedChoicesPanelRaycaster) {
	            _choicesPanelRaycasterAuthoredIgnoreReversed = raycaster.ignoreReversedGraphics;
	            _capturedChoicesPanelRaycaster = true;
	        }
	        raycaster.ignoreReversedGraphics = flip ? false : _choicesPanelRaycasterAuthoredIgnoreReversed;
	    }

	    /// <summary>
	    /// Force readable TMP under a mirrored choice panel. Absolute X sign — safe after theme restore.
	    /// </summary>
	    void ApplyChoiceLabelMirrorState(bool mirrored) {
	        if (_choicesPanel_rectTransf == null)
	            return;
	        foreach (var tmp in _choicesPanel_rectTransf.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null)
	                continue;
	            Vector3 s = tmp.transform.localScale;
	            float ax = Mathf.Abs(s.x) < 1e-4f ? 1f : Mathf.Abs(s.x);
	            s.x = mirrored ? -ax : ax;
	            tmp.transform.localScale = s;
	        }
	    }

	    /// <summary>
	    /// Nomad: flat circle discs + reverse-out labels (light type on dark fill). Restores sphere sprites on builtin.
	    /// MainChoice face is the child <c>Checkmark</c> (glossy pin_top_view), not only the parent Image.
	    /// </summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            if (_choicesPanel_rectTransf != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_choicesPanel_rectTransf);
	            RestoreDimChoice(_3d_choice_button);
	            RestoreDimChoice(_sd_choice_button);
	            RestoreDimChoice(_uv_choice_button);
	            RestoreDimChoice(_bg_choice_button);
	            if (_mainChoiceHoverSurf != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_mainChoiceHoverSurf.transform);
	            if (_mainChoice_text != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_mainChoice_text.transform);
	            ApplyAuthoredSelectionColors();
	            if (_choicesFanFlipped)
	                ApplyChoiceLabelMirrorState(true);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_choicesPanel_rectTransf != null) {
	            var panelImg = _choicesPanel_rectTransf.GetComponent<Image>();
	            if (panelImg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(panelImg, t.panelBg);
	            // Prefab "bg" child under choice panel (may be inactive while fan closed).
	            var bg = SpzUiThemeOps.FindDirectChildIncludingInactive(_choicesPanel_rectTransf, "bg");
	            if (bg != null) {
	                var bgImg = bg.GetComponent<Image>();
	                if (bgImg != null)
	                    SpzUiThemeOps.ApplyBoundChromeGraphic(bgImg, t.panelBg);
	            }
	        }
	        // MainChoice: parent + Checkmark overlay (glossy sphere was the Checkmark Image).
	        Transform mainRoot = _mainChoiceHoverSurf != null
	            ? _mainChoiceHoverSurf.transform
	            : (_mainChoice_text != null ? _mainChoice_text.transform.parent : null);
	        ApplyFlatDiscsUnder(mainRoot, selected: true, t);
	        ApplyFlatDiscsUnder(_3d_choice_button != null ? _3d_choice_button.transform : null,
	            _dimensionMode == DimensionMode.dim_gen_3d, t);
	        ApplyFlatDiscsUnder(_sd_choice_button != null ? _sd_choice_button.transform : null,
	            _dimensionMode == DimensionMode.dim_sd, t);
	        ApplyFlatDiscsUnder(_uv_choice_button != null ? _uv_choice_button.transform : null,
	            _dimensionMode == DimensionMode.dim_uv, t);
	        ApplyFlatDiscsUnder(_bg_choice_button != null ? _bg_choice_button.transform : null, selected: false, t);
	        ApplyReverseOutLabel(_mainChoice_text, t, 22f);
	        if (_mainChoice_text != null)
	            _mainChoice_text.raycastTarget = false;
	        // Main choice opens via hover sensor Graphic — keep that face hittable after TMP clear.
	        if (_mainChoiceHoverSurf != null) {
	            var hoverImg = _mainChoiceHoverSurf.GetComponent<Image>();
	            if (hoverImg != null) {
	                SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(hoverImg);
	                hoverImg.enabled = true;
	                hoverImg.raycastTarget = true;
	            }
	        }
	        ApplyReverseOutLabelsUnder(_3d_choice_button, t);
	        ApplyReverseOutLabelsUnder(_sd_choice_button, t);
	        ApplyReverseOutLabelsUnder(_uv_choice_button, t);
	        ApplyReverseOutLabelsUnder(_bg_choice_button, t);
	        // Labels lose raycasts under BoundChrome; Ensure a hittable face or SD↔3D mode dies (gen path).
	        // ClearNonFace no-ops when targetGraphic is null — must Ensure first (Pass12 litmus).
	        EnsureDimChoiceHitFace(_3d_choice_button);
	        EnsureDimChoiceHitFace(_sd_choice_button);
	        EnsureDimChoiceHitFace(_uv_choice_button);
	        EnsureDimChoiceHitFace(_bg_choice_button);
	        // Flat Checkmark overlays keep authored raycasts — silo hits to each Button face (gen mode litmus).
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_3d_choice_button);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_sd_choice_button);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_uv_choice_button);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_bg_choice_button);
	        // RestoreBoundChrome / Compact labels can reset TMP scale — re-assert readable mirror state.
	        if (_choicesFanFlipped)
	            ApplyChoiceLabelMirrorState(true);
	    }

	    static void EnsureDimChoiceHitFace(Button btn) {
	        if (btn == null) return;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        if (btn.targetGraphic != null)
	            btn.targetGraphic.raycastTarget = true;
	    }

	    static void RestoreDimChoice(Button btn) {
	        if (btn != null)
	            SpzUiThemeOps.RestoreBoundChromeUnder(btn.transform);
	    }

	    static void ApplyFlatDiscsUnder(Transform root, bool selected, SpzUiThemeOps.ThemeTokens t) {
	        if (root == null) return;
	        foreach (var img in root.GetComponentsInChildren<Image>(true)) {
	            if (img == null) continue;
	            // Real Toggle ON glyphs only — MainChoice face is often named Checkmark and must get flat discs.
	            if (SpzUiThemeOps.IsToggleCheckmarkGraphic(img))
	                continue;
	            string n = img.gameObject.name ?? "";
	            if (n == "MonolithLineIcon" || n == "MonolithActiveBar")
	                continue;
	            ApplyFlatDisc(img, selected, t);
	        }
	    }

	    static void ApplyFlatDisc(Image img, bool selected, SpzUiThemeOps.ThemeTokens t) {
	        if (img == null) return;
	        // Mask / radial Filled dials must keep authored sprites (same litmus as ApplyRoundedControlSprite).
	        if (SpzUiThemeOps.IsUiMaskGraphic(img) || img.type == Image.Type.Filled)
	            return;
	        Color fill = selected
	            ? Color.Lerp(t.controlBg, t.accent, 0.22f)
	            : t.controlBg;
	        SpzUiThemeOps.ApplyBoundChromeGraphic(img, fill);
	        var tag = img.GetComponent<SpzUiThemeRoundedControl>();
	        if (tag == null) {
	            tag = img.gameObject.AddComponent<SpzUiThemeRoundedControl>();
	            tag.authoredSprite = img.sprite;
	            tag.authoredType = img.type;
	            tag.authoredPixelsPerUnitMultiplier = img.pixelsPerUnitMultiplier;
	            tag.authoredPreserveAspect = img.preserveAspect;
	            tag.hasAuthoredSnapshot = true;
	        }
	        img.sprite = UiRuntimeSprites.CircleFilled;
	        img.type = Image.Type.Simple;
	        img.preserveAspect = true;
	    }

	    static void ApplyReverseOutLabel(TMP_Text text, SpzUiThemeOps.ThemeTokens t, float basePt = 16f) {
	        if (text == null) return;
	        // Reverse-out light type on dark disc; Compact so SD/3D/UV/BG chips do not spill.
	        SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(text, t.textPrimary, basePt);
	    }

	    static void ApplyReverseOutLabelsUnder(Button button, SpzUiThemeOps.ThemeTokens t) {
	        if (button == null) return;
	        foreach (var tmp in button.GetComponentsInChildren<TMP_Text>(true))
	            ApplyReverseOutLabel(tmp, t, 14f);
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(button);
	    }

	    void ApplyAuthoredSelectionColors() {
	        SetAuthoredButtonColor(_3d_choice_button, _dimensionMode == DimensionMode.dim_gen_3d);
	        SetAuthoredButtonColor(_sd_choice_button, _dimensionMode == DimensionMode.dim_sd);
	        SetAuthoredButtonColor(_uv_choice_button, _dimensionMode == DimensionMode.dim_uv);
	        SetAuthoredButtonColor(_bg_choice_button, false);
	    }

	    void SetAuthoredButtonColor(Button button, bool active) {
	        if (button == null) return;
	        var img = button.GetComponent<Image>();
	        if (img != null)
	            img.color = active ? _activeColor : _inactiveColor;
	    }


	    void OnButtonPressed(Button but){
	        if (but == null)
	            return;
	        string msg = "";
	        if(but == _3d_choice_button){ 
	            _dimensionMode = DimensionMode.dim_gen_3d;
	            if (_mainChoice_text != null) _mainChoice_text.text = "3D";
	            msg = "3d Generation Mode";
	        }
	        if(but == _sd_choice_button){ 
	            _dimensionMode = DimensionMode.dim_sd;
	            if (_mainChoice_text != null) _mainChoice_text.text = "SD";
	            msg = "Stable Diffusion Texturing Mode";
	        } //t for 'textures'
	        if(but == _uv_choice_button){ 
	            _dimensionMode = DimensionMode.dim_uv;
	            if (_mainChoice_text != null) _mainChoice_text.text = "UV";
	            msg = "Inspect Texture Coords Mode"; //don't explain. Self evident and avoids distraction.
	        }
	        if (string.IsNullOrEmpty(msg) == false && Viewport_StatusText.instance != null){
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 3, false);
	        }
	        if (_mainChoice_anim != null)
	            _mainChoice_anim.Play();
	        if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            ApplyThemeTokens();
	        }
	        else {
	            SetAuthoredButtonColor(_3d_choice_button, false);
	            SetAuthoredButtonColor(_sd_choice_button, false);
	            SetAuthoredButtonColor(_uv_choice_button, false);
	            SetAuthoredButtonColor(_bg_choice_button, false);
	            SetAuthoredButtonColor(but, true);
	        }
	        _Act_OnDimensionChanged?.Invoke(_dimensionMode);
	    }


	    void Update(){
	        // Skeleton left hide can change without ActiveChanged (OPEN RIGHT); keep fan side in sync.
	        SyncChoicesFanSideForLayout();
	        if (_choicesPanel_rectTransf == null || _mainChoiceHoverSurf == null)
	            return;
	        if (_ishowingChoicePanel){
	            Vector2 mousePos  = KeyMousePenInput.cursorScreenPos();
	            bool panelHovered = RectTransformUtility.RectangleContainsScreenPoint(_choicesPanel_rectTransf, mousePos);
	            if(_mainChoiceHoverSurf.isHovering==false && !panelHovered){
	                _ishowingChoicePanel = false;
	                if (_choicesPanel_anim != null)
	                    _choicesPanel_anim.SetBool("ShowPanel", false);
	                // Drop hits immediately — close anim still runs for 0.4s before deactivate.
	                SetChoicesPanelRaycastsEnabled(false);
	                if(_showHidePanel_crtn!=null){ StopCoroutine(_showHidePanel_crtn); }
	                _showHidePanel_crtn = StartCoroutine(ShowHidePanel_crtn(hide: true));
	            }
	            ScaleChoice_ifHovered(
	                _3d_choice_button != null ? _3d_choice_button.transform.parent : null, _3d_choice_sensor);
	            ScaleChoice_ifHovered(
	                _sd_choice_button != null ? _sd_choice_button.transform.parent : null, _2d_choice_sensor);
	            ScaleChoice_ifHovered(
	                _uv_choice_button != null ? _uv_choice_button.transform.parent : null, _uv_choice_sensor);
	            ScaleChoice_ifHovered(
	                _bg_choice_button != null ? _bg_choice_button.transform.parent : null, _bg_choice_sensor);
	        }
	        else{//not showing, check if should show:
	            if(_mainChoiceHoverSurf.isHovering){
	                if(_showHidePanel_crtn!=null){ StopCoroutine(_showHidePanel_crtn); }
	                _showHidePanel_crtn  = StartCoroutine(ShowHidePanel_crtn(hide:false));
	                _ishowingChoicePanel = true;
	                if (_choicesPanel_anim != null)
	                    _choicesPanel_anim.SetBool("ShowPanel", true);
	                SetChoicesPanelRaycastsEnabled(true);
	            }
	        }
	    }

	    void LateUpdate() {
	        // After Animator writes Show_ChoicePanel scale (positive), keep fullscreen mirror sign.
	        EnforceChoicesFanScaleSign();
	    }


	    void SetChoicesPanelRaycastsEnabled(bool enabled) {
	        if (_choicesPanel_rectTransf == null)
	            return;
	        var raycaster = _choicesPanel_rectTransf.GetComponent<GraphicRaycaster>();
	        if (raycaster == null)
	            return;
	        raycaster.enabled = enabled;
	        // Re-assert ignoreReversed after enable — flip may have been applied while disabled.
	        if (enabled)
	            ApplyChoicesPanelRaycasterForMirror(_choicesFanFlipped);
	    }

	    IEnumerator ShowHidePanel_crtn(bool hide){
	        if (_choicesPanel_rectTransf == null) {
	            _showHidePanel_crtn = null;
	            yield break;
	        }
	        _choicesPanel_rectTransf.gameObject.SetActive(true);
	        // Unscaled — timescale 0 (pause/modal) must not strand the panel active forever.
	        yield return new WaitForSecondsRealtime(0.4f);
	        if (hide && _choicesPanel_rectTransf != null){
	            _choicesPanel_rectTransf.gameObject.SetActive(false);
	        }
	        _showHidePanel_crtn = null;
	    }


	    void ScaleChoice_ifHovered(Transform transf, MouseHoverSensor_UI sensor){
	        if (transf == null || sensor == null)
	            return;
	        Vector3 targScale = sensor.isHovering ? _choice_originalScale*1.25f : _choice_originalScale;
	        float factor = Time.deltaTime * _choiceHover_AnimSpeed;
	        transf.localScale =  Vector3.Lerp(transf.localScale, targScale, factor);
	    }

	    void OnSurfaceEnter(Button but, PointerEventData p){

	    }

	    void OnSurfaceExit(Button but, PointerEventData p){

	    }
	}
}//end namespace
