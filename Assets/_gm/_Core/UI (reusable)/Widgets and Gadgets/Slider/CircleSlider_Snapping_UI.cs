using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace spz {

	public class CircleSlider_Snapping_UI : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IEndDragHandler,
	                                        IDragHandler, IInitializePotentialDragHandler, IEventSystemHandler
	{
	    [SerializeField] Image _fillImage = null;
	    [SerializeField] TextMeshProUGUI _text = null;
	    [SerializeField] IntegerInputField _int_input;
	    [SerializeField] FloatInputField _float_input;
	    [SerializeField] float _sensitivity = 10;
	    [SerializeField]
	    [Tooltip("Multiplies horizontal stylus drag only (mouse unchanged). Higher = reach min/max with less hand travel. Tune per instance (e.g. brush size dial) in the Inspector.")]
	    [Min(0.25f)]
	    float _penStylusDragGain = 2f;
	    [SerializeField] string _prefix = "";
	    [SerializeField] float _min = 1f;
	    [SerializeField] float _max = 100f;
	    [SerializeField] float _default = 512f;
	    [SerializeField] float _increment = 0.5f;
	    [SerializeField] int _decimalPlaces = 1;
	    [SerializeField] RightMouseClickListener_UI _rightMouseClickListener;
	    [SerializeField] Transform _scaleAffectsSinsitiv_optional;

	    bool _valueNeverChangedYet = true;
	    public UnityEvent onPressedDown { get; } = new UnityEvent();
	    public UnityEvent<float> onValueChanged { get; } = new UnityEvent<float>();
	    public bool isInteractable { get; set; } = true;

	    bool _isDragging = false;
	    float _value;

	    public float value{ get{ return _value; } set { _value=value;}  }
	    public float min { get{ return _min; }  set{ _min = value; }  }
	    public float max { get{ return _max; }  set{ _max = value; }  }
	    public float defaultVal{ get{ return _default; }  set { _default = value; }}

	    // Accumulates small mouse movements for improved responsiveness
	    float _accumulatedDelta = 0f;
	    /// <summary>Pointer/pen/touch delta from OnDrag (pixels). Used so slider works with tablet and touch, not just mouse.</summary>
	    float _accumulatedPointerDeltaX = 0f;

	    void Awake(){
	        // Setting default value.
	        // important to do in Awake! Unlike Start, Awake is called even if our GO is disabled.
	        if (_valueNeverChangedYet){ _value = _default; }
	    }

	    void Start(){
	        _int_input?.onValidInput.AddListener(OnInputFieldEndEdit); // Add listener for when user finishes editing
	        _float_input?.onValidInput.AddListener(OnFloatFieldEndEdit);
	        _rightMouseClickListener.OnRightClick += () => { SetValue(_default); };
	        // Initialize text and slider value
	        if (_valueNeverChangedYet){
	            _value = _default;
	            SetSliderValue(_default, false);
	        }
	        UpdateFillAmount();
	        UpdateText();
	    }

	    void OnDisable(){
	        _isDragging = false;
	        _accumulatedPointerDeltaX = 0f;
	    }

	    public void OnPointerDown(PointerEventData eventData){
	        if (!isInteractable) { return; }
	        // Middle/right are reserved (e.g. reset on RMB). Pen/touch primary contact must not be rejected
	        // because some runtimes do not map tip to InputButton.Left the same way as mouse.
	        if (eventData.button == PointerEventData.InputButton.Right || eventData.button == PointerEventData.InputButton.Middle)
		        return;
	        _isDragging = true;
	        onPressedDown?.Invoke();
	    }

	    //empty, needed to prevent event from propagating to scroll rect etc (parent) from here.
	    public void OnBeginDrag(PointerEventData eventData) { }
	    public void OnEndDrag(PointerEventData eventData) { }
	    public void OnDrag(PointerEventData eventData)
	    {
		    if (!_isDragging || !isInteractable) return;
		    float sensit = _sensitivity;
		    if (_scaleAffectsSinsitiv_optional != null) sensit /= _scaleAffectsSinsitiv_optional.localScale.x;
		    _accumulatedPointerDeltaX += eventData.delta.x * sensit * 0.01f;
	    }
	    public void OnInitializePotentialDrag(PointerEventData eventData) { }

	    void Update(){
	        if(_isDragging && isInteractable){
	            float sensit = _sensitivity;
	            if (_scaleAffectsSinsitiv_optional != null) sensit /= _scaleAffectsSinsitiv_optional.localScale.x;
	            float delta;
	            // Stylus: legacy Mouse X does not track pen; use Pen.delta (same scale as OnDrag eventData.delta * 0.01).
	            // Skip accumulating OnDrag for the same frame to avoid double-applying identical motion.
	            if (Pen.current != null && (Pen.current.tip.isPressed || Pen.current.eraser.isPressed))
	            {
		            delta = Pen.current.delta.ReadValue().x * sensit * 0.01f * _penStylusDragGain;
		            _accumulatedPointerDeltaX = 0f;
	            }
	            else
	            {
		            delta = Calc_MouseDelta() + _accumulatedPointerDeltaX;
		            _accumulatedPointerDeltaX = 0f;
	            }
	            _accumulatedDelta += delta;

	            float normalizedValue = Mathf.InverseLerp(_min, _max, _value);
	            float newNormalizedValue = normalizedValue + _accumulatedDelta * 0.01f;
	            newNormalizedValue = Mathf.Clamp01(newNormalizedValue);

	            float snappedNormalizedValue = SnapToIncrement(newNormalizedValue, _increment / (_max-_min));

	            // Update value only when change is significant (at least half an increment)
	            float elapsed =  Mathf.Abs(snappedNormalizedValue - normalizedValue);
	            bool isSufficient =  elapsed >= _increment / (_max-_min) * 0.5f;
	            if (isSufficient){
	                float newValue = Mathf.Lerp(_min, _max, snappedNormalizedValue);
	                SetValue(newValue);
	                _accumulatedDelta = 0f; // Reset accumulated delta after updating
	            }
	        }
	        if(KeyMousePenInput.isLMBreleasedThisFrame() || (_isDragging && !isInteractable)){
	            _isDragging = false;
	            _accumulatedDelta = 0f;
	            _accumulatedPointerDeltaX = 0f;
	        }
	    }

	    float Calc_MouseDelta(){
	        float mouseMovement = Input.GetAxis("Mouse X");
	        float sensit = _sensitivity;
	        if (_scaleAffectsSinsitiv_optional != null) { sensit /= _scaleAffectsSinsitiv_optional.localScale.x; }
	        return mouseMovement * sensit;
	    }

	    void SetValue(float value){
	        SetSliderValue(value, true);
	    }

	    public void SetSliderValue(float value, bool invokeCallback){
	        value = Mathf.Clamp(value, _min, _max);
	        _value = value;
	        UpdateFillAmount();
	        UpdateText();
	        if (invokeCallback) { onValueChanged?.Invoke(_value); }
	        _valueNeverChangedYet = false;
	    }

	    void UpdateFillAmount(){
	        float fillAmount = Mathf.InverseLerp(_min, _max, _value);
	        _fillImage.fillAmount = fillAmount;
	    }

	    void UpdateText(){
	        if (_text == null) return;
	        string format = "F" + _decimalPlaces;
	        string inputText = _value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
	        _text.text = _prefix + inputText;
	    }

	    float SnapToIncrement(float value, float increment){
	        return Mathf.Round(value / increment) * increment;
	    }

	    void OnInputFieldEndEdit(int inputVal){
	        inputVal = (int)Mathf.Clamp((float)inputVal, _min, _max);
	        SetSliderValue((float)inputVal, true);
	        _valueNeverChangedYet = false;
	    }

	    void OnFloatFieldEndEdit(float inputVal){
	        inputVal = Mathf.Clamp(inputVal, _min, _max);
	        SetSliderValue((float)inputVal, true);
	        _valueNeverChangedYet = false;
	    }
	}
}//end namespace
