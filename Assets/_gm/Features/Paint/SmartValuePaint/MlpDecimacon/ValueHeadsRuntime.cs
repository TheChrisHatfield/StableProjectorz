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
			try {
				string path = Path.Combine(Application.streamingAssetsPath, StreamingRelative.Replace('/', Path.DirectorySeparatorChar));
				if (File.Exists(path)) {
					dto = JsonConvert.DeserializeObject<ValueHeadsWeightsDto>(File.ReadAllText(path));
				} else {
					var ta = Resources.Load<TextAsset>(ResourcesPath);
					if (ta == null) {
						error = "missing value heads at StreamingAssets/" + StreamingRelative;
						return false;
					}
					dto = JsonConvert.DeserializeObject<ValueHeadsWeightsDto>(ta.text);
				}
				if (dto == null || dto.des_weight == null || dto.cur_weight == null) {
					error = "invalid value heads json";
					return false;
				}
				return true;
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
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
		}

		public ValueHeadsRuntime(ValueHeadsWeightsDto w) {
			_w = w ?? throw new ArgumentNullException(nameof(w));
			_width = w.width > 0 ? w.width : DecimaconDims.Width;
		}

		public static bool TryCreate(out ValueHeadsRuntime runtime, out string error) {
			runtime = null;
			if (!ValueHeadsWeightsDto.TryLoad(out var dto, out error)) return false;
			runtime = new ValueHeadsRuntime(dto);
			return true;
		}

		public Output Forward(float[] fused, float[] features7) {
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
			return new Output {
				CurrentBin = ArgMax(logitsCur),
				DesiredBin = ArgMax(logitsDes),
				StrokeRole = ArgMax(logitsRole),
				Blend01 = Sigmoid(cont[0]),
				EdgeSoft01 = Sigmoid(cont[1]),
				Width01 = Sigmoid(cont[2]),
				Opacity01 = Sigmoid(cont[3]),
			};
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
