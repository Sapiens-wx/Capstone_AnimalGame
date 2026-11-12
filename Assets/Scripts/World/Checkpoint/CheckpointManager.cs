using System.Collections.Generic;
using AnimalGame.MapTest;
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
        private int visitedCount;
        public IReadOnlyList<Checkpoint> Checkpoints => checkpoints;
        public IReadOnlyList<Vector2> Positions => positions;
        public int Revision { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            checkpoints = GetComponentsInChildren<Checkpoint>(true);
            positions = new Vector2[checkpoints.Length];
            seeds = new Vector4[Mathf.Max(1, checkpoints.Length)];
            foreach (Checkpoint checkpoint in checkpoints)
            {
                checkpoint.Owner = this;
                checkpoint.ResetVisit();
            }
        }

        private void LateUpdate()
        {
            var player = HeightMapPlayerSceneBootstrap.inst;
            if (player == null || player.gameObject.scene != gameObject.scene || player.map == null
                || !player.map.HasGeneratedMap || player.robot == null) return;
            EnsureFog(player.map);
            if (Time.timeScale <= 0f || visitedCount == checkpoints.Length) return;
            WorldInteractionQuery.Query(player.robot.GetCollisionShape(map), WorldInteractionKind.Checkpoint,
                map, gameObject.scene, overlaps);
            bool changed = false;
            foreach (WorldInteraction interaction in overlaps)
            {
                Checkpoint checkpoint = interaction.GetComponent<Checkpoint>();
                if (checkpoint == null || checkpoint.Owner != this || checkpoint.Visited) continue;
                checkpoint.Visit();
                visitedCount++;
                changed = true;
            }
            if (changed) RenderFog();
        }

        public RenderTexture GetFog(MapTestSceneController sceneMap)
        {
            EnsureFog(sceneMap);
            return fogTexture;
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
            RenderFog();
        }

        private void RenderFog()
        {
            for (int i = 0; i < checkpoints.Length; i++) seeds[i].z = checkpoints[i].Visited ? 1f : 0f;
            seedBuffer.SetData(seeds);
            RenderTexture previous = RenderTexture.active;
            try { Graphics.Blit(Texture2D.blackTexture, fogTexture, fogMaterial); }
            finally { RenderTexture.active = previous; }
            Revision++;
        }

        private void OnDestroy()
        {
            seedBuffer?.Release();
            seedBuffer = null;
            if (fogTexture != null) { fogTexture.Release(); CoreUtils.Destroy(fogTexture); fogTexture = null; }
            if (fogMaterial != null) { CoreUtils.Destroy(fogMaterial); fogMaterial = null; }
            if (inst == this) inst = null;
        }
    }
}
