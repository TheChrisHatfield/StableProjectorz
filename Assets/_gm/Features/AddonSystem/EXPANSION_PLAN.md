# Add-On System Expansion Plan

## Phase 1: Mesh Operations (High Priority) ✅ COMPLETED
- [x] Set mesh position
- [x] Set mesh rotation (quaternion)
- [x] Set mesh scale
- [x] Get mesh position
- [x] Get mesh rotation
- [x] Get mesh scale
- [x] Get mesh bounds
- [x] Set mesh visibility (enable/disable renderer)
- [x] Get mesh visibility
- [x] Get all mesh IDs in scene
- [x] Get mesh name

## Phase 2: Scene Information (High Priority) ✅ COMPLETED
- [x] Get total mesh count
- [x] Get selected mesh count
- [x] Get scene bounds (all meshes)
- [x] Get selected meshes bounds

## Phase 3: Stable Diffusion Integration (Medium Priority) ✅ COMPLETED
- [x] Get positive prompt
- [x] Set positive prompt
- [x] Get negative prompt
- [x] Set negative prompt
- [x] Trigger texture generation
- [x] Check if generation is in progress
- [x] Check if SD service is connected
- [x] Check if 3D generation service is connected

## Phase 4: Projection System (Medium Priority) ✅ COMPLETED
- [x] Get projection camera count
- [x] Get projection camera position
- [x] Get projection camera rotation

## Phase 5: UI Extensions (Low Priority) - PENDING
- [ ] Create slider UI element
- [ ] Create text input field
- [ ] Create dropdown/combobox

## Implementation Status
✅ **Phases 1-4 COMPLETED** - All core functionality implemented
- Mesh operations: Full transform control (position, rotation, scale, visibility)
- Scene information: Complete scene introspection
- Stable Diffusion: Full prompt control and generation triggering
- Projection system: Read-only access to projection cameras

📝 **Phase 5 PENDING** - UI extensions can be added as needed

## New API Methods Added

### C# FastPath_API
- Mesh: SetMeshRotation, SetMeshScale, SetMeshVisibility, GetMeshPosition, GetMeshRotation, GetMeshScale, GetMeshBounds, GetMeshVisibility, GetMeshName
- Scene: GetTotalMeshCount, GetSelectedMeshCount, GetAllMeshIDs, GetSelectedMeshesBounds
- SD: GetPositivePrompt, SetPositivePrompt, GetNegativePrompt, SetNegativePrompt, TriggerTextureGeneration, IsGenerating, IsSDConnected, Is3DConnected
- Projection: GetProjectionCameraCount, GetProjectionCameraPosition, GetProjectionCameraRotation

### Python spz.py
- New API classes: SceneAPI, StableDiffusionAPI, ProjectionAPI
- Expanded ModelsAPI with all transform operations
- Convenience functions: scene(), sd(), projection()

### Example Add-ons
- CameraTools: Basic camera control (existing)
- MeshTools: Comprehensive mesh manipulation and SD integration (new)
