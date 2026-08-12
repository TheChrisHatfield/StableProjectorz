using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>RPC center_max_off must settle chrome/layout like enter and in-app FULL/SRN exit.</summary>
public sealed class FullViewCenterMaxOffChromeTests {

	[Test]
	public void CenterMaxOff_SourceSyncsChromeAndLayoutLikeEnter() {
		string socket = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(socket);
		int off = src.IndexOf("mode == \"center_max_off\"", System.StringComparison.Ordinal);
		Assert.That(off, Is.GreaterThan(0));
		string body = src.Substring(off, System.Math.Min(900, src.Length - off));
		Assert.That(body, Does.Contain("TryExit()"));
		Assert.That(body, Does.Contain("SyncChromeToDriver()"),
			"center_max_off must not return after TryExit alone — outer chrome stays hidden");
		Assert.That(body, Does.Contain("ForceLayoutRefreshAfterPanelResize()"));
		Assert.That(body, Does.Contain("NotifyLayoutRefreshedForPendingGenRefit()"));
	}

	[Test]
	public void OuterChromeBinder_ResubscribesAfterSubsystemRegistration_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "FullView_OuterPanel_Chrome_Binder.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RuntimeInitializeLoadType.SubsystemRegistration"));
		Assert.That(src, Does.Contain("ActiveChanged -= OnFullViewActiveChanged"));
		Assert.That(src, Does.Contain("ActiveChanged += OnFullViewActiveChanged"));
	}
}
