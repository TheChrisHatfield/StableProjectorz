using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Update_callbacks_MGR.navigation is one shared multicast delegate: CameraOrbit, CameraPanning,
/// CameraDolly, CameraMove and CameraFocus all subscribe to it. CameraFocus.OnDestroy assigned it
/// (=) instead of removing itself (-=), which dropped every other camera's handler and left the
/// destroyed CameraFocus.OnUpdate as the sole subscriber — orbit/pan/dolly silently stopped working.
/// </summary>
public sealed class CameraFocusNavigationUnsubscribeTests {

	[Test]
	public void OnDestroyKeepsOtherNavigationSubscribers() {
		Action original = Update_callbacks_MGR.navigation;
		var host = new GameObject("CameraFocusHost");
		host.SetActive(false);
		bool otherCameraRan = false;
		Action otherCamera = () => otherCameraRan = true;
		try {
			Update_callbacks_MGR.navigation = null;
			Update_callbacks_MGR.navigation += otherCamera; // stands in for CameraOrbit/Panning/Dolly

			var focus = host.AddComponent<CameraFocus>();
			var onUpdate = typeof(CameraFocus).GetMethod("OnUpdate",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(onUpdate, Is.Not.Null);
			Update_callbacks_MGR.navigation += (Action)Delegate.CreateDelegate(
				typeof(Action), focus, onUpdate);

			var onDestroy = typeof(CameraFocus).GetMethod("OnDestroy",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(onDestroy, Is.Not.Null);
			onDestroy.Invoke(focus, null);

			Assert.That(Update_callbacks_MGR.navigation, Is.Not.Null,
				"teardown must not wipe the shared navigation delegate");

			Update_callbacks_MGR.navigation.Invoke();
			Assert.That(otherCameraRan, Is.True,
				"orbit/pan/dolly must still receive navigation after a CameraFocus is destroyed");
		}
		finally {
			Update_callbacks_MGR.navigation = original;
			UnityEngine.Object.DestroyImmediate(host);
		}
	}

	[Test]
	public void OnDestroyRemovesItselfRatherThanAssigning() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "Navigation", "CameraFocus.cs");
		string src = File.ReadAllText(path);

		Assert.That(src, Does.Contain("Update_callbacks_MGR.navigation -= OnUpdate;"));
		Assert.That(src, Does.Not.Contain("Update_callbacks_MGR.navigation = OnUpdate;"),
			"assigning the shared delegate drops every other camera subscriber");
	}

	[Test]
	public void OnDestroyAlsoLeavesTheMultiViewEditModeEvents() {
		// Subscribed in Awake; without the matching -= these fire into a destroyed CameraFocus and
		// reparent a dead transform when the user enters/leaves MultiView edit mode.
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "Navigation", "CameraFocus.cs");
		string src = File.ReadAllText(path);
		int destroy = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		Assert.That(destroy, Is.GreaterThan(0));
		string body = src.Substring(destroy);

		Assert.That(body, Does.Contain("MultiView_Ribbon_UI.OnStartEditMode -= OnStartEditMode_MultiView;"));
		Assert.That(body, Does.Contain("MultiView_Ribbon_UI.OnStop2_EditMode -= OnStop2EditMode_MultiView;"));
	}
}
