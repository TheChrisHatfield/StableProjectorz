using System.Threading;
using UnityEngine;

namespace spz {

	public class ExitTheProgram_MGR : MonoBehaviour
	{
	    bool _quitPopupConfirmed = false;
	    /// <summary>True after we showed the "Close the program?" prompt until Yes/No.</summary>
	    bool _exitPromptOpen;
	    /// <summary>Window-close clicks while the exit prompt is already up (Yes/Close unresponsive → force quit).</summary>
	    int _closeAttemptsWhileExitPrompt;
	    const int ForceQuitAfterCloseAttempts = 3;
	    /// <summary>Hard-exit cap so a hung OnDestroy / Dispose / native plugin can't keep the process alive forever
	    /// (heavy painting + many RenderUdims layers can stall GPU release; some IL2CPP/Mono shutdown paths in standalone Win builds
	    /// don't actually terminate the process on Application.Quit alone — see <c>Launch_Addons_Bat_File.RestartWithAddons</c>).</summary>
	    const int ForceExitGraceMs = 4000;
	    static bool _forceExitArmed = false;
	    /// <summary>Set by restart-with-addons (and similar) so <see cref="Application.Quit"/> is not blocked by the close confirm popup.</summary>
	    static bool s_allowQuitWithoutPrompt;

	    public static ExitTheProgram_MGR instance { get; private set; }

	    void Awake(){
	        if (instance != null && instance != this) {
	            Destroy(this);
	            return;
	        }
	        instance = this;
	        Application.wantsToQuit += WantsToQuit;
	    }

	    void OnDestroy(){
	        if (instance == this)
	            instance = null;
	        Application.wantsToQuit -= WantsToQuit;
	    }

	    /// <summary>
	    /// Marks the next quit as user-confirmed (no "Close the program?" popup), arms the force-exit watchdog,
	    /// and starts addon API teardown. Call before <see cref="Application.Quit"/> for programmatic restarts.
	    /// </summary>
	    public static void AllowQuitWithoutConfirmAndArmWatchdog(){
	        s_allowQuitWithoutPrompt = true;
	        // Do not set _quitPopupConfirmed here — only WantsToQuit consumes s_allowQuitWithoutPrompt
	        // so a stalled Quit cannot leave future closes skipping the Close prompt forever.
	        Addon_MGR.ShutdownAddonApiBeforeQuit();
	        ArmForceExitWatchdog();
	    }

	    bool WantsToQuit(){
	        if(_quitPopupConfirmed || s_allowQuitWithoutPrompt){
	            s_allowQuitWithoutPrompt = false;
	            Addon_MGR.ShutdownAddonApiBeforeQuit();
	            ArmForceExitWatchdog();
	            return true;
	        }
        
	        if(ConfirmPopup_UI.instance==null){ 
	            // Popup not ready (early boot) — block quit so we never skip "save first?" without a prompt.
	            Debug.LogWarning("[ExitTheProgram_MGR] Confirm popup missing; refusing quit until UI is ready or AllowQuitWithoutConfirmAndArmWatchdog is used.");
	            return false;
	        }

	        // Exit prompt already up — do not Abort it (that wiped Close and locked quit). Leave it, or force-quit if unresponsive.
	        // If deferred Uninstall Show stole the dialog, header will not match — reclaim Exit below.
	        if (_exitPromptOpen && ConfirmPopup_UI.instance.IsShowing
	            && ConfirmPopup_UI.instance.IsCloseProgramPrompt) {
	            _closeAttemptsWhileExitPrompt++;
	            if (_closeAttemptsWhileExitPrompt >= ForceQuitAfterCloseAttempts) {
	                Debug.LogWarning("[ExitTheProgram_MGR] Exit confirm unresponsive — forcing quit after repeated window-close attempts.");
	                OnExitConfirm();
	                return true;
	            }
	            return false;
	        }

	        // Stop deferred Uninstall before Abort/Show so CoShowUninstallConfirm cannot replace Exit acts.
	        AddonManager_UI.AbortPendingUninstallConfirm(alsoAbortPopup: false);

	        // Never permanently block quit on a stuck Uninstall/Settings confirm.
	        if (ConfirmPopup_UI.instance.IsShowing)
	            ConfirmPopup_UI.instance.AbortAndRestoreUi();

	        _exitPromptOpen = true;
	        _closeAttemptsWhileExitPrompt = 0;
	        ConfirmPopup_UI.instance.Show(
	            "Close the program? Make sure to save progress first (Ctrl+S)",
	            OnExitConfirm,
	            OnExitCanceled,
	            "Close",
	            "Don't Close");
	        return false;
	    }


	    void OnExitConfirm(){
	        _exitPromptOpen = false;
	        _closeAttemptsWhileExitPrompt = 0;
	        _quitPopupConfirmed = true;
	        // Do teardown now as well (not only on the second wantsToQuit pass) so external
	        // servers/processes start closing immediately and cannot keep quit in a limbo state.
	        Addon_MGR.ShutdownAddonApiBeforeQuit();
	        ArmForceExitWatchdog();
	        Application.Quit();
	    }

	    void OnExitCanceled(){
	        _exitPromptOpen = false;
	        _closeAttemptsWhileExitPrompt = 0;
	    }

	    /// <summary>Background timer that hard-terminates the process if Unity's normal quit pipeline stalls.
	    /// Idempotent. Background thread does not itself keep the process alive (IsBackground=true), so a clean
	    /// exit before the timeout simply kills the timer with the process. Safe to call repeatedly from
	    /// <see cref="WantsToQuit"/> and <see cref="OnExitConfirm"/>.</summary>
	    static void ArmForceExitWatchdog(){
	        if (_forceExitArmed) return;
	        _forceExitArmed = true;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
	        var t = new Thread(() => {
	            try { Thread.Sleep(ForceExitGraceMs); }
	            catch { }
	            // If Unity hasn't exited the process by now, force it.
	            try { System.Environment.Exit(0); }
	            catch { }
	            // Last resort: some shutdown deadlocks can ignore/abort Exit.
	            try { Thread.Sleep(1200); } catch { }
	            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); }
	            catch { }
	        }) { IsBackground = true, Name = "SPZ_ExitWatchdog" };
	        t.Start();
#endif
	    }
	}
}//end namespace
