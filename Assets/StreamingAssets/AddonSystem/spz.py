"""
StableProjectorz Python API Client Library

This module provides a Python interface to communicate with StableProjectorz
via JSON-RPC over TCP.
"""

import socket
import json
import threading
import time


class SPZClient:
    """Client for communicating with StableProjectorz via JSON-RPC"""
    
    def __init__(self, host='127.0.0.1', port=5555):
        self.host = host
        self.port = port
        self.socket = None
        self._lock = threading.Lock()
        self._request_id = 0
        
    def _get_next_id(self):
        """Get next request ID"""
        with self._lock:
            self._request_id += 1
            return self._request_id
    
    def _connect(self):
        """Establish connection to server"""
        if self.socket is None or self.socket.fileno() == -1:
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.socket.settimeout(5.0)
            try:
                self.socket.connect((self.host, self.port))
            except Exception as e:
                self.socket = None
                raise ConnectionError(f"Failed to connect to StableProjectorz: {e}")
    
    def _send_request(self, method, params=None):
        """Send a JSON-RPC request and return the response"""
        self._connect()
        
        request = {
            "jsonrpc": "2.0",
            "method": method,
            "params": params or {},
            "id": self._get_next_id()
        }
        
        request_json = json.dumps(request) + "\n"
        
        try:
            self.socket.sendall(request_json.encode('utf-8'))
            
            # Receive response
            response_data = b""
            while True:
                chunk = self.socket.recv(4096)
                if not chunk:
                    break
                response_data += chunk
                if b"\n" in response_data:
                    break
            
            response_str = response_data.decode('utf-8').strip()
            response = json.loads(response_str)
            
            if "error" in response:
                raise RuntimeError(f"Server error: {response['error'].get('message', 'Unknown error')}")
            
            return response.get("result", {})
        except Exception as e:
            self.socket = None  # Reset connection on error
            raise
    
    def close(self):
        """Close the connection"""
        if self.socket:
            try:
                self.socket.close()
            except:
                pass
            self.socket = None


# Global client instance
_client = None


def _get_client():
    """Get or create the global client instance"""
    global _client
    if _client is None:
        _client = SPZClient()
    return _client


# ============================================
# Camera API
# ============================================

class CameraAPI:
    """API for camera operations"""
    
    def __init__(self, client):
        self._client = client
    
    def set_pos(self, camera_index, x, y, z):
        """Set camera position"""
        result = self._client._send_request("spz.cmd.set_camera_pos", {
            "camera_index": camera_index,
            "x": float(x),
            "y": float(y),
            "z": float(z)
        })
        return result.get("success", False)
    
    def set_rot(self, camera_index, x, y, z, w):
        """Set camera rotation (quaternion)"""
        result = self._client._send_request("spz.cmd.set_camera_rot", {
            "camera_index": camera_index,
            "x": float(x),
            "y": float(y),
            "z": float(z),
            "w": float(w)
        })
        return result.get("success", False)
    
    def set_fov(self, camera_index, fov):
        """Set camera field of view"""
        result = self._client._send_request("spz.cmd.set_camera_fov", {
            "camera_index": camera_index,
            "fov": float(fov)
        })
        return result.get("success", False)
    
    def get_pos(self, camera_index):
        """Get camera position"""
        result = self._client._send_request("spz.cmd.get_camera_pos", {
            "camera_index": camera_index
        })
        if result.get("success", False):
            return {
                "x": result.get("x", 0.0),
                "y": result.get("y", 0.0),
                "z": result.get("z", 0.0)
            }
        return None
    
    def get_all_positions(self):
        """Get positions of all cameras"""
        result = self._client._send_request("spz.cmd.get_all_camera_positions", {})
        if result.get("success", False):
            positions = result.get("positions", [])
            return [{"x": p.get("x", 0.0), "y": p.get("y", 0.0), "z": p.get("z", 0.0)} 
                    for p in positions]
        return []
    
    def get_all_rotations(self):
        """Get rotations of all cameras"""
        result = self._client._send_request("spz.cmd.get_all_camera_rotations", {})
        if result.get("success", False):
            rotations = result.get("rotations", [])
            return [{"x": r.get("x", 0.0), "y": r.get("y", 0.0), 
                     "z": r.get("z", 0.0), "w": r.get("w", 1.0)} 
                    for r in rotations]
        return []
    
    def get_all_fovs(self):
        """Get FOVs of all cameras"""
        result = self._client._send_request("spz.cmd.get_all_camera_fovs", {})
        if result.get("success", False):
            return result.get("fovs", [])
        return []


