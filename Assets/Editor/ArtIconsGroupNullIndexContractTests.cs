using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// ArtIconsGroup must not index FindIndex=-1 or icons[0] when every slot is null — that crashes
/// mid-generation UI after a failed spawn / destroy race.
/// </summary>
public sealed class ArtIconsGroupNullIndexContractTests {

	static string Src() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Icons", "IconUI", "ArtIconsGroup.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void Ctor_DoesNotIndexNegativeFindIndex() {
		string src = Src();
		int first = src.IndexOf("int first = Array.FindIndex(icons", StringComparison.Ordinal);
		Assert.That(first, Is.GreaterThan(0));
		string body = src.Substring(first, Math.Min(450, src.Length - first));
		Assert.That(body, Does.Contain("first < 0"),
			"all-null icon batch must not call icons[first] when FindIndex returns -1");
		Assert.That(body.IndexOf("first < 0", StringComparison.Ordinal),
			Is.LessThan(body.IndexOf("icons[first]", StringComparison.Ordinal)),
			"guard must precede the icons[first] click");
	}

	[Test]
	public void Load_DoesNotAssumeIconsZeroWhenChosenIxIsMinusOne() {
		string src = Src();
		int load = src.IndexOf("public void Load_AfterSpawned(", StringComparison.Ordinal);
		Assert.That(load, Is.GreaterThan(0));
		string body = src.Substring(load, Math.Min(700, src.Length - load));
		Assert.That(body, Does.Not.Contain("icons[0]"),
			"chosenIconIx==-1 must not hardcode icons[0]");
		Assert.That(body, Does.Contain("FindIndex(icons"),
			"load must search for a live icon instead of assuming slot 0");
	}
}
