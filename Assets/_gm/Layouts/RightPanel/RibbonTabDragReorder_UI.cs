using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Drag-to-reorder handle for one CommandRibbon strip tab (ControlNet, Art, Paint, add-on tabs).
	/// Only attached while "dynamic tab movement" is unlocked in Settings — when locked, the component is
	/// removed so strip pointer handling (tab click, ScrollRect pan) stays exactly as authored.
	/// Mid-drag the cell floats over the strip (layout ignored) and a placeholder holds its gap, so sibling
	/// swaps do not rebuild / retheme the row every pointer tick.
	/// </summary>
	[DisallowMultipleComponent]
	public class RibbonTabDragReorder_UI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
		IInitializePotentialDragHandler, IPointerUpHandler, ICancelHandler {
		/// <summary>Legacy child name for an older gold top-bar affordance; cleaned on refresh so leftover grips vanish.</summary>
		public const string GRIP_CHILD_NAME = "TabDragGrip";
		/// <summary>Invisible layout gap that stands in for the floating tab while it follows the pointer.</summary>
		public const string PLACEHOLDER_NAME = "TabDragSlotPlaceholder";
		/// <summary>Pointer travel (strip-local px) required after a placeholder move — stops slot ping-pong when neighbors shift.</summary>
		const float kSlotSwapMarginPx = 10f;

		static int _activeDragCount;

		/// <summary>True while a strip tab is being dragged: the ribbon skips its heavy per-frame reflow until drop.</summary>
		public static bool IsDraggingAnyTab => _activeDragCount > 0;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetDragStatics() {
			_activeDragCount = 0;
		}

		CommandRibbon_UI _owner;
		Transform _strip;
		bool _dragging;
		bool _ignoreLayoutWas;
		bool _hadLayoutElement;
		bool _floatRectCaptured;
		LayoutElement _layout;
		RectTransform _cellRt;
		GameObject _placeholder;
		float _dragOffsetX;
		float _homeLocalY;
		float _lastSwapLocalX;
		int _lastSlot;
		int _homeSiblingIndex;
		Vector2 _savedAnchorMin;
		Vector2 _savedAnchorMax;
		Vector2 _savedPivot;
		Vector2 _savedSizeDelta;
		readonly List<RectTransform> _cellsBuffer = new List<RectTransform>();

		public void Bind(CommandRibbon_UI owner, Transform strip) {
			_owner = owner;
			_strip = strip != null ? strip : transform.parent;
		}

		/// <summary>
		/// Slot (index among tab cells, dividers excluded) the pointer sits over: the cell whose rect contains
		/// <paramref name="pointerLocalX"/>, else the nearest cell center. Strip-local X, same space as child <c>localPosition</c>.
		/// </summary>
		public static int ComputeTargetSlot(IList<RectTransform> tabCellsInSlotOrder, float pointerLocalX) {
			if (tabCellsInSlotOrder == null || tabCellsInSlotOrder.Count == 0) return -1;
			int nearest = -1;
			float nearestDist = float.MaxValue;
			for (int i = 0; i < tabCellsInSlotOrder.Count; i++) {
				RectTransform cell = tabCellsInSlotOrder[i];
				if (cell == null) continue;
				float center = cell.localPosition.x;
				float half = Mathf.Abs(cell.rect.width) * 0.5f;
				if (pointerLocalX >= center - half && pointerLocalX <= center + half)
					return i;
				float dist = Mathf.Abs(pointerLocalX - center);
				if (dist >= nearestDist) continue;
				nearestDist = dist;
				nearest = i;
			}
			return nearest;
		}

		/// <summary>
		/// Insert slot for a tab that is floating (not in the layout): how many <paramref name="otherTabCells"/>
		/// have their center left of the pointer. Result is 0..otherCount (last = after every remaining tab).
		/// </summary>
		public static int ComputeInsertSlot(IList<RectTransform> otherTabCells, float pointerLocalX) {
			if (otherTabCells == null) return 0;
			int slot = 0;
			for (int i = 0; i < otherTabCells.Count; i++) {
				RectTransform cell = otherTabCells[i];
				if (cell == null) continue;
				if (cell.localPosition.x <= pointerLocalX)
					slot++;
			}
			return slot;
		}

		/// <summary>Places the drag gap before <paramref name="otherTabCells"/>[<paramref name="targetSlot"/>], or after the last cell when the slot is past the end.</summary>
		public static bool MovePlaceholderToSlot(Transform placeholder, IList<RectTransform> otherTabCells, int targetSlot) {
			if (placeholder == null || otherTabCells == null) return false;
			int n = otherTabCells.Count;
			if (targetSlot < 0 || (n == 0 && targetSlot != 0)) return false;
			if (n == 0) return false;
			int before = placeholder.GetSiblingIndex();
			if (targetSlot >= n)
				InsertAfter(placeholder, otherTabCells[n - 1]);
			else
				InsertBefore(placeholder, otherTabCells[targetSlot]);
			return placeholder.GetSiblingIndex() != before;
		}

		static void InsertBefore(Transform moving, Transform target) {
			if (moving == null || target == null || moving.parent != target.parent) return;
			int i = target.GetSiblingIndex();
			int cur = moving.GetSiblingIndex();
			if (cur < i) i--;
			if (cur != i)
				moving.SetSiblingIndex(i);
		}

		static void InsertAfter(Transform moving, Transform target) {
			if (moving == null || target == null || moving.parent != target.parent) return;
			int i = target.GetSiblingIndex();
			int cur = moving.GetSiblingIndex();
			int want = cur < i ? i : i + 1;
			if (cur != want)
				moving.SetSiblingIndex(want);
		}

		/// <summary>
		/// Keeps a floating tab inside the visible strip / ScrollRect viewport so it cannot slide
		/// past the panel frame (ignoreLayout children are not clipped by the ribbon).
		/// </summary>
		public static float ClampLocalXToStrip(RectTransform strip, RectTransform cell, float localX) {
			if (strip == null || cell == null) return localX;
			float half = Mathf.Max(1f, Mathf.Abs(cell.rect.width) * 0.5f);
			GetStripLocalXClipRange(strip, out float left, out float right);
			float min = left + half;
			float max = right - half;
			if (max < min) return (min + max) * 0.5f;
			return Mathf.Clamp(localX, min, max);
		}

		/// <summary>
		/// Visible horizontal clip in strip-local X. Prefers a parent <see cref="ScrollRect.viewport"/>
		/// when the strip content is wider than the ribbon frame.
		/// </summary>
		public static void GetStripLocalXClipRange(RectTransform strip, out float left, out float right) {
			left = 0f;
			right = 0f;
			if (strip == null) return;
			RectTransform clip = strip;
			var scroll = strip.GetComponentInParent<ScrollRect>();
			if (scroll != null && scroll.viewport != null)
				clip = scroll.viewport;
			if (ReferenceEquals(clip, strip)) {
				left = strip.rect.xMin;
				right = strip.rect.xMax;
				return;
			}
			Vector3[] corners = new Vector3[4];
			clip.GetWorldCorners(corners);
			float x0 = strip.InverseTransformPoint(corners[0]).x;
			float x1 = strip.InverseTransformPoint(corners[2]).x;
			left = Mathf.Min(x0, x1);
			right = Mathf.Max(x0, x1);
		}

		/// <summary>
		/// Strip tabs use stretch anchors under the HLG. Once <c>ignoreLayout</c> is set they fill the
		/// whole strip and stick out past the panel L/R — freeze to a center-pivoted fixed size first.
		/// </summary>
		public static void ApplyFloatingTabRect(RectTransform strip, RectTransform cell) {
			if (strip == null || cell == null) return;
			float w = Mathf.Max(1f, Mathf.Abs(cell.rect.width));
			float h = Mathf.Max(1f, Mathf.Abs(cell.rect.height));
			Vector3 worldCenter = cell.TransformPoint(cell.rect.center);
			Vector3 localCenter = strip.InverseTransformPoint(worldCenter);
			cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 0.5f);
			cell.pivot = new Vector2(0.5f, 0.5f);
			cell.sizeDelta = new Vector2(w, h);
			cell.localPosition = new Vector3(localCenter.x, localCenter.y, cell.localPosition.z);
		}

		public void OnInitializePotentialDrag(PointerEventData eventData) {
			if (eventData == null) return;
			eventData.useDragThreshold = true;
		}

		public void OnBeginDrag(PointerEventData eventData) {
			_dragging = false;
			if (!RibbonTabOrder_Prefs.IsDynamicTabMovementEnabled()) return;
			if (_strip == null) _strip = transform.parent;
			if (_strip == null) return;
			_cellRt = transform as RectTransform;
			if (_cellRt == null) return;

			var stripRect = _strip as RectTransform;
			if (stripRect == null) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
				    stripRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
				return;

			_dragging = true;
			_activeDragCount++;
			_homeSiblingIndex = transform.GetSiblingIndex();
			_lastSwapLocalX = local.x;
			_lastSlot = HomeSlot();

			_layout = GetComponent<LayoutElement>();
			_hadLayoutElement = _layout != null;
			if (_layout == null)
				_layout = gameObject.AddComponent<LayoutElement>();
			_ignoreLayoutWas = _layout.ignoreLayout;
			EnsurePlaceholder(_layout, _cellRt);
			_layout.ignoreLayout = true;
			CaptureAndApplyFloatingTabRect(stripRect);
			_homeLocalY = _cellRt.localPosition.y;
			_dragOffsetX = _cellRt.localPosition.x - local.x;
			transform.SetAsLastSibling();
		}

		void CaptureAndApplyFloatingTabRect(RectTransform stripRect) {
			if (_cellRt == null || stripRect == null) return;
			_savedAnchorMin = _cellRt.anchorMin;
			_savedAnchorMax = _cellRt.anchorMax;
			_savedPivot = _cellRt.pivot;
			_savedSizeDelta = _cellRt.sizeDelta;
			_floatRectCaptured = true;
			ApplyFloatingTabRect(stripRect, _cellRt);
		}

		void RestoreFloatingTabRect() {
			if (_cellRt == null || !_floatRectCaptured) return;
			_cellRt.anchorMin = _savedAnchorMin;
			_cellRt.anchorMax = _savedAnchorMax;
			_cellRt.pivot = _savedPivot;
			_cellRt.sizeDelta = _savedSizeDelta;
			_floatRectCaptured = false;
		}

		public void OnDrag(PointerEventData eventData) {
			if (!_dragging || _cellRt == null) return;
			var stripRect = _strip as RectTransform;
			if (stripRect == null) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
				    stripRect, eventData.position, eventData.pressEventCamera, out Vector2 local))
				return;

			Vector3 pos = _cellRt.localPosition;
			pos.x = ClampLocalXToStrip(stripRect, _cellRt, local.x + _dragOffsetX);
			pos.y = _homeLocalY;
			_cellRt.localPosition = pos;

			if (Mathf.Abs(local.x - _lastSwapLocalX) < kSlotSwapMarginPx) return;
			CommandRibbon_UI.CollectStripTabCellRects(_strip, _cellsBuffer, transform);
			int target = ComputeInsertSlot(_cellsBuffer, local.x);
			if (target == _lastSlot) {
				_lastSwapLocalX = local.x;
				return;
			}
			if (_placeholder != null && MovePlaceholderToSlot(_placeholder.transform, _cellsBuffer, target)) {
				_lastSlot = target;
				_lastSwapLocalX = local.x;
				CommandRibbon_UI.RebuildStripLayoutImmediate(_strip);
			}
		}

		public void OnEndDrag(PointerEventData eventData) => FinishDrag(eventData, commit: true);

		public void OnPointerUp(PointerEventData eventData) {
			// EndDrag is skipped when the press ends over another canvas / the viewport.
			if (_dragging) FinishDrag(eventData, commit: true);
		}

		public void OnCancel(BaseEventData eventData) {
			if (_dragging) EndDragState(commit: false, dropSlot: -1);
		}

		void FinishDrag(PointerEventData eventData, bool commit) {
			if (!_dragging) return;
			int dropSlot = _lastSlot;
			var stripRect = _strip as RectTransform;
			if (commit && eventData != null && stripRect != null
			    && RectTransformUtility.ScreenPointToLocalPointInRectangle(
				    stripRect, eventData.position, eventData.pressEventCamera, out Vector2 local)) {
				CommandRibbon_UI.CollectStripTabCellRects(_strip, _cellsBuffer, transform);
				dropSlot = ComputeInsertSlot(_cellsBuffer, local.x);
			}
			EndDragState(commit, dropSlot);
			if (!commit) return;
			var owner = _owner != null ? _owner : CommandRibbon_UI.instance;
			if (owner != null) owner.OnStripTabDropped();
		}

		void OnDisable() {
			if (!_dragging) return;
			EndDragState(commit: false, dropSlot: -1);
		}

		/// <summary>Puts the cell back in the layout before any drop reflow, so the commit is not treated as mid-drag.</summary>
		void EndDragState(bool commit, int dropSlot) {
			_dragging = false;
			if (_activeDragCount > 0) _activeDragCount--;
			// Seat the tab on the gap before the placeholder vanishes — otherwise it pops at last-sibling
			// (often over the viewport) for a frame.
			if (commit && _placeholder != null && _cellRt != null) {
				var gap = _placeholder.transform as RectTransform;
				if (gap != null) {
					Vector3 p = _cellRt.localPosition;
					p.x = gap.localPosition.x;
					p.y = _homeLocalY;
					_cellRt.localPosition = p;
				}
			}
			DestroyPlaceholder();
			if (commit && dropSlot >= 0)
				CommandRibbon_UI.MoveStripTabToSlot(_strip, transform, dropSlot);
			else
				transform.SetSiblingIndex(_homeSiblingIndex);
			if (_layout != null) {
				_layout.ignoreLayout = _ignoreLayoutWas;
				if (!_hadLayoutElement) {
					if (Application.isPlaying)
						Destroy(_layout);
					else
						DestroyImmediate(_layout);
				}
				_layout = null;
			}
			// Restore stretch anchors after layout reclaims the cell — otherwise one frame of
			// stretch-fill still paints the tab across the whole ribbon frame.
			RestoreFloatingTabRect();
		}

		void DestroyPlaceholder() {
			if (_placeholder == null) return;
			// Immediate: a deferred Destroy would leave the gap in the layout for a frame next to the returned cell.
			Object.DestroyImmediate(_placeholder);
			_placeholder = null;
		}

		int HomeSlot() {
			CommandRibbon_UI.CollectStripTabCellRects(_strip, _cellsBuffer);
			for (int i = 0; i < _cellsBuffer.Count; i++) {
				if (_cellsBuffer[i] != null && _cellsBuffer[i].transform == transform)
					return i;
			}
			return 0;
		}

		void EnsurePlaceholder(LayoutElement src, RectTransform cell) {
			DestroyPlaceholder();
			if (_strip == null || src == null || cell == null) return;
			_placeholder = new GameObject(PLACEHOLDER_NAME);
			_placeholder.transform.SetParent(_strip, false);
			_placeholder.AddComponent<RectTransform>();
			var le = _placeholder.AddComponent<LayoutElement>();
			le.minWidth = Mathf.Max(1f, src.minWidth > 0.5f ? src.minWidth : cell.rect.width);
			le.preferredWidth = Mathf.Max(le.minWidth, src.preferredWidth);
			le.flexibleWidth = src.flexibleWidth;
			le.minHeight = src.minHeight > 0.5f ? src.minHeight : cell.rect.height;
			le.preferredHeight = src.preferredHeight > 0.5f ? src.preferredHeight : cell.rect.height;
			le.flexibleHeight = src.flexibleHeight;
			_placeholder.transform.SetSiblingIndex(_homeSiblingIndex);
		}

		/// <summary>
		/// Previously painted a gold top bar on unlocked tabs; that read as chrome noise on the strip.
		/// Keep the API so callers / leftover grips from older sessions are cleaned — no visual mark.
		/// Drag still works via <see cref="RibbonTabDragReorder_UI"/> on the cell when unlocked.
		/// </summary>
		public static void EnsureGripVisual(Transform tabCell) => RemoveGripVisual(tabCell);

		public static void RemoveGripVisual(Transform tabCell) {
			if (tabCell == null) return;
			Transform grip = FindGrip(tabCell);
			if (grip == null) return;
			if (Application.isPlaying)
				Object.Destroy(grip.gameObject);
			else
				Object.DestroyImmediate(grip.gameObject);
		}

		public static Transform FindGrip(Transform tabCell) {
			if (tabCell == null) return null;
			for (int i = 0; i < tabCell.childCount; i++) {
				Transform c = tabCell.GetChild(i);
				if (c != null && c.name == GRIP_CHILD_NAME)
					return c;
			}
			return null;
		}
	}
}//end namespace
