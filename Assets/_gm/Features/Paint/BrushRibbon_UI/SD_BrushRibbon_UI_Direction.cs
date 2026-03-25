using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace spz {

	// For StableDiffusion-texturing mode.
	// Helps the 'BrushRibbon_UI' component.
	// Knows whether we will be adding (positive) or erasing (negative) color with the brush.
	// Only deals with and represents the small button that shows the direction.
	public class SD_BrushRibbon_UI_Direction : BrushRibbon_UI_Direction{
	    [SerializeField] WorkflowRibbon_UI _rib;
	    [SerializeField] BrushRibbon_UI_Colors _colors;
    
	    void OnUpdateDirection_Mode( WorkflowRibbon_CurrMode currMode ){
	        switch (currMode){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking:
	                if(Keyboard.current.rKey.isPressed){ return; }
	                SetToolMode(BrushToolMode.Erase);
	                break;
	            case WorkflowRibbon_CurrMode.Inpaint_Color: SetToolMode(BrushToolMode.Paint); break;
	            case WorkflowRibbon_CurrMode.Inpaint_NoColor: SetToolMode(BrushToolMode.Paint); break;
	            case WorkflowRibbon_CurrMode.TotalObject: SetToolMode(BrushToolMode.Paint); break;
	            case WorkflowRibbon_CurrMode.WhereEmpty: SetToolMode(BrushToolMode.Paint); break;
	            default: break;
	        }
	    }

	    protected void OnStartedEditMode_MultiView( MultiView_StartEditMode_Args args ){
	        IconUI icon = Art2D_IconsUI_List.instance._mainSelectedIcon;
	        if (icon != null){
	            bool isMultiPOV =  icon._genData.povInfos.numEnabled > 1;
	            if(isMultiPOV){  SetToolMode(BrushToolMode.Paint);  }
	        }
	    }

	    void OnUpdateDirection_Toggle(Toggle toggle, bool isOn){
	        if(!isOn){ return; }
	        if(Projections_MaskPainter.instance._isPainting){ return; }
	        if(Inpaint_MaskPainter.instance._isPainting){ return; }

	        if (toggle == _brushSmudge_Toggle){
	            Cursor_UI.instance.SetCursorColor( new Color(0.5f, 0.5f, 0.5f, 1f) );
	        } else {
	            bool positive = toggle == _brushAdd_Toggle;
	            Cursor_UI.instance.SetCursorColor( positive? Color.white : Color.black );
	        }
	    }


	    void OnBrushStrokeEnd(){
	        base._anim.Play();
	    }


	    protected override void Awake(){
	        base.Awake();
	        CreateSmudgeToggle_IfNeeded();
	        MultiView_Ribbon_UI.OnStartEditMode += OnStartedEditMode_MultiView;
        
	        if(_colors != null){ 
	            _colors._onBrushColorUpdated += (Color col)=>SetToolMode(BrushToolMode.Paint);
	        }
	        if(_rib!=null){
	            WorkflowRibbon_UI._Act_OnModeChanged += OnUpdateDirection_Mode;
	        }
	        Projections_MaskPainter.Act_OnPaintStrokeEnd += OnBrushStrokeEnd;
	        Inpaint_MaskPainter.Act_OnPaintStrokeEnd    += OnBrushStrokeEnd;

	        _brushAdd_Toggle.onValueChanged.AddListener( (isOn)=>OnUpdateDirection_Toggle(_brushAdd_Toggle, isOn) );
	        _brushErase_Toggle.onValueChanged.AddListener( (isOn)=>OnUpdateDirection_Toggle(_brushErase_Toggle, isOn) );
	        if (_brushSmudge_Toggle != null)
	            _brushSmudge_Toggle.onValueChanged.AddListener( (isOn)=>OnUpdateDirection_Toggle(_brushSmudge_Toggle, isOn) );
	    }

    
	    protected override void Start(){
	        base.Start();
	        // Default state must be internally consistent: Erase selected, others off.
	        _brushAdd_Toggle.SetIsOnWithoutNotify(false);
	        _brushErase_Toggle.SetIsOnWithoutNotify(true);
	        if (_brushSmudge_Toggle != null) _brushSmudge_Toggle.SetIsOnWithoutNotify(false);
	        if (Cursor_UI.instance != null)
	            Cursor_UI.instance.SetCursorColor(Color.black);
	    }

	    void CreateSmudgeToggle_IfNeeded(){
	        if (_brushSmudge_Toggle != null) return;
	        if (_brushAdd_Toggle == null || _brushErase_Toggle == null) return;

	        GameObject cloneSrc = _brushAdd_Toggle.gameObject;
	        Transform parent = cloneSrc.transform.parent;
	        if (parent == null) return;

	        GameObject smudgeGO = Instantiate(cloneSrc, parent);
	        smudgeGO.name = "SmudgeBrush_Toggle";

	        _brushSmudge_Toggle = smudgeGO.GetComponent<Toggle>();
	        if (_brushSmudge_Toggle == null){
	            Destroy(smudgeGO);
	            return;
	        }

	        ToggleGroup grp = _brushAdd_Toggle.group;
	        if (grp != null) _brushSmudge_Toggle.group = grp;
	        _brushSmudge_Toggle.SetIsOnWithoutNotify(false);

	        // Adjust anchor positions: brush=top third, smudge=middle third, eraser=bottom third
	        RectTransform addRect = _brushAdd_Toggle.GetComponent<RectTransform>();
	        RectTransform smudgeRect = smudgeGO.GetComponent<RectTransform>();
	        RectTransform eraseRect = _brushErase_Toggle.GetComponent<RectTransform>();

	        const float third = 1f / 3f;
	        const float pad = 0.015f;
	        float baseLeft = addRect != null ? addRect.anchorMin.x : 0f;
	        float baseRight = addRect != null ? addRect.anchorMax.x : 1f;

	        if (addRect != null){
	            addRect.anchorMin = new Vector2(addRect.anchorMin.x, 2 * third + pad);
	            addRect.anchorMax = new Vector2(addRect.anchorMax.x, 1f);
	            baseLeft = addRect.anchorMin.x;
	            baseRight = addRect.anchorMax.x;
	        }
	        if (smudgeRect != null){
	            smudgeRect.anchorMin = new Vector2(baseLeft, third + pad);
	            smudgeRect.anchorMax = new Vector2(baseRight, 2 * third - pad);
	            smudgeRect.offsetMin = Vector2.zero;
	            smudgeRect.offsetMax = Vector2.zero;
	            smudgeRect.SetSiblingIndex(_brushAdd_Toggle.transform.GetSiblingIndex() + 1);
	        }
	        if (eraseRect != null){
	            eraseRect.anchorMin = new Vector2(eraseRect.anchorMin.x, 0f);
	            eraseRect.anchorMax = new Vector2(eraseRect.anchorMax.x, third - pad);
	        }

	        // Increase parent layout element height to fit 3 toggles
	        var rootLayout = GetComponent<LayoutElement>();
	        if (rootLayout != null && rootLayout.minHeight < 200f)
	            rootLayout.minHeight = 210f;

	        TrySetSmudgeIcon(smudgeGO);
	    }

	    void TrySetSmudgeIcon(GameObject smudgeGO){
	        Transform iconTr = smudgeGO.transform.Find("icon");
	        if (iconTr == null){
	            foreach (Transform child in smudgeGO.transform){
	                Image img = child.GetComponent<Image>();
	                if (img != null && img.sprite != null){ iconTr = child; break; }
	            }
	        }
	        if (iconTr == null) return;
	        Image iconImage = iconTr.GetComponent<Image>();
	        if (iconImage == null) return;

	        Sprite loaded = TryLoadSmudgeSprite();
	        // Sprite is white line-art on transparent (same convention as brush/erase); black Image tint → visible glyph.
	        if (loaded != null){
	            iconImage.sprite = loaded;
	            iconImage.preserveAspect = true;
	        }
	    }

	    static Sprite TryLoadSmudgeSprite(){
	        var spr = Resources.Load<Sprite>("icon_smudge");
	        if (spr != null) return spr;
	        Texture2D tex = Resources.Load<Texture2D>("icon_smudge");
	        if (tex != null)
	            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

	        #if UNITY_EDITOR
	        var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_gm/Art/Icons/icon_smudge.png");
	        if (obj != null) return obj;
	        var texEd = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_gm/Art/Icons/icon_smudge.png");
	        if (texEd != null)
	            return Sprite.Create(texEd, new Rect(0, 0, texEd.width, texEd.height), new Vector2(0.5f, 0.5f), 100f);
	        #endif

	        return null;
	    }
	}



	public class BrushRibbon_UI_Direction : MonoBehaviour{
	    [Space(10)]
	    [SerializeField] protected Toggle _brushErase_Toggle;
	    [SerializeField] protected Toggle _brushAdd_Toggle;
	    [SerializeField] protected Animation _anim;
	    protected Toggle _brushSmudge_Toggle;
    
	    public bool isPositive => _brushAdd_Toggle.isOn;
	    public bool isSmudge => _brushSmudge_Toggle != null && _brushSmudge_Toggle.isOn;

	    public BrushToolMode toolMode {
	        get {
	            if (_brushSmudge_Toggle != null && _brushSmudge_Toggle.isOn) return BrushToolMode.Smudge;
	            if (_brushAdd_Toggle.isOn) return BrushToolMode.Paint;
	            return BrushToolMode.Erase;
	        }
	    }

	    protected void SetDirection_Toggle(bool isPositive_dir){
	        if (isPositive_dir){ _brushAdd_Toggle.isOn = true; }
	        if(!isPositive_dir){ _brushErase_Toggle.isOn = true; }
	    }

	    protected void SetToolMode(BrushToolMode mode){
	        switch (mode){
	            case BrushToolMode.Paint:  _brushAdd_Toggle.isOn = true; break;
	            case BrushToolMode.Smudge:
	                if (_brushSmudge_Toggle != null) _brushSmudge_Toggle.isOn = true;
	                else _brushAdd_Toggle.isOn = true;
	                break;
	            case BrushToolMode.Erase:  _brushErase_Toggle.isOn = true; break;
	        }
	    }

	    protected virtual void Update(){
	        if(KeyMousePenInput.isSomeInputFieldActive()){ 
	            return; 
	        }

	        bool anyPainting = (Inpaint_MaskPainter.instance != null && Inpaint_MaskPainter.instance._isPainting)
	                          || (Projections_MaskPainter.instance != null && Projections_MaskPainter.instance._isPainting);
	        if (!anyPainting){
	            if (KeyMousePenInput.isPenEraserPressedThisFrame()){ SetToolMode(BrushToolMode.Erase); }
	            else if (KeyMousePenInput.isPenTipPressedThisFrame()){ SetToolMode(BrushToolMode.Paint); }
	        }

	        bool hasCTRL = KeyMousePenInput.isKey_CtrlOrCommand_pressed();
	        if (!hasCTRL){
	            if(Input.GetKeyDown(KeyCode.X)){  SetDirection_Toggle(!isPositive);  }
	        }
	    }
	    protected virtual void Awake(){}
	    protected virtual void Start(){ }
	}
}//end namespace
