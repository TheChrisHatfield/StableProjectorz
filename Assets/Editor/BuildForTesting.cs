using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds Win64 player to Build_IL2CPP/StableProjectorz.exe for batch mode.
/// Invoked by build_for_testing.bat with -executeMethod BuildForTesting.BuildWin64
///
/// WebUI Forge / Neo: After a successful build, if stable-diffusion-webui-forge is missing in project root we
/// automatically clone classic Forge (git clone), then copy it into Build_IL2CPP so a launch bat is present.
/// Requires Git installed. Launch discovery also accepts stable-diffusion-webui-reForge (forge-neo-swap Phase A).
/// Clean step: When cleaning before build, Build_IL2CPP contents are removed EXCEPT WebUI candidate folders
/// (classic forge + reForge Neo) so venv/models/user data are never overwritten or deleted.
/// </summary>
public static class BuildForTesting
{
	public const string RelativeOutputExe = "Build_IL2CPP/StableProjectorz.exe";
	/// <summary>Folder name in project root; cloned automatically during build if missing, then copied into the build.</summary>
	public const string WebuiForgeFolderName = "stable-diffusion-webui-forge";
	/// <summary>reForge Neo folder — preserved on clean; not auto-cloned in Phase A.</summary>
	public const string WebuiForgeNeoFolderName = "stable-diffusion-webui-reForge";
	static readonly string[] WebuiPreserveFolderNames = {
		WebuiForgeNeoFolderName,
		WebuiForgeFolderName,
	};
	const string WebuiForgeCloneUrl = "https://github.com/lllyasviel/stable-diffusion-webui-forge.git";
	static readonly string[] BeeStateFilesRelative =
	{
		"Library/Bee/TundraBuildState.state",
		"Library/Bee/TundraBuildState.state.map",
		"Library/Bee/tundra.digestcache",
		"Library/Bee/tundra.digestcache.tmp",
	};

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
				DeleteBuildOutputExceptForgeFolder(outputDir);
				Debug.Log("[BuildForTesting] Cleaned " + outputDir + " (preserved " + WebuiForgeFolderName + ").");
			}
			catch (System.Exception e)
			{
				Debug.LogWarning("[BuildForTesting] Could not clean output folder (close StableProjectorz.exe and try again): " + e.Message);
			}
		}
		if (!Directory.Exists(outputDir))
			Directory.CreateDirectory(outputDir);
		TryPrepareBeeStateFilesForBatchBuild(projectRoot);

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
			string hint = "Check build_output.txt above. ";
			string summarized = report.SummarizeErrors();
			if (!string.IsNullOrEmpty(summarized))
				hint += "Errors: " + summarized + ". ";
			if (summarized != null && summarized.Contains("No space left on device"))
				hint += "Free disk space on the drive containing the project and Library, then run Build → Clean and Build Win64 (IL2CPP).";
			else if (summarized != null && summarized.Contains("user-mapped section open"))
				hint += "Close StableProjectorz.exe (and any copy), then run Build → Clean and Build Win64 (IL2CPP).";
			else if (summarized != null && (summarized.Contains("TundraBuildState.state") || summarized.Contains("tundra.digestcache")))
				hint += "Bee state file lock detected. Close Unity Editor/other build instances and antivirus scanners touching Library/Bee, then rerun Clean and Build.";
			else
				hint += "If you see 'user-mapped section open', close the exe and Clean+Build. If you see 'No space left on device', free disk space and Clean+Build.";
			Debug.LogError($"[BuildForTesting] Build failed: {summary.result}, errors: {summary.totalErrors}. {hint}");
			EditorApplication.Exit(1);
		}
	}

	/// <summary>
	/// Bee can fail with "Unable to rename state file" when stale/locked files exist in Library/Bee.
	/// Pre-clear known state files with retries so batch builds fail less often after interrupted runs.
	/// </summary>
	static void TryPrepareBeeStateFilesForBatchBuild(string projectRoot)
	{
		for (int i = 0; i < BeeStateFilesRelative.Length; i++)
		{
			string rel = BeeStateFilesRelative[i];
			string full = Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar));
			TryDeleteFileWithRetry(full, rel, attempts: 8, sleepMs: 125);
		}
	}

	static void TryDeleteFileWithRetry(string fullPath, string relPath, int attempts, int sleepMs)
	{
		if (!File.Exists(fullPath))
			return;
		for (int i = 0; i < attempts; i++)
		{
			try
			{
				File.SetAttributes(fullPath, FileAttributes.Normal);
				File.Delete(fullPath);
				Debug.Log("[BuildForTesting] Cleared stale Bee state: " + relPath);
				return;
			}
			catch (System.Exception e)
			{
				bool isLast = i == attempts - 1;
				if (isLast)
				{
					Debug.LogWarning(
						"[BuildForTesting] Could not clear Bee state file '" + relPath + "'. " +
						"Another process may be locking Library/Bee. Close Unity Editor/other builds, then retry. " +
						"Error: " + e.Message);
				}
				else
				{
					Thread.Sleep(sleepMs);
				}
			}
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

	/// <summary>Delete all contents of the build output directory except WebUI candidate folders (classic Forge + reForge Neo) so venv/models/user data are never overwritten or deleted by the build.</summary>
	static void DeleteBuildOutputExceptForgeFolder(string outputDir)
	{
		if (!Directory.Exists(outputDir)) return;
		foreach (string path in Directory.GetFileSystemEntries(outputDir))
		{
			string name = Path.GetFileName(path);
			bool preserve = false;
			foreach (string keep in WebuiPreserveFolderNames)
			{
				if (string.Equals(name, keep, System.StringComparison.OrdinalIgnoreCase))
				{
					preserve = true;
					break;
				}
			}
			if (preserve)
			{
				// Preserve this folder; do not delete or write into it during clean.
				continue;
			}
			try
			{
				if (Directory.Exists(path))
					Directory.Delete(path, true);
				else
					File.Delete(path);
			}
			catch (System.Exception e)
			{
				Debug.LogWarning("[BuildForTesting] Could not delete " + path + ": " + e.Message);
			}
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

	/// <summary>Ensure stable-diffusion-webui-forge in build output so run_noQuickEdit.bat is present. We never overwrite or write into Build_IL2CPP/stable-diffusion-webui-forge when it already exists—only update the launcher bat so venv/models/user data are preserved. Full copy from project root only when the folder does not exist.</summary>
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
