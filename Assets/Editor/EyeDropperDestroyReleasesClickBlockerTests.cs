using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Eyedropper teardown: destroying while it holds <see cref="GlobalClickBlocker"/> must release the
/// lock. Its PickColor coroutine runs on Coroutines_MGR and is stopped by OnDestroy, so the
/// coroutine's own Unlock_if_can can never run — OnDestroy is the only remaining release point.
/// </summary>
public sealed class EyeDropperDestroyReleasesClickBlockerTests {

	static void InvokeOnDestroy(object target) {
		var m = target.GetType().GetMethod("OnDestroy",
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		Assert.That(m, Is.Not.Null, "BrushRibbon_UI_EyeDropperTool.OnDestroy not found");
		m.Invoke(target, null);
	}

	[Test]
	public void OnDestroyReleasesGlobalClickBlocker() {
		Assume.That(GlobalClickBlocker.isLocked(), Is.False,
			"another owner leaked a lock into this run");

		// Inactive host: no Awake/Start, so the tool has no helper or toggle — the exact state that
		// used to throw an NRE before the release line and strand the blocker.
		var host = new GameObject("EyeDropperHost");
		host.SetActive(false);
		try {
			var tool = host.AddComponent<BrushRibbon_UI_EyeDropperTool>();

			GlobalClickBlocker.Lock(who_is_requesting: tool);
			Assert.That(GlobalClickBlocker.isLocked(), Is.True, "precondition: tool holds the lock");

			InvokeOnDestroy(tool);

			Assert.That(GlobalClickBlocker.isLocked(), Is.False,
				"destroy must release the blocker or the whole app stays unclickable");
		}
		finally {
			Object.DestroyImmediate(host);
			GlobalClickBlocker.Unlock_if_can(who_is_requesting: this);
		}
	}

	[Test]
	public void OnDestroyDoesNotThrowWithoutStart() {
		var host = new GameObject("EyeDropperHostBare");
		host.SetActive(false);
		try {
			var tool = host.AddComponent<BrushRibbon_UI_EyeDropperTool>();
			Assert.DoesNotThrow(() => InvokeOnDestroy(tool),
				"OnDestroy must survive a null fetch helper / toggle to reach the blocker release");
		}
		finally {
			Object.DestroyImmediate(host);
		}
	}
}
