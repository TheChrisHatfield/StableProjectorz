using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	//different UI sections (located in other scenes) can copy our rect transform values, to position themselves.
	public class Global_Skeleton_UI : MonoBehaviour{
	    public static Global_Skeleton_UI instance { get; private set; } = null;

	    [SerializeField] RectTransform _leftColumn_rTransf;
	    [SerializeField] RectTransform _mainViewport_rTransf;
	    [SerializeField] RectTransform _rightColumn_rTransf;

	    struct SideWidthSnapshot {
		    public float minWidth;
		    public float preferredWidth;
		    public float flexibleWidth;
	    }

	    SideWidthSnapshot _sceneLeftWidths;
	    SideWidthSnapshot _sceneRightWidths;
	    bool _capturedSceneWidths;
	    bool _leftPanelCollapsed;
	    bool _rightPanelCollapsed;

	    /// <summary>Same readiness as <see cref="SetSidePanelVisibility"/> (columns + <see cref="LayoutElement"/> + width snapshot).</summary>
	    public bool TryGetSidePanelVisibility(out bool leftVisible, out bool rightVisible) {
		    leftVisible = !_leftPanelCollapsed;
		    rightVisible = !_rightPanelCollapsed;
		    if (_leftColumn_rTransf == null || _rightColumn_rTransf == null) {
			    return false;
		    }
		    if (_leftColumn_rTransf.GetComponent<LayoutElement>() == null ||
		        _rightColumn_rTransf.GetComponent<LayoutElement>() == null) {
			    return false;
		    }
		    EnsureSceneWidthsCaptured();
		    return _capturedSceneWidths;
	    }

	    /// <summary>
	    /// Collapse side columns by zeroing horizontal <see cref="LayoutElement"/> widths on the skeleton
	    /// (tracked placeholders under the main horizontal layout). Restores scene widths from Awake snapshot.
	    /// </summary>
	    public bool SetSidePanelVisibility(bool leftVisible, bool rightVisible) {
		    if (_leftColumn_rTransf == null || _rightColumn_rTransf == null) {
			    return false;
		    }
		    EnsureSceneWidthsCaptured();
		    var leL = _leftColumn_rTransf.GetComponent<LayoutElement>();
		    var leR = _rightColumn_rTransf.GetComponent<LayoutElement>();
		    if (leL == null || leR == null) {
			    return false;
		    }
		    if (!_capturedSceneWidths) {
			    return false;
		    }

		    ApplySideWidth(leL, leftVisible, _sceneLeftWidths);
		    ApplySideWidth(leR, rightVisible, _sceneRightWidths);
		    _leftPanelCollapsed = !leftVisible;
		    _rightPanelCollapsed = !rightVisible;

		    var row = _leftColumn_rTransf.parent as RectTransform;
		    if (row != null) {
			    LayoutRebuilder.ForceRebuildLayoutImmediate(row);
		    }
		    return true;
	    }

	    /// <summary>After side panel width changes, run a second layout pass on the skeleton row, main viewport, and root canvas.
	    /// Improves responsive sizing when toggling (e.g. open right from fullscreen) so the paint column gets correct width/height.</summary>
	    public void ForceLayoutRefreshAfterPanelResize() {
		    if (_leftColumn_rTransf != null) {
			    var row = _leftColumn_rTransf.parent as RectTransform;
			    if (row != null) {
				    LayoutRebuilder.ForceRebuildLayoutImmediate(row);
			    }
		    }
		    if (_mainViewport_rTransf != null) {
			    LayoutRebuilder.ForceRebuildLayoutImmediate(_mainViewport_rTransf);
		    }
		    Canvas.ForceUpdateCanvases();
		    if (MainViewport_UI.instance != null) {
			    MainViewport_UI.instance.ReapplyInnerRibbonLayoutFromSettings();
		    }
	    }

	    void EnsureSceneWidthsCaptured() {
		    if (_capturedSceneWidths) {
			    return;
		    }
		    CaptureSceneWidths();
	    }

	    void CaptureSceneWidths() {
		    if (_leftColumn_rTransf == null || _rightColumn_rTransf == null) {
			    return;
		    }
		    var leL = _leftColumn_rTransf.GetComponent<LayoutElement>();
		    var leR = _rightColumn_rTransf.GetComponent<LayoutElement>();
		    if (leL == null || leR == null) {
			    return;
		    }
		    _sceneLeftWidths = new SideWidthSnapshot {
			    minWidth = leL.minWidth,
			    preferredWidth = leL.preferredWidth,
			    flexibleWidth = leL.flexibleWidth,
		    };
		    _sceneRightWidths = new SideWidthSnapshot {
			    minWidth = leR.minWidth,
			    preferredWidth = leR.preferredWidth,
			    flexibleWidth = leR.flexibleWidth,
		    };
		    _capturedSceneWidths = true;
	    }

	    static void ApplySideWidth(LayoutElement le, bool visible, SideWidthSnapshot scene) {
		    if (visible) {
			    le.minWidth = scene.minWidth;
			    le.preferredWidth = scene.preferredWidth;
			    le.flexibleWidth = scene.flexibleWidth;
		    }
		    else {
			    le.minWidth = 0f;
			    le.preferredWidth = 0f;
			    le.flexibleWidth = 0f;
		    }
	    }

	    public void Place_onto_LeftColumn(RectTransform place_me){
	        place_me.CopyValsFrom(_leftColumn_rTransf);
	    }
	    public void Place_onto_MainViewport(RectTransform place_me){
	        place_me.CopyValsFrom(_mainViewport_rTransf);
	    }

	    public void Place_onto_MainViewport_between_ribbons(RectTransform place_me){
	        if(MainViewport_UI.instance == null){ return; }

	        // Cache original state
	        Transform originalParent = place_me.parent;
	        int originalSiblingIndex = place_me.GetSiblingIndex();

	        // 1. Temporarily parent to the target to inherit its coordinate space
	        // false = reset local position/rotation/scale to match target immediately
	        place_me.SetParent(MainViewport_UI.instance.mainViewportRect, false);

	        // 2. Force stretch to corners (fill the target completely)
	        place_me.anchorMin = Vector2.zero;
	        place_me.anchorMax = Vector2.one;
	        place_me.offsetMin = Vector2.zero;
	        place_me.offsetMax = Vector2.zero;

	        // 3. Return to original parent
	        // true = Unity will recalculate anchors/offsets to maintain the visual position we just set
	        place_me.SetParent(originalParent, true);
	        place_me.SetSiblingIndex(originalSiblingIndex);
	    }

	    public void Place_onto_RightColumn(RectTransform place_me){
	        place_me.CopyValsFrom(_rightColumn_rTransf);
	    }


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        CaptureSceneWidths();
	    }
	}
}//end namespace
