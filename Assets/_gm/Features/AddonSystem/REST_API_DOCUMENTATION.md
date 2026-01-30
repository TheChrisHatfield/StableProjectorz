# REST API Documentation

## Overview
The StableProjectorz Add-on System provides a REST API for web and remote clients. The API is powered by **FastAPI** (running in the Python add-on server) and uses standard HTTP methods (GET, POST) with JSON responses.

**Base URL:** `http://localhost:5557/api/v1`

**Interactive API Documentation:** `http://localhost:5557/docs` (Swagger UI)

**Alternative Docs:** `http://localhost:5557/redoc` (ReDoc)

## Installation
The HTTP server requires FastAPI and uvicorn. Install dependencies:

```bash
pip install -r StreamingAssets/AddonSystem/requirements.txt
```

Or manually:
```bash
pip install fastapi uvicorn
```

## Authentication
Currently, the API has no authentication (localhost only). Future versions may add API keys or tokens.

## CORS
CORS is enabled by default, allowing cross-origin requests from web browsers. The FastAPI server includes CORS middleware configured for all origins.

## Endpoints

### Cameras

#### Get All Camera Positions
```http
GET /api/v1/cameras
```

**Response:**
```json
{
  "data": {
    "positions": [
      {"x": 0.0, "y": 0.0, "z": 0.0},
      {"x": 1.0, "y": 2.0, "z": 3.0}
    ]
  }
}
```

#### Get Camera Position
```http
GET /api/v1/cameras/{camera_index}/position
```

**Response:**
```json
{
  "success": true,
  "x": 0.0,
  "y": 0.0,
  "z": 0.0
}
```

#### Set Camera Position
```http
POST /api/v1/cameras/{camera_index}/position
Content-Type: application/json

{
  "x": 1.0,
  "y": 2.0,
  "z": 3.0
}
```

**Response:**
```json
{
  "success": true
}
```

#### Get Camera Rotation
```http
GET /api/v1/cameras/{camera_index}/rotation
```

**Response:**
```json
{
  "success": true,
  "x": 0.0,
  "y": 0.0,
  "z": 0.0,
  "w": 1.0
}
```

#### Set Camera Rotation
```http
POST /api/v1/cameras/{camera_index}/rotation
Content-Type: application/json

{
  "x": 0.0,
  "y": 0.0,
  "z": 0.0,
  "w": 1.0
}
```

#### Get Camera FOV
```http
GET /api/v1/cameras/{camera_index}/fov
```

#### Set Camera FOV
```http
POST /api/v1/cameras/{camera_index}/fov
Content-Type: application/json

{
  "fov": 60.0
}
```

### Meshes

#### Get All Mesh IDs
```http
GET /api/v1/meshes
```

**Response:**
```json
{
  "data": {
    "mesh_ids": [1, 2, 3, 4, 5]
  }
}
```

#### Get Mesh Info
```http
GET /api/v1/meshes/{mesh_id}
```

**Response:**
```json
{
  "position": {"x": 0.0, "y": 0.0, "z": 0.0},
  "rotation": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0},
  "scale": {"x": 1.0, "y": 1.0, "z": 1.0}
}
```

#### Get Mesh Position
```http
GET /api/v1/meshes/{mesh_id}/position
```

#### Set Mesh Position
```http
POST /api/v1/meshes/{mesh_id}/position
Content-Type: application/json

{
  "x": 1.0,
  "y": 2.0,
  "z": 3.0
}
```

#### Select Mesh
```http
POST /api/v1/meshes/{mesh_id}/select
```

#### Batch Set Mesh Positions
```http
POST /api/v1/meshes/batch/position?action=position
Content-Type: application/json

{
  "mesh_ids": [1, 2, 3],
  "positions": [
    {"x": 1.0, "y": 2.0, "z": 3.0},
    {"x": 4.0, "y": 5.0, "z": 6.0},
    {"x": 7.0, "y": 8.0, "z": 9.0}
  ]
}
```

### Scene

#### Get Scene Info
```http
GET /api/v1/scene/info
```

**Response:**
```json
{
  "total_meshes": 10,
  "selected_meshes": 2
}
```

### Stable Diffusion

#### Get SD Status
```http
GET /api/v1/sd/status
```

**Response:**
```json
{
  "generating": false,
  "connected": true
}
```

#### Get Prompts
```http
GET /api/v1/sd/prompt
```

**Response:**
```json
{
  "positive": "a beautiful landscape",
  "negative": "blurry, low quality"
}
```

#### Set Prompts
```http
POST /api/v1/sd/prompt
Content-Type: application/json

{
  "positive": "a beautiful landscape",
  "negative": "blurry, low quality"
}
```

