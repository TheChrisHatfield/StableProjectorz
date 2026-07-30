using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace spz {

	// =============================================================================
	// LAYER SYSTEM - DATA MODEL (single layer)
	// =============================================================================
	// This file defines one paint layer. The manager is PaintLayerStack_MGR, which
	// holds a list of PaintLayer instances (index 0 = bottom). Inpaint_MaskPainter
	// injects scene into Content and uses each layer's Content for display blits.
	// The layer list UI is in PaintTab_LayersPanel_UI; it only calls the stack.
	// =============================================================================

	/// <summary>
	/// Layer is a container: it holds scene + paint. Scene data is injected into Content by the painter
	/// when the layer is empty or when switching to it. Compute shader writes strokes directly into
	/// Content (the display buffer) so there is no timing gap — GPU command serialization guarantees
	/// strokes are visible on the same frame. Data exists for save/load round-trips but is not part
	/// of the live paint path.
	/// </summary>
	public class PaintLayer
	{
		public string Name { get; set; }
		/// <summary>Whether this layer is shown in the viewport. Layer data is unchanged when toggled. </summary>
		public bool Visible { get; set; } = true;
		public float Opacity { get; set; } = 1f;
		public PaintLayerBlendMode BlendMode { get; set; } = PaintLayerBlendMode.Normal;

		/// <summary>Container: scene + paint (UV-space). Scene is injected here by the painter when the layer is empty. Owned by this layer; Dispose when removing layer. </summary>
		public RenderUdims Content { get; private set; }

		/// <summary>Secondary buffer for save/load round-trips. Not used during live painting (strokes go directly into Content). </summary>
		public RenderUdims Data { get; private set; }

		/// <summary>Per-layer mask for strokes painted in <see cref="WorkflowRibbon_CurrMode.Inpaint_NoColor"/> mode.
		/// Kept separate from <see cref="Content"/> so colorless strokes never pollute color RGB and switching modes
		/// in the ribbon doesn't reveal "secret color" or hide colorless strokes. Lazily allocated by
		/// <see cref="EnsureNoColorMaskMatchesContent"/> on the first No-Color stroke; same shape/format as Content.
		/// Composite blits this on top of Content with <c>_isColorlessMask = 1</c> so it always renders as the checker pattern.</summary>
		public RenderUdims NoColorMask { get; private set; }

		/// <summary>True after the painter has injected static scene into this layer once. Prevents overwriting user paint; ensures we only inject when layer is still an empty vessel.</summary>
		public bool HasReceivedSceneInject { get; set; }

		public PaintLayer(string name)
		{
			Name = name ?? "Layer";
		}

		// --- Allocation / resolution (called by PaintLayerStack_MGR when resolution is set) ---
		/// <summary>Allocate content and data with same layout as inpaint brush (UDIMs, resolution, format). Call when stack resolution is known. New/rezised content needs scene injection again.</summary>
		/// <returns>True if <see cref="Content"/> was (re)allocated; false if existing buffers already matched and were left unchanged.</returns>
		public bool EnsureContent(IReadOnlyList<UDIM_Sector> udims, Vector2Int resolution, GraphicsFormat format, FilterMode filter)
		{
			if (Content != null)
			{
				if (Content.width == resolution.x && Content.height == resolution.y && Content.UdimsCount == udims.Count)
					return false;
				Content.Dispose();
				Data?.Dispose();
				Data = null;
				NoColorMask?.Dispose();
				NoColorMask = null;
			}
			Content = new RenderUdims(udims, resolution, format, filter, Color.clear, depthBits: 0);
			Data = new RenderUdims(udims, resolution, format, filter, Color.clear, depthBits: 0);
			HasReceivedSceneInject = false; // new/rezised vessel; painter will inject static scene once
			return true;
		}

		/// <summary>Lazily allocate <see cref="NoColorMask"/> with the same shape as <see cref="Content"/>. No-op if already matches. Caller should ensure Content exists first (use <see cref="PaintLayerStack_MGR.EnsureContentForLayerIfNeeded"/>).</summary>
		public void EnsureNoColorMaskMatchesContent()
		{
			if (Content == null) return;
			if (NoColorMask != null
			    && NoColorMask.width == Content.width
			    && NoColorMask.height == Content.height
			    && NoColorMask.UdimsCount == Content.UdimsCount)
				return;
			NoColorMask?.Dispose();
			NoColorMask = new RenderUdims(Content.udims_sectors, Content.widthHeight,
				Content.graphicsFormat, Content.filterMode, Color.clear, depthBits: 0);
		}

		// --- Save/load helpers (Content = live paint buffer; Data = mirror for serialization) ---
		/// <summary>Copy Content → Data so future strokes apply on top of current content. Call after the painter injects scene into Content (or after load). </summary>
		public void SyncDataFromContent()
		{
			if (Content == null) return;
			if (Data == null || Data.width != Content.width || Data.height != Content.height || Data.UdimsCount != Content.UdimsCount)
			{
				Data?.Dispose();
				Data = new RenderUdims(Content.udims_sectors, Content.widthHeight, Content.graphicsFormat, Content.filterMode, Color.clear, 0);
			}
			Graphics.CopyTexture(Content.texArray, Data.texArray);
		}

		/// <summary>Bake: copy Data → Content so display/composite shows the latest strokes. Call after writing into Data. </summary>
		public void Bake()
		{
			if (Data == null || Content == null || Data.width != Content.width || Data.height != Content.height || Data.UdimsCount != Content.UdimsCount) return;
			Graphics.CopyTexture(Data.texArray, Content.texArray);
		}

		/// <summary>Assign content loaded from project (used by PaintLayerStack_MGR.Load). Disposes existing content. Data is synced from Content. </summary>
		public void SetContentFromLoad(RenderUdims loadedContent)
		{
			NoColorMask?.Dispose();
			NoColorMask = null;
			Content?.Dispose();
			Data?.Dispose();
			Content = loadedContent;
			Data = null;
			SyncDataFromContent();
			HasReceivedSceneInject = true; // loaded content is the source of truth; do not overwrite with scene
		}

		/// <summary>Assign No-Color mask from disk; takes ownership of <paramref name="loaded"/> or disposes it on mismatch.</summary>
		public void SetNoColorMaskFromLoad(RenderUdims loaded)
		{
			NoColorMask?.Dispose();
			NoColorMask = null;
			if (loaded == null) return;
			if (Content == null)
			{
				loaded.Dispose();
				return;
			}
			if (loaded.width != Content.width || loaded.height != Content.height || loaded.UdimsCount != Content.UdimsCount)
			{
				Debug.LogWarning("[PaintLayer] NoColorMask size mismatch vs Content; discarding loaded mask.");
				loaded.Dispose();
				return;
			}
			NoColorMask = loaded;
		}

		public void Dispose()
		{
			Content?.Dispose();
			Content = null;
			Data?.Dispose();
			Data = null;
			NoColorMask?.Dispose();
			NoColorMask = null;
		}
	}

	public enum PaintLayerBlendMode
	{
		Normal,
		Multiply,
		Screen,
		Overlay
	}
}
