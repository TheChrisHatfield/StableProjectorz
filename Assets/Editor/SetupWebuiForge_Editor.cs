using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu to clone stable-diffusion-webui-forge into project root. The build also auto-clones if the folder is missing; this menu is for doing it manually before building (e.g. to avoid waiting during the build).
/// </summary>
public static class SetupWebuiForge_Editor
{
	const string WebuiForgeFolderName = "stable-diffusion-webui-forge";
	const string CloneUrl = "https://github.com/lllyasviel/stable-diffusion-webui-forge.git";

	[MenuItem("Build/Setup WebUI Forge (clone to project root)")]
	public static void CloneWebuiForgeToProjectRoot()
	{
		string projectRoot = Path.GetDirectoryName(Application.dataPath);
		string destFolder = Path.Combine(projectRoot, WebuiForgeFolderName);
		if (Directory.Exists(destFolder))
		{
			var files = Directory.GetFiles(destFolder);
			var dirs = Directory.GetDirectories(destFolder);
			if (files.Length > 0 || dirs.Length > 0)
			{
				bool overwrite = EditorUtility.DisplayDialog("WebUI Forge folder exists",
					$"'{WebuiForgeFolderName}' already exists in project root. Clone will skip or you can delete it first. Open folder?",
					"Open folder", "Cancel");
				if (overwrite)
					EditorUtility.RevealInFinder(destFolder);
				return;
			}
		}

		// Ensure git is available
		string gitPath = FindGitExe();
		if (string.IsNullOrEmpty(gitPath))
		{
			EditorUtility.DisplayDialog("Git not found",
				"Git was not found in PATH or common locations. Install Git for Windows or clone manually:\n" +
				"git clone " + CloneUrl + " " + WebuiForgeFolderName,
				"OK");
			return;
		}

		bool run = EditorUtility.DisplayDialog("Clone WebUI Forge",
			"Clone stable-diffusion-webui-forge into project root?\n\n" +
			"Path: " + destFolder + "\n\n" +
			"This may take a few minutes. The build will then copy this folder into Build_IL2CPP.",
			"Clone", "Cancel");
		if (!run) return;

		if (Directory.Exists(destFolder))
		{
			try
			{
				Directory.Delete(destFolder, true);
			}
			catch (System.Exception e)
			{
				EditorUtility.DisplayDialog("Error", "Could not remove existing folder: " + e.Message, "OK");
				return;
			}
		}

		ProcessStartInfo psi = new ProcessStartInfo
		{
			FileName = gitPath,
			Arguments = "clone --depth 1 \"" + CloneUrl + "\" \"" + destFolder + "\"",
			WorkingDirectory = projectRoot,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		UnityEngine.Debug.Log("[SetupWebuiForge] Running: " + psi.FileName + " " + psi.Arguments);
		try
		{
			using (var p = Process.Start(psi))
			{
				if (p == null)
				{
					EditorUtility.DisplayDialog("Error", "Failed to start git process.", "OK");
					return;
				}
				string stdout = p.StandardOutput.ReadToEnd();
				string stderr = p.StandardError.ReadToEnd();
				p.WaitForExit(120000); // 2 min
				if (p.ExitCode != 0)
				{
					UnityEngine.Debug.LogError("[SetupWebuiForge] git clone failed: " + stderr + "\n" + stdout);
					EditorUtility.DisplayDialog("Clone failed", "git clone failed (exit " + p.ExitCode + "). Check Console. " + (stderr.Length > 0 ? "\n\n" + stderr : ""), "OK");
					return;
				}
			}
			bool hasBat = File.Exists(Path.Combine(destFolder, "run_noQuickEdit.bat")) || File.Exists(Path.Combine(destFolder, "run.bat"));
			UnityEngine.Debug.Log("[SetupWebuiForge] Clone completed. run_noQuickEdit.bat present: " + hasBat);
			EditorUtility.DisplayDialog("Done", "WebUI Forge cloned to project root.\n\nNext: run Build → Build Win64 for testing. The build will copy this folder into Build_IL2CPP.", "OK");
			EditorUtility.RevealInFinder(destFolder);
		}
		catch (System.Exception e)
		{
			UnityEngine.Debug.LogException(e);
			EditorUtility.DisplayDialog("Error", "Clone failed: " + e.Message, "OK");
		}
	}

	static string FindGitExe()
	{
		string path = System.Environment.GetEnvironmentVariable("PATH");
		if (!string.IsNullOrEmpty(path))
		{
			foreach (string part in path.Split(';'))
			{
				string trimmed = part.Trim();
				if (string.IsNullOrEmpty(trimmed)) continue;
				string exe = Path.Combine(trimmed, "git.exe");
				if (File.Exists(exe)) return exe;
			}
		}
		string[] common = new[]
		{
			@"C:\Program Files\Git\bin\git.exe",
			@"C:\Program Files (x86)\Git\bin\git.exe"
		};
		foreach (string exe in common)
		{
			if (File.Exists(exe)) return exe;
		}
		return null;
	}
}