# ============================================
# Models API
# ============================================

class ModelsAPI:
    """API for mesh/model operations"""
    
    def __init__(self, client):
        self._client = client
    
    def select(self, mesh_id):
        """Select a mesh by ID"""
        result = self._client._send_request("spz.cmd.select_mesh", {
            "mesh_id": int(mesh_id)
        })
        return result.get("success", False)
    
    def deselect(self, mesh_id):
        """Deselect a mesh by ID"""
        result = self._client._send_request("spz.cmd.deselect_mesh", {
            "mesh_id": int(mesh_id)
        })
        return result.get("success", False)
    
    def get_selected(self):
        """Get list of selected mesh IDs"""
        result = self._client._send_request("spz.cmd.get_selected_meshes", {})
        if result.get("success", False):
            return result.get("mesh_ids", [])
        return []
    
    def select_all(self):
        """Select all meshes in scene"""
        result = self._client._send_request("spz.cmd.select_all_meshes", {})
        return result.get("success", False)
    
    def deselect_all(self):
        """Deselect all meshes"""
        result = self._client._send_request("spz.cmd.deselect_all_meshes", {})
        return result.get("success", False)
    
    def set_pos(self, mesh_id, x, y, z):
        """Set mesh position"""
        result = self._client._send_request("spz.cmd.set_mesh_pos", {
            "mesh_id": int(mesh_id),
            "x": float(x),
            "y": float(y),
            "z": float(z)
        })
        return result.get("success", False)
    
    def set_rot(self, mesh_id, x, y, z, w):
        """Set mesh rotation (quaternion)"""
        result = self._client._send_request("spz.cmd.set_mesh_rot", {
            "mesh_id": int(mesh_id),
            "x": float(x),
            "y": float(y),
            "z": float(z),
            "w": float(w)
        })
        return result.get("success", False)
    
    def set_scale(self, mesh_id, x, y, z):
        """Set mesh scale"""
        result = self._client._send_request("spz.cmd.set_mesh_scale", {
            "mesh_id": int(mesh_id),
            "x": float(x),
            "y": float(y),
            "z": float(z)
        })
        return result.get("success", False)
    
    def set_positions(self, mesh_ids, positions):
        """Batch set mesh positions (performance optimization)
        
        Args:
            mesh_ids: List of mesh IDs
            positions: List of (x, y, z) tuples or dicts with x, y, z
            
        Returns:
            int: Number of successfully updated meshes
        """
        # Convert positions to list of dicts
        pos_list = []
        for pos in positions:
            if isinstance(pos, dict):
                pos_list.append({"x": float(pos["x"]), "y": float(pos["y"]), "z": float(pos["z"])})
            else:
                pos_list.append({"x": float(pos[0]), "y": float(pos[1]), "z": float(pos[2])})
        
        result = self._client._send_request("spz.cmd.set_mesh_positions", {
            "mesh_ids": [int(id) for id in mesh_ids],
            "positions": pos_list
        })
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def set_rotations(self, mesh_ids, rotations):
        """Batch set mesh rotations (performance optimization)
        
        Args:
            mesh_ids: List of mesh IDs
            rotations: List of (x, y, z, w) tuples or dicts with x, y, z, w
            
        Returns:
            int: Number of successfully updated meshes
        """
        # Convert rotations to list of dicts
        rot_list = []
        for rot in rotations:
            if isinstance(rot, dict):
                rot_list.append({"x": float(rot["x"]), "y": float(rot["y"]), "z": float(rot["z"]), "w": float(rot["w"])})
            else:
                rot_list.append({"x": float(rot[0]), "y": float(rot[1]), "z": float(rot[2]), "w": float(rot[3])})
        
        result = self._client._send_request("spz.cmd.set_mesh_rotations", {
            "mesh_ids": [int(id) for id in mesh_ids],
            "rotations": rot_list
        })
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def set_scales(self, mesh_ids, scales):
        """Batch set mesh scales (performance optimization)
        
        Args:
            mesh_ids: List of mesh IDs
            scales: List of (x, y, z) tuples or dicts with x, y, z
            
        Returns:
            int: Number of successfully updated meshes
        """
        # Convert scales to list of dicts
        scale_list = []
        for scale in scales:
            if isinstance(scale, dict):
                scale_list.append({"x": float(scale["x"]), "y": float(scale["y"]), "z": float(scale["z"])})
            else:
                scale_list.append({"x": float(scale[0]), "y": float(scale[1]), "z": float(scale[2])})
        
        result = self._client._send_request("spz.cmd.set_mesh_scales", {
            "mesh_ids": [int(id) for id in mesh_ids],
            "scales": scale_list
        })
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def set_visibility(self, mesh_id, visible):
        """Set mesh visibility"""
        result = self._client._send_request("spz.cmd.set_mesh_visibility", {
            "mesh_id": int(mesh_id),
            "visible": bool(visible)
        })
        return result.get("success", False)
    
    def get_pos(self, mesh_id):
        """Get mesh position"""
        result = self._client._send_request("spz.cmd.get_mesh_pos", {
            "mesh_id": int(mesh_id)
        })
        if result.get("success", False):
            return {
                "x": result.get("x", 0.0),
                "y": result.get("y", 0.0),
                "z": result.get("z", 0.0)
            }
        return None
    
    def get_rot(self, mesh_id):
        """Get mesh rotation (quaternion)"""
        result = self._client._send_request("spz.cmd.get_mesh_rot", {
            "mesh_id": int(mesh_id)
        })
        if result.get("success", False):
            return {
                "x": result.get("x", 0.0),
                "y": result.get("y", 0.0),
                "z": result.get("z", 0.0),
                "w": result.get("w", 1.0)
            }
        return None
    
    def get_scale(self, mesh_id):
        """Get mesh scale"""
        result = self._client._send_request("spz.cmd.get_mesh_scale", {
            "mesh_id": int(mesh_id)
        })
        if result.get("success", False):
            return {
                "x": result.get("x", 1.0),
                "y": result.get("y", 1.0),
                "z": result.get("z", 1.0)
            }
        return None
    
    def get_bounds(self, mesh_id):
        """Get mesh bounds"""
        result = self._client._send_request("spz.cmd.get_mesh_bounds", {
            "mesh_id": int(mesh_id)
        })
        if result.get("success", False):
            return {
                "center": {
                    "x": result.get("center_x", 0.0),
                    "y": result.get("center_y", 0.0),
                    "z": result.get("center_z", 0.0)
                },
                "size": {
                    "x": result.get("size_x", 0.0),
                    "y": result.get("size_y", 0.0),
                    "z": result.get("size_z", 0.0)
                }
            }
        return None
    
    def get_visibility(self, mesh_id):
        """Get mesh visibility"""
        result = self._client._send_request("spz.cmd.get_mesh_visibility", {
            "mesh_id": int(mesh_id)
        })
        if result.get("success", False):
            return result.get("visible", True)
        return None
    
    def get_name(self, mesh_id):
        """Get mesh name"""
        result = self._client._send_request("spz.cmd.get_mesh_name", {
            "mesh_id": int(mesh_id)
        })
        if result.get("success", False):
            return result.get("name", "")
        return None


