using System.Diagnostics;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>
	/// Product gate around Live/Propose: LAVD dispatch + measured bandit feedback.
	/// Ultra-lean may skip Live Forward; Propose always runs.
	/// </summary>
	public static class DecimaconProductGate {
		static LavadSmartScheduler _scheduler = new LavadSmartScheduler(seed: 7);
		static SchedulerSignalPacket _last;
		static bool _hasLast;
		static bool _runForward = true;
		static string _skipReason = "";
		static float _lastForwardQuality = 0.55f;

		public static LavadSmartScheduler Scheduler => _scheduler;
		public static SchedulerSignalPacket LastSignal => _last;
		public static bool HasLastDispatch => _hasLast;
		public static bool LastRunForward => _runForward;
		public static string LastSkipReason => _skipReason;
		public static float LastForwardQuality => _lastForwardQuality;

		public static void ResetForTests(int seed = 7) {
			_scheduler = new LavadSmartScheduler(seed);
			_hasLast = false;
			_last = null;
			_runForward = true;
			_skipReason = "";
			_lastForwardQuality = 0.55f;
		}

		public static SchedulerSignalPacket BeginLive() {
			var tel = TelemetrySnapshot.ForLive(_scheduler.HitchEwmaMs);
			_last = _scheduler.Dispatch(tel);
			_hasLast = true;
			_runForward = true;
			_skipReason = "";
			_lastForwardQuality = 0.55f;
			if (_last.SelectedArm == BanditArm.EnergyBalance && tel.LatencyBudgetMs <= 10f) {
				_runForward = false;
				_skipReason = "ultra_lean_budget";
			}
			return _last;
		}

		public static SchedulerSignalPacket BeginPropose() {
			var tel = TelemetrySnapshot.ForPropose(_scheduler.HitchEwmaMs);
			_last = _scheduler.Dispatch(tel);
			_hasLast = true;
			_runForward = true;
			_skipReason = "";
			_lastForwardQuality = 0.55f;
			return _last;
		}

		/// <summary>Record quality from the latest Decimacon forward (route + head confidence).</summary>
		public static void ReportForwardQuality(float routeConfidence01, float headConfidence01, bool armSucceeded = true) {
			float route = Mathf.Clamp01(routeConfidence01);
			float head = Mathf.Clamp01(headConfidence01);
			float q = 0.4f * route + 0.5f * head + (armSucceeded ? 0.1f : 0f);
			_lastForwardQuality = Mathf.Clamp(q, 0.2f, 0.99f);
		}

		/// <summary>User Accept / Dismiss outcome — measured bandit feedback (scheduler ≠ paint fields).</summary>
		public static void ReportUserOutcome(bool accepted) {
			LavdPaintBoundary.RefuseBanditToPaintDto();
			if (!_hasLast || _last == null) return;
			float acc = accepted ? 0.98f : 0.35f;
			_scheduler.UpdateBandit(new PerformanceFeedback {
				SelectedArm = _last.SelectedArm,
				ActualLatencyMs = Mathf.Max(0.5f, _scheduler.HitchEwmaMs),
				ActualAccuracy = acc,
			});
		}

		public static void EndInference(SchedulerSignalPacket signal, float elapsedMs, bool ranForward, float? accuracy = null) {
			if (!ranForward) {
				_scheduler.ObserveHitchMs(elapsedMs);
				return;
			}
			float acc = accuracy ?? _lastForwardQuality;
			if (!float.IsFinite(acc) || acc <= 0f) acc = 0.5f;
			acc = Mathf.Clamp(acc, 0.2f, 0.99f);
			_scheduler.UpdateBandit(new PerformanceFeedback {
				SelectedArm = signal != null ? signal.SelectedArm : BanditArm.Throughput,
				ActualLatencyMs = elapsedMs,
				ActualAccuracy = acc,
			});
			_scheduler.ObserveHitchMs(elapsedMs);
		}

		public static Stopwatch StartTimer() => Stopwatch.StartNew();

		public static float ElapsedMs(Stopwatch sw) {
			if (sw == null) return 0f;
			sw.Stop();
			return (float)sw.Elapsed.TotalMilliseconds;
		}
	}
}
