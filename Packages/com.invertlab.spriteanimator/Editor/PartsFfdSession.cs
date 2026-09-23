using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>
    /// The Warp FFD grid, kept in its own object so Undo / Redo bring the grid back together with the
    /// deform key it bent (Start, each drag, Reset, Apply are all undo steps).
    /// Points are in the part's lattice space (x, y in -0.5..0.5 over the image), row-major from bottom-left.
    /// </summary>
    internal sealed class PartsFfdSession : ScriptableObject
    {
        public bool On;
        /// <summary>True: Edit Mesh FFD on the setup mesh (image space 0..1). False: Warp FFD on the pose.</summary>
        public bool Mesh;
        public string SlotId;
        public float Time;
        public int Cols = 3;
        public int Rows = 3;
        public Vector2[] Rest;
        public Vector2[] Ctrl;
        public int[] Verts;
        /// <summary>Where each vertex sits in the grid (0..1 across, 0..1 up).</summary>
        public Vector2[] Param;
    }
}
