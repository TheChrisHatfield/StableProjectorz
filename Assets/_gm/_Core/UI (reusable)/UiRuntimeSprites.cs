using System.Collections.Generic;
using UnityEngine;

namespace spz {

	public enum StudioLineIcon {
		Folder,
		Refresh,
		Play,
		Restart,
		Trash,
		Brush,
		Grid,
		Settings,
		Eye,
		Mesh,
		/// <summary>Fullscreen / expand affordance (viewport FULL/SRN dock).</summary>
		Expand,
		/// <summary>Open / point left (HIDE RIGHT ↔ return, or open left column).</summary>
		ChevronLeft,
		/// <summary>Open / point right (OPEN RIGHT dock).</summary>
		ChevronRight,
		/// <summary>Art / image list (picture frame).</summary>
		Image,
		/// <summary>Art BG / layered backgrounds.</summary>
		Layers,
		/// <summary>Wireframe / mesh edges (left ribbon).</summary>
		Wireframe,
		/// <summary>Object selection cursor (click-select toggle).</summary>
		Cursor,
		/// <summary>Camera / FOV affordance.</summary>
		Camera,
		/// <summary>Bucket fill tool.</summary>
		Bucket,
		/// <summary>Drop / invert / blend teardrop.</summary>
		Drop,
		/// <summary>Eraser tool.</summary>
		Eraser,
		/// <summary>Smudge / smear blob.</summary>
		Smudge,
		/// <summary>Nomad Flatten litmus — trapezoid press + tip foot (outline-only style reference).</summary>
		Flatten,
		/// <summary>Nomad vertical-slider thumb (circle + center dot).</summary>
		Bullseye,
		/// <summary>Web-find / globe (prompt header image search).</summary>
		Globe,
	}

	/// <summary>
	/// Procedural UI sprites (9-slice rounded rect, circles) for runtime-built uGUI when no project asset is assigned.
	/// Cached statically; safe for IL2CPP (no Resources.GetBuiltinResource).
	/// Soft-edge AA at higher resolution. 9-slice borders stay small so short buttons (≈32px) do not explode into corner blobs.
	/// </summary>
	public static class UiRuntimeSprites {
		static Sprite _solidRect;
		static Sprite _circleFilled;
		static Sprite _circleRing;
		static Sprite _nomadSliderSegmentTile;
		static readonly Dictionary<int, Sprite> RoundedByRadius = new Dictionary<int, Sprite>();
		static readonly Dictionary<StudioLineIcon, Sprite> LineIcons =
			new Dictionary<StudioLineIcon, Sprite>();

		/// <summary>
		/// Opaque white 4×4 fill (no AA, no 9-slice border). Use with <see cref="Image.Type.Simple"/> for hard rectangles.
		/// </summary>
		public static Sprite SolidRect {
			get {
				if (_solidRect == null)
					_solidRect = CreateSolidRect(4);
				return _solidRect;
			}
		}

		/// <summary>True when <paramref name="sprite"/> is the opaque solid fill.</summary>
		public static bool IsSolidRect(Sprite sprite) =>
			sprite != null && ReferenceEquals(sprite, _solidRect);

		/// <summary>
		/// Default 9-slice rounded rect (radius 6). Prefer <see cref="GetRoundedRectSliced"/> for theme-driven radius.
		/// </summary>
		public static Sprite RoundedRectSliced => GetRoundedRectSliced(6);

		/// <summary>
		/// 9-slice rounded rect for the given corner radius (clamped 0–12). Cached per radius.
		/// Soft AA lives in the border patches — pair with <see cref="Image.Type.Sliced"/> (not Simple)
		/// or wide buttons stretch corner AA into horizontal whiskers.
		/// </summary>
		public static Sprite GetRoundedRectSliced(int cornerRadius) {
			int r = Mathf.Clamp(cornerRadius, 0, 12);
			if (!RoundedByRadius.TryGetValue(r, out Sprite sprite) || sprite == null) {
				sprite = CreateRoundedRectSliced(64, r);
				RoundedByRadius[r] = sprite;
			}
			return sprite;
		}

