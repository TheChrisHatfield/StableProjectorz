#!/usr/bin/env python3
"""
FastAPI HTTP Server for StableProjectorz
Provides REST API endpoints that forward to Unity via JSON-RPC
"""

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from typing import Optional, List, Dict, Any
import uvicorn
import threading

# Import spz for Unity communication
try:
    import spz
except ImportError:
    print("Error: Could not import spz module. Make sure spz.py is in the AddonSystem directory.")
    raise

# Global API instance (will be set by addon_server.py)
_api = None

def set_api_instance(api_instance):
    """Set the global API instance"""
    global _api
    _api = api_instance

# FastAPI app
app = FastAPI(
    title="StableProjectorz API",
    description="REST API for controlling StableProjectorz",
    version="1.0.0"
)

# CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Can be configured
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Pydantic models for request bodies
class Position(BaseModel):
    x: float
    y: float
    z: float

class Rotation(BaseModel):
    x: float
    y: float
    z: float
    w: float

class Prompt(BaseModel):
    positive: Optional[str] = None
    negative: Optional[str] = None

class ProjectPath(BaseModel):
    filepath: str

# Helper function to call Unity API
def call_unity(method: str, params: Dict[str, Any] = None) -> Dict[str, Any]:
    """Call Unity via JSON-RPC and return result"""
    if _api is None:
        raise HTTPException(status_code=503, detail="Not connected to Unity")
    
    try:
        # Use the spz client to send JSON-RPC request
        # _api is the result of spz.get_api(), which has a _client attribute
        client = _api._client
        result = client._send_request(method, params or {})
        return result
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

# ============================================
# Camera Endpoints
# ============================================

@app.get("/api/v1/cameras/{camera_id}/position")
async def get_camera_position(camera_id: int):
    """Get camera position"""
    result = call_unity("spz.cmd.get_camera_pos", {"camera_index": camera_id})
    if "success" in result and result["success"]:
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0)
        }
    raise HTTPException(status_code=404, detail="Camera not found")

@app.post("/api/v1/cameras/{camera_id}/position")
async def set_camera_position(camera_id: int, position: Position):
    """Set camera position"""
    result = call_unity("spz.cmd.set_camera_pos", {
        "camera_index": camera_id,
        "x": position.x,
        "y": position.y,
        "z": position.z
    })
    return {"success": result.get("success", False)}

@app.get("/api/v1/cameras/{camera_id}/rotation")
async def get_camera_rotation(camera_id: int):
    """Get camera rotation"""
    result = call_unity("spz.cmd.get_camera_rot", {"camera_index": camera_id})
    if "success" in result and result["success"]:
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0),
            "w": result.get("w", 1.0)
        }
    raise HTTPException(status_code=404, detail="Camera not found")

@app.post("/api/v1/cameras/{camera_id}/rotation")
async def set_camera_rotation(camera_id: int, rotation: Rotation):
    """Set camera rotation"""
    result = call_unity("spz.cmd.set_camera_rot", {
        "camera_index": camera_id,
        "x": rotation.x,
        "y": rotation.y,
        "z": rotation.z,
        "w": rotation.w
    })
    return {"success": result.get("success", False)}

@app.get("/api/v1/cameras/{camera_id}/fov")
async def get_camera_fov(camera_id: int):
    """Get camera FOV"""
    result = call_unity("spz.cmd.get_camera_fov", {"camera_index": camera_id})
    if "success" in result and result["success"]:
        return {"fov": result.get("fov", 60.0)}
    raise HTTPException(status_code=404, detail="Camera not found")

@app.post("/api/v1/cameras/{camera_id}/fov")
async def set_camera_fov(camera_id: int, fov: float):
    """Set camera FOV"""
    result = call_unity("spz.cmd.set_camera_fov", {
        "camera_index": camera_id,
        "fov": float(fov)
    })
    return {"success": result.get("success", False)}

@app.get("/api/v1/cameras/positions")
async def get_all_camera_positions():
    """Get all camera positions"""
    result = call_unity("spz.cmd.get_all_camera_positions", {})
    return result

@app.get("/api/v1/cameras/rotations")
async def get_all_camera_rotations():
    """Get all camera rotations"""
    result = call_unity("spz.cmd.get_all_camera_rotations", {})
    return result

@app.get("/api/v1/cameras/fovs")
async def get_all_camera_fovs():
    """Get all camera FOVs"""
    result = call_unity("spz.cmd.get_all_camera_fovs", {})
    return result

# ============================================
# Mesh Endpoints
# ============================================

@app.get("/api/v1/meshes")
async def get_meshes():
    """Get all mesh IDs"""
    result = call_unity("spz.cmd.get_all_mesh_ids", {})
    return result

@app.get("/api/v1/meshes/{mesh_id}/position")
async def get_mesh_position(mesh_id: int):
    """Get mesh position"""
    result = call_unity("spz.cmd.get_mesh_pos", {"mesh_id": mesh_id})
    if "success" in result and result["success"]:
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0)
        }
    raise HTTPException(status_code=404, detail="Mesh not found")

