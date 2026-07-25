using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Face-pick symmetry plane must track the mesh when it rotates (stored in local space).
/// </summary>
public sealed class PaintSymmetryFacePickTests {

	[Test]
	public void FacePickPlaneTracksAnchorRotation() {
		var go = new GameObject("SymFacePickAnchor");
		go.AddComponent<BoxCollider>();
		Physics.SyncTransforms();
		try {
			Assert.That(Physics.Raycast(new Ray(new Vector3(0f, 0f, -2f), Vector3.forward), out RaycastHit hit, 10f),
				Is.True, "box should be hittable");

			var hostGo = new GameObject("BrushRibbonHost");
			var host = hostGo.AddComponent<BrushRibbon_UI_Size>();
			try {
				host.ApplySymmetryPlaneFromFaceHit(hit);
				Vector3 pointBefore = host.symmetryPlanePointWorld;
				Vector3 normalBefore = host.symmetryPlaneNormalWorld;

				go.transform.Rotate(0f, 90f, 0f, SpaceAndSpace.World);

				Vector3 pointAfter = host.symmetryPlanePointWorld;
				Vector3 normalAfter = host.symmetryPlaneNormalWorld;

				Assert.That((pointAfter - pointBefore).sqrMagnitude, Is.GreaterThan(1e-6f),
					"face-pick plane point must move with the mesh");
				Assert.That(Vector3.Dot(normalBefore.normalized, normalAfter.normalized), Is.LessThan(0.5f),
					"face-pick plane normal must rotate with the mesh");

				go.transform.Rotate(0f, -90f, 0f, SpaceAndSpace.World);
				Assert.That(Vector3.Distance(host.symmetryPlanePointWorld, pointBefore), Is.LessThan(1e-4f));
				Assert.That(Vector3.Dot(host.symmetryPlaneNormalWorld.normalized, normalBefore.normalized),
					Is.GreaterThan(0.999f));
			} finally {
				Object.DestroyImmediate(hostGo);
			}
		} finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void ReflectAcrossPlaneIsInvolution() {
		Vector3 planePoint = new Vector3(1, 2, 3);
		Vector3 planeNormal = new Vector3(0.2f, 0.8f, -0.3f).normalized;
		Vector3 p = new Vector3(-4, 5, 0.5f);
		Vector3 once = PaintSymmetryMesh.ReflectAcrossPlane(p, planePoint, planeNormal);
		Vector3 twice = PaintSymmetryMesh.ReflectAcrossPlane(once, planePoint, planeNormal);
		Assert.That(Vector3.Distance(twice, p), Is.LessThan(1e-5f));
		float d0 = Vector3.Dot(p - planePoint, planeNormal);
		float d1 = Vector3.Dot(once - planePoint, planeNormal);
		Assert.That(d1, Is.EqualTo(-d0).Within(1e-5f));
	}
}
