using UnityEngine;

namespace spz {
	/// <summary>
	/// Shared, persisted transform applied after SPZ's authored Unity → Blender conversion.
	/// FBX and direct mesh streaming both consume this basis so their results cannot drift.
	/// </summary>
	public static class ExportAxisSettings {
		public enum AxisOrder { XYZ, XZY, YXZ, YZX, ZXY, ZYX }

		public const string AxisOrderLabel = "Export axis order";
		public const string FlipXLabel = "Export flip X";
		public const string FlipYLabel = "Export flip Y";
		public const string FlipZLabel = "Export flip Z";

		public const string AxisOrderPrefKey = "spz.export.axis_order";
		public const string FlipXPrefKey = "spz.export.flip_x";
		public const string FlipYPrefKey = "spz.export.flip_y";
		public const string FlipZPrefKey = "spz.export.flip_z";

		public static readonly string[] AxisOrderNames = {
			"XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX"
		};

		public static AxisOrder Order {
			get => (AxisOrder)Mathf.Clamp(PlayerPrefs.GetInt(AxisOrderPrefKey, 0), 0, AxisOrderNames.Length - 1);
			set {
				PlayerPrefs.SetInt(AxisOrderPrefKey, Mathf.Clamp((int)value, 0, AxisOrderNames.Length - 1));
				PlayerPrefs.Save();
			}
		}

		public static bool FlipX {
			get => PlayerPrefs.GetInt(FlipXPrefKey, 0) != 0;
			set => SetBool(FlipXPrefKey, value);
		}

		public static bool FlipY {
			get => PlayerPrefs.GetInt(FlipYPrefKey, 0) != 0;
			set => SetBool(FlipYPrefKey, value);
		}

		public static bool FlipZ {
			get => PlayerPrefs.GetInt(FlipZPrefKey, 0) != 0;
			set => SetBool(FlipZPrefKey, value);
		}

		public static int AxisOrderIndex => (int)Order;
		public static bool IsDefault => Snapshot().IsDefault;

