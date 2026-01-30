# Missing Features in Add-On System

## High Priority Gaps

### 1. Selection Batch Operations ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `SelectAllMeshes()` - Select all meshes in scene
- `DeselectAllMeshes()` - Deselect all meshes
- Python: `api.models.select_all()`, `api.models.deselect_all()`

### 2. Stop Generation ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `StopGeneration()` - Stop current SD generation
- Python: `api.sd.stop_generation()`

### 3. 3D Generation Trigger ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `Trigger3DGeneration()` - Trigger 3D model generation
- `Is3DGenerationReady()` - Check if 3D generation can start
- `Is3DGenerationInProgress()` - Check if 3D generation is running
- Python: `api.gen3d.trigger()`, `api.gen3d.is_ready()`, `api.gen3d.is_in_progress()`

### 4. Projection Camera Write Operations ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `SetProjectionCameraPosition(cameraIndex, x, y, z)`
- `SetProjectionCameraRotation(cameraIndex, x, y, z, w)`
- Python: `api.projection.set_pos()`, `api.projection.set_rot()`

## Medium Priority Gaps

### 5. Camera Rotation/FOV Getters ✅
**Status:** ALREADY IMPLEMENTED  
**Note:** We have `GetCameraRotation()` and `GetCameraFOV()` - these are complete

### 6. Batch Mesh Transform Operations ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `SetMeshPositions(List<ushort> meshIds, List<Vector3> positions)` - Batch position updates
- `SetMeshRotations(List<ushort> meshIds, List<Quaternion> rotations)` - Batch rotation updates
- `SetMeshScales(List<ushort> meshIds, List<Vector3> scales)` - Batch scale updates
- Python: `api.models.set_positions()`, `api.models.set_rotations()`, `api.models.set_scales()`

### 7. Export Operations ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `Export3DWithTextures()` - Export 3D model with textures
- `ExportProjectionTextures(isDilate)` - Export projection textures
- `ExportViewTextures()` - Export view textures
- Python: `api.export.export_3d_with_textures()`, etc.

### 8. Workflow Mode Control ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `GetWorkflowMode()` - Get current workflow mode
- `SetWorkflowMode(modeStr)` - Set workflow mode
- Python: `api.workflow.get_mode()`, `api.workflow.set_mode()`

### 9. Get All Camera Info ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `GetAllCameraPositions()` - Get positions of all cameras
- `GetAllCameraRotations()` - Get rotations of all cameras
- `GetAllCameraFOVs()` - Get FOVs of all cameras
- Python: `api.cameras.get_all_positions()`, `api.cameras.get_all_rotations()`, `api.cameras.get_all_fovs()`

## Low Priority / Complex Features

### 10. Texture/Material Access
**Status:** Very complex (GPU-to-CPU transfer, serialization)  
**Missing:**
- Access to render textures
- Access to depth maps
- Access to material properties
- Access to UDIM textures

**Why Complex:** Requires GPU readback, large data transfer, format conversion

### 11. Painting/Brushing Operations
**Status:** Very complex (real-time input handling)  
**Missing:**
- Paint on meshes
- Create/modify masks
- Brush operations

**Why Complex:** Requires real-time input simulation, complex state management

### 12. Save/Load Operations ✅ COMPLETED
**Status:** Implemented (dialog-based)  
**Added:**
- `SaveProject()` - Save project (shows file dialog)
- `LoadProject()` - Load project (shows file dialog)
- `GetProjectPath()` - Get current project filepath
- `GetProjectVersion()` - Get project version
- `GetProjectDataDir()` - Get project data directory
- `IsProjectOperationInProgress()` - Check if save/load running
- Python: `api.project.save()`, `api.project.load()`, etc.

**Note:** Currently supports dialog-based save/load only. Direct filepath save/load could be added in future.

### 13. Advanced UI Elements ✅ COMPLETED
**Status:** Implemented  
**Added:**
- `AddSlider()` - Create slider with min/max/default
- `AddTextInput()` - Create text input field
- `AddDropdown()` - Create dropdown with options
- `GetUIElementValue()` - Get element value
- `SetUIElementValue()` - Set element value
- Python: `panel.add_slider()`, `panel.add_text_input()`, `panel.add_dropdown()`, `panel.get_value()`, `panel.set_value()`

**Still Missing (Optional):**
- Toggles/checkboxes (could be added)
- Value change callbacks to Python (currently logged only)
- Custom UI styling/themes

### 14. ControlNet Operations ✅ COMPLETED
**Status:** Full operations implemented  
**Added:**
- `GetControlNetUnitCount()` - Get total units
- `GetActiveControlNetUnitCount()` - Get active units
- `SetControlNetUnitEnabled()` - Enable/disable units
- `GetControlNetUnitEnabled()` - Get enabled state
- `SetControlNetUnitWeight()` - Set control weight
- `GetControlNetUnitWeight()` - Get control weight
- `GetControlNetUnitModel()` - Get model name
- Python: Full ControlNet API with all operations

### 15. Background/Skybox Control ✅ COMPLETED
**Status:** Full operations implemented  
**Added:**
- `SetSkyboxColor(isTop, r, g, b, a)` - Set gradient colors
- `IsSkyboxGradientClear()` - Check if gradient is clear
- `GetSkyboxTopColor()` - Get top gradient color
- `GetSkyboxBottomColor()` - Get bottom gradient color
- Python: Full Background API with get/set operations

**Still Missing:**
- Background image control (complex, future consideration)

## Summary

### Quick Wins (Easy to Add)
1. Select All / Deselect All
2. Stop Generation
3. Projection Camera Write Operations
4. Get All Camera Info

### Medium Effort (Worth Adding)
5. 3D Generation Trigger
6. Batch Mesh Operations
7. Export Operations
8. Workflow Mode Control
9. Advanced UI Elements
10. ControlNet Operations
11. Background/Skybox Control

### Complex (Future Consideration)
12. Texture/Material Access
13. Painting/Brushing
14. Save/Load Operations

## Recommended Next Steps

**Phase 6: Quick Wins** ✅ COMPLETED
- ✅ Add SelectAll/DeselectAll
- ✅ Add StopGeneration
- ✅ Add Projection Camera setters
- ✅ Add GetAllCameraInfo methods

**Phase 7: Medium Features** ✅ COMPLETED
- ✅ Add 3D Generation trigger
- ✅ Add Export operations
- ✅ Add Workflow Mode control
- ⏸️ Add Advanced UI elements (Deferred - complex)
- ✅ Add ControlNet Operations (Basic)
- ✅ Add Background/Skybox Control (Basic)

**Phase 8: Performance & Enhanced Features** ✅ COMPLETED
- ✅ Add Batch Mesh Operations (Performance optimization)
- ✅ Add Enhanced ControlNet Operations (Complete implementation)
- ✅ Add Enhanced Background Operations (Complete implementation)

**Phase 9: Save/Load & Project Management** ✅ COMPLETED
- ✅ Add Save/Load project operations (Dialog-based)
- ✅ Add Project information methods (Path, version, data dir)
- ✅ Add Project operation status checking

**Phase 10: Advanced UI Elements** ✅ COMPLETED
- ✅ Add Slider UI element
- ✅ Add Text Input field
- ✅ Add Dropdown/combobox
- ✅ Add Get/Set value operations

**Future Enhancements:**
- Toggles/checkboxes
- Value change callbacks to Python
- Custom UI styling/themes
- Horizontal layout groups
