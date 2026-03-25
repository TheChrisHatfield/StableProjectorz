using TMPro;
using UnityEngine;

namespace spz {

	/// <summary> Per-stamp viewport jitter (spray). </summary>
	public enum BrushScatterMode { None = 0, Light = 1, Medium = 2 }

	/// <summary> How brush tip rotation combines with stroke direction. </summary>
	public enum BrushTipAngleMode { FixedAngle = 0, FollowStroke = 1 }

	/// <summary> World-space mirror plane for mesh symmetry (paint / projections). </summary>
	public enum PaintSymmetryPlaneSource {
		/// <summary> Plane through selection bounds center, normal = average mesh +X (transform.right). </summary>
		Auto = 0,
		/// <summary> Vertical plane through bounds center, normal = view camera +X (screen “left/right” in world). </summary>
		ViewAligned = 1,
		/// <summary> Plane from a picked surface (hit point + hit normal). </summary>
		FacePick = 2,
	}

	/// <summary>
	/// Single source of truth for brush size and spacing. One instance in the app; all painters and UI read/write here
	/// so changes in the Paint tab (or anywhere) persist everywhere until the user changes them again.
	/// </summary>
	public class BrushRibbon_UI_Size : MonoBehaviour{

	    public static BrushRibbon_UI_Size instance { get; private set; }

	    [SerializeField] CircleSlider_Snapping_UI _maskBrushSize_slider;
	    [SerializeField] TextMeshProUGUI _brushSize_text;
	    [Tooltip("Optional. Shows current brush spacing (e.g. 'Continuous' or 'Spacing: 25%').")]
	    [SerializeField] TextMeshProUGUI _brushSpacing_text;

	    float _bracket_BrushSize_nextTime = -99999;
	    public float brushSize01 => _maskBrushSize_slider != null ? _maskBrushSize_slider.value : 0f;

	    /// <summary> App-wide read: current brush size 0–1. Use this instead of any ribbon reference so state is universal. </summary>
	    public static float GetBrushSize01() => instance != null ? instance.brushSize01 : 0f;
	    /// <summary> App-wide read: current brush spacing 0–1 (0 = continuous). Use this so state is universal. </summary>
	    public static float GetBrushSpacing01() => instance != null ? instance.brushSpacing01 : 0f;
	    /// <summary> App-wide read: brush angle in degrees (0–360). </summary>
	    public static float GetBrushAngleDeg() => instance != null ? instance.brushAngleDeg : 0f;
	    /// <summary> App-wide read: brush roundness 0–1 (1 = circle). </summary>
	    public static float GetBrushRoundness01() => instance != null ? instance.brushRoundness01 : 1f;
	    /// <summary> Mirror stroke across vertical center of viewport (screen x = 0.5). </summary>
	    public static bool GetPaintSymmetryXOn() => instance != null && instance._paintSymmetryXOn;

	    /// <summary> Multiplier for Random.insideUnitCircle jitter in viewport UV, scaled by brush diameter (0 = off). </summary>
	    public static float GetBrushScatterJitterMul()
	    {
		    if (instance == null) return 0f;
		    switch (instance._scatterMode)
		    {
			    case BrushScatterMode.Light: return 0.14f;
			    case BrushScatterMode.Medium: return 0.32f;
			    default: return 0f;
		    }
	    }

	    /// <summary> When true, stroke direction (previous→current viewport pos) is added to the brush angle each frame. </summary>
	    public static bool GetBrushTipFollowsStroke()
		    => instance != null && instance._tipAngleMode == BrushTipAngleMode.FollowStroke;

	    /// <summary> Fired when brush size (or slider value) changes so brush preset UI / "brush eyes" can stay in sync with the actual brush system. </summary>
	    public static event System.Action OnBrushSizeChanged;
	    /// <summary> Fired when brush spacing/angle/roundness change so brush preset UI stays in sync. </summary>
	    public static event System.Action OnBrushSettingsChanged;

