using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace spz {

	// Local command bridge so an external agent (MCP server, script) can inspect
	// the app and drive UI actions. Gated by the SpzMcpSPZ ("SPZ MCP") add-on + Listen setting
	// (not spz.config). Loopback only. Boots from RuntimeInitializeOnLoadMethod.
	public class SPZ_Agent_Bridge : MonoBehaviour {

		public const string AddonId = "SpzMcpSPZ";
		public const int DEFAULT_PORT = 8765;

		const string PrefsListen = "spz.agentBridge.listen.v1";
		const string PrefsPort = "spz.agentBridge.port.v1";
		const string PrefsToken = "spz.agentBridge.token.v1";

		const int COMMAND_TIMEOUT_MS = 30000;
		const int MAX_REQUEST_BYTES = 8 * 1024 * 1024;

		public static SPZ_Agent_Bridge instance { get; private set; }

		int _port = DEFAULT_PORT;
		string _token;
		bool _listenDesired;
		TcpListener _listener;
		Thread _acceptThread;
		volatile bool _isStopping;
		string _lastError = "";

		readonly ConcurrentQueue<PendingCmd> _incoming = new ConcurrentQueue<PendingCmd>();
		readonly List<Thread> _clientThreads = new List<Thread>();
		bool _subscribedAddonEvents;


		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Bootstrap() {
			EnsureInstance();
		}

		public static SPZ_Agent_Bridge EnsureInstance() {
			if (instance != null) {
				return instance;
			}
			var go = new GameObject(nameof(SPZ_Agent_Bridge));
			DontDestroyOnLoad(go);
			return go.AddComponent<SPZ_Agent_Bridge>();
		}


		void Awake() {
			if (instance != null && instance != this) {
				DestroyImmediate(gameObject);
				return;
			}
			instance = this;
			LoadSettingsFromPrefs();
			SPZ_Agent_Tools.RegisterAll();
			SubscribeAddonEvents();
			SyncListeningState();
		}

		void OnEnable() {
			SubscribeAddonEvents();
		}

		void OnDisable() {
			UnsubscribeAddonEvents();
		}

		void OnDestroy() {
			if (instance != this) {
				return;
			}
			UnsubscribeAddonEvents();
			StopListening();
			instance = null;
		}

		void SubscribeAddonEvents() {
			if (_subscribedAddonEvents) {
				return;
			}
			Addon_MGR.OnAddonEnabledStateChanged += OnAddonEnabledStateChanged;
			_subscribedAddonEvents = true;
		}

		void UnsubscribeAddonEvents() {
			if (!_subscribedAddonEvents) {
				return;
			}
			Addon_MGR.OnAddonEnabledStateChanged -= OnAddonEnabledStateChanged;
			_subscribedAddonEvents = false;
		}

		void OnAddonEnabledStateChanged(string addonId) {
			if (!string.Equals(addonId, AddonId, StringComparison.Ordinal)) {
				return;
			}
			SyncListeningState();
		}


		void LoadSettingsFromPrefs() {
			_listenDesired = PlayerPrefs.GetInt(PrefsListen, 0) != 0;
			_port = PlayerPrefs.GetInt(PrefsPort, DEFAULT_PORT);
			if (_port < 1 || _port > 65535) {
				_port = DEFAULT_PORT;
			}
			_token = PlayerPrefs.GetString(PrefsToken, "");
			if (string.IsNullOrEmpty(_token)) {
				_token = null;
			}
		}

		void SaveSettingsToPrefs() {
			PlayerPrefs.SetInt(PrefsListen, _listenDesired ? 1 : 0);
			PlayerPrefs.SetInt(PrefsPort, _port);
			PlayerPrefs.SetString(PrefsToken, _token ?? "");
			PlayerPrefs.Save();
		}

		public static bool IsAddonEnabled() {
			return Addon_MGR.IsAddonEnabledStatic(AddonId);
		}

		public bool IsListening => _listener != null && !_isStopping;

		public JObject BuildStatusJson() {
			bool addonEnabled = IsAddonEnabled();
			return new JObject {
				["success"] = true,
				["addon_id"] = AddonId,
				["addon_enabled"] = addonEnabled,
				["listen"] = _listenDesired,
				["listening"] = IsListening,
				["host"] = "127.0.0.1",
				["port"] = _port,
				["has_token"] = !string.IsNullOrEmpty(_token),
				["last_error"] = _lastError ?? "",
				["protocol_version"] = SPZ_Agent_Protocol.PROTOCOL_VERSION,
			};
		}

		/// <summary>
		/// Apply listen/port/token from the lite add-on panel. Requires the add-on enabled.
		/// </summary>
		public JObject ApplySettings(bool listen, int port, string token) {
			if (!IsAddonEnabled()) {
				return new JObject {
					["success"] = false,
					["error"] = $"Add-on '{AddonId}' is not enabled. Enable it in Add-on Manager first.",
				};
			}
			if (port < 1 || port > 65535) {
				return new JObject {
					["success"] = false,
					["error"] = $"Invalid port {port} (expected 1..65535).",
				};
			}
			_listenDesired = listen;
			_port = port;
			_token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
			SaveSettingsToPrefs();
			// Always rebind so port/token changes take effect immediately.
			StopListening();
			SyncListeningState();
			var status = BuildStatusJson();
			if (!string.IsNullOrEmpty(_lastError) && _listenDesired && !IsListening) {
				status["success"] = false;
				status["error"] = _lastError;
			}
			return status;
		}

		public void SyncListeningState() {
			bool shouldListen = IsAddonEnabled() && _listenDesired;
			if (shouldListen) {
				if (IsListening) {
					return;
				}
				StartListening();
			} else {
				StopListening();
				_lastError = "";
			}
		}


		void StartListening() {
			StopListening();
			_isStopping = false;
			_lastError = "";
			try {
				_listener = new TcpListener(IPAddress.Loopback, _port);
				_listener.Start();
			} catch (Exception ex) {
				_lastError = $"could not listen on 127.0.0.1:{_port} — {ex.Message}";
				Debug.LogError($"<color=yellow>[{nameof(SPZ_Agent_Bridge)}]</color> {_lastError}");
				_listener = null;
				return;
			}

			_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "SPZ_AgentBridge_Accept" };
			_acceptThread.Start();
			Debug.Log($"<color=yellow>[{nameof(SPZ_Agent_Bridge)}]</color> listening on 127.0.0.1:{_port}"
			          + (string.IsNullOrEmpty(_token) ? "  (no token)" : "  (token required)"));
		}

		void StopListening() {
			_isStopping = true;
			try { _listener?.Stop(); } catch (Exception) { /* ignore */ }
			_listener = null;
			_acceptThread = null;
			lock (_clientThreads) {
				_clientThreads.Clear();
			}
		}


		void AcceptLoop() {
			while (_isStopping == false) {
				TcpClient client = null;
				try {
					client = _listener?.AcceptTcpClient();
				} catch (Exception) {
					break;
				}
				if (client == null) {
					break;
				}
				var t = new Thread(() => ServeClient(client)) { IsBackground = true, Name = "SPZ_AgentBridge_Client" };
				lock (_clientThreads) { _clientThreads.Add(t); }
				t.Start();
			}
		}

		void ServeClient(TcpClient client) {
			try {
				using (client)
				using (NetworkStream stream = client.GetStream())
				using (var reader = new StreamReader(stream, Encoding.UTF8))
				using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }) {
					while (_isStopping == false) {
						string line = reader.ReadLine();
						if (line == null) { break; }
						if (line.Length == 0) { continue; }
						if (line.Length > MAX_REQUEST_BYTES) {
							writer.WriteLine(JsonConvert.SerializeObject(AgentResponse.Fail(null, "request too large")));
							continue;
						}
						writer.WriteLine(HandleLine(line));
					}
				}
			} catch (Exception) {
				// Dropped connection is normal.
			}
		}

		string HandleLine(string line) {
			AgentRequest req;
			try {
				req = JsonConvert.DeserializeObject<AgentRequest>(line);
			} catch (Exception ex) {
				return JsonConvert.SerializeObject(AgentResponse.Fail(null, "malformed JSON: " + ex.Message));
			}
			if (req == null || string.IsNullOrEmpty(req.tool)) {
				return JsonConvert.SerializeObject(AgentResponse.Fail(req?.id, "missing 'tool'"));
			}
			if (string.IsNullOrEmpty(_token) == false) {
				string given = req.prms?.Value<string>("token");
				if (string.Equals(given, _token, StringComparison.Ordinal) == false) {
					return JsonConvert.SerializeObject(AgentResponse.Fail(req.id, "invalid or missing token"));
				}
			}

			var pending = new PendingCmd(req);
			_incoming.Enqueue(pending);

			if (pending.done.Wait(COMMAND_TIMEOUT_MS) == false) {
				pending.Abandon();
				return JsonConvert.SerializeObject(AgentResponse.Fail(req.id, $"timed out after {COMMAND_TIMEOUT_MS} ms"));
			}
			return pending.responseJson;
		}

		void Update() {
			while (_incoming.TryDequeue(out PendingCmd cmd)) {
				if (cmd.isAbandoned) { continue; }
				Execute(cmd);
			}
		}

		void Execute(PendingCmd cmd) {
			// Wait may have timed out and Abandon()'d after we dequeued but before Execute —
			// do not mutate Unity state for a client that already received a timeout.
			if (cmd.isAbandoned) { return; }
			AgentTool tool = SPZ_Agent_Tools.Find(cmd.req.tool);
			if (tool == null) {
				cmd.Complete(AgentResponse.Fail(cmd.req.id, $"unknown tool '{cmd.req.tool}'. Call 'describe' for the catalogue."));
				return;
			}
			try {
				tool.handler(cmd.req.prms,
					result => {
						if (cmd.isAbandoned) { return; }
						cmd.Complete(AgentResponse.Ok(cmd.req.id, result));
					},
					error => {
						if (cmd.isAbandoned) { return; }
						cmd.Complete(AgentResponse.Fail(cmd.req.id, error));
					});
			} catch (Exception ex) {
				if (cmd.isAbandoned) { return; }
				cmd.Complete(AgentResponse.Fail(cmd.req.id, $"{ex.GetType().Name}: {ex.Message}"));
			}
		}


		class PendingCmd {
			public readonly AgentRequest req;
			public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
			public string responseJson;

			int _completed;
			int _abandoned;
			public bool isAbandoned => Volatile.Read(ref _abandoned) != 0;

			public PendingCmd(AgentRequest req) { this.req = req; }

			public void Complete(AgentResponse resp) {
				if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0) { return; }
				try {
					responseJson = JsonConvert.SerializeObject(resp);
				} catch (Exception ex) {
					responseJson = JsonConvert.SerializeObject(AgentResponse.Fail(resp.id, "could not serialize result: " + ex.Message));
				}
				done.Set();
			}

			public void Abandon() { Interlocked.Exchange(ref _abandoned, 1); }
		}
	}
}//end namespace
