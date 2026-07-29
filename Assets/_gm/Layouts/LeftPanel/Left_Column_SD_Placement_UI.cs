using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace spz {

	// positions the rect-transform on top of the Left-Column of the global UI-skeleton.
	// For the panel that holds ui elements of Stable-Diffusion.
	// Also fades this panel depending on the Mode we are in (preview-UV, StableDiffusion, 3D).
	//
	// There is also a very similar script, `Left_Column_3D_Placement_UI`
	public class Left_Column_SD_Placement_UI : MonoBehaviour
	{
	    [SerializeField] RectTransform _place_me;
	    [SerializeField] CanvasGroup _canvGrp;
	    [SerializeField] float _fadeSpeed = 5;

	    /// <summary>Rect mirrored onto the skeleton left column — used to hide the whole SD strip in viewport full view.</summary>
	    public RectTransform MirroredColumnRoot => _place_me;

	    void Update(){
	        if (ViewportFullViewOnScreen_Driver.ShouldHideMirroredLeftColumnContent()) {
	            UiCanvasGroupModeStrip.Tick(_canvGrp, show: false, _fadeSpeed);
	            return;
	        }
	        DimensionMode currMode = DimensionMode_MGR.instance._dimensionMode;
	        bool show = currMode == DimensionMode.dim_uv || currMode == DimensionMode.dim_sd;
	        if (show)
	            Global_Skeleton_UI.instance?.Place_onto_LeftColumn( _place_me );
	        UiCanvasGroupModeStrip.Tick(_canvGrp, show, _fadeSpeed);
	    }
	}
}//end namespace
