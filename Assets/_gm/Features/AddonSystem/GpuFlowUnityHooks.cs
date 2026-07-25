using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace spz {

	/// <summary>
	/// Calls the Python add-on HTTP server GPU Flow <c>/api/v1/gpu-flow/pace</c> endpoint.
	/// When GpuFlow mode is Off, the server returns immediately (no delay). Adaptive/Fixed modes
	/// wait for headroom before/after heavy GPU work (see SD_Generate_NetworkSender, Gen3D_API).
	/// </summary>
	public static class GpuFlowUnityHooks {

		const int DefaultMaxWaitMs = 12000;

		static string EscapeJson(string s) {
			if (string.IsNullOrEmpty(s)) return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		public static IEnumerator PaceFromAddonHttpCoroutine(
			int maxWaitMs = DefaultMaxWaitMs,
			string source = "unity",
			string phase = "unknown",
			string runId = null
		) {
			// During app shutdown, never block on pacing HTTP calls.
			if (Addon_MGR.IsAddonApiShuttingDown())
				yield break;
			// Dial-off must skip HTTP — Python routes remain mounted after unregister if mode was left Adaptive.
			if (!Addon_MGR.IsAddonEnabledStatic("GpuFlowSPZ"))
				yield break;
			// Match FastAPI PaceBody / Python pace clamp so we never send 422 or under-size the HTTP timeout.
			int clampedMs = Mathf.Clamp(maxWaitMs, 50, 120000);
			int port = Addon_MGR.instance != null ? Addon_MGR.instance.GetHttpServerPort() : 5557;
			string url = $"http://127.0.0.1:{port}/api/v1/gpu-flow/pace";
			string src = EscapeJson(source ?? "unity");
			string ph = EscapeJson(phase ?? "unknown");
			string rid = runId == null ? "null" : $"\"{EscapeJson(runId)}\"";
			string json = $"{{\"max_wait_ms\":{clampedMs},\"source\":\"{src}\",\"phase\":\"{ph}\",\"run_id\":{rid}}}";
			using (var req = new UnityWebRequest(url, "POST")) {
				byte[] body = Encoding.UTF8.GetBytes(json);
				req.uploadHandler = new UploadHandlerRaw(body);
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");
				// Python blocks up to max_wait_ms plus a short quiet gap; use ceiling seconds so we do not abort early.
				int paceSecondsCeil = Mathf.CeilToInt(clampedMs / 1000f);
				req.timeout = Mathf.Clamp(paceSecondsCeil + 8, 10, 130);
				yield return req.SendWebRequest();
				// Ignore errors: add-on HTTP may be down or route missing; do not block generation.
			}
		}
	}
}
