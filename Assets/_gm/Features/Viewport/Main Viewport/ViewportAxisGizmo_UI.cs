using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	/// <summary>Attach parameters for <see cref="ViewportAxisGizmo_UI"/> (JSON-RPC maps onto this in <see cref="ViewportAxisGizmo_AddonBridge"/>).</summary>
	public readonly struct ViewportAxisGizmo_Spec {

		public readonly float SizePx;
		public readonly float MarginPx;
		/// <summary>Absolute path to the center glyph (the SPZ lantern shipped with the add-on). Empty falls back to a line icon.</summary>
		public readonly string CenterIconPath;
		/// <summary><see cref="RibbonDock_CommandBridge"/> id invoked by the lantern center button.</summary>
		public readonly string CenterCommandId;

		public ViewportAxisGizmo_Spec(float sizePx, float marginPx, string centerIconPath, string centerCommandId) {
			SizePx = Mathf.Clamp(sizePx <= 0f ? 104f : sizePx, 64f, 240f);
			MarginPx = Mathf.Clamp(marginPx < 0f ? ProjectUiScale.Space(2) : marginPx, 0f, 128f);
			CenterIconPath = centerIconPath ?? string.Empty;
			CenterCommandId = string.IsNullOrWhiteSpace(centerCommandId)
				? ViewportAxisGizmo_UI.OverviewCommandId
				: centerCommandId;
		}

		public static ViewportAxisGizmo_Spec Default =>
			new ViewportAxisGizmo_Spec(104f, ProjectUiScale.Space(2), string.Empty, ViewportAxisGizmo_UI.OverviewCommandId);
	}

	/// <summary>
	/// StableProjectorz orientation gizmo in the top-right of the 3D view: six axis discs that follow the
	/// camera rotation (+X/+Y/+Z labelled and filled, negatives as rings), with the SPZ lantern in the
	/// middle as an "overview" button that frames the whole scene (every loaded mesh).
	/// Chrome is cool-grey + sky accent by default; Nomad restyles via gated BoundChrome (charcoal + gold).
	///
	/// Parented to <see cref="MainViewport_UI.mainViewportRect"/> (drawn above the view RawImage) and pinned to the
	/// top-right of <see cref="MainViewport_UI.innerViewportRect"/> (the aspect-fitted image). The size-reference
	/// rect itself is the first sibling under the viewport — parenting there hid the gizmo behind the 3D view.
	/// The root carries <see cref="MainViewport_RaycastBlocker"/> so paint / orbit do not fire under the widget.
	///
	/// Attached from the <c>ViewportAxisGizmoSPZ</c> add-on via JSON-RPC <c>spz.ui.attach_viewport_axis_gizmo</c>
	/// (see <see cref="ViewportAxisGizmo_AddonBridge"/>); <see cref="Addon_MGR"/> runs the same attach on the main
	/// thread when Python never registers. Not a command-ribbon tab.
	/// </summary>
	public sealed class ViewportAxisGizmo_UI : MonoBehaviour {

		public const string RootName = "ViewportAxisGizmo";
		public const string BackdropName = "GizmoBackdrop";
		public const string CenterName = "GizmoCenterOverview";
		public const string HandlePrefix = "AxisHandle_";
		public const string LinePrefix = "AxisLine_";
		/// <summary>Command id the lantern button invokes by default (frame the whole scene).</summary>
		public const string OverviewCommandId = "viewport_axis_gizmo_overview";

		/// <summary>Default SPZ lantern tint (grey translucent silhouette). Nomad overrides via theme tokens.</summary>
		public static Color CenterGlyphTint => ViewportAxisGizmo_Palette.SpzDefault.CenterTint;

		static readonly List<ViewportAxisGizmo_UI> Registered = new List<ViewportAxisGizmo_UI>();
		static readonly Dictionary<string, Sprite> CenterSpriteCache = new Dictionary<string, Sprite>();
		static bool s_commandsRegistered;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetStatics() {
			// Enter Play Mode with domain reload disabled keeps destroyed instances / dead sprites otherwise.
			Registered.Clear();
			foreach (var kvp in CenterSpriteCache) {
				if (kvp.Value == null) {
					continue;
				}
				Texture2D tex = kvp.Value.texture;
				if (Application.isPlaying) {
					UnityEngine.Object.Destroy(kvp.Value);
					if (tex != null) {
						UnityEngine.Object.Destroy(tex);
					}
				} else {
					UnityEngine.Object.DestroyImmediate(kvp.Value);
					if (tex != null) {
						UnityEngine.Object.DestroyImmediate(tex);
					}
				}
			}
			CenterSpriteCache.Clear();
			s_commandsRegistered = false;
		}

		ViewportAxisGizmo_Spec _spec = ViewportAxisGizmo_Spec.Default;
		ViewportAxisGizmo_Palette _palette = ViewportAxisGizmo_Palette.SpzDefault;
		bool _tornDown;
		bool _themeHooked;
		RectTransform _root;
		CanvasGroup _canvasGroup;
		Image _backdropImage;
		RectTransform _centerRt;
		Image _centerImage;
		string _loadedCenterIconPath = string.Empty;
		readonly List<RectTransform> _handleRects = new List<RectTransform>();
		readonly List<Image> _handleImages = new List<Image>();
		readonly List<TextMeshProUGUI> _handleLabels = new List<TextMeshProUGUI>();
		readonly List<RectTransform> _lineRects = new List<RectTransform>();
		readonly List<Image> _lineImages = new List<Image>();

		/// <summary>Below this the re-projection is invisible, so an idle view can skip the whole pass.</summary>
		const float RotationEpsilonDeg = 0.02f;
		static readonly Comparison<KeyValuePair<int, int>> ByDrawOrder = (a, b) => a.Value.CompareTo(b.Value);
		readonly List<KeyValuePair<int, int>> _orderBuffer = new List<KeyValuePair<int, int>>(6);
		readonly List<int> _appliedOrder = new List<int>(6);
		Quaternion _lastAppliedRotation = Quaternion.identity;
		bool _hasAppliedOrientation;

		public ViewportAxisGizmo_Spec Spec => _spec;
		public RectTransform RootRect => _root;
		public ViewportAxisGizmo_Palette ActivePalette => _palette;

		#region attach / teardown

		/// <summary>
		/// Build (or refresh) the gizmo on the main viewport. False while the viewport rect is not in the scene yet —
		/// callers retry, same as the FULL/SRN dock attach.
		/// </summary>
		public static bool TryAttach(ViewportAxisGizmo_Spec spec) {
			RectTransform parent = ResolveViewportParent();
			if (parent == null) {
				return false;
			}
			// Look for the widget anywhere, not only under the rect we want it on: an attach that happened before
			// the inner aspect-fitted rect existed landed on the outer viewport, and searching only the new parent
			// would build a second gizmo instead of moving the first one.
			var existing = FindAnyLiveGizmo();
			if (existing != null) {
				existing.EnsureHostedUnder(parent);
				existing.ApplySpec(spec);
				return true;
			}
			return BuildInto(parent, spec) != null;
		}

		/// <summary>
		/// Host for the widget: the main viewport chrome rect. The aspect-fitted
		/// <see cref="MainViewport_UI.innerViewportRect"/> is only a size reference and is the first sibling under
		/// the viewport — parenting there draws the gizmo behind the view RawImage (invisible / unclickable).
		/// Corner placement still tracks the inner rect via <see cref="ApplySpec"/>.
		/// </summary>
		public static RectTransform ResolveViewportParent() {
			var viewport = MainViewport_UI.instance;
			if (viewport == null) {
				return null;
			}
			return viewport.mainViewportRect;
		}

		/// <summary>Aspect-fitted image rect used to place the gizmo on the rendered picture, not the letterbox.</summary>
		public static RectTransform ResolveDockReference() {
			var viewport = MainViewport_UI.instance;
			if (viewport == null) {
				return null;
			}
			if (InnerViewport_SizeReference.instance != null && viewport.innerViewportRect != null) {
				return viewport.innerViewportRect;
			}
			return viewport.mainViewportRect;
		}

		public static ViewportAxisGizmo_UI FindUnder(RectTransform parent) {
			if (parent == null) {
				return null;
			}
			var found = parent.GetComponentsInChildren<ViewportAxisGizmo_UI>(true);
			for (int i = 0; i < found.Length; i++) {
				if (IsLive(found[i])) {
					return found[i];
				}
			}
			return null;
		}

		/// <summary>Any live gizmo in the scene, wherever it is parented (registered instances first, then a scene scan).</summary>
		public static ViewportAxisGizmo_UI FindAnyLiveGizmo() {
			PruneRegistered();
			for (int i = 0; i < Registered.Count; i++) {
				if (IsLive(Registered[i])) {
					return Registered[i];
				}
			}
			var all = FindObjectsByType<ViewportAxisGizmo_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++) {
				if (IsLive(all[i])) {
					return all[i];
				}
			}
			return null;
		}

		static bool IsLive(ViewportAxisGizmo_UI gizmo) => gizmo != null && !gizmo._tornDown;

		/// <summary>
		/// Moves the widget onto <paramref name="parent"/> when that is not where it already lives. The inner
		/// aspect-fitted viewport rect is not always alive when the add-on attaches, and a viewport rebuild can
		/// replace it, so the host is re-checked instead of being decided once. No-op in the steady state.
		/// </summary>
		public bool EnsureHostedUnder(RectTransform parent) {
			if (_tornDown || parent == null || _root == null || _root.parent == parent) {
				return false;
			}
			_root.SetParent(parent, false);
			gameObject.layer = parent.gameObject.layer;
			_root.SetAsLastSibling();
			ApplySpec(_spec);
			return true;
		}

		/// <summary>True when a live gizmo GameObject is active in the hierarchy (mounted). Not the same as on-screen —
		/// UV mode keeps the widget mounted with CanvasGroup alpha 0. Prefer this for attach-retry loops.</summary>
		public static bool IsAnyMountedGizmo() {
			PruneRegistered();
			for (int i = 0; i < Registered.Count; i++) {
				var g = Registered[i];
				if (IsLive(g) && g.gameObject.activeInHierarchy) {
					return true;
				}
			}
			return false;
		}

		/// <summary>Obsolete name for <see cref="IsAnyMountedGizmo"/> — does not mean CanvasGroup alpha &gt; 0.</summary>
		public static bool IsAnyVisibleGizmo() => IsAnyMountedGizmo();

		/// <summary>Remove every gizmo (add-on turned off in Add-on Manager).</summary>
		public static void TeardownAllForAddonDisabled() {
			var all = FindObjectsByType<ViewportAxisGizmo_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++) {
				if (all[i] == null) {
					continue;
				}
				all[i].MarkTornDown();
				var go = all[i].gameObject;
				if (Application.isPlaying) {
					Destroy(go);
				} else {
					DestroyImmediate(go);
				}
			}
			Registered.Clear();
		}

		/// <summary>
		/// Retires this widget before <see cref="Object.Destroy"/> runs. In a player build the destroy only lands at
		/// the end of the frame, so an add-on re-enabled in that same frame would otherwise be handed this dying
		/// instance (reporting attach success with nothing on screen), and its own refresh pass would parent it back
		/// onto the viewport for one last frame.
		/// </summary>
		public void MarkTornDown() {
			_tornDown = true;
			Registered.Remove(this);
			if (_root != null) {
				_root.SetParent(null, false);
			}
			gameObject.SetActive(false);
		}

		static void PruneRegistered() {
			for (int i = Registered.Count - 1; i >= 0; i--) {
				if (Registered[i] == null) {
					Registered.RemoveAt(i);
				}
			}
		}

		/// <summary>Registers built-in gizmo commands so the lantern button resolves through the same bridge add-ons use.</summary>
		public static void EnsureCommandsRegistered() {
			if (s_commandsRegistered) {
				return;
			}
			s_commandsRegistered = true;
			RibbonDock_CommandBridge.Register(OverviewCommandId, () => ViewportAxisGizmo_CameraOps.TryOverview());
		}

		#endregion

		#region build

		/// <summary>Creates the whole widget under <paramref name="parent"/>. Scene-singleton free so EditMode tests can build it.</summary>
		public static ViewportAxisGizmo_UI BuildInto(RectTransform parent, ViewportAxisGizmo_Spec spec) {
			if (parent == null) {
				return null;
			}
			EnsureCommandsRegistered();

			var rootGo = new GameObject(RootName, typeof(RectTransform));
			rootGo.layer = parent.gameObject.layer;
			var rootRt = rootGo.GetComponent<RectTransform>();
			rootRt.SetParent(parent, false);

			var gizmo = rootGo.AddComponent<ViewportAxisGizmo_UI>();
			gizmo._root = rootRt;
			gizmo._canvasGroup = rootGo.AddComponent<CanvasGroup>();
			// Viewport must not treat the widget area as "hovering the 3D view" (paint strokes / orbit start).
			rootGo.AddComponent<MainViewport_RaycastBlocker>();
			gizmo.BuildChildren(spec);
			gizmo.ApplySpec(spec);
			gizmo.ApplyThemeTokens();
			rootRt.SetAsLastSibling();
			gizmo.RefreshFromCamera();
			return gizmo;
		}

		void BuildChildren(ViewportAxisGizmo_Spec spec) {
			_spec = spec;
			_palette = ViewportAxisGizmo_Palette.SpzDefault;

			var backdrop = CreateChild(BackdropName, _root);
			backdrop.anchorMin = Vector2.zero;
			backdrop.anchorMax = Vector2.one;
			backdrop.offsetMin = Vector2.zero;
			backdrop.offsetMax = Vector2.zero;
			_backdropImage = backdrop.gameObject.AddComponent<Image>();
			_backdropImage.sprite = UiRuntimeSprites.CircleFilled;
			_backdropImage.color = _palette.Backdrop;
			_backdropImage.raycastTarget = true;

			var axes = ViewportAxisGizmo_Math.AxisDirections;
			for (int i = 0; i < axes.Length; i++) {
				if (!ViewportAxisGizmo_Math.IsPositiveAxis(axes[i])) {
					_lineRects.Add(null);
					_lineImages.Add(null);
					continue;
				}
				var lineRt = CreateChild(LinePrefix + ViewportAxisGizmo_Math.AxisLabel(axes[i]), _root);
				lineRt.anchorMin = new Vector2(0.5f, 0.5f);
				lineRt.anchorMax = new Vector2(0.5f, 0.5f);
				lineRt.pivot = new Vector2(0f, 0.5f);
				lineRt.anchoredPosition = Vector2.zero;
				var lineImg = lineRt.gameObject.AddComponent<Image>();
				lineImg.sprite = UiRuntimeSprites.SolidRect;
				lineImg.color = _palette.StemColor(0.5f);
				lineImg.raycastTarget = false;
				_lineRects.Add(lineRt);
				_lineImages.Add(lineImg);
			}

			BuildCenter(spec);

			for (int i = 0; i < axes.Length; i++) {
				Vector3 axis = axes[i];
				bool positive = ViewportAxisGizmo_Math.IsPositiveAxis(axis);
				string sign = positive ? "+" : "-";
				var handleRt = CreateChild(HandlePrefix + sign + ViewportAxisGizmo_Math.AxisLabel(axis), _root);
				handleRt.anchorMin = new Vector2(0.5f, 0.5f);
				handleRt.anchorMax = new Vector2(0.5f, 0.5f);
				handleRt.pivot = new Vector2(0.5f, 0.5f);

				var img = handleRt.gameObject.AddComponent<Image>();
				img.sprite = positive ? UiRuntimeSprites.CircleFilled : UiRuntimeSprites.CircleRing;
				img.color = _palette.HandleColor(positive, 0.5f);
				img.raycastTarget = true;

				var button = handleRt.gameObject.AddComponent<Button>();
				button.targetGraphic = img;
				button.transition = Selectable.Transition.ColorTint;
				var colors = button.colors;
				colors.normalColor = Color.white;
				colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
				colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
				colors.selectedColor = colors.normalColor;
				colors.fadeDuration = 0.08f;
				button.colors = colors;
				Vector3 captured = axis;
				button.onClick.AddListener(() => OnAxisClicked(captured));

				TextMeshProUGUI label = null;
				if (positive) {
					var labelRt = CreateChild("Label", handleRt);
					labelRt.anchorMin = Vector2.zero;
					labelRt.anchorMax = Vector2.one;
					labelRt.offsetMin = Vector2.zero;
					labelRt.offsetMax = Vector2.zero;
					label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
					label.text = ViewportAxisGizmo_Math.AxisLabel(axis);
					label.alignment = TextAlignmentOptions.Center;
					label.fontStyle = FontStyles.Bold;
					label.raycastTarget = false;
					label.color = _palette.LabelInk;
				}

				_handleRects.Add(handleRt);
				_handleImages.Add(img);
				_handleLabels.Add(label);
			}
		}

		void BuildCenter(ViewportAxisGizmo_Spec spec) {
			var centerRt = CreateChild(CenterName, _root);
			centerRt.anchorMin = new Vector2(0.5f, 0.5f);
			centerRt.anchorMax = new Vector2(0.5f, 0.5f);
			centerRt.pivot = new Vector2(0.5f, 0.5f);
			centerRt.anchoredPosition = Vector2.zero;

			var img = centerRt.gameObject.AddComponent<Image>();
			Sprite lantern = LoadCenterSprite(spec.CenterIconPath);
			img.sprite = lantern != null ? lantern : UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bullseye);
			img.preserveAspect = true;
			ApplyCenterGlyphAppearance(img);

			var button = centerRt.gameObject.AddComponent<Button>();
			button.targetGraphic = img;
			button.transition = Selectable.Transition.ColorTint;
			var colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
			colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
			colors.selectedColor = colors.normalColor;
			colors.fadeDuration = 0.08f;
			button.colors = colors;
			button.onClick.AddListener(OnCenterClicked);

			_centerRt = centerRt;
			_centerImage = img;
			_loadedCenterIconPath = spec.CenterIconPath ?? string.Empty;
		}

		/// <summary>
		/// Grey translucent multiply tint. Transparent pixels must not eat axis-handle clicks — the lantern
		/// sits last-sibling on top of the collapsed handle, and a full-rect raycast made the "see-through"
		/// glyph still block the view.
		/// </summary>
		void ApplyCenterGlyphAppearance(Image img) {
			if (img == null) {
				return;
			}
			Color tint = _palette.CenterTint;
			if (img.color != tint) {
				img.color = tint;
			}
			if (!img.raycastTarget) {
				img.raycastTarget = true;
			}
			// Unity only alpha-tests when the sprite texture is readable (our lantern PNG load keeps it so).
			Texture2D tex = img.sprite != null ? img.sprite.texture : null;
			float hit = tex != null && tex.isReadable ? 0.1f : 0f;
			if (!Mathf.Approximately(img.alphaHitTestMinimumThreshold, hit)) {
				img.alphaHitTestMinimumThreshold = hit;
			}
		}

		static RectTransform CreateChild(string name, RectTransform parent) {
			var go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			var rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			return rt;
		}

		/// <summary>
		/// Loads the add-on's lantern PNG from StreamingAssets (no Resources / built-in resource lookup — IL2CPP safe)
		/// and converts it to a white RGB + alpha silhouette so UI tinting can draw a grey monochrome transparent glyph.
		/// </summary>
		public static Sprite LoadCenterSprite(string absolutePath) {
			string path = string.IsNullOrWhiteSpace(absolutePath)
				? ViewportAxisGizmo_AddonBridge.DefaultCenterIconPath
				: absolutePath;
			if (string.IsNullOrEmpty(path)) {
				return null;
			}
			if (CenterSpriteCache.TryGetValue(path, out Sprite cached) && cached != null) {
				return cached;
			}
			try {
				if (!File.Exists(path)) {
					Debug.LogWarning($"[ViewportAxisGizmo_UI] Center icon missing at '{path}' — falling back to a line icon.");
					return null;
				}
				byte[] bytes = File.ReadAllBytes(path);
				var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
				if (!tex.LoadImage(bytes)) {
					if (Application.isPlaying) { Destroy(tex); } else { DestroyImmediate(tex); }
					return null;
				}
				tex.wrapMode = TextureWrapMode.Clamp;
				ConvertToTintableMonoGlyph(tex);
				var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
				CenterSpriteCache[path] = sprite;
				return sprite;
			}
			catch (IOException e) {
				Debug.LogWarning($"[ViewportAxisGizmo_UI] Could not read center icon '{path}': {e.Message}");
				return null;
			}
		}

		/// <summary>
		/// Turns an opaque full-color badge (navy grid + bronze lantern) into a tintable silhouette:
		/// pixels near the corner background become transparent; the lantern shape keeps alpha; RGB becomes white
		/// so <see cref="CenterGlyphTint"/> multiplies to soft grey.
		/// </summary>
		public static void ConvertToTintableMonoGlyph(Texture2D tex) {
			if (tex == null) {
				return;
			}
			Color[] pixels = tex.GetPixels();
			if (pixels == null || pixels.Length == 0) {
				return;
			}
			int w = tex.width;
			int h = tex.height;
			Color bg = SampleCornerBackground(pixels, w, h);

			float maxDist = 0.001f;
			for (int i = 0; i < pixels.Length; i++) {
				maxDist = Mathf.Max(maxDist, ColorDistanceRgb(pixels[i], bg));
			}

			for (int i = 0; i < pixels.Length; i++) {
				Color c = pixels[i];
				float dist01 = Mathf.Clamp01(ColorDistanceRgb(c, bg) / maxDist);
				// Soft threshold: kill the navy grid, keep lantern / glow / brackets as ink.
				float alpha = Mathf.SmoothStep(0.10f, 0.42f, dist01);
				// Existing PNG alpha (if any) still gates the result.
				alpha *= c.a;
				pixels[i] = new Color(1f, 1f, 1f, alpha);
			}
			tex.SetPixels(pixels);
			// Keep CPU-readable: Image.alphaHitTestMinimumThreshold needs it so empty lantern pixels
			// do not steal clicks from the axis handle that collapses onto the center.
			tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
		}

		static Color SampleCornerBackground(Color[] pixels, int w, int h) {
			// Average a few corner samples so a single bright pixel cannot poison the key.
			Color sum = Color.black;
			int n = 0;
			void Acc(int x, int y) {
				x = Mathf.Clamp(x, 0, w - 1);
				y = Mathf.Clamp(y, 0, h - 1);
				sum += pixels[y * w + x];
				n++;
			}
			Acc(0, 0); Acc(1, 0); Acc(0, 1);
			Acc(w - 1, 0); Acc(w - 2, 0); Acc(w - 1, 1);
			Acc(0, h - 1); Acc(1, h - 1); Acc(0, h - 2);
			Acc(w - 1, h - 1); Acc(w - 2, h - 1); Acc(w - 1, h - 2);
			return n > 0 ? sum / n : new Color(0.05f, 0.08f, 0.18f, 1f);
		}

		static float ColorDistanceRgb(Color a, Color b) {
			float dr = a.r - b.r;
			float dg = a.g - b.g;
			float db = a.b - b.b;
			return Mathf.Sqrt(dr * dr + dg * dg + db * db);
		}

		#endregion

		#region layout + per-frame refresh

		public void ApplySpec(ViewportAxisGizmo_Spec spec) {
			_spec = spec;
			if (_root == null) {
				return;
			}
			_root.sizeDelta = new Vector2(spec.SizePx, spec.SizePx);
			ApplyCornerDock(spec.MarginPx);

			float diameter = HandleDiameterPx;
			for (int i = 0; i < _handleRects.Count; i++) {
				if (_handleRects[i] != null) {
					_handleRects[i].sizeDelta = new Vector2(diameter, diameter);
				}
				if (_handleLabels[i] != null) {
					_handleLabels[i].fontSize = Mathf.Max(8f, diameter * 0.52f);
				}
			}
			if (_centerRt != null) {
				float centerSize = spec.SizePx * 0.34f;
				_centerRt.sizeDelta = new Vector2(centerSize, centerSize);
			}
			RefreshCenterIconIfNeeded(spec.CenterIconPath);
			// Radius and handle size just changed, so the cached projection is stale even if the view did not move.
			_hasAppliedOrientation = false;
		}

		/// <summary>
		/// Pin the gizmo to the top-right of the aspect-fitted image (not the letterboxed main viewport). Parent is
		/// the main viewport chrome so we draw above the view RT; anchors stay top-right of that parent and the
		/// anchored position is the delta from the parent's top-right corner to the inner rect's.
		/// </summary>
		void ApplyCornerDock(float marginPx) {
			Vector2 topRight = new Vector2(1f, 1f);
			if (_root.anchorMin != topRight) {
				_root.anchorMin = topRight;
			}
			if (_root.anchorMax != topRight) {
				_root.anchorMax = topRight;
			}
			if (_root.pivot != topRight) {
				_root.pivot = topRight;
			}

			var parent = _root.parent as RectTransform;
			RectTransform dock = ResolveDockReference();
			Vector2 wanted;
			if (parent == null || dock == null || dock == parent) {
				wanted = new Vector2(-marginPx, -marginPx);
			} else {
				Vector3 innerTopRightWorld = dock.TransformPoint(new Vector3(dock.rect.xMax, dock.rect.yMax, 0f));
				Vector3 parentLocal = parent.InverseTransformPoint(innerTopRightWorld);
				Vector2 parentTopRight = new Vector2(parent.rect.xMax, parent.rect.yMax);
				Vector2 delta = (Vector2)parentLocal - parentTopRight;
				wanted = delta + new Vector2(-marginPx, -marginPx);
			}
			if (_root.anchoredPosition != wanted) {
				_root.anchoredPosition = wanted;
			}
		}

		/// <summary>
		/// Re-attach with a different <c>center_icon</c> must update the lantern, not leave the sprite from the
		/// first attach. Same path is a no-op so per-frame / retry attaches stay cheap.
		/// </summary>
		void RefreshCenterIconIfNeeded(string centerIconPath) {
			if (_centerImage == null) {
				return;
			}
			string path = centerIconPath ?? string.Empty;
			if (string.Equals(path, _loadedCenterIconPath, StringComparison.Ordinal)) {
				ApplyCenterGlyphAppearance(_centerImage);
				return;
			}
			Sprite lantern = LoadCenterSprite(path);
			_centerImage.sprite = lantern != null ? lantern : UiRuntimeSprites.GetLineIcon(StudioLineIcon.Bullseye);
			ApplyCenterGlyphAppearance(_centerImage);
			_loadedCenterIconPath = path;
		}

		float HandleDiameterPx => Mathf.Max(12f, _spec.SizePx * 0.235f);

		float OrbitRadiusPx => Mathf.Max(4f, _spec.SizePx * 0.5f - HandleDiameterPx * 0.5f - 3f);

		void LateUpdate() {
			RefreshFromCamera();
		}

		/// <summary>Re-projects the six axes for the current view rotation and re-sorts them by depth.</summary>
		public void RefreshFromCamera() {
			if (_tornDown || _root == null) {
				return;
			}
			RectTransform wantedHost = ResolveViewportParent();
			if (wantedHost != null) {
				EnsureHostedUnder(wantedHost);
				// Aspect fit moves the inner rect every early-update; re-pin to its corner even when host is stable.
				ApplyCornerDock(_spec.MarginPx);
				// MainViewport_UI_EventListener is a fullscreen raycast target that likes to sit last — if it climbs
				// above us, axis/lantern clicks never reach the buttons.
				if (_root.GetSiblingIndex() != wantedHost.childCount - 1) {
					_root.SetAsLastSibling();
				}
			}
			bool usable = ViewportAxisGizmo_CameraOps.IsGizmoUsable();
			if (_canvasGroup != null) {
				float wantedAlpha = usable ? 1f : 0f;
				if (!Mathf.Approximately(_canvasGroup.alpha, wantedAlpha)) {
					_canvasGroup.alpha = wantedAlpha;
				}
				if (_canvasGroup.blocksRaycasts != usable) {
					_canvasGroup.blocksRaycasts = usable;
				}
				if (_canvasGroup.interactable != usable) {
					_canvasGroup.interactable = usable;
				}
			}
			if (!usable) {
				return;
			}

			Quaternion camRot = ViewportAxisGizmo_CameraOps.CurrentViewRotation();
			ApplyOrientation(camRot);
		}

		/// <summary>
		/// Placement pass for an explicit camera rotation (used by <see cref="RefreshFromCamera"/> and tests).
		/// Returns false when the view has not moved since the last pass: this runs every frame next to painting and
		/// generation, so an idle camera must not re-sort the canvas hierarchy or re-generate TMP meshes.
		/// Authored axis colors are still reasserted on the idle path so a Nomad / BoundChrome spill cannot stick
		/// until the next orbit (theme silo: gizmo never opts into BoundChrome; it keeps Blender-style RGB).
		/// </summary>
		public bool ApplyOrientation(Quaternion cameraRotation) {
			if (_hasAppliedOrientation && Quaternion.Angle(_lastAppliedRotation, cameraRotation) < RotationEpsilonDeg) {
				ReassertAuthoredAxisColors(_lastAppliedRotation);
				return false;
			}
			_lastAppliedRotation = cameraRotation;
			_hasAppliedOrientation = true;

			var axes = ViewportAxisGizmo_Math.AxisDirections;
			float radius = OrbitRadiusPx;
			float diameter = HandleDiameterPx;
			var order = _orderBuffer;
			order.Clear();

			for (int i = 0; i < axes.Length && i < _handleRects.Count; i++) {
				Vector3 axis = axes[i];
				bool positive = ViewportAxisGizmo_Math.IsPositiveAxis(axis);
				Vector2 offset = ViewportAxisGizmo_Math.AxisHandleOffset(cameraRotation, axis, radius);
				float towards = ViewportAxisGizmo_Math.TowardsViewer01(cameraRotation, axis);

				var handleRt = _handleRects[i];
				if (handleRt != null) {
					handleRt.anchoredPosition = offset;
					float scaled = diameter * ViewportAxisGizmo_Math.HandleScale(towards);
					handleRt.sizeDelta = new Vector2(scaled, scaled);
				}

				ApplyAuthoredHandleColors(i, axis, positive, towards);
				UpdateAxisLine(i, offset, towards);

				var label = _handleLabels[i];
				if (label != null) {
					float wantedFontSize = Mathf.Max(8f, diameter * ViewportAxisGizmo_Math.HandleScale(towards) * 0.52f);
					// Assigning fontSize dirties the text even with the same value, forcing a mesh rebuild.
					if (Mathf.Abs(label.fontSize - wantedFontSize) > 0.01f) {
						label.fontSize = wantedFontSize;
					}
				}

				order.Add(new KeyValuePair<int, int>(i, ViewportAxisGizmo_Math.DrawOrderKey(towards)));
			}

			order.Sort(ByDrawOrder);
			if (DepthOrderChanged(order)) {
				_appliedOrder.Clear();
				for (int i = 0; i < order.Count; i++) {
					var rt = _handleRects[order[i].Key];
					if (rt != null) {
						rt.SetAsLastSibling();
					}
					_appliedOrder.Add(order[i].Key);
				}
				// The axis parallel to the view collapses onto the middle (a front view puts -Z exactly there), so the
				// lantern has to stay the topmost sibling or it gets hidden and its clicks are eaten by that handle.
				if (_centerRt != null) {
					_centerRt.SetAsLastSibling();
				}
			}
			return true;
		}

		/// <summary>
		/// Re-write axis chrome from the active <see cref="ViewportAxisGizmo_Palette"/> without touching layout,
		/// sibling order, or TMP fontSize — safe to call every idle frame for theme-silo honesty.
		/// </summary>
		void ReassertAuthoredAxisColors(Quaternion cameraRotation) {
			var axes = ViewportAxisGizmo_Math.AxisDirections;
			for (int i = 0; i < axes.Length && i < _handleRects.Count; i++) {
				Vector3 axis = axes[i];
				bool positive = ViewportAxisGizmo_Math.IsPositiveAxis(axis);
				float towards = ViewportAxisGizmo_Math.TowardsViewer01(cameraRotation, axis);
				ApplyAuthoredHandleColors(i, axis, positive, towards);
				if (i < _lineImages.Count && _lineImages[i] != null) {
					Color c = _palette.StemColor(towards);
					if (_lineImages[i].color != c) {
						_lineImages[i].color = c;
					}
				}
			}
			if (_backdropImage != null && _backdropImage.color != _palette.Backdrop) {
				_backdropImage.color = _palette.Backdrop;
			}
			ApplyCenterGlyphAppearance(_centerImage);
		}

		void ApplyAuthoredHandleColors(int i, Vector3 axis, bool positive, float towards) {
			var img = i < _handleImages.Count ? _handleImages[i] : null;
			if (img != null) {
				Color c = _palette.HandleColor(positive, towards);
				if (img.color != c) {
					img.color = c;
				}
			}
			var label = i < _handleLabels.Count ? _handleLabels[i] : null;
			if (label != null) {
				Color lc = _palette.LabelColor(towards);
				if (label.color != lc) {
					label.color = lc;
				}
			}
		}

		bool DepthOrderChanged(List<KeyValuePair<int, int>> order) {
			if (_appliedOrder.Count != order.Count) {
				return true;
			}
			for (int i = 0; i < order.Count; i++) {
				if (_appliedOrder[i] != order[i].Key) {
					return true;
				}
			}
			return false;
		}

		void UpdateAxisLine(int axisIndex, Vector2 handleOffset, float towardsViewer01) {
			if (axisIndex >= _lineRects.Count) {
				return;
			}
			var lineRt = _lineRects[axisIndex];
			if (lineRt == null) {
				return;
			}
			float length = handleOffset.magnitude;
			lineRt.sizeDelta = new Vector2(length, Mathf.Max(1.5f, _spec.SizePx * 0.022f));
			lineRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(handleOffset.y, handleOffset.x) * Mathf.Rad2Deg);

			var img = _lineImages[axisIndex];
			if (img == null) {
				return;
			}
			Color c = _palette.StemColor(towardsViewer01);
			if (img.color != c) {
				img.color = c;
			}
		}

		#endregion

		#region theme (SPZ default authored + Nomad BoundChrome)

		void EnsureThemeHooked() {
			if (_themeHooked) {
				return;
			}
			_themeHooked = true;
			SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
			ApplyThemeTokens();
		}

		/// <summary>
		/// Nomad parity: restyle under <see cref="SpzUiThemeOps.ShouldRecolorBoundChrome"/>; leave restores BoundChrome
		/// under this root then reasserts the authored SPZ palette (cool grey + sky). Circles stay CircleFilled/Ring —
		/// colors via snapshot + tint / IconTint only (no SolidSquare flatten).
		/// </summary>
		public void ApplyThemeTokens() {
			if (_tornDown || _root == null) {
				return;
			}
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
				SpzUiThemeOps.RestoreBoundChromeUnder(_root);
				_palette = ViewportAxisGizmo_Palette.SpzDefault;
				ApplyStaticPaletteChrome();
				_hasAppliedOrientation = false;
				return;
			}
			var t = SpzUiThemeOps.Active;
			_palette = ViewportAxisGizmo_Palette.FromThemeTokens(t);

			if (_backdropImage != null) {
				// Color-only: ApplyBoundChromeGraphic would FlattenSlicedChromeFace and crush the disc.
				SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(_backdropImage);
				_backdropImage.color = _palette.Backdrop;
				if (_backdropImage.sprite != UiRuntimeSprites.CircleFilled) {
					_backdropImage.sprite = UiRuntimeSprites.CircleFilled;
				}
			}
			for (int i = 0; i < _handleImages.Count; i++) {
				var img = _handleImages[i];
				if (img == null) {
					continue;
				}
				SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(img);
				bool positive = i < ViewportAxisGizmo_Math.AxisDirections.Length
					&& ViewportAxisGizmo_Math.IsPositiveAxis(ViewportAxisGizmo_Math.AxisDirections[i]);
				img.sprite = positive ? UiRuntimeSprites.CircleFilled : UiRuntimeSprites.CircleRing;
				img.color = _palette.HandleColor(positive, 0.5f);
			}
			for (int i = 0; i < _lineImages.Count; i++) {
				var img = _lineImages[i];
				if (img == null) {
					continue;
				}
				SpzUiThemeOps.SnapshotAuthoredGraphicForTheme(img);
				img.color = _palette.StemColor(0.5f);
			}
			for (int i = 0; i < _handleLabels.Count; i++) {
				var label = _handleLabels[i];
				if (label == null) {
					continue;
				}
				SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(label, _palette.LabelInk, label.fontSize);
				label.raycastTarget = false;
			}
			if (_centerImage != null) {
				SpzUiThemeOps.ApplyBoundChromeIconTint(_centerImage, _palette.CenterTint);
			}
			_hasAppliedOrientation = false;
		}

		/// <summary>Writes palette colors without BoundChrome helpers (builtin leave + initial build).</summary>
		void ApplyStaticPaletteChrome() {
			if (_backdropImage != null) {
				_backdropImage.sprite = UiRuntimeSprites.CircleFilled;
				_backdropImage.color = _palette.Backdrop;
			}
			for (int i = 0; i < _handleImages.Count; i++) {
				var img = _handleImages[i];
				if (img == null) {
					continue;
				}
				bool positive = i < ViewportAxisGizmo_Math.AxisDirections.Length
					&& ViewportAxisGizmo_Math.IsPositiveAxis(ViewportAxisGizmo_Math.AxisDirections[i]);
				img.sprite = positive ? UiRuntimeSprites.CircleFilled : UiRuntimeSprites.CircleRing;
				img.color = _palette.HandleColor(positive, 0.5f);
			}
			for (int i = 0; i < _lineImages.Count; i++) {
				if (_lineImages[i] != null) {
					_lineImages[i].color = _palette.StemColor(0.5f);
				}
			}
			for (int i = 0; i < _handleLabels.Count; i++) {
				if (_handleLabels[i] != null) {
					_handleLabels[i].color = _palette.LabelInk;
				}
			}
			ApplyCenterGlyphAppearance(_centerImage);
		}

		#endregion

		#region clicks

		void OnAxisClicked(Vector3 worldAxis) {
			ViewportAxisGizmo_CameraOps.TrySnapToAxis(worldAxis);
		}

		void OnCenterClicked() {
			EnsureCommandsRegistered();
			string command = string.IsNullOrEmpty(_spec.CenterCommandId) ? OverviewCommandId : _spec.CenterCommandId;
			RibbonDock_CommandBridge.TryInvoke(command);
		}

		#endregion

		void Awake() {
			if (_root == null) {
				_root = transform as RectTransform;
			}
			if (_canvasGroup == null) {
				_canvasGroup = GetComponent<CanvasGroup>();
			}
			Registered.Add(this);
			EnsureThemeHooked();
		}

		void OnDestroy() {
			if (_themeHooked) {
				SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
				_themeHooked = false;
			}
			Registered.Remove(this);
		}
	}
}
