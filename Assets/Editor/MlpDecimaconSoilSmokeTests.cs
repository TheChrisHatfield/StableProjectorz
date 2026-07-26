using NUnit.Framework;
using spz;
using spz.MlpDecimacon;
using UnityEngine;

/// <summary>
/// Pass D smoke: soil Decimacon LAVD → RoutePlan → body depth → value heads (not MultiHead).
/// </summary>
public sealed class MlpDecimaconSoilSmokeTests {

	[Test]
	public void Topology_Locks_5x96x4x12() {
		var body = new TransformerLiteBody();
		Assert.That(body.Layers, Is.EqualTo(5));
		Assert.That(body.Width, Is.EqualTo(96));
		Assert.That(body.Heads, Is.EqualTo(4));
		Assert.That(body.Window, Is.EqualTo(12));
	}

	[Test]
	public void RoutingHead_Loads_AndEmits_RoutePlan() {
		Assert.That(RoutingHeadRuntime.TryCreate(out var head, out string err), Is.True, err);
		var sched = new LavadSmartScheduler(seed: 3);
		var signal = sched.Dispatch(TelemetrySnapshot.ForPropose(0f));
		var plan = head.Plan(signal.EncodedSchedulerState, signal, 0.3f, 0.5f);
		Assert.That(plan.MaxNodes, Is.GreaterThanOrEqualTo(1));
		Assert.That(plan.MaxStages, Is.InRange(1, 3));
		Assert.That(plan.Stages.Count, Is.EqualTo(plan.MaxStages));
		Assert.That(plan.ActivationSparsityBudget, Is.InRange(0f, 1f));
	}

	[Test]
	public void Lavd_SigmaEncode_Q16_AndSaDepthTable() {
		var s = new LavadSmartScheduler(seed: 9);
		var pkt = s.Dispatch(TelemetrySnapshot.ForLive(0f));
		Assert.That(pkt.EncodedSchedulerState, Is.Not.Null);
		Assert.That(pkt.EncodedSchedulerState.Length, Is.EqualTo(16));
		Assert.That(ExtraLavdArmMap.SaDepthForArm(BanditArm.EnergyBalance), Is.EqualTo(1));
		Assert.That(ExtraLavdArmMap.SaDepthForArm(BanditArm.Throughput), Is.EqualTo(3));
		Assert.That(ExtraLavdArmMap.SaDepthForArm(BanditArm.LatencyCritical), Is.EqualTo(5));
	}

	[Test]
	public void Runtime_Forward_Produces_RoutePlan_And_Body() {
		Assert.That(MlpDecimaconRuntime.TryCreate(out var rt, out string err), Is.True, err);
		float[] feat = { 0.5f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.1f };
		var fr = rt.Forward(TelemetrySnapshot.ForPropose(0f), feat);
		Assert.That(fr.Plan, Is.Not.Null);
		Assert.That(fr.BodyVector, Is.Not.Null);
		Assert.That(fr.BodyVector.Length, Is.EqualTo(96));
		Assert.That(fr.ActiveLayers, Is.InRange(1, 5));
		Assert.That(fr.Stage.StagesRun, Is.GreaterThanOrEqualTo(1));
	}

	[Test]
	public void Factory_Neural_Prefers_MlpDecimacon_NotMultiHead() {
		var assist = ValuePaintAssistFactory.Create(preferNeural: true, out string which);
		Assert.That(which, Does.Not.Contain("MlpValuePaintAssist"), which);
		Assert.That(which, Does.Not.Contain("MultiHead"), which);
		if (MlpDecimaconPaintAssist.TryCreate(out _, out _)) {
			Assert.That(which, Does.Contain("MlpDecimaconPaintAssist"), which);
			Assert.That(assist, Is.InstanceOf<MlpDecimaconPaintAssist>());
			var p = assist.ProposeFromLuminance(0.5f);
			Assert.That(p.Source, Does.StartWith("mlp_decimacon"));
		} else {
			Assert.That(assist, Is.InstanceOf<DeterministicValuePaintAssist>());
		}
	}

	[Test]
	public void PaintBoundary_Refuses_Dto_Fields() {
		Assert.DoesNotThrow(() => LavdPaintBoundary.RefuseBanditToPaintDto());
		Assert.Throws<LavdPaintBoundary.LavdPaintBoundaryException>(
			() => LavdPaintBoundary.RefuseBanditToPaintDto("DesiredBin"));
	}

	[Test]
	public void ProductGate_Propose_Always_Runs_And_Updates_Bandit() {
		DecimaconProductGate.ResetForTests(5);
		var d = DecimaconProductGate.BeginPropose();
		Assert.That(DecimaconProductGate.LastRunForward, Is.True);
		float a0 = DecimaconProductGate.Scheduler.GetAlpha(d.SelectedArm);
		DecimaconProductGate.EndInference(d, elapsedMs: 2f, ranForward: true, accuracyProxy: 0.99f);
		// Success under budget may bump alpha
		Assert.That(DecimaconProductGate.Scheduler.GetAlpha(d.SelectedArm), Is.GreaterThanOrEqualTo(a0));
	}

	[Test]
	public void NoPaintUndo_In_Decimacon_Sources() {
		string root = Application.dataPath + "/_gm/Features/Paint/SmartValuePaint/MlpDecimacon";
		foreach (var file in System.IO.Directory.GetFiles(root, "*.cs")) {
			string text = System.IO.File.ReadAllText(file);
			Assert.That(text, Does.Not.Contain("PaintUndo_Scheduler"), file);
		}
	}
}
