using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// TryBindListener calls TcpListener.Start() and only then launches the accept thread. If the thread
/// launch throws, Start() has already bound 127.0.0.1:port — dropping the reference without Stop()
/// keeps the port held for the whole session, so Python add-ons cannot connect and the next launch
/// reports "port already in use". It must also not leave _isRunning true with no listener thread.
/// </summary>
public sealed class AddonSocketBindFailureReleasesPortContractTests {

	static string ReadSocketServer() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		return File.ReadAllText(path);
	}

	static string BindBlock() {
		string src = ReadSocketServer();
		int i = src.IndexOf("bool TryBindListener()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0), "the bind helper must exist");
		int end = src.IndexOf("static string GetReadyMarkerPath(", i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i), "anchor on the real method block");
		return src.Substring(i, end - i);
	}

	[Test]
	public void BindFailureStopsTheListenerBeforeDroppingIt() {
		string body = BindBlock();

		int start = body.IndexOf("_listener.Start();", StringComparison.Ordinal);
		int katch = body.IndexOf("catch (Exception e)", StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThan(0));
		Assert.That(katch, Is.GreaterThan(start));

		string failure = body.Substring(katch);
		int stop = failure.IndexOf("_listener?.Stop();", StringComparison.Ordinal);
		int drop = failure.IndexOf("_listener = null;", StringComparison.Ordinal);
		Assert.That(stop, Is.GreaterThan(0),
			"an already-bound listener must be stopped or the port stays held all session");
		Assert.That(drop, Is.GreaterThan(stop),
			"Stop() must happen before the reference is dropped");
	}

	[Test]
	public void BindFailureDoesNotReportRunning() {
		string body = BindBlock();
		int katch = body.IndexOf("catch (Exception e)", StringComparison.Ordinal);
		string failure = body.Substring(katch);

		Assert.That(failure, Does.Contain("_isRunning = false;"),
			"_isRunning true with no accept thread makes shutdown and status lie");
		Assert.That(failure, Does.Contain("_listenerThread = null;"),
			"a thread that never started must not stay referenced as the live listener thread");
	}

	[Test]
	public void SuccessPathStillStartsTheAcceptThread() {
		string body = BindBlock();
		int start = body.IndexOf("_listener.Start();", StringComparison.Ordinal);
		int thread = body.IndexOf("_listenerThread.Start();", StringComparison.Ordinal);
		int ret = body.IndexOf("return true;", StringComparison.Ordinal);
		Assert.That(thread, Is.GreaterThan(start), "bind, then accept");
		Assert.That(ret, Is.GreaterThan(thread), "only report success after the thread is running");
	}
}
