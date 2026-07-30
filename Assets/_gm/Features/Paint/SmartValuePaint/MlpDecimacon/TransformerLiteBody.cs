using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace spz.MlpDecimacon {

	public sealed class TransformerBodyBlockDto {
		public float[] qkv_weight;
		public float[] qkv_bias;
		public float[] proj_weight;
		public float[] proj_bias;
		public float[] ff1_weight;
		public float[] ff1_bias;
		public float[] ff2_weight;
		public float[] ff2_bias;
		public float[] n1_weight;
		public float[] n1_bias;
		public float[] n2_weight;
		public float[] n2_bias;
	}

	public sealed class TransformerBodyWeightsDto {
		public string version;
		public string arch;
		public int width = DecimaconDims.Width;
		public int layers = DecimaconDims.Layers;
		public int heads = DecimaconDims.Heads;
		public int window = DecimaconDims.Window;
		public float[] out_weight;
		public float[] out_bias;
		public TransformerBodyBlockDto[] blocks;

		public const string StreamingRelative = "MlpDecimacon/transformer_body_v2_weights.json";

		public static bool TryLoad(out TransformerBodyWeightsDto dto, out string error) {
			dto = null;
			error = null;
			try {
				string path = Path.Combine(Application.streamingAssetsPath, StreamingRelative.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(path)) {
					error = "missing " + path;
					return false;
				}
				dto = JsonConvert.DeserializeObject<TransformerBodyWeightsDto>(File.ReadAllText(path));
				if (dto?.blocks == null || dto.blocks.Length < 1 || dto.out_weight == null) {
					error = "invalid body weights";
					return false;
				}
				return true;
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
		}
	}

	/// <summary>
	/// Topology v2 body — prefers soil corpus warm-start weights; else deterministic CPU init.
	/// Dynamic depth via activeLayers (LAVD intent).
	/// </summary>
	public sealed class TransformerLiteBody {
		readonly int _width;
		readonly int _layers;
		readonly int _heads;
		readonly int _window;
		readonly int _headDim;
		readonly bool _warm;
		readonly float[] _wOut;
		readonly float[] _bOut;
		readonly TransformerBodyBlockDto[] _blocks;
		// deterministic fallback only
		readonly float[] _wInFlat;
		readonly float[][] _ff1;
		readonly float[][] _ff2;
		readonly float[][] _tok;
		readonly float[] _h;
		readonly float[] _pooled;
		readonly float[] _ff;
		readonly float[] _mid;
		readonly float[] _attnBuf;
		readonly float[] _qkv;
		readonly float[] _normed;

		public int Layers => _layers;
		public int Width => _width;
		public int Heads => _heads;
		public int Window => _window;
		public bool IsWarmStarted => _warm;

		public TransformerLiteBody(TransformerBodyWeightsDto warm = null) {
			if (warm != null && warm.blocks != null && warm.blocks.Length > 0) {
				_warm = true;
				_width = warm.width > 0 ? warm.width : DecimaconDims.Width;
				_layers = Math.Max(1, Math.Min(warm.layers > 0 ? warm.layers : warm.blocks.Length, warm.blocks.Length));
				_heads = Math.Max(1, warm.heads > 0 ? warm.heads : DecimaconDims.Heads);
				_window = Math.Max(2, warm.window > 0 ? warm.window : DecimaconDims.Window);
				_headDim = Math.Max(1, _width / _heads);
				_blocks = warm.blocks;
				_wOut = warm.out_weight;
				_bOut = warm.out_bias;
				_wInFlat = null;
				_ff1 = null;
				_ff2 = null;
			} else {
				_warm = false;
				_width = DecimaconDims.Width;
				_layers = DecimaconDims.Layers;
				_heads = DecimaconDims.Heads;
				_window = DecimaconDims.Window;
				_headDim = Math.Max(1, _width / _heads);
				_blocks = null;
				_wOut = DeterministicWeight("body.out", _width, _width, 0.1f);
				_bOut = new float[_width];
				_wInFlat = DeterministicWeight("body.in", _width, _width, 0.1f);
				_ff1 = new float[_layers][];
				_ff2 = new float[_layers][];
				for (int i = 0; i < _layers; i++) {
					_ff1[i] = DeterministicWeight($"body.layer{i}.ff1", _width * 2, _width, 0.1f);
					_ff2[i] = DeterministicWeight($"body.layer{i}.ff2", _width, _width * 2, 0.1f);
				}
			}
			_tok = new float[_window][];
			for (int t = 0; t < _window; t++) _tok[t] = new float[_width];
			_h = new float[_width];
			_pooled = new float[_width];
			_ff = new float[_width];
			_mid = new float[_width * 2];
			_attnBuf = new float[_width];
			_qkv = new float[_width * 3];
			_normed = new float[_width];
		}

		public static TransformerLiteBody CreatePreferWarmStart() {
			if (TransformerBodyWeightsDto.TryLoad(out var dto, out _))
				return new TransformerLiteBody(dto);
			return new TransformerLiteBody();
		}

		public float[] ForwardVector(float[] z, int activeLayers) {
			int depth = Mathf.Clamp(activeLayers, 1, _layers);
			PadTo(_h, z, _width);
			L2Normalize(_h);
			if (!_warm && _wInFlat != null) {
				AffineInPlace(_h, _wInFlat, null, _width, _width);
				L2Normalize(_h);
			}

			for (int t = 0; t < _window; t++) {
				float phase = 0.17f * (t + 1);
				for (int d = 0; d < _width; d++)
					_tok[t][d] = _h[d] * (0.85f + 0.15f * Mathf.Sin(phase + d * 0.11f));
				L2Normalize(_tok[t]);
			}

			if (_warm) {
				for (int li = 0; li < depth; li++)
					ApplyWarmBlock(_blocks[li]);
			} else {
				for (int li = 0; li < depth; li++) {
					LocalSelfAttentionFallback();
					PoolTokens(_pooled);
					ApplyFfnFallback(li);
					for (int t = 0; t < _window; t++) {
						for (int d = 0; d < _width; d++)
							_tok[t][d] = _tok[t][d] + 0.2f * _ff[d];
						L2Normalize(_tok[t]);
					}
				}
			}

			PoolTokens(_pooled);
			Affine(_pooled, _wOut, _bOut, _width, _width, _h);
			L2Normalize(_h);
			var outv = new float[_width];
			Array.Copy(_h, outv, _width);
			return outv;
		}

		void ApplyWarmBlock(TransformerBodyBlockDto blk) {
			// x = x + attn(LayerNorm(x)); x = x + ff(LayerNorm(x))
			var residual = new float[_window][];
			for (int t = 0; t < _window; t++) {
				residual[t] = (float[])_tok[t].Clone();
				LayerNorm(_tok[t], blk.n1_weight, blk.n1_bias, _normed);
				Array.Copy(_normed, _tok[t], _width);
			}
			LocalSelfAttentionWarm(blk);
			for (int t = 0; t < _window; t++) {
				for (int d = 0; d < _width; d++)
					_tok[t][d] = residual[t][d] + _tok[t][d];
			}

			for (int t = 0; t < _window; t++) {
				residual[t] = (float[])_tok[t].Clone();
				LayerNorm(_tok[t], blk.n2_weight, blk.n2_bias, _normed);
				Affine(_normed, blk.ff1_weight, blk.ff1_bias, _width, _width * 2, _mid);
				for (int i = 0; i < _width * 2; i++) _mid[i] = Gelu(_mid[i]);
				Affine(_mid, blk.ff2_weight, blk.ff2_bias, _width * 2, _width, _ff);
				for (int d = 0; d < _width; d++)
					_tok[t][d] = residual[t][d] + _ff[d];
			}
		}

		void LocalSelfAttentionWarm(TransformerBodyBlockDto blk) {
			var q = new float[_window][];
			var k = new float[_window][];
			var v = new float[_window][];
			var outTok = new float[_window][];
			for (int t = 0; t < _window; t++) {
				Affine(_tok[t], blk.qkv_weight, blk.qkv_bias, _width, _width * 3, _qkv);
				q[t] = new float[_width];
				k[t] = new float[_width];
				v[t] = new float[_width];
				Array.Copy(_qkv, 0, q[t], 0, _width);
				Array.Copy(_qkv, _width, k[t], 0, _width);
				Array.Copy(_qkv, _width * 2, v[t], 0, _width);
				outTok[t] = new float[_width];
			}

			float scale = 1f / Mathf.Sqrt(_headDim);
			for (int t = 0; t < _window; t++) {
				for (int h = 0; h < _heads; h++) {
					int off = h * _headDim;
					float maxLogit = float.NegativeInfinity;
					var logits = new float[_window];
					for (int j = 0; j < _window; j++) {
						if (Math.Abs(t - j) > _window) {
							logits[j] = float.NegativeInfinity;
							continue;
						}
						float dot = 0f;
						for (int d = 0; d < _headDim; d++)
							dot += q[t][off + d] * k[j][off + d];
						logits[j] = dot * scale;
						if (logits[j] > maxLogit) maxLogit = logits[j];
					}
					float sum = 0f;
					for (int j = 0; j < _window; j++) {
						if (float.IsNegativeInfinity(logits[j])) { logits[j] = 0f; continue; }
						logits[j] = Mathf.Exp(logits[j] - maxLogit);
						sum += logits[j];
					}
					float inv = sum > 1e-8f ? 1f / sum : 0f;
					for (int d = 0; d < _headDim; d++) {
						float acc = 0f;
						for (int j = 0; j < _window; j++)
							acc += (logits[j] * inv) * v[j][off + d];
						outTok[t][off + d] = acc;
					}
				}
			}

			for (int t = 0; t < _window; t++) {
				Affine(outTok[t], blk.proj_weight, blk.proj_bias, _width, _width, _attnBuf);
				Array.Copy(_attnBuf, _tok[t], _width);
			}
		}

		void ApplyFfnFallback(int layer) {
			Affine(_pooled, _ff1[layer], null, _width, _width * 2, _mid);
			for (int i = 0; i < _width * 2; i++) _mid[i] = Gelu(_mid[i]);
			Affine(_mid, _ff2[layer], null, _width * 2, _width, _ff);
		}

		void LocalSelfAttentionFallback() {
			var tmp = new float[_window][];
			for (int t = 0; t < _window; t++) tmp[t] = new float[_width];
			for (int t = 0; t < _window; t++) {
				int lo = Math.Max(0, t - _window / 2);
				int hi = Math.Min(_window - 1, t + _window / 2);
				for (int d = 0; d < _width; d++) {
					float s = 0f;
					int n = 0;
					for (int j = lo; j <= hi; j++) { s += _tok[j][d]; n++; }
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

		static void LayerNorm(float[] x, float[] w, float[] b, float[] y) {
			int n = x.Length;
			float mean = 0f;
			for (int i = 0; i < n; i++) mean += x[i];
			mean /= n;
			float var = 0f;
			for (int i = 0; i < n; i++) {
				float d = x[i] - mean;
				var += d * d;
			}
			var = var / n;
			float inv = 1f / Mathf.Sqrt(var + 1e-5f);
			for (int i = 0; i < n; i++) {
				float nrm = (x[i] - mean) * inv;
				float scale = w != null && i < w.Length ? w[i] : 1f;
				float bias = b != null && i < b.Length ? b[i] : 0f;
				y[i] = nrm * scale + bias;
			}
		}

		static void PadTo(float[] dst, float[] src, int n) {
			for (int i = 0; i < n; i++)
				dst[i] = src != null && i < src.Length ? src[i] : 0f;
		}

		static void AffineInPlace(float[] x, float[] w, float[] b, int inDim, int outDim) {
			var y = new float[outDim];
			Affine(x, w, b, inDim, outDim, y);
			Array.Copy(y, x, Math.Min(x.Length, outDim));
		}

		static void Affine(float[] x, float[] w, float[] b, int inDim, int outDim, float[] y) {
			for (int o = 0; o < outDim; o++) {
				float s = b != null && o < b.Length ? b[o] : 0f;
				int row = o * inDim;
				for (int i = 0; i < inDim; i++) {
					if (w != null && row + i < w.Length)
						s += w[row + i] * (x != null && i < x.Length ? x[i] : 0f);
				}
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
