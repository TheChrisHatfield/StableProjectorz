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
		[SerializeField] RestoreBudgetPolicy _restoreBudgetPolicy = RestoreBudgetPolicy.Thompson;
		[SerializeField] bool _captureBanditEnabled = true;
		[SerializeField] int _captureBanditMinPullsPerBucket = 3;
		[SerializeField] bool _smudgeRouteBanditEnabled = true;
		[SerializeField] int _smudgeRouteMinPullsPerBucket = 3;
		[SerializeField] bool _collapsePathBanditEnabled = true;
		[SerializeField] int _collapsePathMinPullsPerBucket = 3;
		[SerializeField] bool _capturePingPongScratches = true;
		[SerializeField] bool _captureEagerGpuCopy = false;

		readonly PaintUndo_Storage _storage = new PaintUndo_Storage();
		readonly PaintUndo_Scheduler _scheduler = new PaintUndo_Scheduler();
		/// <summary>Slice-batch Thompson/UCB for layer collapse composites; posteriors persist across collapses (unlike per-undo-restore decay).</summary>
		readonly PaintUndo_Scheduler _collapseSliceScheduler = new PaintUndo_Scheduler();

		/// <summary>Live undo scheduler (restore + capture bandits). Used by smudge spacing to share capture Thompson context without extra observations.</summary>
		public PaintUndo_Scheduler UndoScheduler => _scheduler;
		readonly Queue<PendingCaptureJob> _captureQueue = new Queue<PendingCaptureJob>();

		/// <summary>GPU copy + async readback for redo snapshot inside restore only.</summary>
		RenderUdims _scratchRestore;
		/// <summary>Two capture staging buffers so restore never shares scratch with capture (ping-pong optional).</summary>
		readonly RenderUdims[] _scratchCapture = new RenderUdims[2];
		int _captureWriteSeq;
		bool _eagerHeadCopyValid;
		int _eagerHeadScratchIx;
		RenderUdims _eagerHeadTarget;

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
			public int NonStackTargetKind;
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
			PushSchedulerInspectorSettings();
		}

		void PushSchedulerInspectorSettings() {
			_scheduler.restoreBudgetPolicy = _restoreBudgetPolicy;
			_scheduler.captureBanditEnabled = _captureBanditEnabled;
			_scheduler.captureBanditMinPullsPerBucket = Mathf.Max(0, _captureBanditMinPullsPerBucket);
			_scheduler.smudgeRouteBanditEnabled = _smudgeRouteBanditEnabled;
			_scheduler.smudgeRouteMinPullsPerBucket = Mathf.Max(0, _smudgeRouteMinPullsPerBucket);
			_scheduler.collapsePathBanditEnabled = _collapsePathBanditEnabled;
			_scheduler.collapsePathMinPullsPerBucket = Mathf.Max(0, _collapsePathMinPullsPerBucket);
			// Must not call CopyRestoreSchedulerPolicyTo here — that method calls Push again and would recurse until stack overflow (player builds crash immediately).
			PropagateSchedulerPolicyFromMainTo(_collapseSliceScheduler);
		}

		/// <summary>Copies scalar policy from the live main <see cref="_scheduler"/> to another scheduler (learned bandit posteriors on <paramref name="target"/> are left unchanged).</summary>
		void PropagateSchedulerPolicyFromMainTo(PaintUndo_Scheduler target) {
			if (target == null) return;
			var src = _scheduler;
			target.restoreBudgetPolicy = src.restoreBudgetPolicy;
			target.baseBudgetMs = src.baseBudgetMs;
			target.minBudgetMs = src.minBudgetMs;
			target.maxBudgetMs = src.maxBudgetMs;
			target.minSlicesPerFrame = src.minSlicesPerFrame;
			target.maxSlicesPerFrame = src.maxSlicesPerFrame;
			target.agingBoostPerSecond = src.agingBoostPerSecond;
			target.agingMaxMultiplier = src.agingMaxMultiplier;
			target.restoreThompsonSuccessHitchMs = src.restoreThompsonSuccessHitchMs;
			target.restorePosteriorDecayPerSession = src.restorePosteriorDecayPerSession;
			target.restoreContextBucketCount = src.restoreContextBucketCount;
			target.referencePixelsPerSlice = src.referencePixelsPerSlice;
			target.smudgeRouteBanditEnabled = src.smudgeRouteBanditEnabled;
			target.smudgeRouteMinPullsPerBucket = src.smudgeRouteMinPullsPerBucket;
			target.smudgeRouteOpacityPriorLow = src.smudgeRouteOpacityPriorLow;
			target.smudgeRouteOpacityPriorHigh = src.smudgeRouteOpacityPriorHigh;
			target.smudgeRouteSuccessMaxFrameTimeSec = src.smudgeRouteSuccessMaxFrameTimeSec;
			target.collapsePathBanditEnabled = src.collapsePathBanditEnabled;
			target.collapsePathMinPullsPerBucket = src.collapsePathMinPullsPerBucket;
		}

		public void CopyRestoreSchedulerPolicyTo(PaintUndo_Scheduler target) {
			if (target == null) return;
			PushSchedulerInspectorSettings();
			PropagateSchedulerPolicyFromMainTo(target);
		}

		/// <summary>Scheduler used for amortized collapse slice compositing; shares policy with undo restore and retains learned slice-batch arms across collapses.</summary>
		public static PaintUndo_Scheduler GetCollapseSliceScheduler() {
			if (instance == null) return null;
			instance.CopyRestoreSchedulerPolicyTo(instance._collapseSliceScheduler);
			return instance._collapseSliceScheduler;
		}

		void OnDestroy() {
			if (instance == this) instance = null;
			if (PaintLayerStack_MGR.instance != null && _stackHooks)
				PaintLayerStack_MGR.instance.OnLayerStackStructureChanged -= OnLayerStackStructureChanged_ClearHistory;
			_scratchRestore?.Dispose();
			for (int i = 0; i < _scratchCapture.Length; i++)
				_scratchCapture[i]?.Dispose();
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
			_eagerHeadCopyValid = false;
			_eagerHeadTarget = null;
			// Layer Content/NoColorMask may already be Dispose()'d. An in-flight capture can CopyTexture /
			// AsyncGPUReadback a dead RT and never set done=true, leaving IsBusy stuck (fill/clear wait forever).
			if (_captureCrt != null) {
				StopCoroutine(_captureCrt);
				_captureCrt = null;
			}
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

		/// <summary>Paint undo hook — see docs/UNDO_INTEGRATION.md. Call before applying a stroke to <paramref name="paintTarget"/> (inpaint color buffer or layer Content).</summary>
		public void SchedulePreStrokeCapture(RenderUdims paintTarget) {
			SchedulePreStrokeCapture(paintTarget, PaintUndoNonStackTarget.InpaintColor, 0);
		}

		/// <summary>Like <see cref="SchedulePreStrokeCapture(RenderUdims)"/> but sets non-layer restore tag when the target is not layer <c>Content</c> (background mask, projection mask, etc.).</summary>
		public void SchedulePreStrokeCapture(RenderUdims paintTarget, PaintUndoNonStackTarget nonStackKind) {
			SchedulePreStrokeCapture(paintTarget, nonStackKind, 0, false);
		}

		/// <param name="projectionMaskPovIndex">When <paramref name="nonStackKind"/> is <see cref="PaintUndoNonStackTarget.ProjectionGenMask"/>, stored in <see cref="PaintUndo_SnapshotRecord.ActiveLayerIndex"/> (layer count 0) so restore picks the correct POV slot. Ignored for other kinds.</param>
		/// <param name="immediateGpuCopyBeforeMutation">When true, copies the head queued job to capture scratch in this call (if the queue is exactly one job and no capture coroutine is running). Use when the paint target is mutated in the same frame after scheduling (e.g. smudge compute), so the async capture coroutine cannot run before that GPU write.</param>
		public void SchedulePreStrokeCapture(RenderUdims paintTarget, PaintUndoNonStackTarget nonStackKind, int projectionMaskPovIndex, bool immediateGpuCopyBeforeMutation = false) {
			if (!IsUndoEnabled()) return;
			TryHookLayerStack();
			if (paintTarget == null || paintTarget.texArray == null) return;
			RefreshSettingsDepth();
			PushSchedulerInspectorSettings();
			var stack = PaintLayerStack_MGR.instance;
			int lc = stack?.Layers != null ? stack.Layers.Count : 0;
			int aix = stack != null ? stack.ActiveLayerIndex : 0;
			int nstk = 0;
			// Bind snapshot to the buffer actually painted, not "active layer index" alone (fallback buffer vs Content).
			if (stack != null) {
				int ix = stack.IndexOfContent(paintTarget);
				if (ix >= 0)
					aix = ix;
				else {
					int nix = stack.IndexOfNoColorMask(paintTarget);
					if (nix >= 0) {
						// Layer No Color buffer: keep stack binding + tag so restore hits NoColorMask, not Content.
						aix = nix;
						nstk = (int)PaintUndoNonStackTarget.InpaintNoColorMask;
					} else {
						lc = 0;
						nstk = (int)nonStackKind;
						aix = nonStackKind == PaintUndoNonStackTarget.ProjectionGenMask ? Mathf.Max(0, projectionMaskPovIndex) : 0;
					}
				}
			} else {
				lc = 0;
				nstk = (int)nonStackKind;
				aix = nonStackKind == PaintUndoNonStackTarget.ProjectionGenMask ? Mathf.Max(0, projectionMaskPovIndex) : 0;
			}
			_captureQueue.Enqueue(new PendingCaptureJob {
				Target = paintTarget,
				ActiveLayerIndex = aix,
				LayerCount = lc,
				NonStackTargetKind = nstk,
				ClearRedoAfterPush = true
			});
			if (immediateGpuCopyBeforeMutation)
				TryImmediateHeadGpuCopyIfSingle(requireInspectorEagerFlag: false);
			else
				TryEagerGpuCopyHeadIfSingle();
			TryStartCaptureProcessorIfNeeded();
		}

		/// <summary>Optional: copy head job to capture scratch immediately when only one job is queued (overlap with frame work). Coroutine skips duplicate CopyTexture when target matches.</summary>
		void TryEagerGpuCopyHeadIfSingle() {
			TryImmediateHeadGpuCopyIfSingle(requireInspectorEagerFlag: true);
		}

		/// <summary>When <paramref name="requireInspectorEagerFlag"/> is true, only runs if <see cref="_captureEagerGpuCopy"/> is enabled. Otherwise always attempts copy (for smudge / same-frame mutation after schedule).</summary>
		void TryImmediateHeadGpuCopyIfSingle(bool requireInspectorEagerFlag) {
			if (requireInspectorEagerFlag && !_captureEagerGpuCopy) return;
			if (_isRestoring || _captureCrt != null || _captureQueue.Count != 1)
				return;
			PendingCaptureJob head;
			try { head = _captureQueue.Peek(); }
			catch { return; }
			if (head.Target == null || head.Target.texArray == null) return;
			int ix = _capturePingPongScratches ? (_captureWriteSeq & 1) : 0;
			EnsureCaptureScratchSlot(ix, head.Target);
			Graphics.CopyTexture(head.Target.texArray, _scratchCapture[ix].texArray);
			_eagerHeadCopyValid = true;
			_eagerHeadScratchIx = ix;
			_eagerHeadTarget = head.Target;
			if (_logVerbose) Debug.Log($"[PaintUndo] Eager GPU copy → capture scratch[{ix}]");
		}

		/// <summary>Start capture coroutine only when restore is not using <see cref="_scratchRestore"/> (redo readback path).</summary>
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
				PaintUndo_Scheduler.EvaluateWorkload(job.Target.width, job.Target.height, job.Target.UdimsCount,
					_scheduler.referencePixelsPerSlice, out _, out var captureComplexity01, out _);
				int arm = _scheduler.SelectCaptureArm(captureComplexity01, job.Target.UdimsCount, out int readbackInflight, out int postRbYields);
				RenderUdims capScratch;
				bool usedEager = _eagerHeadCopyValid && ReferenceEquals(job.Target, _eagerHeadTarget) && _eagerHeadScratchIx >= 0
				                 && _scratchCapture[_eagerHeadScratchIx] != null;
				if (usedEager) {
					capScratch = _scratchCapture[_eagerHeadScratchIx];
					_eagerHeadCopyValid = false;
					_eagerHeadTarget = null;
					_captureWriteSeq = (_eagerHeadScratchIx + 1) & 1;
					if (_logVerbose) Debug.Log("[PaintUndo] Capture: using eager GPU copy (skip duplicate CopyTexture).");
				} else {
					_eagerHeadCopyValid = false;
					_eagerHeadTarget = null;
					int writeIx = _capturePingPongScratches ? (_captureWriteSeq++ & 1) : 0;
					EnsureCaptureScratchSlot(writeIx, job.Target);
					capScratch = _scratchCapture[writeIx];
					Graphics.CopyTexture(job.Target.texArray, capScratch.texArray);
				}
				if (_logVerbose)
					Debug.Log($"[PaintUndo] Capture readback: complexity01={captureComplexity01:F2}, arm={arm}, maxInflight={readbackInflight} (0=all parallel), postYields={postRbYields}");
				bool done = false;
				List<Texture2D> slices = null;
				if (readbackInflight <= 0 || readbackInflight >= job.Target.UdimsCount)
					TextureTools_SPZ.RenderTexture_to_Texture2DList_Async(capScratch, list => {
						slices = list;
						done = true;
					});
				else
					TextureTools_SPZ.RenderTexture_to_Texture2DList_Async_Staggered(capScratch, readbackInflight, list => {
						slices = list;
						done = true;
					});
				while (!done) yield return null;
				for (int y = 0; y < postRbYields; y++)
					yield return null;
				float maxHitchMs = 0f;
				int obsFrames = Mathf.Max(0, _scheduler.captureObserveFrames);
				for (int f = 0; f < obsFrames; f++) {
					float hitch = Mathf.Max(0f, (Time.deltaTime - (1f / 60f)) * 1000f);
					if (hitch > maxHitchMs) maxHitchMs = hitch;
					yield return null;
				}
				bool captureOk = slices != null && slices.Count > 0;
				if (captureOk) {
					for (int i = 0; i < slices.Count; i++)
						if (slices[i] == null) { captureOk = false; break; }
				}
				bool hitchOk = maxHitchMs < _scheduler.captureSuccessMaxHitchMs;
				_scheduler.RegisterCaptureBanditObservation(captureOk && hitchOk);
				if (_logVerbose && arm >= 0)
					Debug.Log($"[PaintUndo] Capture bandit obs: arm={arm}, maxHitchMs={maxHitchMs:F1}, ok={captureOk && hitchOk}");
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
				if (!PaintUndo_SnapshotRecord.TryBuildUncompressedBlob(slices, job.ActiveLayerIndex, job.LayerCount, job.NonStackTargetKind, out var record, out var uncompressed)) {
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

		void EnsureRestoreScratchMatches(RenderUdims target) {
			if (target == null) return;
			if (_scratchRestore != null
			    && _scratchRestore.width == target.width
			    && _scratchRestore.height == target.height
			    && _scratchRestore.UdimsCount == target.UdimsCount
			    && _scratchRestore.graphicsFormat == target.graphicsFormat)
				return;
			_scratchRestore?.Dispose();
			_scratchRestore = new RenderUdims(target.udims_sectors, target.widthHeight, target.graphicsFormat, target.filterMode, Color.clear, 0);
		}

		void EnsureCaptureScratchSlot(int slot, RenderUdims target) {
			if (target == null) return;
			slot &= 1;
			var s = _scratchCapture[slot];
			if (s != null
			    && s.width == target.width
			    && s.height == target.height
			    && s.UdimsCount == target.UdimsCount
			    && s.graphicsFormat == target.graphicsFormat)
				return;
			_scratchCapture[slot]?.Dispose();
			_scratchCapture[slot] = new RenderUdims(target.udims_sectors, target.widthHeight, target.graphicsFormat, target.filterMode, Color.clear, 0);
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
				if (!TryResolveNonStackRestoreTarget(snap, out target))
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
			EnsureRestoreScratchMatches(target);
			Graphics.CopyTexture(target.texArray, _scratchRestore.texArray);
			bool got = false;
			List<Texture2D> cur = null;
			TextureTools_SPZ.RenderTexture_to_Texture2DList_Async(_scratchRestore, list => { cur = list; got = true; });
			while (!got) yield return null;
			PaintUndo_SnapshotRecord currentRecord = null;
			if (cur != null && cur.Count > 0 && !HasNullSlice(cur)) {
				int lc = stack?.Layers != null ? stack.Layers.Count : 0;
				int aix = stack != null ? stack.IndexOfContent(target) : -1;
				int redoNstk = 0;
				if (aix < 0 && stack != null) {
					aix = stack.IndexOfNoColorMask(target);
					if (aix >= 0)
						redoNstk = (int)PaintUndoNonStackTarget.InpaintNoColorMask;
				}
				if (aix < 0) {
					lc = 0;
					aix = snap.NonStackTargetKind == (int)PaintUndoNonStackTarget.ProjectionGenMask
						? snap.ActiveLayerIndex
						: 0;
					redoNstk = snap.NonStackTargetKind;
				}
				if (PaintUndo_SnapshotRecord.TryBuildUncompressedBlob(cur, aix, lc, redoNstk, out currentRecord, out var curRaw)) {
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
				Debug.Log($"[PaintUndo] Restore session: {target.width}x{target.height} × {sliceData.Count} UDIMs, complexity01={_scheduler.LastSessionComplexity01:F2}, totalPx={_scheduler.LastSessionTotalPixels}, policy={_scheduler.restoreBudgetPolicy}");
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

		/// <summary>Resolve GPU buffer for snapshots with no layer stack binding (<see cref="PaintUndo_SnapshotRecord.LayerCount"/> ≤ 0).</summary>
		static bool TryResolveNonStackRestoreTarget(PaintUndo_SnapshotRecord snap, out RenderUdims target) {
			target = null;
			if (snap.LayerCount > 0) return false;
			switch ((PaintUndoNonStackTarget)snap.NonStackTargetKind) {
				case PaintUndoNonStackTarget.InpaintColor: {
					var inp = Inpaint_MaskPainter.instance;
					target = inp != null ? inp._ObjectUV_brushedColorRGBA : null;
					break;
				}
				case PaintUndoNonStackTarget.InpaintNoColorMask: {
					// Fallback when snapshot lost layer binding: restore into whatever No Color currently paints.
					var inp = Inpaint_MaskPainter.instance;
					target = inp != null ? inp.GetPaintTarget_Undo() : null;
					break;
				}
				case PaintUndoNonStackTarget.BackgroundGenMask:
					target = Background_Painter.instance != null ? Background_Painter.instance.current_BG_MaskRenderUdim() : null;
					break;
				case PaintUndoNonStackTarget.ProjectionGenMask: {
					var art = Art2D_IconsUI_List.instance;
					var icon = art != null ? art._mainSelectedIcon : null;
					var gen = icon != null ? icon._genData : null;
					var mu = gen != null ? gen._masking_utils : null;
					if (mu?._ObjectUV_brushedMaskR8 != null) {
						int ix = snap.ActiveLayerIndex;
						if (ix >= 0 && ix < mu._ObjectUV_brushedMaskR8.Count)
							target = mu._ObjectUV_brushedMaskR8[ix];
					}
					break;
				}
				case PaintUndoNonStackTarget.MeshAccumulation:
					target = Objects_Renderer_MGR.instance != null ? Objects_Renderer_MGR.instance.accumulationTextures_ref() : null;
					break;
				case PaintUndoNonStackTarget.ArtIconUvColor:
					target = Inpaint_MaskPainter.instance != null ? Inpaint_MaskPainter.instance.EnsureArtIconUvColorWrapper() : null;
					break;
				default:
					return false;
			}
			return target != null && snap.MatchesNonStackTarget(target);
		}

		void LateUpdate() {
			if (_restoreSession == null) return;
			float dt = Time.deltaTime;
			PushSchedulerInspectorSettings();
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
				_scheduler.RegisterRestoreBanditObservation(hitchMs, uploaded);
				if (_logVerbose && _scheduler.restoreBudgetPolicy == RestoreBudgetPolicy.Thompson) {
					bool success = hitchMs < _scheduler.restoreThompsonSuccessHitchMs && uploaded > 0;
					Debug.Log($"[PaintUndo] Restore Thompson obs: hitchMs={hitchMs:F1}, uploaded={uploaded}, success={success}");
				}
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

#if UNITY_EDITOR
		/// <summary>Editor / QA: exercise undo scheduler paths without full paint (see test-undo-capture checklist).</summary>
		[ContextMenu("PaintUndo/Diagnostics: Simulate restore session + one bandit tick")]
		void EditorDiagnostics_SimulateRestoreBanditTick() {
			PushSchedulerInspectorSettings();
			_scheduler.BeginRestoreSession(1024, 1024, 4);
			_scheduler.BeginRestoreTick(1f / 60f);
			_scheduler.GetFrameBudget(4, out _, out _);
			_scheduler.RegisterRestoreBanditObservation(5f, 1);
			Debug.Log("[PaintUndo] Diagnostics: restore session + observation applied (check policy in inspector).");
		}
#endif
	}
}
