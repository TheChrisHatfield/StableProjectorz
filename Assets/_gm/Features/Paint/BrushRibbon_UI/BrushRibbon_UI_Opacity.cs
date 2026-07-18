using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;


namespace spz {

	//helps the 'BrushRibbon_UI' component.
	// Only deals with and represents the small button that shows the brush strength/opacity.
	public class BrushRibbon_UI_Opacity : MonoBehaviour{

	    [SerializeField] TextMeshProUGUI _brushOpacityText;
	    [SerializeField] BrushRibbon_UI_Colors _colors;
	    public float _maskBrushOpacity {get; private set;}

	    /// <summary> Current brush strength 0–1 (JSON-RPC / scripts; same backing field as UI). </summary>
	    public float Opacity01 => _maskBrushOpacity;

	    /// <summary> Set strength 0–1 from code (same path as slider: workflow rules e.g. Inpaint_NoColor → 100%). </summary>
	    public void SetOpacity01(float opacity01) => SetBrushOpacity(Mathf.Clamp01(opacity01));

	    /// <summary> Same as <see cref="SetOpacity01(float)"/> but optionally without the viewport status text
	    /// (Value Assist live ticks / restore would otherwise spam "Brush Opacity NN" every update). </summary>
	    public void SetOpacity01(float opacity01, bool quiet) => SetBrushOpacity(Mathf.Clamp01(opacity01), quiet);

	    //we might temporariyl override opacity (due to Colorless-mask, etc). This is what it used to be.
	    float _nonOverridenOpacity;


	    void OnUpdateTextColor(Color col)
	        => _brushOpacityText.color =  SD_WorkflowOptionsRibbon_UI.instance.isPositive?  
	                                            new Color(0.2f, 0.2f, 0.2f, 1)  : new Color(0.8f, 0.8f, 0.8f, 1);


	    void SetBrushOpacity(float brushOpacity, bool quiet=false){

	        float printMsgDur = 1;

	        if (WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor){ 
	            brushOpacity = 1.0f;//important, to avoid bugs. User can instead control denoising strength + blur.

	            //maybe skip showing our message, because thiers is more rare and important:
	            if (!quiet && WorkflowRibbon_NoColor_UI.didShowHint_thisFrame()==false){
	                string msg = "Brush Opacity kept as 100 (colorless-mask)";
	                Viewport_StatusText.instance.ShowStatusText(msg, false, printMsgDur, false);
	            }
	        }else { 
	            if (!quiet){
	                string msg = "Brush Opacity " + Mathf.RoundToInt(brushOpacity * 100);
	                Viewport_StatusText.instance.ShowStatusText(msg, false, printMsgDur, false);
	            }
	            _nonOverridenOpacity = brushOpacity;
	        }
	        _maskBrushOpacity = brushOpacity;

	        int opacityInt = Mathf.RoundToInt(brushOpacity * 100);
	        _brushOpacityText.text = opacityInt == 100 ? $"<size=85%>{opacityInt}</size>" : opacityInt.ToString();
	    }


	    void OnWorkflowModeChanged(WorkflowRibbon_CurrMode mode){
	        if(mode == WorkflowRibbon_CurrMode.Inpaint_NoColor){
	            SetBrushOpacity(1.0f);
	        }
	    }


	    void Update(){
	        if(MainViewport_UI.instance.isCursorHoveringMe() == false){ return; }
	        bool hasCTRL = KeyMousePenInput.isKey_CtrlOrCommand_pressed();
	        bool hasShift = KeyMousePenInput.isKey_Shift_pressed();
	        if (!hasCTRL && !hasShift){
	            if(Input.GetKeyDown(KeyCode.Alpha1)){ SetBrushOpacity(0.1f); }
	            if(Input.GetKeyDown(KeyCode.Alpha2)){ SetBrushOpacity(0.2f); }
	            if(Input.GetKeyDown(KeyCode.Alpha3)){ SetBrushOpacity(0.3f); }
	            if(Input.GetKeyDown(KeyCode.Alpha4)){ SetBrushOpacity(0.4f); }
	            if(Input.GetKeyDown(KeyCode.Alpha5)){ SetBrushOpacity(0.5f); }
	            if(Input.GetKeyDown(KeyCode.Alpha6)){ SetBrushOpacity(0.6f); }
	            if(Input.GetKeyDown(KeyCode.Alpha7)){ SetBrushOpacity(0.7f); }
	            if(Input.GetKeyDown(KeyCode.Alpha8)){ SetBrushOpacity(0.8f); }
	            if(Input.GetKeyDown(KeyCode.Alpha9)){ SetBrushOpacity(0.9f); }
	            if(Input.GetKeyDown(KeyCode.Alpha0)){ SetBrushOpacity(1.0f); }
	        }
	    }


	    void Awake(){
	        _colors._onBrushColorUpdated += OnUpdateTextColor;
	        WorkflowRibbon_UI._Act_OnModeChanged += OnWorkflowModeChanged;
	    }

	    void Start(){
	        SetBrushOpacity(1);
	    }

	    public void Save(BrushRibbon_UI_SL trSL){
	        trSL.maskBrush_opacity01  = _nonOverridenOpacity;
	    }

	    public void Load(BrushRibbon_UI_SL trSL){
	        _maskBrushOpacity = _nonOverridenOpacity = trSL.maskBrush_opacity01;
	    }
	}
}//end namespace
