using System;
using System.IO;
using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.RobotMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GrasslandDynamicsChecks
{
    [MenuItem("Animal Game/Checks/Grassland Dynamics")]
    public static void Run() => Run("output/validation/grassland-dynamics");

    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var material = new Material(Shader.Find("Hidden/AnimalGame/Grassland Render Probe"));
        var snapshots = new List<Texture2D>();
        float oldActive = Shader.GetGlobalFloat("_GrassActive"), oldNow = Shader.GetGlobalFloat("_GrassNow");
        Vector4 oldDynamics = Shader.GetGlobalVector("_GrassDynamics"), oldPeriods = Shader.GetGlobalVector("_GrassWindPeriods");
        Vector4[] oldFootprints = Shader.GetGlobalVectorArray("_GrassFootprints");
        try
        {
            var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Resources/Robot/RobotMarker.prefab").GetComponent<GrasslandTrampleController>());
            Vector2 periods = settings.FindProperty("windPeriodSeconds").vector2Value;
            var dynamics = new Vector4(settings.FindProperty("windTipOffset").floatValue,
                settings.FindProperty("pressedTipOffset").floatValue, settings.FindProperty("pressedLength").floatValue,
                settings.FindProperty("recoverySeconds").floatValue);
            Shader.SetGlobalVector("_GrassWindPeriods", new Vector4(periods.x, periods.y, 0, 0));
            var footprints = new Vector4[8];
            for (int i = 0; i < 8; i++) footprints[i] = new Vector4(0, 0, -10000, 0);
            foreach (string path in new[] { "Assets/Maps/MainHeightMapLevel/MainHeightMapLevel.asset", "Assets/Maps/RockyMountainHeightMapLevel.asset" })
            {
                var level = AssetDatabase.LoadAssetAtPath<HeightMapLevelAsset>(path);
                Require(level.GrasslandBladeRects.Length == 8 && level.BakedGrasslandMask != null, "Missing baked grass data");
                material.SetTexture("_GrassPatternTex", level.GrasslandPattern);
                material.SetTexture("_GrassMaskTex", Texture2D.whiteTexture);
                material.SetVectorArray("_GrassRects", level.GrasslandBladeRects);
                material.SetVector("_GrassLayout", level.GrasslandLayout);
                material.SetVector("_GrassShape", level.GrasslandShape);
                material.SetVector("_GrassAlphaNormalization", new Vector4(level.GrasslandAlphaNormalization.x,
                    level.GrasslandAlphaNormalization.y, 0, 0));
                material.SetMatrix("_GrassMetersToWorld", Matrix4x4.identity);
                material.SetVector("_GrassTileOrigin", Vector4.zero);
                material.SetVector("_MapSizeMeters", new Vector4(1, 1, 0, 0));
                material.SetFloat("_GrassTileSize", 1f);
                material.SetFloat("_GrassProbeMode", 0);
                Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
                Shader.SetGlobalVector("_GrassDynamics", dynamics);
                Shader.SetGlobalFloat("_GrassActive", 0);
                var idle = Capture(material, 512, 512); snapshots.Add(idle);
                var repeat = Capture(material, 512, 512); snapshots.Add(repeat);
                Require(Difference(idle, repeat) == 0, "Layout changed between identical renders");
                material.SetVector("_GrassTileOrigin", new Vector4(1,0,0,0));
                var adjacent = Capture(material, 512, 512); snapshots.Add(adjacent);
                Require(Difference(idle, adjacent) > 10000, "Neighbouring old repeats are still identical");
                material.SetVector("_GrassTileOrigin", Vector4.zero);
                Shader.SetGlobalFloat("_GrassActive", 1);
                Shader.SetGlobalFloat("_GrassNow", .7f);
                var wind = Capture(material, 512, 512); snapshots.Add(wind);
                Require(Difference(idle, wind) > 100, "Wind did not change grass");
                var paused = Capture(material, 512, 512); snapshots.Add(paused);
                Require(Difference(wind, paused) == 0, "Paused simulation changed wind");
                Shader.SetGlobalVector("_GrassDynamics", new Vector4(0,dynamics.y,dynamics.z,dynamics.w));
                Shader.SetGlobalFloat("_GrassNow", 10);
                footprints[0] = new Vector4(.5f,.5f,10,10);
                Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
                var pressed = Capture(material, 512, 512); snapshots.Add(pressed);
                Require(AlphaSum(pressed) < AlphaSum(idle) * .8, "Rotated grass failed to shorten under pressure");
                Shader.SetGlobalFloat("_GrassNow", 10 + dynamics.w + .01f);
                var recovered = Capture(material, 512, 512); snapshots.Add(recovered);
                Require(Difference(idle, recovered) == 0, "Grass failed to recover");
                footprints[0] = new Vector4(0, 0, -10000, 0);
                Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
                Shader.SetGlobalFloat("_GrassActive", 0);
                material.SetFloat("_GrassTileSize", level.GrasslandTileSizeMeters);
                material.SetVector("_MapSizeMeters", new Vector4(32,16,0,0));
                var overview = Capture(material, 1536, 768); snapshots.Add(overview);
                File.WriteAllBytes(directory + "/" + level.name + "-layout.png", overview.EncodeToPNG());
                // Pixel-identical full/half renders also cover grass crossing rotated layout cells.
                material.SetVector("_GrassTileOrigin", new Vector4(-4,7,0,0));
                material.SetVector("_MapSizeMeters", new Vector4(8,4,0,0));
                Shader.SetGlobalFloat("_GrassActive", 1);
                Shader.SetGlobalFloat("_GrassNow", 20);
                footprints[0] = new Vector4(0,9,20,2);
                Shader.SetGlobalVectorArray("_GrassFootprints", footprints);
                var whole = Capture(material, 1024, 512); snapshots.Add(whole);
                material.EnableKeyword("GRASS_WIDE_NEIGHBOURHOOD");
                var wide = Capture(material, 1024, 512); snapshots.Add(wide);
                material.DisableKeyword("GRASS_WIDE_NEIGHBOURHOOD");
                Require(Difference(whole, wide) == 0, "3x3 support clipped grass compared with a 5x5 neighbourhood");
                material.SetVector("_MapSizeMeters", new Vector4(4,4,0,0));
                var left = Capture(material, 512, 512); snapshots.Add(left);
                material.SetVector("_GrassTileOrigin", new Vector4(0,7,0,0));
                var right = Capture(material, 512, 512); snapshots.Add(right);
                var a = whole.GetPixels32(); var l = left.GetPixels32(); var r = right.GetPixels32();
                long errors = 0;
                for (int y = 0; y < 512; y++) for (int x = 0; x < 1024; x++)
                    if (Math.Abs(a[y*1024+x].a - (x < 512 ? l[y*512+x].a : r[y*512+x-512].a)) > 3) errors++;
                Require(errors < 20, "Tile seam in rotated/pressed grass: " + errors + " pixels");
                CheckLayout(material, level);
                material.SetFloat("_GrassProbeMode", 0);
                // Inspect actual map mask transitions without changing or rebaking terrain assets.
                material.SetTexture("_GrassMaskTex", level.BakedGrasslandMask);
                material.SetVector("_MapSizeMeters", new Vector4(level.MapSizeMeters.x,level.MapSizeMeters.y,0,0));
                material.SetVector("_GrassTileOrigin", Vector4.zero);
                Shader.SetGlobalFloat("_GrassActive", 0);
                var map = Capture(material, 1024,1024); snapshots.Add(map);
                File.WriteAllBytes(directory + "/" + level.name + "-coverage.png",map.EncodeToPNG());
                File.WriteAllBytes(directory + "/idle.png",idle.EncodeToPNG());
                File.WriteAllBytes(directory + "/wind.png",wind.EncodeToPNG());
                File.WriteAllBytes(directory + "/pressed.png",pressed.EncodeToPNG());
                CheckDistantBaseGrass(material);
                CheckReveal(level, directory);
                footprints[0] = new Vector4(0,0,-10000,0);
            }
            foreach (var shader in new[] { material.shader, Shader.Find("AnimalGame/Dynamic Height Contours"),
                Shader.Find("Hidden/AnimalGame/Bake Terrain Surface Palette") })
                Require(!ShaderUtil.ShaderHasError(shader), "Shader errors: " + shader.name);
            File.WriteAllText(directory + "/render-checks.txt", "PASS: both maps; deterministic irregular layout, non-repetition, seed changes, position bounds, smooth regional direction, wind, pause, rotated compression/recovery, 3x3 versus 5x5 support, tile continuity, map shader reveal boundary, shader compilation.");
            Debug.Log("Grassland Dynamics GPU checks passed.");
        }
        finally
        {
            Shader.SetGlobalFloat("_GrassActive", oldActive); Shader.SetGlobalFloat("_GrassNow", oldNow);
            Shader.SetGlobalVector("_GrassDynamics", oldDynamics); Shader.SetGlobalVector("_GrassWindPeriods", oldPeriods);
            if (oldFootprints != null && oldFootprints.Length == 8) Shader.SetGlobalVectorArray("_GrassFootprints", oldFootprints);
            UnityEngine.Object.DestroyImmediate(material);
            foreach (var texture in snapshots) UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void CheckDistantBaseGrass(Material material)
    {
        var mask = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        Texture2D distant = null;
        Texture2D nearby = null;
        try
        {
            // Test22's flat grass tiles bake the tutorial opacity and contour fade to 3/255.
            mask.SetPixel(0, 0, new Color32(255, 255, 255, 3));
            mask.Apply();
            material.SetTexture("_GrassMaskTex", mask);
            material.SetVector("_GrassOverlaySettings", new Vector4(0, 0, 0, .2f));
            material.SetVector("_MapSizeMeters", new Vector4(250, 250, 0, 0));
            distant = Capture(material, 384, 384);
            Color32[] broadPixels = distant.GetPixels32();
            int visible = Array.FindAll(broadPixels, pixel => pixel.a > 0).Length;
            Require(visible > 0 && visible < broadPixels.Length * .95f,
                "Distant grass disappeared or filled transparent gaps with a uniform grey floor");

            material.SetVector("_MapSizeMeters", new Vector4(12, 12, 0, 0));
            nearby = Capture(material, 384, 384);
            Color32[] bladePixels = nearby.GetPixels32();
            Require(Array.Exists(bladePixels, pixel => pixel.a > 3) && Array.Exists(bladePixels, pixel => pixel.a == 0),
                "Nearby low-alpha grass lost its individual blade silhouettes");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(nearby);
            UnityEngine.Object.DestroyImmediate(distant);
            UnityEngine.Object.DestroyImmediate(mask);
        }
    }

    private static void CheckReveal(HeightMapLevelAsset level, string directory)
    {
        var map = new Material(Shader.Find("AnimalGame/Dynamic Height Contours"));
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Grass reveal validation");
        SceneManager.MoveGameObjectToScene(root, scene);
        var target = RenderTexture.GetTemporary(512,512,0,RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D image = null;
        Sprite tileSprite = null;
        try
        {
            map.SetFloat("_GrassEnabled", 1);
            map.SetFloat("_SurfaceEnabled", 1);
            map.SetTexture("_SurfaceTex", Texture2D.blackTexture);
            map.SetTexture("_GrassMaskTex", Texture2D.whiteTexture);
            map.SetTexture("_GrassPatternTex", level.GrasslandPattern);
            map.SetVectorArray("_GrassRects", level.GrasslandBladeRects);
            map.SetVector("_GrassLayout", level.GrasslandLayout);
            map.SetVector("_GrassShape", level.GrasslandShape);
            map.SetFloat("_GrassTileSize", level.GrasslandTileSizeMeters);
            map.SetVector("_GrassOverlaySettings", new Vector4(0, 0, 0, .2f));
            map.SetVector("_GrassAlphaNormalization", new Vector4(level.GrasslandAlphaNormalization.x,
                level.GrasslandAlphaNormalization.y, 0, 0));
            map.SetMatrix("_GrassMetersToWorld", Matrix4x4.identity);
            map.SetVector("_MapSizeMeters", new Vector4(8,8,0,0));
            map.SetFloat("_MaximumOpacity", 0);
            map.SetColor("_ContourColor", Color.clear);
            map.SetFloat("_SurfaceRevealEnabled", 1);
            map.SetVector("_SurfaceRevealCenterPixels", new Vector4(256,256,0,0));
            map.SetFloat("_SurfaceRevealRadiusPixels", 180);
            map.SetFloat("_SurfaceRevealEdgePixels", level.SurfaceRevealEdgePixels);
            var cameraObject = new GameObject("Preview camera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.position = new Vector3(0,0,-10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.targetTexture = target;
            var tileObject = new GameObject("Preview map tile");
            tileObject.transform.SetParent(root.transform, false);
            var renderer = tileObject.AddComponent<SpriteRenderer>();
            Texture2D carrier = Texture2D.whiteTexture;
            tileSprite = Sprite.Create(carrier, new Rect(0, 0, carrier.width, carrier.height),
                Vector2.one * .5f, carrier.width * .5f, 0, SpriteMeshType.FullRect);
            renderer.sprite = tileSprite;
            renderer.sharedMaterial = map;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(512,512,TextureFormat.RGBA32,false,true);
            image.ReadPixels(new Rect(0,0,512,512),0,0); image.Apply();
            var pixels = image.GetPixels32();
            long inside = 0, outside = 0;
            for(int y=0;y<512;y++) for(int x=0;x<512;x++)
            {
                var p=pixels[y*512+x];
                if(new Vector2(x+.5f-256,y+.5f-256).sqrMagnitude>181*181)
                    outside+=p.r+p.g+p.b;
                else inside+=p.r+p.g+p.b;
            }
            File.WriteAllBytes(directory+"/"+level.name+"-reveal.png",image.EncodeToPNG());
            Require(inside>10000,"Actual map shader failed to display grass");
            Require(outside==0,"Grass escaped the player reveal circle");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(tileSprite);
            EditorSceneManager.ClosePreviewScene(scene);
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(map);
        }
    }

    private static void CheckLayout(Material material, HeightMapLevelAsset level)
    {
        material.SetVector("_GrassTileOrigin",Vector4.zero);
        float spacing = level.GrasslandTileSizeMeters / Mathf.Sqrt(8 * level.GrasslandShape.x);
        material.SetVector("_MapSizeMeters",new Vector4(spacing*16,spacing*16,0,0));
        material.SetFloat("_GrassProbeMode",1);
        var positions = Capture(material,16,16,true);
        var repeat = Capture(material,16,16,true);
        var layout = level.GrasslandLayout; layout.x += 1;
        material.SetVector("_GrassLayout",layout);
        var changed = Capture(material,16,16,true);
        material.SetVector("_GrassLayout",level.GrasslandLayout);
        try
        {
            var pixels=positions.GetPixels(); var again=repeat.GetPixels(); var other=changed.GetPixels();
            float seedDifference=0;
            for(int y=0;y<16;y++) for(int x=0;x<16;x++)
            {
                int i=y*16+x; var p=pixels[i];
                Require(p==again[i],"Instance data changed across identical renders");
                Require(Mathf.Abs(p.r/spacing-x-.5f)<=.401f && Mathf.Abs(p.g/spacing-y-.5f)<=.401f,"Random point escaped its cell");
                Require(p.a>=0 && p.a<8,"Invalid grass slice");
                seedDifference+=Mathf.Abs(p.r-other[i].r)+Mathf.Abs(p.g-other[i].g);
            }
            Require(seedDifference>spacing,"Changing the map seed did not change distribution");
        }
        finally { UnityEngine.Object.DestroyImmediate(positions); UnityEngine.Object.DestroyImmediate(repeat); UnityEngine.Object.DestroyImmediate(changed); }
        material.SetFloat("_GrassProbeMode",2);
        float region = level.GrasslandTileSizeMeters*level.GrasslandLayout.z;
        material.SetVector("_MapSizeMeters",new Vector4(region*8,region*8,0,0));
        var angles=Capture(material,512,512,true);
        try
        {
            var pixels=angles.GetPixels(); float min=10,max=-10;
            for(int y=0;y<512;y++) for(int x=0;x<512;x++)
            {
                float angle=pixels[y*512+x].r; min=Mathf.Min(min,angle); max=Mathf.Max(max,angle);
                if(x>0) Require(Mathf.Abs(angle-pixels[y*512+x-1].r)<.2f,"Direction jumped horizontally");
                if(y>0) Require(Mathf.Abs(angle-pixels[(y-1)*512+x].r)<.2f,"Direction jumped vertically");
            }
            Require(max-min>5,"Regional directions do not cover north/southwest and intermediate orientations");
        }
        finally { UnityEngine.Object.DestroyImmediate(angles); }
    }
    private static Texture2D Capture(Material material,int width,int height,bool floating=false)
    {
        var target=RenderTexture.GetTemporary(width,height,0,floating?RenderTextureFormat.ARGBFloat:RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        try
        {
            Graphics.Blit(Texture2D.whiteTexture,target,material); RenderTexture.active=target;
            var texture=new Texture2D(width,height,floating?TextureFormat.RGBAFloat:TextureFormat.RGBA32,false,true);
            texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();return texture;
        }
        finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target); }
    }
    private static long AlphaSum(Texture2D texture)
    { long sum=0;foreach(var p in texture.GetPixels32())sum+=p.a;return sum; }
    private static long Difference(Texture2D a,Texture2D b)
    {var x=a.GetPixels32();var y=b.GetPixels32();long sum=0;for(int i=0;i<x.Length;i++)sum+=Math.Abs(x[i].a-y[i].a);return sum;}
    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
}
