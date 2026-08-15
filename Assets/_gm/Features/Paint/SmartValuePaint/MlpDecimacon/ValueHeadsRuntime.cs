using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>Paint value heads on Decimacon width-96 latent (SVP Stage-2 / Pass D).</summary>
	public sealed class ValueHeadsWeightsDto {
		public string version;
		public string arch;
		public int width = DecimaconDims.Width;
		public int feature_dim = 7;
		public float[] cur_weight;
		public float[] cur_bias;
		public float[] des_weight;
		public float[] des_bias;
		public float[] role_weight;
		public float[] role_bias;
		public float[] cont_weight;
		public float[] cont_bias;
		public float[] feat_proj_weight;
		public float[] feat_proj_bias;
		public float[] feat_proj2_weight;
		public float[] feat_proj2_bias;

		public const string StreamingRelative = "MlpDecimacon/value_heads_weights.json";
		public const string ResourcesPath = "SmartValuePaint/MlpDecimacon/value_heads_weights";

		public static bool TryLoad(out ValueHeadsWeightsDto dto, out string error) {
			dto = null;
			error = null;
			string source = "(unresolved)";
			try {
				string path = Path.Combine(Application.streamingAssetsPath, StreamingRelative.Replace('/', Path.DirectorySeparatorChar));
				if (File.Exists(path)) {
					source = path;
					dto = JsonConvert.DeserializeObject<ValueHeadsWeightsDto>(File.ReadAllText(path));
				} else {
					source = "Resources/" + ResourcesPath;
					var ta = Resources.Load<TextAsset>(ResourcesPath);
					if (ta == null) {
						error = "missing value heads: no file at " + path + " and no TextAsset at " + source;
						return false;
					}
					dto = JsonConvert.DeserializeObject<ValueHeadsWeightsDto>(ta.text);
				}
				if (dto == null) {
					error = "value heads json deserialized to null (" + source + ")";
					return false;
				}
				if (!dto.Validate(out string invalidKey)) {
					error = "invalid value heads json: " + invalidKey + " (" + source + ")";
					return false;
				}
				return true;
			} catch (Exception e) {
				error = e.Message + " (" + source + ")";
				return false;
			}
		}

		/// <summary>
		/// Shape-validate every tensor the forward pass indexes (brush-behavior B8.5).
		/// A truncated or mis-exported head must fail here with the offending key, not
		/// silently produce garbage proposals.
		/// </summary>
		public bool Validate(out string invalidKey) {
			int w = width > 0 ? width : DecimaconDims.Width;
			int f = feature_dim > 0 ? feature_dim : 7;
			if (w <= 0) { invalidKey = "width=" + width; return false; }
			if (!Check(cur_weight, 5 * w, "cur_weight", out invalidKey)) return false;
			if (!Check(cur_bias, 5, "cur_bias", out invalidKey)) return false;
			if (!Check(des_weight, 5 * w, "des_weight", out invalidKey)) return false;
			if (!Check(des_bias, 5, "des_bias", out invalidKey)) return false;
			if (!Check(role_weight, 5 * w, "role_weight", out invalidKey)) return false;
			if (!Check(role_bias, 5, "role_bias", out invalidKey)) return false;
			if (!Check(cont_weight, 4 * w, "cont_weight", out invalidKey)) return false;
			if (!Check(cont_bias, 4, "cont_bias", out invalidKey)) return false;
			// Feature projection is the trained input path (z = proj(features7)); when any part
			// is present all of it must be, or the forward silently drops to the raw latent.
			bool anyProj = feat_proj_weight != null || feat_proj2_weight != null;
			if (anyProj) {
				if (!Check(feat_proj_weight, w * f, "feat_proj_weight", out invalidKey)) return false;
				if (!Check(feat_proj_bias, w, "feat_proj_bias", out invalidKey)) return false;
				if (!Check(feat_proj2_weight, w * w, "feat_proj2_weight", out invalidKey)) return false;
				if (!Check(feat_proj2_bias, w, "feat_proj2_bias", out invalidKey)) return false;
			}
			invalidKey = null;
			return true;
		}

		static bool Check(float[] a, int expected, string key, out string invalidKey) {
			if (a == null) { invalidKey = key + " missing"; return false; }
			if (a.Length != expected) {
				invalidKey = key + " length " + a.Length + " != expected " + expected;
				return false;
			}
			for (int i = 0; i < a.Length; i++) {
				if (!float.IsFinite(a[i])) {
					invalidKey = key + " non-finite at " + i;
					return false;
				}
			}
			invalidKey = null;
			return true;
		}
	}

	public sealed class ValueHeadsRuntime {
		readonly ValueHeadsWeightsDto _w;
		readonly int _width;
		readonly float[] _z = new float[DecimaconDims.Width];
		readonly float[] _mid = new float[DecimaconDims.Width];

		public struct Output {
			public int CurrentBin;
			public int DesiredBin;
			public int StrokeRole;
			public float Blend01;
			public float EdgeSoft01;
			public float Width01;
			public float Opacity01;
			public float DesiredConfidence01;
			public float CurrentConfidence01;
		}

		public ValueHeadsRuntime(ValueHeadsWeightsDto w) {
			_w = w ?? throw new ArgumentNullException(nameof(w));
			_width = w.width > 0 ? w.width : DecimaconDims.Width;
		}

		/// <summary>
		/// False when the trained feature projection drives the heads. Value heads were trained as
		/// <c>z = proj(features7)</c> (train_decimacon_value_heads.py), so the stage-DAG fused latent
		/// is deliberately not an input — see brush-behavior B8.7. Mixing it in breaks train parity.
		/// </summary>
		public bool UsesBodyLatent => _w.feat_proj_weight == null || _w.feat_proj2_weight == null;

		public static bool TryCreate(out ValueHeadsRuntime runtime, out string error) {
			runtime = null;
			if (!ValueHeadsWeightsDto.TryLoad(out var dto, out error)) return false;
			runtime = new ValueHeadsRuntime(dto);
			return true;
		}

		public Output Forward(float[] fused, float[] features7) {
			// Trained path: z = proj2(gelu(proj(features7))). `fused` is only the untrained
			// fallback latent (B8.7) — do not blend it in without retraining.
			if (_w.feat_proj_weight != null && _w.feat_proj2_weight != null && features7 != null) {
				Linear(_w.feat_proj_weight, _w.feat_proj_bias, features7, 7, _mid, _width);
				for (int i = 0; i < _width; i++)
					_mid[i] = Gelu(_mid[i]);
				Linear(_w.feat_proj2_weight, _w.feat_proj2_bias, _mid, _width, _z, _width);
			} else {
				for (int i = 0; i < _width; i++)
					_z[i] = fused != null && i < fused.Length ? fused[i] : 0f;
			}
			var logitsCur = new float[5];
			var logitsDes = new float[5];
			var logitsRole = new float[5];
			var cont = new float[4];
			Linear(_w.cur_weight, _w.cur_bias, _z, _width, logitsCur, 5);
			Linear(_w.des_weight, _w.des_bias, _z, _width, logitsDes, 5);
			Linear(_w.role_weight, _w.role_bias, _z, _width, logitsRole, 5);
			Linear(_w.cont_weight, _w.cont_bias, _z, _width, cont, 4);
			SoftmaxInPlace(logitsCur);
			SoftmaxInPlace(logitsDes);
			int curIx = ArgMax(logitsCur);
			int desIx = ArgMax(logitsDes);
			return new Output {
				CurrentBin = curIx,
				DesiredBin = desIx,
				StrokeRole = ArgMax(logitsRole),
				Blend01 = Sigmoid(cont[0]),
				EdgeSoft01 = Sigmoid(cont[1]),
				Width01 = Sigmoid(cont[2]),
				Opacity01 = Sigmoid(cont[3]),
				CurrentConfidence01 = logitsCur[curIx],
				DesiredConfidence01 = logitsDes[desIx],
			};
		}

		static void SoftmaxInPlace(float[] v) {
			float m = v[0];
			for (int i = 1; i < v.Length; i++) if (v[i] > m) m = v[i];
			float sum = 0f;
			for (int i = 0; i < v.Length; i++) {
				v[i] = Mathf.Exp(v[i] - m);
				sum += v[i];
			}
			float inv = sum > 1e-8f ? 1f / sum : 0f;
			for (int i = 0; i < v.Length; i++) v[i] *= inv;
		}

		static float Gelu(float x) =>
			0.5f * x * (1f + (float)System.Math.Tanh(0.7978845608 * (x + 0.044715 * x * x * x)));

		static void Linear(float[] w, float[] b, float[] x, int inDim, float[] y, int outDim) {
			for (int o = 0; o < outDim; o++) {
				float s = b != null && o < b.Length ? b[o] : 0f;
				int row = o * inDim;
				for (int i = 0; i < inDim && w != null && row + i < w.Length; i++)
					s += w[row + i] * (x != null && i < x.Length ? x[i] : 0f);
				y[o] = s;
			}
		}

		static int ArgMax(float[] v) {
			int best = 0;
			float bv = v[0];
			for (int i = 1; i < v.Length; i++) {
				if (v[i] > bv) { bv = v[i]; best = i; }
			}
			return best;
		}

		static float Sigmoid(float x) => 1f / (1f + Mathf.Exp(-x));
	}
}
