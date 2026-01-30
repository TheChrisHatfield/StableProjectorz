# StableProjectorz Add-on System - User Guide

## Table of Contents

1. [Introduction](#introduction)
2. [What Can You Do?](#what-can-you-do)
3. [Getting Started](#getting-started)
4. [Python API Guide](#python-api-guide)
5. [HTTP REST API Guide](#http-rest-api-guide)
6. [Common Use Cases](#common-use-cases)
7. [Troubleshooting](#troubleshooting)
8. [Advanced Topics](#advanced-topics)

## Introduction

The StableProjectorz Add-on System is like having a remote control for StableProjectorz. Instead of clicking buttons in the Unity interface, you can write scripts or use web pages to control everything programmatically.

### Why Use This?

- **Automation** - Automate repetitive tasks
- **Integration** - Connect StableProjectorz with other tools
- **Custom Tools** - Build your own control panels and dashboards
- **Batch Operations** - Process multiple items at once
- **Remote Control** - Control StableProjectorz from another computer

## What Can You Do?

### Camera Control
- Move cameras to any position
- Rotate cameras
- Adjust field of view (FOV)
- Get current camera settings

### Mesh/3D Model Operations
- Move, rotate, and scale meshes
- Select/deselect meshes
- Get mesh information (position, bounds, name)
- Batch operations on multiple meshes

### Scene Information
- Count total meshes
- Count selected meshes
- Get all mesh IDs
- Get combined bounds

### Stable Diffusion
- Set positive and negative prompts
- Trigger texture generation
- Check generation status
- Check if SD service is connected

### Project Management
- Save projects programmatically
- Load projects
- Get project information (path, version)

### And More!
- ControlNet settings
- Background/skybox colors
- Projection camera control
- Create custom UI elements

## Getting Started

### Prerequisites

- StableProjectorz running
- Python 3.x (for Python API) OR
- Any HTTP client (for REST API)

### Step 1: Start StableProjectorz

The add-on system starts automatically when you launch StableProjectorz. You should see messages in the Unity console like:

```
[Addon_SocketServer] Started listening on port 5555
[Addon_HttpServer] Started HTTP server on port 5557
```

### Step 2: Choose Your Method

**Option A: Python (Recommended for automation)**
- Use the `spz.py` library
- Fast and efficient
- Great for scripts

**Option B: HTTP REST (Recommended for web/other languages)**
- Use standard HTTP requests
- Works with any language
- Perfect for web dashboards

### Step 3: Test Your Connection

**Python:**
```python
import spz
api = spz.get_api()
print(api.scene.get_total_mesh_count())
```

**HTTP:**
```bash
curl http://localhost:5557/api/v1/scene/info
```

If you get a response, you're connected! 🎉

## Python API Guide

### Installing Add-ons

StableProjectorz makes it easy to install add-ons:

**Drag-and-Drop Installation:**
1. Create a zip file containing your add-on (with `__init__.py`)
2. Drag the zip file into the StableProjectorz window
3. Wait for the success message - that's it!

**File Browser Installation:**
1. Open Add-on Manager panel
2. Click "Install from File"
3. Select your zip file
4. Installation happens automatically

**Managing Add-ons:**
- View all installed add-ons in the Add-on Manager
- Enable/disable add-ons with toggles
- Remove add-ons with confirmation dialog
- Refresh the list to see newly installed add-ons

See [Add-on Installation Guide](ADDON_INSTALLATION.md) for detailed information.

### Python API Library Installation

1. Find `spz.py` in `Assets/StreamingAssets/AddonSystem/spz.py`
2. Copy it to your Python project folder
3. That's it! No pip install needed.

### Basic Usage

```python
import spz

# Get API instance
api = spz.get_api()

# Use the API
api.cameras.set_pos(0, 1.0, 2.0, 3.0)
```

### API Modules

#### Cameras
```python
# Set camera position
api.cameras.set_pos(0, x=1.0, y=2.0, z=3.0)

# Get camera position
pos = api.cameras.get_pos(0)
print(f"Camera at: {pos['x']}, {pos['y']}, {pos['z']}")

# Set rotation
api.cameras.set_rot(0, x=0, y=0, z=0, w=1)

# Set FOV
api.cameras.set_fov(0, 60.0)
```

#### Meshes/Models
```python
# Get all mesh IDs
mesh_ids = api.scene.get_all_mesh_ids()

# Move a mesh
api.models.set_pos(mesh_id, 1.0, 2.0, 3.0)

# Get mesh position
pos = api.models.get_pos(mesh_id)

# Select a mesh
api.models.select(mesh_id)

# Batch operations (faster!)
api.models.set_positions([1, 2, 3], [(1,2,3), (4,5,6), (7,8,9)])
```

#### Scene Information
```python
# Get counts
total = api.scene.get_total_mesh_count()
selected = api.scene.get_selected_mesh_count()

# Get all mesh IDs
all_ids = api.scene.get_all_mesh_ids()
```

#### Stable Diffusion
```python
# Set prompts
api.sd.set_positive_prompt("a beautiful landscape")
api.sd.set_negative_prompt("blurry, low quality")

# Trigger generation
api.sd.trigger_generation()

# Check status
is_generating = api.sd.is_generating()
is_connected = api.sd.is_connected()
```

#### Projects
```python
# Save project
api.project.save("path/to/project.spz")

# Load project
api.project.load("path/to/project.spz")

# Get project info
path = api.project.get_path()
version = api.project.get_version()
```

### Error Handling

```python
import spz

try:
    api = spz.get_api()
    api.cameras.set_pos(0, 1.0, 2.0, 3.0)
except ConnectionError as e:
    print(f"Connection failed: {e}")
except Exception as e:
    print(f"Error: {e}")
```

## HTTP REST API Guide

### Base URL

All endpoints start with: `http://localhost:5557/api/v1`

### Making Requests

**GET Request** (read data):
```bash
curl http://localhost:5557/api/v1/scene/info
```

**POST Request** (send data):
```bash
curl -X POST http://localhost:5557/api/v1/cameras/0/position \
  -H "Content-Type: application/json" \
  -d '{"x": 1.0, "y": 2.0, "z": 3.0}'
```

### Common Endpoints

#### Get Camera Position
```http
GET /api/v1/cameras/0/position
```

Response:
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
POST /api/v1/cameras/0/position
Content-Type: application/json

{
  "x": 1.0,
  "y": 2.0,
  "z": 3.0
}
```

#### Get Scene Info
```http
GET /api/v1/scene/info
```

Response:
```json
{
  "total_meshes": 10,
  "selected_meshes": 2
}
```

#### Trigger Generation
```http
POST /api/v1/sd/generate
```

### Using from JavaScript

```javascript
// Get scene info
async function getSceneInfo() {
  const response = await fetch('http://localhost:5557/api/v1/scene/info');
  const data = await response.json();
  console.log(data);
}

// Move camera
async function moveCamera(x, y, z) {
  const response = await fetch('http://localhost:5557/api/v1/cameras/0/position', {
    method: 'POST',
    headers: {'Content-Type': 'application/json'},
    body: JSON.stringify({x, y, z})
  });
  const result = await response.json();
  console.log(result);
}
```

### Using from Python (requests library)

```python
import requests

# Get scene info
response = requests.get('http://localhost:5557/api/v1/scene/info')
data = response.json()
print(data)

# Move camera
response = requests.post(
    'http://localhost:5557/api/v1/cameras/0/position',
    json={'x': 1.0, 'y': 2.0, 'z': 3.0}
)
print(response.json())
```

## Common Use Cases

### Use Case 1: Camera Animation

**Python:**
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

### Use Case 2: Batch Mesh Processing

**Python:**
```python
import spz

api = spz.get_api()

# Get all meshes
mesh_ids = api.scene.get_all_mesh_ids()

# Move all meshes up by 1 unit
positions = []
for mesh_id in mesh_ids:
    pos = api.models.get_pos(mesh_id)
    if pos:
        positions.append((pos['x'], pos['y'] + 1.0, pos['z']))

# Batch update (much faster than individual calls)
api.models.set_positions(mesh_ids, positions)
```

### Use Case 3: Web Dashboard

**HTML/JavaScript:**
```html
<!DOCTYPE html>
<html>
<head>
    <title>StableProjectorz Control</title>
    <style>
        body { font-family: Arial; padding: 20px; }
        button { padding: 10px; margin: 5px; }
        #info { margin-top: 20px; }
    </style>
</head>
<body>
    <h1>StableProjectorz Control Panel</h1>
    
    <button onclick="getInfo()">Get Scene Info</button>
    <button onclick="generate()">Generate</button>
    <button onclick="moveCamera()">Move Camera</button>
    
    <div id="info"></div>
    
    <script>
    const API_BASE = 'http://localhost:5557/api/v1';
    
    async function getInfo() {
        const response = await fetch(`${API_BASE}/scene/info`);
        const data = await response.json();
        document.getElementById('info').innerHTML = 
            `<h3>Scene Info</h3>
             <p>Total Meshes: ${data.total_meshes}</p>
             <p>Selected Meshes: ${data.selected_meshes}</p>`;
    }
    
    async function generate() {
        await fetch(`${API_BASE}/sd/generate`, {method: 'POST'});
        alert('Generation started!');
    }
    
    async function moveCamera() {
        await fetch(`${API_BASE}/cameras/0/position`, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({x: 1.0, y: 2.0, z: 3.0})
        });
        alert('Camera moved!');
    }
    </script>
</body>
</html>
```

### Use Case 4: Automated Workflow

**Python:**
```python
import spz
import time

api = spz.get_api()

# Automated workflow
def automated_workflow():
    # 1. Set prompts
    api.sd.set_positive_prompt("a beautiful landscape")
    api.sd.set_negative_prompt("blurry")
    
    # 2. Position camera
    api.cameras.set_pos(0, 0, 5, 10)
    
    # 3. Trigger generation
    api.sd.trigger_generation()
    
    # 4. Wait for completion
    while api.sd.is_generating():
        time.sleep(1)
    
    # 5. Save project
    api.project.save("output/result.spz")
    
    print("Workflow complete!")

automated_workflow()
```

## Troubleshooting

### Problem: "Connection refused"

**Causes:**
- StableProjectorz isn't running
- Add-on system didn't start
- Wrong port number

**Solutions:**
1. Make sure StableProjectorz is running
2. Check Unity console for startup messages
3. Verify ports: 5555 (Python) or 5557 (HTTP)

### Problem: "Method not found"

**Causes:**
- Typo in method name
- Using wrong API version
- Method doesn't exist

**Solutions:**
1. Check API documentation for correct method names
2. Verify you're using the latest API
3. Check error message for suggestions

### Problem: Python can't find `spz` module

**Causes:**
- `spz.py` not in Python path
- Wrong file location

**Solutions:**
1. Copy `spz.py` to your project folder
2. Or add to Python path:
   ```python
   import sys
   sys.path.append('/path/to/spz.py')
   ```

### Problem: CORS errors in browser

**Causes:**
- Browser blocking cross-origin requests
- CORS not enabled

**Solutions:**
1. CORS is enabled by default
2. Make sure you're accessing from `localhost`
3. Check `_allowedOrigins` in Unity inspector

### Problem: Commands not working

**Causes:**
- API not initialized
- Invalid parameters
- Unity not ready

**Solutions:**
1. Wait a few seconds after starting StableProjectorz
2. Check parameter types (floats vs ints)
3. Verify values are in valid ranges
4. Check Unity console for error messages

## Advanced Topics

### Batch Operations

Batch operations are much faster than individual calls:

```python
# Slow: Individual calls
for mesh_id in mesh_ids:
    api.models.set_pos(mesh_id, 1.0, 2.0, 3.0)

# Fast: Batch operation
positions = [(1.0, 2.0, 3.0)] * len(mesh_ids)
api.models.set_positions(mesh_ids, positions)
```

### Error Handling Best Practices

```python
import spz

def safe_operation():
    try:
        api = spz.get_api()
        
        # Check if operation is valid
        if not api.sd.is_connected():
            print("SD not connected!")
            return
        
        # Perform operation
        api.sd.trigger_generation()
        
    except ConnectionError:
        print("Failed to connect to StableProjectorz")
    except Exception as e:
        print(f"Error: {e}")

safe_operation()
```

### Creating Custom UI

```python
import spz

api = spz.get_api()

# Create a panel
panel = api.ui.create_panel("MyAddon", "My Add-on Panel")

# Add UI elements
slider_id = panel.add_slider("Intensity", 0.0, 1.0, 0.5)
text_id = panel.add_text_input("Name", "default")
dropdown_id = panel.add_dropdown("Mode", ["Fast", "Normal", "Slow"], 0)

# Get values
intensity = panel.get_value(slider_id)
name = panel.get_value(text_id)
```

### Performance Tips

1. **Use batch operations** when possible
2. **Cache results** if you need them multiple times
3. **Check status** before operations (e.g., `is_connected()`)
4. **Handle errors gracefully** to avoid crashes

## Next Steps

1. **Explore the API** - Try different methods
2. **Build Tools** - Create your own automation scripts
3. **Share** - Share your add-ons with the community
4. **Contribute** - Help improve the API

## Resources

- [Getting Started Guide](GETTING_STARTED.md) - Quick start
- [REST API Documentation](REST_API_DOCUMENTATION.md) - Complete HTTP API
- [Implementation Details](HYBRID_API_IMPLEMENTATION.md) - Architecture
- Example add-ons in `Assets/StreamingAssets/Addons/`

---

**Happy automating!** 🚀

If you have questions or need help, check the documentation or explore the example add-ons.
