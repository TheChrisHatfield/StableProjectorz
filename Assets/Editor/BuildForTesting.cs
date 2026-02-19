using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds Win64 player to Build_IL2CPP/StableProjectorz.exe for batch mode.
/// Invoked by build_for_testing.bat with -executeMethod BuildForTesting.BuildWin64
/// </summary>
public static class BuildForTesting
{
	public const string RelativeOutputExe = "Build_IL2CPP/StableProjectorz.exe";

	/// <summary>
	/// Entry point for: Unity -batchmode -executeMethod BuildForTesting.BuildWin64 -quit
	/// </summary>
	[MenuItem("Build/Build Win64 for testing (IL2CPP)")]
	public static void BuildWin64()
	{
		string projectRoot = Path.GetDirectoryName(Application.dataPath);
		string outputPath = Path.Combine(projectRoot, RelativeOutputExe.Replace('/', Path.DirectorySeparatorChar));
		string outputDir = Path.GetDirectoryName(outputPath);
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
		}
		else
		{
			Debug.LogError($"[BuildForTesting] Build failed: {summary.result}, errors: {summary.totalErrors}. Check this log file (build_output.txt) for the first error above.");
			EditorApplication.Exit(1);
		}
	}
}
