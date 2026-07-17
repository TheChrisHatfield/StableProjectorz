using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	//helps the 'BrushRibbon_UI' component.
	// Only deals with and represents the small button that controls the brush hardness.
	// When BrushAlphas_MGR is present, supports custom alphas from the user BrushAlphas folder;
	// _brushHardnessTex then returns the current stamp (built-in round or selected custom alpha).
	public class BrushRibbon_UI_Hardness : MonoBehaviour
	{
	    [SerializeField] BrushRibbon_UI _rib;
	    [Space(10)]
	    [SerializeField] Button _hardnessButton;
	    [SerializeField] Image _hardnessChoiceIcon;
	    [SerializeField] List<Sprite> _brushHardnessTextures;
	    [SerializeField] Animation _currHardnessAnim;
	    [Tooltip("Optional. When set, brush stamp can be built-in (0,1,2) or custom alphas from folder.")]
	    [SerializeField] BrushAlphas_MGR _brushAlphasMGR;
	    public int hardnessIx { get; private set; } = 0;
	    /// <summary> Current brush stamp: from BrushAlphas_MGR if present (built-in or custom), else built-in list only. </summary>
	    public Texture2D _brushHardnessTex =>
	        (_brushAlphasMGR != null && _brushAlphasMGR.CurrentBrushStampTex != null)
	            ? _brushAlphasMGR.CurrentBrushStampTex
	            : (_brushHardnessTextures != null && hardnessIx < _brushHardnessTextures.Count && _brushHardnessTextures[hardnessIx] != null
	                ? _brushHardnessTextures[hardnessIx].texture
	                : null);
	    public Texture2D readSpecificHardnessTex(int hardnessIx) => _brushHardnessTextures[hardnessIx].texture;
	    public Action onHovered { get; set; }


	    void OnHardnessButtonHover(PointerEventData pe){
	        if(KeyMousePenInput.isLMBpressed()){ return; }//likely dragging some slider, don't distract user.
	        onHovered?.Invoke();
	    }


	    void OnHardnessButton(){
	        // When a custom alpha is selected, don't overwrite it — hardness button only cycles built-in (0,1,2).
	        if (_brushAlphasMGR != null && _brushAlphasMGR.IsCustomAlpha(_brushAlphasMGR.CurrentIndex))
	            return;
	        // Cycle built-in only (0,1,2). Custom alphas are selected via BrushRibbon_UI_AlphaPicker.
	        hardnessIx = (hardnessIx + 1) > 2 ? 0 : (hardnessIx + 1);
	        if (_brushAlphasMGR != null)
	            _brushAlphasMGR.CurrentIndex = hardnessIx;
	        UpdateHardnessIcon();
	        _currHardnessAnim.Play();
	    }

	    void SetExactHardness(int exactHardness_textureIx, bool playAnimation=true){
	        hardnessIx = Mathf.Clamp(exactHardness_textureIx, 0, 2);
	        if (_brushAlphasMGR != null)
	            _brushAlphasMGR.CurrentIndex = hardnessIx;
	        UpdateHardnessIcon();
	        if(playAnimation){ _currHardnessAnim.Play(); }
	    }

	    void UpdateHardnessIcon(){
	        if (_brushHardnessTextures != null && hardnessIx < _brushHardnessTextures.Count && _brushHardnessTextures[hardnessIx] != null)
	            _hardnessChoiceIcon.sprite = _brushHardnessTextures[hardnessIx];
	        // When using custom alpha (index >= 3), icon could be updated by BrushRibbon_UI_AlphaPicker
	    }

	    /// <summary> Call when BrushAlphas_MGR current selection is set from alpha picker (custom alpha). </summary>
	    public void SetUsingCustomAlpha(int customAlphaIndex){
	        if (_brushAlphasMGR == null) return;
	        _brushAlphasMGR.CurrentIndex = 3 + customAlphaIndex;
	        hardnessIx = 0; // show soft round icon as placeholder; actual stamp is custom
	        UpdateHardnessIcon();
	    }

	    /// <summary> Select one of the three built-in round brushes by index (0,1,2). </summary>
	    public void SetBuiltInOnly(int builtInIx){
	        hardnessIx = Mathf.Clamp(builtInIx, 0, 2);
	        if (_brushAlphasMGR != null)
	            _brushAlphasMGR.CurrentIndex = hardnessIx;
	        UpdateHardnessIcon();
	    }

	    /// <summary>True when the live stamp is a custom alpha (not built-in 0–2).</summary>
	    public bool IsUsingCustomAlpha() {
		    return _brushAlphasMGR != null && _brushAlphasMGR.IsCustomAlpha(_brushAlphasMGR.CurrentIndex);
	    }

	    /// <summary>Like <see cref="SetBuiltInOnly"/> but refuses to overwrite a custom alpha selection (matches hardness button).</summary>
	    public bool TrySetBuiltInOnly(int builtInIx) {
		    if (IsUsingCustomAlpha())
			    return false;
		    SetBuiltInOnly(builtInIx);
		    return true;
	    }


	    void OnStartEditMode(MultiView_StartEditMode_Args args){
	        if(Art2D_IconsUI_List.instance._mainSelectedIcon == null){  return; }
	        if(Art2D_IconsUI_List.instance._mainSelectedIcon._genData.povInfos.numEnabled == 1){ return; }
	        //softest brush isn't sufficient for multiview. Its preview is barely visible. Switching to medium brush:
	        SetExactHardness(1);
	    }

	    void Update(){
	        // COMMENTED OUT, KEPT FOR PRECAUTION. Allow user to do it from anywhere, without hovering the viewport:
	        //    if(MainViewport_UI.instance.isCursorHoveringMe() == false){ return; }
	        if(KeyMousePenInput.isSomeInputFieldActive()){ return; }//maybe typing text, etc

	        if(Input.GetKeyDown(KeyCode.H)){  OnHardnessButton(); }//to next brush hardness

	        bool hasCTRL = KeyMousePenInput.isKey_CtrlOrCommand_pressed();
	        bool hasShift = KeyMousePenInput.isKey_Shift_pressed();
	        if (hasCTRL && !hasShift){
	            if(Input.GetKeyDown(KeyCode.Alpha1)){ SetExactHardness(0); }
	            if(Input.GetKeyDown(KeyCode.Alpha2)){ SetExactHardness(1); }
	            if(Input.GetKeyDown(KeyCode.Alpha3)){ SetExactHardness(2); }
	        }
	    }

	    void Awake(){
	        _hardnessButton.onClick.AddListener( OnHardnessButton );

	        if (_brushAlphasMGR == null) _brushAlphasMGR = BrushAlphas_MGR.instance;
	        if (_brushAlphasMGR == null) _brushAlphasMGR = FindObjectOfType<BrushAlphas_MGR>(true);

	        SetExactHardness(hardnessIx);
	        if (_brushAlphasMGR != null)
	            _brushAlphasMGR.CurrentIndex = hardnessIx;
	        UpdateHardnessIcon();
	        _hardnessButton.GetComponentInChildren<MouseHoverSensor_UI>().onSurfaceEnter += OnHardnessButtonHover;

	        MultiView_Ribbon_UI.OnStartEditMode += OnStartEditMode;
	    }

	    void Start(){
	        if (_brushAlphasMGR == null) _brushAlphasMGR = BrushAlphas_MGR.instance;
	        if (_brushAlphasMGR == null) _brushAlphasMGR = FindObjectOfType<BrushAlphas_MGR>(true);
	    }


	    public void Save(BrushRibbon_UI_SL trSL){
	        trSL.maskBrush_hardnessIx = hardnessIx;
	        if (_brushAlphasMGR != null && _brushAlphasMGR.IsCustomAlpha(_brushAlphasMGR.CurrentIndex))
	            trSL.maskBrush_customAlphaIx = _brushAlphasMGR.CurrentIndex - 3;
	        else
	            trSL.maskBrush_customAlphaIx = -1;
	    }

	    public void Load(BrushRibbon_UI_SL trSL){
	        if (trSL.maskBrush_customAlphaIx >= 0 && _brushAlphasMGR != null &&
	            (3 + trSL.maskBrush_customAlphaIx) < _brushAlphasMGR.AllEntries.Count)
	            SetUsingCustomAlpha(trSL.maskBrush_customAlphaIx);
	        else
	            SetExactHardness(trSL.maskBrush_hardnessIx);
	    }
	}
}//end namespace
