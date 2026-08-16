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
			Assert.That(ViewportAxisGizmo_UI.IsAnyVisibleGizmo(), Is.False);
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

	#region rpc + add-on wiring

	[Test]
	public void RelativeCenterIconResolvesInsideStreamingAssets() {
		string resolved = ViewportAxisGizmo_AddonBridge.ResolveIconPath("Addons/ViewportAxisGizmoSPZ/lantern.png");
		Assert.That(resolved, Does.StartWith(Application.streamingAssetsPath));
		Assert.That(ViewportAxisGizmo_AddonBridge.ResolveIconPath(null),
			Is.EqualTo(ViewportAxisGizmo_AddonBridge.DefaultCenterIconPath));
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
		Assert.That(src, Does.Contain("ViewportAxisGizmo_UI.TeardownAllForAddonDisabled()"),
			"Turning the add-on off must remove the widget.");
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
