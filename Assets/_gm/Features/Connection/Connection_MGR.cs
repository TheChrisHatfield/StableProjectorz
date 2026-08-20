using UnityEngine;
using UnityEngine.UI;

namespace spz {

	// knows about 2 connection panels:  For stableDiffusion (a1111) and for 3d generation (Trellis, etc)
	// This allows us to connect to two different servers, one for generating textures, one for geometry.
	public class Connection_MGR : MonoBehaviour{
	    public static Connection_MGR instance { get; private set; } = null;

	    [SerializeField] RectTransform _placeOnTopOfMainView;

		/// <summary>SD / 3D server strip parented to the main viewport; hidden with <see cref="FullView_OuterPanel_Chrome_Binder"/> in on-screen full view.</summary>
		public RectTransform ViewportTopConnectionStrip => _placeOnTopOfMainView;
	    [SerializeField] GraphicRaycaster _raycaster; //will raycast towards the help button
	    [Space(10)]
	    [SerializeField] ConnectionPanel_UI _a1111_connPanel;
	    [SerializeField] ConnectionPanel_UI _3d_connPanel;

	    bool _did_init = false;
	    public static bool is_sd_connected =>  instance?._a1111_connPanel != null && instance._a1111_connPanel.isConnected;
	    /// <summary>SD strip ping marked Cloud Inference shim (fal/Demo/remote facade), not a local Forge.</summary>
	    public static bool is_cloud_inference =>
	        instance?._a1111_connPanel != null && instance._a1111_connPanel.isCloudInferenceConnected;
	    public static bool is_3d_connected =>  instance?._3d_connPanel != null && instance._3d_connPanel.isConnected;

	    /// <summary>Immediate SERV red + clear Cloud emblem (Cloud Inference Disconnect; do not wait for ping).</summary>
	    public static bool ForceMarkSdDisconnected() {
	        if (instance?._a1111_connPanel == null) return false;
	        instance._a1111_connPanel.ForceMarkDisconnected();
	        return true;
	    }

	    public static string A1111_IP_AND_PORT => instance?._a1111_connPanel == null ? "" : "http://" + instance._a1111_connPanel.ip_and_port;
	    public static string A1111_SD_API_URL  => instance?._a1111_connPanel == null ? "" : "http://" + instance._a1111_connPanel.ip_and_port + "/sdapi/v1";
	    public static string A1111_CTRLNET_API_URL => instance?._a1111_connPanel == null ? "" : "http://" + instance._a1111_connPanel.ip_and_port + "/controlnet";
	    public static string A1111_INTERNAL_API_URL => instance?._a1111_connPanel == null ? "" : "http://" + instance._a1111_connPanel.ip_and_port + "/internal";

	    public static string GEN3D_URL =>  instance?._3d_connPanel == null ? "" : "http://" + instance._3d_connPanel.ip_and_port;


	    public void Save( StableProjectorz_SL spz ){
	        spz.connectionPanel = new ConnectionPanel_SL();
	        _a1111_connPanel?.Save(spz);
	        _3d_connPanel?.Save(spz);
	    }

	    public void Load( StableProjectorz_SL spz ){
	        _a1111_connPanel?.Load(spz);
	        _3d_connPanel?.Load(spz);
	        Init_maybe();
	    }

	    void Awake(){
	        if(instance != null){ DestroyImmediate(this); return; }
	        instance = this;
	        Init_maybe();
	    }


	    void Update(){
	        Global_Skeleton_UI.instance?.Place_onto_MainViewport_between_ribbons(_placeOnTopOfMainView);
	    }
    
	    void Init_maybe(){
	        if(_did_init){ return; }
	        if (_a1111_connPanel == null || _3d_connPanel == null){ return; }
	        _did_init = true;
	        _a1111_connPanel.Init_Maybe();
	        _3d_connPanel.Init_Maybe();
	    }
	}
}//end namespace
