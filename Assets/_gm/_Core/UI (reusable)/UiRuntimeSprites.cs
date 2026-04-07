using UnityEngine;

namespace spz {

	/// <summary>
	/// Procedural UI sprites (9-slice rounded rect, circles) for runtime-built uGUI when no project asset is assigned.
	/// Cached statically; safe for IL2CPP (no Resources.GetBuiltinResource).
	/// </summary>
	public static class UiRuntimeSprites {
		static Sprite _roundedSliced;
		static Sprite _circleFilled;
		static Sprite _circleRing;

		public static Sprite RoundedRectSliced {
			get {
				if (_roundedSliced == null)
					_roundedSliced = CreateRoundedRectSliced(48, 12);
				return _roundedSliced;
			}
		}

		public static Sprite CircleFilled {
			get {
				if (_circleFilled == null)
					_circleFilled = CreateCircleFilled(28);
				return _circleFilled;
			}
		}

		public static Sprite CircleRing {
			get {
				if (_circleRing == null)
					_circleRing = CreateCircleRing(28, 3);
				return _circleRing;
			}
		}

		static float DistSq(float x, float y, float cx, float cy) {
			float dx = x - cx;
			float dy = y - cy;
			return dx * dx + dy * dy;
		}

		static bool InsideRoundedRect(float x, float y, float w, float h, float r) {
			if (x < 0 || y < 0 || x >= w || y >= h) return false;
			if (x < r && y < r) return DistSq(x, y, r, r) <= r * r;
			if (x >= w - r && y < r) return DistSq(x, y, w - r, r) <= r * r;
			if (x < r && y >= h - r) return DistSq(x, y, r, h - r) <= r * r;
			if (x >= w - r && y >= h - r) return DistSq(x, y, w - r, h - r) <= r * r;
			return true;
		}

		static Sprite CreateRoundedRectSliced(int size, int cornerRadius) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			float w = size;
			float h = size;
			float r = Mathf.Min(cornerRadius, size / 4f);
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					bool inR = InsideRoundedRect(x + 0.5f, y + 0.5f, w, h, r);
					tex.SetPixel(x, y, inR ? Color.white : Color.clear);
				}
			}
			tex.Apply(false, true);
			float br = Mathf.Clamp(r, 2f, size / 2f - 1f);
			var border = new Vector4(br, br, br, br);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
		}

		static Sprite CreateCircleFilled(int size) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			float cx = (size - 1) * 0.5f;
			float cy = (size - 1) * 0.5f;
			float rad = size * 0.5f - 1f;
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					float dx = x - cx;
					float dy = y - cy;
					tex.SetPixel(x, y, dx * dx + dy * dy <= rad * rad ? Color.white : Color.clear);
				}
			}
			tex.Apply(false, true);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		static Sprite CreateCircleRing(int size, int thickness) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			float cx = (size - 1) * 0.5f;
			float cy = (size - 1) * 0.5f;
			float rOut = size * 0.5f - 1f;
			float rIn = Mathf.Max(0.5f, rOut - thickness);
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					float dx = x - cx;
					float dy = y - cy;
					float d2 = dx * dx + dy * dy;
					bool ring = d2 <= rOut * rOut && d2 >= rIn * rIn;
					tex.SetPixel(x, y, ring ? Color.white : Color.clear);
				}
			}
			tex.Apply(false, true);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}
	}
}
