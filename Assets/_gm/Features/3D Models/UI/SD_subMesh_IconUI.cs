using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

namespace spz {

	public class SD_subMesh_IconUI : MonoBehaviour{
	    [SerializeField] Image _background;
	    [SerializeField] Color _color_Selected;
	    [SerializeField] Color _color_NotSelected;
	    [SerializeField] Button _wholeIcon_button;
	    [Space(10)]
	    [SerializeField] TextMeshProUGUI _name;
	    [SerializeField] Button _rmvButton;

	    bool _destroyed = false;
	    bool _sendEvent_duringDestroy = true;//for example, if we are placeholder and have to be removed dur game start.
	    Color _authoredSelected;
	    Color _authoredNotSelected;
	    bool _authoredBgSnapshotted;

	    public SD_3D_Mesh myMesh { get; private set; } = null;
	    public bool isSelected => myMesh._isSelected;
	    public static System.Action<SD_subMesh_IconUI> Act_OnWillDestroy_Icon { get; set; }  = null;


	    public void Init( SD_3D_Mesh myMesh ){
	        this.myMesh = myMesh;
	        _name.text = myMesh.gameObject.name;
	        SnapshotAuthoredBgColors();
        
	        //doing it all here, because Start() might not be invoked until entire panel becomes active:
	        _wholeIcon_button.onClick.AddListener(OnWholeIcon_button);
	        _rmvButton.onClick.AddListener(OnRemoveButton);
        
	        SD_3D_Mesh.Act_OnWillDestroyMesh += OnWillDestroyMesh;
	        SD_3D_Mesh.Act_OnMeshSelected += OnSomeMesh_Selected;
	        SD_3D_Mesh.Act_OnMeshDeselected += OnSomeMesh_Deselected;
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();

	        OnSomeMesh_Selected(myMesh);
	    }

	    void SnapshotAuthoredBgColors() {
	        if (_authoredBgSnapshotted) return;
	        _authoredSelected = _color_Selected;
	        _authoredNotSelected = _color_NotSelected;
	        _authoredBgSnapshotted = true;
	    }

	    void ApplyThemeTokens() {
	        SnapshotAuthoredBgColors();
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            _color_Selected = _authoredSelected;
	            _color_NotSelected = _authoredNotSelected;
	            if (_wholeIcon_button != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_wholeIcon_button.transform);
	            if (_name != null)
	                SpzUiThemeOps.RestoreAuthoredGraphic(_name);
	            if (_rmvButton != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_rmvButton.transform);
	            if (_background != null && myMesh != null)
	                ToggleBG(myMesh._isSelected);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        _color_Selected = t.selection.a > 0.2f
	            ? new Color(t.selection.r, t.selection.g, t.selection.b, 1f)
	            : Color.Lerp(t.tabActive, t.accent, 0.55f);
	        _color_NotSelected = t.controlBg;
	        if (_background != null && myMesh != null)
	            ToggleBG(myMesh._isSelected);
	        // Mesh row select: name TMP loses raycasts under BoundChrome. Wire/Ensure a face on
	        // _wholeIcon_button without ApplyBoundChromeSelectable (ToggleBG owns selection fill).
	        if (_wholeIcon_button != null) {
	            if (_wholeIcon_button.targetGraphic == null && _background != null)
	                _wholeIcon_button.targetGraphic = _background;
	            SpzUiThemeOps.EnsureSelectableHitFace(_wholeIcon_button);
	            if (_wholeIcon_button.targetGraphic != null)
	                _wholeIcon_button.targetGraphic.raycastTarget = true;
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_wholeIcon_button);
	        }
	        if (_name != null)
	            SpzUiThemeOps.ApplyBoundChromeTmp(_name, t.textPrimary);
	        if (_rmvButton != null) {
	            SpzUiThemeOps.EnsureSelectableHitFace(_rmvButton);
	            // Trash glyph often IS the face — SolidSquare blanks remove under Nomad (mesh select litmus).
	            if (SpzUiThemeOps.IsAuthoredIconFace(_rmvButton.targetGraphic)) {
	                if (_rmvButton.targetGraphic is Image rmvFace)
	                    SpzUiThemeOps.ApplyBoundChromeIconTint(rmvFace, t.danger);
	            } else {
	                SpzUiThemeOps.ApplyBoundChromeSelectable(_rmvButton, t.controlBg, t.danger);
	            }
	            foreach (var tmp in _rmvButton.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) {
	                if (tmp != null)
	                    SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, t.textPrimary, 11f);
	            }
	            SpzUiThemeOps.ClearNonFaceRaycastsForTheme(_rmvButton);
	        }
	    }


	    public void DestroySelf(bool sendEvents=true){
	        if(_destroyed){ return; } 
	        _sendEvent_duringDestroy = sendEvents;
	        Cleanup();
	        if(this!=null && this.gameObject!=null){ DestroyImmediate(this.gameObject); }
	    }
    
	    void OnDestroy(){
	        if(_destroyed){ return; }
	        Cleanup();
	    }

	    void Cleanup(){
	        _destroyed = true;
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	        if (_sendEvent_duringDestroy){  Act_OnWillDestroy_Icon?.Invoke(this);  }
	        SD_3D_Mesh.Act_OnWillDestroyMesh -= OnWillDestroyMesh;
	        SD_3D_Mesh.Act_OnMeshSelected -= OnSomeMesh_Selected;
	        SD_3D_Mesh.Act_OnMeshDeselected -= OnSomeMesh_Deselected;
	    }


	    void OnWillDestroyMesh(SD_3D_Mesh mesh){
	        if(mesh != myMesh){ return; }
	        DestroySelf();
	    }

	    void OnRemoveButton(){
	        ConfirmPopup_UI.instance.Show("Remove this mesh? There is no CTRL+Z yet.", onYes, null);
	        void onYes(){
	            DestroySelf();
	        }
	    }


	    void OnWholeIcon_button(){
	        bool ctrlOrShift =  KeyMousePenInput.isKey_CtrlOrCommand_pressed() || KeyMousePenInput.isKey_Shift_pressed();
	        bool isSelect = true;
	        if(!myMesh._isSelected){  isSelect=true;  }
	        if(ctrlOrShift && myMesh._isSelected){  isSelect = false; }
        
	        bool isSucces;
	        myMesh.TryChange_SelectionStatus(isSelect, out isSucces, isDeselectOthers:ctrlOrShift==false);
	    }


	    void OnSomeMesh_Selected(SD_3D_Mesh mesh){
	        if(mesh == myMesh){  ToggleBG(true);  }
	    }


	    void OnSomeMesh_Deselected(SD_3D_Mesh mesh){
	        if(mesh == myMesh){  ToggleBG(false);  }
	    }

	    void ToggleBG(bool isEnable)
	        => _background.color =  isEnable ? _color_Selected : _color_NotSelected;
	}
}//end namespace
