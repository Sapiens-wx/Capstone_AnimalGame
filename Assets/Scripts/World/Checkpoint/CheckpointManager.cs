using System;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimalGame.World.Checkpoints
{
    public sealed class CheckpointManager : Singleton<CheckpointManager>
    {
        [SerializeField] private Shader fogShader;
        [SerializeField, Range(128, 4096)] private int fogResolution = 1024;
        [SerializeField, Min(0.01f)] private float transitionWidthMeters = 8f;
        [SerializeField, Min(0.01f)] private float noiseScaleMeters = 24f;
        [SerializeField, Min(0f)] private float noiseAmplitudeMeters = 4f;

        private Checkpoint[] checkpoints;
        private Vector2[] positions;
        private Vector4[] seeds;
        private readonly List<WorldInteraction> overlaps = new();
        private MapTestSceneController map;
        private ComputeBuffer seedBuffer;
        private Material fogMaterial;
        private RenderTexture fogTexture;
        public IReadOnlyList<Checkpoint> Checkpoints => checkpoints;
        public int Revision { get; private set; }
        internal bool HasSurveyData => Array.Exists(checkpoints, checkpoint => checkpoint.Visited);

        protected override void Awake()
        {
            base.Awake();
            checkpoints = GetComponentsInChildren<Checkpoint>(true);
            positions = new Vector2[checkpoints.Length];
            seeds = new Vector4[Mathf.Max(1, checkpoints.Length)];
            var stationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Checkpoint checkpoint in checkpoints)
            {
                if (checkpoint.IsStationBound && !stationIds.Add(checkpoint.StationId))
                    throw new InvalidOperationException($"Station {checkpoint.StationId} has more than one map region.");
                checkpoint.Owner = this;
                checkpoint.ResetVisit();
            }
            SaveSystem.Changed += RefreshFog;
        }

        private void LateUpdate()
        {
            var player = HeightMapPlayerSceneBootstrap.inst;
            if (player == null || player.gameObject.scene != gameObject.scene || player.map == null
                || !player.map.HasGeneratedMap || player.robot == null) return;
            EnsureFog(player.map);
            if (Time.timeScale <= 0f || !Array.Exists(checkpoints, CanDiscoverOnContact)) return;
            WorldInteractionQuery.Query(player.robot.GetCollisionShape(map), WorldInteractionKind.Checkpoint,
                map, gameObject.scene, overlaps);
            bool changed = false;
            foreach (WorldInteraction interaction in overlaps)
            {
                Checkpoint checkpoint = interaction.GetComponent<Checkpoint>();
                if (checkpoint != null && checkpoint.Owner == this)
                    changed |= checkpoint.TryVisitOnContact();
            }
            if (changed) RefreshFog();
        }

        private static bool CanDiscoverOnContact(Checkpoint checkpoint) => !checkpoint.IsStationBound && !checkpoint.Visited;

        public RenderTexture GetFog(MapTestSceneController sceneMap)
        {
            EnsureFog(sceneMap);
            return fogTexture;
        }

        public bool TryGetStationRegion(MapTestSceneController sceneMap, string stationId, out Vector2 center, out Rect bounds)
        {
            int index = FindStationIndex(stationId);
            if (index < 0)
            {
                center = default;
                bounds = default;
                return false;
            }

            EnsureFog(sceneMap);
            center = positions[index];
            Vector2 size = map.MapSizeMeters;
            bounds = new Rect(Vector2.zero, size);
            if (positions.Length == 1) return true;

            var polygon = new List<Vector2> { Vector2.zero, new Vector2(size.x, 0f), size, new Vector2(0f, size.y) };
            var clipped = new List<Vector2>();
            // The exact cell is the intersection of the map and the half-planes closer to this seed.
            // The fog shader softens its boundary independently.
            for (int i = 0; i < positions.Length && polygon.Count > 0; i++)
            {
                if (i == index) continue;
                ClipRegion(polygon, clipped, (center + positions[i]) * .5f, positions[i] - center);
                (polygon, clipped) = (clipped, polygon);
                clipped.Clear();
            }
            if (polygon.Count == 0)
            {
                bounds = default;
                return false;
            }

            Vector2 min = polygon[0], max = polygon[0];
            foreach (Vector2 point in polygon)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private static void ClipRegion(List<Vector2> polygon, List<Vector2> clipped, Vector2 midpoint, Vector2 normal)
        {
            Vector2 from = polygon[^1];
            float fromDistance = Vector2.Dot(from - midpoint, normal);
            foreach (Vector2 to in polygon)
            {
                float toDistance = Vector2.Dot(to - midpoint, normal);
                bool fromInside = fromDistance <= 0f;
                bool toInside = toDistance <= 0f;
                if (fromInside != toInside)
                    clipped.Add(Vector2.LerpUnclamped(from, to, fromDistance / (fromDistance - toDistance)));
                if (toInside) clipped.Add(to);
                from = to;
                fromDistance = toDistance;
            }
        }

        // The caller owns this temporary snapshot and releases it with RenderTexture.ReleaseTemporary.
        public RenderTexture CaptureFog(MapTestSceneController sceneMap)
        {
            RenderTexture source = GetFog(sceneMap);
            var snapshot = RenderTexture.GetTemporary(source.descriptor);
            snapshot.filterMode = source.filterMode;
            snapshot.wrapMode = source.wrapMode;
            try
            {
                RenderTexture previous = RenderTexture.active;
                try { Graphics.Blit(source, snapshot); }
                finally { RenderTexture.active = previous; }
                return snapshot;
            }
            catch
            {
                RenderTexture.ReleaseTemporary(snapshot);
                throw;
            }
        }

        private int FindStationIndex(string stationId)
        {
            if (string.IsNullOrWhiteSpace(stationId)) return -1;
            return Array.FindIndex(checkpoints, checkpoint => checkpoint.StationId == stationId);
        }

        private void EnsureFog(MapTestSceneController sceneMap)
        {
            if (fogTexture != null) return;
            map = sceneMap;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                positions[i] = InteractionShape.ToQuery(checkpoints[i].transform.position, map);
                seeds[i] = new Vector4(positions[i].x, positions[i].y, 0, 0);
            }
            seedBuffer = new ComputeBuffer(seeds.Length, sizeof(float) * 4);
            fogMaterial = new Material(fogShader) { hideFlags = HideFlags.HideAndDontSave };
            fogMaterial.SetBuffer("_Checkpoints", seedBuffer);
            fogMaterial.SetInt("_CheckpointCount", checkpoints.Length);
            fogMaterial.SetVector("_MapSize", map.MapSizeMeters);
            fogMaterial.SetFloat("_TransitionWidth", transitionWidthMeters);
            fogMaterial.SetFloat("_NoiseScale", noiseScaleMeters);
            fogMaterial.SetFloat("_NoiseAmplitude", noiseAmplitudeMeters);
            Vector2 size = map.MapSizeMeters;
            float scale = fogResolution / Mathf.Max(size.x, size.y);
            fogTexture = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(size.x * scale)),
                Mathf.Max(1, Mathf.RoundToInt(size.y * scale)), 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
            {
                name = "Checkpoint discovery fog", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            fogTexture.Create();
            RefreshSeeds();
            RenderFog();
        }

        private void RefreshFog()
        {
            if (fogTexture == null || !RefreshSeeds()) return;
            RenderFog();
        }

        private bool RefreshSeeds()
        {
            bool changed = false;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                float visible = checkpoints[i].Visited ? 1f : 0f;
                changed |= seeds[i].z != visible;
                seeds[i].z = visible;
            }
            return changed;
        }

        private void RenderFog()
        {
            seedBuffer.SetData(seeds);
            RenderTexture previous = RenderTexture.active;
            try { Graphics.Blit(Texture2D.blackTexture, fogTexture, fogMaterial); }
            finally { RenderTexture.active = previous; }
            Revision++;
        }

        private void OnDestroy()
        {
            SaveSystem.Changed -= RefreshFog;
            seedBuffer?.Release();
            seedBuffer = null;
            if (fogTexture != null) { fogTexture.Release(); CoreUtils.Destroy(fogTexture); fogTexture = null; }
            if (fogMaterial != null) { CoreUtils.Destroy(fogMaterial); fogMaterial = null; }
            if (inst == this) inst = null;
        }
    }
}
