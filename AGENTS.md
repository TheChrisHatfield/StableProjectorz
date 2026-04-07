## Learned User Preferences

- Treat Context_Ref and similar reference exports as read-only documentation of the original source; do not edit them when fixing the Unity project—use them only to understand legacy behavior.
- When adjusting Unity UI padding, use `RectOffset` in the order left, right, top, bottom consistently across the paint and ribbon code.
- After runtime creation or rebuild of brush preset sections, re-apply a single flush layout pass (VLG spacing, header `LayoutElement` heights, grid anchors) so thumbnails sit tight under the collapsible header.

## Learned Workspace Facts

- Python add-ons connect to Unity over TCP JSON-RPC on port 5555 (`Addon_SocketServer`, wired from `Addon_MGR`); `Addon_HttpServer` optionally exposes REST endpoints that delegate to the same JSON-RPC handler.
- IL2CPP player builds can assert on `Resources.GetBuiltinResource` in add-on UI (`AddonManager_UI`); prefer explicit fallback sprites or project assets instead of built-in resource lookup on those paths.
- Release builds are normally produced from the Unity Editor (e.g. Build and Run); expect output under `Build_IL2CPP` such as `StableProjectorz.exe`, not an assumed CLI build unless Unity is invoked with a known editor path.
- Runtime brush painting uses `BrushAlphas_MGR.GetCurrentBrushStampTexOrFallback()` as the canonical stamp source for the paint pipeline.
- ABR import and decode edge cases (1-bit stride alignment, RLE, v6+ samp blocks, consumed-byte caps) are documented under `docs/` including `docs/ABR_DECODE_REFERENCE.md`.
- Viewport paint layers reach Stable Diffusion via UV accumulation, material updates, and the content camera capture path; if layers appear on meshes but not in SD payloads, suspect capture-time early-outs (e.g. save guards skipping layer apply) or GPU ordering—ensure layer composite and material updates complete before content-camera render or readback.
- 3D mesh picking reads one pixel from the mesh-ID render target at the cursor (`ClickSelect_Meshes_MGR`), decodes a mesh id, and toggles selection through `ModelsHandler_3D`; occasional expensive CPU readback is acceptable because it runs on click only.
- Shared responsive/tokens for runtime uGUI: `ProjectUiScale` (`Assets/_gm/_Core/UI (reusable)/ProjectUiScale.cs`) — 8px `Space(n)`, Tailwind-aligned width breakpoints in canvas reference px, `GetBand`, `ClampModalSize`. Add-on ribbon tabs use `CommandRibbon_UI` / `TabsGroup_UI` (runtime strip layout + `HarmonizeStripTabTypography`); reuse `ProjectUiScale` for new overlays.
