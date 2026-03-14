using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds Win64 player to Build_IL2CPP/StableProjectorz.exe for batch mode.
/// Invoked by build_for_testing.bat with -executeMethod BuildForTesting.BuildWin64
///
/// WebUI Forge: After a successful build, if stable-diffusion-webui-forge is missing in project root we
/// automatically clone it (git clone), then copy it into Build_IL2CPP so run_noQuickEdit.bat is present for auto-launch.
/// Requires Git installed. In batch mode the entire Build_IL2CPP folder is deleted before building.
/// </summary>
public static class BuildForTesting
{
	public const string RelativeOutputExe = "Build_IL2CPP/StableProjectorz.exe";
	/// <summary>Folder name in project root; cloned automatically during build if missing, then copied into the build.</summary>
	public const string WebuiForgeFolderName = "stable-diffusion-webui-forge";
	const string WebuiForgeCloneUrl = "https://github.com/lllyasviel/stable-diffusion-webui-forge.git";

	/// <summary>
	/// Entry point for: Unity -batchmode -executeMethod BuildForTesting.BuildWin64 -quit
	/// </summary>
	/// <summary> Set to true to delete Build_IL2CPP before building (fixes "user-mapped section open" when the exe or another process has files locked). Close the built exe first. </summary>
	public static bool CleanBeforeBuild = false;

	[MenuItem("Build/Build Win64 for testing (IL2CPP)")]
	public static void BuildWin64() => BuildWin64Internal(false);

	[MenuItem("Build/Clean and Build Win64 (IL2CPP)")]
	public static void CleanAndBuildWin64() => BuildWin64Internal(cleanFirst: true);

	static void BuildWin64Internal(bool cleanFirst)
	{
		Debug.Log("[BuildForTesting] BuildWin64 started.");
		try
		{
			BuildWin64InternalCore(cleanFirst);
		}
		catch (System.Exception e)
		{
			Debug.LogError("[BuildForTesting] Exception: " + e.Message + "\n" + e.StackTrace);
			EditorApplication.Exit(1);
		}
	}

	static void BuildWin64InternalCore(bool cleanFirst)
	{
		// In batch mode, always clean first so that after closing the exe, the next build succeeds.
		if (Application.isBatchMode)
			cleanFirst = true;

		string projectRoot = Path.GetDirectoryName(Application.dataPath);
		string outputPath = Path.Combine(projectRoot, RelativeOutputExe.Replace('/', Path.DirectorySeparatorChar));
		string outputDir = Path.GetDirectoryName(outputPath);
		if ((CleanBeforeBuild || cleanFirst) && Directory.Exists(outputDir))
		{
			try
			{
				Directory.Delete(outputDir, true);
				Debug.Log("[BuildForTesting] Deleted " + outputDir + " (CleanBeforeBuild).");
			}
			catch (System.Exception e)
			{
				Debug.LogWarning("[BuildForTesting] Could not delete output folder (close StableProjectorz.exe and try again): " + e.Message);
			}
		}
		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);

		var scenes = EditorBuildSettings.scenes;
		var enabledScenes = new System.Collections.Generic.List<string>();
		foreach (var s in scenes)
		{
			if (s.enabled)
				enabledScenes.Add(s.path);
		}
		if (enabledScenes.Count == 0)
		{
			Debug.LogError("[BuildForTesting] No enabled scenes in Build Settings.");
			EditorApplication.Exit(1);
			return;
		}

		BuildPlayerOptions opts = new BuildPlayerOptions
		{
			scenes = enabledScenes.ToArray(),
			locationPathName = outputPath,
			target = BuildTarget.StandaloneWindows64,
			options = BuildOptions.None
		};

		Debug.Log($"[BuildForTesting] Building to {outputPath} (scenes: {enabledScenes.Count})");
		BuildReport report = BuildPipeline.BuildPlayer(opts);
		BuildSummary summary = report.summary;