		/// <summary>
		/// Immutable copy of the persisted basis. Hot geometry loops must snapshot ONCE and reuse it:
		/// the static properties each hit <see cref="PlayerPrefs"/>, so per-vertex/per-triangle use
		/// would cost millions of prefs reads on the main thread during a large export. Snapshotting
		/// also guarantees vertices and winding agree even if the user toggles mid-export.
		/// </summary>
		public readonly struct Basis {
			public readonly AxisOrder Order;
			public readonly bool FlipX;
			public readonly bool FlipY;
			public readonly bool FlipZ;

			public Basis(AxisOrder order, bool flipX, bool flipY, bool flipZ) {
				Order = order;
				FlipX = flipX;
				FlipY = flipY;
				FlipZ = flipZ;
			}

			public bool IsDefault => Order == AxisOrder.XYZ && !FlipX && !FlipY && !FlipZ;

			/// <summary>Maps a vector already expressed in standard Blender output coordinates.</summary>
			public Vector3 MapOutput(Vector3 value) {
				Vector3 p;
				switch (Order) {
					case AxisOrder.XZY: p = new Vector3(value.x, value.z, value.y); break;
					case AxisOrder.YXZ: p = new Vector3(value.y, value.x, value.z); break;
					case AxisOrder.YZX: p = new Vector3(value.y, value.z, value.x); break;
					case AxisOrder.ZXY: p = new Vector3(value.z, value.x, value.y); break;
					case AxisOrder.ZYX: p = new Vector3(value.z, value.y, value.x); break;
					default: p = value; break;
				}
				if (FlipX) p.x = -p.x;
				if (FlipY) p.y = -p.y;
				if (FlipZ) p.z = -p.z;
				return p;
			}

			/// <summary>
			/// Exact inverse of <see cref="MapOutput"/>. MapOutput permutes then flips, so undoing it
			/// means flipping first and then applying the inverse permutation (YZX and ZXY invert each
			/// other; the remaining orders are self-inverse).
			/// </summary>
			public Vector3 MapInput(Vector3 value) {
				Vector3 p = value;
				if (FlipX) p.x = -p.x;
				if (FlipY) p.y = -p.y;
				if (FlipZ) p.z = -p.z;
				switch (Order) {
					case AxisOrder.XZY: return new Vector3(p.x, p.z, p.y);
					case AxisOrder.YXZ: return new Vector3(p.y, p.x, p.z);
					case AxisOrder.YZX: return new Vector3(p.z, p.x, p.y);
					case AxisOrder.ZXY: return new Vector3(p.y, p.z, p.x);
					case AxisOrder.ZYX: return new Vector3(p.z, p.y, p.x);
					default: return p;
				}
			}

			/// <summary>
			/// Correction for a vertex that Assimp already converted to Unity space, so that
			/// export → external edit → import is the identity for any basis.
			///
			/// Export is C(B(p)) where B swaps Unity y/z into standard output space; Assimp's fixed
			/// conversion undoes only B, leaving B(C(B(p))). Undoing that is B(C^-1(B(v))). B is its
			/// own inverse, which is why it appears on both sides.
			/// </summary>
			public Vector3 MapImportedUnityVertex(Vector3 unityVertex) {
				Vector3 standard = new Vector3(unityVertex.x, unityVertex.z, unityVertex.y);
				Vector3 undone = MapInput(standard);
				return new Vector3(undone.x, undone.z, undone.y);
			}

			/// <summary>True when the optional output mapping changes handedness.</summary>
			public bool FlipsHandedness {
				get {
					bool oddPermutation = Order == AxisOrder.XZY
						|| Order == AxisOrder.YXZ
						|| Order == AxisOrder.ZYX;
					bool oddFlips = FlipX ^ FlipY ^ FlipZ;
					return oddPermutation ^ oddFlips;
				}
			}

			/// <summary>
			/// Correction in FBX Y-up coordinates equivalent to <see cref="MapOutput"/> after Blender's
			/// fixed FBX import basis B(fbx)=(x,-z,y): D = B^-1 * C * B.
			/// </summary>
			public Matrix4x4 GetFbxCorrectionMatrix() {
				Matrix4x4 blenderFromFbx = Matrix4x4.identity;
				blenderFromFbx.SetColumn(0, new Vector4(1f, 0f, 0f, 0f));
				blenderFromFbx.SetColumn(1, new Vector4(0f, 0f, 1f, 0f));
				blenderFromFbx.SetColumn(2, new Vector4(0f, -1f, 0f, 0f));

				Matrix4x4 outputCorrection = Matrix4x4.identity;
				Vector3 cx = MapOutput(Vector3.right);
				Vector3 cy = MapOutput(Vector3.up);
				Vector3 cz = MapOutput(Vector3.forward);
				outputCorrection.SetColumn(0, new Vector4(cx.x, cx.y, cx.z, 0f));
				outputCorrection.SetColumn(1, new Vector4(cy.x, cy.y, cy.z, 0f));
				outputCorrection.SetColumn(2, new Vector4(cz.x, cz.y, cz.z, 0f));

				return blenderFromFbx.inverse * outputCorrection * blenderFromFbx;
			}
		}

		/// <summary>Read the persisted basis once. Use this before any per-vertex / per-triangle loop.</summary>
		public static Basis Snapshot() => new Basis(Order, FlipX, FlipY, FlipZ);

		static void SetBool(string key, bool value) {
			PlayerPrefs.SetInt(key, value ? 1 : 0);
			PlayerPrefs.Save();
		}

		public static void SetAxisOrderIndex(int index) {
			Order = (AxisOrder)Mathf.Clamp(index, 0, AxisOrderNames.Length - 1);
		}

		/// <summary>Convenience wrapper over <see cref="Snapshot"/>. Never call this inside a geometry loop.</summary>
		public static Vector3 MapOutput(Vector3 value) => Snapshot().MapOutput(value);

		/// <summary>True when the optional output mapping changes handedness.</summary>
		public static bool FlipsHandedness => Snapshot().FlipsHandedness;

		/// <summary>Correction expressed in FBX Y-up space (see <see cref="Basis.GetFbxCorrectionMatrix"/>).</summary>
		public static Matrix4x4 GetFbxCorrectionMatrix() => Snapshot().GetFbxCorrectionMatrix();
	}
}
