# Getting Started with StableProjectorz Add-on System

## What is This?

The StableProjectorz Add-on System lets you control and automate StableProjectorz from external programs, scripts, or web pages. Think of it like a remote control for StableProjectorz - you can move cameras, adjust meshes, trigger generations, and more, all from outside the Unity application.

## Who Can Use This?

- **Python Developers** - Write Python scripts to automate workflows
- **Web Developers** - Create web dashboards and control panels
- **Automation Enthusiasts** - Build custom tools and integrations
- **Anyone** - Even if you're not a programmer, you can use simple tools like cURL or Postman

## Quick Start (5 Minutes)

### For Python Users

1. **Make sure StableProjectorz is running**
   - The add-on system starts automatically when you launch StableProjectorz

2. **Create a simple Python script:**
   ```python
   import spz
   
   # Connect to StableProjectorz
   api = spz.get_api()
   
   # Move camera
   api.cameras.set_pos(0, 1.0, 2.0, 3.0)
   
   # Get scene info
   total_meshes = api.scene.get_total_mesh_count()
   print(f"Total meshes: {total_meshes}")
   ```

3. **Save it as `test.py` and run:**
   ```bash
   python test.py
   ```

That's it! You're controlling StableProjectorz from Python.

### For Web/HTTP Users

1. **Make sure StableProjectorz is running**

2. **Open your browser's developer console** (F12)

3. **Try this:**
   ```javascript
   fetch('http://localhost:5557/api/v1/scene/info')
     .then(r => r.json())
     .then(data => console.log(data));
   ```

4. **Or use cURL in terminal:**
   ```bash
   curl http://localhost:5557/api/v1/scene/info
   ```

## Two Ways to Connect

### Option 1: Python API (Recommended for Python)
- **Port:** 5555
- **Protocol:** TCP (fast, efficient)
- **Best for:** Python scripts, automation
- **Library:** Use `spz.py` (included)

### Option 2: HTTP REST API (Recommended for Web/Other)
- **Port:** 5557
- **Protocol:** HTTP (standard web protocol)
- **Best for:** Web pages, JavaScript, other languages
- **No library needed:** Just use HTTP requests

## Installation

### For Python Users

1. **Copy the Python library:**
   - The `spz.py` file is located at: `Assets/StreamingAssets/AddonSystem/spz.py`
   - Copy it to your Python project folder

2. **That's it!** No pip install needed - it's a single file.

### For Web/HTTP Users

**No installation needed!** Just make HTTP requests to `http://localhost:5557/api/v1/`

## Common Tasks

### Move a Camera

**Python:**
```python
api.cameras.set_pos(0, 1.0, 2.0, 3.0)  # Camera 0, position (1, 2, 3)
```

**HTTP:**
```bash
curl -X POST http://localhost:5557/api/v1/cameras/0/position \
  -H "Content-Type: application/json" \
  -d '{"x": 1.0, "y": 2.0, "z": 3.0}'
```

**JavaScript:**
```javascript
fetch('http://localhost:5557/api/v1/cameras/0/position', {
  method: 'POST',
  headers: {'Content-Type': 'application/json'},
  body: JSON.stringify({x: 1.0, y: 2.0, z: 3.0})
});
```

### Get Scene Information

**Python:**
```python
total = api.scene.get_total_mesh_count()
selected = api.scene.get_selected_mesh_count()
print(f"Total: {total}, Selected: {selected}")
```

**HTTP:**
```bash
curl http://localhost:5557/api/v1/scene/info
```

### Trigger Texture Generation

**Python:**
```python
api.sd.trigger_generation()
```

**HTTP:**
```bash
curl -X POST http://localhost:5557/api/v1/sd/generate
```

### Set Prompts

**Python:**
```python
api.sd.set_positive_prompt("a beautiful landscape")
api.sd.set_negative_prompt("blurry, low quality")
```

**HTTP:**
```bash
curl -X POST http://localhost:5557/api/v1/sd/prompt \
  -H "Content-Type: application/json" \
  -d '{"positive": "a beautiful landscape", "negative": "blurry"}'
```

## Understanding the API Structure

### Python API Structure

