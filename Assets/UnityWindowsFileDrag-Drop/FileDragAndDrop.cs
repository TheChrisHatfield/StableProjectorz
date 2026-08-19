using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using B83.Win32;
using System.IO;
using System;
using spz;

public class FileDragAndDrop : MonoBehaviour
{
    void OnEnable()
    {
        // Must be installed on the main thread to get the right thread ID.
        UnityDragAndDropHook.InstallHook();
        UnityDragAndDropHook.OnDroppedFiles += OnFiles;
    }

    void OnDisable()
    {
        UnityDragAndDropHook.UninstallHook();
        UnityDragAndDropHook.OnDroppedFiles -= OnFiles;
    }

    void OnFiles(List<string> aFiles, B83.Win32.POINT aPos)
    {
        // Convert Windows POINT to Unity screen coordinates - just flip Y
        Vector2Int screenCoord = new Vector2Int(
            aPos.x, 
            Screen.height - aPos.y  // Flip Y coordinate only
        );

        Debug.Log("Drag and Drop Coord: " + screenCoord.x + ", " +screenCoord.y);

        // Do something with the dropped file names. aPos will contain the 
        // mouse position within the window where the files have been dropped.
        string str = "Drag-and-Dropped " + aFiles.Count + " files at: " + aPos + "\n\t" +
            aFiles.Aggregate((a, b) => a + "\n\t" + b);
        Debug.Log(str);

        if (AllFiles3D(aFiles))
        {
            if (ModelsHandler_3D_UI.instance == null) {
                ShowDropStatus("3D model import is not ready.", false);
                return;
            }
            ModelsHandler_3D_UI.instance.OnDragAndDrop_3D_File(aFiles[0]);
            return; // Only import the first model.
        }

        if (AllFilesImages(aFiles)){// Handle image files
            bool consumed = false;
            if (Gen3D_MGR.instance != null)
                consumed = Gen3D_MGR.instance.OnImportedImages_DragAndDrop(aFiles, screenCoord);

            Debug.Log("Drag and Drop isConsumed after OnImportedImages_DragAndDrop: " + consumed);

            if (!consumed){
                if (Art2D_IconsUI_List.instance != null)
                    consumed = Art2D_IconsUI_List.instance.OnImport_DragAndDrop(aFiles);
                else
                    ShowDropStatus("Image import is not ready.", false);
            }
            return; // Imported all the files, now return.
        }

        if (AllFilesZip(aFiles)){// Handle zip files (add-ons)
            if (AddonInstaller_MGR.instance != null) {
                AddonInstaller_MGR.instance.InstallAddonFromZip(aFiles[0], (success, message, addonId) => {
                    if (success) {
                        ShowDropStatus($"Add-on '{addonId}' installed successfully!", true);
                    } else {
                        ShowDropStatus($"Installation failed: {message}", false);
                    }
                });
            } else {
                ShowDropStatus("Add-on installer not available", false);
            }
            return;
        }

        // SD checkpoint / VAE weights — only when dropped onto Model or SD-VAE ownership rects.
		if (SD_WeightFileImport.AllFilesAreWeights(aFiles)) {
            Vector2 screen = new Vector2(screenCoord.x, screenCoord.y);
            bool onModel = SD_Neural_Models.instance != null
                && SD_Neural_Models.instance.ScreenPointHitsOwnership(screen);
            bool onVae = SD_VAE.instance != null
                && SD_VAE.instance.ScreenPointHitsOwnership(screen);
            if (aFiles.Count > 1) {
                ShowDropStatus("Loading first weight only (" + aFiles.Count + " dropped).", false, 3);
            }
            if (onModel && !onVae) {
                SD_WeightFileImport.ImportFromPath(SD_WeightFileImport.Kind.Checkpoint, aFiles[0]);
                return;
            }
            if (onVae && !onModel) {
                SD_WeightFileImport.ImportFromPath(SD_WeightFileImport.Kind.Vae, aFiles[0]);
                return;
            }
            if (onModel && onVae) {
                // Boundary overlap: pick the control whose rect center is closer to the drop.
                float dModel = OwnershipCenterDistance(SD_Neural_Models.instance != null
                    ? SD_Neural_Models.instance.transform as RectTransform : null, screen);
                float dVae = OwnershipCenterDistance(SD_VAE.instance != null
                    ? SD_VAE.instance.transform as RectTransform : null, screen);
                if (dVae <= dModel)
                    SD_WeightFileImport.ImportFromPath(SD_WeightFileImport.Kind.Vae, aFiles[0]);
                else
                    SD_WeightFileImport.ImportFromPath(SD_WeightFileImport.Kind.Checkpoint, aFiles[0]);
                return;
            }
            ShowDropStatus("Drop onto Model or SD-VAE to load this weight.", false);
            return;
        }

        ShowDropStatus("Drag-and-drop contains unsupported file types.", false);
    }

    static void ShowDropStatus(string msg, bool success, int durationSec = 4) {
        if (Viewport_StatusText.instance != null)
            Viewport_StatusText.instance.ShowStatusText(msg, success, durationSec, false);
        else
            Debug.Log(msg);
    }

    static float OwnershipCenterDistance(RectTransform rt, Vector2 screen) {
        if (rt == null) return float.MaxValue;
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(cam, (corners[0] + corners[2]) * 0.5f);
        return Vector2.Distance(screenCenter, screen);
    }

    bool AllFiles3D(List<string> files)
    {
        return files.All(file =>
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            return extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".glb", StringComparison.OrdinalIgnoreCase);
        });
    }

    bool AllFilesImages(List<string> files)
    {
        return files.All(file =>
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
        });
    }

    bool AllFilesZip(List<string> files)
    {
        return files.Count == 1 && files.All(file =>
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        });
    }
}
