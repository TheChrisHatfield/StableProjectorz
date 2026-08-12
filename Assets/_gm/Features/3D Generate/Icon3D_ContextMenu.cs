using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	//belongs to 'Icon3D_UI' element.

	public class Icon3D_ContextMenu : MonoBehaviour{

	    [SerializeField] GameObject _contextMenu_go; //holds all controls for the context menu.
	    [Space(10)]
	    [SerializeField] Button _exportMeshButton;
	    [SerializeField] Button _generateButton;
	    [SerializeField] TextMeshProUGUI _text;

	    float _confirmByTime;

	    public Action onGenerateButton;
	    public bool isShowing => _contextMenu_go.activeSelf;
    

	    public void Toggle(bool isOn){
	        _contextMenu_go.SetActive(isOn);
	        if (isOn)
	            ApplyThemeTokens();
	    }

	    void OnExportMeshButton(){
	        ModelsHandler_3D.instance.ExportModel();
	    }

	    void OnGenerateButton(){
	        if (Time.time > _confirmByTime){
	            _text.text = "ok?";
	            _confirmByTime = Time.time + 1.0f;
	        }else{
	            _text.text = "GEN";
	             onGenerateButton?.Invoke();
	        }
	    } 

	    void Update(){
	        if(Time.time > _confirmByTime){ _text.text = "GEN"; }
	    }

	    void Start(){
	        _exportMeshButton.onClick.AddListener(OnExportMeshButton);
	        _generateButton.onClick.AddListener(OnGenerateButton);
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	    }

	    void ApplyThemeTokens() {
	        if (_contextMenu_go != null)
	            SpzUiThemeOps.ApplyContextMenuChrome(_contextMenu_go);
	        else
	            SpzUiThemeOps.ApplyContextMenuChrome(gameObject);
	        // GEN / Export may sit outside the menu root — theme or restore each ownership control.
	        ThemeOrRestoreGenExportButton(_exportMeshButton);
	        ThemeOrRestoreGenExportButton(_generateButton);
	        if (_text == null)
	            return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(_text.transform);
	            return;
	        }
	        SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(_text, SpzUiThemeOps.Active.textPrimary, 14f);
	    }

	    static void ThemeOrRestoreGenExportButton(Button btn) {
	        if (btn == null) return;
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            SpzUiThemeOps.RestoreBoundChromeUnder(btn.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        // GEN/Export often use the drawn glyph as targetGraphic — SolidSquare blanks mid Gen3D.
	        if (btn.targetGraphic is Image face
	            && face.sprite != null
	            && face.preserveAspect
	            && !UiRuntimeSprites.IsSolidRect(face.sprite)) {
	            SpzUiThemeOps.ApplyBoundChromeIconTint(face, t.iconTint);
	        } else {
	            SpzUiThemeOps.ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
	        }
	        foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp != null)
	                // Compact Truncate clipped "Export Mesh" / confirmation text under Nomad.
	                SpzUiThemeOps.ApplyBoundChromeReadableBodyTmp(tmp, t.textPrimary, 14f);
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }
	}
}//end namespace