# ============================================
# Scene API
# ============================================

class SceneAPI:
    """API for scene information"""
    
    def __init__(self, client):
        self._client = client
    
    def get_total_mesh_count(self):
        """Get total number of meshes in scene"""
        result = self._client._send_request("spz.cmd.get_total_mesh_count", {})
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def get_selected_mesh_count(self):
        """Get number of selected meshes"""
        result = self._client._send_request("spz.cmd.get_selected_mesh_count", {})
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def get_all_mesh_ids(self):
        """Get all mesh IDs in scene"""
        result = self._client._send_request("spz.cmd.get_all_mesh_ids", {})
        if result.get("success", False):
            return result.get("mesh_ids", [])
        return []
    
    def get_selected_meshes_bounds(self):
        """Get bounds of all selected meshes"""
        result = self._client._send_request("spz.cmd.get_selected_meshes_bounds", {})
        if result.get("success", False):
            return {
                "center": {
                    "x": result.get("center_x", 0.0),
                    "y": result.get("center_y", 0.0),
                    "z": result.get("center_z", 0.0)
                },
                "size": {
                    "x": result.get("size_x", 0.0),
                    "y": result.get("size_y", 0.0),
                    "z": result.get("size_z", 0.0)
                }
            }
        return None


# ============================================
# Stable Diffusion API
# ============================================

