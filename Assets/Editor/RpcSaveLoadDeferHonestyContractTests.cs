using System.IO;
using NUnit.Framework;

public sealed class RpcSaveLoadDeferHonestyContractTests {

	[Test]
	public void DefersResponse_IncludesSaveAndLoadProject() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public static bool DefersResponseUntilProjectSaveIdle(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("spz.cmd.save_project"));
		Assert.That(body, Does.Contain("spz.cmd.load_project"));
	}

	[Test]
	public void CoRespond_UsesLastProjectSaveLoadSucceeded() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("LastProjectSaveSucceeded"));
		Assert.That(src, Does.Contain("LastProjectLoadSucceeded"));
		Assert.That(src, Does.Contain("save cancelled or failed"));
		Assert.That(src, Does.Contain("load cancelled or failed"));
	}

	[Test]
	public void FastPath_SaveProject_RefusesWhileDialogInFlight() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool SaveProject()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("IsProjectSaveInFlight"),
			"Second RPC must not return true while the save dialog is still open.");
		Assert.That(body, Does.Contain("_isImportingModel"),
			"Save must refuse while a mesh import is in flight.");
	}

	[Test]
	public void FastPath_LoadProject_RefusesWhileImportInFlight() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool LoadProject()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("_isImportingModel"),
			"Load must refuse while a mesh import is in flight.");
		Assert.That(body, Does.Contain("Gen3D_API"),
			"Load must refuse while Gen3D is busy.");
	}

	[Test]
	public void UiSaveLoad_RefuseImportAndGen3dBusy() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int saveAt = src.IndexOf("public void DoSaveProject()", System.StringComparison.Ordinal);
		int loadAt = src.IndexOf("public void DoLoadProject()", System.StringComparison.Ordinal);
		string saveBody = src.Substring(saveAt, System.Math.Min(1600, loadAt - saveAt));
		string loadBody = src.Substring(loadAt, System.Math.Min(1600, src.Length - loadAt));
		Assert.That(saveBody, Does.Contain("_isImportingModel"));
		Assert.That(saveBody, Does.Contain("Gen3D_API"));
		Assert.That(loadBody, Does.Contain("_isImportingModel"));
		Assert.That(loadBody, Does.Contain("Gen3D_API"));
	}

	[Test]
	public void ProjectMeshLoad_DoesNotAttachSlWhenImportBusy() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs");
		string src = File.ReadAllText(path);
		int tryLoadAt = src.IndexOf("public bool TryLoad(", System.StringComparison.Ordinal);
		Assert.That(tryLoadAt, Is.GreaterThan(0));
		string body = src.Substring(tryLoadAt, System.Math.Min(1400, src.Length - tryLoadAt));
		int busyCheck = body.IndexOf("another mesh import is already in flight", System.StringComparison.Ordinal);
		int assignSl = body.IndexOf("_modelsHandler_SL = sl", System.StringComparison.Ordinal);
		Assert.That(busyCheck, Is.GreaterThan(0));
		Assert.That(assignSl, Is.GreaterThan(busyCheck),
			"Must refuse before attaching project SL while an import is already running.");
		Assert.That(src, Does.Contain("_modelsHandler_SL = null"),
			"Failed imports must clear deferred project SL so the next model cannot inherit it.");
	}
}
