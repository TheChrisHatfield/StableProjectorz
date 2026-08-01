using System.IO;
using NUnit.Framework;

/// <summary>
/// Agent bridge is add-on gated (SpzMcpSPZ / "SPZ MCP"): host listens only when enabled + Listen,
/// settings flow through spz.cmd.agent_bridge_*, StaticEvents exposes introspection for tools.
/// </summary>
public sealed class AgentBridgeAddonContractTests {

	static string RepoPath(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		return Path.GetFullPath(path);
	}

	[Test]
	public void StaticEvents_HasIntrospectionAndTryInvokeDynamic() {
		string path = RepoPath("Assets", "_gm", "_Core", "Logic", "Callbacks + Events", "StaticEvents.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public static List<string> GetRegisteredIds()"));
		Assert.That(src, Does.Contain("public static Type[] GetParameterTypes(string id)"));
		Assert.That(src, Does.Contain("public static bool TryInvokeDynamic(string id, object[] args, out string error)"));
	}

	[Test]
	public void Bridge_GatesOnAddonAndListen_NotSpzConfig() {
		string path = RepoPath("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Bridge.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SpzMcpSPZ"));
		Assert.That(src, Does.Contain("IsAddonEnabledStatic"));
		Assert.That(src, Does.Contain("OnAddonEnabledStateChanged"));
		Assert.That(src, Does.Contain("ApplySettings"));
		Assert.That(src, Does.Contain("SyncListeningState"));
		Assert.That(src, Does.Not.Contain("--agent-bridge"),
			"Primary control must be the add-on, not spz.config flags.");
		Assert.That(src, Does.Contain("IPAddress.Loopback"));
	}

	[Test]
	public void SocketServer_ExposesAgentBridgeSettingsCommands() {
		string path = RepoPath("Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("spz.cmd.agent_bridge_get_status"));
		Assert.That(src, Does.Contain("spz.cmd.agent_bridge_apply_settings"));
		Assert.That(src, Does.Contain("TryExecuteAgentBridgeCommand"));
		Assert.That(src, Does.Contain("bridge.ApplySettings"));
		string bridgePath = RepoPath("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Bridge.cs");
		string bridgeSrc = File.ReadAllText(bridgePath);
		Assert.That(bridgeSrc, Does.Contain("Enable it in Add-on Manager first."));
	}

	[Test]
	public void LiteAddon_ExistsWithPanelControls() {
		string addonJson = RepoPath("Assets", "StreamingAssets", "Addons", "SpzMcpSPZ", "addon.json");
		string initPy = RepoPath("Assets", "StreamingAssets", "Addons", "SpzMcpSPZ", "__init__.py");
		Assert.That(File.Exists(addonJson), Is.True, addonJson);
		Assert.That(File.Exists(initPy), Is.True, initPy);
		string json = File.ReadAllText(addonJson);
		Assert.That(json, Does.Contain("\"displayName\": \"SPZ MCP\""));
		string py = File.ReadAllText(initPy);
		Assert.That(py, Does.Contain("ADDON_ID = \"SpzMcpSPZ\""));
		Assert.That(py, Does.Contain("ADDON_TITLE = \"SPZ MCP\""));
		Assert.That(py, Does.Contain("agent_bridge.get_status"));
		Assert.That(py, Does.Contain("agent_bridge.apply_settings"));
		Assert.That(py, Does.Contain("Enable Listen"));
		Assert.That(py, Does.Contain("Apply Settings"));
	}

	[Test]
	public void Tools_UseStaticEventsIntrospection() {
		string path = RepoPath("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("StaticEvents.GetRegisteredIds()"));
		Assert.That(src, Does.Contain("StaticEvents.TryInvokeDynamic"));
		Assert.That(src, Does.Contain("\"describe\""));
	}

	[Test]
	public void Tools_ExposeExpandedSdControlSurface() {
		string path = RepoPath("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("\"get_sd_gen_settings\""));
		Assert.That(src, Does.Contain("\"set_sd_gen_settings\""));
		Assert.That(src, Does.Contain("\"list_sd_options\""));
		Assert.That(src, Does.Contain("\"set_controlnet_unit\""));
		Assert.That(src, Does.Contain("what_to_send"));
		Assert.That(src, Does.Contain("TrySetWhatImageToSend"));
		Assert.That(src, Does.Contain("\"generate\""));
		Assert.That(src, Does.Contain("ConfirmGenerateStarted_crtn"));
		Assert.That(src, Does.Contain("prepare_flux_klein_test"));
		Assert.That(src, Does.Contain("positive_prompt"));
		Assert.That(src, Does.Contain("TrySelectVAEByName"));
		Assert.That(File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Neural_Models.cs")),
			Does.Contain("TrySelectModelByName"));
		Assert.That(File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Samplers.cs")),
			Does.Contain("TrySelectSamplerByName"));
		Assert.That(File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_VAE.cs")),
			Does.Contain("TrySelectVAEByName"));
	}

	[Test]
	public void Tools_ExposeFullAutonomySpzCmdSurface() {
		string tools = File.ReadAllText(RepoPath("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs"));
		Assert.That(tools, Does.Contain("\"list_spz_commands\""));
		Assert.That(tools, Does.Contain("\"spz_cmd\""));
		Assert.That(tools, Does.Contain("\"get_generation_status\""));
		Assert.That(tools, Does.Contain("\"stop_generation\""));
		Assert.That(tools, Does.Contain("\"focus_camera\""));
		Assert.That(tools, Does.Contain("ProcessRequestDirect"));
		string proto = File.ReadAllText(RepoPath("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Protocol.cs"));
		Assert.That(proto, Does.Contain("PROTOCOL_VERSION = 2"));
		string socket = File.ReadAllText(RepoPath("Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs"));
		Assert.That(socket, Does.Contain("public JObject ProcessRequestDirect"));
	}

	[Test]
	public void Klein_CoOptsControlNetImageAsImg2ImgInit() {
		string list = File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "SD_ControlNetsList_UI.cs"));
		Assert.That(list, Does.Contain("TryGetDisposableKleinImg2ImgInit"));
		Assert.That(list, Does.Contain("HasKleinImg2ImgInitSource"));
		Assert.That(list, Does.Contain("TryPeekKleinImg2ImgInitSource"));
		Assert.That(list, Does.Contain("leftover ContentCam/CustomFile"));
		string hub = File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "StableDiffusion_Hub.cs"));
		Assert.That(hub, Does.Contain("HasKleinImg2ImgInitSource()"));
		Assert.That(hub, Does.Contain("!isMakingBackgrounds"));
		string payload = File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		Assert.That(payload, Does.Contain("TryPeekKleinImg2ImgInitSource"));
		Assert.That(payload, Does.Contain("Klein img2img init from ControlNet"));
		Assert.That(payload, Does.Contain("do not TryGet again"));
		Assert.That(payload, Does.Contain("!forceFullWhiteMask"));
		Assert.That(payload, Does.Contain("cameras/mask painter/workflow not ready"));
		string genReq = File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs"));
		Assert.That(genReq, Does.Contain("img2img aborted: missing init image"));
		string imgs = File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_ImagesDisplay.cs"));
		Assert.That(imgs, Does.Contain("HasValidKleinImg2ImgInit"));
		Assert.That(imgs, Does.Contain("Agent/MCP / restore path"));
		Assert.That(imgs, Does.Contain("EncodeToPNG / TextureToBase64 need a CPU-readable"));
		string unit = File.ReadAllText(RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs"));
		Assert.That(unit, Does.Contain("isActivated"));
		Assert.That(unit, Does.Contain("is_currModel_none"));
		Assert.That(unit, Does.Contain("TryGetDisposableKleinImg2ImgInit"));
		Assert.That(unit, Does.Contain("Bail out before allocating disposable bitmaps"));
		Assert.That(unit, Does.Contain("CustomFile copies are not stored on intermediates"));
		Assert.That(list, Does.Contain("Collapsed/disabled units"));
	}

	[Test]
	public void StaticEvents_TryInvokeDynamic_RuntimeReportsUnknownId() {
		Assert.That(spz.StaticEvents.TryInvokeDynamic("__agent_bridge_missing_event__", null, out string error), Is.False);
		Assert.That(error, Does.Contain("not registered"));
	}
}
