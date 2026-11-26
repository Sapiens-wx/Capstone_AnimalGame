using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.World
{
    internal sealed class WeatherTowerSiteLines
    {
        private static readonly Color Gold = new Color32(0xd4, 0xb8, 0x54, 0xff);
        private readonly List<Vector3> vertices = new(640);
        private readonly List<Color> colors = new(640);
        private readonly List<int> triangles = new(960);
        private readonly Color accent = QualitySettings.activeColorSpace == ColorSpace.Linear ? Gold.linear : Gold;
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;

        public WeatherTowerSiteLines(Transform parent, Material material, string name, int sortingOrder)
        {
            var overlay = new GameObject(name);
            overlay.transform.SetParent(parent, false);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = overlay.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
        }

        public void Begin()
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
        }

        public void UpperOutline(Vector2[] loop, float alpha, float offset)
        {
            var color = new Color(1f, 1f, 1f, alpha);
            for (int index = 0; index < loop.Length; index++)
            {
                Vector2 start = loop[index];
                Vector2 end = loop[(index + 1) % loop.Length];
                Dashes(start, end, .043f, .31f, .15f, offset, color);
                offset += Vector2.Distance(start, end);
            }
        }

        public void Door(Vector2 center, float width, float openness, float offset, float alpha = 1f)
        {
            Vector2 half = Vector2.up * (.5f * width);
            Vector2 opening = half * openness;
            float panelLength = .5f * width * (1f - openness);
            if (openness < 1f)
            {
                Vector2 rail = Vector2.right * .07f;
                var white = new Color(1f, 1f, 1f, alpha);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 jamb = center + half * side;
                    Vector2 edge = center + opening * side;
                    Strip(jamb - rail, edge - rail, .037f, white);
                    Strip(jamb + rail, edge + rail, .037f, white);
                    float lipWidth = Mathf.Min(.037f, panelLength);
                    Vector2 lip = edge + Vector2.up * (side * lipWidth * .5f);
                    Strip(lip - rail, lip + rail, lipWidth, white);
                }
            }
            if (openness > 0f)
            {
                Color yellow = accent;
                yellow.a *= alpha;
                Dashes(center - opening, center + opening, .064f, .23f, .12f, offset + panelLength, yellow);
            }
        }

        public void SocketIndicator(Vector2 center, float dim)
        {
            Vector2 half = Vector2.right * .22f;
            Strip(center - half, center + half, .098f, Color.Lerp(accent, Color.black, dim));
        }

        public void ReminderRing(Vector2 center, float radius, float width, float opacity)
        {
            const int segments = 40;
            Color color = accent;
            color.a *= opacity;
            int first = vertices.Count;
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment * (2f * Mathf.PI / segments);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertices.Add(center + direction * (radius - width * .5f));
                vertices.Add(center + direction * (radius + width * .5f));
                colors.Add(color);
                colors.Add(color);

                int current = first + 2 * segment;
                int next = first + 2 * ((segment + 1) % segments);
                triangles.Add(current);
                triangles.Add(current + 1);
                triangles.Add(next + 1);
                triangles.Add(current);
                triangles.Add(next + 1);
                triangles.Add(next);
            }
        }

        private void Dashes(Vector2 start, Vector2 end, float width, float dash, float gap,
            float offset, Color color)
        {
            Vector2 span = end - start;
            float length = span.magnitude;
            Vector2 direction = span / length;
            float period = dash + gap;
            for (float position = -Mathf.Repeat(offset, period); position < length; position += period)
            {
                float first = Mathf.Max(0f, position);
                float last = Mathf.Min(length, position + dash);
                if (last > first)
                    Strip(start + direction * first, start + direction * last, width, color);
            }
        }

        private void Strip(Vector2 start, Vector2 end, float width, Color color)
        {
            Vector2 direction = (end - start).normalized;
            var edge = new Vector2(-direction.y, direction.x) * (.5f * width);
            int first = vertices.Count;
            vertices.Add(start - edge);
            vertices.Add(start + edge);
            vertices.Add(end + edge);
            vertices.Add(end - edge);
            for (int index = 0; index < 4; index++) colors.Add(color);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        public void Draw()
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            renderer.enabled = true;
        }

        public void Hide() => renderer.enabled = false;
        public void Dispose() => Object.Destroy(mesh);
    }
}
