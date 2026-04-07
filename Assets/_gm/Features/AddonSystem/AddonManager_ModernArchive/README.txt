Addon Manager UI archive (for incremental merge / debugging)

- AddonManager_UI.ModernSnapshot.cs.txt — full C# snapshot of the redesigned manager before restoring git main’s AddonManager_UI.cs (same text as the .cs file; .txt so Unity does not compile a second class).
- AddonManager_UI.FromGitMain_Reference.cs.txt — copy of main-branch AddonManager_UI.cs at restore time (for diffing).

Active runtime script: ../AddonManager_UI.cs (restored from main).

To bring back the modern UI in one step, replace AddonManager_UI.cs with ModernSnapshot (rename to .cs) and re-apply viewport/settings hooks (IsModalOpen, OpenFromMenu) from git history or the snapshot.
