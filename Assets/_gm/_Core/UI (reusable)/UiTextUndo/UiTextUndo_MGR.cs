using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace spz {

	/// <summary>Per-field undo/redo stacks for <see cref="TMP_InputField"/> (all UI text boxes). Independent of <see cref="PaintUndo_MGR"/>.</summary>
	public class UiTextUndo_MGR : MonoBehaviour {

		public static UiTextUndo_MGR instance { get; private set; }

		[SerializeField] int _maxUndoSteps = 64;
		[SerializeField] float _rescanInterval = 1.25f;

		readonly Dictionary<TMP_InputField, FieldStacks> _stacks = new Dictionary<TMP_InputField, FieldStacks>(64);
		float _lastRescan;
		bool _applying;

		struct UiTextSnapshot {
			public string Text;
			public int Caret;
			public int Anchor;
			public int Focus;

			public UiTextSnapshot(string text, int caret, int anchor, int focus) {
				Text = text ?? string.Empty;
				Caret = caret;
				Anchor = anchor;
				Focus = focus;
			}
		}

		class FieldStacks {
			public readonly List<UiTextSnapshot> Undo = new List<UiTextSnapshot>(32);
			public readonly List<UiTextSnapshot> Redo = new List<UiTextSnapshot>(16);
			public string PriorText = string.Empty;
			public int PriorCaret, PriorAnchor, PriorFocus;
			public bool PriorCaptured;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void BootstrapAfterSceneLoad() => EnsureExists();

		void Awake() {
			if (instance != null && instance != this) {
				Destroy(gameObject);
				return;
			}
			instance = this;
			DontDestroyOnLoad(gameObject);
		}

		void OnDestroy() {
			if (instance == this) instance = null;
		}

		void Start() {
			_lastRescan = Time.unscaledTime;
			RescanAndRegisterFields();
		}

		public static void EnsureExists() {
			if (instance != null) return;
			var go = new GameObject("UiTextUndo_MGR");
			go.AddComponent<UiTextUndo_MGR>();
			go.AddComponent<UiTextUndo_Input>();
			DontDestroyOnLoad(go);
		}

		void LateUpdate() {
			if (Time.unscaledTime - _lastRescan >= _rescanInterval) {
				_lastRescan = Time.unscaledTime;
				RescanAndRegisterFields();
				PurgeDestroyedFields();
			}
		}

		void RescanAndRegisterFields() {
			var fields = FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < fields.Length; i++) {
				var f = fields[i];
				if (f == null) continue;
				TryRegister(f);
			}
		}

		void PurgeDestroyedFields() {
			if (_stacks.Count == 0) return;
			var keys = new List<TMP_InputField>(_stacks.Keys);
			for (int i = 0; i < keys.Count; i++) {
				var k = keys[i];
				if (k == null) _stacks.Remove(k);
			}
		}

		void TryRegister(TMP_InputField f) {
			if (f == null || _stacks.ContainsKey(f)) return;
			var st = new FieldStacks();
			_stacks[f] = st;
			f.onSelect.AddListener(_ => OnFieldSelect(f));
			f.onDeselect.AddListener(_ => OnFieldDeselect(f));
			f.onValueChanged.AddListener(_ => OnFieldValueChanged(f));
		}

		void OnFieldSelect(TMP_InputField f) {
			if (!_stacks.TryGetValue(f, out var st)) return;
			st.Redo.Clear();
			CapturePriorState(f, st);
		}

		void OnFieldDeselect(TMP_InputField f) {
			if (!_stacks.TryGetValue(f, out var st)) return;
			st.PriorCaptured = false;
		}

		void OnFieldValueChanged(TMP_InputField f) {
			if (_applying) return;
			if (!_stacks.TryGetValue(f, out var st)) return;
			if (!f.interactable || f.readOnly) {
				CapturePriorState(f, st);
				return;
			}
			if (!st.PriorCaptured) {
				CapturePriorState(f, st);
				return;
			}
			string newText = f.text ?? string.Empty;
			st.Undo.Add(new UiTextSnapshot(st.PriorText, st.PriorCaret, st.PriorAnchor, st.PriorFocus));
			TrimToMax(st.Undo);
			st.Redo.Clear();
			st.PriorText = newText;
			st.PriorCaret = f.caretPosition;
			st.PriorAnchor = f.selectionAnchorPosition;
			st.PriorFocus = f.selectionFocusPosition;
		}

		void TrimToMax(List<UiTextSnapshot> list) {
			int cap = Mathf.Max(4, _maxUndoSteps);
			while (list.Count > cap)
				list.RemoveAt(0);
		}

		void CapturePriorState(TMP_InputField f, FieldStacks st) {
			st.PriorText = f.text ?? string.Empty;
			st.PriorCaret = f.caretPosition;
			st.PriorAnchor = f.selectionAnchorPosition;
			st.PriorFocus = f.selectionFocusPosition;
			st.PriorCaptured = true;
		}

		static TMP_InputField GetFocusedTmpField() {
			var es = UnityEngine.EventSystems.EventSystem.current;
			if (es?.currentSelectedGameObject == null) return null;
			return es.currentSelectedGameObject.GetComponent<TMP_InputField>();
		}

		UiTextSnapshot CaptureCurrent(TMP_InputField f) {
			return new UiTextSnapshot(f.text ?? string.Empty, f.caretPosition, f.selectionAnchorPosition, f.selectionFocusPosition);
		}

		void ApplySnapshot(TMP_InputField f, UiTextSnapshot s) {
			_applying = true;
			try {
				f.SetTextWithoutNotify(s.Text);
				int len = s.Text.Length;
				f.caretPosition = Mathf.Clamp(s.Caret, 0, len);
				f.selectionAnchorPosition = Mathf.Clamp(s.Anchor, 0, len);
				f.selectionFocusPosition = Mathf.Clamp(s.Focus, 0, len);
			} finally {
				_applying = false;
			}
		}

		public bool TryUndo() {
			var f = GetFocusedTmpField();
			if (f == null || !_stacks.TryGetValue(f, out var st)) return false;
			if (st.Undo.Count == 0) return false;
			var current = CaptureCurrent(f);
			int last = st.Undo.Count - 1;
			var prev = st.Undo[last];
			st.Undo.RemoveAt(last);
			st.Redo.Add(current);
			TrimToMax(st.Redo);
			ApplySnapshot(f, prev);
			CapturePriorState(f, st);
			return true;
		}

		public bool TryRedo() {
			var f = GetFocusedTmpField();
			if (f == null || !_stacks.TryGetValue(f, out var st)) return false;
			if (st.Redo.Count == 0) return false;
			var current = CaptureCurrent(f);
			int last = st.Redo.Count - 1;
			var next = st.Redo[last];
			st.Redo.RemoveAt(last);
			st.Undo.Add(current);
			TrimToMax(st.Undo);
			ApplySnapshot(f, next);
			CapturePriorState(f, st);
			return true;
		}
	}
}
