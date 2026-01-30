using System;
using System.Collections;
using System.Collections.Concurrent;
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
	public class Addon_SocketServer : MonoBehaviour {
		public static Addon_SocketServer instance { get; private set; }
		
		private TcpListener _listener;
		private Thread _listenerThread;
		private bool _isRunning = false;
		private int _port = 5555;
		
		// Thread-safe queue for commands from background thread to main thread
		private ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
		
		// Dictionary to store pending responses by request ID
		private ConcurrentDictionary<string, JObject> _pendingResponses = new ConcurrentDictionary<string, JObject>();
		
		// Maximum commands to process per frame
		private const int MAX_COMMANDS_PER_FRAME = 10;
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
		
		void Start() {
			// Get port from Addon_MGR
			if (Addon_MGR.instance != null) {
				_port = Addon_MGR.instance.GetServerPort();
			}
			
			StartServer();
		}
		
		/// <summary>
		/// Starts the TCP listener on a background thread
		/// </summary>
		void StartServer() {
			if (_isRunning) return;
			
			try {
				_listener = new TcpListener(IPAddress.Loopback, _port);
				_listener.Start();
				_isRunning = true;
				
				_listenerThread = new Thread(ListenForClients) {
					IsBackground = true
				};
				_listenerThread.Start();
				
				UnityEngine.Debug.Log($"[Addon_SocketServer] Started listening on port {_port}");
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_SocketServer] Failed to start server: {e.Message}");
			}
		}
		
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
					
					TcpClient client = _listener.AcceptTcpClient();
					Thread clientThread = new Thread(() => HandleClient(client)) {
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
			try {
				NetworkStream stream = client.GetStream();
				byte[] buffer = new byte[4096];
				
				while (client.Connected && _isRunning) {
					int bytesRead = stream.Read(buffer, 0, buffer.Length);
					if (bytesRead == 0) break;
					
					string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
					
					// Parse JSON-RPC request
					try {
						var request = JObject.Parse(message);
						var response = ProcessRequest(request);
						
						// Send response back to client
						string responseJson = JsonConvert.SerializeObject(response);
						byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
						stream.Write(responseBytes, 0, responseBytes.Length);
					}
					catch (Exception e) {
						UnityEngine.Debug.LogError($"[Addon_SocketServer] Error processing request: {e.Message}");
						
						// Send error response
						var errorResponse = new JObject {
							["jsonrpc"] = "2.0",
							["error"] = new JObject {
								["code"] = -32700,
								["message"] = "Parse error"
							},
							["id"] = null
						};
						string errorJson = JsonConvert.SerializeObject(errorResponse);
						byte[] errorBytes = Encoding.UTF8.GetBytes(errorJson + "\n");
						stream.Write(errorBytes, 0, errorBytes.Length);
					}
				}
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_SocketServer] Error handling client: {e.Message}");
			}
			finally {
				client.Close();
			}
		}
		
		// Dictionary to store pending responses by request ID
		private ConcurrentDictionary<string, JObject> _pendingResponses = new ConcurrentDictionary<string, JObject>();
		
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
				JObject response;
				try {
					response = ExecuteCommand(method, @params);
					response["id"] = JToken.FromObject(id);
				}
				catch (Exception e) {
					response = CreateErrorResponse(-32603, $"Internal error: {e.Message}", JToken.FromObject(id));
				}
				_pendingResponses[id] = response;
			});
			
			// Wait for command to execute (with timeout)
			int timeout = 1000; // 1 second
			int elapsed = 0;
			while (elapsed < timeout) {
				if (_pendingResponses.TryGetValue(id, out JObject response) && response != null) {
					_pendingResponses.TryRemove(id, out _);
					return response;
				}
				Thread.Sleep(10);
				elapsed += 10;
			}
			
			_pendingResponses.TryRemove(id, out _);
			return CreateErrorResponse(-32603, "Command execution timeout", JToken.FromObject(id));
		}
		
		/// <summary>
		/// Executes a command on the main thread
		/// </summary>
		JObject ExecuteCommand(string method, JObject @params) {
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
		/// Executes fast-path commands
		/// </summary>
		JObject ExecuteFastPathCommand(string method, JObject @params) {
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
						bool visible = @params["visible"]?.ToObject<bool>() ?? true;
						result["success"] = fastPath.SetMeshVisibility(meshId, visible);
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
						
					case "spz.cmd.export_projection_textures":
						bool dilate = @params["is_dilate"]?.ToObject<bool>() ?? true;
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
						bool isTop = @params["is_top"]?.ToObject<bool>() ?? true;
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
							var meshIds = new List<ushort>();
							var positions = new List<Vector3>();
							
							foreach (var id in meshIdsJson) {
								meshIds.Add(id.ToObject<ushort>());
							}
							
							foreach (var pos in positionsJson) {
								var posObj = pos as JObject;
								if (posObj != null) {
									positions.Add(new Vector3(
										posObj["x"]?.ToObject<float>() ?? 0f,
										posObj["y"]?.ToObject<float>() ?? 0f,
										posObj["z"]?.ToObject<float>() ?? 0f
									));
								}
							}
							
							int successCount = fastPath.SetMeshPositions(meshIds, positions);
							result["success"] = true;
							result["count"] = successCount;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_mesh_rotations":
						meshIdsJson = @params["mesh_ids"] as JArray;
						var rotationsJson = @params["rotations"] as JArray;
						if (meshIdsJson != null && rotationsJson != null) {
							meshIds = new List<ushort>();
							var rotations = new List<Quaternion>();
							
							foreach (var id in meshIdsJson) {
								meshIds.Add(id.ToObject<ushort>());
							}
							
							foreach (var rot in rotationsJson) {
								var rotObj = rot as JObject;
								if (rotObj != null) {
									rotations.Add(new Quaternion(
										rotObj["x"]?.ToObject<float>() ?? 0f,
										rotObj["y"]?.ToObject<float>() ?? 0f,
										rotObj["z"]?.ToObject<float>() ?? 0f,
										rotObj["w"]?.ToObject<float>() ?? 1f
									));
								}
							}
							
							successCount = fastPath.SetMeshRotations(meshIds, rotations);
							result["success"] = true;
							result["count"] = successCount;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.set_mesh_scales":
						meshIdsJson = @params["mesh_ids"] as JArray;
						var scalesJson = @params["scales"] as JArray;
						if (meshIdsJson != null && scalesJson != null) {
							meshIds = new List<ushort>();
							var scales = new List<Vector3>();
							
							foreach (var id in meshIdsJson) {
								meshIds.Add(id.ToObject<ushort>());
							}
							
							foreach (var scale in scalesJson) {
								var scaleObj = scale as JObject;
								if (scaleObj != null) {
									scales.Add(new Vector3(
										scaleObj["x"]?.ToObject<float>() ?? 1f,
										scaleObj["y"]?.ToObject<float>() ?? 1f,
										scaleObj["z"]?.ToObject<float>() ?? 1f
									));
								}
							}
							
							successCount = fastPath.SetMeshScales(meshIds, scales);
							result["success"] = true;
							result["count"] = successCount;
						} else {
							result["success"] = false;
						}
						break;
						
					case "spz.cmd.save_project":
						result["success"] = fastPath.SaveProject();
						break;
						
					case "spz.cmd.load_project":
						result["success"] = fastPath.LoadProject();
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
						string dataDir = fastPath.GetProjectDataDir();
						if (dataDir != null) {
							result["success"] = true;
							result["data_dir"] = dataDir;
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
						foreach (var pos in allPositions) {
							posArray.Add(new JObject {
								["x"] = pos.x,
								["y"] = pos.y,
								["z"] = pos.z
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
		
		/// <summary>
		/// Executes UI commands (delegates to AddonUI_MGR)
		/// </summary>
		JObject ExecuteUICommand(string method, JObject @params) {
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
						string panelId = uiMgr.CreatePanel(addonId, title);
						if (panelId != null) {
							result["success"] = true;
							result["panel_id"] = panelId;
						} else {
							result["error"] = "Failed to create panel";
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
							if (valueToken.Type == JTokenType.Float || valueToken.Type == JTokenType.Integer) {
								valueObj = valueToken.ToObject<float>();
							} else if (valueToken.Type == JTokenType.String) {
								valueObj = valueToken.ToString();
							} else if (valueToken.Type == JTokenType.Integer) {
								valueObj = valueToken.ToObject<int>();
							}
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
		}
		
		void OnDestroy() {
			_isRunning = false;
			_listener?.Stop();
			_listenerThread?.Join(1000);
		}
	}
}
