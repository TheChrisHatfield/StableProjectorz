using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace spz {

	/// <summary>
	/// HTTP REST API server that provides REST endpoints for web/remote clients.
	/// Maps REST endpoints to existing JSON-RPC methods for consistency.
	/// </summary>
	public class Addon_HttpServer : MonoBehaviour {
		public static Addon_HttpServer instance { get; private set; }
		
		private HttpListener _listener;
		private Thread _listenerThread;
		private bool _isRunning = false;
		private int _port = 5557;
		
		// Thread-safe queue for commands from background thread to main thread
		private ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
		
		// Dictionary to store pending responses by request ID
		private ConcurrentDictionary<string, JObject> _pendingResponses = new ConcurrentDictionary<string, JObject>();
		
		// CORS configuration
		[SerializeField] bool _enableCors = true;
		[SerializeField] string _allowedOrigins = "*"; // Comma-separated list or "*" for all
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
		
		void Start() {
			// Get port from Addon_MGR
			if (Addon_MGR.instance != null) {
				_port = Addon_MGR.instance.GetHttpServerPort();
			}
			
			StartServer();
		}
		
		void Update() {
			// Process queued commands on main thread
			int processed = 0;
			while (processed < 10 && _mainThreadQueue.TryDequeue(out Action action)) {
				action?.Invoke();
				processed++;
			}
		}
		
		/// <summary>
		/// Starts the HTTP listener on a background thread
		/// </summary>
		void StartServer() {
			if (_isRunning) return;
			
			try {
				_listener = new HttpListener();
				_listener.Prefixes.Add($"http://localhost:{_port}/");
				_listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
				_listener.Start();
				_isRunning = true;
				
				_listenerThread = new Thread(ListenForRequests) {
					IsBackground = true
				};
				_listenerThread.Start();
				
				UnityEngine.Debug.Log($"[Addon_HttpServer] Started HTTP server on port {_port}");
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_HttpServer] Failed to start server: {e.Message}");
			}
		}
		
		/// <summary>
		/// Stops the HTTP server
		/// </summary>
		void StopServer() {
			if (!_isRunning) return;
			
			_isRunning = false;
			_listener?.Stop();
			_listener?.Close();
			
			UnityEngine.Debug.Log("[Addon_HttpServer] HTTP server stopped");
		}
		
		void OnDestroy() {
			StopServer();
		}
		
		/// <summary>
		/// Background thread loop that handles HTTP requests
		/// </summary>
		void ListenForRequests() {
			while (_isRunning) {
				try {
					HttpListenerContext context = _listener.GetContext();
					ThreadPool.QueueUserWorkItem((state) => {
						try {
							HandleRequest(context);
						}
						catch (Exception e) {
							UnityEngine.Debug.LogError($"[Addon_HttpServer] Error handling request: {e.Message}");
						}
					});
				}
				catch (Exception e) {
					if (_isRunning) {
						UnityEngine.Debug.LogError($"[Addon_HttpServer] Error in listener: {e.Message}");
					}
				}
			}
		}
		
		/// <summary>
		/// Handles a single HTTP request
		/// </summary>
		void HandleRequest(HttpListenerContext context) {
			HttpListenerRequest request = context.Request;
			HttpListenerResponse response = context.Response;
			
			// Handle CORS preflight
			if (request.HttpMethod == "OPTIONS") {
				HandleCorsPreflight(response);
				response.Close();
				return;
			}
			
			// Add CORS headers
			if (_enableCors) {
				AddCorsHeaders(response);
			}
			
			// Route request
			try {
				string path = request.Url.AbsolutePath;
				string method = request.HttpMethod;
				
				JObject result = RouteRequest(method, path, request, response);
				
				// Send response
				if (result != null) {
					// Check if result has status code
					int statusCode = 200;
					if (result["status"] != null) {
						statusCode = result["status"].ToObject<int>();
						result.Remove("status");
					}
					
					string json = JsonConvert.SerializeObject(result);
					byte[] buffer = Encoding.UTF8.GetBytes(json);
					
					response.ContentType = "application/json";
					response.ContentLength64 = buffer.Length;
					response.StatusCode = statusCode;
					
					response.OutputStream.Write(buffer, 0, buffer.Length);
				}
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_HttpServer] Error processing request: {e.Message}");
				
				// Send error response
				var errorResponse = new JObject {
					["error"] = new JObject {
						["code"] = 500,
						["message"] = e.Message
					}
				};
				
				string errorJson = JsonConvert.SerializeObject(errorResponse);
				byte[] errorBuffer = Encoding.UTF8.GetBytes(errorJson);
				
				response.ContentType = "application/json";
				response.StatusCode = 500;
				response.ContentLength64 = errorBuffer.Length;
				response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
			}
			finally {
				response.Close();
			}
		}
		
		/// <summary>
		/// Routes HTTP requests to appropriate handlers
		/// </summary>
		JObject RouteRequest(string httpMethod, string path, HttpListenerRequest request, HttpListenerResponse response) {
			// Parse path: /api/v1/{resource}/{id?}/{action?}
			string[] pathParts = path.Trim('/').Split('/');
			
			if (pathParts.Length < 3 || pathParts[0] != "api" || pathParts[1] != "v1") {
				response.StatusCode = 404;
				return new JObject { ["error"] = "Not Found", ["status"] = 404 };
			}
			
			string resource = pathParts[2];
			string id = pathParts.Length > 3 ? pathParts[3] : null;
			string action = pathParts.Length > 4 ? pathParts[4] : null;
			
			// Read request body
			JObject body = null;
			if (request.HasEntityBody) {
				using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding)) {
					string bodyText = reader.ReadToEnd();
					if (!string.IsNullOrEmpty(bodyText)) {
						try {
							body = JObject.Parse(bodyText);
						}
						catch {
							// Not JSON, ignore
						}
					}
				}
			}
			
			// Route to handler
			switch (resource.ToLower()) {
				case "cameras":
					return HandleCameraRequest(httpMethod, id, action, body, request);
				case "meshes":
					return HandleMeshRequest(httpMethod, id, action, body, request);
				case "scene":
					return HandleSceneRequest(httpMethod, action, body, request);
				case "sd":
				case "stablediffusion":
					return HandleSDRequest(httpMethod, action, body, request);
				case "projection":
					return HandleProjectionRequest(httpMethod, id, action, body, request);
				case "project":
					return HandleProjectRequest(httpMethod, action, body, request);
				case "controlnet":
					return HandleControlNetRequest(httpMethod, id, action, body, request);
				case "background":
					return HandleBackgroundRequest(httpMethod, action, body, request);
				case "workflow":
					return HandleWorkflowRequest(httpMethod, action, body, request);
				default:
					response.StatusCode = 404;
					return new JObject { ["error"] = $"Resource '{resource}' not found" };
			}
		}
		
		/// <summary>
		/// Handles camera-related requests
		/// </summary>
		JObject HandleCameraRequest(string method, string id, string action, JObject body, HttpListenerRequest request) {
			if (string.IsNullOrEmpty(id)) {
				// GET /api/v1/cameras - Get all cameras
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_all_camera_positions", new JObject()));
				}
				return new JObject { ["error"] = "Camera ID required", ["status"] = 400 };
			}
			
			int cameraIndex = int.Parse(id);
			
			if (string.IsNullOrEmpty(action)) {
				// GET /api/v1/cameras/{id}/position
				if (method == "GET") {
					var pos = ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_camera_pos", new JObject {
						["camera_index"] = cameraIndex
					}));
					return pos;
				}
				// POST /api/v1/cameras/{id}/position
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_camera_pos", new JObject {
						["camera_index"] = cameraIndex,
						["x"] = body["x"]?.ToObject<float>() ?? 0f,
						["y"] = body["y"]?.ToObject<float>() ?? 0f,
						["z"] = body["z"]?.ToObject<float>() ?? 0f
					}));
				}
			}
			else if (action == "position") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_camera_pos", new JObject {
						["camera_index"] = cameraIndex
					}));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_camera_pos", new JObject {
						["camera_index"] = cameraIndex,
						["x"] = body["x"]?.ToObject<float>() ?? 0f,
						["y"] = body["y"]?.ToObject<float>() ?? 0f,
						["z"] = body["z"]?.ToObject<float>() ?? 0f
					}));
				}
			}
			else if (action == "rotation") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_camera_rot", new JObject {
						["camera_index"] = cameraIndex
					}));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_camera_rot", new JObject {
						["camera_index"] = cameraIndex,
						["x"] = body["x"]?.ToObject<float>() ?? 0f,
						["y"] = body["y"]?.ToObject<float>() ?? 0f,
						["z"] = body["z"]?.ToObject<float>() ?? 0f,
						["w"] = body["w"]?.ToObject<float>() ?? 1f
					}));
				}
			}
			else if (action == "fov") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_camera_fov", new JObject {
						["camera_index"] = cameraIndex
					}));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_camera_fov", new JObject {
						["camera_index"] = cameraIndex,
						["fov"] = body["fov"]?.ToObject<float>() ?? 60f
					}));
				}
			}
			
			return new JObject { ["error"] = "Invalid action", ["status"] = 400 };
		}
		
		/// <summary>
		/// Handles mesh-related requests
		/// </summary>
		JObject HandleMeshRequest(string method, string id, string action, JObject body, HttpListenerRequest request) {
			if (string.IsNullOrEmpty(id)) {
				// GET /api/v1/meshes - Get all meshes
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_all_mesh_ids", new JObject()));
				}
				// POST /api/v1/meshes/batch/position - Batch operations
				if (method == "POST" && action == "batch" && body != null) {
					string batchAction = request.QueryString["action"] ?? "position";
					if (batchAction == "position") {
						return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_mesh_positions", body));
					}
				}
				return new JObject { ["error"] = "Invalid request" };
			}
			
			ushort meshId = ushort.Parse(id);
			
			if (string.IsNullOrEmpty(action)) {
				// GET /api/v1/meshes/{id} - Get mesh info
				if (method == "GET") {
					var info = new JObject();
					var pos = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_mesh_position", new JObject { ["mesh_id"] = meshId }));
					var rot = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_mesh_rotation", new JObject { ["mesh_id"] = meshId }));
					var scale = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_mesh_scale", new JObject { ["mesh_id"] = meshId }));
					info["position"] = pos;
					info["rotation"] = rot;
					info["scale"] = scale;
					return info;
				}
			}
			else if (action == "position") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_mesh_position", new JObject { ["mesh_id"] = meshId }));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_mesh_position", new JObject {
						["mesh_id"] = meshId,
						["x"] = body["x"]?.ToObject<float>() ?? 0f,
						["y"] = body["y"]?.ToObject<float>() ?? 0f,
						["z"] = body["z"]?.ToObject<float>() ?? 0f
					}));
				}
			}
			else if (action == "select") {
				if (method == "POST") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.select_mesh", new JObject { ["mesh_id"] = meshId }));
				}
			}
			
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles scene-related requests
		/// </summary>
		JObject HandleSceneRequest(string method, string action, JObject body, HttpListenerRequest request) {
			if (action == "info") {
				if (method == "GET") {
					var info = new JObject();
					info["total_meshes"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_total_mesh_count", new JObject()));
					info["selected_meshes"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_selected_mesh_count", new JObject()));
					return info;
				}
			}
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles Stable Diffusion requests
		/// </summary>
		JObject HandleSDRequest(string method, string action, JObject body, HttpListenerRequest request) {
			if (action == "generate") {
				if (method == "POST") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.trigger_texture_generation", new JObject()));
				}
			}
			else if (action == "status") {
				if (method == "GET") {
					var status = new JObject();
					status["generating"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.is_generating", new JObject()));
					status["connected"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.is_sd_connected", new JObject()));
					return status;
				}
			}
			else if (action == "prompt") {
				if (method == "GET") {
					var prompt = new JObject();
					prompt["positive"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_positive_prompt", new JObject()));
					prompt["negative"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_negative_prompt", new JObject()));
					return prompt;
				}
				if (method == "POST" && body != null) {
					if (body["positive"] != null) {
						ExecuteJsonRpc("spz.cmd.set_positive_prompt", new JObject { ["prompt"] = body["positive"] });
					}
					if (body["negative"] != null) {
						ExecuteJsonRpc("spz.cmd.set_negative_prompt", new JObject { ["prompt"] = body["negative"] });
					}
					return new JObject { ["success"] = true };
				}
			}
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles projection camera requests
		/// </summary>
		JObject HandleProjectionRequest(string method, string id, string action, JObject body, HttpListenerRequest request) {
			if (string.IsNullOrEmpty(id)) {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_projection_camera_count", new JObject()));
				}
				return new JObject { ["error"] = "Camera ID required" };
			}
			
			int cameraIndex = int.Parse(id);
			
			if (action == "position") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_projection_camera_pos", new JObject {
						["camera_index"] = cameraIndex
					}));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_projection_camera_pos", new JObject {
						["camera_index"] = cameraIndex,
						["x"] = body["x"]?.ToObject<float>() ?? 0f,
						["y"] = body["y"]?.ToObject<float>() ?? 0f,
						["z"] = body["z"]?.ToObject<float>() ?? 0f
					}));
				}
			}
			
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles project requests
		/// </summary>
		JObject HandleProjectRequest(string method, string action, JObject body, HttpListenerRequest request) {
			if (action == "save") {
				if (method == "POST") {
					string filepath = body?["filepath"]?.ToString();
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.save_project", new JObject {
						["filepath"] = filepath
					}));
				}
			}
			else if (action == "load") {
				if (method == "POST") {
					string filepath = body?["filepath"]?.ToString();
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.load_project", new JObject {
						["filepath"] = filepath
					}));
				}
			}
			else if (action == "info") {
				if (method == "GET") {
					var info = new JObject();
					info["path"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_project_path", new JObject()));
					info["version"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_project_version", new JObject()));
					return info;
				}
			}
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles ControlNet requests
		/// </summary>
		JObject HandleControlNetRequest(string method, string id, string action, JObject body, HttpListenerRequest request) {
			if (string.IsNullOrEmpty(id)) {
				if (method == "GET") {
					var info = new JObject();
					info["total_units"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_controlnet_unit_count", new JObject()));
					info["active_units"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_active_controlnet_unit_count", new JObject()));
					return info;
				}
				return new JObject { ["error"] = "Unit ID required" };
			}
			
			int unitIndex = int.Parse(id);
			
			if (action == "enabled") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_controlnet_unit_enabled", new JObject {
						["unit_index"] = unitIndex
					}));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_controlnet_unit_enabled", new JObject {
						["unit_index"] = unitIndex,
						["enabled"] = body["enabled"]?.ToObject<bool>() ?? false
					}));
				}
			}
			else if (action == "weight") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_controlnet_unit_weight", new JObject {
						["unit_index"] = unitIndex
					}));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_controlnet_unit_weight", new JObject {
						["unit_index"] = unitIndex,
						["weight"] = body["weight"]?.ToObject<float>() ?? 1f
					}));
				}
			}
			
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles background/skybox requests
		/// </summary>
		JObject HandleBackgroundRequest(string method, string action, JObject body, HttpListenerRequest request) {
			if (action == "color") {
				if (method == "GET") {
					var color = new JObject();
					color["top"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_skybox_top_color", new JObject()));
					color["bottom"] = ConvertToRestResponse(ExecuteJsonRpc("spz.cmd.get_skybox_bottom_color", new JObject()));
					return color;
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_skybox_color", new JObject {
						["r"] = body["r"]?.ToObject<float>() ?? 0f,
						["g"] = body["g"]?.ToObject<float>() ?? 0f,
						["b"] = body["b"]?.ToObject<float>() ?? 0f,
						["a"] = body["a"]?.ToObject<float>() ?? 1f
					}));
				}
			}
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Handles workflow requests
		/// </summary>
		JObject HandleWorkflowRequest(string method, string action, JObject body, HttpListenerRequest request) {
			if (action == "mode") {
				if (method == "GET") {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.get_workflow_mode", new JObject()));
				}
				if (method == "POST" && body != null) {
					return ConvertToRestResponse(ExecuteJsonRpcSync("spz.cmd.set_workflow_mode", new JObject {
						["mode"] = body["mode"]?.ToString()
					}));
				}
			}
			return new JObject { ["error"] = "Invalid action" };
		}
		
		/// <summary>
		/// Executes a JSON-RPC command and returns the result (must be called from main thread)
		/// </summary>
		JObject ExecuteJsonRpc(string method, JObject @params) {
			// Reuse the existing JSON-RPC handler from Addon_SocketServer
			if (Addon_SocketServer.instance != null) {
				var request = new JObject {
					["method"] = method,
					["params"] = @params
				};
				var response = Addon_SocketServer.instance.ProcessRequestDirect(request);
				return response["result"] as JObject ?? new JObject();
			}
			
			return new JObject { ["error"] = "JSON-RPC handler not available" };
		}
		
		/// <summary>
		/// Executes a JSON-RPC command on the main thread and waits for result
		/// </summary>
		JObject ExecuteJsonRpcSync(string method, JObject @params) {
			JObject result = null;
			bool completed = false;
			
			// Queue execution on main thread
			_mainThreadQueue.Enqueue(() => {
				result = ExecuteJsonRpc(method, @params);
				completed = true;
			});
			
			// Wait for completion (with timeout)
			int timeout = 1000; // 1 second
			int elapsed = 0;
			while (!completed && elapsed < timeout) {
				Thread.Sleep(10);
				elapsed += 10;
			}
			
			if (!completed) {
				return new JObject { ["error"] = "Command execution timeout" };
			}
			
			return result ?? new JObject { ["error"] = "No result" };
		}
		
		/// <summary>
		/// Converts JSON-RPC response to REST-friendly format
		/// </summary>
		JObject ConvertToRestResponse(JObject jsonRpcResult) {
			if (jsonRpcResult == null) return new JObject();
			
			// If it already has a "success" field, return as-is
			if (jsonRpcResult["success"] != null) {
				return jsonRpcResult;
			}
			
			// Otherwise, wrap in data field
			return new JObject { ["data"] = jsonRpcResult };
		}
		
		/// <summary>
		/// Adds CORS headers to response
		/// </summary>
		void AddCorsHeaders(HttpListenerResponse response) {
			if (_allowedOrigins == "*") {
				response.AddHeader("Access-Control-Allow-Origin", "*");
			}
			else {
				response.AddHeader("Access-Control-Allow-Origin", _allowedOrigins);
			}
			response.AddHeader("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
			response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
		}
		
		/// <summary>
		/// Handles CORS preflight requests
		/// </summary>
		void HandleCorsPreflight(HttpListenerResponse response) {
			AddCorsHeaders(response);
			response.StatusCode = 200;
		}
	}
}
