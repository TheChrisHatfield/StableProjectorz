using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace spz {

	public class ExitTheProgram_MGR : MonoBehaviour
	{
	    bool _quitPopupConfirmed = false;
	    /// <summary>Hard-exit cap so a hung OnDestroy / Dispose / native plugin can't keep the process alive forever
	    /// (heavy painting + many RenderUdims layers can stall GPU release; some IL2CPP/Mono shutdown paths in standalone Win builds
	    /// don't actually terminate the process on Application.Quit alone — see <c>Launch_Addons_Bat_File.RestartWithAddons</c>
	    /// which already uses <c>Environment.Exit</c> for the same reason).</summary>
	    const int ForceExitGraceMs = 4000;
	    static bool _forceExitArmed = false;

	    void Awake(){
	        Application.wantsToQuit += WantsToQuit;
	    }

	    bool WantsToQuit(){
	        if(_quitPopupConfirmed){
	            Addon_MGR.ShutdownAddonApiBeforeQuit();
	            ArmForceExitWatchdog();
	            return true;
	        }
        
	        if(ConfirmPopup_UI.instance==null){ 
	            OnExitConfirm(); 
	            return true; 
	        }
	        ConfirmPopup_UI.instance.Show("Close the program? Make sure to save progress first (Ctrl+S)", OnExitConfirm, OnExitCanceled, "Close", "Don't Close");
	        return false;
	    }


	    void OnExitConfirm(){
	        _quitPopupConfirmed = true;
	        // Do teardown now as well (not only on the second wantsToQuit pass) so external
	        // servers/processes start closing immediately and cannot keep quit in a limbo state.
	        Addon_MGR.ShutdownAddonApiBeforeQuit();
	        ArmForceExitWatchdog();
	        Application.Quit();
	    }

	    void OnExitCanceled(){
	        //do nothing.
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
