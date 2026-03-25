namespace spz {

	/// <summary>When <see cref="PaintUndo_SnapshotRecord.LayerCount"/> is 0, identifies which GPU buffer the snapshot belongs to (restore must not guess).</summary>
	public enum PaintUndoNonStackTarget : int {
		InpaintColor = 0,
		BackgroundGenMask = 1,
		ProjectionGenMask = 2,
	}
}
