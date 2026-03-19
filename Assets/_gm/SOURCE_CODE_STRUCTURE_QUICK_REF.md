# StableProjectorz – Source code structure (quick reference)

Codebase lives under **`Assets/_gm`**. Main architecture is in the root **README.md**.

## Black box / CMD / launching Stable Diffusion (PowerShell terminal)

The **“black box”** is the CMD/PowerShell terminal that opens when the app launches the Stable Diffusion WebUI and connects it to Unity.

| What | Where |
|------|--------|
| **Quick guide panel (CMD intro)** | `Features/Intro Panels/WelcomeScreenCMD UI/WelcomeScreenCMD_MGR.cs` – shown on startup unless “don’t show” is ticked. Tells the user how to open WebUI. |
| **Launch WebUI (bat file)** | `Features/StableDiffusion/Webui/Launch_WebUI_bat_File.cs` – finds `run_noQuickEdit.bat` (or `run.bat` / Forge) next to the EXE and launches it. **Start()** calls **LaunchWebui_Manually()** in builds (skipped in Editor). |
| **Keep terminal open** | **StartExternalProcess.Run_Bat_or_Shortcut_or_Command(..., keepWindow: true)** – uses `cmd.exe /K` so the CMD window stays open (black box visible). |
| **Actual process launch** | `_Core/IO/IL2cppStartProcess/StartExternalProcess.cs` – **CreateProcessW** (IL2CPP-safe). Runs `cmd.exe` with the .bat so the terminal appears. |
| **Restart WebUI** | `Features/StableDiffusion/Webui/RestartTheWebui.cs` – same launch path with **keepWindow: true** so the terminal stays open on restart. |
| **Scene that holds CMD panel** | `Features/Intro Panels/UI_MainWelcomeScreenCMD.unity` – must be in **Build Settings** (see **Start_Scene_Global_MGR.cs** scene list). |

Flow: **EXE starts** → **Start_Scene_Global_MGR** loads scenes (including **UI_MainWelcomeScreenCMD**) → **WelcomeScreenCMD_MGR.Awake** runs, calls **Show(delay:1.5f)** → CMD quick guide appears. **LaunchWebUIBatFile.Start()** (in WebUI scene) runs **LaunchWebui_Manually()** → **StartExternalProcess** runs the bat with **keepWindow: true** → CMD/PowerShell window (black box) opens and stays open while SD runs.

## Paint tab / layer panel

| What | Where |
|------|--------|
| **Layer list UI** | `Features/Paint/PaintTab/PaintTab_LayersPanel_UI.cs` – row buttons (Select, Delete, Visibility), **LayerRowDeleteTrigger**, **LayerRowPointerDownForward**, **TryRemoveLayer**. |
| **Populate Paint tab** | `Features/Paint/PaintTab/PaintTab_CollectPaintUI.cs` – **CollectNow()** wires layout, sections, layers panel, **SetLayerStack**, Add Layer button. |
| **Layer stack** | `Features/Paint/Layers/PaintLayerStack_MGR.cs` – **RemoveLayer**, **EnsureAtLeastOneLayer**, **AddLayer**. |

## Scene loading

**Start_Scene_Global_MGR.cs** – **scenePathsToLoadFirst** and **scenePathsToLoadAfter** list all additive scenes. Each path must be in **Build Settings** or it is skipped with a log.
