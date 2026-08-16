using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// DoForIsolatedMeshes / DoForAllMeshes_EvenIfHidden hide or force-show the whole model, run a caller
/// instruction, then put visibility back. If that instruction throws and the restore is skipped, the
/// user is left staring at a model whose meshes are hidden (or hidden ones forced visible) with no way
/// back short of a reimport — so the restore has to survive the throw.
/// </summary>
public sealed class IsolatedMeshVisibilityRestoreContractTests {

	GameObject _root;
	Objs3D_Container _container;

	[SetUp]
	public void SetUp() {
		_root = new GameObject("Objs3DContainerHost");
		_container = _root.AddComponent<Objs3D_Container>();
	}

	[TearDown]
	public void TearDown() {
		if (_root != null) Object.DestroyImmediate(_root);
	}

	SD_3D_Mesh NewMesh(string name, bool visible) {
		var go = new GameObject(name);
		go.transform.SetParent(_root.transform, false);
		go.AddComponent<MeshFilter>();
		var renderer = go.AddComponent<MeshRenderer>();
		var mesh = go.AddComponent<SD_3D_Mesh>();
		// Edit mode never calls Awake, which is where SD_3D_Mesh caches its renderer. Running Awake by
		// hand would also add a convex MeshCollider with no mesh, so just seed the one field visibility
		// depends on.
		var backing = typeof(SD_3D_Mesh).GetField("<_meshRenderer>k__BackingField",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(backing, Is.Not.Null, "SD_3D_Mesh._meshRenderer backing field moved");
		backing.SetValue(mesh, renderer);
		mesh.ToggleRender(visible);
		return mesh;
	}

	void SeedTwoMeshes(out SD_3D_Mesh shown, out SD_3D_Mesh hidden) {
		shown = NewMesh("Shown", true);
		hidden = NewMesh("Hidden", false);
		_container.meshes = new List<SD_3D_Mesh> { shown, hidden };
	}

	[Test]
	public void Isolation_RestoresVisibility_EvenWhenTheInstructionThrows() {
		SeedTwoMeshes(out var shown, out var hidden);

		Assert.Throws<System.InvalidOperationException>(() =>
			_container.DoForIsolatedMeshes(new List<SD_3D_Mesh> { hidden },
				() => throw new System.InvalidOperationException("export blew up")));

		Assert.That(shown._isVisible, Is.True, "a visible mesh must not stay hidden after a failed run");
		Assert.That(hidden._isVisible, Is.False, "a hidden mesh must not stay force-shown");
		Assert.That(_container.isolatedMeshes, Is.Empty, "isolation must not stay latched on failure");
		Assert.That(_container.isolatedRenderers, Is.Empty);
	}

	[Test]
	public void ShowAll_RestoresVisibility_EvenWhenTheInstructionThrows() {
		SeedTwoMeshes(out var shown, out var hidden);

		Assert.Throws<System.InvalidOperationException>(() =>
			_container.DoForAllMeshes_EvenIfHidden(
				() => throw new System.InvalidOperationException("bake blew up")));

		Assert.That(shown._isVisible, Is.True);
		Assert.That(hidden._isVisible, Is.False, "forcing every mesh visible must be undone");
	}

	[Test]
	public void Isolation_ShowsOnlyTheIsolatedSetWhileRunning_AndRestoresAfter() {
		SeedTwoMeshes(out var shown, out var hidden);
		bool sawIsolation = false;

		_container.DoForIsolatedMeshes(new List<SD_3D_Mesh> { hidden }, () => {
			sawIsolation = !shown._isVisible && hidden._isVisible;
			Assert.That(_container.isolatedMeshes, Has.Count.EqualTo(1));
		});

		Assert.That(sawIsolation, Is.True, "only the isolated mesh may render during the instruction");
		Assert.That(shown._isVisible, Is.True);
		Assert.That(hidden._isVisible, Is.False);
	}

	[Test]
	public void RepeatedRuns_DoNotGrowTheSavedVisibilityState() {
		// The restore loops used to Add to the same list they were reading, doubling it every call.
		// Reads still landed on the saved values by luck, so the only proof is that repeated runs
		// keep restoring correctly no matter how many times they happen.
		SeedTwoMeshes(out var shown, out var hidden);
		for (int i = 0; i < 5; i++) {
			_container.DoForAllMeshes_EvenIfHidden(() => { });
			_container.DoForIsolatedMeshes(new List<SD_3D_Mesh> { shown }, () => { });
		}
		Assert.That(shown._isVisible, Is.True);
		Assert.That(hidden._isVisible, Is.False);
	}
}
