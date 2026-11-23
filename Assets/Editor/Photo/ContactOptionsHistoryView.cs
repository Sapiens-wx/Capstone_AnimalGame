using System;
using System.Globalization;
using AnimalGame.Animals.Communication;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Editor
{
    internal sealed class ContactOptionsHistoryView : VisualElement
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm";

        private readonly Label contactName = new();
        private readonly Button update;
        private readonly ScrollView rows = new();
        private ContactOptions options;

        public ContactOptionsHistoryView()
        {
            style.flexGrow = 1;
            contactName.style.unityFontStyleAndWeight = FontStyle.Bold;
            Add(contactName);
            update = new Button(UpdateHistory) { text = "Update History" };
            update.SetEnabled(false);
            Add(update);
            rows.style.flexGrow = 1;
            Add(rows);
        }

        public void Load(string name, ContactOptions asset)
        {
            contactName.text = name;
            options = asset;
            update.SetEnabled(true);
            Refresh();
        }

        public void Refresh()
        {
            rows.Clear();
            if (options == null) return;
            if (options.HistoryEntryNodeId == 0)
                rows.Add(new Label("Set a history entry node.") { style = { whiteSpace = WhiteSpace.Normal } });
            foreach (var date in options.HistoryDates)
                AddDateRow(options, date);
        }

        private void AddDateRow(ContactOptions asset, ContactHistoryDate date)
        {
            var row = new VisualElement();
            row.style.marginBottom = 6;
            var dateLine = new VisualElement();
            dateLine.style.flexDirection = FlexDirection.Row;
            dateLine.style.alignItems = Align.Center;
            row.Add(dateLine);

            var title = new Label($"#{date.NodeId}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexShrink = 0;
            title.style.marginRight = 3;
            dateLine.Add(title);

            var field = new TextField { isDelayed = true, tooltip = DateFormat };
            field.style.flexGrow = 1;
            field.style.minWidth = 0;
            field.SetValueWithoutNotify(Format(date.GameTime));
            dateLine.Add(field);

            var error = new Label($"Use {DateFormat}.");
            error.style.whiteSpace = WhiteSpace.Normal;
            error.style.display = DisplayStyle.None;
            row.Add(error);
            field.RegisterValueChangedCallback(evt =>
            {
                if (!DateTime.TryParseExact(evt.newValue, DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var time))
                {
                    field.SetValueWithoutNotify(Format(date.GameTime));
                    error.style.display = DisplayStyle.Flex;
                    return;
                }
                error.style.display = DisplayStyle.None;
                if (time == date.GameTime) return;
                Undo.RecordObject(asset, "Edit Contact History Date");
                asset.SetHistoryDate(date.NodeId, time);
                Save(asset);
            });
            rows.Add(row);
        }

        private void UpdateHistory()
        {
            Undo.RecordObject(options, "Update Contact History");
            options.UpdateHistory();
            Save(options);
            Refresh();
        }

        private static string Format(DateTime time) => time.ToString(DateFormat, CultureInfo.InvariantCulture);

        private static void Save(ContactOptions asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }
}
