using UnityEngine;

namespace spz {

	/// <summary>Where smudge writes when both layer stack and mesh accumulation are valid shape.</summary>
	public enum SmudgeWriteTargetPreference {
		/// <summary>Thompson + opacity at stroke start steers multi-layer <em>underlay</em> (full composite under active vs skip that pass); with a layer stack present, writes stay on the active layer when <see cref="LayerSmudgeGateOpen"/>.</summary>
		Auto,
		LayerStack,
		GeneratedMesh
	}

	/// <summary>Stroke-locked outcome for <see cref="SmudgeWriteTargetPreference.Auto"/> (set on first smudge frame).</summary>
	public enum SmudgeAdaptiveRouteLock {
		Inactive,
		PreferLayer,
		PreferMesh
	}

	/// <summary>
	/// Isolates smudge <em>writes</em> between layer stack, mesh UV accumulation, and Art icon UV source.
	/// Kernel spacing follows <see cref="PaintUndo_Scheduler.GetSmudgeKernelSpacingMultiplier"/> when undo manager exists (same contextual bucket + capture Thompson arms as readback/yield; no smudge-only bandit).
	/// For <see cref="SmudgeWriteTargetPreference.Auto"/>, stroke-locked <see cref="SmudgeAdaptiveRouteLock.PreferMesh"/> does <em>not</em> redirect the write off the active layer when
	/// <see cref="LayerSmudgeGateOpen"/> is true — paint stays on <c>ActiveLayer.Content</c>. PreferMesh only steers underlay policy in <c>Inpaint_MaskPainter</c> (multi-layer under pass).
	/// </summary>
	public static class SmudgeStrokeRouter {

		public enum WriteDomain {
			None,
			LayerStack,
			MeshAccumulation,
			ArtIconUvTexture,
			/// <summary>Layer buffer only, no underlay (fallback when other domains unavailable).</summary>
			LayerOnlyNoUnderlay
		}

		public struct Plan {
			public RenderUdims Dest;
			public RenderUdims Underlay;
			public PaintUndoNonStackTarget UndoKind;
			public WriteDomain Domain;
			/// <summary>Multiplies smudge neighbor spacing (workload + capture bandit–aligned arm bump when <see cref="PaintUndo_MGR"/> is present).</summary>
			public float KernelSpacingMultiplier;
		}

		const float ReferencePixelsPerSlice = 512f * 512f;

		static bool SameShape(RenderUdims a, RenderUdims b) {
			return a != null && b != null && a.texArray != null && b.texArray != null
			       && a.width == b.width && a.height == b.height && a.UdimsCount == b.UdimsCount;
		}

		/// <summary>True if any layer is visible and has content (used only for diagnostics / future UI).</summary>
		public static bool StackHasAnyVisiblePaintLayer(PaintLayerStack_MGR stack) {
			if (stack?.Layers == null) return false;
			foreach (var l in stack.Layers) {
				if (l != null && l.Visible && l.Content != null)
					return true;
			}
			return false;
		}

		/// <summary>
		/// <b>Barrier:</b> layer smudge only when the active layer is visible and <paramref name="layerPaintTarget"/> is exactly that layer’s <c>Content</c>.
		/// Prevents smudging a hidden active buffer while other layers are visible, or mixing mesh/art writes with the wrong buffer.
		/// </summary>
		public static bool LayerSmudgeGateOpen(PaintLayerStack_MGR stack, RenderUdims layerPaintTarget) {
			if (layerPaintTarget == null) return false;
			var active = stack?.ActiveLayer;
			if (active == null || active.Content == null) return false;
			if (!active.Visible) return false;
			return ReferenceEquals(layerPaintTarget, active.Content);
		}

		static bool UnderlayBarrierOk(RenderUdims dest, RenderUdims underlay) {
			if (underlay == null || underlay.texArray == null) return true;
			if (dest?.texArray == null) return false;
			return dest.texArray != underlay.texArray;
		}

		public static float ComputeKernelSpacingMultiplier(RenderUdims dest) {
			if (dest == null || dest.texArray == null) return 1f;
			var sch = PaintUndo_MGR.instance != null ? PaintUndo_MGR.instance.UndoScheduler : null;
			if (sch != null)
				return sch.GetSmudgeKernelSpacingMultiplier(dest.width, dest.height, dest.UdimsCount);
			PaintUndo_Scheduler.EvaluateWorkload(dest.width, dest.height, dest.UdimsCount, ReferencePixelsPerSlice,
				out _, out float complexity01, out _);
			return Mathf.Max(0.25f, 1f + 0.35f * complexity01);
		}

