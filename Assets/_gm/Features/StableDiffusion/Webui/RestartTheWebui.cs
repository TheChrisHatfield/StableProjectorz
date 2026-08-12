using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Lavender.Systems;
using System;
using SimpleFileBrowser;


namespace spz {

	public class RestartTheWebui : MonoBehaviour {
	    [SerializeField] protected Button _launchButton;
	    [SerializeField] protected Button _fileButton; //allows to specify path to the file that should be launched
	    [SerializeField] protected string _openFile_os_window_headerMsg;
	    [Space(10)]
	    [SerializeField] protected string _defaultRelativePath = "./" + LaunchWebUIBatFile.WebuiFolderNameNeo + "/webui-user.bat";
	    [SerializeField] protected string _playerPrefs_filepathID = "_RestartWebuiFilepath";
	    [SerializeField] protected Animation _anim;

	    bool _isPlayingAttentionAnim = false;

	    string _filepath; // internal variable to hold the correct path
	    public Action OnClicked { get; set; } = null;


	    public void KeepPlaying_attention_anim(bool isKeepPlaying){
	        if (_anim == null) { return; }
	        if (isKeepPlaying == _isPlayingAttentionAnim){ return; }
	        _isPlayingAttentionAnim = isKeepPlaying;
	        if (isKeepPlaying){ 
	            _anim.Play(); 
	        }else {
	            _anim.Stop();
	            _anim.clip.SampleAnimation(gameObject, 0);
	        }
	    }

	    protected string TryFindFileInParentDirectories(string filepath){
	        // If path is absolute and file exists, return it directly
	        if (Path.IsPathRooted(filepath) && File.Exists(filepath)){ return filepath; }

	        // Get the initial directory to start searching from
	        string currentDir = Directory.GetParent(Application.dataPath).FullName;
	        // Get just the filename, regardless of path structure
	        string filename = Path.GetFileName(filepath);

	        while (currentDir != null){
	            string attemptPath = Path.Combine(currentDir, filename);
	            if (File.Exists(attemptPath)){ return attemptPath;}

	            // Move up one directory
	            DirectoryInfo parentDir = Directory.GetParent(currentDir);
	            if (parentDir == null) break;
	            currentDir = parentDir.FullName;
	        }
	        // If we couldn't find the file, return the original path
	        return filepath;
	    }

	    protected virtual void OnStartWebuiButton(){
	        if(string.IsNullOrEmpty(_filepath)){
	            Print_Webui_NotFound();
	            return; 
	        }

	        string full_path = _filepath;
	        if (!Path.IsPathRooted(full_path)) {
	            string exeDirectory = Directory.GetParent(Application.dataPath).FullName;
	            full_path = Path.Combine(exeDirectory, full_path);
	        }
	        try {
	            full_path = Path.GetFullPath(full_path);
	        } catch (Exception) {
	            Debug.Log("path is incorrect, please check it again");
	        }
        
	        // Try to find the file recursively in parent directories if it doesn't exist
	        if (!File.Exists(full_path)){
	            full_path = TryFindFileInParentDirectories(full_path);
	        }
	        // Prefs/default often point at a missing .lnk; fall back to the same aggressive search as auto-launch.
	        if (!File.Exists(full_path)) {
	            string discovered = LaunchWebUIBatFile.GetWebuiFilePathStatic(printStatusTextIfNotFound: false);
	            if (!string.IsNullOrEmpty(discovered) && File.Exists(discovered)) {
	                full_path = discovered;
	                Debug.Log($"[RestartTheWebui] Using discovered WebUI launch path: {full_path}");
	            }
	        }
	        if (File.Exists(full_path) == false){
	            Print_Webui_NotFound();
	            return; 
	        }
	        LaunchWebUIBatFile.TryCloseLastLaunchedWebUi();
	        full_path = OnWillLaunchWebui_AdjustArgs(full_path);
	        string workingDir;
	        bool showExternalWindows = LaunchWebUIBatFile.PrefsWantShowExternalProcessWindows();
	        string launchPath = LaunchWebUIBatFile.GetLaunchPathWithGpuSetting(full_path, out workingDir, preferNoConsole: !showExternalWindows);
	        // keepWindow false — show/hide via hidden only; /K leaves zombie CMD after Forge exits.
	        uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(
	            launchPath,
	            isJustFile:true,
	            workingDir,
	            keepWindow:false,
	            hidden:!showExternalWindows,
	            attachToConsole:false
	        );
	        if (pid == 0){
	            Debug.LogError("Failed to launch the file. Consider launching StableProjectorz as Admin.");
	            return;
	        }
	        LaunchWebUIBatFile.SetLastLaunchedWebUiPid(pid);
	        LaunchWebUIBatFile.NotifyWebUiLaunchStarted();
	        OnClicked?.Invoke();
	    }


