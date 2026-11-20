using System.Collections;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.World;
using Clipper2Lib;
using UnityEngine;

namespace AnimalGame.Animals
{
    public sealed class AnimalSpawner : MonoBehaviour
    {
        internal const float OuterRadiusMultiplier = 5f;
        private const int Scale = 10000;
        [SerializeField, Min(0)] private int maximumAnimals = 30;

        private sealed class Candidate
        {
            public AnimalSpawnInfo Info;
            public Paths64 Region, Triangles;
            public readonly List<Vegetation> Plants = new();
            public double Area;
            public float Probability;
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(3f + Random.value);
            var interval = new WaitForSeconds(3f);
            while (true)
            {
                var level = HeightMapPlayerSceneBootstrap.inst;
                if (level != null && level.InitialViewRadiusMeters > 0f && level.map.HasGeneratedMap
                    && !level.map.HasPendingHeightLoads && level.map.LevelAsset.AnimalSpawnRegions != null)
                    Check(level.map, InteractionShape.ToQuery(level.robot.transform.position, level.map), level.InitialViewRadiusMeters);
                yield return interval;
            }
        }

        public void Check(MapTestSceneController map, Vector2 center, float radius)
        {
            int count = 0;
            foreach (var animal in AnimalAgent.ActiveAgents)
                if (animal.gameObject.scene == gameObject.scene) count++;
            if (count >= maximumAnimals) return;

            float outer = radius * OuterRadiusMultiplier;
            var origin = ToPoint(center);
            const int sides = 128;
            // Circumscribe the exclusion circle so every sampled point is at least 2R away.
            var hole = Clipper.Ellipse(origin, radius * 2 * Scale / Mathf.Cos(Mathf.PI / sides), 0, sides);
            hole.Reverse();
            var ring = new Paths64 { Clipper.Ellipse(origin, outer * Scale, 0, sides), hole };
            var candidates = new List<Candidate>();
            List<WorldInteraction> vegetation = null;
            foreach (var species in map.LevelAsset.AnimalSpawnRegions.Regions)
            {
                var info = AnimalInfoLibrary.Active.BySpecies[species.species].spawnInfo;
                if (info == null || info.Prefab == null || info.BaseSpawnProbability <= 0f) continue;
                var paths = new Paths64();
                foreach (var polygon in species.polygons)
                {
                    paths.Add(Path(polygon.vertices, false));
                    foreach (var h in polygon.holes) paths.Add(Path(h.vertices, true));
                }
                var region = Clipper.Intersect(paths, ring, FillRule.NonZero);
                if (region.Count == 0) continue;
                var candidate = new Candidate { Info = info, Region = region, Probability = info.BaseSpawnProbability };
                if (info.BoundVegetation != VegetationKind.None)
                {
                    if (vegetation == null)
                    {
                        vegetation = new List<WorldInteraction>();
                        WorldInteractionQuery.Query(InteractionShape.Circle(center, outer), WorldInteractionKind.Vegetation,
                            map, gameObject.scene, vegetation);
                    }
                    foreach (var item in vegetation)
                        if (item is Vegetation plant && plant.vegetationKind == info.BoundVegetation)
                        {
                            var shape = plant.GetShape(map);
                            if (Contains(region, shape.Center)) candidate.Plants.Add(plant);
                        }
                    if (candidate.Plants.Count == 0) continue;
                }
                else
                {
                    if (Clipper.Triangulate(region, out candidate.Triangles) != TriangulateResult.success) continue;
                    foreach (var t in candidate.Triangles) candidate.Area += System.Math.Abs(Clipper.Area(t));
                    if (candidate.Area == 0) continue;
                }
                candidates.Add(candidate);
            }
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            bool remaining;
            do
            {
                remaining = false;
                foreach (var c in candidates)
                {
                    if (c.Probability <= 0f) continue;
                    if ((c.Probability >= 1f || Random.value < c.Probability) && TrySpawn(c, map))
                        if (++count >= maximumAnimals) return;
                    float next = c.Probability - c.Info.ProbabilityReduction;
                    c.Probability = next < c.Probability ? next : 0f;
                    remaining |= c.Probability > 0f;
                }
            } while (remaining);
        }

        private static bool TrySpawn(Candidate c, MapTestSceneController map)
        {
            Vector2 point;
            Vegetation vegetation = null;
            if (c.Plants.Count > 0)
            {
                vegetation = c.Plants[Random.Range(0, c.Plants.Count)];
                var plant = vegetation.GetShape(map);
                float angle = Random.value * Mathf.PI * 2f;
                point = plant.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                    * (plant.Radius + c.Info.Prefab.Config.BodyRadiusMeters + .01f);
                if (!Contains(c.Region, point)) return false;
            }
            else
            {
                double pick = Random.value * c.Area;
                int i = 0;
                for (; i < c.Triangles.Count - 1; i++)
                    if ((pick -= System.Math.Abs(Clipper.Area(c.Triangles[i]))) <= 0) break;
                var t = c.Triangles[i];
                float u = Mathf.Sqrt(Random.value);
                point = Vector2.Lerp(InMeters(t[0]), Vector2.Lerp(InMeters(t[1]), InMeters(t[2]), Random.value), u);
            }
            var animal = Instantiate(c.Info.Prefab, map.MapPositionToWorld(point), c.Info.Prefab.transform.rotation, map.transform);
            if (vegetation != null && animal.Behaviour is TreeBirdBehaviour bird)
                bird.BindBirthTree(vegetation.GetComponentInParent<TreeHabitat>(), false);
            if (animal.TryInitialize(spawnedAtRuntime: true)) return true;
            animal.gameObject.SetActive(false);
            Destroy(animal.gameObject);
            return false;
        }

        private static Point64 ToPoint(Vector2 p) => new Point64((double)p.x * Scale, (double)p.y * Scale);
        private static Vector2 InMeters(Point64 p) => new Vector2(p.X, p.Y) / Scale;
        private static Path64 Path(Vector2[] points, bool hole)
        {
            var path = new Path64(points.Length);
            foreach (var p in points) path.Add(ToPoint(p));
            if (Clipper.IsPositive(path) == hole) path.Reverse();
            return path;
        }
        private static bool Contains(Paths64 region, Vector2 position)
        {
            var point = ToPoint(position);
            bool inside = false;
            foreach (var path in region)
            {
                var result = Clipper.PointInPolygon(point, path);
                if (result == PointInPolygonResult.IsOn) return true;
                if (result == PointInPolygonResult.IsInside) inside = !inside;
            }
            return inside;
        }
    }
}
