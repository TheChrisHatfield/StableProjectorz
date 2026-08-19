using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	public class Icon3D_UI : MonoBehaviour{

	    [SerializeField] Icon3D_ContextMenu _contextMenu;
	    [SerializeField] MouseClickSensor_UI _wholeIcon_button;
	    [SerializeField] RawImage_with_aspect _icon;
	    [SerializeField] Image _stackSheets_lines;//enabled when our icon "carries" several textures at once, instead of a single texture.

	    //Icon from BG list that we "listen to". Assigned to us during OnAfterInstatiated().
	    IconUI _bgIcon_ref;

	    GenData2D _genData =>  _bgIcon_ref?._genData;

	    void OnMouseClick(int buttonIx){
	        if(buttonIx == 1){
	            _contextMenu.Toggle(!_contextMenu.isShowing);
	        }
	    }

	    public void OnAfterInstantiated(IconUI bgIcon_ref){
	        _bgIcon_ref = bgIcon_ref;
	        if (_genData == null) {
	            Viewport_StatusText.instance?.ShowStatusText("3D icon has no generation data — skipped.", false, 4f, false);
	            return;
	        }
	        _wholeIcon_button._onMouseClick += OnMouseClick;
	        _contextMenu.onGenerateButton += OnGenerateButton;
	        _genData.Subscribe_for_TextureUpdates(_bgIcon_ref.texture_guids, OnTextureUpdated);

	        bool is_img_stack =  _genData.use_many_icons == false  &&  _genData.n_total > 1;
	        _stackSheets_lines.gameObject.SetActive(is_img_stack);

	        GenData_TextureRef texRef = _genData.GetTexture_ref0();
	        OnTextureUpdated(texRef);
	    }


	    void OnGenerateButton(){
	        var sdHub = StableDiffusion_Hub.instance;
	        if (sdHub == null) {
	            Viewport_StatusText.instance?.ShowStatusText("Stable Diffusion not ready.", false, 4, true);
	            return;
	        }
	        if(sdHub._generating || Time.time < sdHub._generationCooldownUntil){
	            Viewport_StatusText.instance?.ShowStatusText("Cant generate 3D while StableDiffusion is making images", false, 6, true);
	            return; 
	        }
	        if (_genData == null) {
	            Viewport_StatusText.instance?.ShowStatusText("No generation data on this 3D icon.", false, 4, true);
	            return;
	        }
	        GenData_TextureRef texRef = _genData.GetTexture_ref0();
		if (texRef == null || _genData.textureGuidsOrdered == null || _genData.textureGuidsOrdered.Count < 1) {
	            Viewport_StatusText.instance?.ShowStatusText("Cant generate 3D — icon has no texture.", false, 6, true);
	            return;
	        }
	        if(texRef.texturePreference != TexturePreference.Tex2D || texRef.tex2D==null){
	            Viewport_StatusText.instance?.ShowStatusText("Cant generate 3D from a stacked-image. Must be a single texture.", false, 6, true);
	            return;
	        }
	        var inputs = UnityEngine.Object.FindObjectOfType<Gen3D_SingleImageInput_UI>(true);
	        var gen3d = Gen3D_MGR.instance;
	        if (inputs == null || gen3d == null) {
	            Viewport_StatusText.instance?.ShowStatusText("Gen 3D panel not ready — open Gen 3D first.", false, 5, true);
	            return;
	        }
	        // Gen3D slot takes ownership — feed a copy so the icon keeps its tex2D.
	        Texture2D copy = Instantiate(texRef.tex2D);
	        copy.name = texRef.tex2D.name + "_gen3d";
	        if (!inputs.TryAssignImageForGenerate(copy)) {
	            DestroyImmediate(copy);
	            Viewport_StatusText.instance?.ShowStatusText("Could not assign image to Gen 3D input.", false, 5, true);
	            return;
	        }
	        if (!gen3d.Trigger3DGeneration()) {
	            Viewport_StatusText.instance?.ShowStatusText("Could not start 3D generation (busy or not ready).", false, 5, true);
	        }
	    }

	    void OnTextureUpdated(GenData_TextureRef texRef){
	        _icon.ShowTexture_dontOwn( texRef.tex_by_preference(),  texRef.sliceIx,  isGenerated:false, 
	                                   CameraTexType.Unknown,  _genData.kind );
	    }

	    public void DestroySelf(){
	        // Capture refs first: _genData is derived from the BG icon, so clearing the icon first
	        // made unsubscribe a no-op and left OnTextureUpdated on a destroyed GO.
	        var gen = _genData;
	        var guids = _bgIcon_ref != null ? _bgIcon_ref.texture_guids : null;
	        gen?.Unsubscribe_from_textureUpdates(guids, OnTextureUpdated);
	        _bgIcon_ref = null;
	        Destroy(this.gameObject);
	    }
	}
}//end namespace
