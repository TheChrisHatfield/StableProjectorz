# Quick Reference Card

## Python API - Common Commands

```python
import spz
api = spz.get_api()

# Cameras
api.cameras.set_pos(0, x, y, z)
api.cameras.get_pos(0)
api.cameras.set_rot(0, x, y, z, w)
api.cameras.set_fov(0, fov)

# Meshes
api.models.set_pos(mesh_id, x, y, z)
api.models.get_pos(mesh_id)
api.models.select(mesh_id)
api.models.deselect(mesh_id)
api.models.set_visibility(mesh_id, True/False)

# Scene
api.scene.get_total_mesh_count()
api.scene.get_selected_mesh_count()
api.scene.get_all_mesh_ids()

# Stable Diffusion
api.sd.set_positive_prompt("text")
api.sd.set_negative_prompt("text")
api.sd.trigger_generation()
api.sd.is_generating()
api.sd.is_connected()

# Projects
api.project.save("path.spz")
api.project.load("path.spz")
api.project.get_path()
```

## HTTP REST API - Common Endpoints

**Base URL:** `http://localhost:5557/api/v1`  
**Interactive Docs:** `http://localhost:5557/docs` (FastAPI Swagger UI)  
**Powered by:** FastAPI (Python)

### GET Requests (Read Data)
```bash
# Scene info
GET /api/v1/scene/info

# Camera position
GET /api/v1/cameras/0/position

# All meshes
GET /api/v1/meshes

# Mesh position
GET /api/v1/meshes/1/position

# SD status
GET /api/v1/sd/status

# Project info
GET /api/v1/project/info
```

### POST Requests (Send Data)
```bash
# Move camera
POST /api/v1/cameras/0/position
Body: {"x": 1.0, "y": 2.0, "z": 3.0}

# Move mesh
POST /api/v1/meshes/1/position
Body: {"x": 1.0, "y": 2.0, "z": 3.0}

# Set prompts
POST /api/v1/sd/prompt
Body: {"positive": "text", "negative": "text"}

# Trigger generation
POST /api/v1/sd/generate

# Save project
POST /api/v1/project/save
Body: {"filepath": "path.spz"}
```

## cURL Examples

```bash
# Get scene info
curl http://localhost:5557/api/v1/scene/info

# Move camera
curl -X POST http://localhost:5557/api/v1/cameras/0/position \
  -H "Content-Type: application/json" \
  -d '{"x": 1.0, "y": 2.0, "z": 3.0}'

# Trigger generation
curl -X POST http://localhost:5557/api/v1/sd/generate
```

## JavaScript Examples

```javascript
// Get scene info
fetch('http://localhost:5557/api/v1/scene/info')
  .then(r => r.json())
  .then(console.log);

// Move camera
fetch('http://localhost:5557/api/v1/cameras/0/position', {
  method: 'POST',
  headers: {'Content-Type': 'application/json'},
  body: JSON.stringify({x: 1.0, y: 2.0, z: 3.0})
});
```

## Ports

- **5555** - Python API (TCP)
- **5557** - HTTP REST API
- **5558** - WebSocket (future)

## Common Errors

| Error | Solution |
|-------|----------|
| Connection refused | Make sure StableProjectorz is running |
| Method not found | Check method name spelling |
| CORS error | Access from localhost |
| Import error | Copy spz.py to project folder |

## Need More Help?

- [Getting Started Guide](GETTING_STARTED.md)
- [User Guide](USER_GUIDE.md)
- [REST API Documentation](REST_API_DOCUMENTATION.md)