#### Trigger Generation
```http
POST /api/v1/sd/generate
```

### Projection Cameras

#### Get Projection Camera Count
```http
GET /api/v1/projection
```

#### Get Projection Camera Position
```http
GET /api/v1/projection/{camera_index}/position
```

#### Set Projection Camera Position
```http
POST /api/v1/projection/{camera_index}/position
Content-Type: application/json

{
  "x": 1.0,
  "y": 2.0,
  "z": 3.0
}
```

### Project

#### Get Project Info
```http
GET /api/v1/project/info
```

**Response:**
```json
{
  "path": "C:/Projects/myproject.spz",
  "version": "2.4.5"
}
```

#### Save Project
```http
POST /api/v1/project/save
Content-Type: application/json

{
  "filepath": "C:/Projects/myproject.spz"
}
```

**Note:** If `filepath` is omitted, a save dialog will be shown.

#### Load Project
```http
POST /api/v1/project/load
Content-Type: application/json

{
  "filepath": "C:/Projects/myproject.spz"
}
```

**Note:** If `filepath` is omitted, a load dialog will be shown.

### ControlNet

#### Get ControlNet Info
```http
GET /api/v1/controlnet
```

**Response:**
```json
{
  "total_units": 4,
  "active_units": 2
}
```

#### Get ControlNet Unit Enabled
```http
GET /api/v1/controlnet/{unit_index}/enabled
```

#### Set ControlNet Unit Enabled
```http
POST /api/v1/controlnet/{unit_index}/enabled
Content-Type: application/json

{
  "enabled": true
}
```

#### Get ControlNet Unit Weight
```http
GET /api/v1/controlnet/{unit_index}/weight
```

#### Set ControlNet Unit Weight
```http
POST /api/v1/controlnet/{unit_index}/weight
Content-Type: application/json

{
  "weight": 1.0
}
```

### Background/Skybox

#### Get Background Color
```http
GET /api/v1/background/color
```

**Response:**
```json
{
  "top": {"r": 0.5, "g": 0.5, "b": 0.5, "a": 1.0},
  "bottom": {"r": 0.2, "g": 0.2, "b": 0.2, "a": 1.0}
}
```

#### Set Background Color
```http
POST /api/v1/background/color
Content-Type: application/json

{
  "r": 0.5,
  "g": 0.5,
  "b": 0.5,
  "a": 1.0
}
```

### Workflow

#### Get Workflow Mode
```http
GET /api/v1/workflow/mode
```

#### Set Workflow Mode
```http
POST /api/v1/workflow/mode
Content-Type: application/json

{
  "mode": "3D"
}
```

## Error Responses

All errors return JSON with an `error` field:

```json
{
  "error": "Camera ID required",
  "status": 400
}
```

**HTTP Status Codes:**
- `200` - Success
- `400` - Bad Request (invalid parameters)
- `404` - Not Found (invalid endpoint)
- `500` - Internal Server Error

## Example Usage

### cURL Examples

```bash
# Get camera position
curl http://localhost:5557/api/v1/cameras/0/position

# Set camera position
curl -X POST http://localhost:5557/api/v1/cameras/0/position \
  -H "Content-Type: application/json" \
  -d '{"x": 1.0, "y": 2.0, "z": 3.0}'

# Get scene info
curl http://localhost:5557/api/v1/scene/info

# Trigger SD generation
curl -X POST http://localhost:5557/api/v1/sd/generate
```

### JavaScript Example

```javascript
// Get camera position
fetch('http://localhost:5557/api/v1/cameras/0/position')
  .then(response => response.json())
  .then(data => console.log(data));

// Set camera position
fetch('http://localhost:5557/api/v1/cameras/0/position', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
  },
  body: JSON.stringify({
    x: 1.0,
    y: 2.0,
    z: 3.0
  })
})
  .then(response => response.json())
  .then(data => console.log(data));
```

### Python Example

```python
import requests

# Get camera position
response = requests.get('http://localhost:5557/api/v1/cameras/0/position')
print(response.json())

# Set camera position
response = requests.post(
    'http://localhost:5557/api/v1/cameras/0/position',
    json={'x': 1.0, 'y': 2.0, 'z': 3.0}
)
print(response.json())
```

## Notes

- All endpoints require the API to be running (HTTP server enabled in Unity)
- Port 5557 is the default, but can be configured in `Addon_MGR`
- All float values are validated and clamped to reasonable ranges
- Batch operations are limited to 1000 items per request
- CORS is enabled by default for web clients
