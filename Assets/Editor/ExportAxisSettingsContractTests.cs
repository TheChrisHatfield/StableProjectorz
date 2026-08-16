using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace spz.EditorTests {
	public sealed class ExportAxisSettingsContractTests {
		bool _hadOrder, _hadX, _hadY, _hadZ;
		int _order, _x, _y, _z;

		[SetUp]
		public void SaveAndResetPrefs() {
			_hadOrder = PlayerPrefs.HasKey(ExportAxisSettings.AxisOrderPrefKey);
			_hadX = PlayerPrefs.HasKey(ExportAxisSettings.FlipXPrefKey);
			_hadY = PlayerPrefs.HasKey(ExportAxisSettings.FlipYPrefKey);
			_hadZ = PlayerPrefs.HasKey(ExportAxisSettings.FlipZPrefKey);
			_order = PlayerPrefs.GetInt(ExportAxisSettings.AxisOrderPrefKey);
			_x = PlayerPrefs.GetInt(ExportAxisSettings.FlipXPrefKey);
			_y = PlayerPrefs.GetInt(ExportAxisSettings.FlipYPrefKey);
			_z = PlayerPrefs.GetInt(ExportAxisSettings.FlipZPrefKey);
			PlayerPrefs.DeleteKey(ExportAxisSettings.AxisOrderPrefKey);
			PlayerPrefs.DeleteKey(ExportAxisSettings.FlipXPrefKey);
			PlayerPrefs.DeleteKey(ExportAxisSettings.FlipYPrefKey);
			PlayerPrefs.DeleteKey(ExportAxisSettings.FlipZPrefKey);
		}

		[TearDown]
		public void RestorePrefs() {
			Restore(ExportAxisSettings.AxisOrderPrefKey, _hadOrder, _order);
			Restore(ExportAxisSettings.FlipXPrefKey, _hadX, _x);
			Restore(ExportAxisSettings.FlipYPrefKey, _hadY, _y);
			Restore(ExportAxisSettings.FlipZPrefKey, _hadZ, _z);
			PlayerPrefs.Save();
		}

		static void Restore(string key, bool had, int value) {
			if (had) PlayerPrefs.SetInt(key, value);
			else PlayerPrefs.DeleteKey(key);
		}

		[Test]
		public void Defaults_PreserveCurrentOutput() {
			Assert.That(ExportAxisSettings.IsDefault, Is.True);
			Assert.That(ExportAxisSettings.MapOutput(new Vector3(1f, 2f, 3f)),
				Is.EqualTo(new Vector3(1f, 2f, 3f)));
			Assert.That(ExportAxisSettings.FlipsHandedness, Is.False);
		}

		[TestCase(ExportAxisSettings.AxisOrder.XYZ, 1f, 2f, 3f)]
		[TestCase(ExportAxisSettings.AxisOrder.XZY, 1f, 3f, 2f)]
		[TestCase(ExportAxisSettings.AxisOrder.YXZ, 2f, 1f, 3f)]
		[TestCase(ExportAxisSettings.AxisOrder.YZX, 2f, 3f, 1f)]
		[TestCase(ExportAxisSettings.AxisOrder.ZXY, 3f, 1f, 2f)]
		[TestCase(ExportAxisSettings.AxisOrder.ZYX, 3f, 2f, 1f)]
		public void AxisOrders_MapExpectedComponents(ExportAxisSettings.AxisOrder order, float x, float y, float z) {
			ExportAxisSettings.Order = order;
			Assert.That(ExportAxisSettings.MapOutput(new Vector3(1f, 2f, 3f)),
				Is.EqualTo(new Vector3(x, y, z)));
		}

		[Test]
		public void Flips_ApplyAfterPermutation() {
			ExportAxisSettings.Order = ExportAxisSettings.AxisOrder.YZX;
			ExportAxisSettings.FlipX = true;
			ExportAxisSettings.FlipZ = true;
			Assert.That(ExportAxisSettings.MapOutput(new Vector3(1f, 2f, 3f)),
				Is.EqualTo(new Vector3(-2f, 3f, -1f)));
		}

		[Test]
		public void Handedness_TracksPermutationAndFlipParity() {
			ExportAxisSettings.Order = ExportAxisSettings.AxisOrder.XZY;
			Assert.That(ExportAxisSettings.FlipsHandedness, Is.True, "one axis swap is odd");
			ExportAxisSettings.FlipX = true;
			Assert.That(ExportAxisSettings.FlipsHandedness, Is.False, "swap plus one sign flip is even");
		}

		[Test]
		public void FbxCorrection_ComposesToSameBlenderOutputMapping() {
			ExportAxisSettings.Order = ExportAxisSettings.AxisOrder.ZXY;
			ExportAxisSettings.FlipY = true;
			Vector3 fbx = new Vector3(2f, 3f, 5f);
			Vector3 standardBlender = new Vector3(fbx.x, -fbx.z, fbx.y);
			Vector3 correctedFbx = ExportAxisSettings.GetFbxCorrectionMatrix().MultiplyVector(fbx);
			Vector3 imported = new Vector3(correctedFbx.x, -correctedFbx.z, correctedFbx.y);
			Assert.That(imported, Is.EqualTo(ExportAxisSettings.MapOutput(standardBlender)));
		}

		[Test]
		public void UiAndWriters_ConsumeSharedSettings() {
			string root = Directory.GetCurrentDirectory();
			string ui = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs"));
			string fbx = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "3D Models", "ModelsHandler_SaveFBX_Helper.cs"));
			string stream = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "AddonSystem", "SpzGoMeshStream.cs"));
			string py = File.ReadAllText(Path.Combine(root, "Assets", "StreamingAssets", "Addons", "StableProjectorzGO", "__init__.py"));
			Assert.That(ui, Does.Contain("ExportAxisSettings.AxisOrderLabel"));
			Assert.That(ui, Does.Contain("PersistSpzGoExportToggleIfNeeded"));
			Assert.That(fbx, Does.Contain("CreateExportAxisCorrectionNode"));
			Assert.That(fbx, Does.Contain("ExportAxisSettings.Snapshot()"));
			Assert.That(stream, Does.Contain("ExportAxisSettings.Snapshot()"));
			Assert.That(py, Does.Contain("Export axis order"));
			Assert.That(py, Does.Contain("Export flip Z"));
		}

		[Test]
		public void Snapshot_MatchesStaticAccessorsAndIsSelfConsistent() {
			ExportAxisSettings.SetAxisOrderIndex((int)ExportAxisSettings.AxisOrder.ZXY);
			ExportAxisSettings.FlipX = true;
			ExportAxisSettings.FlipZ = true;

			var basis = ExportAxisSettings.Snapshot();
			Assert.That(basis.Order, Is.EqualTo(ExportAxisSettings.Order));
			Assert.That(basis.FlipX, Is.True);
			Assert.That(basis.FlipY, Is.False);
			Assert.That(basis.FlipZ, Is.True);
			Assert.That(basis.MapOutput(new Vector3(1f, 2f, 3f)),
				Is.EqualTo(ExportAxisSettings.MapOutput(new Vector3(1f, 2f, 3f))));
			Assert.That(basis.FlipsHandedness, Is.EqualTo(ExportAxisSettings.FlipsHandedness));
			Assert.That(basis.GetFbxCorrectionMatrix(), Is.EqualTo(ExportAxisSettings.GetFbxCorrectionMatrix()));
		}

		[Test]
		public void Snapshot_IsStableWhilePrefsChangeMidExport() {
			ExportAxisSettings.SetAxisOrderIndex((int)ExportAxisSettings.AxisOrder.XYZ);
			ExportAxisSettings.FlipX = false;

			var basis = ExportAxisSettings.Snapshot();
			bool windingBefore = basis.FlipsHandedness;

			// Simulate the user toggling a flip while geometry is still streaming.
			ExportAxisSettings.FlipX = true;

			Assert.That(basis.FlipX, Is.False, "snapshot must not observe later pref writes");
			Assert.That(basis.FlipsHandedness, Is.EqualTo(windingBefore),
				"winding parity must stay fixed for the whole export");
			Assert.That(basis.MapOutput(new Vector3(1f, 2f, 3f)), Is.EqualTo(new Vector3(1f, 2f, 3f)));
		}

		[Test]
		public void MapInput_InvertsMapOutputForEveryBasis() {
			var probe = new Vector3(1.5f, -2.25f, 4f);
			foreach (ExportAxisSettings.AxisOrder order in System.Enum.GetValues(typeof(ExportAxisSettings.AxisOrder))) {
				for (int mask = 0; mask < 8; mask++) {
					var basis = new ExportAxisSettings.Basis(order,
						(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
					Vector3 roundTripped = basis.MapInput(basis.MapOutput(probe));
					Assert.That(roundTripped, Is.EqualTo(probe),
						$"MapInput must invert MapOutput for {order} flips={mask}");
				}
			}
		}

		[Test]
		public void ImportCorrection_MakesExportImportIdentityForEveryBasis() {
			var unityVertex = new Vector3(3f, -1f, 0.5f);
			foreach (ExportAxisSettings.AxisOrder order in System.Enum.GetValues(typeof(ExportAxisSettings.AxisOrder))) {
				for (int mask = 0; mask < 8; mask++) {
					var basis = new ExportAxisSettings.Basis(order,
						(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);

					// Export: Unity -> standard output space (swap y/z) -> user basis.
					Vector3 exported = basis.MapOutput(new Vector3(unityVertex.x, unityVertex.z, unityVertex.y));
					// Assimp's fixed conversion undoes only the y/z swap.
					Vector3 afterAssimp = new Vector3(exported.x, exported.z, exported.y);
					// Import correction must recover the original Unity vertex.
					Assert.That(basis.MapImportedUnityVertex(afterAssimp), Is.EqualTo(unityVertex),
						$"round trip must be identity for {order} flips={mask}");
				}
			}
		}

		[Test]
		public void ImportPath_AppliesTheInverseBasis() {
			string root = Directory.GetCurrentDirectory();
			string loader = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "3D Models", "AssimpLoader.cs"));
			// Export and import are documented as exact inverses; a one-way basis would silently break
			// the SPZ GO round trip for any non-default setting.
			Assert.That(loader, Does.Contain("ExportAxisSettings.Snapshot()"),
				"import must read the shared basis once per load");
			Assert.That(loader, Does.Contain("MapImportedUnityVertex"),
				"imported vertices must be mapped back out of the user's external basis");
			Assert.That(loader, Does.Contain("_axisBasis.FlipsHandedness"),
				"a mirroring basis must flip imported winding back");
		}

		[Test]
		public void GeometryLoops_DoNotReadPlayerPrefsPerElement() {
			string root = Directory.GetCurrentDirectory();
			string stream = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "AddonSystem", "SpzGoMeshStream.cs"));
			string fbx = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "3D Models", "ModelsHandler_SaveFBX_Helper.cs"));

			// The static accessors hit PlayerPrefs on every call, so per-vertex / per-triangle use is a
			// hard perf regression. Hot loops must go through the snapshotted basis instead.
			Assert.That(stream, Does.Not.Contain("ExportAxisSettings.MapOutput("),
				"mesh stream must map vertices through the snapshot, not the PlayerPrefs-backed static");
			Assert.That(stream, Does.Not.Contain("ExportAxisSettings.FlipsHandedness"),
				"mesh stream must resolve winding once from the snapshot");
			Assert.That(fbx, Does.Not.Contain("ExportAxisSettings.FlipsHandedness"),
				"FBX winding must come from the per-export snapshot");
			Assert.That(fbx, Does.Not.Contain("ExportAxisSettings.IsDefault"),
				"FBX correction node must use the same snapshot as the winding");
		}
	}
}
