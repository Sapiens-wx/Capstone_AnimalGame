using System;
using System.IO;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEngine;

public static class GrasslandDynamicsChecks
{
    [MenuItem("Animal Game/Checks/Grassland Dynamics")]
    public static void Run()
    {
        string directory = "output/validation/grassland-dynamics";
        Directory.CreateDirectory(directory);
        var level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>("Assets/Maps/MainHeightMapLevel/MainHeightMapLevel.asset");
        var material = new Material(Shader.Find("Hidden/AnimalGame/Grassland Render Probe"));
        var snapshots = new System.Collections.Generic.List<Texture2D>();
        float oldActive = Shader.GetGlobalFloat("_GrassActive");
        float oldNow = Shader.GetGlobalFloat("_GrassNow");
        Vector4 oldDynamics = Shader.GetGlobalVector("_GrassDynamics");
        Vector4 oldPeriods = Shader.GetGlobalVector("_GrassWindPeriods");
        Vector4[] oldFootprints = Shader.GetGlobalVectorArray("_GrassFootprints");
        try
        {
            Require(level.GrasslandBladeRects.Length == 4 && level.BakedGrasslandMask != null, "Missing baked grass data");
            material.SetTexture("_GrassPatternTex", level.GrasslandPattern);
            material.SetTexture("_GrassMaskTex", Texture2D.whiteTexture);
            material.SetVectorArray("_GrassRects", level.GrasslandBladeRects);
            material.SetVector("_GrassAlphaNormalization", new Vector4(level.GrasslandAlphaNormalization.x,
                level.GrasslandAlphaNormalization.y, 0, 0));
            material.SetMatrix("_GrassMetersToWorld", Matrix4x4.identity);
            material.SetVector("_GrassTileOrigin", Vector4.zero);
            material.SetVector("_MapSizeMeters", new Vector4(1, 1, 0, 0));
            material.SetFloat("_GrassTileSize", 1f);
            var footprints = new Vector4[8];
            for (int i = 0; i < 8; i++) footprints[i] = new Vector4(0, 0, -10000, 0);
            Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
            var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Resources/Robot/RobotMarker.prefab").GetComponent<GrasslandTrampleController>());
            Vector2 periods = settings.FindProperty("windPeriodSeconds").vector2Value;
            var dynamics = new Vector4(settings.FindProperty("windTipOffset").floatValue,
                settings.FindProperty("pressedTipOffset").floatValue,
                settings.FindProperty("pressedLength").floatValue,
                settings.FindProperty("recoverySeconds").floatValue);
            Require(settings.FindProperty("trampleRadiusOfBodyDiameter").floatValue * .65f > .5f,
                "Strong pressure is hidden beneath the opaque robot body");
            Shader.SetGlobalVector("_GrassWindPeriods", new Vector4(periods.x, periods.y, 0, 0));
            Shader.SetGlobalVector("_GrassDynamics", dynamics);
            Shader.SetGlobalFloat("_GrassActive", 0f);
            var idle = Capture(material, 256, 256); snapshots.Add(idle);
            Shader.SetGlobalFloat("_GrassActive", 1f);
            Shader.SetGlobalFloat("_GrassNow", .7f);
            var wind = Capture(material, 256, 256); snapshots.Add(wind);
            Require(Difference(idle, wind) > 100, "Wind did not change grass");
            var paused = Capture(material, 256, 256); snapshots.Add(paused);
            Require(Difference(wind, paused) == 0, "Unchanged simulation time changed wind");
            dynamics.x = 0;
            Shader.SetGlobalVector("_GrassDynamics", dynamics);
            Shader.SetGlobalFloat("_GrassNow", 10f);
            Vector4 blade = level.GrasslandBladeRects[0];
            footprints[0] = new Vector4(blade.x + blade.z * .5f, blade.y + blade.w * .5f, 10, .2f);
            Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
            var pressed = Capture(material, 256, 256); snapshots.Add(pressed);
            Require(AlphaSum(pressed) < AlphaSum(idle) * .92f, "Contact did not visibly shorten grass");
            Shader.SetGlobalFloat("_GrassNow", 10.21f);
            var recovered = Capture(material, 256, 256); snapshots.Add(recovered);
            Require(Difference(idle, recovered) == 0, "Grass failed to recover after 0.2 seconds");
            material.SetFloat("_GrassTileSize", .4f);
            footprints[0] = new Vector4(.3f, .14f, 10.21f, .25f);
            Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
            var whole = Capture(material, 512, 256); snapshots.Add(whole);
            long crossingAlpha = 0;
            for (int y = 30; y < 35; y++) for (int x = 205; x < 215; x++)
                crossingAlpha += whole.GetPixel(x, y).a > .05f ? 1 : 0;
            Require(crossingAlpha > 0, "Strong bend was clipped at the repeating cell edge");
            material.SetVector("_MapSizeMeters", new Vector4(.5f, 1, 0, 0));
            var left = Capture(material, 256, 256); snapshots.Add(left);
            material.SetVector("_GrassTileOrigin", new Vector4(.5f, 0, 0, 0));
            var right = Capture(material, 256, 256); snapshots.Add(right);
            var pixels = whole.GetPixels32(); var l = left.GetPixels32(); var r = right.GetPixels32();
            for (int y = 0; y < 256; y++) for (int x = 0; x < 512; x++)
                Require(Mathf.Abs(pixels[y * 512 + x].a - (x < 256 ? l[y * 256 + x].a : r[y * 256 + x - 256].a)) <= 2,
                    "Tile boundary changed grass sampling");
            foreach (var shader in new[] { material.shader, Shader.Find("AnimalGame/Dynamic Height Contours"),
                Shader.Find("Hidden/AnimalGame/Bake Terrain Surface Palette") })
                Require(!ShaderUtil.ShaderHasError(shader), "Shader errors: " + shader.name);
            File.WriteAllBytes(directory + "/idle.png", idle.EncodeToPNG());
            File.WriteAllBytes(directory + "/wind.png", wind.EncodeToPNG());
            File.WriteAllBytes(directory + "/pressed.png", pressed.EncodeToPNG());
            File.WriteAllText(directory + "/render-checks.txt", "PASS: wind, paused clock, compression, 0.2s recovery, tile continuity, shader compilation.");
            Debug.Log("Grassland Dynamics GPU checks passed.");
        }
        finally
        {
            Shader.SetGlobalFloat("_GrassActive", oldActive);
            Shader.SetGlobalFloat("_GrassNow", oldNow);
            Shader.SetGlobalVector("_GrassDynamics", oldDynamics);
            Shader.SetGlobalVector("_GrassWindPeriods", oldPeriods);
            if (oldFootprints != null && oldFootprints.Length == 8)
                Shader.SetGlobalVectorArray("_GrassFootprints", oldFootprints);
            UnityEngine.Object.DestroyImmediate(material);
            foreach (var snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
        }
    }

    private static Texture2D Capture(Material material, int width, int height)
    {
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(Texture2D.whiteTexture, target, material);
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            return texture;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
    }
    private static long AlphaSum(Texture2D texture)
    { long sum = 0; foreach (var pixel in texture.GetPixels32()) sum += pixel.a; return sum; }
    private static long Difference(Texture2D a, Texture2D b)
    { var x = a.GetPixels32(); var y = b.GetPixels32(); long sum = 0;
        for (int i = 0; i < x.Length; i++) sum += Math.Abs(x[i].a - y[i].a); return sum; }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
