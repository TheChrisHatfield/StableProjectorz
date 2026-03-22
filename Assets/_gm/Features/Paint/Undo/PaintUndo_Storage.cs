using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>Undo + redo stacks with max depth (oldest dropped).</summary>
	public class PaintUndo_Storage {

		readonly List<PaintUndo_SnapshotRecord> _undo = new List<PaintUndo_SnapshotRecord>();
		readonly List<PaintUndo_SnapshotRecord> _redo = new List<PaintUndo_SnapshotRecord>();
		int _maxDepth = 8;

		public int UndoCount => _undo.Count;
		public int RedoCount => _redo.Count;

		public void SetMaxDepth(int depth) {
			_maxDepth = Mathf.Max(1, depth);
			TrimUndoToMax();
			while (_redo.Count > _maxDepth)
				_redo.RemoveAt(0);
		}

		public int MaxDepth => _maxDepth;

		void TrimUndoToMax() {
			while (_undo.Count > _maxDepth)
				_undo.RemoveAt(0);
		}

		public void ClearRedo() => _redo.Clear();

		public void ClearAll() {
			_undo.Clear();
			_redo.Clear();
		}

		public void PushUndo(PaintUndo_SnapshotRecord snap) {
			if (snap == null) return;
			_undo.Add(snap);
			TrimUndoToMax();
		}

		public void PushRedo(PaintUndo_SnapshotRecord snap) {
			if (snap == null) return;
			_redo.Add(snap);
			while (_redo.Count > _maxDepth)
				_redo.RemoveAt(0);
		}

		public PaintUndo_SnapshotRecord PopUndo() {
			if (_undo.Count == 0) return null;
			int i = _undo.Count - 1;
			var s = _undo[i];
			_undo.RemoveAt(i);
			return s;
		}

		public PaintUndo_SnapshotRecord PeekUndo() {
			if (_undo.Count == 0) return null;
			return _undo[_undo.Count - 1];
		}

		public PaintUndo_SnapshotRecord PopRedo() {
			if (_redo.Count == 0) return null;
			int i = _redo.Count - 1;
			var s = _redo[i];
			_redo.RemoveAt(i);
			return s;
		}
	}
}