class StableDiffusionAPI:
    """API for Stable Diffusion operations"""
    
    def __init__(self, client):
        self._client = client
    
    def get_positive_prompt(self):
        """Get current positive prompt"""
        result = self._client._send_request("spz.cmd.get_positive_prompt", {})
        if result.get("success", False):
            return result.get("prompt", "")
        return None
    
    def set_positive_prompt(self, prompt):
        """Set positive prompt"""
        result = self._client._send_request("spz.cmd.set_positive_prompt", {
            "prompt": str(prompt)
        })
        return result.get("success", False)
    
    def get_negative_prompt(self):
        """Get current negative prompt"""
        result = self._client._send_request("spz.cmd.get_negative_prompt", {})
        if result.get("success", False):
            return result.get("prompt", "")
        return None
    
    def set_negative_prompt(self, prompt):
        """Set negative prompt"""
        result = self._client._send_request("spz.cmd.set_negative_prompt", {
            "prompt": str(prompt)
        })
        return result.get("success", False)
    
    def trigger_generation(self, is_background=False):
        """Trigger texture generation"""
        result = self._client._send_request("spz.cmd.trigger_texture_generation", {
            "is_background": bool(is_background)
        })
        return result.get("success", False)
    
    def stop_generation(self):
        """Stop current generation"""
        result = self._client._send_request("spz.cmd.stop_generation", {})
        return result.get("success", False)
    
    def is_generating(self):
        """Check if generation is in progress"""
        result = self._client._send_request("spz.cmd.is_generating", {})
        if result.get("success", False):
            return result.get("generating", False)
        return False
    
    def is_connected(self):
        """Check if Stable Diffusion service is connected"""
        result = self._client._send_request("spz.cmd.is_sd_connected", {})
        if result.get("success", False):
            return result.get("connected", False)
        return False


# ============================================
# 3D Generation API
# ============================================

class Gen3DAPI:
    """API for 3D generation operations"""
    
    def __init__(self, client):
        self._client = client
    
    def is_ready(self):
        """Check if 3D generation can start"""
        result = self._client._send_request("spz.cmd.is_3d_generation_ready", {})
        if result.get("success", False):
            return result.get("ready", False)
        return False
    
    def is_in_progress(self):
        """Check if 3D generation is in progress"""
        result = self._client._send_request("spz.cmd.is_3d_generation_in_progress", {})
        if result.get("success", False):
            return result.get("in_progress", False)
        return False
    
    def trigger(self):
        """Trigger 3D generation"""
        result = self._client._send_request("spz.cmd.trigger_3d_generation", {})
        return result.get("success", False)


# ============================================
# Export API
# ============================================

class ExportAPI:
    """API for export operations"""
    
    def __init__(self, client):
        self._client = client
    
    def export_3d_with_textures(self):
        """Export 3D model with textures"""
        result = self._client._send_request("spz.cmd.export_3d_with_textures", {})
        return result.get("success", False)
    
    def export_projection_textures(self, is_dilate=True):
        """Export projection textures"""
        result = self._client._send_request("spz.cmd.export_projection_textures", {
            "is_dilate": bool(is_dilate)
        })
        return result.get("success", False)
    
    def export_view_textures(self):
        """Export view textures (what camera sees)"""
        result = self._client._send_request("spz.cmd.export_view_textures", {})
        return result.get("success", False)


# ============================================
# Workflow API
# ============================================

class WorkflowAPI:
    """API for workflow mode operations"""
    
    def __init__(self, client):
        self._client = client
    
    def get_mode(self):
        """Get current workflow mode"""
        result = self._client._send_request("spz.cmd.get_workflow_mode", {})
        if result.get("success", False):
            return result.get("mode", "ProjectionsMasking")
        return None
    
    def set_mode(self, mode):
        """Set workflow mode
        
        Valid modes:
        - "ProjectionsMasking"
        - "Inpaint_Color"
        - "Inpaint_NoColor"
        - "TotalObject"
        - "WhereEmpty"
        - "AntiShade"
        """
        result = self._client._send_request("spz.cmd.set_workflow_mode", {
            "mode": str(mode)
        })
        return result.get("success", False)


# ============================================
# ControlNet API
# ============================================

