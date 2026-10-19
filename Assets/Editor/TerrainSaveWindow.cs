using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    public sealed class TerrainSaveWindow : EditorWindow
    {
        [SerializeField] private GameObject target;
        [SerializeField] private string saveName = "";
        [SerializeField] private string selectedId;
        [SerializeField] private bool followSelection = true;
        private List<TerrainSaveRecord> records = new List<TerrainSaveRecord>();
        private Vector2 scroll;
        private string status, targetIssue;
        private MessageType statusType = MessageType.Info;

        [MenuItem("Animal Game/Terrain Saves")]
        public static void Open()
        {
            TerrainSaveWindow window = GetWindow<TerrainSaveWindow>("Terrain 存档");
            window.minSize = new Vector2(560, 620);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            float width = Mathf.Min(600f, main.width - 40f);
            float height = Mathf.Min(700f, main.height - 60f);
            window.position = new Rect(main.center.x - width * 0.5f, main.center.y - height * 0.5f, width, height);
            if (TerrainSaveStore.GetTargets(Selection.activeGameObject).Length > 0) window.target = Selection.activeGameObject;
            window.RefreshSaves();
            window.Show();
        }

        [MenuItem("CONTEXT/Terrain/Terrain 存档")]
        private static void OpenContext(MenuCommand command)
        {
            Terrain terrain = command.context as Terrain;
            Open();
            GetWindow<TerrainSaveWindow>().SetTarget(terrain != null ? terrain.gameObject : null);
        }

        private void OnEnable() => RefreshSaves();
        private void OnSelectionChange()
        {
            if (followSelection && TerrainSaveStore.GetTargets(Selection.activeGameObject).Length > 0)
                SetTarget(Selection.activeGameObject);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Terrain 命名存档", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("选目标 → 命名保存 → 从列表选择恢复", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            GameObject next = (GameObject)EditorGUILayout.ObjectField("目标 / 地形组根物体", target, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) SetTarget(next);
            using (new EditorGUILayout.HorizontalScope())
            {
                followSelection = EditorGUILayout.ToggleLeft("跟随 Hierarchy 选择", followSelection);
                if (GUILayout.Button("使用当前选择", GUILayout.Width(115))) SetTarget(Selection.activeGameObject);
            }

            Terrain[] targets = TerrainSaveStore.GetTargets(target);
            int dataCount = targets.Where(terrain => terrain.terrainData != null).Select(terrain => terrain.terrainData).Distinct().Count();
            if (!string.IsNullOrEmpty(targetIssue)) EditorGUILayout.HelpBox(targetIssue, MessageType.Info);
            else
            {
                EditorGUILayout.LabelField($"目标：{targets.Length} 块 Terrain / {dataCount} 份地形数据");
                int shared = CountSharedReferences(targets);
                if (shared > 0) EditorGUILayout.HelpBox($"另有 {shared} 块已加载的 Terrain 共用这些地形数据，恢复时也会同步变化。", MessageType.Info);
            }
            EditorGUILayout.HelpBox("完整 TerrainData：高度、洞、地表权重、树/草分布及尺寸。\n场景位置、外部材质和贴图资源不随存档回退。", MessageType.None);
            bool writable = !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating && string.IsNullOrEmpty(targetIssue);
            EditorGUILayout.Space(6);
            saveName = EditorGUILayout.TextField("新存档名称", saveName, GUILayout.MinWidth(200));
            using (new EditorGUI.DisabledScope(!writable || string.IsNullOrWhiteSpace(saveName)))
                if (GUILayout.Button("保存当前 Terrain 为新存档", GUILayout.Height(30)))
                    Execute(() =>
                    {
                        TerrainSaveRecord saved = TerrainSaveStore.Save(targets, saveName);
                        selectedId = saved.id;
                        return "已保存：" + saved.label;
                    });

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"可恢复的存档（{records.Count}）", EditorStyles.boldLabel);
                if (GUILayout.Button("刷新", GUILayout.Width(60))) RefreshSaves();
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(145));
            if (records.Count == 0)
                EditorGUILayout.LabelField("这个目标还没有存档。保存后会出现在这里。", EditorStyles.wordWrappedLabel);
            foreach (TerrainSaveRecord record in records)
            {
                string time = DateTime.TryParse(record.createdUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime date)
                    ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : record.createdUtc;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Toggle(selectedId == record.id, (record.automatic ? "[自动] " : "") + record.label, EditorStyles.radioButton))
                        selectedId = record.id;
                    double mib = record.entries.Sum(entry => (double)entry.bytes) / 1048576d;
                    EditorGUILayout.LabelField($"{time}  ·  {record.entries.Length} 份数据  ·  {mib:F1} MB", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            TerrainSaveRecord selected = records.FirstOrDefault(record => record.id == selectedId);
            if (selected != null && selected.entries.Length == 1)
            {
                TerrainSaveEntry entry = selected.entries[0];
                EditorGUILayout.LabelField($"存档尺寸：{entry.size.x:g} × {entry.size.z:g} m；高度范围 {entry.size.y:g} m；分辨率 {entry.resolution}²", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.HelpBox("恢复会直接覆盖当前 Terrain，不会自动备份；如需保留，请先手动存档。\n连续地形建议选整个地形组一起存取，保持区块边缘一致。", MessageType.None);
            using (new EditorGUI.DisabledScope(!writable || selected == null))
                if (GUILayout.Button("恢复选中的存档", GUILayout.Height(32)))
                    Execute(() =>
                    {
                        TerrainSaveStore.Restore(targets, selected);
                        return "已恢复：" + selected.label + "。";
                    });
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("存档保存在本地 LocalBackups/TerrainSaves，不提交到 Git。", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("打开本地存档目录"))
            {
                Directory.CreateDirectory(TerrainSaveStore.RootFolder);
                EditorUtility.RevealInFinder(TerrainSaveStore.RootFolder);
            }
            EditorGUILayout.Space(6);
        }

        private void SetTarget(GameObject value)
        {
            target = value;
            selectedId = null;
            status = null;
            RefreshSaves();
            Repaint();
        }

        private void RefreshSaves()
        {
            try
            {
                records = TerrainSaveStore.List(TerrainSaveStore.GetTargets(target));
                targetIssue = null;
                if (!records.Any(record => record.id == selectedId)) selectedId = records.FirstOrDefault()?.id;
            }
            catch (Exception error) { targetIssue = error.Message; records = new List<TerrainSaveRecord>(); }
        }

        private void Execute(Func<string> operation)
        {
            try { status = operation(); statusType = MessageType.Info; }
            catch (Exception error) { status = error.Message; statusType = MessageType.Error; Debug.LogException(error); }
            RefreshSaves();
            Repaint();
        }

        private static int CountSharedReferences(Terrain[] targets)
        {
            var data = new HashSet<TerrainData>(targets.Where(terrain => terrain.terrainData != null).Select(terrain => terrain.terrainData));
            return Resources.FindObjectsOfTypeAll<Terrain>().Count(terrain => terrain.gameObject.scene.IsValid()
                && terrain.gameObject.scene.isLoaded && !targets.Contains(terrain) && data.Contains(terrain.terrainData));
        }
    }
}
