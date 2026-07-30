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

	    Vector3 _choice_originalScale;

	    bool _ishowingChoicePanel;

	    Coroutine _showHidePanel_crtn = null;

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


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this.gameObject); return; }
	        instance = this;
	        var sensor = _3d_choice_button.GetComponentInParent<MouseHoverSensor_UI>();
	        sensor.onSurfaceEnter += p=>OnSurfaceEnter(_3d_choice_button, p);
	        sensor.onSurfaceExit += p=>OnSurfaceExit(_3d_choice_button, p);
	        _3d_choice_button.onClick.AddListener( ()=>OnButtonPressed(_3d_choice_button) );

	        sensor = _sd_choice_button.GetComponentInParent<MouseHoverSensor_UI>();
	        sensor.onSurfaceEnter += p=>OnSurfaceEnter(_sd_choice_button, p);
	        sensor.onSurfaceExit += p=>OnSurfaceExit(_sd_choice_button, p);
	        _sd_choice_button.onClick.AddListener( ()=>OnButtonPressed(_sd_choice_button) );

	        sensor = _uv_choice_button.GetComponentInParent<MouseHoverSensor_UI>();
	        sensor.onSurfaceEnter += p=>OnSurfaceEnter(_uv_choice_button, p);
	        sensor.onSurfaceExit += p=>OnSurfaceExit(_uv_choice_button, p);
	        _uv_choice_button.onClick.AddListener( ()=>OnButtonPressed(_uv_choice_button) );

	        sensor = _bg_choice_button.GetComponentInParent<MouseHoverSensor_UI>(includeInactive:true);
	        sensor.onSurfaceEnter += p=>OnSurfaceEnter(_bg_choice_button, p);
	        sensor.onSurfaceExit += p=>OnSurfaceExit(_bg_choice_button, p);
	        _bg_choice_button.onClick.AddListener( ()=>OnButtonPressed(_bg_choice_button) );
        
	        _choice_originalScale = _3d_choice_button.transform.parent.localScale;
	        _choicesPanel_anim.SetBool("ShowPanel", false);
	    }

	    void Start(){
	        _Act_OnDimensionChanged?.Invoke(_dimensionMode);
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy(){
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        if (instance == this)
	            instance = null;
	    }

	    /// <summary>
	    /// Nomad: flat circle discs + reverse-out labels (light type on dark fill). Restores sphere sprites on builtin.
	    /// MainChoice face is the child <c>Checkmark</c> (glossy pin_top_view), not only the parent Image.
	    /// </summary>
	    void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(transform);
	            ApplyAuthoredSelectionColors();
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        if (_choicesPanel_rectTransf != null) {
	            var panelImg = _choicesPanel_rectTransf.GetComponent<Image>();
	            if (panelImg != null)
	                SpzUiThemeOps.ApplyBoundChromeGraphic(panelImg, t.panelBg);
	            // Prefab "bg" child under choice panel.
	            var bg = _choicesPanel_rectTransf.Find("bg");
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
	    }

	    static void EnsureDimChoiceHitFace(Button btn) {
	        if (btn == null) return;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        if (btn.targetGraphic != null)
	            btn.targetGraphic.raycastTarget = true;
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
	        // Reverse-out: light type on dark disc (not black-on-white sphere).
	        SpzUiThemeOps.ApplyBoundChromeTmp(text, t.textPrimary, basePt);
	    }

	    static void ApplyReverseOutLabelsUnder(Button button, SpzUiThemeOps.ThemeTokens t) {
	        if (button == null) return;
	        foreach (var tmp in button.GetComponentsInChildren<TMP_Text>(true))
	            ApplyReverseOutLabel(tmp, t, 14f);
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
	        string msg = "";
	        if(but == _3d_choice_button){ 
	            _dimensionMode = DimensionMode.dim_gen_3d; _mainChoice_text.text = "3D";
	            msg = "3d Generation Mode";
	        }
	        if(but == _sd_choice_button){ 
	            _dimensionMode = DimensionMode.dim_sd; _mainChoice_text.text = "SD";
	            msg = "Stable Diffusion Texturing Mode";
	        } //t for 'textures'
	        if(but == _uv_choice_button){ 
	            _dimensionMode = DimensionMode.dim_uv; _mainChoice_text.text = "UV";
	            msg = "Inspect Texture Coords Mode"; //don't explain. Self evident and avoids distraction.
	        }
	        if (string.IsNullOrEmpty(msg) == false){
	            Viewport_StatusText.instance.ShowStatusText(msg, false, 3, false);
	        }
	        _mainChoice_anim.Play();
	        if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            ApplyThemeTokens();
	        }
	        else {
	            _3d_choice_button.GetComponent<Image>().color = _inactiveColor;
	            _sd_choice_button.GetComponent<Image>().color = _inactiveColor;
	            _uv_choice_button.GetComponent<Image>().color = _inactiveColor;
	            _bg_choice_button.GetComponent<Image>().color = _inactiveColor;
	            var img = but.GetComponent<Image>();
	            if (img != null)
	                img.color = Color.white;
	        }
	        _Act_OnDimensionChanged?.Invoke(_dimensionMode);
	    }


	    void Update(){
	        if (_ishowingChoicePanel){
	            Vector2 mousePos  = KeyMousePenInput.cursorScreenPos();
	            bool panelHovered = RectTransformUtility.RectangleContainsScreenPoint(_choicesPanel_rectTransf, mousePos);
	            if(_mainChoiceHoverSurf.isHovering==false && !panelHovered){
	                _ishowingChoicePanel = false;
	                _choicesPanel_anim.SetBool("ShowPanel", false);
	                _showHidePanel_crtn = StartCoroutine(ShowHidePanel_crtn(hide: false));
	            }
	            ScaleChoice_ifHovered(_3d_choice_button.transform.parent, _3d_choice_sensor);
	            ScaleChoice_ifHovered(_sd_choice_button.transform.parent, _2d_choice_sensor);
	            ScaleChoice_ifHovered(_uv_choice_button.transform.parent, _uv_choice_sensor);
	            ScaleChoice_ifHovered(_bg_choice_button.transform.parent, _bg_choice_sensor);
	        }
	        else{//not showing, check if should show:
	            if(_mainChoiceHoverSurf.isHovering){
	                if(_showHidePanel_crtn!=null){ StopCoroutine(_showHidePanel_crtn); }
	                _showHidePanel_crtn  = StartCoroutine(ShowHidePanel_crtn(hide:false));
	                _ishowingChoicePanel = true;
	                _choicesPanel_anim.SetBool("ShowPanel", true);
	            }
	        }
	    }


	    IEnumerator ShowHidePanel_crtn(bool hide){
	        _choicesPanel_rectTransf.gameObject.SetActive(true);
	        yield return new WaitForSeconds(0.4f);
	        if (hide){
	            _choicesPanel_rectTransf.gameObject.SetActive(false);
	        }
	        _showHidePanel_crtn = null;
	    }


	    void ScaleChoice_ifHovered(Transform transf, MouseHoverSensor_UI sensor){
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
