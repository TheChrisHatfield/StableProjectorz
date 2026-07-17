using System;
using Newtonsoft.Json;
using UnityEngine;

namespace spz {

	/// <summary>JSON payload from <c>scripts/export_unity_weights.py</c> (T9.2 → T5).</summary>
	[Serializable]
	public sealed class ValuePaintMlpWeightsDto {
		public string version;
		public string arch;
		public int feature_dim = 7;
		public int hidden = 64;
		public int hidden2 = 32;
		public float[] trunk0_weight;
		public float[] trunk0_bias;
		public float[] trunk1_weight;
		public float[] trunk1_bias;
		public float[] cur_weight;
		public float[] cur_bias;
		public float[] des_weight;
		public float[] des_bias;
		public float[] role_weight;
		public float[] role_bias;
		public float[] cont_weight;
		public float[] cont_bias;

		public static bool TryLoadFromResources(string resourcesPath, out ValuePaintMlpWeightsDto dto, out string error) {
			dto = null;
			error = null;
			var ta = Resources.Load<TextAsset>(resourcesPath);
			if (ta == null) {
				error = "Resources.Load missed TextAsset at '" + resourcesPath + "'";
				return false;
			}
			try {
				dto = JsonConvert.DeserializeObject<ValuePaintMlpWeightsDto>(ta.text);
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
			if (dto == null || dto.trunk0_weight == null || dto.cont_weight == null) {
				error = "weights JSON incomplete";
				return false;
			}
			return true;
		}
	}

	/// <summary>CPU forward for T9.2 MultiHead (eval mode: ReLU, sigmoid on cont, no dropout).</summary>
	public sealed class ValuePaintMlpRuntime {
		readonly int _in;
		readonly int _h1;
		readonly int _h2;
		readonly float[] _t0w, _t0b, _t1w, _t1b;
		readonly float[] _curW, _curB, _desW, _desB, _roleW, _roleB, _contW, _contB;
		readonly float[] _hA, _hB, _logits;

		public ValuePaintMlpRuntime(ValuePaintMlpWeightsDto w) {
			_in = w.feature_dim > 0 ? w.feature_dim : 7;
			_h1 = w.hidden > 0 ? w.hidden : 64;
			_h2 = w.hidden2 > 0 ? w.hidden2 : 32;
			_t0w = w.trunk0_weight;
			_t0b = w.trunk0_bias;
			_t1w = w.trunk1_weight;
			_t1b = w.trunk1_bias;
			_curW = w.cur_weight;
			_curB = w.cur_bias;
			_desW = w.des_weight;
			_desB = w.des_bias;
			_roleW = w.role_weight;
			_roleB = w.role_bias;
			_contW = w.cont_weight;
			_contB = w.cont_bias;
			_hA = new float[_h1];
			_hB = new float[_h2];
			_logits = new float[5];
			Validate();
		}

		void Validate() {
			Expect(_t0w, _h1 * _in, "trunk0_weight");
			Expect(_t0b, _h1, "trunk0_bias");
			Expect(_t1w, _h2 * _h1, "trunk1_weight");
			Expect(_t1b, _h2, "trunk1_bias");
			Expect(_curW, 5 * _h2, "cur_weight");
			Expect(_curB, 5, "cur_bias");
			Expect(_desW, 5 * _h2, "des_weight");
			Expect(_desB, 5, "des_bias");
			Expect(_roleW, 5 * _h2, "role_weight");
			Expect(_roleB, 5, "role_bias");
			Expect(_contW, 4 * _h2, "cont_weight");
			Expect(_contB, 4, "cont_bias");
		}

		static void Expect(float[] a, int n, string name) {
			if (a == null || a.Length != n)
				throw new InvalidOperationException(name + " len=" + (a == null ? -1 : a.Length) + " expected " + n);
		}

		public struct Output {
			public int CurrentBin;
			public int DesiredBin;
			public int StrokeRole;
			public float Blend01;
			public float EdgeSoft01;
			public float Width01;
			public float Opacity01;
		}

		public Output Forward(float[] features7) {
			if (features7 == null || features7.Length < _in)
				throw new ArgumentException("features");
			LinearRelu(_t0w, _t0b, features7, _in, _hA, _h1);
			LinearRelu(_t1w, _t1b, _hA, _h1, _hB, _h2);
			Linear(_curW, _curB, _hB, _h2, _logits, 5);
			int cur = ArgMax(_logits, 5);
			Linear(_desW, _desB, _hB, _h2, _logits, 5);
			int des = ArgMax(_logits, 5);
			Linear(_roleW, _roleB, _hB, _h2, _logits, 5);
			int role = ArgMax(_logits, 5);
			// cont: 4 outs + sigmoid
			float blend = Sigmoid(DotRow(_contW, 0, _h2, _hB) + _contB[0]);
			float edge = Sigmoid(DotRow(_contW, 1, _h2, _hB) + _contB[1]);
			float width = Sigmoid(DotRow(_contW, 2, _h2, _hB) + _contB[2]);
			float op = Sigmoid(DotRow(_contW, 3, _h2, _hB) + _contB[3]);
			return new Output {
				CurrentBin = cur,
				DesiredBin = des,
				StrokeRole = role,
				Blend01 = blend,
				EdgeSoft01 = edge,
				Width01 = width,
				Opacity01 = op,
			};
		}

		static void LinearRelu(float[] w, float[] b, float[] x, int inDim, float[] y, int outDim) {
			for (int o = 0; o < outDim; o++) {
				float s = b[o] + DotRow(w, o, inDim, x);
				y[o] = s > 0f ? s : 0f;
			}
		}

		static void Linear(float[] w, float[] b, float[] x, int inDim, float[] y, int outDim) {
			for (int o = 0; o < outDim; o++)
				y[o] = b[o] + DotRow(w, o, inDim, x);
		}

		static float DotRow(float[] w, int row, int inDim, float[] x) {
			int off = row * inDim;
			float s = 0f;
			for (int i = 0; i < inDim; i++)
				s += w[off + i] * x[i];
			return s;
		}

		static int ArgMax(float[] v, int n) {
			int best = 0;
			float bv = v[0];
			for (int i = 1; i < n; i++) {
				if (v[i] > bv) {
					bv = v[i];
					best = i;
				}
			}
			return best;
		}

		static float Sigmoid(float z) {
			if (z > 20f) return 1f;
			if (z < -20f) return 0f;
			return 1f / (1f + Mathf.Exp(-z));
		}
	}

}