class ControlNetAPI:
    """API for ControlNet operations"""
    
    def __init__(self, client):
        self._client = client
    
    def get_unit_count(self):
        """Get total number of ControlNet units"""
        result = self._client._send_request("spz.cmd.get_controlnet_unit_count", {})
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def get_active_unit_count(self):
        """Get number of active ControlNet units"""
        result = self._client._send_request("spz.cmd.get_active_controlnet_unit_count", {})
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def set_unit_enabled(self, unit_index, enabled):
        """Enable or disable a ControlNet unit
        
        Args:
            unit_index: Index of the ControlNet unit (0-based)
            enabled: True to enable, False to disable
        """
        result = self._client._send_request("spz.cmd.set_controlnet_unit_enabled", {
            "unit_index": int(unit_index),
            "enabled": bool(enabled)
        })
        return result.get("success", False)
    
    def get_unit_enabled(self, unit_index):
        """Get enabled state of a ControlNet unit
        
        Args:
            unit_index: Index of the ControlNet unit (0-based)
            
        Returns:
            bool or None if unit doesn't exist
        """
        result = self._client._send_request("spz.cmd.get_controlnet_unit_enabled", {
            "unit_index": int(unit_index)
        })
        if result.get("success", False):
            return result.get("enabled", False)
        return None
    
    def set_unit_weight(self, unit_index, weight):
        """Set ControlNet unit weight
        
        Args:
            unit_index: Index of the ControlNet unit (0-based)
            weight: Control weight (0.0-2.0)
        """
        result = self._client._send_request("spz.cmd.set_controlnet_unit_weight", {
            "unit_index": int(unit_index),
            "weight": float(weight)
        })
        return result.get("success", False)
    
    def get_unit_weight(self, unit_index):
        """Get ControlNet unit weight
        
        Args:
            unit_index: Index of the ControlNet unit (0-based)
            
        Returns:
            float or None if unit doesn't exist
        """
        result = self._client._send_request("spz.cmd.get_controlnet_unit_weight", {
            "unit_index": int(unit_index)
        })
        if result.get("success", False):
            return result.get("weight", 1.0)
        return None
    
    def get_unit_model(self, unit_index):
        """Get ControlNet unit model name
        
        Args:
            unit_index: Index of the ControlNet unit (0-based)
            
        Returns:
            str or None if unit doesn't exist
        """
        result = self._client._send_request("spz.cmd.get_controlnet_unit_model", {
            "unit_index": int(unit_index)
        })
        if result.get("success", False):
            return result.get("model", "None")
        return None


# ============================================
# Background API
# ============================================

class BackgroundAPI:
    """API for background/skybox operations"""
    
    def __init__(self, client):
        self._client = client
    
    def set_skybox_color(self, is_top, r, g, b, a=1.0):
        """Set skybox gradient color
        
        Args:
            is_top: True for top color, False for bottom color
            r, g, b, a: Color components (0.0-1.0)
        """
        result = self._client._send_request("spz.cmd.set_skybox_color", {
            "is_top": bool(is_top),
            "r": float(r),
            "g": float(g),
            "b": float(b),
            "a": float(a)
        })
        return result.get("success", False)
    
    def is_gradient_clear(self):
        """Check if skybox gradient is clear (no gradient colors)"""
        result = self._client._send_request("spz.cmd.is_skybox_gradient_clear", {})
        if result.get("success", False):
            return result.get("is_clear", True)
        return True
    
    def get_skybox_top_color(self):
        """Get skybox top gradient color
        
        Returns:
            dict with r, g, b, a (0.0-1.0) or None if failed
        """
        result = self._client._send_request("spz.cmd.get_skybox_top_color", {})
        if result.get("success", False):
            return {
                "r": result.get("r", 0.0),
                "g": result.get("g", 0.0),
                "b": result.get("b", 0.0),
                "a": result.get("a", 1.0)
            }
        return None
    
    def get_skybox_bottom_color(self):
        """Get skybox bottom gradient color
        
        Returns:
            dict with r, g, b, a (0.0-1.0) or None if failed
        """
        result = self._client._send_request("spz.cmd.get_skybox_bottom_color", {})
        if result.get("success", False):
            return {
                "r": result.get("r", 0.0),
                "g": result.get("g", 0.0),
                "b": result.get("b", 0.0),
                "a": result.get("a", 1.0)
            }
        return None


# ============================================
# Project API
# ============================================

