using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AnimalGame.Editor
{
    internal static class OfficialMapWaterBrush
    {
        private static readonly int ControlHint = "OfficialMapWaterBrush".GetHashCode();
        private static readonly string[] Modes = { "涂画", "擦除" };
        private static readonly Vector3[] Outline = new Vector3[41];
        private static readonly HashSet<Vector2Int> Visited = new HashSet<Vector2Int>();

        private static bool editing;
        private static bool eraseMode;
        private static float radius = 12f;
        private static float depth = .65f;
        private static SceneView strokeView;
        private static OfficialMapWaterLayout strokeLayout;
        private static int strokeControl;
        private static bool strokeErase;
        private static bool strokeChanged;
        private static float strokeRadius;
        private static float strokeDepth;
        private static bool hasLastPoint;
        private static Vector2 lastPoint;

        static OfficialMapWaterBrush()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => Editing = false;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Editing = false;
            };
            AssemblyReloadEvents.beforeAssemblyReload += FinishStroke;
        }

        internal static bool Editing
        {
            get => editing;
            set
            {
                if (editing == value) return;
                editing = value;
                if (editing) OfficialMapWaterPreview.Visible = true;
                else FinishStroke();
                SceneView.RepaintAll();
            }
        }

        internal static void DrawControls()
        {
            Editing = GUILayout.Toggle(Editing, "编辑水体范围");
            if (!Editing) return;

            EditorGUI.BeginChangeCheck();
            eraseMode = GUILayout.Toolbar(eraseMode ? 1 : 0, Modes) == 1;
            radius = DrawSlider("笔刷半径（米）", radius, 1f, 100f);
            using (new EditorGUI.DisabledScope(eraseMode))
                depth = DrawSlider("新涂水深（米）", depth, .05f, 3f);
            GUILayout.Label("左键拖画 · Shift 擦除 · Esc 退出", EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        internal static void Handle(SceneView view, Rect panelRect, Scene scene)
        {
            if (!Editing) return;
            if (!OfficialMapWaterPreview.IsAuthoringScene(scene) || EditorApplication.isPlayingOrWillChangePlaymode
                || !OfficialMapWaterPreview.Visible)
            {
                Editing = false;
                return;
            }

            Event current = Event.current;
            int control = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
            if (strokeLayout != null && strokeView != view) return;
            bool ownsStroke = strokeLayout != null;
            if (ownsStroke && (GUIUtility.hotControl != strokeControl || current.rawType == EventType.MouseLeaveWindow))
            {
                FinishStroke();
                return;
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                Editing = false;
                current.Use();
                return;
            }
            if (ownsStroke && current.type == EventType.KeyDown && (current.control || current.command)
                && (current.keyCode == KeyCode.Z || current.keyCode == KeyCode.Y))
            {
                FinishStroke();
                return;
            }

            bool navigation = current.alt || current.control || current.command;
            bool overPanel = panelRect.Contains(current.mousePosition);
            Vector3 point = default;
            bool hit = !navigation && !overPanel && OfficialMapWaterTerrain.TryHit(
                HandleUtility.GUIPointToWorldRay(current.mousePosition), out point);

            if (ownsStroke && current.rawType == EventType.MouseUp && current.button == 0)
            {
                if (hit) PaintTo(new Vector2(point.x, point.z));
                FinishStroke();
                if (current.type == EventType.MouseUp) current.Use();
                view.Repaint();
                return;
            }
            if (ownsStroke && navigation && current.rawType == EventType.MouseDrag)
            {
                FinishStroke();
                return;
            }
            if (current.type == EventType.MouseMove) view.Repaint();
            if (hit && current.type == EventType.Repaint)
                DrawOutline(point, ownsStroke ? strokeRadius : radius, ownsStroke ? strokeErase : eraseMode || current.shift);
            if (hit && current.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

            if (hit && !ownsStroke && current.type == EventType.MouseDown && current.button == 0
                && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == control)
            {
                BeginStroke(view, control, eraseMode || current.shift, new Vector2(point.x, point.z));
                current.Use();
                view.Repaint();
            }
            else if (ownsStroke && current.rawType == EventType.MouseDrag)
            {
                if (hit) PaintTo(new Vector2(point.x, point.z));
                else hasLastPoint = false;
                if (current.type == EventType.MouseDrag) current.Use();
                view.Repaint();
            }
        }

        private static float DrawSlider(string label, float value, float minimum, float maximum)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(94f));
                return EditorGUILayout.Slider(value, minimum, maximum);
            }
        }

        private static void BeginStroke(SceneView view, int control, bool erase, Vector2 point)
        {
            strokeLayout = OfficialMapWaterLayout.Load();
            strokeView = view;
            strokeControl = control;
            strokeErase = erase;
            strokeRadius = radius;
            strokeDepth = depth;
            GUIUtility.hotControl = control;
            PaintTo(point);
        }

        private static void PaintTo(Vector2 point)
        {
            if (!hasLastPoint)
            {
                PaintDab(point);
            }
            else
            {
                float spacing = Mathf.Max(.25f, strokeRadius * .25f);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(lastPoint, point) / spacing));
                for (int step = 1; step <= steps; step++)
                    PaintDab(Vector2.Lerp(lastPoint, point, step / (float)steps));
            }
            lastPoint = point;
            hasLastPoint = true;
        }

        private static void PaintDab(Vector2 point)
        {
            float radiusSquared = strokeRadius * strokeRadius;
            int minimumX = Mathf.CeilToInt(point.x - strokeRadius);
            int maximumX = Mathf.FloorToInt(point.x + strokeRadius);
            int minimumZ = Mathf.CeilToInt(point.y - strokeRadius);
            int maximumZ = Mathf.FloorToInt(point.y + strokeRadius);
            for (int z = minimumZ; z <= maximumZ; z++)
            {
                for (int x = minimumX; x <= maximumX; x++)
                {
                    if ((new Vector2(x, z) - point).sqrMagnitude > radiusSquared || !Visited.Add(new Vector2Int(x, z))) continue;
                    ushort stage = 0;
                    if (!strokeErase)
                    {
                        if (strokeLayout.ReadStage(x, z) != 0) continue;
                        if (!OfficialMapWaterTerrain.TryGround(x, z, out float ground)) continue;
                        stage = OfficialMapWaterLayout.EncodeStage(ground + strokeDepth);
                    }
                    if (!strokeLayout.SetStage(x, z, stage)) continue;
                    strokeChanged = true;
                    OfficialMapWaterPreview.MarkChanged(x, z);
                }
            }
        }

        private static void FinishStroke()
        {
            if (strokeLayout == null) return;
            OfficialMapWaterLayout layout = strokeLayout;
            bool changed = strokeChanged;
            string undoName = strokeErase ? "擦除正式地图水域" : "涂画正式地图水域";
            if (GUIUtility.hotControl == strokeControl) GUIUtility.hotControl = 0;
            strokeLayout = null;
            strokeView = null;
            strokeControl = 0;
            hasLastPoint = false;
            strokeChanged = false;
            Visited.Clear();
            if (changed)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(undoName);
                layout.Commit(undoName);
            }
        }

        private static void DrawOutline(Vector3 point, float brushRadius, bool erase)
        {
            for (int i = 0; i < Outline.Length; i++)
            {
                float angle = i * (Mathf.PI * 2f / (Outline.Length - 1));
                float x = point.x + Mathf.Cos(angle) * brushRadius;
                float z = point.z + Mathf.Sin(angle) * brushRadius;
                float y = OfficialMapWaterTerrain.TryGround(Mathf.RoundToInt(x), Mathf.RoundToInt(z), out float ground)
                    ? ground : point.y;
                Outline[i] = new Vector3(x, y + .15f, z);
            }
            CompareFunction depthTest = Handles.zTest;
            using (new Handles.DrawingScope(erase ? new Color(1f, .36f, .25f) : new Color(.55f, .9f, 1f)))
            {
                Handles.zTest = CompareFunction.Always;
                Handles.DrawAAPolyLine(2f, Outline);
                Handles.zTest = depthTest;
            }
        }
    }
}
