using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace spz {

	/// <summary>
	/// Thin horizontal drag handle between two Paint tab section roots.
	/// Transfers preferred height between adjacent <see cref="LayoutElement"/>s while dragging.
	/// Micro: docs/delta/20_micro/paint-tab-section-splitters.md
	/// </summary>
	public sealed class PaintTab_SectionSplitter_UI : MonoBehaviour,
		IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		public const float HandleHeight = 6f;

		LayoutElement _above;
		LayoutElement _below;
		Action _onDragEnded;
		bool _dragActive;

		Image _bar;

		public LayoutElement Above => _above;
		public LayoutElement Below => _below;
		public Image Bar => _bar;
		public bool IsDragging => _dragActive;

		public void Bind(LayoutElement above, LayoutElement below, Action onDragEnded = null)
		{
			_above = above;
			_below = below;
			_onDragEnded = onDragEnded;
		}

		void OnDisable()
		{
			// Pointer can be lost without OnEndDrag (tab hide, disable). Unlock to flex weights.
			if (_dragActive)
				FinishDrag();
		}

		/// <summary>Builds a splitter GO under <paramref name="parent"/> (or configures an existing one).</summary>
		public static PaintTab_SectionSplitter_UI EnsureOn(
			Transform parent,
			string name,
			LayoutElement above,
			LayoutElement below,
			Action onDragEnded,
			Color defaultBarColor)
		{
			if (parent == null) return null;
			Transform existing = parent.Find(name);
			GameObject go = existing != null ? existing.gameObject : new GameObject(name);
			if (existing == null)
				go.transform.SetParent(parent, false);

			var rt = go.GetComponent<RectTransform>();
			if (rt == null) rt = go.AddComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = new Vector2(0f, HandleHeight);

			var le = go.GetComponent<LayoutElement>();
			if (le == null) le = go.AddComponent<LayoutElement>();
			le.minHeight = HandleHeight;
			le.preferredHeight = HandleHeight;
			le.flexibleHeight = 0f;
			le.flexibleWidth = 1f;

			var img = go.GetComponent<Image>();
			if (img == null) img = go.AddComponent<Image>();
			img.raycastTarget = true;
			if (!SpzUiThemeOps.ShouldRecolorBoundChrome)
				img.color = defaultBarColor;

			var splitter = go.GetComponent<PaintTab_SectionSplitter_UI>();
			if (splitter == null) splitter = go.AddComponent<PaintTab_SectionSplitter_UI>();
			splitter._bar = img;
			splitter.Bind(above, below, onDragEnded);
			return splitter;
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
				return;
			_dragActive = true;
			LockPreferredFromRect(_above);
			LockPreferredFromRect(_below);
			RebuildParentLayout();
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (!_dragActive || eventData == null) return;
			if (eventData.button != PointerEventData.InputButton.Left) return;
			ApplyDragDelta(_above, _below, ScreenDeltaToLayoutY(eventData.delta.y));
			RebuildParentLayout();
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			FinishDrag();
		}

		void FinishDrag()
		{
			if (!_dragActive) return;
			_dragActive = false;
			_onDragEnded?.Invoke();
			RebuildParentLayout();
		}

		float ScreenDeltaToLayoutY(float screenDeltaY)
		{
			float scale = 1f;
			var canvas = GetComponentInParent<Canvas>();
			if (canvas != null && canvas.scaleFactor > 0.01f)
				scale = canvas.scaleFactor;
			return screenDeltaY / scale;
		}

		void RebuildParentLayout()
		{
			var parent = transform.parent as RectTransform;
			if (parent != null)
				LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
		}

		/// <summary>
		/// Pointer moved up (positive <paramref name="deltaY"/>) shrinks above and grows below.
		/// Clamps each side to its <see cref="LayoutElement.minHeight"/> (at least 1).
		/// </summary>
		public static void ApplyDragDelta(LayoutElement above, LayoutElement below, float deltaY)
		{
			if (above == null || below == null) return;

			float minA = Mathf.Max(1f, above.minHeight);
			float minB = Mathf.Max(1f, below.minHeight);
			float aboveH = above.preferredHeight > 0f ? above.preferredHeight : minA;
			float belowH = below.preferredHeight > 0f ? below.preferredHeight : minB;

			float newAbove = aboveH - deltaY;
			float newBelow = belowH + deltaY;

			if (newAbove < minA) {
				float fix = minA - newAbove;
				newAbove = minA;
				newBelow -= fix;
			}
			if (newBelow < minB) {
				float fix = minB - newBelow;
				newBelow = minB;
				newAbove -= fix;
			}
			if (newAbove < minA || newBelow < minB)
				return;

			above.preferredHeight = newAbove;
			below.preferredHeight = newBelow;
			above.flexibleHeight = 0f;
			below.flexibleHeight = 0f;
		}

		public static void LockPreferredFromRect(LayoutElement le)
		{
			if (le == null) return;
			var rt = le.transform as RectTransform;
			float h = rt != null && rt.rect.height > 1f ? rt.rect.height : Mathf.Max(le.minHeight, 1f);
			le.preferredHeight = h;
			le.flexibleHeight = 0f;
		}
	}
}
