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
			Assert.That(py, Does.Contain(ExportAxisSettings.FlipLabel));
			Assert.That(py, Does.Not.Contain("add_toggle(\"Export flip"),
				"Flips are one dropdown now; loose per-axis toggles must not reappear beside it.");
		}

		[Test]
		public void ProjectLoad_IgnoresTheExportAxisPreference() {
			string root = Directory.GetCurrentDirectory();
			string helper = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs"));
			string loader = File.ReadAllText(Path.Combine(root, "Assets", "_gm", "Features", "3D Models", "AssimpLoader.cs"));

			// Project load re-imports the project's own saved mesh bytes. Interpreting those through the
			// current EXPORT preference means a UI toggle silently changes the shape of an already-saved
			// project, while its stored paint and UV data still describe the original orientation.
			Assert.That(helper, Does.Contain("ImportModel_via_Filepath(fp, applyExportAxisBasis: false)"),
				"project load must opt out of the export axis basis");
			Assert.That(loader, Does.Contain("bool applyExportAxisBasis = true"),
				"the loader must accept the opt-out");
			Assert.That(loader, Does.Contain("applyExportAxisBasis\n\t            ? ExportAxisSettings.Snapshot()")
				.Or.Contain("applyExportAxisBasis"),
				"the snapshot must be conditional");

			// Interchange with the user's external tool keeps the round-trip correction.
			Assert.That(helper, Does.Contain("bool applyExportAxisBasis = true"),
				"ordinary imports must still default to honouring the basis");
		}

		[Test]
		public void OptedOutImport_UsesAnIdentityBasis() {
			// The opt-out must land on a basis that provably does nothing, not merely a different one.
			var identity = new ExportAxisSettings.Basis(ExportAxisSettings.AxisOrder.XYZ, false, false, false);
			Assert.That(identity.IsDefault, Is.True);
			var probe = new Vector3(1f, 2f, 3f);
			Assert.That(identity.MapImportedUnityVertex(probe), Is.EqualTo(probe),
				"an opted-out import must return vertices untouched");
			Assert.That(identity.FlipsHandedness, Is.False,
				"an opted-out import must not reverse winding");
		}

		[Test]
		public void FlipDropdownOptions_MatchAcrossPythonAndNativePanel() {
			// Unity persists the flip by INDEX, so a python list in a different order would silently
			// apply the wrong axis — picking "Y" would flip something else.
			string py = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
				"Assets", "StreamingAssets", "Addons", "StableProjectorzGO", "__init__.py"));
			int start = py.IndexOf("\"" + ExportAxisSettings.FlipLabel + "\"", System.StringComparison.Ordinal);
			Assert.That(start, Is.GreaterThan(0), "python panel must declare the flip dropdown");
			int open = py.IndexOf('[', start);
			int close = py.IndexOf(']', open);
			Assert.That(open, Is.GreaterThan(0));
			Assert.That(close, Is.GreaterThan(open));
			string list = py.Substring(open + 1, close - open - 1);
			foreach (string name in ExportAxisSettings.FlipNames) {
				Assert.That(list, Does.Contain("\"" + name + "\""), "missing option " + name);
			}
			// Same count, same order.
			int cursor = -1;
			foreach (string name in ExportAxisSettings.FlipNames) {
				int at = list.IndexOf("\"" + name + "\"", System.StringComparison.Ordinal);
				Assert.That(at, Is.GreaterThan(cursor), "option order diverged at " + name);
				cursor = at;
			}
			Assert.That(list.Split('"').Length - 1, Is.EqualTo(ExportAxisSettings.FlipNames.Length * 2),
				"python flip list must hold exactly the shared options");
		}

		[Test]
		public void FlipIndex_RoundTripsEveryCombination() {
			for (int i = 0; i < ExportAxisSettings.FlipNames.Length; i++) {
				ExportAxisSettings.SetFlipIndex(i);
				Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(i),
					"selection " + ExportAxisSettings.FlipNames[i] + " must survive a round trip");
			}
			// Every distinct flag combination must be reachable, or a basis becomes unselectable.
			var seen = new System.Collections.Generic.HashSet<int>();
			for (int i = 0; i < ExportAxisSettings.FlipNames.Length; i++) {
				ExportAxisSettings.SetFlipIndex(i);
				int mask = (ExportAxisSettings.FlipX ? 1 : 0)
					| (ExportAxisSettings.FlipY ? 2 : 0)
					| (ExportAxisSettings.FlipZ ? 4 : 0);
				Assert.That(seen.Add(mask), Is.True, "duplicate flip combination at index " + i);
			}
			Assert.That(seen.Count, Is.EqualTo(8));
		}

		[Test]
		public void FlipIndex_LabelsMatchTheFlagsTheySet() {
			for (int i = 0; i < ExportAxisSettings.FlipNames.Length; i++) {
				ExportAxisSettings.SetFlipIndex(i);
				string label = ExportAxisSettings.FlipNames[i];
				Assert.That(ExportAxisSettings.FlipX, Is.EqualTo(label.Contains("X")), label + " / X");
				Assert.That(ExportAxisSettings.FlipY, Is.EqualTo(label.Contains("Y")), label + " / Y");
				Assert.That(ExportAxisSettings.FlipZ, Is.EqualTo(label.Contains("Z")), label + " / Z");
			}
		}

		[Test]
		public void FlipIndex_ClampsOutOfRangeSelection() {
			ExportAxisSettings.SetFlipIndex(-5);
			Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(0));
			ExportAxisSettings.SetFlipIndex(999);
			Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(ExportAxisSettings.FlipNames.Length - 1));
		}

		[Test]
		public void NativePanel_ShowsFlipsAsDropdownAndRetiresOldToggles() {
			string ui = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
				"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs"));
			Assert.That(ui, Does.Contain("ExportAxisSettings.FlipLabel"));
			Assert.That(ui, Does.Contain("ExportAxisSettings.FlipNames"));
			Assert.That(ui, Does.Contain("ExportAxisSettings.SetFlipIndex"));
			Assert.That(ui, Does.Not.Contain("AddToggle(StableProjectorzGoAddonId, panelId, ExportAxisSettings.FlipXLabel"),
				"the native panel must not seed loose flip toggles any more");
			// A panel seeded by an older session keeps its toggles unless they are explicitly pruned.
			Assert.That(ui, Does.Contain("RemoveNamedControls(StableProjectorzGoAddonId, panel, \"Toggle_\" + ExportAxisSettings.FlipXLabel"));
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
