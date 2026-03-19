using TMPro;
using UnityEngine;

namespace spz {

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
	        RefreshSpacingText();
	    }
	}
}//end namespace
