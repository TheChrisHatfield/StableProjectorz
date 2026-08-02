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

	    public static void Clear(){ _last = null; }

	    public static void Set(string key, object value){
	        if (!Enabled) return;
	        if (_last == null) _last = new Dictionary<string, object>();
	        _last[key] = value;
	    }

	    public static Dictionary<string, object> SnapshotOrNull(){
	        if (!Enabled || _last == null) return null;
	        return new Dictionary<string, object>(_last);
	    }

	    public static void BeginRequest(){
	        if (!Enabled) return;
	        _last = new Dictionary<string, object>();
	    }
	}
}
