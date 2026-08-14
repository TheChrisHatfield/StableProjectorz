#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Lavender.Systems
{
	public static class StartExternalProcess
	{
	    //for launching the executables
	    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	    static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile, string lpParameters, string lpDirectory, int nShowCmd);
	    const int SW_HIDE = 0;
	    const int SW_SHOW = 5;


	    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	    static extern bool CreateProcessW(
	        string lpApplicationName,
	        string lpCommandLine,
	        IntPtr lpProcessAttributes,
	        IntPtr lpThreadAttributes,
	        bool bInheritHandles,
	        uint dwCreationFlags,
	        IntPtr lpEnvironment,
	        string lpCurrentDirectory,
	        [In] ref STARTUPINFO lpStartupInfo,
	        out PROCESS_INFORMATION lpProcessInformation);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    [return: MarshalAs(UnmanagedType.Bool)]
	    static extern bool CloseHandle(IntPtr hObject);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    [return: MarshalAs(UnmanagedType.Bool)]
	    static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    [return: MarshalAs(UnmanagedType.Bool)]
	    static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    static extern bool AttachConsole(uint dwProcessId);


	    [DllImport("kernel32.dll", SetLastError = true)]
	    static extern bool FreeConsole();

	    [DllImport("kernel32.dll", SetLastError = true)]
	    static extern uint GetCurrentProcessId();

	    [DllImport("user32.dll")]
	    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	    [DllImport("user32.dll")]
	    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	    [DllImport("user32.dll", SetLastError = true)]
	    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	    [DllImport("user32.dll")]
	    static extern bool IsWindowVisible(IntPtr hWnd);

	    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	    struct STARTUPINFO{
	        public int cb;
	        public string lpReserved;
	        public string lpDesktop;
	        public string lpTitle;
	        public uint dwX;
	        public uint dwY;
	        public uint dwXSize;
	        public uint dwYSize;
	        public uint dwXCountChars;
	        public uint dwYCountChars;
	        public uint dwFillAttribute;
	        public uint dwFlags;
	        public short wShowWindow;
	        public short cbReserved2;
	        public IntPtr lpReserved2;
	        public IntPtr hStdInput;
	        public IntPtr hStdOutput;
	        public IntPtr hStdError;
	    }

	    [StructLayout(LayoutKind.Sequential)]
	    struct PROCESS_INFORMATION{
	        public IntPtr hProcess;
	        public IntPtr hThread;
	        public uint dwProcessId;
	        public uint dwThreadId;
	    }

	    const uint NORMAL_PRIORITY_CLASS = 0x0020;
	    const uint CREATE_NO_WINDOW = 0x08000000;
	    const uint CREATE_NEW_CONSOLE = 0x00000010;
	    const uint CREATE_NEW_PROCESS_GROUP = 0x00000200;
	    /// <summary>Child survives when Unity's process job is torn down on quit (required for in-app restart).</summary>
	    const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
	    const uint STARTF_USESHOWWINDOW = 0x00000001;
	    const uint INFINITE = 0xFFFFFFFF;
	    // Prefer limited rights — PROCESS_ALL_ACCESS often fails OpenProcess after CreateProcess
	    // (Wait then falsely reports "exited" and callers read incomplete logs).
	    const uint PROCESS_TERMINATE = 0x0001;
	    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
	    const uint SYNCHRONIZE = 0x00100000;
	    const uint PROCESS_WAIT_ACCESS = SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION;
	    const uint PROCESS_QUERY_ACCESS = PROCESS_QUERY_LIMITED_INFORMATION;
	    const uint STILL_ACTIVE = 259;


	    public static bool Run_Exe(string filepath, string workingDir, bool hidden=false){
	        if(Path.GetExtension(filepath).ToLowerInvariant() != ".exe"){ return false; }
	        // Use ShellExecute for .exe files + request elevation (administrator rights)
	        IntPtr result = ShellExecuteW( IntPtr.Zero, "runas", filepath, null, workingDir, 
	                                        hidden? SW_HIDE : SW_SHOW );

	        if(result.ToInt64() <= 32){// ShellExecute returns a value <= 32 on error
	            int error = Marshal.GetLastWin32Error();
	            Debug.LogError($"Failed to launch exe with elevation. Error code: {error}, Error message: {new Win32Exception(error).Message}");
	            return false;
	        }
	        // Note: We can't get the process ID when using ShellExecute
	        // Return a boolean
	        Debug.Log($"Executable launched successfully with elevation: {filepath}");
	        return true; 
	    }


	    /// <param name="attachToConsole">If true, attach Unity to the child's console. Prefer false for game launches.</param>
	    /// <param name="hidden">Default true — black CMD/console stays hidden unless caller opts into a visible window.</param>
	    /// <param name="breakAwayFromJob">If true, child survives Unity quit (Job teardown). Required for Restart with addons.</param>
	    public static uint Run_Bat_or_Shortcut_or_Command( string filepath_or_command,  bool isJustFile,  string workingDir, 
	                                                        bool keepWindow=false,  bool hidden = true, bool attachToConsole = false,
	                                                        bool breakAwayFromJob = false ){
	        string fileToLaunch = "C:\\Windows\\System32\\cmd.exe";
	        string arguments = "";

	        if (isJustFile){
	            var extension = Path.GetExtension(filepath_or_command).ToLowerInvariant();
	            if (extension == ".lnk"){
	                // keepWindow: use /K so the CMD window stays open; user sees it and "start" opens the .lnk in another window
	                // When hidden, avoid "start" (always pops a visible box) — use start /B instead.
	                string prefix = keepWindow ? "/K " : "/C ";
	                arguments = hidden
	                    ? $"{prefix}start \"\" /B \"{filepath_or_command}\""
	                    : $"{prefix}start \"\" \"{filepath_or_command}\"";
	            } else if (extension == ".bat" || extension == ".cmd"){
	                // CreateProcessW cannot run .bat/.cmd directly; must run via cmd.exe
	                string prefix = keepWindow ? "/K " : "/C ";
	                arguments = $"{prefix}\"{filepath_or_command}\"";
	            } else {
	                fileToLaunch = filepath_or_command;
	            }
	        }else{// For complex commands, we'll use cmd.exe with /C to execute the command
	            // The  /C will close the window, while /K would keep it open.
	            // To remain open your command should specify  pause  inside it.
	            string prefix = keepWindow? "/K " : "/C ";
	            arguments = $"{prefix}\"{filepath_or_command}\"";
	        }
	        uint creationFlags  = NORMAL_PRIORITY_CLASS;
	        // Do not combine CREATE_NO_WINDOW with DETACHED_PROCESS — Windows ignores CREATE_NO_WINDOW in that case.
	        if (hidden)
	            creationFlags |= CREATE_NO_WINDOW;
	        // Own console for visible child when we are not attaching Unity to it (avoids FreeConsole on our process).
	        if (!hidden && !attachToConsole)
	            creationFlags |= CREATE_NEW_CONSOLE;
	        // Restart-with-addons: Unity often sits in a Job that kills children on exit. Break away or
	        // the bat/`start` relaunch dies with Application.Quit and the user only sees "closed".
	        if (breakAwayFromJob)
	            creationFlags |= CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP;

	        string commandLine = $"{fileToLaunch} {arguments}";
	        Debug.Log($"Attempting to execute: {commandLine}");
	        Debug.Log($"Working directory: {workingDir}");

	        // Only detach Unity's console when we plan to AttachConsole to the child.
	        // FreeConsole during "Restart with addons" (and similar) can stall/crash the player.
	        if (attachToConsole)
	            FreeConsole();

	        STARTUPINFO si = new STARTUPINFO();
	        PROCESS_INFORMATION pi;
	        si.cb = Marshal.SizeOf(si);
	        si.dwFlags = STARTF_USESHOWWINDOW;
	        si.wShowWindow = hidden ? (short)SW_HIDE : (short)1; // SW_SHOWNORMAL = 1

	        bool success = CreateProcessW(  null,  commandLine,  IntPtr.Zero,  IntPtr.Zero,  false,
	                                        creationFlags,  IntPtr.Zero,  workingDir,  ref si,  out pi );
	        if (!success){
	            int error = Marshal.GetLastWin32Error();
	            // Breakaway can fail if the process is not in a job — retry without it.
	            if (breakAwayFromJob) {
	                Debug.LogWarning($"[StartExternalProcess] Breakaway launch failed ({error}: {new Win32Exception(error).Message}) — retrying without CREATE_BREAKAWAY_FROM_JOB.");
	                uint retryFlags = creationFlags & ~(CREATE_BREAKAWAY_FROM_JOB | CREATE_NEW_PROCESS_GROUP);
	                success = CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
	                    retryFlags, IntPtr.Zero, workingDir, ref si, out pi);
	            }
	            if (!success) {
	                error = Marshal.GetLastWin32Error();
	                Debug.LogError($"Failed to start process. Error code: {error}, Error message: {new Win32Exception(error).Message}");
	                return 0;
	            }
	        }
	        Debug.Log($"Process started successfully. Process ID: {pi.dwProcessId}");

	        CloseHandle(pi.hProcess);
	        CloseHandle(pi.hThread);

	        if (attachToConsole) AttachConsole(pi.dwProcessId);
	        return pi.dwProcessId;
	    }


	    public static bool KillProcess(uint processId){
	        IntPtr hProcess = OpenProcess(PROCESS_TERMINATE, false, processId);
	        if (hProcess == IntPtr.Zero)
	            return false;

	        bool result = TerminateProcess(hProcess, 0);
	        CloseHandle(hProcess);
	        return result;
	    }

	    /// <summary>
	    /// Kill <paramref name="processId"/> and its descendants (e.g. cmd.exe → blender.exe).
	    /// Plain <see cref="KillProcess"/> leaves child Blender sessions running after install timeout.
	    /// </summary>
	    public static bool KillProcessTree(uint processId){
	        if (processId == 0) return false;
	        string workDir = Path.GetTempPath();
	        string cmd = "taskkill /PID " + processId + " /T /F";
	        uint killer = Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
	        if (killer != 0)
	            WaitForProcessExit(killer, 5000);
	        // Fallback if taskkill unavailable
	        if (IsProcessRunning(processId))
	            return KillProcess(processId);
	        return true;
	    }

	    /// <summary>Current process ID (Unity/game exe). Use to avoid killing self when freeing ports.</summary>
	    public static uint GetCurrentPid() => GetCurrentProcessId();

	    public static bool IsProcessRunning(uint processId){
	        IntPtr hProcess = OpenProcess(PROCESS_QUERY_ACCESS, false, processId);
	        if (hProcess == IntPtr.Zero)
	            return false;

	        uint exitCode;
	        bool result = GetExitCodeProcess(hProcess, out exitCode);
	        CloseHandle(hProcess);

	        return result && exitCode == STILL_ACTIVE;
	    }

	    /// <summary>
	    /// Show or hide top-level windows owned by any of <paramref name="processIds"/>.
	    /// Returns how many matching windows were targeted. Processes started with CREATE_NO_WINDOW have no HWND (returns 0).
	    /// Note: Win32 ShowWindow's BOOL is "was previously visible", not success — do not use it as a success check.
	    /// </summary>
	    public static int TrySetWindowsVisibleForProcessIds(IEnumerable<uint> processIds, bool visible) {
	        if (processIds == null) return 0;
	        var want = new HashSet<uint>();
	        foreach (uint pid in processIds) {
	            if (pid != 0) want.Add(pid);
	        }
	        if (want.Count == 0) return 0;

	        int showCmd = visible ? SW_SHOW : SW_HIDE;
	        int changed = 0;
	        EnumWindows((hWnd, _) => {
	            if (GetWindowThreadProcessId(hWnd, out uint ownerPid) == 0) return true;
	            if (!want.Contains(ownerPid)) return true;
	            // When hiding, only touch currently visible windows; when showing, also raise hidden ones.
	            if (!visible && !IsWindowVisible(hWnd)) return true;
	            ShowWindow(hWnd, showCmd);
	            changed++;
	            return true;
	        }, IntPtr.Zero);
	        return changed;
	    }

	    public static bool WaitForProcessExit(uint processId, int timeoutMs = -1){
	        IntPtr hProcess = OpenProcess(PROCESS_WAIT_ACCESS, false, processId);
	        int waitedMs = 0;
	        if (hProcess == IntPtr.Zero) {
	            // OpenProcess can fail while the process is still alive (rights/timing).
	            // Do not treat that as "already exited" — poll IsProcessRunning instead.
	            if (timeoutMs == 0)
	                return !IsProcessRunning(processId);
	            const int slice = 50;
	            while (timeoutMs < 0 || waitedMs < timeoutMs) {
	                if (!IsProcessRunning(processId))
	                    return true;
	                System.Threading.Thread.Sleep(slice);
	                if (timeoutMs >= 0)
	                    waitedMs += slice;
	                hProcess = OpenProcess(PROCESS_WAIT_ACCESS, false, processId);
	                if (hProcess != IntPtr.Zero)
	                    break;
	            }
	            if (hProcess == IntPtr.Zero)
	                return !IsProcessRunning(processId);
	        }

	        // Use remaining budget after any OpenProcess poll — do not restart the full timeout.
	        uint waitBudget;
	        if (timeoutMs < 0)
	            waitBudget = INFINITE;
	        else {
	            int remaining = timeoutMs - waitedMs;
	            if (remaining <= 0) {
	                CloseHandle(hProcess);
	                return !IsProcessRunning(processId);
	            }
	            waitBudget = (uint)remaining;
	        }
	        uint waitResult = WaitForSingleObject(hProcess, waitBudget);
	        CloseHandle(hProcess);

	        return waitResult != 0x00000102; // WAIT_TIMEOUT = 0x00000102
	    }
	}
}
#endif