	    /// <summary> Brush spacing 0–1 (0 = continuous). 1 = 100% (one stamp per diameter). Set from ABR when selecting a brush. </summary>
	    float _brushSpacing01;
	    public float brushSpacing01 => _brushSpacing01;
	    public void SetBrushSpacing(float s)
	    {
	        _brushSpacing01 = Mathf.Clamp01(s);
	        RefreshSpacingText();
	        OnBrushSettingsChanged?.Invoke();
	    }

	    /// <summary> Brush angle in degrees (0–360). Set from ABR when selecting a brush; used for directional alphas. </summary>
	    float _brushAngleDeg;
	    public float brushAngleDeg => _brushAngleDeg;
	    public void SetBrushAngle(float deg)
	    {
	        _brushAngleDeg = Mathf.Repeat(deg, 360f);
	        OnBrushSettingsChanged?.Invoke();
	    }

	    /// <summary> Brush roundness 0–1 (1 = circle). Set from ABR when selecting a brush; used for elliptical tips. </summary>
	    float _brushRoundness01;
	    public float brushRoundness01 => _brushRoundness01;
	    public void SetBrushRoundness(float r)
	    {
	        _brushRoundness01 = Mathf.Clamp01(r);
	        OnBrushSettingsChanged?.Invoke();
	    }

	    /// <summary> Vertical-axis symmetry: duplicate brush at mirrored screen x (1−x). </summary>
	    bool _paintSymmetryXOn;
	    public bool paintSymmetryXOn => _paintSymmetryXOn;
	    public void SetPaintSymmetryXOn(bool on)
	    {
		    _paintSymmetryXOn = on;
		    OnBrushSettingsChanged?.Invoke();
	    }

	    BrushScatterMode _scatterMode = BrushScatterMode.None;
	    public BrushScatterMode scatterMode => _scatterMode;
	    public void SetScatterMode(BrushScatterMode mode)
	    {
		    _scatterMode = mode;
		    OnBrushSettingsChanged?.Invoke();
	    }

	    BrushTipAngleMode _tipAngleMode = BrushTipAngleMode.FixedAngle;
	    public BrushTipAngleMode tipAngleMode => _tipAngleMode;
	    public void SetTipAngleMode(BrushTipAngleMode mode)
	    {
		    _tipAngleMode = mode;
		    OnBrushSettingsChanged?.Invoke();
	    }

	    PaintSymmetryPlaneSource _paintSymmetryPlaneSource = PaintSymmetryPlaneSource.Auto;
	    public PaintSymmetryPlaneSource paintSymmetryPlaneSource => _paintSymmetryPlaneSource;
	    Vector3 _symmetryPlanePointWorld;
	    Vector3 _symmetryPlaneNormalWorld = Vector3.right;
	    public Vector3 symmetryPlanePointWorld => _symmetryPlanePointWorld;
	    public Vector3 symmetryPlaneNormalWorld => _symmetryPlaneNormalWorld;

	    public void SetPaintSymmetryPlaneSource(PaintSymmetryPlaneSource src)
	    {
		    _paintSymmetryPlaneSource = src;
		    OnBrushSettingsChanged?.Invoke();
	    }

	    /// <summary> Use struck face as mirror plane (normal from mesh; flip if mirror side is inverted). </summary>
	    public void ApplySymmetryPlaneFromFaceHit(RaycastHit hit)
	    {
		    _paintSymmetryPlaneSource = PaintSymmetryPlaneSource.FacePick;
		    _symmetryPlanePointWorld = hit.point;
		    _symmetryPlaneNormalWorld = hit.normal.sqrMagnitude > 1e-12f ? hit.normal.normalized : Vector3.up;
		    OnBrushSettingsChanged?.Invoke();
	    }