class ProjectAPI:
    """API for project management operations"""
    
    def __init__(self, client):
        self._client = client
    
    def save(self):
        """Save project (shows file dialog)
        
        Note: This is async - returns immediately, actual save happens in background.
        Use is_operation_in_progress() to check if save is complete.
        """
        result = self._client._send_request("spz.cmd.save_project", {})
        return result.get("success", False)
    
    def load(self):
        """Load project (shows file dialog)
        
        Note: This is async - returns immediately, actual load happens in background.
        Use is_operation_in_progress() to check if load is complete.
        """
        result = self._client._send_request("spz.cmd.load_project", {})
        return result.get("success", False)
    
    def get_path(self):
        """Get current project filepath (if saved)
        
        Returns:
            str or None if project hasn't been saved
        """
        result = self._client._send_request("spz.cmd.get_project_path", {})
        if result.get("success", False):
            return result.get("path", None)
        return None
    
    def get_version(self):
        """Get project version string
        
        Returns:
            str or None if failed
        """
        result = self._client._send_request("spz.cmd.get_project_version", {})
        if result.get("success", False):
            return result.get("version", None)
        return None
    
    def get_data_dir(self):
        """Get project data directory path (if project is saved)
        
        Returns:
            str or None if project hasn't been saved
        """
        result = self._client._send_request("spz.cmd.get_project_data_dir", {})
        if result.get("success", False):
            return result.get("data_dir", None)
        return None
    
    def is_operation_in_progress(self):
        """Check if save or load operation is in progress
        
        Returns:
            bool: True if save or load is currently running
        """
        result = self._client._send_request("spz.cmd.is_project_operation_in_progress", {})
        if result.get("success", False):
            return result.get("in_progress", False)
        return False


# ============================================
# Projection API
# ============================================

class ProjectionAPI:
    """API for projection camera operations"""
    
    def __init__(self, client):
        self._client = client
    
    def get_count(self):
        """Get number of projection cameras"""
        result = self._client._send_request("spz.cmd.get_projection_camera_count", {})
        if result.get("success", False):
            return result.get("count", 0)
        return 0
    
    def get_pos(self, camera_index):
        """Get projection camera position"""
        result = self._client._send_request("spz.cmd.get_projection_camera_pos", {
            "camera_index": int(camera_index)
        })
        if result.get("success", False):
            return {
                "x": result.get("x", 0.0),
                "y": result.get("y", 0.0),
                "z": result.get("z", 0.0)
            }
        return None
    
    def get_rot(self, camera_index):
        """Get projection camera rotation (quaternion)"""
        result = self._client._send_request("spz.cmd.get_projection_camera_rot", {
            "camera_index": int(camera_index)
        })
        if result.get("success", False):
            return {
                "x": result.get("x", 0.0),
                "y": result.get("y", 0.0),
                "z": result.get("z", 0.0),
                "w": result.get("w", 1.0)
            }
        return None
    
    def set_pos(self, camera_index, x, y, z):
        """Set projection camera position"""
        result = self._client._send_request("spz.cmd.set_projection_camera_pos", {
            "camera_index": int(camera_index),
            "x": float(x),
            "y": float(y),
            "z": float(z)
        })
        return result.get("success", False)
    
    def set_rot(self, camera_index, x, y, z, w):
        """Set projection camera rotation (quaternion)"""
        result = self._client._send_request("spz.cmd.set_projection_camera_rot", {
            "camera_index": int(camera_index),
            "x": float(x),
            "y": float(y),
            "z": float(z),
            "w": float(w)
        })
        return result.get("success", False)


# ============================================
# UI API
# ============================================

class UIAPI:
    """API for UI creation"""
    
    def __init__(self, client):
        self._client = client
        self._panels = {}
    
    def create_panel(self, addon_id, title):
        """Create a UI panel for an add-on"""
        result = self._client._send_request("spz.ui.create_panel", {
            "addon_id": addon_id,
            "title": title
        })
        if result.get("success", False):
            panel_id = result.get("panel_id")
            self._panels[panel_id] = Panel(self._client, panel_id, addon_id)
            return self._panels[panel_id]
        return None
    
    def get_panel(self, panel_id):
        """Get a panel by ID"""
        return self._panels.get(panel_id)