		if (summary.result == BuildResult.Succeeded)
		{
			Debug.Log($"[BuildForTesting] Build succeeded. Size: {summary.totalSize} bytes.");
			try
			{
				EnsureWebuiForgeInProjectRoot(projectRoot);
				CopyWebuiForgeFolderIntoBuild(projectRoot, outputDir);
			}
			catch (System.Exception e)
			{
				Debug.LogWarning("[BuildForTesting] Post-build (Forge clone/copy) failed: " + e.Message);
			}
			EditorApplication.Exit(0);
		}
		else
		{
			Debug.LogError($"[BuildForTesting] Build failed: {summary.result}, errors: {summary.totalErrors}. Check build_output.txt above. If it says 'user-mapped section open', close StableProjectorz.exe (and any copy), then run Build → Clean and Build Win64 (IL2CPP).");
			EditorApplication.Exit(1);
		}
	}

	/// <summary>If Forge folder is missing in project root, clone it automatically (git). Then copy into build output so run_noQuickEdit.bat is present.</summary>
	static void EnsureWebuiForgeInProjectRoot(string projectRoot)
	{
		string sourceForge = Path.Combine(projectRoot, WebuiForgeFolderName);
		if (Directory.Exists(sourceForge))
		{
			string bat = Path.Combine(sourceForge, "run_noQuickEdit.bat");
			if (File.Exists(bat) || File.Exists(Path.Combine(sourceForge, "run.bat")))
				return;
		}
		string gitPath = FindGitExe();
		if (string.IsNullOrEmpty(gitPath))
		{
			Debug.LogWarning("[BuildForTesting] Git not found. Install Git or run Build → Setup WebUI Forge to clone " + WebuiForgeFolderName + " into project root.");
			return;
		}
		if (Directory.Exists(sourceForge))
		{
			try { Directory.Delete(sourceForge, true); }
			catch (System.Exception e) { Debug.LogWarning("[BuildForTesting] Could not remove incomplete folder: " + e.Message); return; }
		}
		Debug.Log("[BuildForTesting] Cloning " + WebuiForgeFolderName + " into project root (this may take a few minutes)...");
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = gitPath,
				Arguments = "clone --depth 1 \"" + WebuiForgeCloneUrl + "\" \"" + sourceForge + "\"",
				WorkingDirectory = projectRoot,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			using (var p = Process.Start(psi))
			{
				if (p == null) { Debug.LogWarning("[BuildForTesting] Failed to start git."); return; }
				string stderr = p.StandardError.ReadToEnd();
				p.StandardOutput.ReadToEnd();
				p.WaitForExit(300000); // 5 min
				if (p.ExitCode != 0)
				{
					Debug.LogWarning("[BuildForTesting] git clone failed (exit " + p.ExitCode + "): " + stderr);
					return;
				}
			}
			Debug.Log("[BuildForTesting] Clone completed: " + sourceForge);
		}
		catch (System.Exception e)
		{
			Debug.LogWarning("[BuildForTesting] Clone failed: " + e.Message);
		}
	}

	static string FindGitExe()
	{
		string path = System.Environment.GetEnvironmentVariable("PATH");
		if (!string.IsNullOrEmpty(path))
		{
			foreach (string part in path.Split(';'))
			{
				string exe = Path.Combine(part.Trim(), "git.exe");
				if (File.Exists(exe)) return exe;
			}
		}
		foreach (string exe in new[] { @"C:\Program Files\Git\bin\git.exe", @"C:\Program Files (x86)\Git\bin\git.exe" })
			if (File.Exists(exe)) return exe;
		return null;
	}

	/// <summary>Ensure stable-diffusion-webui-forge in build output so run_noQuickEdit.bat is present. If dest folder already exists, only copy launcher bat file(s) so venv/models/data are not overwritten.</summary>
	static void CopyWebuiForgeFolderIntoBuild(string projectRoot, string buildOutputDir)
	{
		string sourceForge = Path.Combine(projectRoot, WebuiForgeFolderName);
		string destForge = Path.Combine(buildOutputDir, WebuiForgeFolderName);
		if (!Directory.Exists(sourceForge))
		{
			Debug.LogWarning($"[BuildForTesting] {WebuiForgeFolderName} not found in project root ({sourceForge}). Clone may have failed (check Git is installed).");
			return;
		}
		string sourceBat = Path.Combine(sourceForge, "run_noQuickEdit.bat");
		if (!File.Exists(sourceBat))
			sourceBat = Path.Combine(sourceForge, "run.bat");
		if (!File.Exists(sourceBat))
			Debug.LogWarning($"[BuildForTesting] {WebuiForgeFolderName} exists but run_noQuickEdit.bat and run.bat not found. Clone may be incomplete.");
		try
		{
			if (Directory.Exists(destForge))
			{
				// Dest already exists (e.g. from previous build with venv/models). Do not overwrite; only ensure bat file is present.
				if (File.Exists(sourceBat))
				{
					string destBat = Path.Combine(destForge, Path.GetFileName(sourceBat));
					File.Copy(sourceBat, destBat, overwrite: true);
					Debug.Log($"[BuildForTesting] Build Forge folder already exists at {destForge}; updated launcher bat only (data/venv preserved).");
				}
				else
					Debug.Log($"[BuildForTesting] Build Forge folder already exists at {destForge}; no bat to copy (data preserved).");
				return;
			}
			FileUtil.CopyFileOrDirectory(sourceForge, destForge);
			Debug.Log($"[BuildForTesting] Copied {WebuiForgeFolderName} into build at {destForge}. run_noQuickEdit.bat will be found on exe start.");
		}
		catch (System.Exception e)
		{
			Debug.LogWarning($"[BuildForTesting] Could not copy/update {WebuiForgeFolderName} in build: {e.Message}");
		}
	}
}