	    public void FlipPickedSymmetryPlaneNormal()
	    {
		    if (_paintSymmetryPlaneSource != PaintSymmetryPlaneSource.FacePick) return;
		    _symmetryPlaneNormalWorld = -_symmetryPlaneNormalWorld;
		    OnBrushSettingsChanged?.Invoke();
	    }

	    void RefreshSpacingText()
	    {
	        if (_brushSpacing_text == null) return;
	        _brushSpacing_text.text = _brushSpacing01 <= 0f ? "Continuous" : "Spacing: " + Mathf.RoundToInt(_brushSpacing01 * 100f) + "%";
	    }

	    public void SetBrushSize(float s)
	    {
	        if (_maskBrushSize_slider != null)
	        {
	            _maskBrushSize_slider.SetSliderValue(s, true);
	            OnBrushSizeChanged?.Invoke();
	        }
	    }


	    void OnBrushSize_sliderPressed()
	        => Viewport_StatusText.instance.ShowStatusText("Shift + RightMouseDrag to change size easier :)", false, 2, false);

	    void OnBrushSize_slider(float size)
	    {
	        _brushSize_text.text = Mathf.RoundToInt(size * 100).ToString();
	        OnBrushSizeChanged?.Invoke();
	    }


	    /// <summary>True when viewport or the brush size slider area is hovered (mouse or pen), so bracket keys work for tablet users focused on the slider.</summary>
	    bool IsViewportOrBrushSizeControlHovered()
	    {
		    if (MainViewport_UI.instance != null && MainViewport_UI.instance.isCursorHoveringMe()) return true;
		    if (_maskBrushSize_slider == null) return false;
		    var rt = _maskBrushSize_slider.GetComponent<RectTransform>();
		    if (rt == null) return false;
		    Vector2 screenPos = KeyMousePenInput.cursorScreenPos();
		    return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
	    }

	    void Update(){
	        if (!IsViewportOrBrushSizeControlHovered()){ return; }

	        float currVal = _maskBrushSize_slider.value;
	        float brushIncrement = 0.01f;
	        if(Input.GetKeyDown(KeyCode.LeftBracket)){
	            _maskBrushSize_slider.SetSliderValue( currVal-brushIncrement, invokeCallback:true);
	            _bracket_BrushSize_nextTime = Time.time + 0.2f;
	        }
	        if(Input.GetKeyDown(KeyCode.RightBracket)){ 
	            _maskBrushSize_slider.SetSliderValue( currVal+brushIncrement, invokeCallback:true);
	            _bracket_BrushSize_nextTime = Time.time + 0.2f;
	        }
	        if(Input.GetKey(KeyCode.LeftBracket) && Time.time>=_bracket_BrushSize_nextTime){
	            _maskBrushSize_slider.SetSliderValue( currVal-brushIncrement*3, invokeCallback:true);
	            _bracket_BrushSize_nextTime = Time.time+0.03f;
	        }
	        if(Input.GetKey(KeyCode.RightBracket) && Time.time>=_bracket_BrushSize_nextTime){
	            _maskBrushSize_slider.SetSliderValue( currVal+brushIncrement*3, invokeCallback:true);
	            _bracket_BrushSize_nextTime = Time.time+0.03f;
	        }
	    }

	    void Awake(){
	        if (instance == null) instance = this;
	        else if (instance != this) Debug.LogWarning("BrushRibbon_UI_Size: multiple instances found; only the first is the app-wide source of truth.");
	        _maskBrushSize_slider.onValueChanged.AddListener(OnBrushSize_slider);
	        _maskBrushSize_slider.onPressedDown.AddListener(OnBrushSize_sliderPressed);
	    }

	    void OnDestroy(){
	        if (instance == this) instance = null;
	    }

	    void Start(){
	        OnBrushSize_slider(_maskBrushSize_slider.value);
	        RefreshSpacingText();
	    }