```
api
├── cameras      # Camera control
├── models       # Mesh/3D model operations
├── scene        # Scene information
├── sd           # Stable Diffusion operations
├── projection   # Projection cameras
├── project      # Save/load projects
├── controlnet   # ControlNet settings
├── background   # Skybox/background
└── ui           # Create UI elements
```

### HTTP API Structure

All HTTP endpoints follow this pattern:
```
http://localhost:5557/api/v1/{resource}/{id}/{action}
```

Examples:
- `/api/v1/cameras/0/position` - Camera 0 position
- `/api/v1/meshes/1/position` - Mesh 1 position
- `/api/v1/scene/info` - Scene information

## Complete Examples

### Example 1: Simple Camera Animation (Python)

```python
import spz
import time

api = spz.get_api()

# Animate camera in a circle
for i in range(360):
    angle = i * 3.14159 / 180  # Convert to radians
    x = 5 * math.cos(angle)
    z = 5 * math.sin(angle)
    api.cameras.set_pos(0, x, 2.0, z)
    time.sleep(0.1)  # Wait 100ms
```

### Example 2: Web Dashboard (HTML/JavaScript)

```html
<!DOCTYPE html>
<html>
<head>
    <title>StableProjectorz Control</title>
</head>
<body>
    <h1>StableProjectorz Control Panel</h1>
    
    <button onclick="getSceneInfo()">Get Scene Info</button>
    <button onclick="triggerGeneration()">Generate</button>
    
    <div id="info"></div>
    
    <script>
    async function getSceneInfo() {
        const response = await fetch('http://localhost:5557/api/v1/scene/info');
        const data = await response.json();
        document.getElementById('info').innerHTML = 
            `Total Meshes: ${data.total_meshes}<br>Selected: ${data.selected_meshes}`;
    }
    
    async function triggerGeneration() {
        await fetch('http://localhost:5557/api/v1/sd/generate', {method: 'POST'});
        alert('Generation started!');
    }
    </script>
</body>
</html>
```

### Example 3: Batch Mesh Operations (Python)

```python
import spz

api = spz.get_api()

# Get all mesh IDs
mesh_ids = api.scene.get_all_mesh_ids()

# Move all meshes up by 1 unit
positions = []
for mesh_id in mesh_ids:
    pos = api.models.get_pos(mesh_id)
    if pos:
        positions.append((pos['x'], pos['y'] + 1.0, pos['z']))

# Batch update (faster than individual calls)
api.models.set_positions(mesh_ids, positions)
```

## Troubleshooting

### "Connection refused" or "Failed to connect"

**Problem:** StableProjectorz isn't running or the add-on system isn't started.

**Solution:**
1. Make sure StableProjectorz is running
2. Check the Unity console for add-on system messages
3. Verify the ports (5555 for Python, 5557 for HTTP)

### "Method not found" error

**Problem:** You're using an API method that doesn't exist.

**Solution:**
- Check the API documentation for available methods
- Make sure you're using the correct method name
- Verify you're using the right API version

### Python script can't find `spz` module

**Problem:** The `spz.py` file isn't in your Python path.

**Solution:**
1. Copy `spz.py` to your project folder, or
2. Add the folder containing `spz.py` to your Python path:
   ```python
   import sys
   sys.path.append('/path/to/spz.py/folder')
   import spz
   ```

### HTTP requests return CORS errors (browser)

**Problem:** Browser blocks cross-origin requests.

**Solution:**
- CORS is enabled by default
- If you still get errors, check the `_allowedOrigins` setting in Unity
- Make sure you're accessing from `localhost` or an allowed origin

## Next Steps

1. **Read the API Documentation:**
   - `REST_API_DOCUMENTATION.md` - Complete HTTP API reference
   - Python API is documented in `spz.py` (docstrings)

2. **Try the Examples:**
   - Start with simple tasks (get info, move camera)
   - Gradually try more complex operations

3. **Build Your Own Tools:**
   - Create custom dashboards
   - Automate repetitive tasks
   - Integrate with other tools

## Need Help?

- Check the API documentation files
- Look at example add-ons in `Assets/StreamingAssets/Addons/`
- Check Unity console for error messages
- Review the code comments in `spz.py`

## What's Next?

Once you're comfortable with the basics:
- Explore advanced features (batch operations, UI creation)
- Create your own add-ons
- Build web dashboards
- Automate complex workflows

Happy automating! 🚀
