using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Dev/lock-in traceback for Klein structure orchestration. Default off for players —
	/// agent/capture may force-enable. Do not spam viewport HUD.
	/// </summary>
	public static class KleinStructureTrace {
	    const string PrefsKey = "spz.klein.structure_trace.v1";

	    static Dictionary<string, object> _last;

	    /// <summary>Runtime gate. PlayerPrefs default 0 (off). Editor may set 1 during probes.</summary>
	    public static bool Enabled {
	        get {
#if UNITY_EDITOR
	            if (PlayerPrefs.GetInt(PrefsKey, 0) != 0) return true;
	            // Editor default off unless prefs set — agents call ForceEnableForProbe.
	            return false;
#else
	            return PlayerPrefs.GetInt(PrefsKey, 0) != 0;
#endif
	        }
	        set {
	            PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
	            PlayerPrefs.Save();
	        }
	    }

	    public static void ForceEnableForProbe() => Enabled = true;

	    public static void Clear(){ _last = null; LastRejectReason = ""; }

	    /// <summary>
	    /// Last structure-channel reject_reason (always updated, even when verbose trace is off).
	    /// Used by payload abort messaging so style_ref_missing is not mislabeled as depth-missing.
	    /// </summary>
	    public static string LastRejectReason { get; private set; } = "";

	    public static void Set(string key, object value){
	        if (key == "reject_reason")
	            LastRejectReason = value != null ? value.ToString() : "";
	        if (!Enabled) return;
	        if (_last == null) _last = new Dictionary<string, object>();
	        _last[key] = value;
	    }

	    public static Dictionary<string, object> SnapshotOrNull(){
	        if (!Enabled || _last == null) return null;
	        return new Dictionary<string, object>(_last);
	    }

	    public static void BeginRequest(){
	        LastRejectReason = "";
	        if (!Enabled) return;
	        _last = new Dictionary<string, object>();
	    }

	    /// <summary>
	    /// Start a fresh request dict only when none exists. Used after PayloadMaker
	    /// BeginRequest+AppendRefControl so attach does not wipe LoRA keys.
	    /// </summary>
	    public static void EnsureRequestStarted(){
	        if (!Enabled) return;
	        if (_last == null) _last = new Dictionary<string, object>();
	    }
	}
}