	    public void Save(BrushRibbon_UI_SL trSL){
	        trSL.maskBrush_size01 = _maskBrushSize_slider.value;
	        trSL.maskBrush_spacing01 = _brushSpacing01;
	        trSL.maskBrush_angleDeg = _brushAngleDeg;
	        trSL.maskBrush_roundness01 = _brushRoundness01;
	        trSL.maskBrush_symmetryX = _paintSymmetryXOn;
	        trSL.maskBrush_scatterMode = (int)_scatterMode;
	        trSL.maskBrush_tipAngleMode = (int)_tipAngleMode;
	        trSL.maskBrush_symmetryPlaneSource = (int)_paintSymmetryPlaneSource;
	        if (_paintSymmetryPlaneSource == PaintSymmetryPlaneSource.FacePick)
	        {
		        trSL.maskBrush_symmetryPlanePoint = new Vector3Serializable(_symmetryPlanePointWorld.x, _symmetryPlanePointWorld.y, _symmetryPlanePointWorld.z);
		        trSL.maskBrush_symmetryPlaneNormal = new Vector3Serializable(_symmetryPlaneNormalWorld.x, _symmetryPlaneNormalWorld.y, _symmetryPlaneNormalWorld.z);
	        }
	        else
	        {
		        trSL.maskBrush_symmetryPlanePoint = null;
		        trSL.maskBrush_symmetryPlaneNormal = null;
	        }
	    }

	    /// <summary> Default brush size when none saved: 32 (on 0–100 display). </summary>
	    const float DefaultBrushSize01 = 32f / 100f;

	    public void Load(BrushRibbon_UI_SL trSL){
	        float s = trSL.maskBrush_size01;
	        if (s <= 0f || s > 1f) s = DefaultBrushSize01;
	        _maskBrushSize_slider.SetSliderValue(s, invokeCallback: true);
	        float sp = trSL.maskBrush_spacing01;
	        if (sp < 0f || sp > 1f) sp = 0f;
	        _brushSpacing01 = sp;
	        _brushAngleDeg = Mathf.Repeat(trSL.maskBrush_angleDeg, 360f);
	        _brushRoundness01 = Mathf.Clamp01(trSL.maskBrush_roundness01);
	        _paintSymmetryXOn = trSL.maskBrush_symmetryX;
	        int sc = trSL.maskBrush_scatterMode;
	        _scatterMode = (sc >= 0 && sc <= 2) ? (BrushScatterMode)sc : BrushScatterMode.None;
	        int ta = trSL.maskBrush_tipAngleMode;
	        _tipAngleMode = (ta >= 0 && ta <= 1) ? (BrushTipAngleMode)ta : BrushTipAngleMode.FixedAngle;
	        int plSrc = trSL.maskBrush_symmetryPlaneSource;
	        if (plSrc < 0 || plSrc > 2) plSrc = 0;
	        _paintSymmetryPlaneSource = (PaintSymmetryPlaneSource)plSrc;
	        if (_paintSymmetryPlaneSource == PaintSymmetryPlaneSource.FacePick
	            && trSL.maskBrush_symmetryPlanePoint != null && trSL.maskBrush_symmetryPlaneNormal != null)
	        {
		        _symmetryPlanePointWorld = trSL.maskBrush_symmetryPlanePoint.toVec3();
		        _symmetryPlaneNormalWorld = trSL.maskBrush_symmetryPlaneNormal.toVec3();
		        if (_symmetryPlaneNormalWorld.sqrMagnitude < 1e-8f)
			        _paintSymmetryPlaneSource = PaintSymmetryPlaneSource.Auto;
		        else
			        _symmetryPlaneNormalWorld.Normalize();
	        }
	        else if (_paintSymmetryPlaneSource == PaintSymmetryPlaneSource.FacePick)
		        _paintSymmetryPlaneSource = PaintSymmetryPlaneSource.Auto;
	        RefreshSpacingText();
	    }
	}
}//end namespace
