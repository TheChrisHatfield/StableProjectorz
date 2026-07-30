using System.Collections;
using System.Collections.Generic;
using System.Web;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	public class SD_PromptWord_WebFind : MonoBehaviour{

	    [SerializeField] Button _button;
	    [SerializeField] Image _image;
	    [SerializeField] Sprite _inactiveSprite;
	    [SerializeField] Sprite _activeSprite;
	    [Space(10)]
	    [SerializeField] TMP_InputField _myPrompt;

	    string _latestSelected_text = "";

	    void OnButtonClicked(){
	        if(_latestSelected_text == ""){ return; }
	        string encodedQuery = _latestSelected_text.Replace(" ", "+");
	        string url = $"https://www.google.com/search?q={encodedQuery}&tbm=isch";
	        Application.OpenURL(url);
	    }

	    void Update(){
	        string highlighted = GetHighlightedText();
	        if (SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            // Nomad uses MonolithLineIcon Globe — do not swap authored globe sprites over it.
	            // Gen3D may theme a frame later than first Update: if Monolith is not wired yet,
	            // keep authored active/inactive sprites so the control is not a dead mute glyph.
	            var line = SpzUiThemeOps.FindDirectChildIncludingInactive(transform, "MonolithLineIcon");
	            if (line != null) {
	                var img = line.GetComponent<Image>();
	                if (img != null) {
	                    SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(img);
	                    Color c = SpzUiThemeOps.Active.iconTint;
	                    if (string.IsNullOrEmpty(highlighted))
	                        c = Color.Lerp(c, SpzUiThemeOps.Active.textMuted, 0.45f);
	                    img.color = c;
	                }
	            } else if (_image != null) {
	                _image.sprite = highlighted != "" ? _activeSprite : _inactiveSprite;
	            }
	            _latestSelected_text = highlighted != "" ? highlighted : _latestSelected_text;
	            return;
	        }
	        // Leave Nomad: unwind Monolith tint so authored globe sprites own the cue again.
	        var monolith = SpzUiThemeOps.FindDirectChildIncludingInactive(transform, "MonolithLineIcon");
	        if (monolith != null) {
	            var mImg = monolith.GetComponent<Image>();
	            if (mImg != null)
	                SpzUiThemeOps.RestoreAuthoredGraphic(mImg);
	        }
	        _image.sprite =  highlighted!=""? _activeSprite : _inactiveSprite;

	        // Only update the latest-selected text, if highlighted isn't "".
	        // UI is polled at different framerate than Update. 
	        // Therefore wa always want to "remember" the latest-selected-text just in case:
	        _latestSelected_text =  highlighted!="" ? highlighted : _latestSelected_text;
	    }

	    string GetHighlightedText(){
	        if(!_myPrompt.isFocused){ return ""; }
	        int selectionStart = _myPrompt.selectionStringAnchorPosition;
	        int selectionEnd   = _myPrompt.selectionStringFocusPosition;

	        // Ensure selectionStart is always the lower index
	        if (selectionStart > selectionEnd){
	            int temp = selectionStart;
	            selectionStart = selectionEnd; 
	            selectionEnd = temp;
	        }
	        // Check if there's any text selected
	        if (selectionStart == selectionEnd){ return ""; }
        
	        string txt = _myPrompt.text.Substring(selectionStart, selectionEnd-selectionStart);
	        return StableDiffusion_Prompts_UI.StripColorTags( txt );
	    }


	    void Awake(){
	        _button.onClick.AddListener( OnButtonClicked );
	        _image.sprite = _inactiveSprite;
	    }

	}
}//end namespace
