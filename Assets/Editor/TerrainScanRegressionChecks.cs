using System;
using System.Collections;
using System.Reflection;
using AnimalGame.MapTest;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    public static class TerrainScanRegressionChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Animal Game/Validation/Run Terrain Scan Checks")]
        public static void Run()
        {
            CheckHeights(false, 19, 27, 19);
            CheckHeights(true, 19, 30, 19);
            CheckHeights(true, 30, 19, 30);
            CheckHeights(false, 19, 26, 19);
            CheckHeights(false, 19, 30, 25);
            CheckHeights(false, 19, 40, 60, 90);
            CheckHeights(true, 19, 30, 30, 30, 19);
            CheckHeights(true, 30, 19, 19, 19, 30);
            CheckHeights(true, 0, 4, 8, 12, 8, 4, 0);

            var random = new System.Random(23451);
            for (int trial = 0; trial < 300; trial++)
            {
                var heights = new double[20];
                for (int i = 0; i < heights.Length; i++) heights[i] = random.Next(50);
                bool brute = false;
                for (int a = 0; a < heights.Length; a++)
                    for (int b = a + 1; b < heights.Length; b++)
                        for (int c = b + 1; c < heights.Length; c++)
                            brute |= (heights[b] - heights[a] > 8 && heights[b] - heights[c] > 8)
                                || (heights[a] - heights[b] > 8 && heights[c] - heights[b] > 8);
                var state = new TerrainScanPeakState(heights[0], 8);
                for (int i = 1; i < heights.Length; i++) state.Add(heights[i]);
                Require(state.Blocked == brute, "Online state must match independent ordered-triple oracle.");
            }

            using (var field = Field(2, 2, new float[] { 0, 24, 24, 0 }))
            {
                Require(Profile(field, Vector2.zero, Vector2.one).Blocked,
                    "Interior bilinear peak must block even with equal cell entry/exit heights.");
                Require(Profile(field, Vector2.one, Vector2.zero).Blocked, "Reverse diagonal must block.");
                Require(Profile(field, Vector2.zero, Vector2.up).Clear, "Monotone vertical ray must remain clear.");
                Require(Profile(field, Vector2.zero, Vector2.right).Clear, "Monotone horizontal ray must remain clear.");
                Require(Profile(field, Vector2.one, Vector2.one).Clear, "Zero-length edge ray must terminate.");
                Require(Profile(field, new Vector2(-1, 0), Vector2.one).InvalidMap, "Outside endpoint must fail separately.");
            }
            using (var field = Field(2, 2, new float[] { 0, 16, 16, 0 }))
                Require(Profile(field, Vector2.zero, Vector2.one).Clear, "Interior peak exactly one interval high must not block.");
            using (var field = Field(2, 2, new float[] { 24, 0, 0, 24 }))
                Require(Profile(field, Vector2.zero, Vector2.one).Blocked, "Interior bilinear valley must block.");
            using (var field = Field(2, 2, new float[] { 0, 100, 0, 0 }))
                Require(Profile(field, Vector2.zero, Vector2.up).Clear, "Off-ray high corner must not block.");
            using (var field = Field(3, 2, new float[6], new byte[] { 255, 0, 255, 255, 0, 255 }))
                Require(Profile(field, Vector2.zero, new Vector2(2, 0)).InvalidMap,
                    "Line must reject an interior playable-mask hole.");

            // Compare the configured sample positions against an independent
            // bilinear-surface oracle; detection now deliberately uses fixed density.
            var samples = new float[64];
            for (int i = 0; i < samples.Length; i++) samples[i] = random.Next(40);
            using (var field = Field(8, 8, samples))
            {
                for (int trial = 0; trial < 200; trial++)
                {
                    Vector2 a = new Vector2((float)random.NextDouble() * 7, (float)random.NextDouble() * 7);
                    Vector2 b = new Vector2((float)random.NextDouble() * 7, (float)random.NextDouble() * 7);
                    var oracle = new TerrainScanPeakState(field.SampleSurfaceHeight(a / 7f), 8);
                    for (int i = 1; i <= 128; i++)
                        oracle.Add(field.SampleSurfaceHeight(Vector2.Lerp(a, b, i / 128f) / 7f));
                    Require(Profile(field, a, b).Blocked == oracle.Blocked, "Profile must match the configured uniform surface samples.");
                }
            }
            CheckSamplingDensity();
            CheckSelection();
            CheckFrameScheduling();
            Debug.Log("Terrain scan regression checks passed: ordered peaks, configurable sampling, masks, spatial buckets, OR selection, and frame deadlines.");
        }

        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void CheckSamplingDensity()
        {
            MethodInfo samples = typeof(TraversalScanOverlayUI).GetMethod("SamplesForRadius", BindingFlags.Static | BindingFlags.NonPublic);
            Require((int)samples.Invoke(null, new object[] { 1f, 32 }) == 32, "View edge must use the configured count.");
            Require((int)samples.Invoke(null, new object[] { 0.5f, 32 }) == 16, "Half-radius profile must use half the samples.");
            Require((int)samples.Invoke(null, new object[] { 0.01f, 1 }) == 1, "Short lines must include their endpoint.");
            using (var field = Field(3, 2, new float[] { 0, 20, 0, 0, 20, 0 }))
            {
                var coarse = new TerrainScanProfile(field, Vector2.zero, new Vector2(2, 0), 8, 1);
                coarse.Step();
                Require(coarse.Clear && coarse.SamplesRead == 1, "One sample must read only P after O, deliberately skipping the interior ridge.");
                var fine = new TerrainScanProfile(field, Vector2.zero, new Vector2(2, 0), 8, 2);
                fine.Step(); fine.Step();
                Require(fine.Blocked && fine.SamplesRead == 2, "Increasing density must detect a ridge at the added sample.");
            }
            using (var field = Field(2, 2, new float[4]))
            {
                foreach (int count in new[] { 1, 8, 32 })
                {
                    var profile = new TerrainScanProfile(field, Vector2.zero, Vector2.one, 8, count);
                    for (int i = 0; i < count; i++) profile.Step();
                    Require(profile.Clear && profile.SamplesRead == count, "Flat profile must finish at exactly the chosen sample count.");
                }
            }
        }

        private static void CheckFrameScheduling()
        {
            foreach (int frames in new[] { 1, 2, 4, 8 })
            {
                var go = new GameObject("Scan frame budget validation");
                var overlay = go.AddComponent<TraversalScanOverlayUI>();
                overlay.enabled = false;
                try
                {
                    Set(overlay, "capturedCompletionFrames", frames);
                    Set(overlay, "scanStage", Enum.Parse(typeof(TraversalScanOverlayUI).GetNestedType("ScanStage", BindingFlags.NonPublic), "Markers"));
                    IList candidates = (IList)Get(overlay, "sampledCandidates");
                    Type type = typeof(TraversalScanOverlayUI).GetNestedType("SampledCandidate", BindingFlags.NonPublic);
                    for (int i = 0; i < 64; i++)
                    {
                        Add(candidates, type, new Vector2(i, 0), true, false);
                        typeof(TraversalScanOverlayUI).GetMethod("PushSelectedCandidate", Private).Invoke(overlay, new object[] { i });
                    }
                    int previous = 0, smallest = int.MaxValue, largest = 0;
                    for (int i = 0; i < frames; i++)
                    {
                        Call(overlay, "ProcessScanFrame");
                        int produced = overlay.VisibleMarkerCount - previous;
                        previous = overlay.VisibleMarkerCount;
                        smallest = Math.Min(smallest, produced); largest = Math.Max(largest, produced);
                    }
                    Require(overlay.VisibleMarkerCount == 64 && Get(overlay, "scanStage").ToString() == "Finished",
                        "All markers must finish within the configured frame deadline, including one frame.");
                    Require(largest - smallest <= 2, "Equivalent marker work must be distributed evenly across frames.");
                    Require(overlay.ScanFramesUsed == frames, "Frame accounting must match the configured schedule.");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            // A deadline of one must also drain a partially completed profile and
            // every downstream stage, not just the marker-only fixture above.
            var profileGo = new GameObject("Profile deadline validation");
            var profileOverlay = profileGo.AddComponent<TraversalScanOverlayUI>();
            profileOverlay.enabled = false;
            try
            {
                using (var field = Field(4, 2, new float[8]))
                {
                    Set(profileOverlay, "capturedCompletionFrames", 1);
                    Set(profileOverlay, "scanHeightField", field);
                    Set(profileOverlay, "scanOrigin", Vector2.zero);
                    Set(profileOverlay, "scanContourInterval", 8f);
                    Set(profileOverlay, "scanStage", Enum.Parse(typeof(TraversalScanOverlayUI).GetNestedType("ScanStage", BindingFlags.NonPublic), "Profiles"));
                    IList candidates = (IList)Get(profileOverlay, "sampledCandidates");
                    Type type = typeof(TraversalScanOverlayUI).GetNestedType("SampledCandidate", BindingFlags.NonPublic);
                    Add(candidates, type, new Vector2(3, 0), false, false);
                    object item = candidates[0]; type.GetField("NearAnyDanger").SetValue(item, true); candidates[0] = item;
                    Call(profileOverlay, "CheckProfileStep");
                    Call(profileOverlay, "ProcessScanFrame");
                    Require(profileOverlay.VisibleMarkerCount == 1 && profileOverlay.ProfileSampleVisitCount == 16
                        && Get(profileOverlay, "scanStage").ToString() == "Finished",
                        "Final frame must resume and drain profile, eligibility, and marker creation.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(profileGo); }
        }

        private static void CheckSelection()
        {
            var go = new GameObject("Terrain scan validation");
            var overlay = go.AddComponent<TraversalScanOverlayUI>();
            overlay.enabled = false;
            try
            {
                // Ridge is inside the first meter: excluding central UI candidates
                // must never truncate the O-to-P profile.
                using (var field = Field(4, 2, new float[] { 0, 20, 0, 0, 0, 20, 0, 0 }))
                {
                    Set(overlay, "scanOrigin", Vector2.zero);
                    Set(overlay, "scanHeightField", field);
                    Set(overlay, "scanContourInterval", 8f);
                    Set(overlay, "unpassableNeighborhoodRadiusMeters", 1f);
                    IList candidates = (IList)Get(overlay, "sampledCandidates");
                    Type type = typeof(TraversalScanOverlayUI).GetNestedType("SampledCandidate", BindingFlags.NonPublic);
                    Add(candidates, type, new Vector2(2.5f, 0), true, false);
                    Add(candidates, type, new Vector2(3, 0), false, true);
                    Add(candidates, type, new Vector2(3, 0), false, false);
                    Add(candidates, type, new Vector2(1.2f, 1), false, false);
                    Add(candidates, type, new Vector2(2.75f, 0), false, false);
                    Add(candidates, type, Vector2.zero, true, false);
                    Add(candidates, type, new Vector2(0.25f, 0), false, false);
                    // Build the same linked buckets in an intentionally reversed order.
                    IDictionary buckets = (IDictionary)Get(overlay, "candidateBuckets");
                    for (int i = candidates.Count - 1; i >= 0; i--)
                    {
                        object item = candidates[i];
                        Vector2 position = (Vector2)type.GetField("MapPosition").GetValue(item);
                        var key = new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
                        type.GetField("NextInBucket").SetValue(item, buckets.Contains(key) ? (int)buckets[key] : -1);
                        candidates[i] = item;
                        buckets[key] = i;
                    }
                    ((IList)Get(overlay, "unpassableSeeds")).Add(0);
                    ((IList)Get(overlay, "unpassableSeeds")).Add(5);
                    for (int i = 0; i < 100; i++) Call(overlay, "ExpandDangerStep");
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Vector2 point = (Vector2)type.GetField("MapPosition").GetValue(candidates[i]);
                        bool expected = (point - new Vector2(2.5f, 0)).sqrMagnitude <= 1 || point.sqrMagnitude <= 1;
                        Require((bool)type.GetField("NearAnyDanger").GetValue(candidates[i]) == expected,
                            "Spatial buckets must agree with true squared distance, independent of insertion order.");
                    }
                    for (int i = 0; i < 100; i++) Call(overlay, "CheckProfileStep");
                    Require((bool)type.GetField("IsSelected").GetValue(candidates[0]), "Local danger must survive blocked profile.");
                    Require((bool)type.GetField("IsSelected").GetValue(candidates[1]), "Contour OR must survive blocked profile.");
                    Require(!(bool)type.GetField("IsSelected").GetValue(candidates[2]), "Danger neighbor behind ridge must fail.");
                    Require(!(bool)type.GetField("IsSelected").GetValue(candidates[3]), "Remote flat candidate must stay hidden.");
                    Require((bool)type.GetField("IsSelected").GetValue(candidates[6]), "Clear danger neighborhood must qualify through condition 2.");
                    Require(overlay.ProfileCheckCount == 3, "Only neighborhood-only candidates need one profile each.");
                    Require(overlay.ProfileSampleVisitCount > 3, "Profiles must resume through multiple samples.");
                    MethodInfo pop = typeof(TraversalScanOverlayUI).GetMethod("PopSelectedCandidate", Private);
                    foreach (int expectedIndex in new[] { 5, 0, 1, 6 })
                        Require((int)pop.Invoke(overlay, null) == expectedIndex,
                            "Marker cap must prioritize danger, contour, then neighborhood, with distance ties.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void Add(IList list, Type type, Vector2 position, bool danger, bool contour)
        {
            object candidate = Activator.CreateInstance(type);
            type.GetField("MapPosition").SetValue(candidate, position);
            type.GetField("LocalDanger").SetValue(candidate, danger);
            type.GetField("NearContour").SetValue(candidate, contour);
            type.GetField("ProfileSampleCount").SetValue(candidate, 16);
            list.Add(candidate);
        }

        private static void CheckHeights(bool expected, params double[] heights)
        {
            var state = new TerrainScanPeakState(heights[0], 8);
            for (int i = 1; i < heights.Length; i++) state.Add(heights[i]);
            Require(state.Blocked == expected, "Peak/valley threshold example failed.");
        }

        private static TerrainScanProfile Profile(BakedHeightField field, Vector2 a, Vector2 b)
        {
            var profile = new TerrainScanProfile(field, a, b, 8, 128);
            int visits = 0;
            while (!profile.Complete && visits++ < 128) profile.Step();
            Require(profile.Complete, "Profile exceeded configured sample count or failed to terminate.");
            return profile;
        }

        private static BakedHeightField Field(int width, int height, float[] values, byte[] mask = null)
        {
            return (BakedHeightField)typeof(BakedHeightField).GetConstructors(Private)[0].Invoke(new object[]
            {
                width, height, new Vector2(width - 1, height - 1), 0f, 100f, 0f, 1f,
                values, values, values, mask, null, null, false
            });
        }

        private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