class Panel:
    """Represents a UI panel"""
    
    def __init__(self, client, panel_id, addon_id):
        self._client = client
        self._panel_id = panel_id
        self._addon_id = addon_id
    
    def add_button(self, label, callback):
        """Add a button to this panel"""
        result = self._client._send_request("spz.ui.add_button", {
            "addon_id": self._addon_id,
            "panel_id": self._panel_id,
            "label": label,
            "callback": callback
        })
        if result.get("success", False):
            return result.get("button_id")
        return None
    
    def add_slider(self, label, min_val, max_val, default_val):
        """Add a slider to this panel
        
        Args:
            label: Slider label
            min_val: Minimum value (float)
            max_val: Maximum value (float)
            default_val: Default value (float)
            
        Returns:
            str: Element ID or None if failed
        """
        result = self._client._send_request("spz.ui.add_slider", {
            "addon_id": self._addon_id,
            "panel_id": self._panel_id,
            "label": str(label),
            "min": float(min_val),
            "max": float(max_val),
            "default": float(default_val)
        })
        if result.get("success", False):
            return result.get("element_id", None)
        return None
    
    def add_text_input(self, label, default_text=""):
        """Add a text input field to this panel
        
        Args:
            label: Input field label
            default_text: Default text value
            
        Returns:
            str: Element ID or None if failed
        """
        result = self._client._send_request("spz.ui.add_text_input", {
            "addon_id": self._addon_id,
            "panel_id": self._panel_id,
            "label": str(label),
            "default": str(default_text)
        })
        if result.get("success", False):
            return result.get("element_id", None)
        return None
    
    def add_dropdown(self, label, options, default_index=0):
        """Add a dropdown/combobox to this panel
        
        Args:
            label: Dropdown label
            options: List of option strings
            default_index: Default selected index (0-based)
            
        Returns:
            str: Element ID or None if failed
        """
        result = self._client._send_request("spz.ui.add_dropdown", {
            "addon_id": self._addon_id,
            "panel_id": self._panel_id,
            "label": str(label),
            "options": list(options),
            "default": int(default_index)
        })
        if result.get("success", False):
            return result.get("element_id", None)
        return None
    
    def get_value(self, element_id):
        """Get the value of a UI element
        
        Args:
            element_id: ID of the UI element
            
        Returns:
            Value (float, int, or str depending on element type) or None if failed
        """
        result = self._client._send_request("spz.ui.get_value", {
            "element_id": str(element_id)
        })
        if result.get("success", False):
            return result.get("value", None)
        return None
    
    def set_value(self, element_id, value):
        """Set the value of a UI element
        
        Args:
            element_id: ID of the UI element
            value: Value to set (float, int, or str depending on element type)
            
        Returns:
            bool: True if successful
        """
        result = self._client._send_request("spz.ui.set_value", {
            "element_id": str(element_id),
            "value": value
        })
        return result.get("success", False)


# ============================================
# Main API Module
# ============================================

class SPZAPI:
    """Main API interface"""
    
    def __init__(self):
        self._client = _get_client()
        self.cameras = CameraAPI(self._client)
        self.models = ModelsAPI(self._client)
        self.scene = SceneAPI(self._client)
        self.sd = StableDiffusionAPI(self._client)
        self.gen3d = Gen3DAPI(self._client)
        self.export = ExportAPI(self._client)
        self.workflow = WorkflowAPI(self._client)
        self.controlnet = ControlNetAPI(self._client)
        self.background = BackgroundAPI(self._client)
        self.project = ProjectAPI(self._client)
        self.projection = ProjectionAPI(self._client)
        self.ui = UIAPI(self._client)
    
    def close(self):
        """Close the connection"""
        self._client.close()


# Global API instance
_api = None


def get_api():
    """Get the global API instance"""
    global _api
    if _api is None:
        _api = SPZAPI()
    return _api


# Convenience aliases for easier import
def cameras():
    """Get cameras API"""
    return get_api().cameras


def models():
    """Get models API"""
    return get_api().models


def scene():
    """Get scene API"""
    return get_api().scene


def sd():
    """Get Stable Diffusion API"""
    return get_api().sd


def gen3d():
    """Get 3D Generation API"""
    return get_api().gen3d


def export():
    """Get Export API"""
    return get_api().export


def workflow():
    """Get Workflow API"""
    return get_api().workflow


def controlnet():
    """Get ControlNet API"""
    return get_api().controlnet


def background():
    """Get Background API"""
    return get_api().background


def project():
    """Get Project API"""
    return get_api().project


def projection():
    """Get projection API"""
    return get_api().projection


def ui():
    """Get UI API"""
    return get_api().ui
