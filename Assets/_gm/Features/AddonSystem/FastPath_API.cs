using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Fast-path API for real-time operations that bypass IPC.
	/// Provides safe, validated access to Unity singletons with rate limiting.
	/// </summary>
	public class FastPath_API : MonoBehaviour {
		public static FastPath_API instance { get; private set; }
		
		// Rate limiting to prevent spam
		private float _lastCameraUpdate = 0f;
		private const float MIN_CAMERA_UPDATE_INTERVAL = 0.016f; // ~60fps max
		
		// Validation flags
		private bool _isInitialized = false;
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
		
		void Start() {
			// Wait for all managers to be ready
			StartCoroutine(WaitForInitialization());
		}
		
		IEnumerator WaitForInitialization() {
			while (ModelsHandler_3D.instance == null || 
			       UserCameras_MGR.instance == null) {
				yield return null;
			}
			_isInitialized = true;
		}
		
		// ============================================
		// CAMERA OPERATIONS (Real-time)
		// ============================================
		
		/// <summary>
		/// Fast camera position update (validated, rate-limited)
		/// </summary>
		public bool SetCameraPosition(int cameraIndex, float x, float y, float z) {
			if (!_isInitialized) return false;
			if (Time.time - _lastCameraUpdate < MIN_CAMERA_UPDATE_INTERVAL) return false;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return false;
			
			var camera = cameras.GetViewCamera(cameraIndex);
			if (camera == null || !camera.gameObject.activeInHierarchy) return false;
			
			// Validate position (prevent NaN, infinity, extreme values)
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z)) {
				return false;
			}
			
			// Clamp to reasonable bounds (adjust as needed)
			x = Mathf.Clamp(x, -1000f, 1000f);
			y = Mathf.Clamp(y, -1000f, 1000f);
			z = Mathf.Clamp(z, -1000f, 1000f);
			
			camera.transform.position = new Vector3(x, y, z);
			_lastCameraUpdate = Time.time;
			return true;
		}
		
		/// <summary>
		/// Fast camera rotation update
		/// </summary>
		public bool SetCameraRotation(int cameraIndex, float x, float y, float z, float w) {
			if (!_isInitialized) return false;
			if (Time.time - _lastCameraUpdate < MIN_CAMERA_UPDATE_INTERVAL) return false;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return false;
			
			var camera = cameras.GetViewCamera(cameraIndex);
			if (camera == null || !camera.gameObject.activeInHierarchy) return false;
			
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z) || !IsValidFloat(w)) {
				return false;
			}
			
			// Normalize quaternion
			var quat = new Quaternion(x, y, z, w);
			// Check if quaternion is approximately zero (Quaternion doesn't have .magnitude)
			if (Mathf.Abs(quat.x) < 0.01f && Mathf.Abs(quat.y) < 0.01f && Mathf.Abs(quat.z) < 0.01f && Mathf.Abs(quat.w) < 0.01f) {
				return false; // Invalid quaternion
			}
			quat.Normalize();
			
			camera.transform.rotation = quat;
			_lastCameraUpdate = Time.time;
			return true;
		}
		
		/// <summary>
		/// Fast camera FOV update
		/// </summary>
		public bool SetCameraFOV(int cameraIndex, float fov) {
			if (!_isInitialized) return false;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return false;
			
			var camera = cameras.GetViewCamera(cameraIndex);
			if (camera == null || !camera.gameObject.activeInHierarchy) return false;
			
			if (!IsValidFloat(fov) || fov < 1f || fov > 179f) {
				return false;
			}
			
			if (camera.myCamera != null) {
				camera.myCamera.fieldOfView = fov;
				return true;
			}
			
			return false;
		}

		/// <summary>
		/// Snapshot of view cameras (the 3D viewports that render into the main viewport RT). Not projection / SD cams.
		/// </summary>
		public JObject GetViewCamerasStateJson() {
			if (!_isInitialized) return null;
			var mgr = UserCameras_MGR.instance;
			if (mgr == null) return null;
			int n = mgr.GetViewCameraCount();
			var arr = new JArray();
			for (int i = 0; i < n; i++) {
				var vc = mgr.GetViewCamera(i);
				arr.Add(vc != null && vc.gameObject.activeSelf);
			}
			return new JObject {
				["count"] = n,
				["max_count"] = UserCameras_MGR.MAX_NUM_VIEW_CAMERAS,
				["current_index"] = mgr.CurrentViewCameraIndex,
				["num_active"] = mgr.numActiveViewCameras(),
				["active"] = arr,
				["note"] = "Active cameras composite into one full-viewport RT.",
			};
		}

		public JObject GetViewCameraProjectionJson(int cameraIndex) {
			if (!_isInitialized) return null;
			var mgr = UserCameras_MGR.instance;
			var vc = mgr?.GetViewCamera(cameraIndex);
			if (vc == null || vc.myCamera == null) return null;
			var cam = vc.myCamera;
			float fovReport = vc.fovMgr._trueCameraFov >= 1f && vc.fovMgr._trueCameraFov <= 179f
				? vc.fovMgr._trueCameraFov
				: cam.fieldOfView;
			return new JObject {
				["orthographic"] = cam.orthographic,
				["orthographic_size"] = cam.orthographicSize,
				["field_of_view"] = fovReport,
			};
		}

		public bool SetViewCamerasEnabledCount(int num) {
			if (!_isInitialized) return false;
			var mgr = UserCameras_MGR.instance;
			if (mgr == null) return false;
			return mgr.TryEnableExactlyActiveViewCameras(num);
		}

		public bool SetViewCameraActiveRpc(int index, bool active) {
			if (!_isInitialized) return false;
			var mgr = UserCameras_MGR.instance;
			if (mgr == null) return false;
			return mgr.TrySetViewCameraActive(index, active);
		}

		public bool SetCurrentViewCameraIndexRpc(int index) {
			if (!_isInitialized) return false;
			var mgr = UserCameras_MGR.instance;
			if (mgr == null) return false;
			return mgr.TrySetCurrentViewCameraIndex(index);
		}

		/// <summary>Optional orthographic / size / FOV; at least one must apply. <paramref name="orthographicSize"/> alone enables orthographic mode.</summary>
		public bool SetViewCameraProjectionRpc(int cameraIndex, bool? orthographic, float? orthographicSize, float? fieldOfView) {
			if (!_isInitialized) return false;
			var mgr = UserCameras_MGR.instance;
			var vc = mgr?.GetViewCamera(cameraIndex);
			if (vc == null || !vc.gameObject.activeInHierarchy || vc.myCamera == null) return false;
			var cam = vc.myCamera;
			if (orthographicSize.HasValue && !IsValidFloat(orthographicSize.Value)) return false;
			if (fieldOfView.HasValue && !IsValidFloat(fieldOfView.Value)) return false;

			bool any = false;
			if (orthographic == false) {
				cam.orthographic = false;
				any = true;
			}
			else if (orthographic == true || (orthographic == null && orthographicSize.HasValue)) {
				cam.orthographic = true;
				if (orthographicSize.HasValue)
					cam.orthographicSize = Mathf.Clamp(orthographicSize.Value, 0.01f, 5000f);
				any = true;
			}
			else if (orthographicSize.HasValue && cam.orthographic) {
				cam.orthographicSize = Mathf.Clamp(orthographicSize.Value, 0.01f, 5000f);
				any = true;
			}
			if (fieldOfView.HasValue) {
				float f = Mathf.Clamp(fieldOfView.Value, 1f, 179f);
				cam.orthographic = false;
				vc.fovMgr.SetFieldOfView(f);
				any = true;
			}
			return any;
		}
		
		// ============================================
		// SELECTION OPERATIONS (Real-time)
		// ============================================
		
		/// <summary>
		/// Fast mesh selection by ID
		/// </summary>
		public bool SelectMesh(ushort meshId) {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			// Get mesh by ID through the safe API
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return false;
			
			// Use the existing selection mechanism
			// Check if already selected
			if (mesh._isSelected) return true;
			
			// Change selection status
			mesh.TryChange_SelectionStatus(true, out bool isSuccess, isDeselectOthers: false);
			return isSuccess;
		}
		
		/// <summary>
		/// Fast mesh deselection
		/// </summary>
		public bool DeselectMesh(ushort meshId) {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return false;
			
			// Check if already deselected
			if (!mesh._isSelected) return true;
			
			// Change selection status
			mesh.TryChange_SelectionStatus(false, out bool isSuccess, isDeselectOthers: false, preventDeselect_ifLast: false);
			return isSuccess;
		}
		
		/// <summary>
		/// Select all meshes in scene
		/// </summary>
		public bool SelectAllMeshes() {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			SD_3D_Mesh.SelectAll();
			return true;
		}
		
		/// <summary>
		/// Deselect all meshes (keeps at least one selected)
		/// </summary>
		public bool DeselectAllMeshes() {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			SD_3D_Mesh.DeselectAll();
			return true;
		}
		
		// ============================================
		// TRANSFORM OPERATIONS (Real-time)
		// ============================================
		
		/// <summary>
		/// Fast mesh transform update
		/// </summary>
		public bool SetMeshPosition(ushort meshId, float x, float y, float z) {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return false;
			
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z)) {
				return false;
			}
			
			// Clamp to reasonable bounds
			x = Mathf.Clamp(x, -1000f, 1000f);
			y = Mathf.Clamp(y, -1000f, 1000f);
			z = Mathf.Clamp(z, -1000f, 1000f);
			
			mesh.transform.position = new Vector3(x, y, z);
			return true;
		}
		
		/// <summary>
		/// Set mesh rotation (quaternion)
		/// </summary>
		public bool SetMeshRotation(ushort meshId, float x, float y, float z, float w) {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return false;
			
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z) || !IsValidFloat(w)) {
				return false;
			}
			
			var quat = new Quaternion(x, y, z, w);
			// Check if quaternion is approximately zero
			if (Mathf.Abs(quat.x) < 0.01f && Mathf.Abs(quat.y) < 0.01f && Mathf.Abs(quat.z) < 0.01f && Mathf.Abs(quat.w) < 0.01f) {
				return false;
			}
			quat.Normalize();
			
			mesh.transform.rotation = quat;
			return true;
		}
		
		/// <summary>
		/// Set mesh scale
		/// </summary>
		public bool SetMeshScale(ushort meshId, float x, float y, float z) {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return false;
			
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z)) {
				return false;
			}
			
			// Clamp scale to reasonable bounds
			x = Mathf.Clamp(x, 0.001f, 100f);
			y = Mathf.Clamp(y, 0.001f, 100f);
			z = Mathf.Clamp(z, 0.001f, 100f);
			
			mesh.transform.localScale = new Vector3(x, y, z);
			return true;
		}
		
		/// <summary>
		/// Batch set mesh positions (performance optimization)
		/// </summary>
		public int SetMeshPositions(List<ushort> meshIds, List<Vector3> positions) {
			if (!_isInitialized) return 0;
			if (meshIds == null || positions == null) return 0;
			if (meshIds.Count != positions.Count) return 0;
			
			// Edge case: Empty batch
			if (meshIds.Count == 0) return 0;
			
			// Edge case: Batch size limit to prevent performance issues
			const int MAX_BATCH_SIZE = 1000;
			if (meshIds.Count > MAX_BATCH_SIZE) {
				UnityEngine.Debug.LogWarning($"[FastPath_API] Batch size {meshIds.Count} exceeds limit {MAX_BATCH_SIZE}, truncating");
				meshIds = meshIds.GetRange(0, MAX_BATCH_SIZE);
				positions = positions.GetRange(0, MAX_BATCH_SIZE);
			}
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return 0;
			
			int successCount = 0;
			for (int i = 0; i < meshIds.Count; i++) {
				var mesh = modelsHandler.getMesh_byUniqueID(meshIds[i]);
				if (mesh == null) continue;
				
				var pos = positions[i];
				if (!IsValidFloat(pos.x) || !IsValidFloat(pos.y) || !IsValidFloat(pos.z)) continue;
				
				pos.x = Mathf.Clamp(pos.x, -1000f, 1000f);
				pos.y = Mathf.Clamp(pos.y, -1000f, 1000f);
				pos.z = Mathf.Clamp(pos.z, -1000f, 1000f);
				
				mesh.transform.position = pos;
				successCount++;
			}
			
			return successCount;
		}
		
		/// <summary>
		/// Batch set mesh rotations (performance optimization)
		/// </summary>
		public int SetMeshRotations(List<ushort> meshIds, List<Quaternion> rotations) {
			if (!_isInitialized) return 0;
			if (meshIds == null || rotations == null) return 0;
			if (meshIds.Count != rotations.Count) return 0;
			
			// Edge case: Empty batch
			if (meshIds.Count == 0) return 0;
			
			// Edge case: Batch size limit
			const int MAX_BATCH_SIZE = 1000;
			if (meshIds.Count > MAX_BATCH_SIZE) {
				UnityEngine.Debug.LogWarning($"[FastPath_API] Batch size {meshIds.Count} exceeds limit {MAX_BATCH_SIZE}, truncating");
				meshIds = meshIds.GetRange(0, MAX_BATCH_SIZE);
				rotations = rotations.GetRange(0, MAX_BATCH_SIZE);
			}
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return 0;
			
			int successCount = 0;
			for (int i = 0; i < meshIds.Count; i++) {
				var mesh = modelsHandler.getMesh_byUniqueID(meshIds[i]);
				if (mesh == null) continue;
				
				var rot = rotations[i];
				if (!IsValidFloat(rot.x) || !IsValidFloat(rot.y) || !IsValidFloat(rot.z) || !IsValidFloat(rot.w)) continue;
				
				// Check if quaternion is approximately zero
				if (Mathf.Abs(rot.x) < 0.01f && Mathf.Abs(rot.y) < 0.01f && Mathf.Abs(rot.z) < 0.01f && Mathf.Abs(rot.w) < 0.01f) continue;
				rot.Normalize();
				
				mesh.transform.rotation = rot;
				successCount++;
			}
			
			return successCount;
		}
		
		/// <summary>
		/// Batch set mesh scales (performance optimization)
		/// </summary>
		public int SetMeshScales(List<ushort> meshIds, List<Vector3> scales) {
			if (!_isInitialized) return 0;
			if (meshIds == null || scales == null) return 0;
			if (meshIds.Count != scales.Count) return 0;
			
			// Edge case: Empty batch
			if (meshIds.Count == 0) return 0;
			
			// Edge case: Batch size limit
			const int MAX_BATCH_SIZE = 1000;
			if (meshIds.Count > MAX_BATCH_SIZE) {
				UnityEngine.Debug.LogWarning($"[FastPath_API] Batch size {meshIds.Count} exceeds limit {MAX_BATCH_SIZE}, truncating");
				meshIds = meshIds.GetRange(0, MAX_BATCH_SIZE);
				scales = scales.GetRange(0, MAX_BATCH_SIZE);
			}
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return 0;
			
			int successCount = 0;
			for (int i = 0; i < meshIds.Count; i++) {
				var mesh = modelsHandler.getMesh_byUniqueID(meshIds[i]);
				if (mesh == null) continue;
				
				var scale = scales[i];
				if (!IsValidFloat(scale.x) || !IsValidFloat(scale.y) || !IsValidFloat(scale.z)) continue;
				
				scale.x = Mathf.Clamp(scale.x, 0.001f, 100f);
				scale.y = Mathf.Clamp(scale.y, 0.001f, 100f);
				scale.z = Mathf.Clamp(scale.z, 0.001f, 100f);
				
				mesh.transform.localScale = scale;
				successCount++;
			}
			
			return successCount;
		}
		
		/// <summary>
		/// Set mesh visibility
		/// </summary>
		public bool SetMeshVisibility(ushort meshId, bool visible) {
			if (!_isInitialized) return false;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return false;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return false;
			
			mesh.ToggleRender(visible);
			return true;
		}
		
		/// <summary>
		/// Get mesh position
		/// </summary>
		public Vector3? GetMeshPosition(ushort meshId) {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return null;
			
			return mesh.transform.position;
		}
		
		/// <summary>
		/// Get mesh rotation
		/// </summary>
		public Quaternion? GetMeshRotation(ushort meshId) {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return null;
			
			return mesh.transform.rotation;
		}
		
		/// <summary>
		/// Get mesh scale
		/// </summary>
		public Vector3? GetMeshScale(ushort meshId) {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return null;
			
			return mesh.transform.localScale;
		}
		
		/// <summary>
		/// Get mesh bounds
		/// </summary>
		public Bounds? GetMeshBounds(ushort meshId) {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return null;
			
			return mesh.bounds;
		}
		
		/// <summary>
		/// Get mesh visibility
		/// </summary>
		public bool? GetMeshVisibility(ushort meshId) {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return null;
			
			return mesh._isVisible;
		}
		
		/// <summary>
		/// Get mesh name
		/// </summary>
		public string GetMeshName(ushort meshId) {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			var mesh = modelsHandler.getMesh_byUniqueID(meshId);
			if (mesh == null) return null;
			
			return mesh.gameObject.name;
		}
		
		// ============================================
		// READ OPERATIONS (Fast, but cached)
		// ============================================
		
		/// <summary>
		/// Get current camera position (cached, updates every frame)
		/// </summary>
		public Vector3? GetCameraPosition(int cameraIndex) {
			if (!_isInitialized) return null;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return null;
			
			var camera = cameras.GetViewCamera(cameraIndex);
			if (camera == null || !camera.gameObject.activeInHierarchy) return null;
			
			return camera.transform.position;
		}
		
		/// <summary>
		/// Get current camera rotation
		/// </summary>
		public Quaternion? GetCameraRotation(int cameraIndex) {
			if (!_isInitialized) return null;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return null;
			
			var camera = cameras.GetViewCamera(cameraIndex);
			if (camera == null || !camera.gameObject.activeInHierarchy) return null;
			
			return camera.transform.rotation;
		}
		
		/// <summary>
		/// Get current camera FOV
		/// </summary>
		public float? GetCameraFOV(int cameraIndex) {
			if (!_isInitialized) return null;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return null;
			
			var camera = cameras.GetViewCamera(cameraIndex);
			if (camera == null || !camera.gameObject.activeInHierarchy) return null;
			
			if (camera.myCamera != null) {
				return camera.myCamera.fieldOfView;
			}
			
			return null;
		}
		
		// ============================================
		// UTILITY FUNCTIONS
		// ============================================
		
		/// <summary>
		/// Validates a float value (NaN and Infinity check)
		/// Uses epsilon-based validation for edge cases
		/// </summary>
		private bool IsValidFloat(float value) {
			// Check for NaN and Infinity
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				return false;
			}
			
			// Additional check: Very extreme values might indicate errors
			// Using epsilon-greedy strategy: be strict about extreme values
			const float MAX_REASONABLE_VALUE = 1e6f; // 1 million
			if (Mathf.Abs(value) > MAX_REASONABLE_VALUE) {
				UnityEngine.Debug.LogWarning($"[FastPath_API] Extreme float value detected: {value}");
				return false;
			}
			
			return true;
		}
		
		/// <summary>
		/// Epsilon-based float comparison for precise operations
		/// </summary>
		private const float EPSILON = 0.0001f;
		
		private bool FloatEquals(float a, float b) {
			return Mathf.Abs(a - b) < EPSILON;
		}
		
		/// <summary>
		/// Check if fast-path is ready
		/// </summary>
		public bool IsReady() {
			return _isInitialized;
		}
		
		/// <summary>
		/// Get number of available cameras
		/// </summary>
		public int GetCameraCount() {
			if (!_isInitialized) return 0;
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return 0;
			return cameras.GetViewCameraCount();
		}
		
		/// <summary>
		/// Get all camera positions
		/// </summary>
		public List<Vector3> GetAllCameraPositions() {
			var positions = new List<Vector3>();
			if (!_isInitialized) return positions;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return positions;
			
			int count = cameras.GetViewCameraCount();
			for (int i = 0; i < count; i++) {
				var camera = cameras.GetViewCamera(i);
				if (camera != null && camera.gameObject.activeInHierarchy) {
					positions.Add(camera.transform.position);
				}
			}
			
			return positions;
		}
		
		/// <summary>
		/// Get all camera rotations
		/// </summary>
		public List<Quaternion> GetAllCameraRotations() {
			var rotations = new List<Quaternion>();
			if (!_isInitialized) return rotations;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return rotations;
			
			int count = cameras.GetViewCameraCount();
			for (int i = 0; i < count; i++) {
				var camera = cameras.GetViewCamera(i);
				if (camera != null && camera.gameObject.activeInHierarchy) {
					rotations.Add(camera.transform.rotation);
				}
			}
			
			return rotations;
		}
		
		/// <summary>
		/// Get all camera FOVs
		/// </summary>
		public List<float> GetAllCameraFOVs() {
			var fovs = new List<float>();
			if (!_isInitialized) return fovs;
			
			var cameras = UserCameras_MGR.instance;
			if (cameras == null) return fovs;
			
			int count = cameras.GetViewCameraCount();
			for (int i = 0; i < count; i++) {
				var camera = cameras.GetViewCamera(i);
				if (camera != null && camera.gameObject.activeInHierarchy && camera.myCamera != null) {
					fovs.Add(camera.myCamera.fieldOfView);
				}
			}
			
			return fovs;
		}
		
		/// <summary>
		/// Get list of selected mesh IDs
		/// </summary>
		public List<ushort> GetSelectedMeshIDs() {
			if (!_isInitialized) return new List<ushort>();
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return new List<ushort>();
			
			var selected = modelsHandler.selectedMeshes;
			var ids = new List<ushort>();
			foreach (var mesh in selected) {
				if (mesh != null) {
					ids.Add(mesh.unique_id);
				}
			}
			return ids;
		}
		
		// ============================================
		// SCENE INFORMATION
		// ============================================
		
		/// <summary>
		/// Get total mesh count in scene
		/// </summary>
		public int GetTotalMeshCount() {
			if (!_isInitialized) return 0;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return 0;
			
			return modelsHandler.meshes.Count;
		}
		
		/// <summary>
		/// Get selected mesh count
		/// </summary>
		public int GetSelectedMeshCount() {
			if (!_isInitialized) return 0;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return 0;
			
			return modelsHandler.selectedMeshes.Count;
		}
		
		/// <summary>
		/// Get all mesh IDs in scene
		/// </summary>
		public List<ushort> GetAllMeshIDs() {
			if (!_isInitialized) return new List<ushort>();
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return new List<ushort>();
			
			var ids = new List<ushort>();
			foreach (var mesh in modelsHandler.meshes) {
				if (mesh != null) {
					ids.Add(mesh.unique_id);
				}
			}
			return ids;
		}
		
		/// <summary>
		/// Get bounds of all selected meshes
		/// </summary>
		public Bounds? GetSelectedMeshesBounds() {
			if (!_isInitialized) return null;
			
			var modelsHandler = ModelsHandler_3D.instance;
			if (modelsHandler == null) return null;
			
			if (modelsHandler.selectedMeshes.Count == 0) return null;
			
			return modelsHandler.GetTotalBounds_ofSelectedMeshes();
		}
		
		// ============================================
		// STABLE DIFFUSION OPERATIONS
		// ============================================
		
		/// <summary>
		/// Get positive prompt
		/// </summary>
		public string GetPositivePrompt() {
			if (!_isInitialized) return null;
			
			var prompts = StableDiffusion_Prompts_UI.instance;
			if (prompts == null) return null;
			
			return prompts.positivePrompt;
		}
		
		/// <summary>
		/// Set positive prompt
		/// </summary>
		public bool SetPositivePrompt(string prompt) {
			if (!_isInitialized) return false;
			// Match SetNegativePrompt / RPC default (params["prompt"] ?? ""): allow "" to clear; reject null only.
			if (prompt == null) return false;
			
			var prompts = StableDiffusion_Prompts_UI.instance;
			if (prompts == null) return false;
			
			prompts.SetPositivePrompt(prompt);
			return true;
		}
		
		/// <summary>
		/// Get negative prompt
		/// </summary>
		public string GetNegativePrompt() {
			if (!_isInitialized) return null;
			
			var prompts = StableDiffusion_Prompts_UI.instance;
			if (prompts == null) return null;
			
			return prompts.negativePrompt;
		}
		
		/// <summary>
		/// Set negative prompt
		/// </summary>
		public bool SetNegativePrompt(string prompt) {
			if (!_isInitialized) return false;
			if (prompt == null) return false;
			
			var prompts = StableDiffusion_Prompts_UI.instance;
			if (prompts == null) return false;
			
			prompts.SetNegativePrompt(prompt);
			return true;
		}
		
		/// <summary>
		/// Trigger texture generation
		/// </summary>
		public bool TriggerTextureGeneration(bool isBackground = false) {
			if (!_isInitialized) return false;
			
			var sdHub = StableDiffusion_Hub.instance;
			if (sdHub == null) return false;
			
			if (sdHub._generating) return false; // Already generating
			
			sdHub.Generate(isMakingBackgrounds: isBackground);
			return true;
		}
		
		/// <summary>
		/// Stop current generation
		/// </summary>
		public bool StopGeneration() {
			if (!_isInitialized) return false;
			
			var sdHub = StableDiffusion_Hub.instance;
			if (sdHub == null) return false;
			
			if (!sdHub._generating) return false; // Not generating
			
			sdHub.OnStopGenerate_Button();
			return true;
		}
		
		/// <summary>
		/// Check if generation is in progress
		/// </summary>
		public bool IsGenerating() {
			if (!_isInitialized) return false;
			
			var sdHub = StableDiffusion_Hub.instance;
			if (sdHub == null) return false;
			
			return sdHub._generating;
		}
		
		/// <summary>
		/// Check if Stable Diffusion service is connected
		/// </summary>
		public bool IsSDConnected() {
			if (!_isInitialized) return false;
			return Connection_MGR.is_sd_connected;
		}
		
		/// <summary>
		/// Check if 3D generation service is connected
		/// </summary>
		public bool Is3DConnected() {
			if (!_isInitialized) return false;
			return Connection_MGR.is_3d_connected;
		}
		
		/// <summary>
		/// Check if 3D generation can start
		/// </summary>
		public bool Is3DGenerationReady() {
			if (!_isInitialized) return false;
			return Gen3D_MGR.isCanStart_make_meshes_and_tex();
		}
		
		/// <summary>
		/// Check if 3D generation is in progress
		/// </summary>
		public bool Is3DGenerationInProgress() {
			if (!_isInitialized) return false;
			var gen3D = Gen3D_API.instance;
			if (gen3D == null) return false;
			return gen3D.isBusy;
		}
		
		/// <summary>
		/// Trigger 3D generation (make_meshes_and_tex)
		/// Note: This uses default UI inputs. For custom parameters, use Gen3D_MGR directly.
		/// </summary>
		public bool Trigger3DGeneration() {
			if (!_isInitialized) return false;
			
			var gen3D = Gen3D_MGR.instance;
			if (gen3D == null) return false;
			
			return gen3D.Trigger3DGeneration();
		}
		
		// ============================================
		// PROJECTION CAMERA OPERATIONS
		// ============================================
		
		/// <summary>
		/// Get projection camera count
		/// </summary>
		public int GetProjectionCameraCount() {
			if (!_isInitialized) return 0;
			
			var projMGR = ProjectorCameras_MGR.instance;
			if (projMGR == null) return 0;
			
			return projMGR.num_projCameras;
		}
		
		/// <summary>
		/// Get projection camera position
		/// </summary>
		public Vector3? GetProjectionCameraPosition(int cameraIndex) {
			if (!_isInitialized) return null;
			
			var projMGR = ProjectorCameras_MGR.instance;
			if (projMGR == null) return null;
			
			var projCam = projMGR.ix_toProjCam(cameraIndex);
			if (projCam == null) return null;
			
			return projCam.transform.position;
		}
		
		// ============================================
		// EXPORT OPERATIONS
		// ============================================
		
		/// <summary>
		/// Export 3D model with textures
		/// </summary>
		public bool Export3DWithTextures() {
			if (!_isInitialized) return false;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return false;
			
			saveMGR.Export3D_with_textures();
			return true;
		}
		
		/// <summary>
		/// Load a 3D file from an absolute path (DCC / automation; same as Load model, no file dialog).
		/// </summary>
		public bool Import3DModelFromFile(string absolutePath) {
			if (!_isInitialized) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Import3DModelFromFile: API not initialized yet.");
				return false;
			}
			if (string.IsNullOrEmpty(absolutePath)) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Import3DModelFromFile: empty path.");
				return false;
			}
			if (!File.Exists(absolutePath)) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Import3DModelFromFile: file not found: " + absolutePath);
				return false;
			}
			var mh = ModelsHandler_3D.instance;
			if (mh == null) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Import3DModelFromFile: ModelsHandler_3D.instance is null.");
				return false;
			}
			return mh.TryImportModelFromFile(absolutePath);
		}
		
		/// <summary>
		/// Export 3D + associated textures to a known full mesh path (no file dialog; textures share base name).
		/// </summary>
		public bool Export3DWithTexturesToPath(string meshFilePath) {
			if (!_isInitialized) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Export3DWithTexturesToPath: API not initialized yet.");
				return false;
			}
			if (string.IsNullOrEmpty(meshFilePath)) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Export3DWithTexturesToPath: empty mesh file path.");
				return false;
			}
			var sm = Save_MGR.instance;
			if (sm == null) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Export3DWithTexturesToPath: Save_MGR.instance is null.");
				return false;
			}
			string norm;
			try {
				norm = Path.GetFullPath(meshFilePath);
			} catch {
				UnityEngine.Debug.LogWarning("[FastPath_API] Export3DWithTexturesToPath: invalid path: " + meshFilePath);
				return false;
			}
			if (string.IsNullOrEmpty(Path.GetFileName(norm))) {
				UnityEngine.Debug.LogWarning("[FastPath_API] Export3DWithTexturesToPath: missing filename in path: " + norm);
				return false;
			}
			try {
				var dir = Path.GetDirectoryName(norm);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
					Directory.CreateDirectory(dir);
				}
			} catch {
				UnityEngine.Debug.LogWarning("[FastPath_API] Export3DWithTexturesToPath: cannot create directory for: " + norm);
				return false;
			}
			return sm.Export3D_with_textures_ToPath(norm);
		}
		
		/// <summary>
		/// Export projection textures
		/// </summary>
		public bool ExportProjectionTextures(bool isDilate = true) {
			if (!_isInitialized) return false;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return false;
			
			saveMGR.SaveProjectionTextures(isDilate);
			return true;
		}
		
		/// <summary>
		/// Export view textures (what camera sees)
		/// </summary>
		public bool ExportViewTextures() {
			if (!_isInitialized) return false;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return false;
			
			saveMGR.SaveViewTextures();
			return true;
		}
		
		// ============================================
		// WORKFLOW MODE OPERATIONS
		// ============================================
		
		/// <summary>
		/// Get current workflow mode
		/// Returns: "ProjectionsMasking", "Inpaint_Color", "Inpaint_NoColor", "TotalObject", "WhereEmpty", "AntiShade"
		/// </summary>
		public string GetWorkflowMode() {
			if (!_isInitialized) return null;
			
			var workflow = WorkflowRibbon_UI.instance;
			if (workflow == null) return null;
			
			return workflow.currentMode().ToString();
		}
		
		/// <summary>
		/// Set workflow mode
		/// Valid modes: "ProjectionsMasking", "Inpaint_Color", "Inpaint_NoColor", "TotalObject", "WhereEmpty", "AntiShade"
		/// </summary>
		public bool SetWorkflowMode(string modeStr) {
			if (!_isInitialized) return false;
			if (string.IsNullOrEmpty(modeStr)) return false;
			
			var workflow = WorkflowRibbon_UI.instance;
			if (workflow == null) return false;
			
			if (Enum.TryParse<WorkflowRibbon_CurrMode>(modeStr, true, out var mode)) {
				workflow.Set_CurrentMode(mode, playAttentionAnim: false);
				return true;
			}
			
			return false;
		}

		// --- SD / Forge-facing generation options (workflow ribbon → WebUI) ---

		public void PopulateSdWorkflowOptions(JObject result) {
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) {
				result["success"] = false;
				result["error"] = "SD_WorkflowOptionsRibbon_UI not available";
				return;
			}
			result["success"] = true;
			result["denoising_strength"] = sd.denoisingStrength;
			result["mask_blur_step01"] = sd.maskBlur_StepLength01;
			result["soft_inpaint"] = sd.isSoftInpaint;
			result["tileable_inpaint"] = sd.isTileable;
			result["ignore_depth_or_normals"] = sd.ignoreDepthOrNormals;
			result["edge_thresh"] = sd.edgeThresh;
			result["edge_thick"] = sd.edgeThick;
			var wf = WorkflowRibbon_UI.instance;
			if (wf != null)
				result["workflow_mode"] = wf.currentMode().ToString();
			result["sd_connected"] = Connection_MGR.is_sd_connected;
		}

		public bool SetSdDenoisingStrength(float value) {
			if (!IsValidFloat(value)) return false;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			return sd != null && sd.TrySetDenoisingStrengthFromApi(value);
		}

		public bool SetSdMaskBlurStep(float value) {
			if (!IsValidFloat(value)) return false;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			return sd != null && sd.TrySetMaskBlurStepFromApi(value);
		}

		public bool SetSdSoftInpaint(bool on) {
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return false;
			sd.SetIsSoftInpaint_from_script(on);
			return true;
		}

		public bool SetSdTileableInpaint(bool on) {
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return false;
			sd.SetIsTileable_from_script(on);
			return true;
		}

		public bool SetSdIgnoreDepthOrNormals(bool on) {
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return false;
			sd.SetIgnoreDepthOrNormals_from_script(on);
			return true;
		}
		
		// ============================================
		// CONTROLNET OPERATIONS
		// ============================================
		
		/// <summary>
		/// Get number of ControlNet units
		/// </summary>
		public int GetControlNetUnitCount() {
			if (!_isInitialized) return 0;
			
			var ctrlNets = SD_ControlNetsList_UI.instance;
			if (ctrlNets == null) return 0;
			
			return ctrlNets.numTotalUnitsExisting();
		}
		
		/// <summary>
		/// Get number of active ControlNet units
		/// </summary>
		public int GetActiveControlNetUnitCount() {
			if (!_isInitialized) return 0;
			
			var ctrlNets = SD_ControlNetsList_UI.instance;
			if (ctrlNets == null) return 0;
			
			return ctrlNets.numActiveUnits();
		}
		
		/// <summary>
		/// Get ControlNet unit by index
		/// </summary>
		private ControlNetUnit_UI GetControlNetUnit(int unitIndex) {
			if (!_isInitialized) return null;
			
			var ctrlNets = SD_ControlNetsList_UI.instance;
			if (ctrlNets == null) return null;
			
			return ctrlNets.GetUnit(unitIndex);
		}
		
		/// <summary>
		/// Set ControlNet unit enabled state
		/// </summary>
		public bool SetControlNetUnitEnabled(int unitIndex, bool enabled) {
			var unit = GetControlNetUnit(unitIndex);
			if (unit == null) return false;
			
			if (unit.isActivated != enabled) {
				unit.ToggleSection();
			}
			return true;
		}
		
		/// <summary>
		/// Get ControlNet unit enabled state
		/// </summary>
		public bool? GetControlNetUnitEnabled(int unitIndex) {
			var unit = GetControlNetUnit(unitIndex);
			if (unit == null) return null;
			
			return unit.isActivated;
		}
		
		/// <summary>
		/// Set ControlNet unit weight
		/// </summary>
		public bool SetControlNetUnitWeight(int unitIndex, float weight) {
			var unit = GetControlNetUnit(unitIndex);
			if (unit == null) return false;
			
			if (!IsValidFloat(weight)) return false;
			weight = Mathf.Clamp(weight, 0f, 2f);
			
			unit.SetControlWeight(weight);
			return true;
		}
		
		/// <summary>
		/// Get ControlNet unit weight
		/// </summary>
		public float? GetControlNetUnitWeight(int unitIndex) {
			var unit = GetControlNetUnit(unitIndex);
			if (unit == null) return null;
			
			return unit.GetControlWeight();
		}
		
		/// <summary>
		/// Get ControlNet unit model name
		/// </summary>
		public string GetControlNetUnitModel(int unitIndex) {
			var unit = GetControlNetUnit(unitIndex);
			if (unit == null) return null;
			
			return unit.currModelName();
		}
		
		// ============================================
		// BACKGROUND/SKYBOX OPERATIONS
		// ============================================
		
		/// <summary>
		/// Set skybox gradient color (top or bottom)
		/// </summary>
		public bool SetSkyboxColor(bool isTop, float r, float g, float b, float a) {
			if (!_isInitialized) return false;
			
			var skybox = SkyboxBackground_MGR.instance;
			if (skybox == null) return false;
			
			if (!IsValidFloat(r) || !IsValidFloat(g) || !IsValidFloat(b) || !IsValidFloat(a)) {
				return false;
			}
			
			r = Mathf.Clamp01(r);
			g = Mathf.Clamp01(g);
			b = Mathf.Clamp01(b);
			a = Mathf.Clamp01(a);
			
			skybox.SetTopOrBottomColor(isTop, new Color(r, g, b, a));
			return true;
		}
		
		/// <summary>
		/// Check if skybox gradient is clear (no gradient colors)
		/// </summary>
		public bool IsSkyboxGradientClear() {
			if (!_isInitialized) return true;
			
			var skybox = SkyboxBackground_MGR.instance;
			if (skybox == null) return true;
			
			return skybox.isGradientColorClear;
		}
		
		/// <summary>
		/// Get skybox top gradient color
		/// </summary>
		public Color? GetSkyboxTopColor() {
			if (!_isInitialized) return null;
			
			var skybox = SkyboxBackground_MGR.instance;
			if (skybox == null) return null;
			
			return skybox.GetTopColor();
		}
		
		/// <summary>
		/// Get skybox bottom gradient color
		/// </summary>
		public Color? GetSkyboxBottomColor() {
			if (!_isInitialized) return null;
			
			var skybox = SkyboxBackground_MGR.instance;
			if (skybox == null) return null;
			
			return skybox.GetBottomColor();
		}
		
		// ============================================
		// PROJECT MANAGEMENT OPERATIONS
		// ============================================
		
		/// <summary>
		/// Save project (shows file dialog)
		/// Note: This is async - returns immediately, actual save happens in background
		/// </summary>
		public bool SaveProject() {
			if (!_isInitialized) return false;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return false;
			
			// Check if already saving/loading
			if (saveMGR._isSaving || saveMGR._isLoading) return false;
			
			// Check if generating (blocks save)
			if (StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._generating) {
				return false;
			}
			
			// Use standard save with dialog
			saveMGR.DoSaveProject();
			return true;
		}
		
		/// <summary>
		/// Load project (shows file dialog)
		/// Note: This is async - returns immediately, actual load happens in background
		/// </summary>
		public bool LoadProject() {
			if (!_isInitialized) return false;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return false;
			
			// Check if already saving/loading
			if (saveMGR._isSaving || saveMGR._isLoading) return false;
			
			// Check if generating (blocks load)
			if (StableDiffusion_Hub.instance != null && StableDiffusion_Hub.instance._generating) {
				return false;
			}
			
			// Use standard load with dialog
			saveMGR.DoLoadProject();
			return true;
		}
		
		/// <summary>
		/// Get current project filepath (if saved)
		/// </summary>
		public string GetProjectPath() {
			if (!_isInitialized) return null;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return null;
			
			var helper = saveMGR.GetComponent<ProjectSaveLoad_Helper>();
			if (helper == null) return null;
			
			string path = helper.GetLastSaveFilepath();
			return string.IsNullOrEmpty(path) ? null : path;
		}
		
		/// <summary>
		/// Data directory for a saved project: folder containing the .spz + <c>ProjectName_Data</c> — or null if not saved.
		/// Does not require <see cref="_isInitialized"/>; uses <see cref="Save_MGR"/> / <see cref="ProjectSaveLoad_Helper"/> only.
		/// </summary>
		static string GetSavedProjectDataDirIfAny() {
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) {
				return null;
			}
			var helper = saveMGR.GetComponent<ProjectSaveLoad_Helper>();
			if (helper == null) {
				return null;
			}
			string projectPath = helper.GetLastSaveFilepath();
			if (string.IsNullOrEmpty(projectPath)) {
				return null;
			}
			string projectDir = System.IO.Path.GetDirectoryName(projectPath);
			if (string.IsNullOrEmpty(projectDir)) {
				return null;
			}
			return System.IO.Path.Combine(
				projectDir,
				System.IO.Path.GetFileNameWithoutExtension(projectPath) + "_Data"
			);
		}

		/// <summary>
		/// Get project data directory (if project is saved)
		/// </summary>
		public string GetProjectDataDir() {
			if (!_isInitialized) {
				return null;
			}
			return GetSavedProjectDataDirIfAny();
		}

		const string SessionDataDirSubfolder = "StableProjectorzGO_session";

		/// <summary>True when there is no on-disk .spz path, so <see cref="GetProjectDataDirOrSession"/> uses the session exchange folder — independent of <see cref="_isInitialized"/>.</summary>
		public bool IsSpzGoSessionDataDir() {
			return string.IsNullOrEmpty(GetSavedProjectDataDirIfAny());
		}

		/// <summary>
		/// <see cref="GetProjectDataDir"/> when a .spz is saved, otherwise a persistent session folder
		/// so HTTP/Blender SPZ GO (exchange FBX) works without a saved file — similar to a temp work directory.
		/// Works before the FastPath coroutine flips <see cref="_isInitialized"/>, as long as <see cref="Save_MGR"/> exists.
		/// </summary>
		public string GetProjectDataDirOrSession() {
			string saved = GetSavedProjectDataDirIfAny();
			if (!string.IsNullOrEmpty(saved)) {
				return saved;
			}
			// Unsaved: session is valid even before the FastPath init coroutine (_isInitialized), so early HTTP/Blender works.
			try {
				string root = System.IO.Path.Combine(Application.persistentDataPath, SessionDataDirSubfolder);
				System.IO.Directory.CreateDirectory(root);
				return root;
			} catch (System.Exception) {
				return null;
			}
		}

		/// <summary>
		/// Delete the SPZ GO session root under <see cref="Application.persistentDataPath"/> on exit so
		/// unsaved-session FBX/texture exchange files do not accumulate. When a .spz is saved, the real
		/// <c>_Data</c> path is used instead, so this folder is only the temporary SPZ GO bridge.
		/// </summary>
		/// <remarks>
		/// <b>Player only:</b> In the Unity Editor, stopping Play Mode calls <see cref="Addon_MGR"/>'s
		/// <c>OnDestroy</c> → <see cref="Addon_MGR.ShutdownAddonApiBeforeQuit"/> — the same as a real
		/// application exit. Skipping here avoids deleting the exchange as soon as you leave Play
		/// (e.g. Blender can still be using <c>from_spz.fbx</c> under the same session path).
		/// Closing the whole Editor is not forced-cleaned here; delete under LocalLow if needed.
		/// </remarks>
		public static void TryDeleteSpzGoSessionFolderOnQuit() {
			if (Application.isEditor) {
				return;
			}
			try {
				string root = System.IO.Path.Combine(Application.persistentDataPath, SessionDataDirSubfolder);
				if (System.IO.Directory.Exists(root)) {
					System.IO.Directory.Delete(root, true);
				}
			} catch (System.Exception ex) {
				UnityEngine.Debug.LogWarning("[FastPath_API] SPZ GO session folder cleanup: " + ex.Message);
			}
		}
		
		/// <summary>
		/// Get project version string
		/// </summary>
		public string GetProjectVersion() {
			if (!_isInitialized) return null;
			
			// Get version from CheckForUpdates_MGR
			return CheckForUpdates_MGR.CURRENT_VERSION_HERE;
		}
		
		/// <summary>
		/// Check if project is currently being saved or loaded
		/// </summary>
		public bool IsProjectOperationInProgress() {
			if (!_isInitialized) return false;
			
			var saveMGR = Save_MGR.instance;
			if (saveMGR == null) return false;
			
			return saveMGR._isSaving || saveMGR._isLoading;
		}
		
		// ============================================
		// PROJECTION CAMERA OPERATIONS
		// ============================================
		
		/// <summary>
		/// Get projection camera rotation
		/// </summary>
		public Quaternion? GetProjectionCameraRotation(int cameraIndex) {
			if (!_isInitialized) return null;
			
			var projMGR = ProjectorCameras_MGR.instance;
			if (projMGR == null) return null;
			
			var projCam = projMGR.ix_toProjCam(cameraIndex);
			if (projCam == null) return null;
			
			return projCam.transform.rotation;
		}
		
		/// <summary>
		/// Set projection camera position
		/// </summary>
		public bool SetProjectionCameraPosition(int cameraIndex, float x, float y, float z) {
			if (!_isInitialized) return false;
			
			var projMGR = ProjectorCameras_MGR.instance;
			if (projMGR == null) return false;
			
			var projCam = projMGR.ix_toProjCam(cameraIndex);
			if (projCam == null) return false;
			
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z)) {
				return false;
			}
			
			x = Mathf.Clamp(x, -1000f, 1000f);
			y = Mathf.Clamp(y, -1000f, 1000f);
			z = Mathf.Clamp(z, -1000f, 1000f);
			
			projCam.transform.position = new Vector3(x, y, z);
			return true;
		}
		
		/// <summary>
		/// Set projection camera rotation
		/// </summary>
		public bool SetProjectionCameraRotation(int cameraIndex, float x, float y, float z, float w) {
			if (!_isInitialized) return false;
			
			var projMGR = ProjectorCameras_MGR.instance;
			if (projMGR == null) return false;
			
			var projCam = projMGR.ix_toProjCam(cameraIndex);
			if (projCam == null) return false;
			
			if (!IsValidFloat(x) || !IsValidFloat(y) || !IsValidFloat(z) || !IsValidFloat(w)) {
				return false;
			}
			
			var quat = new Quaternion(x, y, z, w);
			// Check if quaternion is approximately zero
			if (Mathf.Abs(quat.x) < 0.01f && Mathf.Abs(quat.y) < 0.01f && Mathf.Abs(quat.z) < 0.01f && Mathf.Abs(quat.w) < 0.01f) {
				return false;
			}
			quat.Normalize();
			
			projCam.transform.rotation = quat;
			return true;
		}

		// ============================================
		// ADD-ON DISCOVERY / CONTEXT (single-call snapshot for external tools)
		// ============================================

		/// <summary>
		/// One JSON object with scene, pipeline, project, brush, paint layers, and SD workflow snapshot
		/// (same data as separate spz.cmd.* calls; for add-on ergonomics).
		/// </summary>
		public void PopulateAddonContext(JObject root) {
			root["success"] = true;
			root["fast_path_ready"] = _isInitialized;
			root["viewport_camera_count"] = GetCameraCount();
			root["total_mesh_count"] = GetTotalMeshCount();
			root["selected_mesh_count"] = GetSelectedMeshCount();
			root["selected_mesh_ids"] = JArray.FromObject(GetSelectedMeshIDs());
			string wmStr = GetWorkflowMode();
			root["workflow_mode"] = wmStr != null ? wmStr : (JToken)JValue.CreateNull();
			root["sd_connected"] = Connection_MGR.is_sd_connected;
			root["sd_generating"] = IsGenerating();
			root["is_3d_connected"] = Is3DConnected();
			root["is_3d_generation_ready"] = Is3DGenerationReady();
			root["is_3d_generation_in_progress"] = Is3DGenerationInProgress();
			root["projection_camera_count"] = GetProjectionCameraCount();
			root["project_operation_in_progress"] = IsProjectOperationInProgress();
			string pp = GetProjectPath();
			root["project_path"] = pp != null ? pp : (JToken)JValue.CreateNull();
			string dd = GetProjectDataDirOrSession();
			root["project_data_dir"] = dd != null ? dd : (JToken)JValue.CreateNull();
			root["project_data_dir_is_session"] = IsSpzGoSessionDataDir();
			string ver = GetProjectVersion();
			root["spz_release_version"] = ver != null ? ver : (JToken)JValue.CreateNull();

			var brush = new JObject();
			PopulateBrushSettings(brush);
			root["brush"] = brush;

			var paint = new JObject();
			PopulatePaintLayers(paint);
			root["paint_layers"] = paint;

			var sdw = new JObject();
			PopulateSdWorkflowOptions(sdw);
			root["sd_workflow"] = sdw;
		}

		// ============================================
		// BRUSH + PAINT LAYERS (viewport / inpaint stack)
		// ============================================

		/// <summary> Fill <paramref name="result"/> with current brush + stamp index (for spz.cmd.get_brush_settings). </summary>
		public void PopulateBrushSettings(JObject result) {
			result["success"] = true;
			result["size01"] = BrushRibbon_UI_Size.GetBrushSize01();
			result["spacing01"] = BrushRibbon_UI_Size.GetBrushSpacing01();
			result["angle_deg"] = BrushRibbon_UI_Size.GetBrushAngleDeg();
			result["roundness01"] = BrushRibbon_UI_Size.GetBrushRoundness01();
			result["opacity01"] = BrushRibbon_UI.instance != null ? BrushRibbon_UI.instance.brushOpacity01 : 1f;
			result["symmetry_x"] = BrushRibbon_UI_Size.GetPaintSymmetryXOn();
			result["tip_follows_stroke"] = BrushRibbon_UI_Size.GetBrushTipFollowsStroke();
			var sz = BrushRibbon_UI_Size.instance;
			result["scatter_mode"] = sz != null ? (int)sz.scatterMode : 0;
			int idx = 0, cnt = 0;
			string bname = "";
			if (BrushAlphas_MGR.instance != null) {
				cnt = BrushAlphas_MGR.instance.AllEntries.Count;
				idx = BrushAlphas_MGR.instance.CurrentIndex;
				if (cnt > 0 && idx >= 0 && idx < cnt) {
					var ent = BrushAlphas_MGR.instance.AllEntries[idx];
					bname = string.IsNullOrEmpty(ent.name) ? "" : ent.name;
				}
			}
			result["brush_index"] = idx;
			result["brush_count"] = cnt;
			result["brush_name"] = bname;

			var sdOpt = SD_WorkflowOptionsRibbon_UI.instance;
			if (sdOpt != null) {
				result["brush_tool_mode"] = (int)sdOpt.brushToolMode;
				result["is_positive"] = sdOpt.isPositive;
				result["is_smudge"] = sdOpt.isSmudge;
				var bc = sdOpt.brushColor;
				result["brush_color"] = new JObject { ["r"] = bc.r, ["g"] = bc.g, ["b"] = bc.b, ["a"] = bc.a };
				result["depth_limit_slider01"] = sdOpt.GetBrushDepthLimitSlider01();
			}
			var wfRib = WorkflowRibbon_UI.instance;
			if (wfRib != null)
				result["workflow_mode"] = wfRib.currentMode().ToString();
		}

		/// <summary> Fill layer list summary for spz.cmd.get_paint_layers. </summary>
		public void PopulatePaintLayers(JObject result) {
			var stack = PaintLayerStack_MGR.instance;
			if (stack == null) {
				result["success"] = false;
				result["error"] = "PaintLayerStack_MGR not available";
				return;
			}
			result["success"] = true;
			result["active_index"] = stack.ActiveLayerIndex;
			var arr = new JArray();
			for (int i = 0; i < stack.Layers.Count; i++) {
				var L = stack.Layers[i];
				if (L == null) continue;
				arr.Add(new JObject {
					["index"] = i,
					["name"] = L.Name ?? "",
					["visible"] = L.Visible,
					["opacity"] = L.Opacity,
				});
			}
			result["layers"] = arr;
		}

		public bool SetBrushSize01(float size01) {
			if (!IsValidFloat(size01) || size01 < 0f || size01 > 1f) return false;
			if (BrushRibbon_UI.instance != null) {
				BrushRibbon_UI.instance.SetBrushSize(size01);
				return true;
			}
			if (BrushRibbon_UI_Size.instance != null) {
				BrushRibbon_UI_Size.instance.SetBrushSize(size01);
				return true;
			}
			return false;
		}

		public bool SetBrushSpacing01(float spacing01) {
			if (!IsValidFloat(spacing01) || spacing01 < 0f || spacing01 > 1f) return false;
			if (BrushRibbon_UI.instance != null) {
				BrushRibbon_UI.instance.SetBrushSpacing(spacing01);
				return true;
			}
			if (BrushRibbon_UI_Size.instance != null) {
				BrushRibbon_UI_Size.instance.SetBrushSpacing(spacing01);
				return true;
			}
			return false;
		}

		public bool SetBrushAngleDeg(float deg) {
			if (!IsValidFloat(deg)) return false;
			if (BrushRibbon_UI.instance != null) {
				BrushRibbon_UI.instance.SetBrushAngle(deg);
				return true;
			}
			if (BrushRibbon_UI_Size.instance != null) {
				BrushRibbon_UI_Size.instance.SetBrushAngle(deg);
				return true;
			}
			return false;
		}

		public bool SetBrushRoundness01(float r01) {
			if (!IsValidFloat(r01) || r01 < 0f || r01 > 1f) return false;
			if (BrushRibbon_UI.instance != null) {
				BrushRibbon_UI.instance.SetBrushRoundness(r01);
				return true;
			}
			if (BrushRibbon_UI_Size.instance != null) {
				BrushRibbon_UI_Size.instance.SetBrushRoundness(r01);
				return true;
			}
			return false;
		}

		public bool SetBrushOpacity01(float opacity01) {
			if (!IsValidFloat(opacity01) || opacity01 < 0f || opacity01 > 1f) return false;
			if (BrushRibbon_UI.instance == null) return false;
			BrushRibbon_UI.instance.SetBrushOpacity01(opacity01);
			return true;
		}

		public bool SetBrushStampIndex(int index) {
			if (index < 0) return false;
			if (BrushAlphas_MGR.instance == null) return false;
			if (BrushAlphas_MGR.instance.AllEntries.Count == 0) return false;
			BrushAlphas_MGR.instance.CurrentIndex = index;
			return true;
		}

		public bool SetActivePaintLayerIndex(int index) {
			var stack = PaintLayerStack_MGR.instance;
			if (stack == null) return false;
			if (index < 0 || index >= stack.Layers.Count) return false;
			stack.SetActiveLayer(index);
			return stack.ActiveLayerIndex == index;
		}

		public bool SetPaintLayerVisible(int index, bool visible) {
			var stack = PaintLayerStack_MGR.instance;
			if (stack == null) return false;
			if (index < 0 || index >= stack.Layers.Count) return false;
			stack.SetLayerVisible(index, visible);
			return true;
		}

		public bool SetPaintLayerOpacity01(int index, float opacity01) {
			if (!IsValidFloat(opacity01)) return false;
			opacity01 = Mathf.Clamp01(opacity01);
			var stack = PaintLayerStack_MGR.instance;
			if (stack == null) return false;
			if (index < 0 || index >= stack.Layers.Count) return false;
			stack.SetLayerOpacity(index, opacity01);
			return true;
		}

		public bool SetPaintLayerName(int index, string name) {
			var stack = PaintLayerStack_MGR.instance;
			if (stack == null || name == null) return false;
			if (index < 0 || index >= stack.Layers.Count) return false;
			stack.SetLayerName(index, name);
			return true;
		}

		public bool SetBrushSymmetryX(bool on) {
			if (BrushRibbon_UI_Size.instance == null) return false;
			BrushRibbon_UI_Size.instance.SetPaintSymmetryXOn(on);
			return true;
		}

		public bool SetBrushScatterMode(int modeInt) {
			if (modeInt < 0 || modeInt > 2) return false;
			if (BrushRibbon_UI_Size.instance == null) return false;
			BrushRibbon_UI_Size.instance.SetScatterMode((BrushScatterMode)modeInt);
			return true;
		}

		public bool SetBrushTipFollowsStroke(bool follows) {
			if (BrushRibbon_UI_Size.instance == null) return false;
			BrushRibbon_UI_Size.instance.SetTipAngleMode(follows ? BrushTipAngleMode.FollowStroke : BrushTipAngleMode.FixedAngle);
			return true;
		}

		public bool SetWorkflowModeByName(string modeName) {
			if (string.IsNullOrEmpty(modeName) || WorkflowRibbon_UI.instance == null) return false;
			if (!Enum.TryParse<WorkflowRibbon_CurrMode>(modeName, true, out var mode)) return false;
			WorkflowRibbon_UI.instance.Set_CurrentMode(mode, false);
			return true;
		}

		public bool SetBrushToolModeFromApi(int modeInt) {
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return false;
			return sd.TrySetBrushToolModeFromApi(modeInt);
		}

		public bool SetBrushColorFromApi(float r, float g, float b, float a) {
			if (!IsValidFloat(r) || !IsValidFloat(g) || !IsValidFloat(b) || !IsValidFloat(a)) return false;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return false;
			return sd.SetBrushColorFromApi(r, g, b, Mathf.Clamp01(a));
		}

		public bool SetBrushDepthLimitSlider01(float slider01) {
			if (!IsValidFloat(slider01)) return false;
			var sd = SD_WorkflowOptionsRibbon_UI.instance;
			if (sd == null) return false;
			sd.SetBrushDepthLimitFromSlider01(Mathf.Clamp01(slider01));
			return true;
		}
	}
}
