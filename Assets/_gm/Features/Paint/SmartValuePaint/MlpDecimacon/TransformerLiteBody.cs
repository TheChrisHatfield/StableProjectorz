using System;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>
	/// Soil TransformerLiteBody topology v2 — deterministic CPU weights (production soil body path).
	/// Dynamic depth via activeLayers (LAVD intent).
	/// </summary>
	public sealed class TransformerLiteBody {
		readonly int _width;
		readonly int _layers;
		readonly int _heads;
		readonly int _window;
		readonly float[][] _wIn; // width x width row-major flat via helpers
		readonly float[] _wInFlat;
		readonly float[] _wOutFlat;
		readonly float[][] _ff1; // per layer: (2w) x w
		readonly float[][] _ff2; // per layer: w x (2w)
		readonly float[][] _tok;
		readonly float[] _h;
		readonly float[] _pooled;
		readonly float[] _ff;

		public int Layers => _layers;
		public int Width => _width;
		public int Heads => _heads;
		public int Window => _window;

		public TransformerLiteBody(
			int width = DecimaconDims.Width,
			int layers = DecimaconDims.Layers,
			int heads = DecimaconDims.Heads,
			int window = DecimaconDims.Window) {
			_width = width;
			_layers = Math.Max(1, layers);
			_heads = Math.Max(1, heads);
			_window = Math.Max(2, window);
			_wInFlat = DeterministicWeight("body.in", _width, _width, 0.1f);
			_wOutFlat = DeterministicWeight("body.out", _width, _width, 0.1f);
			_ff1 = new float[_layers][];
			_ff2 = new float[_layers][];
			for (int i = 0; i < _layers; i++) {
				_ff1[i] = DeterministicWeight($"body.layer{i}.ff1", _width * 2, _width, 0.1f);
				_ff2[i] = DeterministicWeight($"body.layer{i}.ff2", _width, _width * 2, 0.1f);
			}
			_tok = new float[_window][];
			for (int t = 0; t < _window; t++) _tok[t] = new float[_width];
			_h = new float[_width];
			_pooled = new float[_width];
			_ff = new float[_width];
		}

		public float[] ForwardVector(float[] z, int activeLayers) {
			int depth = Mathf.Clamp(activeLayers, 1, _layers);
			PadTo(_h, z, _width);
			L2Normalize(_h);
			AffineInPlace(_h, _wInFlat, _width, _width);
			L2Normalize(_h);

			for (int t = 0; t < _window; t++) {
				float phase = 0.17f * (t + 1);
				for (int d = 0; d < _width; d++)
					_tok[t][d] = _h[d] * (0.85f + 0.15f * Mathf.Sin(phase + d * 0.11f));
				L2Normalize(_tok[t]);
			}

			for (int li = 0; li < depth; li++) {
				LocalSelfAttention();
				PoolTokens(_pooled);
				ApplyFfn(li);
				for (int t = 0; t < _window; t++) {
					for (int d = 0; d < _width; d++)
						_tok[t][d] = _tok[t][d] + 0.2f * _ff[d];
					L2Normalize(_tok[t]);
				}
			}

			PoolTokens(_pooled);
			AffineInPlace(_pooled, _wOutFlat, _width, _width);
			L2Normalize(_pooled);
			var outv = new float[_width];
			Array.Copy(_pooled, outv, _width);
			return outv;
		}

		readonly float[] _mid = new float[DecimaconDims.Width * 2];

		void ApplyFfn(int layer) {
			Affine(_pooled, _ff1[layer], _width, _width * 2, _mid);
			for (int i = 0; i < _width * 2; i++)
				_mid[i] = Gelu(_mid[i]);
			Affine(_mid, _ff2[layer], _width * 2, _width, _ff);
		}

		void LocalSelfAttention() {
			// Lightweight local window SA: average neighbors + residual (CPU-first soil spirit).
			int headDim = Math.Max(1, _width / _heads);
			var tmp = new float[_window][];
			for (int t = 0; t < _window; t++) tmp[t] = new float[_width];
			for (int t = 0; t < _window; t++) {
				int lo = Math.Max(0, t - _window / 2);
				int hi = Math.Min(_window - 1, t + _window / 2);
				for (int d = 0; d < _width; d++) {
					float s = 0f;
					int n = 0;
					for (int j = lo; j <= hi; j++) {
						s += _tok[j][d];
						n++;
					}
					tmp[t][d] = _tok[t][d] + (s / Math.Max(1, n)) * 0.25f;
				}
				L2Normalize(tmp[t]);
			}
			for (int t = 0; t < _window; t++)
				Array.Copy(tmp[t], _tok[t], _width);
		}

		void PoolTokens(float[] dst) {
			for (int d = 0; d < _width; d++) {
				float s = 0f;
				for (int t = 0; t < _window; t++) s += _tok[t][d];
				dst[d] = s / _window;
			}
		}

		static void PadTo(float[] dst, float[] src, int n) {
			for (int i = 0; i < n; i++)
				dst[i] = src != null && i < src.Length ? src[i] : 0f;
		}

		static void AffineInPlace(float[] x, float[] w, int inDim, int outDim) {
			var y = new float[outDim];
			Affine(x, w, inDim, outDim, y);
			Array.Copy(y, x, outDim);
		}

		static void Affine(float[] x, float[] w, int inDim, int outDim, float[] y) {
			for (int o = 0; o < outDim; o++) {
				float s = 0f;
				int row = o * inDim;
				for (int i = 0; i < inDim; i++) s += w[row + i] * x[i];
				y[o] = s;
			}
		}

		static float Gelu(float x) =>
			0.5f * x * (1f + (float)System.Math.Tanh(0.7978845608 * (x + 0.044715 * x * x * x)));

		static void L2Normalize(float[] v) {
			float n2 = 0f;
			for (int i = 0; i < v.Length; i++) n2 += v[i] * v[i];
			float inv = n2 > 1e-8f ? 1f / Mathf.Sqrt(n2) : 1f;
			for (int i = 0; i < v.Length; i++) v[i] *= inv;
		}

		static float[] DeterministicWeight(string seed, int rows, int cols, float scale) {
			var w = new float[rows * cols];
			unchecked {
				uint h = 2166136261u;
				for (int i = 0; i < seed.Length; i++) {
					h ^= seed[i];
					h *= 16777619u;
				}
				for (int i = 0; i < w.Length; i++) {
					h ^= (uint)i;
					h *= 16777619u;
					float u = (h % 10000u) / 10000f;
					w[i] = (u * 2f - 1f) * scale;
				}
			}
			return w;
		}
	}
}
