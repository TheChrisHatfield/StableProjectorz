using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// Can notify subscribers when some geometry is clicked.
	// Uses a special ID texture for that. (texture has size of viewport, contains ids of objects).
	public class ClickSelect_Meshes_MGR : MonoBehaviour{
	    public static ClickSelect_Meshes_MGR instance { get; private set; } = null;

    
	    bool _manuallyEnabled = false; //when user clicked on the toggle.

	    // Hover hysteresis: a candidate mesh must be picked for this many *consecutive frames* before
	    // we actually switch manipulation focus. Removes single-frame jitter when the cursor sits on
	    // a silhouette edge between two close-up meshes.
	    // ROLLBACK NOTE: set _hoverFocusStableFramesNeeded = 1 to restore the prior "switch immediately" behavior.
	    const int _hoverFocusStableFramesNeeded = 3;
	    SD_3D_Mesh _pendingFocusCandidate;
	    int _pendingFocusStableFrames;

	    public bool _isSelectMode { get; private set; } = false;
	    public static Action<SD_3D_Mesh> _Act_OnClickedMesh { get; set; } = null;


	    ClickSelectMeshes_Toggle_UI get_selectMode_toggle()
	        => EventsBinder.FindComponent<ClickSelectMeshes_Toggle_UI>( nameof(ClickSelectMeshes_Toggle_UI) );
    /// <summary>Ray from the current view camera through the main-viewport cursor. Returns selected mesh + precise world hit point on Geometry colliders.</summary>
    public static bool TryRaycastSelectedMeshUnderMainViewport(View_UserCamera vCam, out SD_3D_Mesh meshOut, out Vector3 hitPointWorld) {
		    meshOut = null;
		    hitPointWorld = default;
		    if (ModelsHandler_3D.instance == null || MainViewport_UI.instance == null) {
			    return false;
		    }
		    if (vCam == null || vCam.myCamera == null) {
			    return false;
		    }
		    Vector2 uv = MainViewport_UI.instance.cursorMainViewportPos01;
		    // Render-matched ray: uses the same FOV-expanded + pin-shifted projection the camera renders
		    // with, so the ray goes through the pixel actually under the cursor (raw ViewportPointToRay
		    // was offset in multi-view / widescreen viewports -- the "imprecise targeting" symptom).
		    var ray = vCam.ViewportPointToRay_RenderMatched(uv);
		    int mask = LayerMask.GetMask("Geometry");
		    if (!Physics.Raycast(ray, out RaycastHit hit, 1e5f, mask, QueryTriggerInteraction.Ignore)) {
			    return false;
		    }
		    var sm = hit.collider != null ? hit.collider.GetComponent<SD_3D_Mesh>() : null;
		    if (sm == null || !sm._isSelected) {
			    return false;
		    }
		    meshOut = sm;
		    hitPointWorld = hit.point;
		    return true;
    }

    /// <summary>Compatibility overload when callers only need the selected mesh.</summary>
    public static bool TryRaycastSelectedMeshUnderMainViewport(View_UserCamera vCam, out SD_3D_Mesh meshOut) {
		    return TryRaycastSelectedMeshUnderMainViewport(vCam, out meshOut, out _);
    }

    /// <summary>
    /// Camera-independent: identifies the selected mesh under the cursor by sampling the composite mesh-ID
    /// render texture (written by every active view camera in <see cref="UserCameras_MGR.OnUpdate_ViewCams_depth_ids_Render"/>).
    /// Use this in multi-view where a single-camera physics raycast cannot reach a mesh that's only visible
    /// through a *different* sub-view camera's frustum.
    ///
    /// Internally a small NxN window around the cursor is sampled (default radius = 2 px) to absorb single-
    /// pixel ID-buffer flicker on mesh edges -- without that, the cursor sliding across a silhouette would
    /// briefly read background (id=0) and a downstream snap-back path could re-pick the wrong mesh.
    /// The dominant non-zero ID weighted by inverse-distance from the cursor wins.
    /// </summary>
    public static bool TryPickSelectedMeshUnderCursor_byIdBuffer(out SD_3D_Mesh meshOut, int searchRadiusPx = 2) {
		    meshOut = null;
		    if (ModelsHandler_3D.instance == null || MainViewport_UI.instance == null) { return false; }
		    if (UserCameras_MGR.instance == null || UserCameras_MGR.instance.camTextures == null) { return false; }
		    RenderTexture id_tex = UserCameras_MGR.instance.camTextures._viewCam_meshIDs_ref;
		    if (id_tex == null) { return false; }

		    Vector2 uv = MainViewport_UI.instance.cursorMainViewportPos01;
		    // Floor, not Round: pixel index = floor(uv * size). This keeps hover focus sampling the EXACT
		    // same texel that SampleMeshIdAtViewport01_static reads on click (ReadPixels truncates), so what
		    // the hover targets is always what a click would select -- rounding could disagree by 1px on edges.
		    int cx = Mathf.Clamp(Mathf.FloorToInt(uv.x * id_tex.width),  0, id_tex.width  - 1);
		    int cy = Mathf.Clamp(Mathf.FloorToInt(uv.y * id_tex.height), 0, id_tex.height - 1);

		    // PRECISION FIRST: sample the exact pixel under the cursor. When the cursor is unambiguously
		    // over a mesh (the common case, especially when zoomed in close where two meshes overlap on
		    // screen), this is the only sample we need. The previous NxN+weighted-average would let
		    // neighboring meshes "win" purely by occupying more pixels in the window -- exactly the
		    // imprecision symptom (cursor on character 2, but character 1 takes more of the 7x7 window
		    // because it's larger or closer to the camera, so focus snapped to character 1).
		    //
		    // ROLLBACK NOTE: remove this block to restore the prior "always NxN weighted vote" behavior.
		    Texture2D centerTex = new Texture2D(1, 1, TextureFormat.RG16, false);
		    RenderTexture prevC = RenderTexture.active;
		    RenderTexture.active = id_tex;
		    centerTex.ReadPixels(new Rect(cx, cy, 1, 1), 0, 0);
		    centerTex.Apply();
		    RenderTexture.active = prevC;
		    ushort centerId = SD_3D_Mesh_UniqueIDMaker.DecodeID_fromColor(centerTex.GetPixel(0, 0));
		    Destroy(centerTex);
		    if (centerId != 0) {
			    var mc = ModelsHandler_3D.instance.getMesh_byUniqueID(centerId);
			    if (mc != null && mc._isSelected) {
				    meshOut = mc;
				    return true;
			    }
			    // Center pixel is a real mesh that is just not selected (an occluder / neighbor).
			    // Do NOT run the window vote here: it let a selected mesh 1-2 px away "win" focus even
			    // though the cursor is visibly on a different object -- mis-targeting when meshes are
			    // close together. The window fallback is for EMPTY center pixels only (silhouette/AA edge).
			    return false;
		    }

		    // Fallback: nearest non-zero ID texel ONLY when the center pixel is empty (silhouette /
		    // AA edge). Radius 1 in multi-view so a larger close neighbor cannot steal the pick.
		    int edgeR = (UserCameras_MGR.instance != null && UserCameras_MGR.instance.numActiveViewCameras() > 1)
			    ? 1 : searchRadiusPx;
		    ushort bestId = SampleDominantMeshIdNearViewport01(viewportUv01: uv, edgeR);
		    if (bestId == 0) { return false; }
		    var m = ModelsHandler_3D.instance.getMesh_byUniqueID(bestId);
		    if (m == null || !m._isSelected) { return false; }
		    meshOut = m;
		    return true;
    }

    /// <summary>
    /// Non-zero mesh ID nearest the cursor texel (true nearest neighbor in a small window).
    /// Prefer this over area-weighted voting when assets sit close — a larger neighbor used to
    /// win the vote even when the cursor was on a thinner silhouette.
    /// </summary>
    public static ushort SampleDominantMeshIdNearViewport01(Vector2 viewportUv01, int searchRadiusPx) {
		    if (UserCameras_MGR.instance == null || UserCameras_MGR.instance.camTextures == null) { return 0; }
		    RenderTexture id_tex = UserCameras_MGR.instance.camTextures._viewCam_meshIDs_ref;
		    if (id_tex == null) { return 0; }
		    int cx = Mathf.Clamp(Mathf.FloorToInt(viewportUv01.x * id_tex.width),  0, id_tex.width  - 1);
		    int cy = Mathf.Clamp(Mathf.FloorToInt(viewportUv01.y * id_tex.height), 0, id_tex.height - 1);

		    int r = Mathf.Clamp(searchRadiusPx, 0, 16);
		    if (r <= 0) { return 0; }
		    int x0 = Mathf.Max(0, cx - r);
		    int y0 = Mathf.Max(0, cy - r);
		    int x1 = Mathf.Min(id_tex.width  - 1, cx + r);
		    int y1 = Mathf.Min(id_tex.height - 1, cy + r);
		    int w = x1 - x0 + 1;
		    int h = y1 - y0 + 1;
		    if (w <= 0 || h <= 0) { return 0; }

		    Texture2D tex = new Texture2D(w, h, TextureFormat.RG16, false);
		    RenderTexture prev = RenderTexture.active;
		    RenderTexture.active = id_tex;
		    tex.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
		    tex.Apply();
		    RenderTexture.active = prev;

		    ushort bestId = 0;
		    float bestSqr = float.MaxValue;
		    for (int yy = 0; yy < h; ++yy) {
			    for (int xx = 0; xx < w; ++xx) {
				    ushort id = SD_3D_Mesh_UniqueIDMaker.DecodeID_fromColor(tex.GetPixel(xx, yy));
				    if (id == 0) { continue; }
				    float dx = (x0 + xx) - cx;
				    float dy = (y0 + yy) - cy;
				    float sqr = dx * dx + dy * dy;
				    if (sqr >= bestSqr) { continue; }
				    bestSqr = sqr;
				    bestId = id;
			    }
		    }
		    Destroy(tex);
		    return bestId;
    }

    /// <summary>
    /// "Directional" multi-camera mesh pick: casts a render-matched ray from active sub-view cameras
    /// through the shared cursor viewport position and returns a hit on a *selected* mesh's <c>Geometry</c>
    /// collider. This is the right tool when the ID buffer is ambiguous (cursor on edge / background) but
    /// the cursor is genuinely over a mesh visible through one of the sub-view cameras.
    /// In single-view it degenerates to the OG one-camera raycast.
    ///
    /// Priority: the camera whose PIN is nearest the cursor is tried FIRST and wins outright if it hits --
    /// that's the sub-view the user is visually working in (same ownership rule navigation uses, see
    /// <see cref="UserCameras_MGR.NearestToCursor"/>). In multi-view, other cameras are NOT tried: the same
    /// UV through a different pin shift hits a neighboring asset when figures sit close on screen.
    /// Single-view may still fall through remaining cameras for robustness.
    /// </summary>
    public static bool TryRaycastSelectedMeshUnder_AnyActiveViewCamera(out SD_3D_Mesh meshOut, out Vector3 hitPointWorld) {
		    return TryRaycastMeshUnder_AnyActiveViewCamera(requireSelected: true, out meshOut, out hitPointWorld);
    }

    /// <summary>
    /// Same pin-priority multi-camera pick, generalized: <paramref name="requireSelected"/> false accepts
    /// any VISIBLE mesh (renderer on). Used by click selection as a last resort so thin silhouettes /
    /// tiny sub-view objects that the non-MSAA ID buffer misses can still be clicked. Hidden meshes keep
    /// their colliders, so visibility is checked explicitly -- a background click must not toggle an
    /// invisible mesh.
    /// </summary>
    public static bool TryRaycastMeshUnder_AnyActiveViewCamera(bool requireSelected, out SD_3D_Mesh meshOut, out Vector3 hitPointWorld) {
		    meshOut = null;
		    hitPointWorld = default;
		    if (UserCameras_MGR.instance == null || MainViewport_UI.instance == null) { return false; }
		    Vector2 uv = MainViewport_UI.instance.cursorMainViewportPos01;
		    int mask = LayerMask.GetMask("Geometry");
		    bool isMultiView = UserCameras_MGR.instance.numActiveViewCameras() > 1;

		    // Tier 1: the sub-view that "owns" the cursor (nearest pin / perspective-center Voronoi).
		    View_UserCamera nearestCam = UserCameras_MGR.instance.NearestToCursor();
		    if (TryRaycastMesh_throughCamera(nearestCam, uv, mask, requireSelected, out var nearestMesh, out var nearestPt)) {
			    meshOut = nearestMesh;
			    hitPointWorld = nearestPt;
			    return true;
		    }

		    // Tier 2 (single-view only): remaining cameras. Multi-view must not borrow other columns —
		    // close-together assets were selected from the wrong figure via another cam's ray.
		    if (!MultiviewPinLayoutRules.MeshPickMayUseOtherViewCameras(isMultiView)) {
			    return false;
		    }

		    int n = UserCameras_MGR.instance.GetViewCameraCount();
		    float bestDist = float.MaxValue;
		    SD_3D_Mesh bestMesh = null;
		    Vector3 bestPt = default;
		    for (int i = 0; i < n; ++i) {
			    var vc = UserCameras_MGR.instance.GetViewCamera(i);
			    if (vc == null || vc == nearestCam) { continue; }
			    if (!TryRaycastMesh_throughCamera(vc, uv, mask, requireSelected, out var sm, out var pt)) { continue; }
			    float d = (pt - vc.transform.position).sqrMagnitude;
			    if (d >= bestDist) { continue; }
			    bestDist = d;
			    bestMesh = sm;
			    bestPt = pt;
		    }
		    if (bestMesh == null) { return false; }
		    meshOut = bestMesh;
		    hitPointWorld = bestPt;
		    return true;
    }

    /// <summary>One camera's render-matched cursor ray vs mesh Geometry colliders (selected-only or any visible).</summary>
    static bool TryRaycastMesh_throughCamera(View_UserCamera vc, Vector2 uv, int mask, bool requireSelected,
                                             out SD_3D_Mesh meshOut, out Vector3 hitPointWorld) {
		    meshOut = null;
		    hitPointWorld = default;
		    if (vc == null || !vc.gameObject.activeInHierarchy || vc.myCamera == null) { return false; }
		    var ray = vc.ViewportPointToRay_RenderMatched(uv);
		    if (!Physics.Raycast(ray, out RaycastHit hh, 1e5f, mask, QueryTriggerInteraction.Ignore)) { return false; }
		    var sm = hh.collider != null ? hh.collider.GetComponent<SD_3D_Mesh>() : null;
		    if (sm == null) { return false; }
		    if (requireSelected && !sm._isSelected) { return false; }
		    if (!requireSelected && !sm._isVisible) { return false; }
		    meshOut = sm;
		    hitPointWorld = hh.point;
		    return true;
    }

    /// <summary>
    /// Identifies the selected mesh under the cursor (ID-buffer first, then multi-camera raycast)
    /// AND derives a precise world point on that mesh under the cursor (not mesh bounds center —
    /// that fallback made multi-view orbit/pan feel "offset" from the mouse).
    /// </summary>
    public static bool TryPickSelectedMeshAndPoint(View_UserCamera vCam, out SD_3D_Mesh meshOut, out Vector3 hitPointWorld) {
		    meshOut = null;
		    hitPointWorld = default;
		    bool gotMesh = TryPickSelectedMeshUnderCursor_byIdBuffer(out meshOut) && meshOut != null;
		    if (!gotMesh) {
			    if (!TryRaycastSelectedMeshUnder_AnyActiveViewCamera(out meshOut, out hitPointWorld) || meshOut == null) {
				    return false;
			    }
			    return true;
		    }
		    if (TryResolveWorldPointOnMeshUnderCursor(meshOut, vCam, out hitPointWorld)) {
			    return true;
		    }
		    hitPointWorld = meshOut.bounds.center;
		    return true;
    }

    /// <summary>
    /// World point on <paramref name="mesh"/> under the main-viewport cursor: render-matched rays
    /// (owning / nearest camera first), then closest-point on the collider along that ray so drag
    /// pivots stay locked to where the user is pointing instead of snapping to bounds.center.
    /// Pass <paramref name="owningCameraOnly"/> for multi-view nav (MMB pan/dolly): other cameras'
    /// rays through the same UV hit a *different* asset's surface and feel like an offset pivot.
    /// </summary>
    public static bool TryResolveWorldPointOnMeshUnderCursor(SD_3D_Mesh mesh, View_UserCamera preferCam, out Vector3 hitPointWorld, bool owningCameraOnly = false) {
		    hitPointWorld = default;
		    if (mesh == null || MainViewport_UI.instance == null) { return false; }
		    Vector2 uv = MainViewport_UI.instance.cursorMainViewportPos01;
		    var col = mesh.GetComponent<Collider>();
		    if (col == null) { return false; }

		    if (TryColliderRayHit(preferCam, col, uv, out hitPointWorld)) { return true; }
		    if (!owningCameraOnly) {
			    View_UserCamera nearest = UserCameras_MGR.instance != null ? UserCameras_MGR.instance.NearestToCursor() : null;
			    if (nearest != preferCam && TryColliderRayHit(nearest, col, uv, out hitPointWorld)) { return true; }
			    if (UserCameras_MGR.instance != null) {
				    int n = UserCameras_MGR.instance.GetViewCameraCount();
				    for (int i = 0; i < n; ++i) {
					    var v = UserCameras_MGR.instance.GetViewCamera(i);
					    if (v == null || v == preferCam || v == nearest) { continue; }
					    if (TryColliderRayHit(v, col, uv, out hitPointWorld)) { return true; }
				    }
			    }
		    }
		    // Soft lock: project along the owning camera's ray to the closest point on the collider.
		    View_UserCamera softCam = preferCam;
		    if (softCam == null && !owningCameraOnly) {
			    softCam = UserCameras_MGR.instance != null ? UserCameras_MGR.instance.NearestToCursor() : null;
		    }
		    if (softCam != null && softCam.myCamera != null && softCam.gameObject.activeInHierarchy) {
			    var ray = softCam.ViewportPointToRay_RenderMatched(uv);
			    float guessDist = Vector3.Distance(softCam.transform.position, mesh.bounds.center);
			    guessDist = Mathf.Max(0.05f, guessDist);
			    Vector3 guess = ray.GetPoint(guessDist);
			    hitPointWorld = col.ClosestPoint(guess);
			    return true;
		    }
		    return false;
    }

    static bool TryColliderRayHit(View_UserCamera vc, Collider col, Vector2 uv, out Vector3 hitPointWorld) {
		    hitPointWorld = default;
		    if (vc == null || !vc.gameObject.activeInHierarchy || vc.myCamera == null || col == null) { return false; }
		    var ray = vc.ViewportPointToRay_RenderMatched(uv);
		    if (!col.Raycast(ray, out RaycastHit hh, 1e5f)) { return false; }
		    hitPointWorld = hh.point;
		    return true;
    }


	    void OnUpdate(){
	        bool isSelectMode, allowClicks;
	        allow_or_not(out isSelectMode, out allowClicks);
        
	        // Only update UI if the state actually changed, to avoid spamming the component
	        if (_isSelectMode != isSelectMode){
	            _isSelectMode = isSelectMode;
	            if (!_manuallyEnabled){
	                get_selectMode_toggle()?.SetIsOnWithoutNotify(_isSelectMode);
	            }
	        }

	        UpdateManipulationFocusFromViewportRay();

	        if(!allowClicks){ return; }
	        Click_maybe();
	    }

	    /// <summary>When several submeshes are selected, move the manipulation target to the one under the cursor, so rotation/transform tools can follow the mesh the user is pointing at.</summary>
	    void UpdateManipulationFocusFromViewportRay() {
		    if (ModelsHandler_3D.instance == null) { return; }
		    var sel = ModelsHandler_3D.instance.selectedMeshes;
		    if (sel == null || sel.Count <= 1) { return; }
		    if (MainViewport_UI.instance == null || !MainViewport_UI.instance.isCursorHoveringMe()) { return; }
		    if (DimensionMode_MGR.instance == null || !DimensionMode_MGR.instance.is_3d_navigation_allowed) { return; }
		    if (KeyMousePenInput.isKey_alt_pressed()) { return; } // camera orbit: don't steal target
		    if (KeyMousePenInput.isSomeInputFieldActive()) { return; }

		    // Two-tier picker, in priority order:
		    //   A) ID-buffer with a small NxN search window around the cursor (camera-independent;
		    //      composite of every sub-view's mesh IDs; the same source Click_maybe uses for click
		    //      selection -- so hover focus stays in agreement with what a click would select).
		    //   B) "Directional" multi-camera physics raycast (render-matched rays): the sub-view whose
		    //      pin is nearest the cursor is tried first; only if it misses do the other cameras vote.
		    //      This recovers the cases where the ID buffer briefly returns 0 on a silhouette edge,
		    //      but the cursor is genuinely over a mesh visible through another sub-view's frustum.
		    //
		    // STICKY: if neither tier finds a selected mesh (cursor on background / between meshes),
		    // KEEP the existing focus. Do NOT fall back to a single-camera (_curr_viewCamera) raycast --
		    // that path is what was snapping focus back to character 1 in multi-view.
		    // ROLLBACK NOTE: previously a third fallback called TryRaycastSelectedMeshUnderMainViewport
		    // through _curr_viewCamera (and earlier through NearestToCursor), which always defaulted to
		    // camera 0 in multi-view; restore those lines below if the sticky behavior ever feels
		    // wrong in single-view (note: in single-view tier B already degenerates to that same call).
		    SD_3D_Mesh candidate = null;
		    if (TryPickSelectedMeshUnderCursor_byIdBuffer(out var smId) && smId != null) {
			    candidate = smId;
		    } else if (TryRaycastSelectedMeshUnder_AnyActiveViewCamera(out var smRay, out _) && smRay != null) {
			    candidate = smRay;
		    }
		    if (candidate == null) {
			    // intentionally no-op: cursor over background / no selected mesh -> keep existing focus.
			    _pendingFocusCandidate = null;
			    _pendingFocusStableFrames = 0;
			    return;
		    }
		    var current = ModelsHandler_3D.instance.GetManipulationTargetMesh();
		    if (candidate == current) {
			    _pendingFocusCandidate = null;
			    _pendingFocusStableFrames = 0;
			    return;
		    }
		    if (candidate == _pendingFocusCandidate) {
			    _pendingFocusStableFrames++;
		    } else {
			    _pendingFocusCandidate = candidate;
			    _pendingFocusStableFrames = 1;
		    }
		    if (_pendingFocusStableFrames >= _hoverFocusStableFramesNeeded) {
			    ModelsHandler_3D.instance.SetManipulationFocusMesh(candidate);
			    _pendingFocusCandidate = null;
			    _pendingFocusStableFrames = 0;
		    }
	    }


	    void allow_or_not(out bool isSelectMode_, out bool isAllowClicks_){
	        isSelectMode_  = true;
	        isAllowClicks_ = true;

	        // maybe disallow ctrl, but only if not pressing Ctrl+A or Ctrl+D, which we need to take care of.
	        bool allow_ctrl  = Settings_MGR.instance.get_ignoreCtrl_if_clickSelectingMeshes()==false
	                            || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D);
	        // Inpaint color workflows: Ctrl alone used to force mesh-select mode and blocked LMB painting + made eyedropper
	        // ignore the viewport (see MaskPainter / BrushRibbon_UI_EyeDropperTool). Swatch picks had no Ctrl, so they "worked".
	        // Here we only enable Ctrl+click selection when the user turns on the mesh-select toggle, or outside these modes.
	        bool inpaintColorWorkflow = WorkflowRibbon_UI.instance != null
	                                    && (WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_Color
	                                        || WorkflowRibbon_UI.instance.currentMode() == WorkflowRibbon_CurrMode.Inpaint_NoColor);
	        bool ctrlForMeshSelect = KeyMousePenInput.isKey_CtrlOrCommand_pressed() && allow_ctrl && !inpaintColorWorkflow;
        
	        //allow if manually enabled because we want to orbit around.
	        isSelectMode_  =  !KeyMousePenInput.isKey_alt_pressed();
	        isSelectMode_ &=  !KeyMousePenInput.isMMBpressed();
	        isSelectMode_ &=   ctrlForMeshSelect;
	        isSelectMode_ |=  _manuallyEnabled;

	        isAllowClicks_ = !KeyMousePenInput.isKey_alt_pressed();
	        isAllowClicks_ &= isSelectMode_;
	    }


	    void Click_maybe(){
	        if(KeyMousePenInput.isLMBpressedThisFrame()){ 
	            Vector2 viewportPos = MainViewport_UI.instance.cursorMainViewportPos01;
	            bool isMultiView = UserCameras_MGR.instance != null
	                               && UserCameras_MGR.instance.numActiveViewCameras() > 1;
	            // Exact texel under the cursor first — required when assets sit close in multi-view.
	            ushort id = SampleMeshId(viewportPos);
	            SD_3D_Mesh mesh = id != 0 ? ModelsHandler_3D.instance.getMesh_byUniqueID(id) : null;

	            // Owning-column ray before any edge window: window / other-cam fallbacks stole the
	            // neighbor when silhouettes nearly touch (see multi-view close-asset pick).
	            if (mesh == null && UserCameras_MGR.instance != null) {
		            View_UserCamera own = UserCameras_MGR.instance.NearestToCursor();
		            int mask = LayerMask.GetMask("Geometry");
		            if (TryRaycastMesh_throughCamera(own, viewportPos, mask, requireSelected: false, out mesh, out _)
		                && mesh != null) {
			            // keep mesh
		            } else {
			            mesh = null;
		            }
	            }

	            if (mesh == null) {
		            // Edge tolerance: nearest non-zero ID texel (not area-weighted). Radius 1 when
		            // multi-view / close assets so a larger neighbor cannot win from 2px away.
		            int edgeR = isMultiView ? 1 : 2;
		            id = SampleDominantMeshIdNearViewport01(viewportPos, searchRadiusPx: edgeR);
		            mesh = id != 0 ? ModelsHandler_3D.instance.getMesh_byUniqueID(id) : null;
	            }

	            if (mesh == null) {
		            // Single-view may still try remaining cameras; multi-view stays owning-column only.
		            if (!TryRaycastMeshUnder_AnyActiveViewCamera(requireSelected: false, out mesh, out _)) { return; }
	            }
	            if(mesh == null){ return; }
	            bool wasSelected = mesh._isSelected;
	            bool isSuccess = false;
	            mesh.TryChange_SelectionStatus(isSELECT: !wasSelected, out isSuccess, 
	                                           isDeselectOthers:false, preventDeselect_ifLast:false );
	            get_selectMode_toggle()?.PlayAnim();
	        }

	        bool any_inputField = KeyMousePenInput.isSomeInputFieldActive();

	        if (Input.GetKeyDown(KeyCode.A) && !any_inputField){
	            IReadOnlyList<SD_3D_Mesh> meshes = ModelsHandler_3D.instance.meshes;
	            for(int i=0; i<meshes.Count; ++i){
	                SD_3D_Mesh m = meshes[i];
	                if(m._isSelected){ continue; }
	                SD_3D_Mesh.SelectAll();
	            }
	            string msg = "All objects Selected. CTRL+Click, or CTRL+D to deselect all.";
	            Viewport_StatusText.instance.ShowStatusText(msg,false, 5, false);
	            get_selectMode_toggle()?.PlayAnim();
	        }

	        if(Input.GetKeyDown(KeyCode.D) && !any_inputField){
	            IReadOnlyList<SD_3D_Mesh> meshes = ModelsHandler_3D.instance.meshes;
	            for(int i=0; i<meshes.Count; ++i){
	                SD_3D_Mesh m = meshes[i];
	                if(!m._isSelected){ continue; }
	                SD_3D_Mesh.DeselectAll();
	            }
	            string msg = "All objects Deselected. CTRL+Click, or CTRL+A to select all.";
	            Viewport_StatusText.instance.ShowStatusText(msg,false, 5, false);
	            get_selectMode_toggle()?.PlayAnim();
	        }
	    }


	    //uv is a viewport pos [0,1]
	    ushort SampleMeshId(Vector2 uv) => SampleMeshIdAtViewport01_static(uv);

	    /// <summary>ID-buffer sample at viewport UV [0,1] (e.g. <see cref="MainViewport_UI.cursorMainViewportPos01"/>). Used for navigation, selection, etc.</summary>
	    public static ushort SampleMeshIdAtViewport01_static(Vector2 uv) {
	        if (UserCameras_MGR.instance == null || UserCameras_MGR.instance.camTextures == null) {
	            return 0;
	        }
	        RenderTexture id_tex = UserCameras_MGR.instance.camTextures._viewCam_meshIDs_ref;
	        if (id_tex == null) { return 0; }
	        Texture2D tex = new Texture2D(1, 1, TextureFormat.RG16, false);

	        // Y flip off for Unity 6000 mesh-ID RT; match <see cref="AreTexturesFlipped_Y"/>.
	        RenderTexture originalActive = RenderTexture.active;
	        RenderTexture.active = id_tex;
	        Rect pixelRect =  new Rect(uv.x*id_tex.width, uv.y*id_tex.height, 1, 1);
	        tex.ReadPixels(pixelRect, 0, 0);
	        tex.Apply();
	        RenderTexture.active = originalActive;

	        Color col = tex.GetPixel(0, 0);
	        ushort meshId = SD_3D_Mesh_UniqueIDMaker.DecodeID_fromColor(col);
	        Destroy(tex);
	        return meshId;
	    }


	    bool AreTexturesFlipped_Y(){
	        return false; //after updating to Unity 6000 rendered textures don't seem to be upside-down. Jan 2026.

	        // Create a simple orthographic projection matrix
	        Matrix4x4 projectionMatrix = Matrix4x4.Ortho(0, 1, 0, 1, -1, 1);
	        // Get the GPU projection matrix
	        Matrix4x4 gpuProjectionMatrix = GL.GetGPUProjectionMatrix(projectionMatrix, true);
	        // Check if the y-scale has been flipped
	        return gpuProjectionMatrix[1, 1] < 0;
	    }


	    void OnToggled_SelectMode(bool isOn){
	        _manuallyEnabled = isOn;
	        string msg = "Show/Hide meshes.  You can just Ctrl+click them to do it easier :)\nAlso, Ctrl+A to select all, or Ctrl+D to deselect all.\n";
	        Viewport_StatusText.instance.ShowStatusText(msg, false, 4, false);
	    }


	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;

	        StaticEvents.SubscribeAppend<bool>(nameof(ClickSelectMeshes_Toggle_UI)+"_toggle", OnToggled_SelectMode);
	    }

	    void Start(){
	        Update_callbacks_MGR.meshClick_mgr += OnUpdate;
	    }

	    void OnDestroy(){
	        Update_callbacks_MGR.meshClick_mgr -= OnUpdate;
	        StaticEvents.Unsubscribe<bool>(nameof(ClickSelectMeshes_Toggle_UI) + "_toggle", OnToggled_SelectMode);
	    }//end()
	}
}//end namespace
