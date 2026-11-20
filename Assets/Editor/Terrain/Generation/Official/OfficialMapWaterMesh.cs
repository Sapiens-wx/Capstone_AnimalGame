using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.Editor
{
    internal static class OfficialMapWaterMesh
    {
        private const float PreviewLift = .06f;

        internal static Mesh Build(Terrain terrain, OfficialMapWaterLayout layout)
        {
            return new TileBuilder(terrain, layout).Build();
        }

        private sealed class TileBuilder
        {
            private readonly TerrainData terrainData;
            private readonly Vector3 origin;
            private readonly Vector2 inverseSize;
            private readonly int side;
            private readonly int stride;
            private readonly int halfStepStride;
            private readonly ushort[] stages;
            private readonly int wetCount;
            private readonly Dictionary<int, int> vertexIndices;
            private readonly List<Vector3> vertices;
            private readonly List<int> triangles;
            private readonly List<int> shoreline;

            internal TileBuilder(Terrain terrain, OfficialMapWaterLayout layout)
            {
                terrainData = terrain.terrainData;
                origin = terrain.transform.position;
                Vector3 size = terrainData.size;
                inverseSize = new Vector2(1f / size.x, 1f / size.z);
                side = Mathf.RoundToInt(size.x) / OfficialMapWaterLayout.Step;
                stride = side + 1;
                halfStepStride = side * 2 + 1;
                stages = new ushort[stride * stride];

                int originX = Mathf.RoundToInt(origin.x);
                int originZ = Mathf.RoundToInt(origin.z);
                for (int z = 0; z <= side; z++)
                for (int x = 0; x <= side; x++)
                {
                    ushort stage = layout.ReadStage(originX + x * OfficialMapWaterLayout.Step,
                        originZ + z * OfficialMapWaterLayout.Step);
                    stages[z * stride + x] = stage;
                    if (stage != 0) wetCount++;
                }

                vertexIndices = new Dictionary<int, int>(wetCount);
                vertices = new List<Vector3>(wetCount);
                triangles = new List<int>(wetCount * 6);
                shoreline = new List<int>();
            }

            internal Mesh Build()
            {
                if (wetCount == 0) return null;

                for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int southwest = z * stride + x;
                    int southeast = southwest + 1;
                    int northwest = southwest + stride;
                    int northeast = northwest + 1;
                    if (stages[southwest] != 0 && stages[northeast] != 0
                        && stages[northwest] == 0 && stages[southeast] == 0)
                    {
                        // Join the dry diagonal in ambiguous cells so separate wet patches stay separate.
                        ClipTriangle(southwest, northwest, southeast);
                        ClipTriangle(northwest, northeast, southeast);
                    }
                    else
                    {
                        ClipTriangle(southwest, northwest, northeast);
                        ClipTriangle(southwest, northeast, southeast);
                    }
                }

                var mesh = new Mesh
                {
                    name = "Official Map water preview tile",
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
                    subMeshCount = 2
                };
                mesh.SetVertices(vertices);
                mesh.SetIndices(triangles, MeshTopology.Triangles, 0, false);
                mesh.SetIndices(shoreline, MeshTopology.Lines, 1, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }

            private void ClipTriangle(int a, int b, int c)
            {
                bool wetA = stages[a] != 0;
                bool wetB = stages[b] != 0;
                bool wetC = stages[c] != 0;
                if (!wetA && !wetB && !wetC) return;
                if (wetA && wetB && wetC)
                {
                    AddTriangle(Node(a), Node(b), Node(c));
                    return;
                }

                // Rotate the triangle so a is the sole wet or dry node, preserving its winding.
                bool loneWet = (wetA ? 1 : 0) + (wetB ? 1 : 0) + (wetC ? 1 : 0) == 1;
                if (wetA != loneWet)
                {
                    if (wetB == loneWet) (a, b, c) = (b, c, a);
                    else (a, b, c) = (c, a, b);
                }

                int ab = Boundary(a, b);
                int ca = Boundary(c, a);
                if (loneWet)
                    AddTriangle(Node(a), ab, ca);
                else
                {
                    AddTriangle(ab, Node(b), Node(c));
                    AddTriangle(ab, Node(c), ca);
                }

                // Wet-to-dry intersections are shores. Shared tile edges and triangulation edges are not drawn.
                shoreline.Add(ab);
                shoreline.Add(ca);
            }

            private void AddTriangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            private int Node(int node) => Vertex(2 * (node % stride), 2 * (node / stride), stages[node]);

            private int Boundary(int a, int b)
            {
                return Vertex(a % stride + b % stride, a / stride + b / stride,
                    stages[a] != 0 ? stages[a] : stages[b]);
            }

            private int Vertex(int xHalfSteps, int zHalfSteps, ushort stage)
            {
                int key = zHalfSteps * halfStepStride + xHalfSteps;
                if (vertexIndices.TryGetValue(key, out int index)) return index;

                float localX = .5f * OfficialMapWaterLayout.Step * xHalfSteps;
                float localZ = .5f * OfficialMapWaterLayout.Step * zHalfSteps;
                float ground = origin.y + terrainData.GetInterpolatedHeight(localX * inverseSize.x, localZ * inverseSize.y);
                float y = Mathf.Max(stage * .01f, ground) + PreviewLift;
                index = vertices.Count;
                vertices.Add(new Vector3(origin.x + localX, y, origin.z + localZ));
                vertexIndices.Add(key, index);
                return index;
            }
        }
    }
}
