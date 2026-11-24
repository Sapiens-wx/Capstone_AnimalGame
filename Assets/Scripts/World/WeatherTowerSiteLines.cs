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

        public void Door(Vector2 center, float width, bool open, float offset, float alpha = 1f)
        {
            Vector2 half = Vector2.up * (.5f * width);
            if (open)
            {
                Color yellow = accent;
                yellow.a *= alpha;
                Dashes(center - half, center + half, .064f, .23f, .12f, offset, yellow);
                return;
            }

            Vector2 pair = Vector2.right * .07f;
            var white = new Color(1f, 1f, 1f, alpha);
            Strip(center - half - pair, center + half - pair, .037f, white);
            Strip(center - half + pair, center + half + pair, .037f, white);
        }

        public void LiftControl(Vector2 center, float dim)
        {
            Vector2 half = Vector2.right * .22f;
            Color color = Color.Lerp(accent, Color.black, dim);
            Strip(center - half, center + half, .098f, color);
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
