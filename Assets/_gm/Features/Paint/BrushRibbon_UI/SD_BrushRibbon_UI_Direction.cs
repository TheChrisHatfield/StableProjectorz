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

	    /// <summary> JSON-RPC / add-ons: paint, smudge, or erase without simulating UI toggles. </summary>
	    public void SetToolModeFromApi(BrushToolMode mode) => SetToolMode(mode);
    
	    void OnUpdateDirection_Mode( WorkflowRibbon_CurrMode currMode ){
	        switch (currMode){
	            case WorkflowRibbon_CurrMode.ProjectionsMasking:
	                if (Keyboard.current != null && Keyboard.current.rKey.isPressed){ return; }
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
	        var icon = Art2D_IconsUI_List.instance?._mainSelectedIcon;
	        if (icon?._genData?.povInfos?.povs == null){ return; }
	        bool isMultiPOV = icon._genData.povInfos.numEnabled > 1;
	        if (isMultiPOV){ SetToolMode(BrushToolMode.Paint); }
	    }

	    void OnBrushColorUpdated_ForcePaintMode(Color _){
	        SetToolMode(BrushToolMode.Paint);
	    }

	    void OnUpdateDirection_Toggle(Toggle toggle, bool isOn){
	        if(!isOn){ return; }
	        if(Projections_MaskPainter.instance != null && Projections_MaskPainter.instance._isPainting){ return; }
	        if(Inpaint_MaskPainter.instance != null && Inpaint_MaskPainter.instance._isPainting){ return; }

	        if (toggle != _brushSmudge_Toggle){
	            bool positive = toggle == _brushAdd_Toggle;
	            Cursor_UI.instance?.SetCursorColor( positive? Color.white : Color.black );
	        }
	        // Smudge: ring tint comes from mesh under cursor (Inpaint_MaskPainter GPU readback).
	        BrushRibbon_UI_Direction.RaiseDirectionToggleChanged();
	    }


	    void OnBrushStrokeEnd(){
	        base._anim.Play();
	    }


	    protected override void Awake(){
	        base.Awake();
	        CreateSmudgeToggle_IfNeeded();
	        MultiView_Ribbon_UI.OnStartEditMode += OnStartedEditMode_MultiView;
        
	        if(_colors != null){ 
	            _colors._onBrushColorUpdated += OnBrushColorUpdated_ForcePaintMode;
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

	    void OnDestroy(){
	        MultiView_Ribbon_UI.OnStartEditMode -= OnStartedEditMode_MultiView;
	        if (_rib != null){
	            WorkflowRibbon_UI._Act_OnModeChanged -= OnUpdateDirection_Mode;
	        }
	        if (_colors != null){
	            _colors._onBrushColorUpdated -= OnBrushColorUpdated_ForcePaintMode;
	        }
	        Projections_MaskPainter.Act_OnPaintStrokeEnd -= OnBrushStrokeEnd;
	        Inpaint_MaskPainter.Act_OnPaintStrokeEnd -= OnBrushStrokeEnd;
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

	        if (_brushSmudge_Toggle != null)
	            _brushSmudge_Toggle.transform.SetSiblingIndex(_brushAdd_Toggle.transform.GetSiblingIndex() + 1);

	        // Default gaps; Nomad ThemeDirectionTools packs flat squares tightly.
	        ApplyPaintSmudgeEraseGaps(this, nomadGaps: false);

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
	        const string kIconsPath = "Assets/_gm/Art/Icons/icon_smudge.png";
	        var spr = Resources.Load<Sprite>("icon_smudge");
	        if (spr != null) return spr;
	        Texture2D tex = Resources.Load<Texture2D>("icon_smudge");
	        if (tex != null)
	            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

	        #if UNITY_EDITOR
	        var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(kIconsPath);
	        if (obj != null) return obj;
	        var texEd = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(kIconsPath);
	        if (texEd != null)
	            return Sprite.Create(texEd, new Rect(0, 0, texEd.width, texEd.height), new Vector2(0.5f, 0.5f), 100f);
	        #endif

	        return null;
	    }
	}



	public class BrushRibbon_UI_Direction : MonoBehaviour{
	    /// <summary>Fired when paint / smudge / erase mode changes (after the active toggle updates).</summary>
	    public static event System.Action OnDirectionToggleChanged;

	    public static void RaiseDirectionToggleChanged() => OnDirectionToggleChanged?.Invoke();

	    [Space(10)]
	    [SerializeField] protected Toggle _brushErase_Toggle;
	    [SerializeField] protected Toggle _brushAdd_Toggle;
	    [SerializeField] protected Animation _anim;
	    protected Toggle _brushSmudge_Toggle;
    
	    public bool isPositive => _brushAdd_Toggle.isOn;
	    public bool isSmudge => _brushSmudge_Toggle != null && _brushSmudge_Toggle.isOn;

	    public Toggle PaintToggle => _brushAdd_Toggle;
	    public Toggle EraseToggle => _brushErase_Toggle;
	    public Toggle SmudgeToggle => _brushSmudge_Toggle;

	    /// <summary>
	    /// Stack Paint / Smudge / Erase as equal bands.
	    /// Nomad flat squares pack tight (hairline break) — wide gaps left a sparse black column.
	    /// Equal cell heights; gap is shared between neighbors (not subtracted from one side only).
	    /// </summary>
	    public static void ApplyPaintSmudgeEraseGaps(BrushRibbon_UI_Direction dir, bool nomadGaps) {
	        if (dir == null) return;
	        var paint = dir.PaintToggle;
	        var erase = dir.EraseToggle;
	        var smudge = dir.SmudgeToggle;
	        if (paint == null || erase == null) return;

	        var addRect = paint.transform as RectTransform;
	        var eraseRect = erase.transform as RectTransform;
	        var smudgeRect = smudge != null ? smudge.transform as RectTransform : null;
	        if (addRect == null || eraseRect == null) return;

	        // Snapshot only on Nomad apply so leave restores the default (post-smudge-inject) gaps.
	        if (nomadGaps) {
	            SpzUiThemeOps.SnapshotToolFaceLayout(addRect);
	            SpzUiThemeOps.SnapshotToolFaceLayout(eraseRect);
	            if (smudgeRect != null)
	                SpzUiThemeOps.SnapshotToolFaceLayout(smudgeRect);
	        }

	        // Nomad: hairline between squares (was 0.08 — left large black gutters). Builtin keeps authored spacing.
	        float gap = nomadGaps ? 0.015f : (smudgeRect != null ? 0.028f : 0.02f);
	        float left = addRect.anchorMin.x;
	        float right = addRect.anchorMax.x;

	        if (smudgeRect != null) {
	            // Three equal bands + two equal breaks between them.
	            float cell = (1f - 2f * gap) / 3f;
	            eraseRect.anchorMin = new Vector2(left, 0f);
	            eraseRect.anchorMax = new Vector2(right, cell);
	            eraseRect.offsetMin = Vector2.zero;
	            eraseRect.offsetMax = Vector2.zero;

	            smudgeRect.anchorMin = new Vector2(left, cell + gap);
	            smudgeRect.anchorMax = new Vector2(right, 2f * cell + gap);
	            smudgeRect.offsetMin = Vector2.zero;
	            smudgeRect.offsetMax = Vector2.zero;

	            addRect.anchorMin = new Vector2(left, 2f * cell + 2f * gap);
	            addRect.anchorMax = new Vector2(right, 1f);
	            addRect.offsetMin = Vector2.zero;
	            addRect.offsetMax = Vector2.zero;
	        }
	        else {
	            // Paint / Erase only (Gen3D): equal halves with a visible break.
	            float cell = (1f - gap) * 0.5f;
	            eraseRect.anchorMin = new Vector2(left, 0f);
	            eraseRect.anchorMax = new Vector2(right, cell);
	            eraseRect.offsetMin = Vector2.zero;
	            eraseRect.offsetMax = Vector2.zero;
	            addRect.anchorMin = new Vector2(left, cell + gap);
	            addRect.anchorMax = new Vector2(right, 1f);
	            addRect.offsetMin = Vector2.zero;
	            addRect.offsetMax = Vector2.zero;
	        }

	        var rootLayout = dir.GetComponent<LayoutElement>();
	        if (rootLayout != null && nomadGaps) {
	            // Snapshot so RestoreBoundChromeUnder can unwind; leave must not hardcode 210 over Restore.
	            SpzUiThemeOps.SnapshotLayoutElementForTheme(rootLayout);
	            if (smudgeRect != null) {
	                // Square cells like bucket/trash: each band height ≈ column width (not a tall 280px stack).
	                float colW = MeasureDirectionColumnWidth(dir);
	                float cellFrac = (1f - 2f * gap) / 3f;
	                float squareStackH = colW / Mathf.Max(0.05f, cellFrac);
	                rootLayout.minHeight = squareStackH;
	                rootLayout.preferredHeight = squareStackH;
	            }
	            else {
	                float colW = MeasureDirectionColumnWidth(dir);
	                float cellFrac = (1f - gap) * 0.5f;
	                float squareStackH = colW / Mathf.Max(0.05f, cellFrac);
	                rootLayout.minHeight = Mathf.Max(rootLayout.minHeight, squareStackH);
	                rootLayout.preferredHeight = squareStackH;
	            }
	        }
	    }

	    static float MeasureDirectionColumnWidth(BrushRibbon_UI_Direction dir) {
	        var rt = dir != null ? dir.transform as RectTransform : null;
	        if (rt != null && rt.rect.width > 4f)
	            return rt.rect.width;
	        // Inactive / EditMode: match typical brush-strip square chrome (~bucket/trash).
	        return 40f;
	    }

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
