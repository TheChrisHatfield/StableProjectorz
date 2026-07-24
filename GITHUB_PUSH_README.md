# Push this project to GitHub

Remote: **https://github.com/TheChrisHatfield/StableProjectorz**

GitHub should only get what an end user needs to **open in Unity and compile/build** — not local agent, Hive, cartridge, research, or build output.

## Ship-surface allow-list (stage only these)

```bat
git add Assets Packages ProjectSettings External README.md LICENSE BUILD_INSTRUCTIONS.md CHANGELOG.md CHANGELOG.txt .gitignore .vsconfig Run_with_Addons.bat GITHUB_PUSH_README.md
```

Optional helpers if you intentionally want them on the remote:

```bat
git add build_for_testing.bat build_for_testing.ps1 build_minimal_test.bat
```

**Do not** run `git add .` — that can stage local-only trees (`cartridge/`, `tools/`, `docs/`, `.hive/`, `context-library/`, `Build_IL2CPP/`, etc.). Those paths are in `.gitignore` for a reason.

## Safe push flow

```bat
cd "D:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz"

git status
git add Assets Packages ProjectSettings External README.md LICENSE BUILD_INSTRUCTIONS.md CHANGELOG.md CHANGELOG.txt .gitignore .vsconfig Run_with_Addons.bat GITHUB_PUSH_README.md
git status
git diff --cached --stat
git commit -m "Describe the product change"
git push origin main
```

Or, after you have already committed: double-click **`push_to_github.bat`** / **`update_github.bat`**. Those scripts **only push** (they do not stage or commit).

## Authentication

GitHub no longer accepts account passwords for `git push`. Use a **Personal Access Token (PAT)** as the password.

1. Open: **https://github.com/settings/tokens**
2. **Generate new token (classic)** with **`repo`**
3. When prompted: username = GitHub user; password = the token

## If push fails

Check **`push_diagnostic.txt`** (written by `push_to_github.bat`) for the exact error.
