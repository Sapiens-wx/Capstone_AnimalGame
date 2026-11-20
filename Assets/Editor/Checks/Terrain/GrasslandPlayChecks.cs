using System;
using System.Collections.Generic;
using System.IO;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GrasslandPlayChecks
{
    private const string Key = "GrasslandPlayChecks.Running";
    private const string Report = "output/validation/grassland-dynamics/play-checks.txt";
    private static GrasslandTrampleController player;
    private static RobotMover mover;
    private static Vector3 centre;
    private static int phase, lastFrame;
    private static double started, phaseStarted;
    private static float pausedClock;
    private static float airborneStamp;
    private static RobotHeightMotionDetector heightMotion;
    private static MapTestSceneController runtimeMap;
    private static Material retiredMaterial;
    private static readonly List<float> staticFrames = new List<float>();
    private static readonly List<float> dynamicFrames = new List<float>();

    static GrasslandPlayChecks()
    {
        if (SessionState.GetBool(Key, false))
        {
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
    }

    [MenuItem("Animal Game/Checks/Grassland Play Smoke")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Start the grass smoke check from Edit mode.");
        SessionState.SetBool(Key, true);
        phase = 0; lastFrame = -1;
        staticFrames.Clear(); dynamicFrames.Clear();
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 60) throw new TimeoutException("Grassland play smoke exceeded 60 seconds.");
            if (phase == 0)
            {
                player = UnityEngine.Object.FindFirstObjectByType<GrasslandTrampleController>();
                if (player == null || now - started < 3) return;
                mover = player.GetComponent<RobotMover>();
                var map = UnityEngine.Object.FindFirstObjectByType<MapTestSceneController>();
                Require(map != null && map.HasGeneratedMap, "Runtime map not ready");
                bool bound = false;
                foreach (var renderer in map.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.name.StartsWith("Map Tile ") && renderer.sharedMaterial.HasProperty("_GrassEnabled")
                        && renderer.sharedMaterial.GetFloat("_GrassEnabled") > .5f) bound = true;
                Require(bound, "Runtime grass mask was not bound");
                centre = FindGrass(map);
                mover.Teleport(centre);
                mover.SetIgnoreCollisionAndTerrain(true);
                Time.timeScale = 1;
                phase = 1; phaseStarted = now;
                return;
            }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (phase == 1 && now - phaseStarted > 1)
            {
                Require(Shader.GetGlobalFloat("_GrassActive") > .5f, "Player did not activate grass");
                Require(Shader.GetGlobalVectorArray("_GrassFootprints").Length == 8, "History capacity changed");
                pausedClock = Time.time;
                Time.timeScale = 0; phase = 2; phaseStarted = now;
            }
            else if (phase == 2 && now - phaseStarted > .3)
            {
                Require(Mathf.Abs(Shader.GetGlobalFloat("_GrassNow") - pausedClock) < .0001f, "Grass clock advanced while paused");
                Time.timeScale = 1;
                player.enabled = false;
                Require(Shader.GetGlobalFloat("_GrassActive") == 0f, "Disabled player left grass active");
                phase = 3; phaseStarted = now;
            }
            else if (phase == 3 && now - phaseStarted > .5)
            {
                staticFrames.Add(Time.unscaledDeltaTime * 1000);
                if (staticFrames.Count >= 60)
                { player.enabled = true; phase = 4; phaseStarted = now; }
            }
            else if (phase == 4)
            {
                // Move the actual player footprint through the grass without changing saved scene data.
                mover.Teleport(centre + Vector3.right * (Mathf.Sin(Time.time * 2) * .25f));
                if (now - phaseStarted < .5) return;
                dynamicFrames.Add(Time.unscaledDeltaTime * 1000);
                if (dynamicFrames.Count >= 60)
                {
                    var history = Shader.GetGlobalVectorArray("_GrassFootprints");
                    Require(history[7].w > 0 && Time.time - history[7].z < .1f, "Live contact stopped updating");
                    foreach (var point in history) Require(!float.IsNaN(point.x) && !float.IsNaN(point.y), "Invalid footprint coordinate");
                    Directory.CreateDirectory("output/validation/grassland-dynamics");
                    ScreenCapture.CaptureScreenshot("output/validation/grassland-dynamics/play-scene.png");
                    heightMotion = player.GetComponent<RobotHeightMotionDetector>();
                    Require(heightMotion != null, "Missing height-motion detector");
                    heightMotion.enabled = false;
                    typeof(RobotHeightMotionDetector).GetProperty("HasData").SetValue(heightMotion, true);
                    typeof(RobotHeightMotionDetector).GetProperty("IsAirborne").SetValue(heightMotion, true);
                    airborneStamp = history[7].z;
                    Time.timeScale = 1;
                    phase = 5; phaseStarted = now;
                }
            }
            else if (phase == 5 && now - phaseStarted > .15)
            {
                Require(Shader.GetGlobalVectorArray("_GrassFootprints")[7].z == airborneStamp,
                    "Airborne signal continued adding contacts");
                typeof(RobotHeightMotionDetector).GetProperty("IsAirborne").SetValue(heightMotion, false);
                heightMotion.enabled = true;
                phase = 6; phaseStarted = now;
            }
            else if (phase == 6 && now - phaseStarted > .15)
            {
                Require(Shader.GetGlobalVectorArray("_GrassFootprints")[7].z > airborneStamp,
                    "Ground contact did not resume");
                runtimeMap = UnityEngine.Object.FindFirstObjectByType<MapTestSceneController>();
                foreach (var renderer in runtimeMap.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.name.StartsWith("Map Tile ")) { retiredMaterial = renderer.sharedMaterial; break; }
                typeof(MapTestSceneController).GetMethod("RebuildGeneratedMap",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(runtimeMap, null);
                phase = 7; phaseStarted = now;
            }
            else if (phase == 7 && now - phaseStarted > .3)
            {
                Require(retiredMaterial == null, "Map reload leaked the previous tile material");
                bool rebound = false;
                foreach (var renderer in runtimeMap.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.name.StartsWith("Map Tile ") && renderer.sharedMaterial.GetFloat("_GrassEnabled") > .5f)
                        rebound = true;
                Require(rebound, "Map reload lost grass data");
                staticFrames.Sort(); dynamicFrames.Sort();
                Finish("PASS: real scene bindings, moving contacts, pause/resume, disable/re-enable, simulated airborne signal, landing, map reload and tile material disposal.\n"
                    + "Editor median frame time, 60 samples each: static=" + staticFrames[30].ToString("F2")
                    + "ms; dynamic=" + dynamicFrames[30].ToString("F2") + "ms.\n"
                    + "Editor frame times are a smoke check, not a GPU benchmark.");
            }
        }
        catch (Exception exception) { Finish("FAILED: " + exception); Debug.LogException(exception); }
    }

    private static Vector3 FindGrass(MapTestSceneController map)
    {
        foreach (var entry in map.LevelAsset.Tiles)
        {
            if (entry.level.BakedGrasslandMask == null) continue;
            var data = entry.level.GetOrCreateSurfaceTypeMap();
            int resolution = entry.level.SurfaceMaskResolution;
            for (int y = resolution / 4; y < resolution * 3 / 4; y++)
                for (int x = resolution / 4; x < resolution * 3 / 4; x++)
                    if (data[y * resolution + x] == (byte)TerrainKind.Grassland)
                        return map.MapPositionToWorld(Vector2.Scale(new Vector2((x + .5f) / resolution,
                            (y + .5f) / resolution), entry.level.MapSizeMeters)
                            + Vector2.Scale(entry.coordinate, map.LevelAsset.TileSizeMeters));
        }
        throw new InvalidOperationException("No painted grassland found for play smoke");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Finish(string result)
    {
        File.WriteAllText(Report, result);
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Time.timeScale = 1;
        EditorApplication.ExitPlaymode();
    }
}