@app.post("/api/v1/meshes/{mesh_id}/position")
async def set_mesh_position(mesh_id: int, position: Position):
    """Set mesh position"""
    result = call_unity("spz.cmd.set_mesh_pos", {
        "mesh_id": mesh_id,
        "x": position.x,
        "y": position.y,
        "z": position.z
    })
    return {"success": result.get("success", False)}

@app.post("/api/v1/meshes/batch/position")
async def set_mesh_positions_batch(request: Dict[str, Any]):
    """Set multiple mesh positions (batch operation)"""
    mesh_ids = request.get("mesh_ids", [])
    positions = request.get("positions", [])
    result = call_unity("spz.cmd.set_mesh_positions", {
        "mesh_ids": mesh_ids,
        "positions": positions
    })
    return result

# ============================================
# Scene Endpoints
# ============================================

@app.get("/api/v1/scene/info")
async def get_scene_info():
    """Get scene information"""
    total = call_unity("spz.cmd.get_total_mesh_count", {})
    selected = call_unity("spz.cmd.get_selected_mesh_count", {})
    return {
        "total_meshes": total.get("count", 0),
        "selected_meshes": selected.get("count", 0)
    }

@app.post("/api/v1/scene/select_all")
async def select_all_meshes():
    """Select all meshes"""
    result = call_unity("spz.cmd.select_all_meshes", {})
    return result

@app.post("/api/v1/scene/deselect_all")
async def deselect_all_meshes():
    """Deselect all meshes"""
    result = call_unity("spz.cmd.deselect_all_meshes", {})
    return result

# ============================================
# Stable Diffusion Endpoints
# ============================================

@app.get("/api/v1/sd/prompt")
async def get_sd_prompt():
    """Get Stable Diffusion prompts"""
    positive = call_unity("spz.cmd.get_positive_prompt", {})
    negative = call_unity("spz.cmd.get_negative_prompt", {})
    return {
        "positive": positive.get("prompt", ""),
        "negative": negative.get("prompt", "")
    }

@app.post("/api/v1/sd/prompt")
async def set_sd_prompt(prompt: Prompt):
    """Set Stable Diffusion prompts"""
    results = {}
    if prompt.positive is not None:
        results["positive"] = call_unity("spz.cmd.set_positive_prompt", {
            "prompt": prompt.positive
        })
    if prompt.negative is not None:
        results["negative"] = call_unity("spz.cmd.set_negative_prompt", {
            "prompt": prompt.negative
        })
    return results

@app.post("/api/v1/sd/generate")
async def trigger_sd_generation():
    """Trigger Stable Diffusion generation"""
    result = call_unity("spz.cmd.trigger_generation", {})
    return result

@app.get("/api/v1/sd/status")
async def get_sd_status():
    """Get Stable Diffusion status"""
    is_generating = call_unity("spz.cmd.is_generating", {})
    is_connected = call_unity("spz.cmd.is_connected", {})
    return {
        "generating": is_generating.get("is_generating", False),
        "connected": is_connected.get("is_connected", False)
    }

@app.post("/api/v1/sd/stop")
async def stop_sd_generation():
    """Stop Stable Diffusion generation"""
    result = call_unity("spz.cmd.stop_generation", {})
    return result

# ============================================
# Project Endpoints
# ============================================

@app.get("/api/v1/project/info")
async def get_project_info():
    """Get project information"""
    path = call_unity("spz.cmd.get_project_path", {})
    version = call_unity("spz.cmd.get_project_version", {})
    data_dir = call_unity("spz.cmd.get_project_data_dir", {})
    return {
        "path": path.get("filepath", ""),
        "version": version.get("version", ""),
        "data_dir": data_dir.get("data_dir", "")
    }

@app.post("/api/v1/project/save")
async def save_project(project_path: ProjectPath):
    """Save project"""
    result = call_unity("spz.cmd.save_project", {
        "filepath": project_path.filepath
    })
    return result

@app.post("/api/v1/project/load")
async def load_project(project_path: ProjectPath):
    """Load project"""
    result = call_unity("spz.cmd.load_project", {
        "filepath": project_path.filepath
    })
    return result

# ============================================
# Health Check
# ============================================

@app.get("/")
async def root():
    """Root endpoint - API info"""
    return {
        "name": "StableProjectorz API",
        "version": "1.0.0",
        "docs": "/docs",
        "status": "running"
    }

@app.get("/health")
async def health():
    """Health check endpoint"""
    if _api is None:
        return {"status": "disconnected", "unity": False}
    try:
        # Try a simple call to Unity
        call_unity("spz.cmd.get_total_mesh_count", {})
        return {"status": "connected", "unity": True}
    except:
        return {"status": "disconnected", "unity": False}

def start_server(host: str = "127.0.0.1", port: int = 5557):
    """Start the FastAPI server"""
    print(f"[HTTP Server] Starting FastAPI server on http://{host}:{port}")
    print(f"[HTTP Server] API docs available at http://{host}:{port}/docs")
    uvicorn.run(app, host=host, port=port, log_level="info")
