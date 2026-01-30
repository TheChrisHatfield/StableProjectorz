# StableProjectorz Add-on System

A powerful API system that allows you to control and automate StableProjectorz from external programs, scripts, and web applications.

## 🚀 Quick Start

### For Python Users

```python
import spz

# Connect to StableProjectorz
api = spz.get_api()

# Move camera
api.cameras.set_pos(0, 1.0, 2.0, 3.0)

# Get scene info
print(f"Total meshes: {api.scene.get_total_mesh_count()}")
```

### For Web/HTTP Users

```bash
# Get scene information
curl http://localhost:5557/api/v1/scene/info

# Move camera
curl -X POST http://localhost:5557/api/v1/cameras/0/position \
  -H "Content-Type: application/json" \
  -d '{"x": 1.0, "y": 2.0, "z": 3.0}'
```

## 📚 Documentation

- **[Getting Started Guide](GETTING_STARTED.md)** - Start here if you're new!
- **[REST API Documentation](REST_API_DOCUMENTATION.md)** - Complete HTTP API reference
- **[Hybrid API Implementation](HYBRID_API_IMPLEMENTATION.md)** - Architecture overview
- **[Edge Cases Analysis](EDGE_CASES_ANALYSIS.md)** - Error handling and validation

## 🎯 Features

### ✅ What You Can Do

- **Camera Control** - Move, rotate, adjust FOV
- **Mesh Operations** - Position, rotation, scale, selection
- **Scene Information** - Get mesh counts, bounds, IDs
- **Stable Diffusion** - Set prompts, trigger generation, check status
- **Projection Cameras** - Control projection camera positions
- **Project Management** - Save/load projects programmatically
- **ControlNet** - Enable/disable units, set weights
- **Background/Skybox** - Adjust colors and gradients
- **UI Creation** - Create panels, buttons, sliders, inputs, dropdowns

### 🔌 Two Connection Methods

1. **Python API (TCP)** - Port 5555
   - Fast, efficient for Python scripts
   - Persistent connections
   - Use `spz.py` library

2. **HTTP REST API** - Port 5557
   - Works with any HTTP client
   - Perfect for web applications
   - No library needed

## 📦 Installation

### Python

1. Copy `spz.py` from `Assets/StreamingAssets/AddonSystem/spz.py` to your project
2. That's it! No dependencies required.

### HTTP/Web

No installation needed! Just make HTTP requests to `http://localhost:5557/api/v1/`

## 💡 Examples

### Python Example: Camera Animation

```python
import spz
import math
import time

api = spz.get_api()

# Animate camera in a circle
for i in range(360):
    angle = i * math.pi / 180
    x = 5 * math.cos(angle)
    z = 5 * math.sin(angle)
    api.cameras.set_pos(0, x, 2.0, z)
    time.sleep(0.1)
```

### JavaScript Example: Web Dashboard

```javascript
// Get scene info
fetch('http://localhost:5557/api/v1/scene/info')
  .then(r => r.json())
  .then(data => {
    console.log(`Total meshes: ${data.total_meshes}`);
  });

// Move camera
fetch('http://localhost:5557/api/v1/cameras/0/position', {
  method: 'POST',
  headers: {'Content-Type': 'application/json'},
  body: JSON.stringify({x: 1.0, y: 2.0, z: 3.0})
});
```

### Batch Operations (Python)

```python
import spz

api = spz.get_api()

# Get all meshes
mesh_ids = api.scene.get_all_mesh_ids()

# Move all meshes up by 1 unit (batch operation - fast!)
positions = []
for mesh_id in mesh_ids:
    pos = api.models.get_pos(mesh_id)
    if pos:
        positions.append((pos['x'], pos['y'] + 1.0, pos['z']))

api.models.set_positions(mesh_ids, positions)
```

## 🏗️ Architecture

The add-on system uses a hybrid approach:

```
Python Addons  ──TCP (5555)──┐
                              ├──► JSON-RPC Dispatcher
Web/Remote     ──HTTP (5557)──┤
                              │
Real-time Apps ──WS (5558)───┘  (Future)
```

- **TCP JSON-RPC** - For Python add-ons (efficient, persistent)
- **HTTP REST API** - For web/remote clients (standard, flexible)
- **WebSocket** - Planned for real-time features (optional)

## 📋 API Overview

### Python API Structure

```python
api.cameras      # Camera operations
api.models       # Mesh/3D model operations
api.scene        # Scene information
api.sd           # Stable Diffusion
api.projection   # Projection cameras
api.project      # Project save/load
api.controlnet   # ControlNet settings
api.background   # Skybox/background
api.ui           # UI creation
```

### HTTP REST API Structure

All endpoints: `http://localhost:5557/api/v1/{resource}/{id}/{action}`

Examples:
- `GET /api/v1/cameras/0/position` - Get camera position
- `POST /api/v1/cameras/0/position` - Set camera position
- `GET /api/v1/scene/info` - Get scene information
- `POST /api/v1/sd/generate` - Trigger generation

## 🔧 Configuration

### Enable/Disable Servers

In Unity Inspector (`Addon_MGR` component):
- `_enableHttpServer` - Enable HTTP REST API (default: true)
- `_httpServerPort` - HTTP server port (default: 5557)
- `_serverPort` - TCP server port (default: 5555)

### CORS Settings

In Unity Inspector (`Addon_HttpServer` component):
- `_enableCors` - Enable CORS (default: true)
- `_allowedOrigins` - Allowed origins (default: "*" for all)

## 🐛 Troubleshooting

### Connection Issues

**Problem:** "Connection refused" or "Failed to connect"

**Solutions:**
1. Make sure StableProjectorz is running
2. Check Unity console for add-on system startup messages
3. Verify ports (5555 for Python, 5557 for HTTP)
4. Check firewall settings

### Python Import Errors

**Problem:** Can't import `spz` module

**Solutions:**
1. Copy `spz.py` to your project folder
2. Or add the folder to Python path:
   ```python
   import sys
   sys.path.append('/path/to/spz.py')
   ```

### CORS Errors (Browser)

**Problem:** Browser blocks requests

**Solutions:**
1. CORS is enabled by default
2. Make sure you're accessing from `localhost`
3. Check `_allowedOrigins` setting in Unity

## 📖 Full Documentation

- **[Getting Started](GETTING_STARTED.md)** - Beginner-friendly guide
- **[REST API Reference](REST_API_DOCUMENTATION.md)** - Complete HTTP API docs
- **[Edge Cases](EDGE_CASES_ANALYSIS.md)** - Error handling guide
- **[Implementation Details](HYBRID_API_IMPLEMENTATION.md)** - Architecture deep dive

## 🤝 Contributing

Found a bug or want to add a feature? Check the codebase structure:

- `FastPath_API.cs` - Core API implementation
- `Addon_SocketServer.cs` - TCP JSON-RPC server
- `Addon_HttpServer.cs` - HTTP REST API server
- `spz.py` - Python client library

## 📝 License

Part of the StableProjectorz project. See main project license.

## 🙏 Acknowledgments

Built for the StableProjectorz community to enable powerful automation and integration capabilities.

---

**Ready to get started?** Check out the [Getting Started Guide](GETTING_STARTED.md)!