	    void Print_Webui_NotFound(){
	        string msg = "File not found in the current or parent directories." +
	                        "\nVerify it's correct or launch StableProjectorz as Admin.";
	        Viewport_StatusText.instance.ShowStatusText(msg, false, 3, false);
	    }


	    protected virtual string OnWillLaunchWebui_AdjustArgs(string path){
	        return path; //child classes can append custom args, for example path+"--precision full", or something like that.
	    }

	    protected virtual void OnSpecifyFileButton(){
        
	        FileBrowser.SetFilters(true, new FileBrowser.Filter("Executables", "bat", "cmd", "lnk", "exe", "sh"));
	        FileBrowser.SetDefaultFilter("bat");

	        FileBrowser.ShowLoadDialog( (paths) => {
	            if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0])){
	                _filepath = Path.GetFullPath(paths[0]);
	                PlayerPrefs.SetString(_playerPrefs_filepathID, _filepath);
	            }
	        }, 
	        null, 
	        FileBrowser.PickMode.Files, false, null, null, _openFile_os_window_headerMsg, "Select");
	    }

	    protected virtual void Awake(){
	        SpzUiThemeOps.ThemeChanged += ApplyThemeTokens;
	        ApplyThemeTokens();
	    }

	    protected virtual void OnDestroy() {
	        SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens;
	    }

	    /// <summary>
	    /// Nomad flat SD SERV / 3D SERV + folder picker (top strip) — not Unity default light bricks.
	    /// Ensure hit faces so label ClearNonFace cannot kill launch / pick-path (gen litmus).
	    /// </summary>
	    protected virtual void ApplyThemeTokens() {
	        if (!SpzUiThemeOps.ShouldRecolorBoundChrome) {
	            if (_launchButton != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_launchButton.transform);
	            if (_fileButton != null)
	                SpzUiThemeOps.RestoreBoundChromeUnder(_fileButton.transform);
	            return;
	        }
	        var t = SpzUiThemeOps.Active;
	        ThemeTopStripButton(_launchButton, t, applyFolderIcon: false);
	        ThemeTopStripButton(_fileButton, t, applyFolderIcon: true);
	    }

	    static void ThemeTopStripButton(Button btn, SpzUiThemeOps.ThemeTokens t, bool applyFolderIcon) {
	        if (btn == null) return;
	        SpzUiThemeOps.EnsureSelectableHitFace(btn);
	        SpzUiThemeOps.ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
	        if (btn.targetGraphic is Image face) {
	            SpzUiThemeOps.ApplyRoundedControlSprite(face, markEligible: true);
	            SpzUiThemeOps.FlattenToolFaceImage(face);
	            face.preserveAspect = false;
	            face.raycastTarget = true;
	        }
	        // Leading Monolith — centered Bullseye/Folder stamps over "SERV"; hide authored folders too.
	        SpzUiThemeOps.ApplyControlLineIconLeading(btn.transform,
	            applyFolderIcon ? StudioLineIcon.Folder : StudioLineIcon.Bullseye, 16f);
	        foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>(true)) {
	            if (tmp == null) continue;
	            // SD SERV / 3D SERV: strip tracking 18 overflows ~118px top-strip (Soft litmus).
	            SpzUiThemeOps.ApplyBoundChromeCompactToolLabelTmp(tmp, t.textPrimary, 11f);
	        }
	        SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
	    }

	    protected virtual void Start(){
	        _launchButton.onClick.AddListener(OnStartWebuiButton);
	        _fileButton.onClick.AddListener(OnSpecifyFileButton);

	        _filepath = PlayerPrefs.GetString(_playerPrefs_filepathID, _defaultRelativePath);
	        _filepath = _filepath.Length<2048? _filepath : _defaultRelativePath;//helps if glitched
	        // Prefab often inactive under ThemeChanged — re-assert after Start wiring.
	        ApplyThemeTokens();
	    }
    

	}
}//end namespace
