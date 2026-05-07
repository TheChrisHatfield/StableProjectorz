using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// Other scripts can look at my size, to deduce the position and size
	// of other viewports, such as Depth or Content viewports.
	// Those might be disabled/inactive, but I am always active.
	//
	// Anchors are stretched vertically through the midline of this RectTransform.
	// The RectTransform is centered in the viewport.
	// It tells its aspect ratio fitter the aspect.
	// Aspect ratio fitter has "Fit in Parent" mode, so it will always be inside the main large viewport
	public class InnerViewport_SizeReference : MonoBehaviour{
	    public static InnerViewport_SizeReference instance { get; private set; }
	    [SerializeField] AspectRatioFitter _aspectFitter;
    
	    public RectTransform rectTransf => _myRectTransf;
	    [SerializeField] RectTransform _myRectTransf;

	    void EarlyUpdate(){
	        float aspect;
	        if (ViewportFullViewOnScreen_Driver.ShouldUseBetweenRibbonsMainViewportPlacement() && MainViewport_UI.instance != null) {
		        var r = MainViewport_UI.instance.mainViewportRect.rect;
		        aspect = r.height > 1e-4f ? r.width / r.height : 1f;
	        } else {
		        Vector2 sd_widthHeight = SD_InputPanel_UI.instance?.widthHeight() ?? new Vector2Int(512, 512);
		        aspect = sd_widthHeight.x / Mathf.Max(1e-4f, sd_widthHeight.y);
	        }
	        _aspectFitter.aspectRatio = aspect;
	    }

	    void Awake(){
	        if(instance != null){  DestroyImmediate(this); return; }
	        instance = this;
	        EarlyUpdate_callbacks_MGR.instance.onEarlyUpdate3 += EarlyUpdate;
	    }

	    private void OnDestroy(){
	        if (EarlyUpdate_callbacks_MGR.instance != null){
	            EarlyUpdate_callbacks_MGR.instance.onEarlyUpdate3 -= EarlyUpdate;
	        }
	    }
	}
}//end namespace
