# Phase 9: Save/Load & Project Management

## Overview
Phase 9 focuses on project management capabilities, allowing add-ons to save/load projects and access project information.

## Features to Implement

### 1. Save/Load Operations
**Priority:** High - Very useful for automation
**Complexity:** Medium

**C# Methods:**
- `SaveProject(string filepath)` - Save project to filepath (or show dialog if null)
- `LoadProject(string filepath)` - Load project from filepath (or show dialog if null)
- `GetProjectPath()` - Get current project filepath (or null if unsaved)
- `IsProjectDirty()` - Check if project has unsaved changes

**Python API:**
```python
api.project.save("path/to/project.spz")  # Save to specific path
api.project.save()  # Show save dialog
api.project.load("path/to/project.spz")  # Load from specific path
api.project.load()  # Show load dialog
api.project.get_path()  # Returns current project path or None
api.project.is_dirty()  # Returns True if unsaved changes
```

**Why Important:** Enables automation workflows, batch processing, project templates

### 2. Project Information
**Priority:** Medium - Useful for add-ons
**Complexity:** Low

**C# Methods:**
- `GetProjectVersion()` - Get project version string
- `GetProjectDataDir()` - Get project data directory path

**Python API:**
```python
api.project.get_version()  # Returns version string
api.project.get_data_dir()  # Returns data directory path
```

**Why Important:** Add-ons can check compatibility, access project resources

## Implementation Details

### Access Points
- `ProjectSaveLoad_Helper` - Has `SaveProject()` and `LoadProject()` methods
- `StableProjectorz_SL` - Contains project data structure
- Need to check if there's a "dirty" flag or unsaved changes tracking

### Considerations
- Save/Load operations are async (use coroutines)
- Need to handle file dialogs (can pass null to show dialog)
- Need to handle errors gracefully
- Should check if generation is in progress (blocks save/load)

## Estimated Effort

- Save/Load Operations: 2-3 hours
- Project Information: 1 hour
- **Total Phase 9: 3-4 hours**

## Alternative: Advanced UI Elements

If Save/Load is too complex, we could do **Advanced UI Elements** instead:
- Sliders
- Text input fields
- Dropdowns
- Toggles

This would enable more sophisticated add-on UIs.
