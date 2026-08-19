using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace spz {

	/// <summary>
	/// TCP JSON-RPC server that receives commands from Python add-ons
	/// and marshals them to the Unity main thread for execution.
	/// </summary>
	[DefaultExecutionOrder(0)]  // Run after Addon_MGR (-100) so port is set and socket binds before Python starts next frame
	public class Addon_SocketServer : MonoBehaviour {
		public static Addon_SocketServer instance { get; private set; }
		
		/// <summary>True once the TCP listener has been started (port 5555 bound). Addon_MGR waits for this before starting Python.</summary>
		public bool IsListening => _isRunning;
		
		private TcpListener _listener;
		private Thread _listenerThread;
		private volatile bool _isRunning = false; // Volatile for thread safety
		private int _port = 5555;
		bool _quitNetworkingShutdownDone;
		
		// Connection limit to prevent resource exhaustion
		private const int MAX_CONCURRENT_CONNECTIONS = 50;
		private int _activeConnections = 0;
		private readonly object _connectionLock = new object();
		
		// Maximum message size to prevent memory exhaustion (10MB)
		private const int MAX_MESSAGE_SIZE = 10 * 1024 * 1024;
		
		// Thread-safe queue for commands from background thread to main thread
		private ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
		
		// Dictionary to store pending responses by request ID
		private ConcurrentDictionary<string, JObject> _pendingResponses = new ConcurrentDictionary<string, JObject>();
		/// <summary>Request ids the waiter already timed out on — late main-thread/coroutine completions must not re-park a response.</summary>
		private ConcurrentDictionary<string, byte> _abandonedResponseIds = new ConcurrentDictionary<string, byte>();
		
		// Maximum commands to process per frame
		private const int MAX_COMMANDS_PER_FRAME = 10;
		// JSON-RPC response wait budgets (background thread waiting for main-thread execution).
		private const int COMMAND_TIMEOUT_DEFAULT_MS = 10000;   // fast commands
		private const int COMMAND_TIMEOUT_LONG_OP_MS = 300000;  // mesh import/export + texture encode (was 120s; UDIM/dilate often exceeds)
		
		void Awake() {
			// Diagnostic: if you search "Addon_SocketServer" in Player.log and never see this line, the scene/GameObject/script is not running (scene not loaded, GO disabled, or script missing).
			UnityEngine.Debug.Log("[Addon_SocketServer] Awake (component running; will bind to port next).");
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			// Bind immediately (Addon_MGR runs first via DefaultExecutionOrder -100 so port is available)
			if (Addon_MGR.instance != null)
				_port = Addon_MGR.instance.GetServerPort();
			StartServer();
		}
		
		void Start() {
			// Socket already bound in Awake; Start() no-op for listener (kept for any future init)
		}
		
		/// <summary>
		/// Starts the TCP listener on a background thread
		/// </summary>
		void StartServer() {
			if (_isRunning) return;
			
			// Delete any stale marker left by a previous crashed/killed process
			// so Python doesn't immediately try to connect before we've actually bound.
			try {
				string staleMarker = GetReadyMarkerPath(_port);
				if (File.Exists(staleMarker)) {
					File.Delete(staleMarker);
					UnityEngine.Debug.Log($"[Addon_SocketServer] Removed stale ready marker from previous run: {staleMarker}");
				}
				string staleFail = GetBindFailedMarkerPath(_port);
				if (File.Exists(staleFail))
					File.Delete(staleFail);
			} catch { }

			if (!TryBindListener()) {
				// Port is occupied. This is normal when Unity Editor is running at the same time
				// (Editor has its own Addon_SocketServer on the same port). Do NOT kill processes
				// on this port -- that would kill the Editor. Just log and skip.
				UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Port {_port} already in use (Unity Editor likely running). Socket server will not start in this instance. Addons will use the Editor's socket instead.");
				try {
					File.WriteAllText(GetBindFailedMarkerPath(_port),
						"Port " + _port + " in use. Close Unity Editor or any process on 127.0.0.1:" + _port + " and restart the game.");
				} catch (Exception ex) {
					UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Could not write bind-failed marker: {ex.Message}");
				}
				return;
			}

			UnityEngine.Debug.Log($"[Addon_SocketServer] Started listening on 127.0.0.1:{_port} (loopback only; Python connects here)");
			try {
				string markerPath = GetReadyMarkerPath(_port);
				File.WriteAllText(markerPath, _port.ToString());
				UnityEngine.Debug.Log($"[Addon_SocketServer] Ready marker written: {markerPath}");
				string failPath = GetBindFailedMarkerPath(_port);
				if (File.Exists(failPath))
					File.Delete(failPath);
			} catch (Exception ex) {
				UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Could not write ready marker file: {ex.Message}");
			}
		}
		
		bool TryBindListener() {
			try {
				_listener = new TcpListener(IPAddress.Loopback, _port);
				_listener.Start();
				_isRunning = true;
				_listenerThread = new Thread(ListenForClients) { IsBackground = true };
				_listenerThread.Start();
				return true;
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_SocketServer] Failed to bind port {_port}: {e.Message}");
				// Start() may already have bound the port before the thread failed to launch. Dropping
				// the reference without Stop() keeps 127.0.0.1:_port held for the rest of the session:
				// addons cannot connect and the next launch reports "port already in use".
				_isRunning = false;//never leave "running" true with no listener thread
				try { _listener?.Stop(); } catch { }
				_listener = null;
				_listenerThread = null;
				return false;
			}
		}

		/// <summary>Path to the ready marker file (same as Python checks). Remove on shutdown.</summary>
		static string GetReadyMarkerPath(int port) => Path.Combine(Path.GetTempPath(), "spz_addon_" + port + "_ready.txt");

		/// <summary>Python polls this to fail fast when port is in use (no 90s wait).</summary>
		static string GetBindFailedMarkerPath(int port) => Path.Combine(Path.GetTempPath(), "spz_addon_" + port + "_bind_failed.txt");
		
		/// <summary>
		/// Background thread loop that accepts connections
		/// </summary>
		void ListenForClients() {
			while (_isRunning) {
				try {
					if (!_listener.Pending()) {
						Thread.Sleep(10);
						continue;
					}
					
					// Check connection limit — must Accept+Close or Pending() stays true forever.
					bool atLimit;
					lock (_connectionLock) {
						atLimit = _activeConnections >= MAX_CONCURRENT_CONNECTIONS;
					}
					if (atLimit) {
						UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Connection limit reached ({MAX_CONCURRENT_CONNECTIONS}), rejecting new connection");
						try {
							TcpClient rejected = _listener.AcceptTcpClient();
							rejected?.Close();
						} catch (Exception rejectEx) {
							if (_isRunning)
								UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Reject close failed: {rejectEx.Message}");
						}
						Thread.Sleep(100);
						continue;
					}
					
					TcpClient client = _listener.AcceptTcpClient();
					lock (_connectionLock) {
						_activeConnections++;
					}
					Thread clientThread = new Thread(() => {
						try {
							HandleClient(client);
						} finally {
							lock (_connectionLock) {
								_activeConnections--;
							}
						}
					}) {
						IsBackground = true
					};
					clientThread.Start();
				}
				catch (Exception e) {
					if (_isRunning) {
						UnityEngine.Debug.LogError($"[Addon_SocketServer] Error accepting client: {e.Message}");
					}
				}
			}
		}
		
		/// <summary>
		/// Handles a single client connection
		/// </summary>
		void HandleClient(TcpClient client) {
			NetworkStream stream = null;
			try {
				stream = client.GetStream();
				byte[] buffer = new byte[4096];
				StringBuilder messageBuffer = new StringBuilder(); // Buffer for incomplete messages
				
				while (client.Connected && _isRunning) {
					int bytesRead = 0;
					try {
						bytesRead = stream.Read(buffer, 0, buffer.Length);
					} catch (System.Net.Sockets.SocketException) {
						// Client disconnected
						break;
					} catch (System.IO.IOException) {
						// Stream closed
						break;
					}
					
					if (bytesRead == 0) break;
					
					// Check message buffer size to prevent memory exhaustion
					if (messageBuffer.Length + bytesRead > MAX_MESSAGE_SIZE) {
						UnityEngine.Debug.LogError($"[Addon_SocketServer] Message buffer exceeded maximum size ({MAX_MESSAGE_SIZE} bytes), closing connection");
						break;
					}
					
					// Append to message buffer (handles split messages)
					messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
					
					// Process complete messages (delimited by newline)
					string remaining = messageBuffer.ToString();
					messageBuffer.Clear();
					
					// Check if remaining ends with newline (all messages complete)
					bool endsWithNewline = remaining.EndsWith("\n");
					
					// Split by newline, but keep empty entries to detect incomplete messages
					string[] messages = remaining.Split(new[] { '\n' }, StringSplitOptions.None);
					
					if (endsWithNewline) {
						// All messages are complete, process all (including empty ones from trailing newlines)
						foreach (string message in messages) {
							if (!string.IsNullOrWhiteSpace(message)) {
								ProcessMessage(message, stream);
							}
						}
					} else if (messages.Length > 0) {
						// Last message is incomplete, keep it in buffer
						string lastMessage = messages[messages.Length - 1];
						if (!string.IsNullOrEmpty(lastMessage)) {
							messageBuffer.Append(lastMessage);
						}
						
						// Process all but the last message
						for (int i = 0; i < messages.Length - 1; i++) {
							if (!string.IsNullOrWhiteSpace(messages[i])) {
								ProcessMessage(messages[i], stream);
							}
						}
					}
				}
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_SocketServer] Error handling client: {e.Message}");
			}
			finally {
				// Properly dispose resources
				try {
					stream?.Close();
					stream?.Dispose();
				} catch { }
				try {
					client?.Close();
					client?.Dispose();
				} catch { }
			}
		}
		
		/// <summary>
		/// Processes a single JSON-RPC message
		/// </summary>
		void ProcessMessage(string message, NetworkStream stream) {
			// Validate message size before parsing
			if (message.Length > MAX_MESSAGE_SIZE) {
				UnityEngine.Debug.LogError($"[Addon_SocketServer] Message too large ({message.Length} bytes), maximum is {MAX_MESSAGE_SIZE}");
				SendErrorResponse(stream, null, -32600, "Message too large");
				return;
			}
			
			try {
				var request = JObject.Parse(message);
				var requestId = request["id"]?.ToString();
				var response = ProcessRequest(request);
				
				// Send response back to client
				string responseJson = JsonConvert.SerializeObject(response);
				byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
				try {
					stream.Write(responseBytes, 0, responseBytes.Length);
					stream.Flush(); // Ensure data is sent immediately
				} catch (System.Net.Sockets.SocketException) {
					// Client disconnected, ignore
				} catch (System.IO.IOException) {
					// Stream closed, ignore
				}
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_SocketServer] Error processing request: {e.Message}");
				
				// Try to extract request ID from message
				JToken requestId = null;
				try {
					var request = JObject.Parse(message);
					requestId = request["id"];
				} catch { }
				
				SendErrorResponse(stream, requestId, -32700, "Parse error");
			}
		}
		
		/// <summary>
		/// Sends an error response to the client
		/// </summary>
		void SendErrorResponse(NetworkStream stream, JToken requestId, int errorCode, string errorMessage) {
			var errorResponse = new JObject {
				["jsonrpc"] = "2.0",
				["error"] = new JObject {
					["code"] = errorCode,
					["message"] = errorMessage
				},
				["id"] = requestId ?? JValue.CreateNull()
			};
			string errorJson = JsonConvert.SerializeObject(errorResponse);
			byte[] errorBytes = Encoding.UTF8.GetBytes(errorJson + "\n");
			try {
				stream.Write(errorBytes, 0, errorBytes.Length);
				stream.Flush();
			} catch {
				// Stream may be closed, ignore
			}
		}
		
		/// <summary>
		/// Processes a JSON-RPC request and queues the command for main thread execution
		/// </summary>
		JObject ProcessRequest(JObject request) {
			string method = request["method"]?.ToString();
			var @params = request["params"] as JObject;
			var id = request["id"]?.ToString() ?? Guid.NewGuid().ToString();
			
			if (string.IsNullOrEmpty(method)) {
				return CreateErrorResponse(-32600, "Invalid Request", JToken.FromObject(id));
			}
			
			// Queue command for main thread execution
			_pendingResponses[id] = null; // Mark as pending
			_mainThreadQueue.Enqueue(() => {
				try {
					// Headless mesh I/O: export waits for texture encode; import waits for Assimp/UDIM complete.
					if (DefersResponseUntilProjectSaveIdle(method)) {
						BeginCommandAndRespondWhenProjectSaveIdle(id, method, @params);
						return;
					}
					if (DefersResponseUntilImportIdle(method)) {
						BeginCommandAndRespondWhenImportIdle(id, method, @params);
						return;
					}
					JObject response = ExecuteCommand(method, @params);
					response["id"] = JToken.FromObject(id);
					TryPublishPendingResponse(id, response);
				}
				catch (Exception e) {
					TryPublishPendingResponse(id, CreateErrorResponse(-32603, $"Internal error: {e.Message}", JToken.FromObject(id)));
				}
			});
			
			// Wait for command to execute (with timeout)
			// Note: This blocks the background thread, but is necessary for synchronous response
			// Consider using async/await pattern in future for better scalability
			int timeout = IsLongRunningMethod(method) ? COMMAND_TIMEOUT_LONG_OP_MS : COMMAND_TIMEOUT_DEFAULT_MS;
			int elapsed = 0;
			int checkInterval = 50; // Check every 50ms instead of 10ms to reduce CPU usage
			
			while (elapsed < timeout) {
				if (_pendingResponses.TryGetValue(id, out JObject response) && response != null) {
					_pendingResponses.TryRemove(id, out _);
					return response;
				}
				Thread.Sleep(checkInterval);
				elapsed += checkInterval;
			}
			
			// Cleanup on timeout — mark abandoned so a late ExecuteCommand / CoRespond* write is dropped.
			_abandonedResponseIds[id] = 0;
			_pendingResponses.TryRemove(id, out _);
			UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Command '{method}' timed out after {timeout}ms");
			return CreateErrorResponse(-32603, "Command execution timeout", JToken.FromObject(id));
		}

		/// <summary>
		/// Publish a JSON-RPC response for a waiter still holding <paramref name="id"/>.
		/// No-ops when the background thread already timed out (avoids orphan dict entries and duplicate client retries).
		/// </summary>
		bool TryPublishPendingResponse(string id, JObject response) {
			if (string.IsNullOrEmpty(id) || response == null)
				return false;
			if (_abandonedResponseIds.TryRemove(id, out _)) {
				UnityEngine.Debug.LogWarning(
					$"[Addon_SocketServer] Dropping late response for timed-out request id={id}");
				return false;
			}
			if (!_pendingResponses.ContainsKey(id))
				return false;
			_pendingResponses[id] = response;
			return true;
		}

		static bool IsLongRunningMethod(string method) {
			if (string.IsNullOrEmpty(method)) {
				return false;
			}
			switch (method) {
				case "spz.cmd.import_3d_model":
				case "spz.cmd.export_3d_with_textures":
				case "spz.cmd.export_3d_with_textures_to_path":
				case "spz.cmd.stream_mesh_to_blender":
				case "spz.cmd.save_project":
				case "spz.cmd.load_project":
				case "spz.cmd.export_projection_textures":
				case "spz.cmd.export_view_textures":
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// True when JSON-RPC success must wait for <see cref="Save_MGR._isSaving"/> to clear
		/// (mesh write is sync; texture encode/write is deferred via coroutines).
		/// </summary>
		public static bool DefersResponseUntilProjectSaveIdle(string method) {
			return string.Equals(method, "spz.cmd.export_3d_with_textures_to_path", StringComparison.Ordinal)
				|| string.Equals(method, "spz.cmd.export_3d_with_textures", StringComparison.Ordinal)
				|| string.Equals(method, "spz.cmd.export_projection_textures", StringComparison.Ordinal)
				|| string.Equals(method, "spz.cmd.export_view_textures", StringComparison.Ordinal)
				|| string.Equals(method, "spz.cmd.save_project", StringComparison.Ordinal)
				|| string.Equals(method, "spz.cmd.load_project", StringComparison.Ordinal);
		}

		/// <summary>
		/// True when JSON-RPC success must wait for <see cref="ModelsHandler_3D._isImportingModel"/> to clear
		/// (Assimp load + UDIM scan are async after TryImport returns).
		/// </summary>
		public static bool DefersResponseUntilImportIdle(string method) {
			return string.Equals(method, "spz.cmd.import_3d_model", StringComparison.Ordinal);
		}

		void BeginCommandAndRespondWhenProjectSaveIdle(string id, string method, JObject @params) {
			JObject result;
			try {
				result = ExecuteFastPathCommand(method, @params ?? new JObject());
			} catch (Exception e) {
				TryPublishPendingResponse(id, CreateErrorResponse(-32603, $"Internal error: {e.Message}", JToken.FromObject(id)));
				return;
			}
			bool started = result["success"]?.ToObject<bool>() ?? false;
			if (!started) {
				TryPublishPendingResponse(id, new JObject {
					["jsonrpc"] = "2.0",
					["result"] = result,
					["id"] = JToken.FromObject(id)
				});
				return;
			}
			string meshPath = @params?["mesh_filepath"]?.ToString() ?? "";
			StartCoroutine(CoRespondWhenProjectSaveIdle(id, result, meshPath, method));
		}

		void BeginCommandAndRespondWhenImportIdle(string id, string method, JObject @params) {
			JObject result;
			try {
				result = ExecuteFastPathCommand(method, @params ?? new JObject());
			} catch (Exception e) {
				TryPublishPendingResponse(id, CreateErrorResponse(-32603, $"Internal error: {e.Message}", JToken.FromObject(id)));
				return;
			}
			bool started = result["success"]?.ToObject<bool>() ?? false;
			if (!started) {
				TryPublishPendingResponse(id, new JObject {
					["jsonrpc"] = "2.0",
					["result"] = result,
					["id"] = JToken.FromObject(id)
				});
				return;
			}
			StartCoroutine(CoRespondWhenImportIdle(id, result));
		}

		IEnumerator CoRespondWhenProjectSaveIdle(string id, JObject result, string meshFilePath, string method = null) {
			float timeoutSec = COMMAND_TIMEOUT_LONG_OP_MS / 1000f;
			float elapsed = 0f;
			var sm = Save_MGR.instance;
			bool isSaveProject = string.Equals(method, "spz.cmd.save_project", StringComparison.Ordinal);
			bool isLoadProject = string.Equals(method, "spz.cmd.load_project", StringComparison.Ordinal);

			if (isSaveProject || isLoadProject) {
				// Dialog/async: wait until in-flight clears (save) or _isLoading clears (load).
				while (sm != null && elapsed < timeoutSec) {
					bool busy = isLoadProject
						? sm._isLoading
						: (sm._isSaving || (sm.SaveLoadHelper != null && sm.SaveLoadHelper.IsProjectSaveInFlight));
					if (!busy) break;
					elapsed += Time.unscaledDeltaTime;
					yield return null;
					sm = Save_MGR.instance;
				}
				if (sm == null) {
					result["success"] = false;
					result["error"] = isLoadProject ? "load failed (Save_MGR unavailable)" : "save failed (Save_MGR unavailable)";
				} else if (isLoadProject && sm._isLoading) {
					result["success"] = false;
					result["error"] = "load timed out";
				} else if (isSaveProject && (sm._isSaving || (sm.SaveLoadHelper != null && sm.SaveLoadHelper.IsProjectSaveInFlight))) {
					result["success"] = false;
					result["error"] = "save timed out";
				} else if (isSaveProject) {
					bool ok = sm.SaveLoadHelper != null && sm.SaveLoadHelper.LastProjectSaveSucceeded;
					result["success"] = ok;
					if (!ok) result["error"] = "save cancelled or failed";
				} else {
					bool ok = sm.SaveLoadHelper != null && sm.SaveLoadHelper.LastProjectLoadSucceeded;
					result["success"] = ok;
					if (!ok) result["error"] = "load cancelled or failed";
				}
				TryPublishPendingResponse(id, new JObject {
					["jsonrpc"] = "2.0",
					["result"] = result,
					["id"] = JToken.FromObject(id)
				});
				yield break;
			}

			// Export sets _isSaving before returning; wait until texture pipeline OnComplete clears it.
			while (sm != null && sm._isSaving && elapsed < timeoutSec) {
				elapsed += Time.unscaledDeltaTime;
				yield return null;
				sm = Save_MGR.instance;
			}
			if (sm == null) {
				result["success"] = false;
				result["error"] = "export to path failed (Save_MGR unavailable during texture write)";
				UnityEngine.Debug.LogWarning("[Addon_SocketServer] export_3d_with_textures_to_path: Save_MGR became null while waiting for texture write.");
			} else if (sm._isSaving) {
				result["success"] = false;
				result["error"] = "export to path timed out waiting for texture write";
				UnityEngine.Debug.LogWarning("[Addon_SocketServer] export_3d_with_textures_to_path: texture write still in progress after timeout.");
			} else if (!string.IsNullOrEmpty(meshFilePath)) {
				// Ready stamp is ToPath-only (dialog export never writes .spz_go_ready).
				// Prefer the FBX path actually written (SaveDefaultDoor may normalize extension).
				string stampMeshPath = meshFilePath;
				var mh = ModelsHandler_3D.instance;
				if (mh != null && !string.IsNullOrEmpty(mh._path_recentlyExported))
					stampMeshPath = mh._path_recentlyExported;
				string stamp = null;
				try {
					if (!string.IsNullOrEmpty(stampMeshPath)) {
						string dir = Path.GetDirectoryName(stampMeshPath);
						string baseName = Path.GetFileNameWithoutExtension(stampMeshPath);
						if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
							stamp = Path.Combine(dir, baseName + ".spz_go_ready");
					}
				} catch (Exception ex) {
					UnityEngine.Debug.LogWarning("[Addon_SocketServer] export stamp path: " + ex.Message);
				}
				if (string.IsNullOrEmpty(stamp) || !File.Exists(stamp)) {
					result["success"] = false;
					result["error"] = "export to path failed (ready stamp missing for Blender auto-import)";
					UnityEngine.Debug.LogWarning("[Addon_SocketServer] export_3d_with_textures_to_path: ready stamp missing: " + stamp);
				}
			} else if (string.Equals(method, "spz.cmd.export_projection_textures", StringComparison.Ordinal)
			           || string.Equals(method, "spz.cmd.export_view_textures", StringComparison.Ordinal)) {
				// Idle after cancel also clears _isSaving — use the dedicated success flag so cancel
				// does not leave the deferred TCP result at the initial success:true.
				bool ok = sm.LastTextureDialogExportSucceeded;
				result["success"] = ok;
				if (!ok) result["error"] = "texture export cancelled or failed";
			} else {
				// Dialog mesh+texture export: no .spz_go_ready. Succeed only if this op wrote a mesh.
				var mh = ModelsHandler_3D.instance;
				string written = mh != null ? mh._path_recentlyExported : null;
				if (string.IsNullOrEmpty(written) || !File.Exists(written)) {
					result["success"] = false;
					result["error"] = "export cancelled or mesh not written";
				}
			}
			TryPublishPendingResponse(id, new JObject {
				["jsonrpc"] = "2.0",
				["result"] = result,
				["id"] = JToken.FromObject(id)
			});
		}

		IEnumerator CoRespondWhenImportIdle(string id, JObject result) {
			float timeoutSec = COMMAND_TIMEOUT_LONG_OP_MS / 1000f;
			float elapsed = 0f;
			var mh = ModelsHandler_3D.instance;
			while (mh != null && mh._isImportingModel && elapsed < timeoutSec) {
				elapsed += Time.unscaledDeltaTime;
				yield return null;
				mh = ModelsHandler_3D.instance;
			}
			if (mh == null) {
				result["success"] = false;
				result["error"] = "import failed (ModelsHandler unavailable during load)";
				UnityEngine.Debug.LogWarning("[Addon_SocketServer] import_3d_model: ModelsHandler became null while waiting.");
			} else if (mh._isImportingModel) {
				result["success"] = false;
				result["error"] = "import timed out waiting for Assimp/UDIM load";
				UnityEngine.Debug.LogWarning("[Addon_SocketServer] import_3d_model: still importing after timeout.");
			} else if (!mh._lastImportSucceeded) {
				result["success"] = false;
				result["error"] = "import failed (Assimp/Init/UDIM)";
			} else {
				result["success"] = true;
			}
			TryPublishPendingResponse(id, new JObject {
				["jsonrpc"] = "2.0",
				["result"] = result,
				["id"] = JToken.FromObject(id)
			});
		}
		
		/// <summary>
		/// Bool toggles (set_sd_* etc): a missing value/on param must be an error,
		/// not a silent "turn the feature off and report success".
		/// </summary>
		static bool TryReadBoolParam(JObject @params, out bool value, out string error) {
			value = false;
			error = null;
			JToken tok = @params?["value"] ?? @params?["on"];
			if (tok == null) { error = "Missing value/on (boolean)"; return false; }
			try { value = tok.ToObject<bool>(); return true; }
			catch { error = "Invalid value/on (boolean)"; return false; }
		}

		/// <summary>
		/// Executes a command on the main thread
		/// </summary>
		JObject ExecuteCommand(string method, JObject @params) {
			@params ??= new JObject();
			var result = new JObject();
			
			// Route to appropriate handler
			if (method.StartsWith("spz.cmd.")) {
				result = ExecuteFastPathCommand(method, @params);
			}
			else if (method.StartsWith("spz.ui.")) {
				result = ExecuteUICommand(method, @params);
			}
			else {
				return CreateErrorResponse(-32601, $"Method not found: {method}", null);
			}
			
			return new JObject {
				["jsonrpc"] = "2.0",
				["result"] = result
			};
		}
		
		/// <summary>
		/// Directly executes a JSON-RPC request synchronously (for HTTP server)
		/// Must be called from main thread
		/// </summary>
		public JObject ProcessRequestDirect(JObject request) {
			string method = request["method"]?.ToString();
			var @params = request["params"] as JObject ?? new JObject();
			
			if (string.IsNullOrEmpty(method)) {
				return CreateErrorResponse(-32600, "Invalid Request", null);
			}
			
			return ExecuteCommand(method, @params);
		}
		
		/// <summary>
		/// Static catalog of JSON-RPC methods supported by this build (for spz.cmd.get_api_capabilities).
		/// Keep in sync when adding cases in ExecuteFastPathCommand / ExecuteUICommand.
		/// </summary>
		static JObject BuildAddonApiCapabilities() {
			var cmd = new JArray {
				"spz.cmd.deselect_all_meshes", "spz.cmd.deselect_mesh", "spz.cmd.export_3d_with_textures",
				"spz.cmd.export_3d_with_textures_to_path", "spz.cmd.export_projection_textures", "spz.cmd.export_view_textures",
				"spz.cmd.import_3d_model", "spz.cmd.stream_mesh_to_blender",
				"spz.cmd.agent_bridge_apply_settings", "spz.cmd.agent_bridge_get_status",
				"spz.cmd.get_active_controlnet_unit_count", "spz.cmd.get_addon_context", "spz.cmd.get_all_camera_fovs",
				"spz.cmd.get_all_camera_positions", "spz.cmd.get_all_camera_rotations", "spz.cmd.get_all_mesh_ids",
				"spz.cmd.get_api_capabilities", "spz.cmd.get_brush_settings", "spz.cmd.get_camera_pos",
				"spz.cmd.get_camera_fov", "spz.cmd.get_camera_rot",
				"spz.cmd.get_controlnet_unit_count", "spz.cmd.get_controlnet_unit_enabled",
				"spz.cmd.get_controlnet_unit_model", "spz.cmd.get_controlnet_unit_weight",
				"spz.cmd.get_cursor_state",
				"spz.cmd.get_display_mode",
				"spz.cmd.get_editor_layout",
				"spz.cmd.get_event_system", "spz.cmd.get_ribbon_tabs",
				"spz.cmd.get_manipulation_target_mesh_id", "spz.cmd.get_mesh_bounds", "spz.cmd.get_mesh_name", "spz.cmd.get_mesh_pos", "spz.cmd.get_mesh_rot",
				"spz.cmd.get_mesh_scale", "spz.cmd.get_mesh_visibility", "spz.cmd.get_negative_prompt",
				"spz.cmd.get_paint_layers", "spz.cmd.get_positive_prompt", "spz.cmd.get_projection_camera_count",
				"spz.cmd.get_projection_camera_pos", "spz.cmd.get_projection_camera_rot",
				"spz.cmd.get_project_data_dir", "spz.cmd.get_project_path", "spz.cmd.get_project_version",
				"spz.cmd.get_sd_workflow_options", "spz.cmd.get_selected_mesh_count", "spz.cmd.get_selected_meshes",
				"spz.cmd.get_selected_meshes_bounds", "spz.cmd.get_skybox_bottom_color", "spz.cmd.get_skybox_top_color",
				"spz.cmd.get_total_mesh_count", 				"spz.cmd.get_ui_scale", "spz.cmd.get_ui_target_active",
				"spz.cmd.get_view_camera_projection", "spz.cmd.get_view_cameras", "spz.cmd.get_view_camera_povs",
				"spz.cmd.get_workflow_mode", "spz.cmd.is_3d_connected",
				"spz.cmd.is_3d_generation_in_progress", "spz.cmd.is_3d_generation_ready", "spz.cmd.is_generating",
				"spz.cmd.is_project_operation_in_progress", "spz.cmd.is_sd_connected",
				"spz.cmd.is_skybox_gradient_clear", "spz.cmd.list_ui_targets", "spz.cmd.load_project", "spz.cmd.save_project",
				"spz.cmd.select_all_meshes", "spz.cmd.select_mesh", "spz.cmd.set_active_paint_layer",
				"spz.cmd.set_brush_angle", "spz.cmd.set_brush_opacity", "spz.cmd.set_brush_roundness",
				"spz.cmd.set_brush_size", "spz.cmd.set_brush_spacing", "spz.cmd.set_brush_stamp_index",
				"spz.cmd.set_camera_fov", "spz.cmd.set_camera_pos", "spz.cmd.set_camera_rot",
				"spz.cmd.set_current_view_camera", "spz.cmd.set_controlnet_unit_enabled", "spz.cmd.set_controlnet_unit_weight",
				"spz.cmd.set_cursor_state",
				"spz.cmd.set_display_mode",
				"spz.cmd.set_editor_layout",
				"spz.cmd.set_event_system",
				"spz.cmd.set_ribbon_tab",
				"spz.cmd.set_mesh_pos", "spz.cmd.set_mesh_positions", "spz.cmd.set_mesh_rot",
				"spz.cmd.set_mesh_rotations", "spz.cmd.set_mesh_scale", "spz.cmd.set_mesh_scales",
				"spz.cmd.set_mesh_visibility", "spz.cmd.set_negative_prompt", "spz.cmd.set_positive_prompt",
				"spz.cmd.set_projection_camera_pos", "spz.cmd.set_projection_camera_rot",
				"spz.cmd.set_sd_denoising_strength", "spz.cmd.set_sd_ignore_depth_or_normals",
				"spz.cmd.set_sd_inpainting_mask_invert", "spz.cmd.set_sd_mask_blur", "spz.cmd.set_sd_soft_inpaint",
				"spz.cmd.set_sd_strict_isolation_flip", "spz.cmd.set_sd_tileable_inpaint",
				"spz.cmd.set_skybox_color", 				"spz.cmd.set_ui_scale", "spz.cmd.set_ui_target_active",
				"spz.cmd.set_view_camera_active", "spz.cmd.set_view_camera_projection", "spz.cmd.set_view_cameras_enabled_count",
				"spz.cmd.isolate_view_camera", "spz.cmd.restore_view_camera_povs", "spz.cmd.apply_view_camera_slot_pov",
				"spz.cmd.set_workflow_mode", "spz.cmd.show_status_text", "spz.cmd.stop_generation",
				"spz.cmd.trigger_3d_generation", "spz.cmd.trigger_texture_generation",
			};
			var ui = new JArray {
				"spz.ui.add_button", "spz.ui.add_dropdown", "spz.ui.add_foldout", "spz.ui.add_host_sections",
				"spz.ui.add_slider", "spz.ui.add_text_input", "spz.ui.add_toggle",
				"spz.ui.attach_viewport_axis_gizmo",
				"spz.ui.attach_viewport_fullview_toggle",
				"spz.ui.apply_theme", "spz.ui.create_panel", "spz.ui.get_theme", "spz.ui.get_value",
				"spz.ui.list_line_icons", "spz.ui.list_themes", "spz.ui.register_theme", "spz.ui.reset_theme",
				"spz.ui.set_line_icon", "spz.ui.set_value", "spz.ui.unregister_theme",
			};
			return new JObject {
				["success"] = true,
				["addon_rpc_version"] = "1.17",
				["spz_cmd"] = cmd,
				["spz_ui"] = ui,
				["context_command"] = "spz.cmd.get_addon_context",
				["note"] = "get_api_capabilities, editor/display/chrome UI helpers are available before FastPath; get_addon_context requires FastPath ready.",
			};
		}

		/// <summary>
		/// Main editor chrome (left input column, center viewport, right ribbon column) via <see cref="Global_Skeleton_UI"/>.
		/// When both sides are hidden, <see cref="ViewportFullViewOnScreen_Driver"/> updates and
		/// <see cref="FullView_OuterPanel_Chrome_Binder"/> suppresses outer right panel draw overflow (same path as in-app full view).
		/// Does not require FastPath_API (runs as soon as the skeleton scene is loaded).
		/// </summary>
		static JObject TryExecuteEditorLayoutCommand(string method, JObject @params) {
			if (method == "spz.cmd.get_editor_layout") {
				var result = new JObject { ["success"] = false };
				if (Global_Skeleton_UI.instance == null) {
					result["error"] = "Global_Skeleton_UI not available";
					return result;
				}
				if (!Global_Skeleton_UI.instance.TryGetSidePanelVisibility(out bool left, out bool right)) {
					result["error"] = "Editor layout not ready (missing column RectTransforms, LayoutElements, or width snapshot)";
					return result;
				}
				result["success"] = true;
				result["left_visible"] = left;
				result["right_visible"] = right;
				result["viewport_expanded"] = !left && !right;
				// Legacy name: same as viewport_expanded (both skeleton columns hidden; inner viewport ribbons remain).
				result["center_max"] = !left && !right;
				return result;
			}

			if (method == "spz.cmd.set_editor_layout") {
				var result = new JObject { ["success"] = false };
				if (Global_Skeleton_UI.instance == null) {
					result["error"] = "Global_Skeleton_UI not available";
					return result;
				}
				if (!Global_Skeleton_UI.instance.TryGetSidePanelVisibility(out _, out _)) {
					result["error"] = "Editor layout not ready (missing column RectTransforms, LayoutElements, or width snapshot)";
					return result;
				}

				string mode = @params["mode"]?.ToString();
				if (!string.IsNullOrEmpty(mode) && (mode == "center_max" || mode == "ribbon_right")) {
					bool okEnter = ViewportFullViewOnScreen_Driver.TryEnter();
					result["success"] = okEnter;
					if (okEnter) {
						FullView_OuterPanel_Chrome_Binder.SyncChromeToDriver();
						if (Global_Skeleton_UI.instance != null) {
							Global_Skeleton_UI.instance.ForceLayoutRefreshAfterPanelResize();
						}
						ViewportFullViewOnScreen_Driver.NotifyLayoutRefreshedForPendingGenRefit();
					} else {
						result["error"] = "Failed to apply on-screen full view (skeleton not ready)";
					}
					return result;
				}

				if (!string.IsNullOrEmpty(mode) && mode == "center_max_off") {
					bool okExit = ViewportFullViewOnScreen_Driver.TryExit();
					result["success"] = okExit;
					if (okExit) {
						// Mirror center_max enter — TryExit alone leaves outer chrome/layout stale
						// (ActiveChanged may also be null after Play without domain reload).
						FullView_OuterPanel_Chrome_Binder.SyncChromeToDriver();
						if (Global_Skeleton_UI.instance != null) {
							Global_Skeleton_UI.instance.ForceLayoutRefreshAfterPanelResize();
						}
						ViewportFullViewOnScreen_Driver.NotifyLayoutRefreshedForPendingGenRefit();
					} else {
						result["error"] = "Failed to restore editor layout from on-screen full view";
					}
					return result;
				}

				bool left = true;
				bool right = true;
				if (!string.IsNullOrEmpty(mode)) {
					if (mode == "viewport_focus" || mode == "fullscreen_center") {
						left = false;
						right = false;
					}
					else if (mode == "default") {
						left = true;
						right = true;
					}
					else {
						result["error"] = "Unknown mode; use default, viewport_focus, fullscreen_center, center_max, ribbon_right, center_max_off";
						return result;
					}
				}

				try {
					if (@params["left_visible"] != null) {
						left = @params["left_visible"].ToObject<bool>();
					}
					if (@params["right_visible"] != null) {
						right = @params["right_visible"].ToObject<bool>();
					}
				}
				catch {
					result["error"] = "Invalid left_visible or right_visible (use boolean JSON values)";
					return result;
				}

				bool ok2 = Global_Skeleton_UI.instance.SetSidePanelVisibility(left, right);
				if (ok2) {
					ViewportFullViewOnScreen_Driver.SyncFromCurrentSkeleton();
					FullView_OuterPanel_Chrome_Binder.SyncChromeToDriver();
					if (Global_Skeleton_UI.instance != null) {
						Global_Skeleton_UI.instance.ForceLayoutRefreshAfterPanelResize();
					}
				}
				result["success"] = ok2;
				if (!ok2) {
					result["error"] = "Failed to apply editor layout";
				}
				return result;
			}

			return null;
		}

		/// <summary>
		/// Player window: windowed, borderless fullscreen, OS exclusive fullscreen via <see cref="SpzPlayerDisplay_API"/>.
		/// </summary>
		static JObject TryExecutePlayerDisplayCommand(string method, JObject @params) {
			if (method == "spz.cmd.get_display_mode") {
				SpzPlayerDisplay_API.GetScreenState(out bool fs, out FullScreenMode fsMode, out int w, out int h);
				SpzPlayerDisplay_API.GetPrimaryDisplaySize(out int mainW, out int mainH);
				bool hasPref = SpzPlayerDisplay_API.TryGetPreferredWindowedSize(out int prefW, out int prefH);
				var jo = new JObject {
					["success"] = true,
					["fullscreen"] = fs,
					["fullscreen_mode"] = fsMode.ToString(),
					["exclusive_fullscreen"] = SpzPlayerDisplay_API.IsExclusiveFullScreen(fsMode),
					["width"] = w,
					["height"] = h,
					["main_display_width"] = mainW,
					["main_display_height"] = mainH,
					["preferred_window_saved"] = hasPref,
					["batch_mode"] = Application.isBatchMode,
				};
				if (hasPref) {
					jo["preferred_window_width"] = prefW;
					jo["preferred_window_height"] = prefH;
				}
				return jo;
			}

			if (method == "spz.cmd.set_display_mode") {
				var result = new JObject { ["success"] = false };
				if (Application.isBatchMode) {
					result["error"] = "Display mode cannot be changed in batch mode";
					return result;
				}

				string mode = @params["mode"]?.ToString();
				if (string.IsNullOrEmpty(mode)) {
					result["error"] = "Missing mode (windowed, exclusive_fullscreen, borderless_fullscreen)";
					return result;
				}

				int w;
				int h;
				int hz;
				try {
					w = @params["width"]?.ToObject<int>() ?? 0;
					h = @params["height"]?.ToObject<int>() ?? 0;
					hz = @params["refresh_rate_hz"]?.ToObject<int>() ?? 0;
				}
				catch {
					result["error"] = "Invalid width, height, or refresh_rate_hz (use integer JSON values)";
					return result;
				}

				if (w < 0 || h < 0) {
					result["error"] = "width and height must be >= 0 when specified";
					return result;
				}
				if (hz < 0) {
					result["error"] = "refresh_rate_hz must be >= 0";
					return result;
				}
				const int maxResolution = 16384;
				if ((w > 0 && w > maxResolution) || (h > 0 && h > maxResolution)) {
					result["error"] = $"width and height must be <= {maxResolution}";
					return result;
				}
				if (hz > 1000) {
					result["error"] = "refresh_rate_hz is unrealistically large";
					return result;
				}

				bool ok;
				switch (mode) {
					case "windowed":
						ok = SpzPlayerDisplay_API.SetWindowed(w, h);
						break;
					case "exclusive_fullscreen":
					case "exclusive":
						ok = SpzPlayerDisplay_API.SetExclusiveFullScreen(w, h, hz);
						break;
					case "borderless_fullscreen":
					case "borderless":
					case "fullscreen_window":
						ok = SpzPlayerDisplay_API.SetBorderlessFullScreen(w, h);
						break;
					default:
						result["error"] = "Unknown mode; use windowed, exclusive_fullscreen, borderless_fullscreen";
						return result;
				}

				result["success"] = ok;
				if (!ok) {
					result["error"] = "Failed to apply display mode";
				}
				return result;
			}

			return null;
		}

		/// <summary>
		/// Ribbon tab strip, cursor lock/visibility — no FastPath required.
		/// </summary>
		static JObject TryExecuteUiChromeCommand(string method, JObject @params) {
			if (method == "spz.cmd.get_ribbon_tabs") {
				var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbon == null) {
					return new JObject { ["success"] = false, ["error"] = "CommandRibbon_UI not available" };
				}
				var titles = ribbon.GetRibbonTabTitles();
				var arr = new JArray();
				foreach (var t in titles) {
					arr.Add(t);
				}
				var addonNote = "Add-on tabs use ids like \"addon_<folderId>\" (see AddonRibbonIntegration.TabIdForAddon). Built-in examples: art list, art bg list, mesh, controlnet, paint.";
				return new JObject {
					["success"] = true,
					["tabs"] = arr,
					["note"] = addonNote,
				};
			}

			if (method == "spz.cmd.set_ribbon_tab") {
				var result = new JObject { ["success"] = false };
				string tab = @params["tab"]?.ToString();
				if (string.IsNullOrEmpty(tab)) {
					result["error"] = "Missing tab (ribbon tab title, case-insensitive)";
					return result;
				}
				var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbon == null) {
					result["error"] = "CommandRibbon_UI not available";
					return result;
				}
				bool ok = ribbon.TrySwitchRibbonTabByTitle(tab);
				result["success"] = ok;
				if (!ok) {
					result["error"] = $"No ribbon tab matched \"{tab}\". Use spz.cmd.get_ribbon_tabs to list titles.";
				}
				return result;
			}

			if (method == "spz.cmd.get_cursor_state") {
				SpzCursor_API.GetState(out CursorLockMode lm, out bool vis);
				return new JObject {
					["success"] = true,
					["lock_mode"] = lm.ToString(),
					["visible"] = vis,
				};
			}

			if (method == "spz.cmd.set_cursor_state") {
				var result = new JObject { ["success"] = false };
				bool hasLock = @params.TryGetValue("lock_mode", out JToken lockTok) && lockTok != null && lockTok.Type != JTokenType.Null;
				bool hasVis = @params.TryGetValue("visible", out JToken visTok) && visTok != null && visTok.Type != JTokenType.Null;
				if (!hasLock && !hasVis) {
					result["error"] = "Provide lock_mode and/or visible";
					return result;
				}
				SpzCursor_API.GetState(out CursorLockMode lm, out bool vis);
				try {
					if (hasLock) {
						if (!SpzCursor_API.TryParseLockMode(lockTok.ToString(), out lm)) {
							result["error"] = "Invalid lock_mode; use None, Locked, or Confined";
							return result;
						}
					}
					if (hasVis) {
						vis = visTok.ToObject<bool>();
					}
				}
				catch {
					result["error"] = "Invalid lock_mode or visible parameter";
					return result;
				}
				SpzCursor_API.Apply(lm, vis);
				result["success"] = true;
				return result;
			}

			if (method == "spz.cmd.get_ui_scale") {
				if (Application.isBatchMode) {
					return new JObject { ["success"] = false, ["error"] = "UI scale not available in batch mode" };
				}
				if (!SpzUiChromeOps.TryGetUiScale(out float mult, out float rx, out float ry)) {
					return new JObject { ["success"] = false, ["error"] = "Main CanvasScaler not available (Global_Skeleton_UI)" };
				}
				return new JObject {
					["success"] = true,
					["scale_multiplier"] = mult,
					["reference_resolution_x"] = rx,
					["reference_resolution_y"] = ry,
					["note"] = "scale_multiplier 1 = session baseline; applies to Scale With Screen Size reference resolution on skeleton canvas.",
				};
			}

			if (method == "spz.cmd.set_ui_scale") {
				var result = new JObject { ["success"] = false };
				if (Application.isBatchMode) {
					result["error"] = "UI scale not available in batch mode";
					return result;
				}
				float m = float.NaN;
				if (@params.TryGetValue("scale_multiplier", out JToken smTok) && smTok != null && smTok.Type != JTokenType.Null) {
					try {
						m = smTok.ToObject<float>();
					}
					catch {
						m = float.NaN;
					}
				}
				if (float.IsNaN(m) && @params.TryGetValue("multiplier", out JToken multTok) && multTok != null && multTok.Type != JTokenType.Null) {
					try {
						m = multTok.ToObject<float>();
					}
					catch {
						m = float.NaN;
					}
				}
				if (float.IsNaN(m) || float.IsInfinity(m)) {
					result["error"] = "Missing or invalid scale_multiplier (float, typically 0.5–2)";
					return result;
				}
				bool applied = SpzUiChromeOps.SetUiScaleMultiplier(m);
				result["success"] = applied;
				if (!applied)
					result["error"] = "Could not apply UI scale (needs Scale With Screen Size on skeleton CanvasScaler)";
				return result;
			}

			if (method == "spz.cmd.list_ui_targets") {
				var ids = SpzUiChromeOps.ListUiTargetIds();
				var arr = new JArray();
				foreach (var id in ids)
					arr.Add(id);
				return new JObject {
					["success"] = true,
					["targets"] = arr,
					["note"] = "Built-ins: global_skeleton_canvas, viewport_statusline, command_ribbon. Add SpzUiChromeRegistry to scene for more.",
				};
			}

			if (method == "spz.cmd.get_ui_target_active") {
				var result = new JObject { ["success"] = false };
				string id = @params["id"]?.ToString();
				if (string.IsNullOrEmpty(id)) {
					result["error"] = "Missing id (use list_ui_targets)";
					return result;
				}
				if (!SpzUiChromeOps.TryGetUiTargetActive(id, out bool ac)) {
					result["error"] = $"Unknown or unresolved id \"{id}\"";
					return result;
				}
				result["success"] = true;
				result["active"] = ac;
				return result;
			}

			if (method == "spz.cmd.set_ui_target_active") {
				var result = new JObject { ["success"] = false };
				string id = @params["id"]?.ToString();
				if (string.IsNullOrEmpty(id)) {
					result["error"] = "Missing id";
					return result;
				}
				if (!@params.TryGetValue("active", out JToken activeTok) || activeTok == null || activeTok.Type == JTokenType.Null) {
					result["error"] = "Missing or invalid active (boolean)";
					return result;
				}
				try {
					bool active = activeTok.ToObject<bool>();
					if (!SpzUiChromeOps.SetUiTargetActive(id, active)) {
						result["error"] = $"Unknown or unresolved id \"{id}\"";
						return result;
					}
					result["success"] = true;
				}
				catch {
					result["error"] = "Missing or invalid active (boolean)";
				}
				return result;
			}

			if (method == "spz.cmd.show_status_text") {
				var result = new JObject { ["success"] = false };
				string msg = @params["message"]?.ToString() ?? @params["text"]?.ToString() ?? "";
				bool eta = false;
				float dur = 2f;
				bool prog = false;
				try {
					if (@params.TryGetValue("text_is_eta", out JToken etaTok) && etaTok != null && etaTok.Type != JTokenType.Null)
						eta = etaTok.ToObject<bool>();
					if (@params.TryGetValue("duration", out JToken durTok) && durTok != null && durTok.Type != JTokenType.Null)
						dur = durTok.ToObject<float>();
					if (@params.TryGetValue("progress_visibility", out JToken progTok) && progTok != null && progTok.Type != JTokenType.Null)
						prog = progTok.ToObject<bool>();
					if (!SpzUiChromeOps.ShowStatusText(msg, eta, dur, prog)) {
						result["error"] = "Viewport_StatusText not available";
						return result;
					}
					result["success"] = true;
				}
				catch {
					result["error"] = "Invalid status text parameters";
				}
				return result;
			}

			if (method == "spz.cmd.get_event_system") {
				if (!SpzUiChromeOps.TryGetEventSystemEnabled(out bool en)) {
					return new JObject { ["success"] = false, ["error"] = "EventSystem.current is null" };
				}
				return new JObject { ["success"] = true, ["enabled"] = en };
			}

			if (method == "spz.cmd.set_event_system") {
				var result = new JObject { ["success"] = false };
				if (!@params.TryGetValue("enabled", out JToken enTok) || enTok == null || enTok.Type == JTokenType.Null) {
					result["error"] = "Missing or invalid enabled (boolean)";
					return result;
				}
				try {
					bool en = enTok.ToObject<bool>();
					if (!SpzUiChromeOps.SetEventSystemEnabled(en)) {
						result["error"] = "EventSystem.current is null";
						return result;
					}
					result["success"] = true;
				}
				catch {
					result["error"] = "Missing or invalid enabled (boolean)";
				}
				return result;
			}

			return null;
		}

		/// <summary>
		/// Agent bridge status / settings — no FastPath required. Socket is gated by SpzMcpSPZ (SPZ MCP).
		/// </summary>
		static JObject TryExecuteAgentBridgeCommand(string method, JObject @params) {
			if (method == "spz.cmd.agent_bridge_get_status") {
				var bridge = SPZ_Agent_Bridge.EnsureInstance();
				return bridge.BuildStatusJson();
			}

			if (method == "spz.cmd.agent_bridge_apply_settings") {
				@params ??= new JObject();
				var bridge = SPZ_Agent_Bridge.EnsureInstance();
				bool listen = @params["listen"]?.ToObject<bool>()
				              ?? @params["enabled"]?.ToObject<bool>()
				              ?? false;
				int port = @params["port"]?.ToObject<int>() ?? SPZ_Agent_Bridge.DEFAULT_PORT;
				string token = @params["token"]?.ToString() ?? "";
				return bridge.ApplySettings(listen, port, token);
			}

			return null;
		}

		/// <summary>
		/// Executes fast-path commands
		/// </summary>
		JObject ExecuteFastPathCommand(string method, JObject @params) {
			if (method == "spz.cmd.get_api_capabilities") {
				return BuildAddonApiCapabilities();
			}

			var editorLayoutResult = TryExecuteEditorLayoutCommand(method, @params);
			if (editorLayoutResult != null) {
				return editorLayoutResult;
			}

			var playerDisplayResult = TryExecutePlayerDisplayCommand(method, @params);
			if (playerDisplayResult != null) {
				return playerDisplayResult;
			}

			var uiChromeResult = TryExecuteUiChromeCommand(method, @params);
			if (uiChromeResult != null) {
				return uiChromeResult;
			}

			var agentBridgeResult = TryExecuteAgentBridgeCommand(method, @params);
			if (agentBridgeResult != null) {
				return agentBridgeResult;
			}

			if (FastPath_API.instance == null || !FastPath_API.instance.IsReady()) {
				return new JObject { ["success"] = false, ["error"] = "FastPath_API not ready" };
			}
			
			var fastPath = FastPath_API.instance;
			var result = new JObject { ["success"] = false };
			
			try {
				switch (method) {
					case "spz.cmd.set_camera_pos":
						int camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						float x = @params["x"]?.ToObject<float>() ?? 0f;
						float y = @params["y"]?.ToObject<float>() ?? 0f;
						float z = @params["z"]?.ToObject<float>() ?? 0f;
						result["success"] = fastPath.SetCameraPosition(camIdx, x, y, z);
						break;
						
					case "spz.cmd.set_camera_rot":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						x = @params["x"]?.ToObject<float>() ?? 0f;
						y = @params["y"]?.ToObject<float>() ?? 0f;
						z = @params["z"]?.ToObject<float>() ?? 0f;
						float w = @params["w"]?.ToObject<float>() ?? 1f;
						result["success"] = fastPath.SetCameraRotation(camIdx, x, y, z, w);
						break;
						
					case "spz.cmd.set_camera_fov":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						float fov = @params["fov"]?.ToObject<float>() ?? 60f;
						result["success"] = fastPath.SetCameraFOV(camIdx, fov);
						break;
						
					case "spz.cmd.get_camera_pos":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						var pos = fastPath.GetCameraPosition(camIdx);
						if (pos.HasValue) {
							result["success"] = true;
							result["x"] = pos.Value.x;
							result["y"] = pos.Value.y;
							result["z"] = pos.Value.z;
						} else {
							result["success"] = false;
						}
						break;

					case "spz.cmd.get_camera_rot":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						var camRot = fastPath.GetCameraRotation(camIdx);
						if (camRot.HasValue) {
							result["success"] = true;
							result["x"] = camRot.Value.x;
							result["y"] = camRot.Value.y;
							result["z"] = camRot.Value.z;
							result["w"] = camRot.Value.w;
						} else {
							result["success"] = false;
						}
						break;

					case "spz.cmd.get_camera_fov":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						var camFov = fastPath.GetCameraFOV(camIdx);
						if (camFov.HasValue) {
							result["success"] = true;
							result["fov"] = camFov.Value;
						} else {
							result["success"] = false;
						}
						break;

					case "spz.cmd.get_view_cameras": {
						var state = fastPath.GetViewCamerasStateJson();
						if (state == null) {
							return new JObject { ["success"] = false, ["error"] = "View cameras not available (FastPath or UserCameras_MGR)" };
						}
						state["success"] = true;
						return state;
					}

					case "spz.cmd.get_view_camera_projection": {
						int projIdx = @params["camera_index"]?.ToObject<int>() ?? -1;
						var proj = fastPath.GetViewCameraProjectionJson(projIdx);
						if (proj == null) {
							return new JObject { ["success"] = false, ["error"] = "Invalid camera_index or view camera not found" };
						}
						proj["success"] = true;
						proj["camera_index"] = projIdx;
						return proj;
					}

					case "spz.cmd.set_view_cameras_enabled_count":
						try {
							int cnt = @params["count"]?.ToObject<int>() ?? -1;
							if (cnt < 0) {
								result["error"] = "count must be >= 0 (omitted count defaults to invalid -1)";
								break;
							}
							bool okCnt = fastPath.SetViewCamerasEnabledCount(cnt);
							result["success"] = okCnt;
							if (!okCnt) {
								result["error"] = "FastPath not ready or UserCameras_MGR missing";
							}
						}
						catch {
							result["error"] = "Invalid count (integer)";
						}
						break;

					case "spz.cmd.set_view_camera_active":
						try {
							int vix = @params["camera_index"]?.ToObject<int>() ?? -1;
							if (!@params.TryGetValue("active", out JToken actTok) || actTok == null || actTok.Type == JTokenType.Null) {
								result["error"] = "Missing or invalid active (boolean)";
								break;
							}
							bool vact = actTok.ToObject<bool>();
							bool okV = fastPath.SetViewCameraActiveRpc(vix, vact);
							result["success"] = okV;
							if (!okV) {
								result["error"] = "Invalid camera_index or FastPath not ready";
							}
						}
						catch {
							result["error"] = "Invalid camera_index or active";
						}
						break;

					case "spz.cmd.set_current_view_camera":
						try {
							int curIx = @params["camera_index"]?.ToObject<int>() ?? -1;
							bool okCur = fastPath.SetCurrentViewCameraIndexRpc(curIx);
							result["success"] = okCur;
							if (!okCur) {
								result["error"] = "Invalid index or view camera not active (set_view_camera_active first)";
							}
						}
						catch {
							result["error"] = "Invalid camera_index";
						}
						break;

					case "spz.cmd.get_view_camera_povs": {
						var povState = fastPath.GetViewCameraPovsJson();
						if (povState == null) {
							return new JObject { ["success"] = false, ["error"] = "View cameras not available (FastPath or UserCameras_MGR)" };
						}
						povState["success"] = true;
						return povState;
					}

					case "spz.cmd.isolate_view_camera":
						try {
							int isoIx = @params["camera_index"]?.ToObject<int>() ?? -1;
							bool okIso = fastPath.IsolateViewCameraRpc(isoIx);
							result["success"] = okIso;
							if (!okIso) {
								result["error"] = "Invalid camera_index or FastPath not ready";
							}
						}
						catch {
							result["error"] = "Invalid camera_index";
						}
						break;

					case "spz.cmd.restore_view_camera_povs":
						try {
							var povArr = @params["povs"] as JArray;
							bool okRestore = fastPath.RestoreViewCameraPovsFromJson(povArr);
							result["success"] = okRestore;
							if (!okRestore) {
								result["error"] = "Invalid povs array or FastPath not ready";
							}
						}
						catch {
							result["error"] = "Invalid povs (array of POV objects)";
						}
						break;

					case "spz.cmd.apply_view_camera_slot_pov":
						try {
							int slotIx = @params["camera_index"]?.ToObject<int>() ?? -1;
							var slotPov = @params["pov"] as JObject;
							bool okSlot = fastPath.ApplyViewCameraSlotPovRpc(slotIx, slotPov);
							result["success"] = okSlot;
							if (!okSlot) {
								result["error"] = "Invalid camera_index, pov object, or FastPath not ready";
							}
						}
						catch {
							result["error"] = "Invalid camera_index or pov";
						}
						break;

					case "spz.cmd.set_view_camera_projection": {
						int pix = @params["camera_index"]?.ToObject<int>() ?? -1;
						bool? orthoP = null;
						if (@params.TryGetValue("orthographic", out JToken orthoTok) && orthoTok != null && orthoTok.Type != JTokenType.Null) {
							try {
								orthoP = orthoTok.ToObject<bool>();
							}
							catch {
								return new JObject { ["success"] = false, ["error"] = "Invalid orthographic (boolean)" };
							}
						}
						float? orthoSz = null;
						if (@params.TryGetValue("orthographic_size", out JToken osTok) && osTok != null && osTok.Type != JTokenType.Null) {
							try {
								orthoSz = osTok.ToObject<float>();
							}
							catch {
								return new JObject { ["success"] = false, ["error"] = "Invalid orthographic_size (float)" };
							}
						}
						float? fovP = null;
						if (@params.TryGetValue("field_of_view", out JToken fovTok) && fovTok != null && fovTok.Type != JTokenType.Null) {
							try {
								fovP = fovTok.ToObject<float>();
							}
							catch {
								return new JObject { ["success"] = false, ["error"] = "Invalid field_of_view (float)" };
							}
						}
						if (orthoP == null && !orthoSz.HasValue && !fovP.HasValue) {
							return new JObject { ["success"] = false, ["error"] = "Provide orthographic and/or orthographic_size and/or field_of_view" };
						}
						bool okP = fastPath.SetViewCameraProjectionRpc(pix, orthoP, orthoSz, fovP);
						if (!okP) {
							return new JObject { ["success"] = false, ["error"] = "Could not apply (invalid index, inactive camera, or no effective change)" };
						}
						return new JObject { ["success"] = true };
					}

					case "spz.cmd.select_mesh":
						ushort meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						result["success"] = fastPath.SelectMesh(meshId);
						break;
						
					case "spz.cmd.deselect_mesh":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						result["success"] = fastPath.DeselectMesh(meshId);
						break;
						
					case "spz.cmd.get_selected_meshes":
						var selectedIds = fastPath.GetSelectedMeshIDs();
						result["success"] = true;
						result["mesh_ids"] = JArray.FromObject(selectedIds);
						break;
						
					case "spz.cmd.get_manipulation_target_mesh_id": {
						ushort tid = fastPath.GetManipulationTargetMeshId();
						result["success"] = true;
						result["mesh_id"] = tid;
						break;
					}
					
					case "spz.cmd.select_all_meshes":
						result["success"] = fastPath.SelectAllMeshes();
						break;
						
					case "spz.cmd.deselect_all_meshes":
						result["success"] = fastPath.DeselectAllMeshes();
						break;
						
					case "spz.cmd.set_mesh_pos":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						x = @params["x"]?.ToObject<float>() ?? 0f;
						y = @params["y"]?.ToObject<float>() ?? 0f;
						z = @params["z"]?.ToObject<float>() ?? 0f;
						result["success"] = fastPath.SetMeshPosition(meshId, x, y, z);
						break;
						
					case "spz.cmd.set_mesh_rot":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						x = @params["x"]?.ToObject<float>() ?? 0f;
						y = @params["y"]?.ToObject<float>() ?? 0f;
						z = @params["z"]?.ToObject<float>() ?? 0f;
						w = @params["w"]?.ToObject<float>() ?? 1f;
						result["success"] = fastPath.SetMeshRotation(meshId, x, y, z, w);
						break;
						
					case "spz.cmd.set_mesh_scale":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						x = @params["x"]?.ToObject<float>() ?? 1f;
						y = @params["y"]?.ToObject<float>() ?? 1f;
						z = @params["z"]?.ToObject<float>() ?? 1f;
						result["success"] = fastPath.SetMeshScale(meshId, x, y, z);
						break;
						
					case "spz.cmd.set_mesh_visibility":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						JToken visTok = @params?["visible"];
						if (visTok == null) {
							result["success"] = false;
							result["error"] = "visible bool required (omitting it used to fail-open as true)";
							break;
						}
						try {
							result["success"] = fastPath.SetMeshVisibility(meshId, visTok.ToObject<bool>());
						} catch {
							result["success"] = false;
							result["error"] = "invalid visible (boolean)";
						}
						break;
						
					case "spz.cmd.get_mesh_pos":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						pos = fastPath.GetMeshPosition(meshId);
						if (pos.HasValue) {
							result["success"] = true;
							result["x"] = pos.Value.x;
							result["y"] = pos.Value.y;
							result["z"] = pos.Value.z;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_mesh_rot":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						var rot = fastPath.GetMeshRotation(meshId);
						if (rot.HasValue) {
							result["success"] = true;
							result["x"] = rot.Value.x;
							result["y"] = rot.Value.y;
							result["z"] = rot.Value.z;
							result["w"] = rot.Value.w;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_mesh_scale":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						var scale = fastPath.GetMeshScale(meshId);
						if (scale.HasValue) {
							result["success"] = true;
							result["x"] = scale.Value.x;
							result["y"] = scale.Value.y;
							result["z"] = scale.Value.z;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_mesh_bounds":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						var bounds = fastPath.GetMeshBounds(meshId);
						if (bounds.HasValue) {
							result["success"] = true;
							result["center_x"] = bounds.Value.center.x;
							result["center_y"] = bounds.Value.center.y;
							result["center_z"] = bounds.Value.center.z;
							result["size_x"] = bounds.Value.size.x;
							result["size_y"] = bounds.Value.size.y;
							result["size_z"] = bounds.Value.size.z;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_mesh_visibility":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						var vis = fastPath.GetMeshVisibility(meshId);
						if (vis.HasValue) {
							result["success"] = true;
							result["visible"] = vis.Value;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_mesh_name":
						meshId = @params["mesh_id"]?.ToObject<ushort>() ?? 0;
						var name = fastPath.GetMeshName(meshId);
						if (name != null) {
							result["success"] = true;
							result["name"] = name;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_total_mesh_count":
						result["success"] = true;
						result["count"] = fastPath.GetTotalMeshCount();
						break;
						
					case "spz.cmd.get_selected_mesh_count":
						result["success"] = true;
						result["count"] = fastPath.GetSelectedMeshCount();
						break;
						
					case "spz.cmd.get_all_mesh_ids":
						var allIds = fastPath.GetAllMeshIDs();
						result["success"] = true;
						result["mesh_ids"] = JArray.FromObject(allIds);
						break;
						
					case "spz.cmd.get_selected_meshes_bounds":
						var selBounds = fastPath.GetSelectedMeshesBounds();
						if (selBounds.HasValue) {
							result["success"] = true;
							result["center_x"] = selBounds.Value.center.x;
							result["center_y"] = selBounds.Value.center.y;
							result["center_z"] = selBounds.Value.center.z;
							result["size_x"] = selBounds.Value.size.x;
							result["size_y"] = selBounds.Value.size.y;
							result["size_z"] = selBounds.Value.size.z;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_positive_prompt":
						var posPrompt = fastPath.GetPositivePrompt();
						if (posPrompt != null) {
							result["success"] = true;
							result["prompt"] = posPrompt;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_positive_prompt":
						string prompt = @params["prompt"]?.ToString() ?? "";
						result["success"] = fastPath.SetPositivePrompt(prompt);
						break;
						
					case "spz.cmd.get_negative_prompt":
						var negPrompt = fastPath.GetNegativePrompt();
						if (negPrompt != null) {
							result["success"] = true;
							result["prompt"] = negPrompt;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_negative_prompt":
						prompt = @params["prompt"]?.ToString() ?? "";
						result["success"] = fastPath.SetNegativePrompt(prompt);
						break;
						
					case "spz.cmd.trigger_texture_generation":
						bool isBG = @params["is_background"]?.ToObject<bool>() ?? false;
						result["success"] = fastPath.TriggerTextureGeneration(isBG);
						break;
						
					case "spz.cmd.stop_generation":
						result["success"] = fastPath.StopGeneration();
						break;
						
					case "spz.cmd.is_generating":
						result["success"] = true;
						result["generating"] = fastPath.IsGenerating();
						break;
						
					case "spz.cmd.is_sd_connected":
						result["success"] = true;
						result["connected"] = fastPath.IsSDConnected();
						break;
						
					case "spz.cmd.is_3d_connected":
						result["success"] = true;
						result["connected"] = fastPath.Is3DConnected();
						break;
						
					case "spz.cmd.get_projection_camera_count":
						result["success"] = true;
						result["count"] = fastPath.GetProjectionCameraCount();
						break;
						
					case "spz.cmd.get_projection_camera_pos":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						pos = fastPath.GetProjectionCameraPosition(camIdx);
						if (pos.HasValue) {
							result["success"] = true;
							result["x"] = pos.Value.x;
							result["y"] = pos.Value.y;
							result["z"] = pos.Value.z;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_projection_camera_rot":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						rot = fastPath.GetProjectionCameraRotation(camIdx);
						if (rot.HasValue) {
							result["success"] = true;
							result["x"] = rot.Value.x;
							result["y"] = rot.Value.y;
							result["z"] = rot.Value.z;
							result["w"] = rot.Value.w;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.is_3d_generation_ready":
						result["success"] = true;
						result["ready"] = fastPath.Is3DGenerationReady();
						break;
						
					case "spz.cmd.is_3d_generation_in_progress":
						result["success"] = true;
						result["in_progress"] = fastPath.Is3DGenerationInProgress();
						break;
						
					case "spz.cmd.trigger_3d_generation":
						result["success"] = fastPath.Trigger3DGeneration();
						break;
						
					case "spz.cmd.export_3d_with_textures":
						result["success"] = fastPath.Export3DWithTextures();
						break;

					case "spz.cmd.stream_mesh_to_blender": {
						string streamHost = @params["host"]?.ToString() ?? "127.0.0.1";
						int streamPort = @params["port"]?.ToObject<int>() ?? SpzGoMeshStream.DefaultPort;
						string codec = @params["codec"]?.ToString() ?? "gzip";
						bool useGzip = !string.Equals(codec, "none", StringComparison.OrdinalIgnoreCase);
						bool streamOk = fastPath.StreamCurrentModelToBlender(
							streamHost, streamPort, useGzip, out int streamedMeshes, out string streamError);
						result["success"] = streamOk;
						result["protocol_version"] = SpzGoMeshStream.ProtocolVersion;
						result["mesh_count"] = streamedMeshes;
						result["codec"] = useGzip ? "gzip" : "none";
						if (!streamOk)
							result["error"] = streamError ?? "mesh stream failed";
						break;
					}
					
					case "spz.cmd.import_3d_model": {
						string imPath = @params["filepath"]?.ToString() ?? "";
						bool imOk = fastPath.Import3DModelFromFile(imPath);
						result["success"] = imOk;
						if (!imOk) {
							result["error"] = "import failed (invalid path, file missing, or import busy)";
						}
						break;
					}
					
					case "spz.cmd.export_3d_with_textures_to_path": {
						string exPath = @params["mesh_filepath"]?.ToString() ?? "";
						string hostId = @params["host_id"]?.ToString()
							?? @params["hostId"]?.ToString();
						bool exOk = fastPath.Export3DWithTexturesToPath(exPath, hostId);
						result["success"] = exOk;
						if (!exOk) {
							result["error"] = "export to path failed (invalid path, Save_MGR not ready, or could not create directory)";
						}
						break;
					}
						
					case "spz.cmd.export_projection_textures":
						JToken dilateTok = @params?["is_dilate"];
						if (dilateTok == null) {
							result["success"] = false;
							result["error"] = "is_dilate bool required (omitting it used to fail-open as true)";
							break;
						}
						bool dilate;
						try {
							dilate = dilateTok.ToObject<bool>();
						} catch {
							result["success"] = false;
							result["error"] = "invalid is_dilate (boolean)";
							break;
						}
						result["success"] = fastPath.ExportProjectionTextures(dilate);
						break;
						
					case "spz.cmd.export_view_textures":
						result["success"] = fastPath.ExportViewTextures();
						break;
						
					case "spz.cmd.get_workflow_mode":
						var mode = fastPath.GetWorkflowMode();
						if (mode != null) {
							result["success"] = true;
							result["mode"] = mode;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_workflow_mode":
						string modeStr = @params["mode"]?.ToString() ?? "";
						result["success"] = fastPath.SetWorkflowMode(modeStr);
						break;
						
					case "spz.cmd.get_controlnet_unit_count":
						result["success"] = true;
						result["count"] = fastPath.GetControlNetUnitCount();
						break;
						
					case "spz.cmd.get_active_controlnet_unit_count":
						result["success"] = true;
						result["count"] = fastPath.GetActiveControlNetUnitCount();
						break;
						
					case "spz.cmd.set_skybox_color":
						JToken isTopTok = @params?["is_top"];
						if (isTopTok == null) {
							result["success"] = false;
							result["error"] = "is_top bool required (omitting it used to fail-open as true)";
							break;
						}
						bool isTop;
						try {
							isTop = isTopTok.ToObject<bool>();
						} catch {
							result["success"] = false;
							result["error"] = "invalid is_top (boolean)";
							break;
						}
						float r = @params["r"]?.ToObject<float>() ?? 0f;
						float g = @params["g"]?.ToObject<float>() ?? 0f;
						float b = @params["b"]?.ToObject<float>() ?? 0f;
						float a = @params["a"]?.ToObject<float>() ?? 1f;
						result["success"] = fastPath.SetSkyboxColor(isTop, r, g, b, a);
						break;
						
					case "spz.cmd.is_skybox_gradient_clear":
						result["success"] = true;
						result["is_clear"] = fastPath.IsSkyboxGradientClear();
						break;
						
					case "spz.cmd.get_skybox_top_color":
						var topColor = fastPath.GetSkyboxTopColor();
						if (topColor.HasValue) {
							result["success"] = true;
							result["r"] = topColor.Value.r;
							result["g"] = topColor.Value.g;
							result["b"] = topColor.Value.b;
							result["a"] = topColor.Value.a;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_skybox_bottom_color":
						var bottomColor = fastPath.GetSkyboxBottomColor();
						if (bottomColor.HasValue) {
							result["success"] = true;
							result["r"] = bottomColor.Value.r;
							result["g"] = bottomColor.Value.g;
							result["b"] = bottomColor.Value.b;
							result["a"] = bottomColor.Value.a;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_controlnet_unit_enabled":
						int unitIdx = @params["unit_index"]?.ToObject<int>() ?? 0;
						bool enabled = @params["enabled"]?.ToObject<bool>() ?? false;
						result["success"] = fastPath.SetControlNetUnitEnabled(unitIdx, enabled);
						break;
						
					case "spz.cmd.get_controlnet_unit_enabled":
						unitIdx = @params["unit_index"]?.ToObject<int>() ?? 0;
						var isEnabled = fastPath.GetControlNetUnitEnabled(unitIdx);
						if (isEnabled.HasValue) {
							result["success"] = true;
							result["enabled"] = isEnabled.Value;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_controlnet_unit_weight":
						unitIdx = @params["unit_index"]?.ToObject<int>() ?? 0;
						float weight = @params["weight"]?.ToObject<float>() ?? 1f;
						result["success"] = fastPath.SetControlNetUnitWeight(unitIdx, weight);
						break;
						
					case "spz.cmd.get_controlnet_unit_weight":
						unitIdx = @params["unit_index"]?.ToObject<int>() ?? 0;
						var unitWeight = fastPath.GetControlNetUnitWeight(unitIdx);
						if (unitWeight.HasValue) {
							result["success"] = true;
							result["weight"] = unitWeight.Value;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_controlnet_unit_model":
						unitIdx = @params["unit_index"]?.ToObject<int>() ?? 0;
						string modelName = fastPath.GetControlNetUnitModel(unitIdx);
						if (modelName != null) {
							result["success"] = true;
							result["model"] = modelName;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_mesh_positions":
						var meshIdsJson = @params["mesh_ids"] as JArray;
						var positionsJson = @params["positions"] as JArray;
						if (meshIdsJson != null && positionsJson != null) {
							var meshIdsList = new List<ushort>();
							var positionsList = new List<Vector3>();
							
							foreach (var id in meshIdsJson) {
								meshIdsList.Add(id.ToObject<ushort>());
							}
							
							foreach (var posItem in positionsJson) {
								var posObj = posItem as JObject;
								if (posObj != null) {
									positionsList.Add(new Vector3(
										posObj["x"]?.ToObject<float>() ?? 0f,
										posObj["y"]?.ToObject<float>() ?? 0f,
										posObj["z"]?.ToObject<float>() ?? 0f
									));
								}
							}
							
							int successCountPos = fastPath.SetMeshPositions(meshIdsList, positionsList);
							result["success"] = successCountPos > 0;
							result["count"] = successCountPos;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_mesh_rotations":
						meshIdsJson = @params["mesh_ids"] as JArray;
						var rotationsJson = @params["rotations"] as JArray;
						if (meshIdsJson != null && rotationsJson != null) {
							var meshIdsRot = new List<ushort>();
							var rotationsList = new List<Quaternion>();
							
							foreach (var id in meshIdsJson) {
								meshIdsRot.Add(id.ToObject<ushort>());
							}
							
							foreach (var rotItem in rotationsJson) {
								var rotObj = rotItem as JObject;
								if (rotObj != null) {
									rotationsList.Add(new Quaternion(
										rotObj["x"]?.ToObject<float>() ?? 0f,
										rotObj["y"]?.ToObject<float>() ?? 0f,
										rotObj["z"]?.ToObject<float>() ?? 0f,
										rotObj["w"]?.ToObject<float>() ?? 1f
									));
								}
							}
							
							int successCountRot = fastPath.SetMeshRotations(meshIdsRot, rotationsList);
							result["success"] = successCountRot > 0;
							result["count"] = successCountRot;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_mesh_scales":
						meshIdsJson = @params["mesh_ids"] as JArray;
						var scalesJson = @params["scales"] as JArray;
						if (meshIdsJson != null && scalesJson != null) {
							var meshIdsScale = new List<ushort>();
							var scalesList = new List<Vector3>();
							
							foreach (var id in meshIdsJson) {
								meshIdsScale.Add(id.ToObject<ushort>());
							}
							
							foreach (var scaleItem in scalesJson) {
								var scaleObj = scaleItem as JObject;
								if (scaleObj != null) {
									scalesList.Add(new Vector3(
										scaleObj["x"]?.ToObject<float>() ?? 1f,
										scaleObj["y"]?.ToObject<float>() ?? 1f,
										scaleObj["z"]?.ToObject<float>() ?? 1f
									));
								}
							}
							
							int successCountScale = fastPath.SetMeshScales(meshIdsScale, scalesList);
							result["success"] = successCountScale > 0;
							result["count"] = successCountScale;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.save_project":
						result["success"] = fastPath.SaveProject(@params?["filepath"]?.ToString());
						break;
						
					case "spz.cmd.load_project":
						result["success"] = fastPath.LoadProject(@params?["filepath"]?.ToString());
						break;
						
					case "spz.cmd.get_project_path":
						string projectPath = fastPath.GetProjectPath();
						if (projectPath != null) {
							result["success"] = true;
							result["path"] = projectPath;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_project_version":
						string version = fastPath.GetProjectVersion();
						if (version != null) {
							result["success"] = true;
							result["version"] = version;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.get_project_data_dir":
						string dataDir = fastPath.GetProjectDataDirOrSession();
						if (dataDir != null) {
							result["success"] = true;
							result["data_dir"] = dataDir;
							result["data_dir_is_session"] = fastPath.IsSpzGoSessionDataDir();
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.is_project_operation_in_progress":
						result["success"] = true;
						result["in_progress"] = fastPath.IsProjectOperationInProgress();
						break;
						
					case "spz.cmd.set_projection_camera_pos":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						x = @params["x"]?.ToObject<float>() ?? 0f;
						y = @params["y"]?.ToObject<float>() ?? 0f;
						z = @params["z"]?.ToObject<float>() ?? 0f;
						result["success"] = fastPath.SetProjectionCameraPosition(camIdx, x, y, z);
						break;
						
					case "spz.cmd.set_projection_camera_rot":
						camIdx = @params["camera_index"]?.ToObject<int>() ?? 0;
						x = @params["x"]?.ToObject<float>() ?? 0f;
						y = @params["y"]?.ToObject<float>() ?? 0f;
						z = @params["z"]?.ToObject<float>() ?? 0f;
						w = @params["w"]?.ToObject<float>() ?? 1f;
						result["success"] = fastPath.SetProjectionCameraRotation(camIdx, x, y, z, w);
						break;
						
					case "spz.cmd.get_all_camera_positions":
						var allPositions = fastPath.GetAllCameraPositions();
						result["success"] = true;
						var posArray = new JArray();
						foreach (var posItem in allPositions) {
							posArray.Add(new JObject {
								["x"] = posItem.x,
								["y"] = posItem.y,
								["z"] = posItem.z
							});
						}
						result["positions"] = posArray;
						break;
						
					case "spz.cmd.get_all_camera_rotations":
						var allRotations = fastPath.GetAllCameraRotations();
						result["success"] = true;
						var rotArray = new JArray();
						foreach (var rotation in allRotations) {
							rotArray.Add(new JObject {
								["x"] = rotation.x,
								["y"] = rotation.y,
								["z"] = rotation.z,
								["w"] = rotation.w
							});
						}
						result["rotations"] = rotArray;
						break;
						
					case "spz.cmd.get_all_camera_fovs":
						var allFOVs = fastPath.GetAllCameraFOVs();
						result["success"] = true;
						result["fovs"] = JArray.FromObject(allFOVs);
						break;

					case "spz.cmd.get_brush_settings":
						fastPath.PopulateBrushSettings(result);
						break;

					case "spz.cmd.get_paint_layers":
						fastPath.PopulatePaintLayers(result);
						break;

					case "spz.cmd.set_brush_size":
						result["success"] = fastPath.SetBrushSize01(@params["value"]?.ToObject<float>() ?? @params["size01"]?.ToObject<float>() ?? -1f);
						break;

					case "spz.cmd.set_brush_spacing":
						result["success"] = fastPath.SetBrushSpacing01(@params["value"]?.ToObject<float>() ?? @params["spacing01"]?.ToObject<float>() ?? -1f);
						break;

					case "spz.cmd.set_brush_angle":
						result["success"] = fastPath.SetBrushAngleDeg(@params["value"]?.ToObject<float>() ?? @params["angle_deg"]?.ToObject<float>() ?? float.NaN);
						break;

					case "spz.cmd.set_brush_roundness":
						result["success"] = fastPath.SetBrushRoundness01(@params["value"]?.ToObject<float>() ?? @params["roundness01"]?.ToObject<float>() ?? -1f);
						break;

					case "spz.cmd.set_brush_opacity":
						result["success"] = fastPath.SetBrushOpacity01(@params["value"]?.ToObject<float>() ?? @params["opacity01"]?.ToObject<float>() ?? -1f);
						break;

					case "spz.cmd.set_brush_stamp_index":
						result["success"] = fastPath.SetBrushStampIndex(@params["index"]?.ToObject<int>() ?? -1);
						break;

					case "spz.cmd.set_active_paint_layer":
						result["success"] = fastPath.SetActivePaintLayerIndex(@params["index"]?.ToObject<int>() ?? -1);
						break;

					case "spz.cmd.get_sd_workflow_options":
						fastPath.PopulateSdWorkflowOptions(result);
						break;

					case "spz.cmd.set_sd_denoising_strength":
						result["success"] = fastPath.SetSdDenoisingStrength(
							@params["value"]?.ToObject<float>() ?? float.NaN);
						break;

					case "spz.cmd.set_sd_mask_blur":
						result["success"] = fastPath.SetSdMaskBlurStep(
							@params["value"]?.ToObject<float>() ?? float.NaN);
						break;

				case "spz.cmd.set_sd_inpainting_mask_invert":
					if (TryReadBoolParam(@params, out bool invOn, out string invErr))
						result["success"] = fastPath.SetSdInpaintingMaskInvert(invOn);
					else
						result["error"] = invErr;
					break;

				case "spz.cmd.set_sd_soft_inpaint":
					if (TryReadBoolParam(@params, out bool softOn, out string softErr))
						result["success"] = fastPath.SetSdSoftInpaint(softOn);
					else
						result["error"] = softErr;
					break;

				case "spz.cmd.set_sd_strict_isolation_flip":
					if (TryReadBoolParam(@params, out bool flipOn, out string flipErr))
						result["success"] = fastPath.SetSdStrictIsolationFlip(flipOn);
					else
						result["error"] = flipErr;
					break;

				case "spz.cmd.set_sd_tileable_inpaint":
					if (TryReadBoolParam(@params, out bool tileOn, out string tileErr))
						result["success"] = fastPath.SetSdTileableInpaint(tileOn);
					else
						result["error"] = tileErr;
					break;

				case "spz.cmd.set_sd_ignore_depth_or_normals":
					if (TryReadBoolParam(@params, out bool ignOn, out string ignErr))
						result["success"] = fastPath.SetSdIgnoreDepthOrNormals(ignOn);
					else
						result["error"] = ignErr;
					break;

					case "spz.cmd.get_addon_context":
						fastPath.PopulateAddonContext(result);
						break;
						
					default:
						result["success"] = false;
						result["error"] = $"Unknown command: {method}";
						break;
				}
			}
			catch (Exception e) {
				result["success"] = false;
				result["error"] = e.Message;
			}
			
			return result;
		}

		/// <summary>Picks a workflow ribbon host when <see cref="SD_WorkflowOptionsRibbon_UI.instance"/> is null or inactive (load order / toolchest).</summary>
		static bool WorkflowHostIsUnderViewportInnerLeftRibbon(SD_WorkflowOptionsRibbon_UI host) {
			if (host == null) {
				return false;
			}
			var mv = MainViewport_UI.instance;
			if (mv == null || mv.innerLeftRibbonRect == null) {
				return false;
			}
			return host.transform.IsChildOf(mv.innerLeftRibbonRect);
		}

		static bool WorkflowHostIsUnderViewportInnerRightRibbon(SD_WorkflowOptionsRibbon_UI host) {
			if (host == null) {
				return false;
			}
			var mv = MainViewport_UI.instance;
			if (mv == null || mv.innerRightRibbonRect == null) {
				return false;
			}
			return host.transform.IsChildOf(mv.innerRightRibbonRect);
		}

		/// <summary>Inner-left duplicates Gen Art; inner-right is hidden in full view (<see cref="FullView_OuterPanel_Chrome_Binder"/>). Prefer hosts under Paint / right column instead.</summary>
		static bool WorkflowHostIsUnderViewportInnerEdgeRibbonStrip(SD_WorkflowOptionsRibbon_UI host) {
			return WorkflowHostIsUnderViewportInnerLeftRibbon(host) || WorkflowHostIsUnderViewportInnerRightRibbon(host);
		}

		/// <summary>Pick <see cref="SD_WorkflowOptionsRibbon_UI"/> to host the dock component (logic state). Visual row is parented from <see cref="RibbonViewportFullViewOnScreen_Toggle_UI"/> to the viewport Gen Art column — not to a command-ribbon tab body.</summary>
		static SD_WorkflowOptionsRibbon_UI PickWorkflowRibbonHostForFullViewAttach() {
			var all = UnityEngine.Object.FindObjectsByType<SD_WorkflowOptionsRibbon_UI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			var inst = SD_WorkflowOptionsRibbon_UI.instance;
			for (int i = 0; i < all.Length; i++) {
				var c = all[i];
				if (c != null && c.gameObject.activeInHierarchy && !WorkflowHostIsUnderViewportInnerEdgeRibbonStrip(c)) {
					return c;
				}
			}
			if (inst != null && inst.gameObject.activeInHierarchy && !WorkflowHostIsUnderViewportInnerEdgeRibbonStrip(inst)) {
				return inst;
			}
			if (inst != null && !WorkflowHostIsUnderViewportInnerEdgeRibbonStrip(inst)) {
				return inst;
			}
			for (int i = 0; i < all.Length; i++) {
				var c = all[i];
				if (c != null && !WorkflowHostIsUnderViewportInnerEdgeRibbonStrip(c)) {
					return c;
				}
			}
			// Legacy fallbacks when every host still lives under an edge strip (should be rare).
			for (int i = 0; i < all.Length; i++) {
				var c = all[i];
				if (c != null && c.gameObject.activeInHierarchy && !WorkflowHostIsUnderViewportInnerLeftRibbon(c)) {
					return c;
				}
			}
			if (inst != null && inst.gameObject.activeInHierarchy && !WorkflowHostIsUnderViewportInnerLeftRibbon(inst)) {
				return inst;
			}
			if (inst != null && !WorkflowHostIsUnderViewportInnerLeftRibbon(inst)) {
				return inst;
			}
			for (int i = 0; i < all.Length; i++) {
				var c = all[i];
				if (c != null && !WorkflowHostIsUnderViewportInnerLeftRibbon(c)) {
					return c;
				}
			}
			if (inst != null) {
				return inst;
			}
			for (int i = 0; i < all.Length; i++) {
				if (all[i] != null) {
					return all[i];
				}
			}
			return null;
		}

		/// <summary>
		/// JSON-RPC <c>spz.ui.attach_viewport_fullview_toggle</c>: docks FULL/SCREEN above the viewport Gen Art control (not on the command-ribbon tab strip; does not switch tabs).
		/// Params (optional): <c>button_label</c> (string), <c>command</c> (string, default <c>viewport_fullview_toggle</c> → <see cref="RibbonDock_CommandBridge"/>).
		/// </summary>
		static JObject TryExecuteAttachViewportFullViewToggle(JObject @params) {
			var r = new JObject { ["success"] = false };
			try {
				var spec = RibbonDock_ButtonSpec.FromRpc(@params);
				// Prefer the viewport Gen Art strip. SD_WorkflowOptionsRibbon_UI often disables when the user
				// opens Add-on Manager (or after Generate UI churn); OnDisable stops CoBuildWhenGenArtReady
				// and PickWorkflowRibbonHost still returns that inactive host — so enable-after-Generate
				// never reaches TryEnsureOnGenerateButtonsStrip and FULL/SRN never appears.
				if (RibbonViewportFullViewOnScreen_Toggle_UI.TryEnsureOnGenerateButtonsStrip(spec)) {
					bool visible = RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyVisibleBuiltDock();
					bool inFlight = RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyDockBuildInFlight();
					// Kicked ≠ visible: Python register() must not treat a pending CoBuild as attached.
					r["success"] = visible || inFlight;
					r["visible"] = visible;
					r["building"] = inFlight;
					r["host"] = "GenerateButtons_Main_UI";
					return r;
				}
				var host = PickWorkflowRibbonHostForFullViewAttach();
				if (host != null) {
					RibbonViewportFullViewOnScreen_Toggle_UI.EnsureCreated(host, spec);
					bool visible = RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyVisibleBuiltDock();
					bool inFlight = RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyDockBuildInFlight();
					r["success"] = visible || inFlight;
					r["visible"] = visible;
					r["building"] = inFlight;
					r["host"] = "SD_WorkflowOptionsRibbon_UI";
					return r;
				}
				r["error"] = "SD_WorkflowOptionsRibbon_UI and GenerateButtons_Main_UI not in scene; cannot mount viewport full-view toggle.";
			}
			catch (Exception e) {
				r["error"] = e.Message;
			}
			return r;
		}

		/// <summary>
		/// Same as JSON-RPC <c>spz.ui.attach_viewport_fullview_toggle</c>, for <see cref="Addon_MGR"/> when HTTP <c>load_addon</c> is off
		/// (Python <c>register()</c> never runs) or as a main-thread retry until the SD workflow ribbon exists.
		/// Call from the Unity main thread only.
		/// </summary>
		public static JObject TryAttachViewportFullViewToggleFromCore(JObject @params) {
			return TryExecuteAttachViewportFullViewToggle(@params ?? new JObject());
		}

		/// <summary>
		/// JSON-RPC <c>spz.ui.attach_viewport_axis_gizmo</c>: mounts the orientation gizmo (axis balls + lantern
		/// overview button) in the top-right of the 3D view. Not a command-ribbon tab; see
		/// <see cref="ViewportAxisGizmo_AddonBridge"/> for parameters.
		/// </summary>
		static JObject TryExecuteAttachViewportAxisGizmo(JObject @params) {
			return ViewportAxisGizmo_AddonBridge.TryAttachFromCore(@params ?? new JObject());
		}
		
		/// <summary>
		/// Executes UI commands (delegates to AddonUI_MGR)
		/// </summary>
		JObject ExecuteUICommand(string method, JObject @params) {
			UnityEngine.Debug.Log($"[Addon_SocketServer] Executing UI Command: {method} with params: {@params?.ToString(Formatting.None)}");
			if (string.Equals(method, "spz.ui.attach_viewport_fullview_toggle", StringComparison.Ordinal)) {
				return TryExecuteAttachViewportFullViewToggle(@params ?? new JObject());
			}
			if (string.Equals(method, "spz.ui.attach_viewport_axis_gizmo", StringComparison.Ordinal)) {
				return TryExecuteAttachViewportAxisGizmo(@params ?? new JObject());
			}
			if (string.Equals(method, "spz.ui.get_theme", StringComparison.Ordinal)) {
				return SpzUiThemeOps.GetThemeResult();
			}
			if (string.Equals(method, "spz.ui.list_themes", StringComparison.Ordinal)) {
				return SpzUiThemeOps.ListThemesResult();
			}
			if (string.Equals(method, "spz.ui.register_theme", StringComparison.Ordinal)) {
				var themeResult = new JObject { ["success"] = false };
				string themeId = @params?["theme_id"]?.ToString() ?? "";
				string label = @params?["label"]?.ToString();
				string owner = @params?["owner"]?.ToString();
				var tokens = @params?["tokens"] as JObject;
				if (SpzUiThemeOps.TryRegisterTheme(themeId, label, tokens, owner, out string error)) {
					themeResult["success"] = true;
					themeResult["theme_id"] = themeId.Trim();
					return themeResult;
				}
				themeResult["error"] = error;
				return themeResult;
			}
			if (string.Equals(method, "spz.ui.unregister_theme", StringComparison.Ordinal)) {
				var themeResult = new JObject { ["success"] = false };
				string themeId = @params?["theme_id"]?.ToString() ?? "";
				if (SpzUiThemeOps.TryUnregisterTheme(themeId, out string error)) {
					themeResult["success"] = true;
					themeResult["theme_id"] = themeId.Trim();
					return themeResult;
				}
				themeResult["error"] = error;
				return themeResult;
			}
			if (string.Equals(method, "spz.ui.apply_theme", StringComparison.Ordinal)) {
				var themeResult = new JObject { ["success"] = false };
				string themeId = @params?["theme_id"]?.ToString() ?? "";
				var tokens = @params?["tokens"] as JObject;
				string mode = @params?["mode"]?.ToString() ?? "replace";
				if (SpzUiThemeOps.TryApplyTheme(themeId, tokens, mode, out string error)) {
					return SpzUiThemeOps.GetThemeResult();
				}
				themeResult["error"] = error;
				return themeResult;
			}
			if (string.Equals(method, "spz.ui.reset_theme", StringComparison.Ordinal)) {
				SpzUiThemeOps.ResetTheme();
				return SpzUiThemeOps.GetThemeResult();
			}
			if (string.Equals(method, "spz.ui.list_line_icons", StringComparison.Ordinal)) {
				return new JObject {
					["success"] = true,
					["icons"] = SpzUiThemeOps.ListLineIconNames(),
				};
			}
			if (string.Equals(method, "spz.ui.set_line_icon", StringComparison.Ordinal)) {
				var iconResult = new JObject { ["success"] = false };
				string tab = @params?["tab"]?.ToString() ?? @params?["target"]?.ToString() ?? "";
				string icon = @params?["icon"]?.ToString() ?? "";
				if (SpzUiThemeOps.TrySetStripTabLineIcon(tab, icon, out string iconError)) {
					iconResult["success"] = true;
					iconResult["tab"] = tab;
					iconResult["icon"] = icon;
				} else {
					iconResult["error"] = iconError ?? "set_line_icon failed";
				}
				return iconResult;
			}
			if (AddonUI_MGR.instance == null) {
				return new JObject { ["success"] = false, ["error"] = "AddonUI_MGR not available" };
			}
			
			var uiMgr = AddonUI_MGR.instance;
			var result = new JObject { ["success"] = false };
			
			try {
				switch (method) {
					case "spz.ui.create_panel":
						string addonId = @params["addon_id"]?.ToString() ?? "";
						string title = @params["title"]?.ToString() ?? "Add-on Panel";
						UnityEngine.Debug.Log($"[Addon_SocketServer] create_panel from Python: addonId={addonId}, title={title}");
						if (!Addon_MGR.IsAddonEnabledStatic(addonId)) {
							result["error"] = $"Add-on '{addonId}' is disabled; enable it in Add-on Manager before create_panel";
							UnityEngine.Debug.LogWarning(
								$"[Addon_SocketServer] create_panel blocked — add-on '{addonId}' is not enabled.");
							break;
						}
						string panelId = uiMgr.CreatePanel(addonId, title);
						if (panelId != null) {
							bool parked = uiMgr.IsPanelParkedOffRibbon(panelId);
							result["success"] = true;
							result["panel_id"] = panelId;
							result["parked"] = parked;
							result["visible"] = !parked;
							if (parked) {
								result["note"] =
									"Panel created off-ribbon (parking). Enable Show in Command Ribbon or wait for ribbon migrate.";
								UnityEngine.Debug.Log(
									$"[Addon_SocketServer] create_panel OK but parked (not visible): panel_id={panelId}");
							} else {
								UnityEngine.Debug.Log($"[Addon_SocketServer] create_panel OK: panel_id={panelId}");
							}
						} else {
							result["error"] = "Failed to create panel";
							UnityEngine.Debug.LogWarning($"[Addon_SocketServer] create_panel FAILED for {addonId} (CreatePanel returned null)");
						}
						break;
						
					case "spz.ui.add_button":
						addonId = @params["addon_id"]?.ToString() ?? "";
						string panelIdParam = @params["panel_id"]?.ToString() ?? "";
						string label = @params["label"]?.ToString() ?? "Button";
						string callbackName = @params["callback"]?.ToString() ?? "";
						string buttonId = uiMgr.AddButton(addonId, panelIdParam, label, callbackName);
						if (buttonId != null) {
							result["success"] = true;
							result["button_id"] = buttonId;
						} else {
							result["error"] = "Failed to create button";
						}
						break;

					case "spz.ui.add_toggle":
						addonId = @params["addon_id"]?.ToString() ?? "";
						panelIdParam = @params["panel_id"]?.ToString() ?? "";
						label = @params["label"]?.ToString() ?? "Toggle";
						bool defaultOn = @params["default"]?.ToObject<bool>() ?? false;
						callbackName = @params["callback"]?.ToString();
						string toggleId = uiMgr.AddToggle(addonId, panelIdParam, label, defaultOn, callbackName);
						if (toggleId != null) {
							result["success"] = true;
							result["element_id"] = toggleId;
						} else {
							result["error"] = "Failed to create toggle";
						}
						break;
						
					case "spz.ui.add_slider":
						addonId = @params["addon_id"]?.ToString() ?? "";
						panelIdParam = @params["panel_id"]?.ToString() ?? "";
						label = @params["label"]?.ToString() ?? "Slider";
						float min = @params["min"]?.ToObject<float>() ?? 0f;
						float max = @params["max"]?.ToObject<float>() ?? 100f;
						float defaultValue = @params["default"]?.ToObject<float>() ?? 50f;
						string sliderId = uiMgr.AddSlider(addonId, panelIdParam, label, min, max, defaultValue);
						if (sliderId != null) {
							result["success"] = true;
							result["element_id"] = sliderId;
						} else {
							result["error"] = "Failed to create slider";
						}
						break;
						
					case "spz.ui.add_text_input":
						addonId = @params["addon_id"]?.ToString() ?? "";
						panelIdParam = @params["panel_id"]?.ToString() ?? "";
						label = @params["label"]?.ToString() ?? "Text Input";
						string defaultValueStr = @params["default"]?.ToString() ?? "";
						string textInputId = uiMgr.AddTextInput(addonId, panelIdParam, label, defaultValueStr);
						if (textInputId != null) {
							result["success"] = true;
							result["element_id"] = textInputId;
						} else {
							result["error"] = "Failed to create text input";
						}
						break;
						
					case "spz.ui.add_dropdown":
						addonId = @params["addon_id"]?.ToString() ?? "";
						panelIdParam = @params["panel_id"]?.ToString() ?? "";
						label = @params["label"]?.ToString() ?? "Dropdown";
						var optionsJson = @params["options"] as JArray;
						var options = new List<string>();
						if (optionsJson != null) {
							foreach (var opt in optionsJson) {
								options.Add(opt.ToString());
							}
						}
						int defaultIndex = @params["default"]?.ToObject<int>() ?? 0;
						string dropdownId = uiMgr.AddDropdown(addonId, panelIdParam, label, options, defaultIndex);
						if (dropdownId != null) {
							result["success"] = true;
							result["element_id"] = dropdownId;
						} else {
							result["error"] = "Failed to create dropdown";
						}
						break;
						
					case "spz.ui.add_foldout":
						addonId = @params["addon_id"]?.ToString() ?? "";
						panelIdParam = @params["panel_id"]?.ToString() ?? "";
						label = @params["label"]?.ToString() ?? "Settings";
						bool startOpen = @params["open"]?.ToObject<bool>() ?? false;
						// The CONTENT id, so the caller adds widgets straight into the drop-tab.
						string foldoutId = uiMgr.AddFoldout(addonId, panelIdParam, label, startOpen);
						if (foldoutId != null) {
							result["success"] = true;
							result["element_id"] = foldoutId;
						} else {
							result["error"] = "Failed to create foldout";
						}
						break;

					case "spz.ui.add_host_sections": {
						addonId = @params["addon_id"]?.ToString() ?? "";
						panelIdParam = @params["panel_id"]?.ToString() ?? "";
						string onlyHost = @params["host_id"]?.ToString();
						var built = new JArray();
						GameObject panelGo = uiMgr.FindUIElementPublic(panelIdParam);
						foreach (var host in SpzGoHosts.All) {
							if (!string.IsNullOrEmpty(onlyHost)
							    && !string.Equals(host.Id, onlyHost, StringComparison.OrdinalIgnoreCase))
								continue;
							// Idempotent: a half-built panel (Blender only) must gain the rest without
							// stacking duplicate HostSection_blender on reload.
							if (panelGo != null
							    && uiMgr.PanelHasSpzGoHostSection(panelGo, host.Id)) {
								built.Add(host.Id);
								continue;
							}
							string sectionId = uiMgr.AddHostSection(addonId, panelIdParam, host.Id);
							if (sectionId != null)
								built.Add(host.Id);
						}
						// Partial success is still a failure to build the shell — say so rather than
						// letting the add-on report a load that produced half a panel.
						int wanted = string.IsNullOrEmpty(onlyHost) ? SpzGoHosts.All.Count : 1;
						result["success"] = built.Count == wanted;
						result["host_ids"] = built;
						if (built.Count != wanted)
							result["error"] = $"Built {built.Count} of {wanted} host sections";
						else if (string.IsNullOrEmpty(onlyHost))
							uiMgr.EnsureSpzGoHostSectionsComplete();
						break;
					}

					case "spz.ui.get_value":
						string elementId = @params["element_id"]?.ToString() ?? "";
						object value = uiMgr.GetUIElementValue(elementId);
						if (value != null) {
							result["success"] = true;
							result["value"] = JToken.FromObject(value);
						} else {
							result["success"] = false;
							result["error"] = "Element not found or has no value";
						}
						break;
						
					case "spz.ui.set_value":
						elementId = @params["element_id"]?.ToString() ?? "";
						var valueToken = @params["value"];
						object valueObj = null;
						if (valueToken != null) {
							// Keep JSON types distinct: int for dropdowns, bool for toggles, float for sliders.
							if (valueToken.Type == JTokenType.Boolean)
								valueObj = valueToken.ToObject<bool>();
							else if (valueToken.Type == JTokenType.Integer)
								valueObj = valueToken.ToObject<int>();
							else if (valueToken.Type == JTokenType.Float)
								valueObj = valueToken.ToObject<float>();
							else if (valueToken.Type == JTokenType.String)
								valueObj = valueToken.ToString();
							else
								valueObj = valueToken.ToObject<object>();
						}
						result["success"] = uiMgr.SetUIElementValue(elementId, valueObj);
						if (!result["success"].ToObject<bool>()) {
							result["error"] = "Failed to set value";
						}
						break;
						
					default:
						result["error"] = $"Unknown UI command: {method}";
						break;
				}
			}
			catch (Exception e) {
				result["success"] = false;
				result["error"] = e.Message;
			}
			
			return result;
		}
		
		/// <summary>
		/// Creates a JSON-RPC error response
		/// </summary>
		JObject CreateErrorResponse(int code, string message, JToken id) {
			return new JObject {
				["jsonrpc"] = "2.0",
				["error"] = new JObject {
					["code"] = code,
					["message"] = message
				},
				["id"] = id
			};
		}
		
		/// <summary>
		/// Processes queued commands on the main thread (called from Update)
		/// </summary>
		void Update() {
			int processed = 0;
			while (processed < MAX_COMMANDS_PER_FRAME && _mainThreadQueue.TryDequeue(out Action action)) {
				try {
					action();
				}
				catch (Exception e) {
					UnityEngine.Debug.LogError($"[Addon_SocketServer] Error executing queued command: {e.Message}");
				}
				processed++;
			}
			
			// Periodic cleanup of stale pending responses (older than 10 seconds)
			// This prevents memory leaks if responses are never retrieved
			if (Time.frameCount % 300 == 0) { // Every ~5 seconds at 60fps
				CleanupStaleResponses();
			}
		}
		
		/// <summary>
		/// Cleans up stale pending responses to prevent memory leaks
		/// </summary>
		void CleanupStaleResponses() {
			// Note: This is a simple cleanup. In production, you might want to track timestamps
			// For now, if dictionary grows too large, clear it (shouldn't happen in normal operation)
			if (_pendingResponses.Count > 1000) {
				UnityEngine.Debug.LogWarning($"[Addon_SocketServer] Too many pending responses ({_pendingResponses.Count}), clearing stale entries");
				// Remove all null entries (pending but not completed)
				var keysToRemove = new List<string>();
				foreach (var kvp in _pendingResponses) {
					if (kvp.Value == null) {
						keysToRemove.Add(kvp.Key);
					}
				}
				foreach (var key in keysToRemove) {
					_pendingResponses.TryRemove(key, out _);
				}
			}
		}
		
		/// <summary>
		/// Stops the JSON-RPC TCP listener and joins the accept thread. Call after Python/add-on clients are terminated
		/// so <see cref="HandleClient"/> loops can exit. Idempotent; safe from <see cref="Application.quitting"/> or <see cref="MonoBehaviour.OnDestroy"/>.
		/// </summary>
		public void ShutdownNetworkingForQuit() {
			if (_quitNetworkingShutdownDone)
				return;
			_quitNetworkingShutdownDone = true;
			_isRunning = false;
			try {
				_listener?.Stop();
			} catch { }
			if (_listenerThread != null && _listenerThread.IsAlive) {
				// Keep quit path responsive: do not block app close waiting on background accept loop.
				_listenerThread.Join(100);
				if (_listenerThread.IsAlive)
					UnityEngine.Debug.LogWarning("[Addon_SocketServer] Listener thread did not terminate within timeout (quit).");
			}
			_pendingResponses.Clear();
			_abandonedResponseIds.Clear();
			while (_mainThreadQueue.TryDequeue(out _)) { }
			try {
				string markerPath = GetReadyMarkerPath(_port);
				if (File.Exists(markerPath))
					File.Delete(markerPath);
			} catch { }
		}

		void OnDestroy() {
			ShutdownNetworkingForQuit();
		}
	}
}
