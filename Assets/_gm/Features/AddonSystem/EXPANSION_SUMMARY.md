# Add-On System Expansion Summary

## Overview
The add-on system has been significantly expanded from a basic proof-of-concept to a comprehensive API covering most core StableProjectorz features.

## What Was Added

### 1. Mesh Operations (Complete Transform Control)
**Before:** Only position setting and selection
**Now:** Full transform control
- Position: Get/Set
- Rotation: Get/Set (quaternion)
- Scale: Get/Set
- Visibility: Get/Set (show/hide)
- Bounds: Get mesh bounding box
- Name: Get mesh name

### 2. Scene Information (Full Introspection)
**Before:** Only selected mesh IDs
**Now:** Complete scene statistics
- Total mesh count
- Selected mesh count
- All mesh IDs list
- Selected meshes bounds (combined bounding box)

### 3. Stable Diffusion Integration (Full Control)
**Before:** No SD access
**Now:** Complete prompt and generation control
- Get/Set positive prompt
- Get/Set negative prompt
- Trigger texture generation
- Check generation status
- Check SD service connection
- Check 3D generation service connection

### 4. Projection System (Read Access)
**Before:** No projection access
**Now:** Read projection camera information
- Get projection camera count
- Get projection camera position
- Get projection camera rotation

## API Structure

### C# Side (FastPath_API.cs)
All new methods follow the same pattern:
- Input validation (NaN, infinity checks)
- Value clamping (reasonable bounds)
- Safe singleton access
- Null checks and error handling

### Python Side (spz.py)
New API modules:
- `api.scene` - Scene information
- `api.sd` - Stable Diffusion operations
- `api.projection` - Projection cameras
- Expanded `api.models` - Full mesh operations

## Example Usage

```python
import spz

api = spz.get_api()

# Mesh operations
api.models.set_pos(mesh_id, 1.0, 2.0, 3.0)
api.models.set_rot(mesh_id, 0, 0, 0, 1)
api.models.set_scale(mesh_id, 2.0, 2.0, 2.0)
api.models.set_visibility(mesh_id, False)

# Scene information
total = api.scene.get_total_mesh_count()
selected = api.scene.get_selected_mesh_count()
all_ids = api.scene.get_all_mesh_ids()
bounds = api.scene.get_selected_meshes_bounds()

# Stable Diffusion
api.sd.set_positive_prompt("beautiful texture")
api.sd.set_negative_prompt("blurry")
api.sd.trigger_generation()
is_gen = api.sd.is_generating()

# Projection
count = api.projection.get_count()
pos = api.projection.get_pos(0)
```

## Example Add-Ons

1. **CameraTools** - Basic camera control (original)
2. **MeshTools** - Comprehensive mesh manipulation and SD integration (new)

## Limitations & Future Work

### Still Not Available
- Mesh material/texture access
- Depth map access
- Render texture access
- Painting/brushing operations
- Mask creation/modification
- Save/load operations
- Advanced UI elements (sliders, text fields, dropdowns)

### Why These Are Limited
- **Textures/Materials**: Require GPU-to-CPU transfers, complex serialization
- **Painting/Masks**: Require real-time input handling, complex state management
- **Save/Load**: Require full project serialization access
- **Advanced UI**: Require more complex UI prefab system

### Future Expansion Path
The architecture supports easy expansion. To add new features:
1. Add method to `FastPath_API.cs`
2. Add command handler to `Addon_SocketServer.cs`
3. Add Python method to `spz.py`
4. Update documentation

## Performance Considerations

- **Rate Limiting**: Camera operations limited to ~60fps
- **Validation**: All inputs validated and clamped
- **Thread Safety**: Commands marshaled to main thread
- **Error Handling**: Graceful failures, no crashes

## Testing Recommendations

1. Test with multiple meshes
2. Test with no meshes selected
3. Test SD connection states
4. Test generation during active generation
5. Test invalid mesh IDs
6. Test extreme values (very large/small numbers)