		/// <summary>True when <paramref name="sprite"/> is one of our cached runtime rounded rects.</summary>
		public static bool IsCachedRoundedRect(Sprite sprite) {
			if (sprite == null)
				return false;
			foreach (var pair in RoundedByRadius) {
				if (ReferenceEquals(pair.Value, sprite))
					return true;
			}
			return false;
		}

		public static Sprite CircleFilled {
			get {
				if (_circleFilled == null)
					_circleFilled = CreateCircleFilled(64);
				return _circleFilled;
			}
		}

		public static Sprite CircleRing {
			get {
				if (_circleRing == null)
					_circleRing = CreateCircleRing(64, 5f);
				return _circleRing;
			}
		}

		/// <summary>
		/// White tile for Nomad vertical slider fill (rounded block + gap). Tint with Image.color; use <see cref="Image.Type.Tiled"/>.
		/// </summary>
		public static Sprite NomadSliderSegmentTile {
			get {
				if (_nomadSliderSegmentTile == null)
					_nomadSliderSegmentTile = CreateNomadSliderSegmentTile(28, 18);
				return _nomadSliderSegmentTile;
			}
		}

		/// <summary>True when <paramref name="sprite"/> is the Nomad segmented fill tile.</summary>
		public static bool IsNomadSliderSegmentTile(Sprite sprite) =>
			sprite != null && ReferenceEquals(sprite, _nomadSliderSegmentTile);

		/// <summary>
		/// Nomad paint-tool outline stroke on the 64px atlas (Flatten litmus).
		/// Thinner than legacy chrome glyphs so Brush/Smudge/Bucket/Trash read as wire outlines.
		/// </summary>
		public const float NomadPaintToolStroke = 2.4f;

		/// <summary>Thin 1.5px-style line glyphs for runtime-created professional chrome.</summary>
		public static Sprite GetLineIcon(StudioLineIcon icon) {
			if (!LineIcons.TryGetValue(icon, out Sprite sprite) || sprite == null) {
				sprite = CreateLineIcon(icon, 64);
				LineIcons[icon] = sprite;
			}
			return sprite;
		}

		/// <summary>Drops cached line glyphs so redesigned paths regenerate (also cleared on domain reload).</summary>
		public static void ClearLineIconCache() => LineIcons.Clear();

		static float SoftAlpha(float signedDistance) {
			return Mathf.Clamp01(0.5f - signedDistance);
		}

		static float DistToRoundedRect(float x, float y, float w, float h, float r) {
			float cx = Mathf.Clamp(x, r, w - r);
			float cy = Mathf.Clamp(y, r, h - r);
			float dx = x - cx;
			float dy = y - cy;
			float outside = Mathf.Sqrt(dx * dx + dy * dy) - r;
			if (x >= r && x <= w - r && y >= r && y <= h - r)
				return -Mathf.Min(x, y, w - x, h - y);
			if (x >= r && x <= w - r)
				return y < r ? r - y : y - (h - r);
			if (y >= r && y <= h - r)
				return x < r ? r - x : x - (w - r);
			return outside;
		}

