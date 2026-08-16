using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using spz;

/// <summary>
/// Viewport orientation gizmo (ViewportAxisGizmoSPZ add-on): projection math, widget build, viewport-only add-on
/// wiring, and the RPC + Python package that ship with it.
/// </summary>
public sealed class ViewportAxisGizmoContractTests {

	static string RepoPath(string relative) =>
		Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

	static string ReadRepo(string relative) {
		string path = RepoPath(relative);
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	#region math

	[Test]
	public void IdentityView_PutsXRightYUpAndZBehindTheCenter() {
		const float radius = 40f;
		Vector2 x = ViewportAxisGizmo_Math.AxisHandleOffset(Quaternion.identity, Vector3.right, radius);
		Vector2 y = ViewportAxisGizmo_Math.AxisHandleOffset(Quaternion.identity, Vector3.up, radius);
		Vector2 z = ViewportAxisGizmo_Math.AxisHandleOffset(Quaternion.identity, Vector3.forward, radius);

		Assert.That(x.x, Is.EqualTo(radius).Within(0.001f), "+X must sit to the right of the gizmo center.");
		Assert.That(x.y, Is.EqualTo(0f).Within(0.001f));
		Assert.That(y.y, Is.EqualTo(radius).Within(0.001f), "+Y must sit above the gizmo center.");
		Assert.That(z.magnitude, Is.LessThan(0.001f), "+Z points away from a default camera, so it collapses onto the center.");

		Assert.That(ViewportAxisGizmo_Math.TowardsViewer01(Quaternion.identity, Vector3.forward), Is.EqualTo(0f).Within(0.001f));
		Assert.That(ViewportAxisGizmo_Math.TowardsViewer01(Quaternion.identity, Vector3.back), Is.EqualTo(1f).Within(0.001f));
	}

	[Test]
	public void HandlesNearTheViewerAreBiggerBrighterAndDrawnLast() {
		Assert.That(ViewportAxisGizmo_Math.HandleScale(1f), Is.GreaterThan(ViewportAxisGizmo_Math.HandleScale(0f)));
		Assert.That(ViewportAxisGizmo_Math.HandleAlpha(1f, true), Is.GreaterThan(ViewportAxisGizmo_Math.HandleAlpha(0f, true)));
		Assert.That(ViewportAxisGizmo_Math.HandleAlpha(1f, false), Is.LessThan(ViewportAxisGizmo_Math.HandleAlpha(1f, true)),
			"Negative axes stay dimmer than their labelled positive twin.");
		Assert.That(ViewportAxisGizmo_Math.DrawOrderKey(1f), Is.GreaterThan(ViewportAxisGizmo_Math.DrawOrderKey(0.5f)));
	}

	[Test]
	public void SnapPose_LooksBackAtThePivotFromTheClickedAxis() {
		var pivot = new Vector3(2f, 3f, -4f);
		foreach (Vector3 axis in ViewportAxisGizmo_Math.AxisDirections) {
			float distance = ViewportAxisGizmo_Math.SnapDistance(pivot + axis * 7f, pivot);
			Assert.That(distance, Is.EqualTo(7f).Within(0.001f), "Snapping keeps the current framing distance.");

			Vector3 pos = ViewportAxisGizmo_Math.CameraPositionForAxis(pivot, axis, distance);
			Quaternion rot = ViewportAxisGizmo_Math.CameraRotationForAxis(axis);
			Vector3 toPivot = (pivot - pos).normalized;
			Assert.That(Vector3.Dot(rot * Vector3.forward, toPivot), Is.EqualTo(1f).Within(0.001f),
				$"Camera snapped to {axis} must look back at the pivot.");
		}
	}

	[Test]
	public void TopAndBottomViewsUseAZUpHintSoLookRotationStaysValid() {
		Assert.That(ViewportAxisGizmo_Math.UpHintForAxis(Vector3.up), Is.EqualTo(Vector3.forward));
		Assert.That(ViewportAxisGizmo_Math.UpHintForAxis(Vector3.down), Is.EqualTo(Vector3.back));
		Assert.That(ViewportAxisGizmo_Math.UpHintForAxis(Vector3.right), Is.EqualTo(Vector3.up));
	}

	#endregion

	#region widget

	static RectTransform NewHost() {
		var go = new GameObject("GizmoHost", typeof(RectTransform));
		var rt = go.GetComponent<RectTransform>();
		rt.sizeDelta = new Vector2(1280f, 720f);
		return rt;
	}

	[Test]
	public void BuildInto_DocksTopRightWithLanternCenterAndSixAxisHandles() {
		RectTransform host = NewHost();
		try {
			var spec = new ViewportAxisGizmo_Spec(120f, 16f, string.Empty, ViewportAxisGizmo_UI.OverviewCommandId);
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(host, spec);
			Assert.That(gizmo, Is.Not.Null);

			RectTransform root = gizmo.RootRect;
			Assert.That(root.name, Is.EqualTo(ViewportAxisGizmo_UI.RootName));
			Assert.That(root.anchorMin, Is.EqualTo(new Vector2(1f, 1f)), "Gizmo anchors to the viewport's top-right corner.");
			Assert.That(root.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
			Assert.That(root.sizeDelta, Is.EqualTo(new Vector2(120f, 120f)));
			Assert.That(root.anchoredPosition, Is.EqualTo(new Vector2(-16f, -16f)));

			Assert.That(root.GetComponent<MainViewport_RaycastBlocker>(), Is.Not.Null,
				"Paint / orbit must not fire under the gizmo.");
			var backdrop = root.Find(ViewportAxisGizmo_UI.BackdropName);
			Assert.That(backdrop, Is.Not.Null);
			Assert.That(backdrop.GetComponent<Image>().raycastTarget, Is.True,
				"The backdrop is what makes the widget area block the 3D view.");

			var center = root.Find(ViewportAxisGizmo_UI.CenterName);
			Assert.That(center, Is.Not.Null, "The lantern overview button lives in the middle of the gizmo.");
			Assert.That(center.GetComponent<Button>(), Is.Not.Null);
			Assert.That(center.GetComponent<Image>().sprite, Is.Not.Null);

			int handles = 0;
			foreach (Transform child in root) {
				if (child.name.StartsWith(ViewportAxisGizmo_UI.HandlePrefix, StringComparison.Ordinal)) {
					handles++;
					Assert.That(child.GetComponent<Button>(), Is.Not.Null, $"{child.name} must be clickable to snap the view.");
				}
			}
			Assert.That(handles, Is.EqualTo(6), "+X/-X/+Y/-Y/+Z/-Z.");
			foreach (string label in new[] { "+X", "+Y", "+Z" }) {
				Assert.That(root.Find(ViewportAxisGizmo_UI.HandlePrefix + label), Is.Not.Null);
			}
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void ApplyOrientation_MovesHandlesAndSortsNearOnesOnTop() {
		RectTransform host = NewHost();
		try {
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(host, ViewportAxisGizmo_Spec.Default);
			gizmo.ApplyOrientation(Quaternion.identity);

			RectTransform root = gizmo.RootRect;
			var plusX = (RectTransform)root.Find(ViewportAxisGizmo_UI.HandlePrefix + "+X");
			var minusX = (RectTransform)root.Find(ViewportAxisGizmo_UI.HandlePrefix + "-X");
			var minusZ = (RectTransform)root.Find(ViewportAxisGizmo_UI.HandlePrefix + "-Z");
			var plusZ = (RectTransform)root.Find(ViewportAxisGizmo_UI.HandlePrefix + "+Z");

			Assert.That(plusX.anchoredPosition.x, Is.GreaterThan(0f));
			Assert.That(minusX.anchoredPosition.x, Is.LessThan(0f));
			Assert.That(minusZ.GetSiblingIndex(), Is.GreaterThan(plusZ.GetSiblingIndex()),
				"-Z faces the default camera, so it must draw over the axis pointing away.");

			// Yaw 90°: the camera now looks down +X, so the X handles collapse onto the center and -X is the near one.
			gizmo.ApplyOrientation(Quaternion.Euler(0f, 90f, 0f));
			Assert.That(Mathf.Abs(plusX.anchoredPosition.x), Is.LessThan(1f));
			Assert.That(minusX.GetSiblingIndex(), Is.GreaterThan(plusX.GetSiblingIndex()));
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void LanternStaysOnTopOfTheHandleThatCollapsesOntoTheCenter() {
		RectTransform host = NewHost();
		try {
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(host, ViewportAxisGizmo_Spec.Default);
			RectTransform root = gizmo.RootRect;
			var center = (RectTransform)root.Find(ViewportAxisGizmo_UI.CenterName);
			var minusZ = (RectTransform)root.Find(ViewportAxisGizmo_UI.HandlePrefix + "-Z");

			// Default front view: -Z faces the camera and projects onto the gizmo center, right over the lantern.
			gizmo.ApplyOrientation(Quaternion.identity);
			Assert.That(minusZ.anchoredPosition.magnitude, Is.LessThan(1f));
			Assert.That(center.GetSiblingIndex(), Is.GreaterThan(minusZ.GetSiblingIndex()),
				"The lantern must stay clickable and visible instead of being covered by the collapsed handle.");

			gizmo.ApplyOrientation(Quaternion.Euler(35f, 140f, 0f));
			Assert.That(center.GetSiblingIndex(), Is.EqualTo(root.childCount - 1),
				"Every orientation pass must leave the lantern as the topmost sibling.");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void TryAttachIsIdempotentAndTeardownRemovesTheWidget() {
		RectTransform host = NewHost();
		try {
			ViewportAxisGizmo_UI.BuildInto(host, ViewportAxisGizmo_Spec.Default);
			ViewportAxisGizmo_UI found = ViewportAxisGizmo_UI.FindUnder(host);
			Assert.That(found, Is.Not.Null);

			var respec = new ViewportAxisGizmo_Spec(150f, 24f, string.Empty, ViewportAxisGizmo_UI.OverviewCommandId);
			found.ApplySpec(respec);
			Assert.That(found.RootRect.sizeDelta, Is.EqualTo(new Vector2(150f, 150f)));
			Assert.That(host.GetComponentsInChildren<ViewportAxisGizmo_UI>(true).Length, Is.EqualTo(1),
				"Re-attaching must refresh the existing gizmo, not stack a second one.");

			ViewportAxisGizmo_UI.TeardownAllForAddonDisabled();
			Assert.That(ViewportAxisGizmo_UI.FindUnder(host), Is.Null, "Disabling the add-on removes the gizmo.");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void GizmoMovesToTheRealViewportRectInsteadOfDuplicating() {
		RectTransform early = NewHost();
		RectTransform inner = NewHost();
		try {
			// Attach that happened before the inner aspect-fitted rect existed.
			var spec = new ViewportAxisGizmo_Spec(120f, 16f, string.Empty, ViewportAxisGizmo_UI.OverviewCommandId);
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(early, spec);

			Assert.That(gizmo.EnsureHostedUnder(inner), Is.True, "The widget must follow the viewport rect it belongs on.");
			Assert.That(gizmo.RootRect.parent, Is.EqualTo((Transform)inner));
			Assert.That(early.GetComponentsInChildren<ViewportAxisGizmo_UI>(true).Length, Is.EqualTo(0),
				"No stale copy may stay behind on the old rect.");
			Assert.That(inner.GetComponentsInChildren<ViewportAxisGizmo_UI>(true).Length, Is.EqualTo(1));
			Assert.That(gizmo.RootRect.anchorMin, Is.EqualTo(new Vector2(1f, 1f)), "Re-hosting re-applies the corner dock.");
			Assert.That(gizmo.RootRect.anchoredPosition, Is.EqualTo(new Vector2(-16f, -16f)));

			Assert.That(gizmo.EnsureHostedUnder(inner), Is.False, "Steady state must not touch the hierarchy.");
			Assert.That(gizmo.EnsureHostedUnder(null), Is.False, "A missing viewport must not orphan the widget.");
			Assert.That(ViewportAxisGizmo_UI.FindAnyLiveGizmo(), Is.EqualTo(gizmo),
				"Attach reuses the live widget wherever it is parented, so it cannot build a second one.");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(early.gameObject);
			UnityEngine.Object.DestroyImmediate(inner.gameObject);
		}
	}

	[Test]
	public void RetiredGizmoIsNeverHandedBackWhileItsDestroyIsStillPending() {
		RectTransform host = NewHost();
		try {
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(host, ViewportAxisGizmo_Spec.Default);
			gizmo.MarkTornDown();

			Assert.That(ViewportAxisGizmo_UI.FindAnyLiveGizmo(), Is.Null,
				"A re-enable in the same frame must build a fresh widget, not refresh the dying one.");
			Assert.That(ViewportAxisGizmo_UI.FindUnder(host), Is.Null);
			Assert.That(ViewportAxisGizmo_UI.IsAnyMountedGizmo(), Is.False);
			Assert.That(gizmo.gameObject.activeSelf, Is.False);

			gizmo.RefreshFromCamera();
			Assert.That(gizmo.EnsureHostedUnder(host), Is.False,
				"The retired widget must not parent itself back onto the viewport for its last frame.");
			Assert.That(gizmo.RootRect.parent, Is.Null);
			UnityEngine.Object.DestroyImmediate(gizmo.gameObject);
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void SpecClampsSizeAndDefaultsTheCenterCommand() {
		var tiny = new ViewportAxisGizmo_Spec(1f, -5f, null, null);
		Assert.That(tiny.SizePx, Is.InRange(64f, 240f));
		Assert.That(tiny.MarginPx, Is.GreaterThanOrEqualTo(0f));
		Assert.That(tiny.CenterCommandId, Is.EqualTo(ViewportAxisGizmo_UI.OverviewCommandId));
		Assert.That(new ViewportAxisGizmo_Spec(9999f, 0f, null, "custom").SizePx, Is.LessThanOrEqualTo(240f));
	}

	#endregion

	[Test]
	public void OverviewFramesTheWholeSceneAndFailsHonestlyWhenEmpty() {
		Assert.That(ViewportAxisGizmo_CameraOps.HasSomethingToFrame(), Is.False,
			"EditMode has no ModelsHandler_3D, so the lantern must know there is nothing to frame.");

		string ops = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_CameraOps.cs");
		Assert.That(ops, Does.Contain("HasSomethingToFrame()"),
			"TryOverview must check the scene before claiming success.");
		Assert.That(ops, Does.Contain("Nothing loaded to frame"),
			"The lantern click must tell the user why overview did nothing.");
		Assert.That(ops, Does.Contain("Frame_Bounds_maybe"),
			"Lantern overview must frame explicit whole-scene bounds, not just the selection.");
		Assert.That(ops, Does.Contain("GetTotalBounds_ofAllMeshes"),
			"Overview must frame every loaded mesh (whole scene), not selectedMeshes only.");
		Assert.That(ops, Does.Contain("TryGetNavigationLockedCamera"),
			"Multiview: mid-orbit the gizmo must ride the nav lock, not the corner under the cursor.");
		Assert.That(ops, Does.Contain("_curr_viewCamera"),
			"Corner-docked gizmo must drive the marked-current camera, not NearestToCursor (rightmost-column bias).");
		Assert.That(ops, Does.Not.Contain("NearestToCursor()"),
			"Idle gizmo clicks must not Voronoi the cursor — the cursor is always over the top-right chrome.");
	}

	[Test]
	public void MultiviewNavLockPromotesCurrentViewCamera() {
		string mgr = ReadRepo("Assets/_gm/Features/Camera/UserCameras_MGR.cs");
		Assert.That(mgr, Does.Contain("SetCurrViewCamera(cameraIndex)"),
			"LockNavigationCamera must promote the locked column to current so the gizmo survives after orbit ends.");
	}

	[Test]
	public void AxisSnapPivotFallsBackToWholeSceneWhenNothingSelected() {
		string ops = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_CameraOps.cs");
		Assert.That(ops, Does.Contain("GetTotalBounds_ofSelectedMeshes"),
			"Axis snap should prefer the selection pivot when something is selected.");
		Assert.That(ops, Does.Contain("GetTotalBounds_ofAllMeshes"),
			"With nothing selected, snap must orbit the loaded scene — not silent world origin.");
		Assert.That(ViewportAxisGizmo_CameraOps.ResolvePivot(), Is.EqualTo(Vector3.zero),
			"EditMode has no models; pivot stays at origin without inventing a bounds center.");
	}

	[Test]
	public void IdleViewDoesNoPerFrameWorkButSizeChangesReproject() {
		RectTransform host = NewHost();
		try {
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(host, ViewportAxisGizmo_Spec.Default);
			var rotation = Quaternion.Euler(20f, 55f, 0f);

			Assert.That(gizmo.ApplyOrientation(rotation), Is.True, "First pass for a new rotation must project.");
			Assert.That(gizmo.ApplyOrientation(rotation), Is.False,
				"A static camera must not re-sort the canvas hierarchy or re-generate TMP meshes every frame.");
			Assert.That(gizmo.ApplyOrientation(Quaternion.Euler(20f, 55.005f, 0f)), Is.False,
				"Sub-pixel jitter must not churn the hierarchy either.");
			Assert.That(gizmo.ApplyOrientation(Quaternion.Euler(20f, 70f, 0f)), Is.True,
				"A real orbit must still re-project.");

			// Theme silo: an idle camera must still scrub spilled tints back to authored axis RGB.
			var xHandle = gizmo.RootRect.Find(ViewportAxisGizmo_UI.HandlePrefix + "+X");
			Assert.That(xHandle, Is.Not.Null);
			var xImg = xHandle.GetComponent<Image>();
			Color authored = xImg.color;
			xImg.color = Color.magenta;
			Assert.That(gizmo.ApplyOrientation(Quaternion.Euler(20f, 70f, 0f)), Is.False,
				"Same rotation stays idle for layout, but…");
			Assert.That(xImg.color, Is.EqualTo(authored),
				"…authored axis colors must be reasserted so Nomad/BoundChrome spill cannot stick.");

			gizmo.ApplySpec(new ViewportAxisGizmo_Spec(200f, 20f, string.Empty, null));
			Assert.That(gizmo.ApplyOrientation(Quaternion.Euler(20f, 70f, 0f)), Is.True,
				"A resize changes the orbit radius, so the cached projection must be discarded.");

			var center = (RectTransform)gizmo.RootRect.Find(ViewportAxisGizmo_UI.CenterName);
			Assert.That(center.GetSiblingIndex(), Is.EqualTo(gizmo.RootRect.childCount - 1),
				"Skipping redundant re-sorts must not lose the lantern-on-top rule.");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void SnapRecordsAUsableFovAndRetargetsAnInFlightCameraMove() {
		Assert.That(ViewportAxisGizmo_CameraOps.ResolveFov(null), Is.InRange(1f, 179f),
			"A snap POV must never carry the -1 sentinel that ViewCamera_FOV starts with.");

		string ops = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_CameraOps.cs");
		Assert.That(ops, Does.Contain("interruptCurrentFly: true"),
			"A second axis click during the fly animation must retarget instead of being silently dropped.");
		Assert.That(ops, Does.Contain("ResolveFov(cam)"));

		string focus = ReadRepo("Assets/_gm/Features/Camera/Navigation/CameraFocus.cs");
		Assert.That(focus, Does.Contain("bool interruptCurrentFly=false"),
			"Existing POV-restore callers must keep the old drop-if-busy behaviour by default.");
		Assert.That(focus, Does.Contain("public bool isFlyingCamera"));
	}

	#region rpc + add-on wiring

	[Test]
	public void RelativeCenterIconResolvesInsideStreamingAssets() {
		string resolved = ViewportAxisGizmo_AddonBridge.ResolveIconPath("Addons/ViewportAxisGizmoSPZ/lantern.png");
		Assert.That(resolved, Does.StartWith(Application.streamingAssetsPath));
		Assert.That(File.Exists(resolved), Is.True, "The shipped lantern must resolve when given the StreamingAssets-relative path.");
		Assert.That(ViewportAxisGizmo_AddonBridge.ResolveIconPath(null),
			Is.EqualTo(ViewportAxisGizmo_AddonBridge.DefaultCenterIconPath));
	}

	[Test]
	public void ReattachWithANewCenterIconUpdatesTheLanternSprite() {
		RectTransform host = NewHost();
		try {
			var first = new ViewportAxisGizmo_Spec(104f, 8f, ViewportAxisGizmo_AddonBridge.DefaultCenterIconPath, null);
			ViewportAxisGizmo_UI gizmo = ViewportAxisGizmo_UI.BuildInto(host, first);
			var centerImg = gizmo.RootRect.Find(ViewportAxisGizmo_UI.CenterName).GetComponent<Image>();
			Sprite firstSprite = centerImg.sprite;
			Assert.That(firstSprite, Is.Not.Null);

			string missing = Path.Combine(Application.streamingAssetsPath, "Addons", "ViewportAxisGizmoSPZ", "does-not-exist.png");
			gizmo.ApplySpec(new ViewportAxisGizmo_Spec(104f, 8f, missing, null));
			Assert.That(centerImg.sprite, Is.Not.Null);
			Assert.That(centerImg.sprite, Is.Not.EqualTo(firstSprite),
				"A re-attach with a different center_icon must refresh the lantern, not keep the first sprite.");
			Assert.That(centerImg.sprite, Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bullseye)),
				"A missing icon must fall back to the line icon instead of leaving a stale lantern.");

			gizmo.ApplySpec(new ViewportAxisGizmo_Spec(104f, 8f, missing, null));
			Assert.That(centerImg.sprite, Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bullseye)),
				"Repeating the same path must be a no-op, not thrash the sprite.");
		}
		finally {
			UnityEngine.Object.DestroyImmediate(host.gameObject);
		}
	}

	[Test]
	public void BareLanternFilenameResolvesInsideTheAddonFolder() {
		// Python docs say relative names resolve inside the add-on folder; a bare filename must not silently
		// look under StreamingAssets root and then fall back to the Bullseye line icon.
		string resolved = ViewportAxisGizmo_AddonBridge.ResolveIconPath("lantern.png");
		Assert.That(resolved, Is.EqualTo(ViewportAxisGizmo_AddonBridge.DefaultCenterIconPath));
		Assert.That(File.Exists(resolved), Is.True);
	}

	[Test]
	public void DomainReloadOffClearsAndDestroysCachedLanternTextures() {
		string src = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_UI.cs");
		int reset = src.IndexOf("static void ResetStatics()", StringComparison.Ordinal);
		Assert.That(reset, Is.GreaterThan(0));
		string body = src.Substring(reset, Math.Min(900, src.Length - reset));
		Assert.That(body, Does.Contain("CenterSpriteCache"));
		Assert.That(body, Does.Contain("Destroy(kvp.Value)"),
			"Domain-reload-off Play Mode must Destroy cached sprites, not only drop the dictionary entries.");
		Assert.That(body, Does.Contain("Destroy(tex)"),
			"The Texture2D behind each lantern sprite must be released too.");
	}

	[Test]
	public void ThemeSilo_GizmoNeverCallsBoundChromeAndReassertsAuthoredColorsWhenIdle() {
		string src = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_UI.cs");
		Assert.That(src, Does.Not.Contain("ApplyBoundChrome"),
			"Gizmo keeps Blender-style authored RGB; it must not opt into Nomad BoundChrome mutators.");
		Assert.That(src, Does.Contain("ReassertAuthoredAxisColors"),
			"Idle ApplyOrientation must scrub spilled tints without hierarchy/TMP churn.");
	}

	[Test]
	public void RefreshOnlyTouchesCanvasGroupWhenUsableStateFlips() {
		string src = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_UI.cs");
		int refresh = src.IndexOf("public void RefreshFromCamera()", StringComparison.Ordinal);
		Assert.That(refresh, Is.GreaterThan(0));
		string body = src.Substring(refresh, Math.Min(1400, src.Length - refresh));
		Assert.That(body, Does.Contain("Mathf.Approximately(_canvasGroup.alpha, wantedAlpha)"));
		Assert.That(body, Does.Contain("ApplyCornerDock(_spec.MarginPx)"),
			"Aspect fit moves the inner rect every frame — the corner dock must be re-applied.");
		Assert.That(body, Does.Contain("SetAsLastSibling()"),
			"Stay above MainViewport_UI_EventListener so axis/lantern clicks reach the buttons.");
	}

	[Test]
	public void GizmoParentsToMainViewportChromeNotTheSizeReference() {
		string src = ReadRepo("Assets/_gm/Features/Viewport/Main Viewport/ViewportAxisGizmo_UI.cs");
		int resolve = src.IndexOf("public static RectTransform ResolveViewportParent()", StringComparison.Ordinal);
		int dock = src.IndexOf("public static RectTransform ResolveDockReference()", StringComparison.Ordinal);
		Assert.That(resolve, Is.GreaterThan(0));
		Assert.That(dock, Is.GreaterThan(resolve));
		string parentBody = src.Substring(resolve, dock - resolve);
		Assert.That(parentBody, Does.Contain("mainViewportRect"));
		Assert.That(parentBody, Does.Not.Contain("innerViewportRect"),
			"The size-reference rect is the first sibling under the viewport — parenting there draws behind the view RT.");
		string dockBody = src.Substring(dock, Math.Min(500, src.Length - dock));
		Assert.That(dockBody, Does.Contain("innerViewportRect"),
			"Corner placement must still track the aspect-fitted image.");
		Assert.That(src, Does.Contain("ApplyCornerDock"));
	}

	[Test]
	public void AttachRpcReportsMountedAndVisibleSeparately() {
		string bridge = ReadRepo("Assets/_gm/Features/AddonSystem/ViewportAxisGizmo_AddonBridge.cs");
		Assert.That(bridge, Does.Contain("r[\"mounted\"] = mounted"));
		Assert.That(bridge, Does.Contain("r[\"visible\"] = visible"));
		Assert.That(bridge, Does.Contain("IsGizmoUsable()"),
			"visible must mean the canvas is shown (3D nav on), not merely that the GameObject is active.");
		Assert.That(bridge, Does.Contain("IsAnyMountedGizmo()"));
	}

	[Test]
	public void SocketServerPublishesAndDispatchesTheAttachCommand() {
		string src = ReadRepo("Assets/_gm/Features/AddonSystem/Addon_SocketServer.cs");
		Assert.That(src, Does.Contain("\"spz.ui.attach_viewport_axis_gizmo\""),
			"The capability list must advertise the gizmo attach so add-ons can feature-detect it.");
		Assert.That(src, Does.Contain("TryExecuteAttachViewportAxisGizmo(@params ?? new JObject())"),
			"Advertising without a dispatch branch would be scaffold-only.");
		Assert.That(src, Does.Contain("ViewportAxisGizmo_AddonBridge.TryAttachFromCore"));
	}

	[Test]
	public void AddonManagerTreatsTheGizmoAsViewportOnly() {
		Assert.That(Addon_MGR.IsViewportOnlyAddon(Addon_MGR.ViewportAxisGizmoAddonId), Is.True,
			"The gizmo lives on the viewport and must never create a Command Ribbon tab.");
		Assert.That(Addon_MGR.IsViewportOnlyAddon(Addon_MGR.RibbonOnlyFullscreenAddonId), Is.True);
		Assert.That(Addon_MGR.IsViewportOnlyAddon("MeshTools"), Is.False);
		Assert.That(Addon_MGR.ViewportAxisGizmoAddonId, Is.EqualTo(ViewportAxisGizmo_AddonBridge.AddonId));
	}

	[Test]
	public void AddonManagerAttachesNativelyWhenPythonNeverRegisters() {
		string src = ReadRepo("Assets/_gm/Features/AddonSystem/Addon_MGR.cs");
		Assert.That(src, Does.Contain("StartEnsureViewportAxisGizmo"),
			"HTTP-off / register() failure must still attach the widget on the main thread.");
		Assert.That(src, Does.Contain("StopEnsureAndTeardownViewportAxisGizmo"),
			"Disable must stop the attach-retry coroutine before destroying the widget.");
		Assert.That(src, Does.Contain("ViewportAxisGizmo_UI.TeardownAllForAddonDisabled()"),
			"Turning the add-on off must remove the widget.");
		int ensure = src.IndexOf("IEnumerator CoEnsureViewportAxisGizmo()", StringComparison.Ordinal);
		Assert.That(ensure, Is.GreaterThan(0));
		string body = src.Substring(ensure, Math.Min(1400, src.Length - ensure));
		Assert.That(body, Does.Contain("TryAttachFromCore(null)"));
		int attachAt = body.IndexOf("TryAttachFromCore(null)", StringComparison.Ordinal);
		int recheckAt = body.IndexOf("if (!IsAddonEnabled(ViewportAxisGizmoAddonId))", attachAt, StringComparison.Ordinal);
		Assert.That(recheckAt, Is.GreaterThan(attachAt),
			"After TryAttach the loop must re-check enabled state so a mid-frame disable cannot leave a zombie.");
	}

	[Test]
	public void AddonPackageShipsIconAndCallsTheAttachRpc() {
		string init = ReadRepo("Assets/StreamingAssets/Addons/ViewportAxisGizmoSPZ/__init__.py");
		Assert.That(init, Does.Contain("attach_viewport_axis_gizmo"));
		Assert.That(init, Does.Not.Contain("create_panel("), "Viewport-only add-on: no Command Ribbon tab.");

		string manifest = ReadRepo("Assets/StreamingAssets/Addons/ViewportAxisGizmoSPZ/addon.json");
		Assert.That(manifest, Does.Contain("\"version\""));
		Assert.That(manifest, Does.Contain("\"description\""));

		Assert.That(File.Exists(RepoPath("Assets/StreamingAssets/Addons/ViewportAxisGizmoSPZ/lantern.png")), Is.True,
			"The lantern glyph must ship with the add-on — the center button loads it from StreamingAssets.");

		string client = ReadRepo("Assets/StreamingAssets/AddonSystem/spz.py");
		Assert.That(client, Does.Contain("def attach_viewport_axis_gizmo("),
			"spz.py must expose the helper the add-on calls.");
	}

	#endregion
}
