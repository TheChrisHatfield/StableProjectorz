using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace spz {

	/// <summary>Facade for viewport paint undo: stroke-boundary snapshots, CPU Deflate storage, amortized restore.</summary>
	public class PaintUndo_MGR : MonoBehaviour {

		public static PaintUndo_MGR instance { get; private set; }

		[SerializeField] bool _logVerbose = false;

		readonly PaintUndo_Storage _storage = new PaintUndo_Storage();
		readonly PaintUndo_Scheduler _scheduler = new PaintUndo_Scheduler();
		readonly Queue<PendingCaptureJob> _captureQueue = new Queue<PendingCaptureJob>();

		/// <summary>GPU copy + async readback staging for capture <b>and</b> redo snapshot inside restore — mutually exclusive users (see <see cref="TryStartCaptureProcessorIfNeeded"/>).</summary>
		RenderUdims _scratchPreStroke;
		Coroutine _captureCrt;
		Texture2D _uploadStaging;

		RestoreSession _restoreSession;
		bool _isRestoring;
		bool _stackHooks;
		/// <summary>Ctrl+Z/Y while capture readback/deflate is running would be ignored (<see cref="IsBusy"/>); queue and run when idle so undo stays reliable project-wide.</summary>
		int _deferredUndoCount;
		int _deferredRedoCount;
		const int MaxDeferredUndoRedo = 16;

		struct PendingCaptureJob {
			public RenderUdims Target;
			public int ActiveLayerIndex;
			public int LayerCount;
			public bool ClearRedoAfterPush;
		}

		class RestoreSession {
			public RenderUdims Target;
			public List<byte[]> SliceData;
			public int[] Order;
			public int OrderCursor;
		}

		void Awake() {
			if (instance != null && instance != this) {
				Destroy(gameObject);
				return;
			}
			instance = this;
			DontDestroyOnLoad(gameObject);
			RefreshSettingsDepth();
		}

		void OnDestroy() {
			if (instance == this) instance = null;
			if (PaintLayerStack_MGR.instance != null && _stackHooks)
				PaintLayerStack_MGR.instance.OnLayerStackStructureChanged -= OnLayerStackStructureChanged_ClearHistory;
			_scratchPreStroke?.Dispose();
			if (_uploadStaging != null) Destroy(_uploadStaging);
		}

		void Start() => TryHookLayerStack();

		void Update() => TryHookLayerStack();

		void TryHookLayerStack() {
			if (_stackHooks) return;
			var s = PaintLayerStack_MGR.instance;
			if (s == null) return;
			s.OnLayerStackStructureChanged += OnLayerStackStructureChanged_ClearHistory;
			_stackHooks = true;
		}

		void OnLayerStackStructureChanged_ClearHistory() {
			_storage.ClearAll();
			_captureQueue.Clear();
			_restoreSession = null;
			_isRestoring = false;
			_deferredUndoCount = 0;
			_deferredRedoCount = 0;
			if (_logVerbose) Debug.Log("[PaintUndo] Cleared undo/redo (layer stack structure changed).");
		}

		public static void EnsureExists() {
			if (instance != null) return;
			var go = new GameObject("PaintUndo_MGR");
			go.AddComponent<PaintUndo_MGR>();
			go.AddComponent<PaintUndo_Input>();
			DontDestroyOnLoad(go);
		}

		void RefreshSettingsDepth() {
			int d = Settings_MGR.instance != null ? Settings_MGR.instance.get_paintUndo_maxDepth() : 8;
			_storage.SetMaxDepth(d);
		}

		/// <summary>Call when user changes max depth in Settings so stacks trim immediately.</summary>
		public void ApplyMaxDepthFromSettings() {
			RefreshSettingsDepth();
		}

		public bool BlocksNewStroke => _isRestoring;

		public bool IsBusy => _captureCrt != null || _isRestoring;

		/// <summary>Paint undo hook — see docs/UNDO_INTEGRATION.md. Call before Apply_into_ColorBrushTex with the same target.</summary>
		public void SchedulePreStrokeCapture(RenderUdims paintTarget) {
			if (!IsUndoEnabled()) return;
			TryHookLayerStack();
			if (paintTarget == null || paintTarget.texArray == null) return;
			RefreshSettingsDepth();
			var stack = PaintLayerStack_MGR.instance;
			int lc = stack?.Layers != null ? stack.Layers.Count : 0;
			int aix = stack != null ? stack.ActiveLayerIndex : 0;
			// Bind snapshot to the buffer actually painted, not "active layer index" alone (fallback buffer vs Content).
			if (stack != null) {
				int ix = stack.IndexOfContent(paintTarget);
				if (ix >= 0)
					aix = ix;
				else {
					lc = 0;
					aix = 0;
				}
			} else
				lc = 0;
			_captureQueue.Enqueue(new PendingCaptureJob {
				Target = paintTarget,
				ActiveLayerIndex = aix,
				LayerCount = lc,
				ClearRedoAfterPush = true
			});
			TryStartCaptureProcessorIfNeeded();
		}

		/// <summary>Start capture coroutine only when restore is not using <see cref="_scratchPreStroke"/> (redo readback path).</summary>
		void TryStartCaptureProcessorIfNeeded() {
			if (_captureCrt != null || _isRestoring || _captureQueue.Count == 0)
				return;
			_captureCrt = StartCoroutine(CaptureProcessorCoroutine());
		}

		bool IsUndoEnabled() {
			return Settings_MGR.instance != null && Settings_MGR.instance.get_paintUndo_enabled();
		}

		IEnumerator CaptureProcessorCoroutine() {
			while (_captureQueue.Count > 0) {
				var job = _captureQueue.Dequeue();
				if (job.Target == null || job.Target.texArray == null) continue;
				EnsureScratchMatches(job.Target);
				Graphics.CopyTexture(job.Target.texArray, _scratchPreStroke.texArray);
				PaintUndo_Scheduler.EvaluateWorkload(job.Target.width, job.Target.height, job.Target.UdimsCount,
					_scheduler.referencePixelsPerSlice, out _, out var captureComplexity01, out _);
				int readbackInflight = PaintUndo_Scheduler.GetCaptureGpuReadbackMaxInflight(captureComplexity01, job.Target.UdimsCount);
				if (_logVerbose)
					Debug.Log($"[PaintUndo] Capture readback: complexity01={captureComplexity01:F2}, maxInflight={readbackInflight} (0=all parallel)");
				bool done = false;
				List<Texture2D> slices = null;
				if (readbackInflight <= 0 || readbackInflight >= job.Target.UdimsCount)
					TextureTools_SPZ.RenderTexture_to_Texture2DList_Async(_scratchPreStroke, list => {
						slices = list;
						done = true;
					});
				else
					TextureTools_SPZ.RenderTexture_to_Texture2DList_Async_Staggered(_scratchPreStroke, readbackInflight, list => {
						slices = list;
						done = true;
					});
				while (!done) yield return null;
				int postRbYields = PaintUndo_Scheduler.GetCapturePostReadbackYieldFrames(captureComplexity01);
				for (int y = 0; y < postRbYields; y++)
					yield return null;
				if (slices == null || slices.Count == 0) {
					if (_logVerbose) Debug.LogWarning("[PaintUndo] Capture: no slices from readback.");
					continue;
				}
				bool anyNull = false;
				for (int i = 0; i < slices.Count; i++)
					if (slices[i] == null) { anyNull = true; break; }
				if (anyNull) {
					foreach (var t in slices)
						if (t != null) Destroy(t);
					Debug.LogWarning("[PaintUndo] Capture: readback error on slice(s).");
					continue;
				}
				if (!PaintUndo_SnapshotRecord.TryBuildUncompressedBlob(slices, job.ActiveLayerIndex, job.LayerCount, out var record, out var uncompressed)) {
					foreach (var t in slices)
						if (t != null) Destroy(t);
					continue;
				}
				foreach (var t in slices)
					Destroy(t);
				byte[] rawForTask = uncompressed;
				var deflateTask = Task.Run(() => {
					try { return PaintUndo_Compress.Deflate(rawForTask); }
					catch (Exception e) {
						Debug.LogError("[PaintUndo] Capture Deflate failed: " + e.Message);
						return null;
					}
				});
				while (!deflateTask.IsCompleted) yield return null;
				record.CompressedBytes = deflateTask.Result;
				if (record.CompressedBytes == null) continue;
				if (job.ClearRedoAfterPush)
					_storage.ClearRedo();
				_storage.PushUndo(record);
				if (_logVerbose) Debug.Log($"[PaintUndo] PushUndo depth={_storage.UndoCount} bytes={record.CompressedBytes?.Length ?? 0}");
			}
			_captureCrt = null;
			ProcessDeferredUndoRedo();
			TryStartCaptureProcessorIfNeeded();
		}

		/// <summary>After capture or restore finishes, run deferred Ctrl+Z / Ctrl+Y that arrived while <see cref="IsBusy"/>.</summary>
		void ProcessDeferredUndoRedo() {
			if (!IsUndoEnabled()) {
				_deferredUndoCount = 0;
				_deferredRedoCount = 0;
				return;
			}
			if (_isRestoring || _captureCrt != null)
				return;
			while (_deferredUndoCount > 0) {
				var snapUndo = _storage.PopUndo();
				if (snapUndo == null) {
					_deferredUndoCount = 0;
					break;
				}
				_deferredUndoCount--;
				StartDeferredRestoreCoroutine(snapUndo, pushCurrentToRedo: true);
				return;
			}
			while (_deferredRedoCount > 0) {
				var snapRedo = _storage.PopRedo();
				if (snapRedo == null) {
					_deferredRedoCount = 0;
					break;
				}
				_deferredRedoCount--;
				StartDeferredRestoreCoroutine(snapRedo, pushCurrentToRedo: false);
				return;
			}
		}

		/// <summary>Sets <see cref="_isRestoring"/> then schedules <see cref="UndoOrRedoCoroutine"/> — use for every restore start (TryUndo/TryRedo and <see cref="ProcessDeferredUndoRedo"/>) so scratch is not re-entered before the enumerator runs.</summary>
		void StartDeferredRestoreCoroutine(PaintUndo_SnapshotRecord snap, bool pushCurrentToRedo) {
			_isRestoring = true;
			StartCoroutine(UndoOrRedoCoroutine(snap, pushCurrentToRedo));
		}

		void EnsureScratchMatches(RenderUdims target) {
			if (target == null) return;
			if (_scratchPreStroke != null
			    && _scratchPreStroke.width == target.width
			    && _scratchPreStroke.height == target.height
			    && _scratchPreStroke.UdimsCount == target.UdimsCount
			    && _scratchPreStroke.graphicsFormat == target.graphicsFormat)
				return;
			_scratchPreStroke?.Dispose();
			_scratchPreStroke = new RenderUdims(target.udims_sectors, target.widthHeight, target.graphicsFormat, target.filterMode, Color.clear, 0);
		}

		void EnsureUploadStaging(int w, int h, GraphicsFormat fmt) {
			if (_uploadStaging != null && _uploadStaging.width == w && _uploadStaging.height == h && _uploadStaging.graphicsFormat == fmt)
				return;
			if (_uploadStaging != null) Destroy(_uploadStaging);
			_uploadStaging = new Texture2D(w, h, fmt, TextureCreationFlags.None);
		}

		public void TryUndo() {
			if (!IsUndoEnabled()) return;
			if (_isRestoring || _captureCrt != null) {
				_deferredUndoCount = Mathf.Min(_deferredUndoCount + 1, MaxDeferredUndoRedo);
				if (_logVerbose) Debug.Log($"[PaintUndo] Undo deferred (capture/restore busy); queue={_deferredUndoCount}");
				return;
			}
			var snap = _storage.PopUndo();
			if (snap == null) return;
			StartDeferredRestoreCoroutine(snap, pushCurrentToRedo: true);
		}

		public void TryRedo() {
			if (!IsUndoEnabled()) return;
			if (_isRestoring || _captureCrt != null) {
				_deferredRedoCount = Mathf.Min(_deferredRedoCount + 1, MaxDeferredUndoRedo);
				if (_logVerbose) Debug.Log($"[PaintUndo] Redo deferred (capture/restore busy); queue={_deferredRedoCount}");
				return;
			}
			var snap = _storage.PopRedo();
			if (snap == null) return;
			StartDeferredRestoreCoroutine(snap, pushCurrentToRedo: false);
		}

		IEnumerator UndoOrRedoCoroutine(PaintUndo_SnapshotRecord snap, bool pushCurrentToRedo) {
			// _isRestoring is set by <see cref="StartDeferredRestoreCoroutine"/> before this enumerator is scheduled.
			var stack = PaintLayerStack_MGR.instance;
			RenderUdims target = null;
			if (snap.TryGetRestoreTarget(stack, out target)) {
				// Restore into the layer Content this stroke was captured from (index + count match current stack).
			} else if (snap.LayerCount <= 0) {
				// Standalone UV color buffer — not GetPaintTarget_Undo() (that follows *current* active layer).
				var inpaint = Inpaint_MaskPainter.instance;
				target = inpaint != null ? inpaint._ObjectUV_brushedColorRGBA : null;
				if (target == null || !snap.MatchesNonStackTarget(target))
					target = null;
			} else {
				target = null;
			}
			if (target == null) {
				Debug.LogWarning("[PaintUndo] Cannot resolve restore target (stack/layer mismatch); skipping restore.");
				if (pushCurrentToRedo) _storage.PushUndo(snap);
				else _storage.PushRedo(snap);
				_isRestoring = false;
				ProcessDeferredUndoRedo();
				TryStartCaptureProcessorIfNeeded();
				yield break;
			}
			EnsureScratchMatches(target);
			Graphics.CopyTexture(target.texArray, _scratchPreStroke.texArray);
			bool got = false;
			List<Texture2D> cur = null;
			TextureTools_SPZ.RenderTexture_to_Texture2DList_Async(_scratchPreStroke, list => { cur = list; got = true; });
			while (!got) yield return null;
			PaintUndo_SnapshotRecord currentRecord = null;
			if (cur != null && cur.Count > 0 && !HasNullSlice(cur)) {
				int lc = stack?.Layers != null ? stack.Layers.Count : 0;
				int aix = stack != null ? stack.IndexOfContent(target) : -1;
				if (aix < 0) {
					aix = 0;
					lc = 0;
				}
				if (PaintUndo_SnapshotRecord.TryBuildUncompressedBlob(cur, aix, lc, out currentRecord, out var curRaw)) {
					foreach (var t in cur)
						if (t != null) Destroy(t);
					cur = null;
					var deflateRedo = Task.Run(() => {
						try { return PaintUndo_Compress.Deflate(curRaw); }
						catch (Exception e) {
							Debug.LogError("[PaintUndo] Redo/undo snapshot Deflate failed: " + e.Message);
							return null;
						}
					});
					while (!deflateRedo.IsCompleted) yield return null;
					currentRecord.CompressedBytes = deflateRedo.Result;
					if (currentRecord.CompressedBytes == null) currentRecord = null;
				}
			}
			if (cur != null)
				foreach (var t in cur)
					if (t != null) Destroy(t);
			if (currentRecord != null) {
				if (pushCurrentToRedo) _storage.PushRedo(currentRecord);
				else _storage.PushUndo(currentRecord);
			}
			if (!snap.TryUnpackSlices(out var sliceData, out var err)) {
				Debug.LogWarning("[PaintUndo] Unpack failed: " + err);
				if (pushCurrentToRedo) _storage.PushUndo(snap);
				else _storage.PushRedo(snap);
				_isRestoring = false;
				ProcessDeferredUndoRedo();
				TryStartCaptureProcessorIfNeeded();
				yield break;
			}
			_scheduler.BeginRestoreSession(target.width, target.height, sliceData.Count);
			if (_logVerbose)
				Debug.Log($"[PaintUndo] Restore session: {target.width}x{target.height} × {sliceData.Count} UDIMs, complexity01={_scheduler.LastSessionComplexity01:F2}, totalPx={_scheduler.LastSessionTotalPixels}");
			_restoreSession = new RestoreSession {
				Target = target,
				SliceData = sliceData,
				Order = PaintUndo_Scheduler.LinearSliceUploadOrder(sliceData.Count),
				OrderCursor = 0
			};
			while (_restoreSession != null && _restoreSession.OrderCursor < _restoreSession.Order.Length)
				yield return null;
			_restoreSession = null;
			FinishRestore(target);
			_isRestoring = false;
			if (_logVerbose) Debug.Log("[PaintUndo] Restore complete.");
			ProcessDeferredUndoRedo();
			TryStartCaptureProcessorIfNeeded();
		}

		static bool HasNullSlice(List<Texture2D> list) {
			for (int i = 0; i < list.Count; i++)
				if (list[i] == null) return true;
			return false;
		}

		void LateUpdate() {
			if (_restoreSession == null) return;
			float dt = Time.deltaTime;
			_scheduler.BeginRestoreTick(dt);
			var session = _restoreSession;
			int remaining = session.Order.Length - session.OrderCursor;
			if (remaining > 0) {
				_scheduler.GetFrameBudget(remaining, out float budgetMs, out int maxSlices);
				float start = Time.realtimeSinceStartup;
				int uploaded = 0;
				while (uploaded < maxSlices && session.OrderCursor < session.Order.Length) {
					if ((Time.realtimeSinceStartup - start) * 1000f >= budgetMs) break;
					int sliceIx = session.Order[session.OrderCursor];
					byte[] raw = session.SliceData[sliceIx];
					EnsureUploadStaging(session.Target.width, session.Target.height, session.Target.graphicsFormat);
					_uploadStaging.LoadRawTextureData(raw);
					_uploadStaging.Apply(false, false);
					Graphics.CopyTexture(_uploadStaging, 0, 0, session.Target.texArray, sliceIx, 0);
					session.OrderCursor++;
					uploaded++;
				}
				float hitchMs = Mathf.Max(0f, (Time.deltaTime - (1f / 60f)) * 1000f);
				float reward = -hitchMs * 0.01f + uploaded * 0.15f;
				_scheduler.RegisterUcbReward(reward);
			}
			if (_restoreSession != null && _restoreSession.OrderCursor >= _restoreSession.Order.Length)
				_restoreSession = null;
		}

		void FinishRestore(RenderUdims target) {
			var stack = PaintLayerStack_MGR.instance;
			if (stack?.Layers != null) {
				for (int i = 0; i < stack.Layers.Count; i++) {
					var layer = stack.Layers[i];
					if (layer != null && layer.Content == target)
						layer.SyncDataFromContent();
				}
			}
			if (Objects_Renderer_MGR.instance != null) {
				Objects_Renderer_MGR.instance.ReRenderAll_soon();
				Objects_Renderer_MGR.instance.EnsureInpaintColorLayerAppliedForCapture();
			}
		}
	}
}
