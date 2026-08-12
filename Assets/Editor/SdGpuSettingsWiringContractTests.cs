using System.IO;
using NUnit.Framework;

/// <summary>
/// SD GPU Settings must map physical CUDA index → CUDA_VISIBLE_DEVICES correctly
/// (logical --gpu-device-id 0 after mask), and stay hardware-agnostic via nvidia-smi.
/// </summary>
public sealed class SdGpuSettingsWiringContractTests {
	static string ReadLaunch() {
		return File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Webui", "Launch_WebUI_bat_File.cs"));
	}

	[Test]
	public void GetCudaDeviceList_UsesNvidiaSmiIndexAndName() {
		string src = ReadLaunch();
		Assert.That(src, Does.Contain("GetCudaDeviceListString"));
		Assert.That(src, Does.Contain("--query-gpu=index,name"));
		Assert.That(src, Does.Not.Contain("Tesla T10")); // no hardcoded card names
	}

	[Test]
	public void GpuPinnedGpu_UsesCudaVisibleDevices_AndLogicalGpuDeviceIdZero() {
		string src = ReadLaunch();
		int i = src.IndexOf("string argsBase = gpuId >= 0", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("bool canDirectLaunch", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("--gpu-device-id 0"));
		Assert.That(body, Does.Not.Contain("--gpu-device-id \" + gpuId"));
		Assert.That(src, Does.Contain("CUDA_VISIBLE_DEVICES=\" + gpuId"));
	}
}
