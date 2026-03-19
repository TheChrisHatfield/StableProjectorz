# Push this project to GitHub

Your remote is set to: **https://github.com/TheChrisHatfield/StableProjectorz**

## Step 1: Run the diagnostic

Double-click **`push_to_github.bat`** in this folder.

It will:
- Stage and commit your changes
- Try to push to `origin main`
- Save all output to **`push_diagnostic.txt`**

If push fails, the window and the log file will show the error.

---

## Step 2: If you see an authentication error

GitHub no longer accepts account passwords for `git push`. Use a **Personal Access Token (PAT)** as the password.

1. Open: **https://github.com/settings/tokens**
2. Click **"Generate new token"** → **"Generate new token (classic)"**
3. Name it (e.g. "StableProjectorz push"), set expiration, and check **`repo`**
4. Generate and **copy the token** (you won’t see it again)
5. When you run `push_to_github.bat` (or `git push`), at the prompt:
   - **Username:** your GitHub username (e.g. `TheChrisHatfield`)
   - **Password:** paste the token (not your GitHub password)

To avoid typing it every time, use Git Credential Manager (usually installed with Git for Windows) or:

```bat
git config --global credential.helper store
```

Then the next successful login will be saved.

---

## Step 3: Push from Command Prompt (alternative)

Open **Command Prompt** or **PowerShell**, then:

```bat
cd "D:\DRIVE_DOWNLOADS\Stable_Projectoz_Dev_Build\StableProjectorz"
git add .
git commit -m "fix: Add-on Manager UI, IL2CPP compatibility, and API robustness"
git push origin main
```

When asked for password, use your **PAT**, not your GitHub password.

---

## If it still doesn’t work

Send the contents of **`push_diagnostic.txt`** (or the exact error message) so we can see the real failure (auth, network, permissions, etc.).
