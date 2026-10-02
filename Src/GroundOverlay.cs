using System.Collections.Generic;
using UnityEngine;

namespace com.github.lhervier.ksp.terrainprecisionfixdiag5
{
    /// <summary>
    /// The drawing of the collider of one terrain quad: the outline of each cell of its grid. Stock gives
    /// the collider the quad's own mesh, so the drawing is built from the quad's own vertices, in the frame
    /// of the quad, and lands where the collider is, wherever the quad is moved to.
    /// </summary>
    internal sealed class GroundOverlay
    {
        // Scratch buffer shared by every rebuild, which all happen on the main thread.
        private static readonly List<int> INDICES = new List<int>();
        private static Color32[] white = new Color32[0];

        /// <summary>The quad this overlay draws.</summary>
        public PQ Quad { get; }

        private readonly Mesh mesh;

        // What the drawing was built from. Stock keeps a quad object alive and builds it again elsewhere
        // when the terrain changes, so the same quad can come back with other vertices.
        private Vector3[] builtVerts;
        private Vector3d builtPosition;
        private Vector3 builtFirstVertex;
        private Vector3 builtLastVertex;

        /// <summary>Creates the overlay of <paramref name="quad"/>, with nothing drawn yet.</summary>
        public GroundOverlay(PQ quad)
        {
            Quad = quad;
            mesh = new Mesh { name = "TerrainPrecisionFixDiag5 ground of " + quad.name };
            mesh.MarkDynamic();
        }

        /// <summary>
        /// Makes the drawing match the vertices of <see cref="Quad"/> right now. Does nothing when it
        /// already does.
        /// </summary>
        public void Refresh()
        {
            Vector3[] verts = Quad.verts;
            if (verts == null || verts.Length == 0)
            {
                return;
            }
            if (verts == builtVerts
                && Quad.positionPlanetRelative == builtPosition
                && verts[0].Equals(builtFirstVertex)
                && verts[verts.Length - 1].Equals(builtLastVertex))
            {
                return;
            }

            // The grid is square: vertex (x, z) is at z * side + x.
            int side = Mathf.RoundToInt(Mathf.Sqrt(verts.Length));
            INDICES.Clear();
            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int vertex = z * side + x;
                    if (x + 1 < side)
                    {
                        INDICES.Add(vertex);
                        INDICES.Add(vertex + 1);
                    }
                    if (z + 1 < side)
                    {
                        INDICES.Add(vertex);
                        INDICES.Add(vertex + side);
                    }
                }
            }

            if (white.Length != verts.Length)
            {
                white = new Color32[verts.Length];
                for (int i = 0; i < white.Length; i++)
                {
                    white[i] = new Color32(255, 255, 255, 255);
                }
            }
            mesh.Clear();
            mesh.vertices = verts;
            // The colour comes from the material; the shader multiplies it by this one.
            mesh.colors32 = white;
            mesh.SetIndices(INDICES.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            builtVerts = verts;
            builtPosition = Quad.positionPlanetRelative;
            builtFirstVertex = verts[0];
            builtLastVertex = verts[verts.Length - 1];
        }

        /// <summary>Queues the drawing for this frame, where the physics has the quad's collider now.</summary>
        public void Draw(Material lines, int layer)
        {
            if (Quad == null || builtVerts == null)
            {
                return;
            }
            MeshCollider collider = Quad.meshCollider;
            Matrix4x4 matrix = collider != null && collider.sharedMesh != null
                ? ColliderOverlay.PhysicsMatrix(collider, collider.sharedMesh.bounds.center)
                : Quad.transform.localToWorldMatrix;
            Graphics.DrawMesh(mesh, matrix, lines, layer);
        }

        /// <summary>Releases the mesh. The overlay cannot be drawn afterwards.</summary>
        public void Destroy()
        {
            Object.Destroy(mesh);
        }
    }
}