		static Sprite CreateSolidRect(int size) {
			size = Mathf.Max(2, size);
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Point;
			var white = Color.white;
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++)
					tex.SetPixel(x, y, white);
			}
			tex.Apply(false, true);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		static Sprite CreateRoundedRectSliced(int size, int cornerRadius) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			float w = size;
			float h = size;
			// Keep corner radius <= ~1/8 of size so 9-slice borders fit short buttons. Allow 0 (square).
			float r = Mathf.Clamp(cornerRadius, 0f, size / 8f);
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					float sd = DistToRoundedRect(x + 0.5f, y + 0.5f, w, h, r);
					float a = SoftAlpha(sd);
					tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
				}
			}
			tex.Apply(false, true);
			// Border must be < half the shortest control that uses this sprite (header buttons ≈34px tall).
			float br = r <= 0.01f ? 1f : Mathf.Clamp(r + 1f, 2f, 8f);
			var border = new Vector4(br, br, br, br);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
		}

		static Sprite CreateCircleFilled(int size) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			float cx = size * 0.5f;
			float cy = size * 0.5f;
			float rad = size * 0.5f - 1.5f;
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					float dx = (x + 0.5f) - cx;
					float dy = (y + 0.5f) - cy;
					float dist = Mathf.Sqrt(dx * dx + dy * dy);
					float a = SoftAlpha(dist - rad);
					tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
				}
			}
			tex.Apply(false, true);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		static Sprite CreateCircleRing(int size, float thickness) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			float cx = size * 0.5f;
			float cy = size * 0.5f;
			float rOut = size * 0.5f - 1.5f;
			float rIn = Mathf.Max(1f, rOut - thickness);
			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					float dx = (x + 0.5f) - cx;
					float dy = (y + 0.5f) - cy;
					float dist = Mathf.Sqrt(dx * dx + dy * dy);
					float outerA = SoftAlpha(dist - rOut);
					float innerA = SoftAlpha(rIn - dist);
					float a = Mathf.Min(outerA, innerA);
					tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
				}
			}
			tex.Apply(false, true);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		/// <summary>
		/// One vertical tile: transparent gap on top + soft white rounded block (tint via Image.color).
		/// </summary>
		static Sprite CreateNomadSliderSegmentTile(int width, int height) {
			var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			int gap = Mathf.Max(3, height / 5);
			float blockH = height - gap;
			float r = Mathf.Min(width, blockH) * 0.28f;
			for (int y = 0; y < height; y++) {
				for (int x = 0; x < width; x++) {
					if (y >= blockH) {
						tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0f));
						continue;
					}
					float sd = DistToRoundedRect(x + 0.5f, y + 0.5f, width, blockH, r);
					float a = SoftAlpha(sd);
					tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
				}
			}
			tex.Apply(false, true);
			// pixelsPerUnit ≈ height so each tile is ~1 world unit tall at default canvas scale.
			return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), height);
		}

		static Sprite CreateLineIcon(StudioLineIcon icon, int size) {
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			var clear = new Color(1f, 1f, 1f, 0f);
			for (int y = 0; y < size; y++)
				for (int x = 0; x < size; x++)
					tex.SetPixel(x, y, clear);

			const float stroke = 3.2f;
			float paintStroke = NomadPaintToolStroke;
			switch (icon) {
				case StudioLineIcon.Folder:
					Line(tex, 12, 18, 27, 18, stroke);
					Line(tex, 27, 18, 33, 24, stroke);
					Line(tex, 33, 24, 52, 24, stroke);
					Line(tex, 52, 24, 49, 47, stroke);
					Line(tex, 49, 47, 12, 47, stroke);
					Line(tex, 12, 47, 12, 18, stroke);
					break;
				case StudioLineIcon.Refresh:
					Arc(tex, 32, 32, 18, 35, 330, stroke);
					Line(tex, 43, 16, 49, 16, stroke);
					Line(tex, 49, 16, 49, 23, stroke);
					break;
				case StudioLineIcon.Play:
					Line(tex, 22, 16, 49, 32, stroke);
					Line(tex, 49, 32, 22, 48, stroke);
					Line(tex, 22, 48, 22, 16, stroke);
					break;
				case StudioLineIcon.Restart:
					Arc(tex, 32, 32, 18, 20, 340, stroke);
					Line(tex, 44, 14, 51, 16, stroke);
					Line(tex, 51, 16, 48, 23, stroke);
					break;
				case StudioLineIcon.Trash:
					// Nomad litmus: lid + can body as closed outlines (no fill ribs).
					Line(tex, 26, 14, 38, 14, paintStroke);
					Line(tex, 26, 14, 26, 20, paintStroke);
					Line(tex, 38, 14, 38, 20, paintStroke);
					Line(tex, 18, 20, 46, 20, paintStroke);
					Line(tex, 22, 22, 42, 22, paintStroke);
					Line(tex, 42, 22, 40, 50, paintStroke);
					Line(tex, 40, 50, 24, 50, paintStroke);
					Line(tex, 24, 50, 22, 22, paintStroke);
					break;
				case StudioLineIcon.Brush:
					// Nomad litmus: closed handle parallelogram + triangular tip (outline only).
					Line(tex, 14, 48, 20, 42, paintStroke);
					Line(tex, 20, 42, 32, 30, paintStroke);
					Line(tex, 32, 30, 26, 24, paintStroke);
					Line(tex, 26, 24, 14, 36, paintStroke);
					Line(tex, 14, 36, 14, 48, paintStroke);
					Line(tex, 26, 24, 36, 14, paintStroke);
					Line(tex, 36, 14, 48, 20, paintStroke);
					Line(tex, 48, 20, 32, 30, paintStroke);
					break;
				case StudioLineIcon.Flatten:
					// Litmus reference: wide-top trapezoid press + tip foot (matches Nomad Flatten cell).
					Line(tex, 16, 16, 48, 16, paintStroke);
					Line(tex, 48, 16, 40, 38, paintStroke);
					Line(tex, 40, 38, 24, 38, paintStroke);
					Line(tex, 24, 38, 16, 16, paintStroke);
					Line(tex, 32, 38, 38, 50, paintStroke);
					Line(tex, 35, 50, 44, 50, paintStroke);
					break;
				case StudioLineIcon.Grid:
					for (int i = 0; i < 3; i++) {
						float p = 18 + i * 14;
						Line(tex, p, 12, p, 52, stroke);
						Line(tex, 12, p, 52, p, stroke);
					}
					break;
				case StudioLineIcon.Settings:
					Circle(tex, 32, 32, 8, stroke);
					Circle(tex, 32, 32, 18, stroke);
					for (int i = 0; i < 8; i++) {
						float a = i * Mathf.PI / 4f;
						Line(tex,
							32 + Mathf.Cos(a) * 18, 32 + Mathf.Sin(a) * 18,
							32 + Mathf.Cos(a) * 23, 32 + Mathf.Sin(a) * 23, stroke);
					}
					break;
				case StudioLineIcon.Eye:
					Arc(tex, 32, 33, 21, 200, 340, stroke);
					Arc(tex, 32, 31, 21, 20, 160, stroke);
					Circle(tex, 32, 32, 6, stroke);
					break;
				case StudioLineIcon.Image:
					Line(tex, 12, 16, 52, 16, stroke);
					Line(tex, 52, 16, 52, 48, stroke);
					Line(tex, 52, 48, 12, 48, stroke);
					Line(tex, 12, 48, 12, 16, stroke);
					Line(tex, 12, 36, 24, 28, stroke);
					Line(tex, 24, 28, 34, 38, stroke);
					Line(tex, 34, 38, 52, 22, stroke);
					Circle(tex, 22, 24, 4, stroke);
					break;
				case StudioLineIcon.Layers:
					Line(tex, 16, 40, 32, 48, stroke);
					Line(tex, 32, 48, 48, 40, stroke);
					Line(tex, 48, 40, 32, 32, stroke);
					Line(tex, 32, 32, 16, 40, stroke);
					Line(tex, 16, 32, 32, 40, stroke);
					Line(tex, 32, 40, 48, 32, stroke);
					Line(tex, 48, 32, 32, 24, stroke);
					Line(tex, 32, 24, 16, 32, stroke);
					Line(tex, 16, 24, 32, 32, stroke);
					Line(tex, 32, 32, 48, 24, stroke);
					Line(tex, 48, 24, 32, 16, stroke);
					Line(tex, 32, 16, 16, 24, stroke);
					break;
				case StudioLineIcon.Mesh:
					Line(tex, 32, 10, 51, 21, stroke);
					Line(tex, 51, 21, 51, 43, stroke);
					Line(tex, 51, 43, 32, 54, stroke);
					Line(tex, 32, 54, 13, 43, stroke);
					Line(tex, 13, 43, 13, 21, stroke);
					Line(tex, 13, 21, 32, 10, stroke);
					Line(tex, 13, 21, 32, 32, stroke);
					Line(tex, 32, 32, 51, 21, stroke);
					Line(tex, 32, 32, 32, 54, stroke);
					break;
				case StudioLineIcon.Expand:
					// Outer frame + inward corner ticks (fullscreen).
					Line(tex, 14, 14, 50, 14, stroke);
					Line(tex, 50, 14, 50, 50, stroke);
					Line(tex, 50, 50, 14, 50, stroke);
					Line(tex, 14, 50, 14, 14, stroke);
					Line(tex, 14, 14, 22, 14, stroke);
					Line(tex, 14, 14, 14, 22, stroke);
					Line(tex, 50, 14, 42, 14, stroke);
					Line(tex, 50, 14, 50, 22, stroke);
					Line(tex, 50, 50, 42, 50, stroke);
					Line(tex, 50, 50, 50, 42, stroke);
					Line(tex, 14, 50, 22, 50, stroke);
					Line(tex, 14, 50, 14, 42, stroke);
					break;
				case StudioLineIcon.ChevronLeft:
					Line(tex, 40, 16, 22, 32, stroke + 0.6f);
					Line(tex, 22, 32, 40, 48, stroke + 0.6f);
					break;
				case StudioLineIcon.ChevronRight:
					Line(tex, 24, 16, 42, 32, stroke + 0.6f);
					Line(tex, 42, 32, 24, 48, stroke + 0.6f);
					break;
				case StudioLineIcon.Wireframe:
					// Mountain / triangle wire peek (left ribbon wireframe).
					Line(tex, 12, 48, 32, 14, stroke);
					Line(tex, 32, 14, 52, 48, stroke);
					Line(tex, 52, 48, 12, 48, stroke);
					Line(tex, 22, 48, 32, 30, stroke * 0.85f);
					Line(tex, 32, 30, 42, 48, stroke * 0.85f);
					break;
				case StudioLineIcon.Cursor:
					Line(tex, 18, 12, 18, 48, stroke + 0.4f);
					Line(tex, 18, 12, 40, 34, stroke + 0.4f);
					Line(tex, 18, 28, 30, 28, stroke);
					Line(tex, 30, 28, 24, 48, stroke);
					Line(tex, 24, 48, 18, 34, stroke);
					break;
				case StudioLineIcon.Camera:
					Line(tex, 14, 24, 50, 24, stroke);
					Line(tex, 50, 24, 50, 46, stroke);
					Line(tex, 50, 46, 14, 46, stroke);
					Line(tex, 14, 46, 14, 24, stroke);
					Circle(tex, 32, 35, 8, stroke);
					Line(tex, 22, 24, 26, 16, stroke);
					Line(tex, 26, 16, 38, 16, stroke);
					Line(tex, 38, 16, 42, 24, stroke);
					break;
				case StudioLineIcon.Bucket:
					// Nomad litmus: pot trapezoid + side handle + pour tick (outline only).
					Line(tex, 18, 18, 46, 18, paintStroke);
					Line(tex, 46, 18, 42, 38, paintStroke);
					Line(tex, 42, 38, 22, 38, paintStroke);
					Line(tex, 22, 38, 18, 18, paintStroke);
					Arc(tex, 46, 28, 9, 280, 80, paintStroke);
					Line(tex, 30, 38, 34, 50, paintStroke);
					Line(tex, 32, 50, 40, 50, paintStroke);
					break;
				case StudioLineIcon.Drop:
					Line(tex, 32, 12, 44, 34, stroke);
					Arc(tex, 32, 38, 12, 200, 340, stroke);
					Line(tex, 20, 34, 32, 12, stroke);
					Line(tex, 32, 28, 32, 48, stroke * 0.7f);
					break;
				case StudioLineIcon.Eraser:
					// Same tool strip as Brush — Nomad outline parallelogram + ferrule tick.
					Line(tex, 16, 40, 28, 16, paintStroke);
					Line(tex, 28, 16, 48, 26, paintStroke);
					Line(tex, 48, 26, 36, 50, paintStroke);
					Line(tex, 36, 50, 16, 40, paintStroke);
					Line(tex, 22, 36, 40, 45, paintStroke);
					break;
				case StudioLineIcon.Smudge:
					// Nomad litmus: closed teardrop body + two parallel smear arcs.
					Arc(tex, 28, 36, 13, 50, 310, paintStroke);
					Line(tex, 18, 42, 22, 50, paintStroke);
					Line(tex, 22, 50, 28, 48, paintStroke);
					Arc(tex, 40, 26, 9, 200, 40, paintStroke);
					Arc(tex, 48, 20, 6, 200, 40, paintStroke);
					break;
				case StudioLineIcon.Bullseye:
					Circle(tex, 32, 32, 18, stroke);
					Circle(tex, 32, 32, 4.5f, stroke + 0.8f);
					break;
				case StudioLineIcon.Globe:
					Circle(tex, 32, 32, 18, stroke);
					// Meridians + equator (wireframe globe).
					Arc(tex, 32, 32, 18, 250, 290, stroke * 0.9f);
					Arc(tex, 32, 32, 18, 70, 110, stroke * 0.9f);
					Line(tex, 14, 32, 50, 32, stroke * 0.85f);
					Arc(tex, 32, 32, 10, 200, 340, stroke * 0.8f);
					Arc(tex, 32, 32, 10, 20, 160, stroke * 0.8f);
					break;
				default:
					Line(tex, 32, 10, 51, 21, stroke);
					Line(tex, 51, 21, 51, 43, stroke);
					Line(tex, 51, 43, 32, 54, stroke);
					Line(tex, 32, 54, 13, 43, stroke);
					Line(tex, 13, 43, 13, 21, stroke);
					Line(tex, 13, 21, 32, 10, stroke);
					break;
			}

			// Glyph paths are authored y-down (UI space). Texture2D is y-up — flip before sprite create.
			FlipTextureVertically(tex);
			tex.Apply(false, true);
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		}

		static void FlipTextureVertically(Texture2D tex) {
			int w = tex.width;
			int h = tex.height;
			int half = h / 2;
			for (int y = 0; y < half; y++) {
				int y2 = h - 1 - y;
				for (int x = 0; x < w; x++) {
					Color a = tex.GetPixel(x, y);
					Color b = tex.GetPixel(x, y2);
					tex.SetPixel(x, y, b);
					tex.SetPixel(x, y2, a);
				}
			}
		}

		static void Arc(Texture2D tex, float cx, float cy, float radius, float startDeg, float endDeg, float thickness) {
			const int segments = 40;
			float range = endDeg - startDeg;
			if (range < 0f) range += 360f;
			Vector2 previous = PointOnCircle(cx, cy, radius, startDeg);
			for (int i = 1; i <= segments; i++) {
				Vector2 next = PointOnCircle(cx, cy, radius, startDeg + range * i / segments);
				Line(tex, previous.x, previous.y, next.x, next.y, thickness);
				previous = next;
			}
		}

		static Vector2 PointOnCircle(float cx, float cy, float radius, float degrees) {
			float radians = degrees * Mathf.Deg2Rad;
			return new Vector2(cx + Mathf.Cos(radians) * radius, cy + Mathf.Sin(radians) * radius);
		}

		static void Circle(Texture2D tex, float cx, float cy, float radius, float thickness) {
			Arc(tex, cx, cy, radius, 0f, 360f, thickness);
		}

		static void Line(Texture2D tex, float ax, float ay, float bx, float by, float thickness) {
			int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx) - thickness - 1f));
			int maxX = Mathf.Min(tex.width - 1, Mathf.CeilToInt(Mathf.Max(ax, bx) + thickness + 1f));
			int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by) - thickness - 1f));
			int maxY = Mathf.Min(tex.height - 1, Mathf.CeilToInt(Mathf.Max(ay, by) + thickness + 1f));
			Vector2 a = new Vector2(ax, ay);
			Vector2 b = new Vector2(bx, by);
			Vector2 ab = b - a;
			float denom = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));
			for (int y = minY; y <= maxY; y++) {
				for (int x = minX; x <= maxX; x++) {
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
					float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
					float distance = Vector2.Distance(p, a + ab * t);
					float alpha = Mathf.Clamp01(thickness * 0.5f + 0.75f - distance);
					if (alpha <= 0f) continue;
					Color old = tex.GetPixel(x, y);
					if (alpha > old.a)
						tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
				}
			}
		}
	}
}
