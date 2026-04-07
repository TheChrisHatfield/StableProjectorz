namespace spz {

	/// <summary>When <see cref="PaintUndo_SnapshotRecord.LayerCount"/> is 0, identifies which GPU buffer the snapshot belongs to (restore must not guess).</summary>
	public enum PaintUndoNonStackTarget : int {
		InpaintColor = 0,
		BackgroundGenMask = 1,
		ProjectionGenMask = 2,
		/// <summary>Mesh UV accumulation (projections / SD blit target) when smudging with no visible paint layers.</summary>
		MeshAccumulation = 3,
		/// <summary>Main art icon’s UV color texture array when accumulation does not match brush resolution.</summary>
		ArtIconUvColor = 4,
	}
}
