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
	/// Blender / 3ds Max style orientation gizmo in the top-right of the 3D view: six axis balls that follow the
	/// camera rotation (+X/+Y/+Z labelled and filled, negatives dim), with the StableProjectorz lantern in the
	/// middle as an "overview" button that re-frames the selection.
	///
	/// Parented to <see cref="MainViewport_UI.innerViewportRect"/> (the aspect-fitted rect the view RT is drawn in),
	/// so it stays on the rendered image when the viewport letterboxes. The root carries
	/// <see cref="MainViewport_RaycastBlocker"/> so paint / orbit do not fire under the widget.
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
		/// <summary>Command id the lantern button invokes by default (re-frame the selection).</summary>
		public const string OverviewCommandId = "viewport_axis_gizmo_overview";

		static readonly List<ViewportAxisGizmo_UI> Registered = new List<ViewportAxisGizmo_UI>();
		static readonly Dictionary<string, Sprite> CenterSpriteCache = new Dictionary<string, Sprite>();
		static bool s_commandsRegistered;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetStatics() {
			// Enter Play Mode with domain reload disabled keeps destroyed instances / dead sprites otherwise.
			Registered.Clear();
			CenterSpriteCache.Clear();
			s_commandsRegistered = false;
		}

		ViewportAxisGizmo_Spec _spec = ViewportAxisGizmo_Spec.Default;
		RectTransform _root;
		CanvasGroup _canvasGroup;
		RectTransform _centerRt;
		readonly List<RectTransform> _handleRects = new List<RectTransform>();
		readonly List<Image> _handleImages = new List<Image>();
		readonly List<TextMeshProUGUI> _handleLabels = new List<TextMeshProUGUI>();
		readonly List<RectTransform> _lineRects = new List<RectTransform>();
		readonly List<Image> _lineImages = new List<Image>();

		public ViewportAxisGizmo_Spec Spec => _spec;
		public RectTransform RootRect => _root;

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
			var existing = FindUnder(parent);
			if (existing != null) {
				existing.ApplySpec(spec);
				return true;
			}
			return BuildInto(parent, spec) != null;
		}

		/// <summary>Inner viewport rect (the aspect-fitted 3D image), or null while scenes load.</summary>
		public static RectTransform ResolveViewportParent() {
			var viewport = MainViewport_UI.instance;
			if (viewport == null) {
				return null;
			}
			if (InnerViewport_SizeReference.instance == null) {
				return viewport.mainViewportRect;
			}
			return viewport.innerViewportRect != null ? viewport.innerViewportRect : viewport.mainViewportRect;
		}

		public static ViewportAxisGizmo_UI FindUnder(RectTransform parent) {
			if (parent == null) {
				return null;
			}
			var found = parent.GetComponentsInChildren<ViewportAxisGizmo_UI>(true);
			for (int i = 0; i < found.Length; i++) {
				if (found[i] != null) {
					return found[i];
				}
			}
			return null;
		}

		public static bool IsAnyVisibleGizmo() {
			PruneRegistered();
			for (int i = 0; i < Registered.Count; i++) {
				var g = Registered[i];
				if (g != null && g.gameObject.activeInHierarchy) {
					return true;
				}
			}
			return false;
		}

		/// <summary>Remove every gizmo (add-on turned off in Add-on Manager).</summary>
		public static void TeardownAllForAddonDisabled() {
			var all = FindObjectsByType<ViewportAxisGizmo_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++) {
				if (all[i] == null) {
					continue;
				}
				var go = all[i].gameObject;
				if (Application.isPlaying) {
					Destroy(go);
				} else {
					DestroyImmediate(go);
				}
			}
			Registered.Clear();
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
			gizmo.RefreshFromCamera();
			return gizmo;
		}

		void BuildChildren(ViewportAxisGizmo_Spec spec) {
			_spec = spec;

			var backdrop = CreateChild(BackdropName, _root);
			backdrop.anchorMin = Vector2.zero;
			backdrop.anchorMax = Vector2.one;
			backdrop.offsetMin = Vector2.zero;
			backdrop.offsetMax = Vector2.zero;
			var backdropImg = backdrop.gameObject.AddComponent<Image>();
			backdropImg.sprite = UiRuntimeSprites.CircleFilled;
			backdropImg.color = new Color(0.06f, 0.07f, 0.09f, 0.34f);
			backdropImg.raycastTarget = true;

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
				lineImg.color = ViewportAxisGizmo_Math.AxisColor(axes[i]);
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
				img.color = ViewportAxisGizmo_Math.AxisColor(axis);
				img.raycastTarget = true;

				var button = handleRt.gameObject.AddComponent<Button>();
				button.targetGraphic = img;
				button.transition = Selectable.Transition.ColorTint;
				var colors = button.colors;
				colors.normalColor = Color.white;
				colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
				colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
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
					label.color = new Color(0.06f, 0.07f, 0.09f, 1f);
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
			img.color = Color.white;
			img.raycastTarget = true;

			var button = centerRt.gameObject.AddComponent<Button>();
			button.targetGraphic = img;
			button.transition = Selectable.Transition.ColorTint;
			var colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
			colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
			colors.selectedColor = colors.normalColor;
			colors.fadeDuration = 0.08f;
			button.colors = colors;
			button.onClick.AddListener(OnCenterClicked);

			_centerRt = centerRt;
		}

		static RectTransform CreateChild(string name, RectTransform parent) {
			var go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			var rt = go.GetComponent<RectTransform>();
			rt.SetParent(parent, false);
			return rt;
		}

		/// <summary>Loads the add-on's lantern PNG from StreamingAssets (no Resources / built-in resource lookup — IL2CPP safe).</summary>
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
					return null;
				}
				byte[] bytes = File.ReadAllBytes(path);
				var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
				if (!tex.LoadImage(bytes)) {
					if (Application.isPlaying) { Destroy(tex); } else { DestroyImmediate(tex); }
					return null;
				}
				tex.wrapMode = TextureWrapMode.Clamp;
				var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
				CenterSpriteCache[path] = sprite;
				return sprite;
			}
			catch (IOException e) {
				Debug.LogWarning($"[ViewportAxisGizmo_UI] Could not read center icon '{path}': {e.Message}");
				return null;
			}
		}

		#endregion

		#region layout + per-frame refresh

		public void ApplySpec(ViewportAxisGizmo_Spec spec) {
			_spec = spec;
			if (_root == null) {
				return;
			}
			_root.anchorMin = new Vector2(1f, 1f);
			_root.anchorMax = new Vector2(1f, 1f);
			_root.pivot = new Vector2(1f, 1f);
			_root.sizeDelta = new Vector2(spec.SizePx, spec.SizePx);
			_root.anchoredPosition = new Vector2(-spec.MarginPx, -spec.MarginPx);

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
				float centerSize = spec.SizePx * 0.42f;
				_centerRt.sizeDelta = new Vector2(centerSize, centerSize);
			}
		}

		float HandleDiameterPx => Mathf.Max(12f, _spec.SizePx * 0.235f);

		float OrbitRadiusPx => Mathf.Max(4f, _spec.SizePx * 0.5f - HandleDiameterPx * 0.5f - 3f);

		void LateUpdate() {
			RefreshFromCamera();
		}

		/// <summary>Re-projects the six axes for the current view rotation and re-sorts them by depth.</summary>
		public void RefreshFromCamera() {
			if (_root == null) {
				return;
			}
			bool usable = ViewportAxisGizmo_CameraOps.IsGizmoUsable();
			if (_canvasGroup != null) {
				_canvasGroup.alpha = usable ? 1f : 0f;
				_canvasGroup.blocksRaycasts = usable;
				_canvasGroup.interactable = usable;
			}
			if (!usable) {
				return;
			}

			Quaternion camRot = ViewportAxisGizmo_CameraOps.CurrentViewRotation();
			ApplyOrientation(camRot);
		}

		/// <summary>Placement pass for an explicit camera rotation (used by <see cref="RefreshFromCamera"/> and tests).</summary>
		public void ApplyOrientation(Quaternion cameraRotation) {
			var axes = ViewportAxisGizmo_Math.AxisDirections;
			float radius = OrbitRadiusPx;
			float diameter = HandleDiameterPx;
			var order = new List<KeyValuePair<int, int>>(axes.Length);

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

				var img = _handleImages[i];
				if (img != null) {
					Color c = ViewportAxisGizmo_Math.AxisColor(axis);
					c.a = ViewportAxisGizmo_Math.HandleAlpha(towards, positive);
					if (img.color != c) {
						img.color = c;
					}
				}

				var label = _handleLabels[i];
				if (label != null) {
					Color lc = label.color;
					lc.a = ViewportAxisGizmo_Math.HandleAlpha(towards, true);
					if (label.color != lc) {
						label.color = lc;
					}
					label.fontSize = Mathf.Max(8f, diameter * ViewportAxisGizmo_Math.HandleScale(towards) * 0.52f);
				}

				UpdateAxisLine(i, offset, towards);
				order.Add(new KeyValuePair<int, int>(i, ViewportAxisGizmo_Math.DrawOrderKey(towards)));
			}

			order.Sort((a, b) => a.Value.CompareTo(b.Value));
			for (int i = 0; i < order.Count; i++) {
				var rt = _handleRects[order[i].Key];
				if (rt != null) {
					rt.SetAsLastSibling();
				}
			}
			// The axis parallel to the view collapses onto the middle (a front view puts -Z exactly there), so the
			// lantern has to stay the topmost sibling or it gets hidden and its clicks are eaten by that handle.
			if (_centerRt != null) {
				_centerRt.SetAsLastSibling();
			}
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
			Color c = ViewportAxisGizmo_Math.AxisColor(ViewportAxisGizmo_Math.AxisDirections[axisIndex]);
			c.a = ViewportAxisGizmo_Math.HandleAlpha(towardsViewer01, true) * 0.8f;
			if (img.color != c) {
				img.color = c;
			}
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
		}

		void OnDestroy() {
			Registered.Remove(this);
		}
	}
}
