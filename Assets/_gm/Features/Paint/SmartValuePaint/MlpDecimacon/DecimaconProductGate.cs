using System.Diagnostics;

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

		public static LavadSmartScheduler Scheduler => _scheduler;
		public static SchedulerSignalPacket LastSignal => _last;
		public static bool HasLastDispatch => _hasLast;
		public static bool LastRunForward => _runForward;
		public static string LastSkipReason => _skipReason;

		public static void ResetForTests(int seed = 7) {
			_scheduler = new LavadSmartScheduler(seed);
			_hasLast = false;
			_last = null;
			_runForward = true;
			_skipReason = "";
		}

		public static SchedulerSignalPacket BeginLive() {
			var tel = TelemetrySnapshot.ForLive(_scheduler.HitchEwmaMs);
			_last = _scheduler.Dispatch(tel);
			_hasLast = true;
			_runForward = true;
			_skipReason = "";
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
			_runForward = true; // Propose always Forward
			_skipReason = "";
			return _last;
		}

		public static void EndInference(SchedulerSignalPacket signal, float elapsedMs, bool ranForward, float accuracyProxy = 0.99f) {
			if (!ranForward) {
				_scheduler.ObserveHitchMs(elapsedMs);
				return;
			}
			_scheduler.UpdateBandit(new PerformanceFeedback {
				SelectedArm = signal != null ? signal.SelectedArm : BanditArm.Throughput,
				ActualLatencyMs = elapsedMs,
				ActualAccuracy = accuracyProxy,
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
