using System;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// Owns the UI controls that affect brushing.
	// Doesn't actually deal with textures etc, only with the UI controls.
	public class BrushRibbon_UI : MonoBehaviour{
	    public static BrushRibbon_UI instance { get; private set; } = null;

	    [Space(10)]
	    [SerializeField] BrushRibbon_UI_Colors _colors;
	    [SerializeField] BrushRibbon_UI_Opacity _opacity;
	    [SerializeField] BrushRibbon_UI_Hardness _hardness;
	    [SerializeField] BrushRibbon_UI_PressureMode _pressureTabletMode;
	    [Space(10)]
	    [SerializeField] BrushRibbon_UI_Size _size;
	    [SerializeField] BrushRibbon_UI_BucketFill _bucketFill;
	    [SerializeField] BrushRibbon_UI_InvertMask _invertMask;
	    [SerializeField] BrushRibbon_UI_DeleteButton _deleteColorsButton;
	    [SerializeField] Toggle _eyeDropperToggle;

	    public BrushRibbon_UI_Hardness brushHardnessUI => _hardness;

	    /// <summary> Set brush size 0–1. Used when applying ABR suggested size; also allows AlphaPicker to work when SD_WorkflowOptionsRibbon_UI is not present. </summary>
	    public void SetBrushSize(float s) { if (_size != null) _size.SetBrushSize(s); }
	    /// <summary> Set brush spacing 0–1 (0 = continuous). Used when applying ABR suggested spacing. </summary>
	    public void SetBrushSpacing(float s) { if (_size != null) _size.SetBrushSpacing(s); }
	    public void SetBrushAngle(float deg) { if (_size != null) _size.SetBrushAngle(deg); }
	    public void SetBrushRoundness(float r) { if (_size != null) _size.SetBrushRoundness(r); }
	    public float brushSize01 => _size != null ? _size.brushSize01 : 0f;
	    public float brushSpacing01 => _size != null ? _size.brushSpacing01 : 0f;
	    public float brushAngleDeg => _size != null ? _size.brushAngleDeg : 0f;
	    public float brushRoundness01 => _size != null ? _size.brushRoundness01 : 1f;

	    public float brushOpacity01 => _opacity != null ? _opacity.Opacity01 : 1f;
	    public void SetBrushOpacity01(float opacity01) { if (_opacity != null) _opacity.SetOpacity01(opacity01); }

	    void Awake(){
	        if (instance != null){ DestroyImmediate(this); return; }
	        instance = this;

	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:ColorsButton", _colors);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:OpacityButton", _opacity);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:HardnessButton", _hardness);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:PressureButton", _pressureTabletMode);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:SizeSlider", _size);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:BucketFillButton", _bucketFill);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:InvertMaskButton", _invertMask);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:DeleteColorsButton", _deleteColorsButton);
	        EventsBinder.Bind_Clickable_to_event("BrushRibbon_UI:EyeDropperToggle", _eyeDropperToggle);
	    }

	    public void Save( StableProjectorz_SL spz){
	        var trSL = new BrushRibbon_UI_SL();
	        _hardness.Save(trSL);
	        _colors.Save(trSL);
	        _size.Save(trSL);
	        _opacity.Save(trSL);
	        spz.brush_MGR = trSL;
	    }

	    public void Load(StableProjectorz_SL spz){
	        BrushRibbon_UI_SL trSL = spz.brush_MGR;
	        if (trSL == null) return;
	        _hardness.Load(trSL);
	        _colors.Load(trSL);
	        _size.Load(trSL);
	        _opacity.Load(trSL);
	    }
	}
}//end namespace