		static Plan MeshAccumulationPlan(RenderUdims meshAccumulation) {
			var plan = new Plan {
				Dest = meshAccumulation,
				Underlay = null,
				UndoKind = PaintUndoNonStackTarget.MeshAccumulation,
				Domain = WriteDomain.MeshAccumulation,
				KernelSpacingMultiplier = ComputeKernelSpacingMultiplier(meshAccumulation)
			};
			return plan;
		}

		/// <param name="prebuiltMultiLayerUnderlay">From <see cref="Inpaint_MaskPainter"/> multi-layer under pass; may be null.</param>
		public static Plan Build(
			RenderUdims layerPaintTarget,
			PaintLayerStack_MGR stack,
			RenderUdims meshAccumulation,
			RenderUdims artIconUvWrapper,
			RenderUdims prebuiltMultiLayerUnderlay,
			SmudgeWriteTargetPreference writePreference) {

			var plan = new Plan {
				Dest = null,
				Underlay = null,
				UndoKind = PaintUndoNonStackTarget.InpaintColor,
				Domain = WriteDomain.None,
				KernelSpacingMultiplier = 1f
			};

			if (layerPaintTarget == null || layerPaintTarget.texArray == null)
				return plan;

			bool layerGate = LayerSmudgeGateOpen(stack, layerPaintTarget);
			bool meshOk = meshAccumulation != null && SameShape(layerPaintTarget, meshAccumulation);
			bool hasLayerStack = stack != null && stack.Layers != null && stack.Layers.Count > 0;
			var effectiveWritePreference = writePreference;

			// Layer-stack fence: once a layer stack exists, smudge writes are confined to ActiveLayer.Content.
			// If gate is closed (e.g. transient new-layer setup), return no-op so later frames can retry safely.
			if (hasLayerStack) {
				if (!layerGate)
					return plan;
				if (effectiveWritePreference == SmudgeWriteTargetPreference.GeneratedMesh)
					effectiveWritePreference = SmudgeWriteTargetPreference.LayerStack;
			}

			if (effectiveWritePreference == SmudgeWriteTargetPreference.GeneratedMesh) {
				if (meshOk)
					return MeshAccumulationPlan(meshAccumulation);
				// Explicit mesh target but accumulation does not match paint resolution: do not fall through to layer/art (misleading write).
				return plan;
			}

			if (layerGate && (effectiveWritePreference == SmudgeWriteTargetPreference.LayerStack
			                  || effectiveWritePreference == SmudgeWriteTargetPreference.Auto)) {
				plan.Dest = layerPaintTarget;
				plan.UndoKind = PaintUndoNonStackTarget.InpaintColor;
				plan.Domain = WriteDomain.LayerStack;
				plan.KernelSpacingMultiplier = ComputeKernelSpacingMultiplier(layerPaintTarget);

				bool multi = stack.Layers != null && stack.Layers.Count > 1;
				if (multi) {
					if (prebuiltMultiLayerUnderlay != null && UnderlayBarrierOk(plan.Dest, prebuiltMultiLayerUnderlay))
						plan.Underlay = prebuiltMultiLayerUnderlay;
				} else if (meshAccumulation != null && SameShape(layerPaintTarget, meshAccumulation)
				           && UnderlayBarrierOk(plan.Dest, meshAccumulation)) {
					plan.Underlay = meshAccumulation;
				}
				return plan;
			}

			// --- Isolation: not allowed to treat as layer-stack smudge → mesh accumulation or Art UV only ---

			if (meshAccumulation != null && SameShape(layerPaintTarget, meshAccumulation)) {
				return MeshAccumulationPlan(meshAccumulation);
			}

			if (artIconUvWrapper != null && SameShape(layerPaintTarget, artIconUvWrapper)) {
				plan.Dest = artIconUvWrapper;
				plan.Underlay = null;
				plan.UndoKind = PaintUndoNonStackTarget.ArtIconUvColor;
				plan.Domain = WriteDomain.ArtIconUvTexture;
				plan.KernelSpacingMultiplier = ComputeKernelSpacingMultiplier(artIconUvWrapper);
				return plan;
			}

			// Hidden active (or no gate) and no mesh/art match: do not write hidden layer UVs (avoids “ghost” smudge into invisible stack).
			var active = stack?.ActiveLayer;
			if (active != null && !active.Visible) {
				plan.Domain = WriteDomain.None;
				return plan;
			}

			plan.Dest = layerPaintTarget;
			plan.Underlay = null;
			plan.UndoKind = PaintUndoNonStackTarget.InpaintColor;
			plan.Domain = WriteDomain.LayerOnlyNoUnderlay;
			plan.KernelSpacingMultiplier = ComputeKernelSpacingMultiplier(layerPaintTarget);
			if (meshAccumulation != null && SameShape(layerPaintTarget, meshAccumulation)
			    && UnderlayBarrierOk(plan.Dest, meshAccumulation))
				plan.Underlay = meshAccumulation;

			return plan;
		}
	}
}
