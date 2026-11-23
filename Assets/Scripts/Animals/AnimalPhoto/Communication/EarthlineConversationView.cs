using System;
using System.Collections.Generic;
using System.Globalization;
using AnimalGame.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.Communication
{
    public sealed class EarthlineConversationView : IDisposable
    {
        private const float ScrollSpeed = 1000f;
        private readonly VisualElement root, messages;
        private readonly ScrollView scroll;
        private readonly List<RenderTexture> thumbnails = new();
        private readonly List<(VisualElement frame, VisualElement grid, int count)> batches = new();
        public EarthlineConversationView(VisualElement root)
        {
            this.root = root; messages = root.Q("chat-messages"); scroll = root.Q<ScrollView>("chat-scroll");
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => ResizePhotos());
            scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => scroll.schedule.Execute(ScrollToBottom));
        }
        public bool AtBottom => scroll.verticalScroller.highValue <= 0 || scroll.scrollOffset.y >= scroll.verticalScroller.highValue - 2;
        public void Scroll(float direction)
        {
            float next = Mathf.Clamp(scroll.scrollOffset.y - direction * ScrollSpeed * Time.unscaledDeltaTime,
                0, Mathf.Max(0, scroll.verticalScroller.highValue));
            scroll.scrollOffset = new Vector2(0, next);
        }
        public void Focus(bool focused) => scroll.EnableInClassList("chat-focused", focused);

        private void ScrollToBottom() =>
            scroll.scrollOffset = new Vector2(0, Mathf.Max(0, scroll.verticalScroller.highValue));

        public void Render(GameSave save, ContactDefinition contact, DateTime time)
        {
            Clear();
            root.Q<Label>("conversation-name").text = contact.Name;
            root.Q<Label>("conversation-profession").text = contact.profession;
            root.Q<Label>("conversation-date").text = time.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
            var options = contact.options;
            if (options != null)
            {
                int index = 0;
                foreach (int id in options.GetHistoryNodeIds())
                {
                    DateTime sentAt = index < options.HistoryDates.Count
                        ? options.HistoryDates[index].GameTime : GameSave.InitialGameTime;
                    AddTextMessage(contact, options.GetNode(id), sentAt);
                    index++;
                }
            }
            var conversation = save.conversations.Find(c => c.contact == contact.contact);
            if (conversation != null)
                foreach (var message in conversation.messages)
                {
                    if (message.kind == MessageKind.Photos) AddPhotoMessage(save, contact, message);
                    else if (options != null && options.ContainsNode(message.nodeId))
                        AddTextMessage(contact, options.GetNode(message.nodeId), message.sentAt);
                }
            if (messages.childCount == 0)
            {
                var empty = new Label("No messages yet. Send a reply or share a photograph."); empty.AddToClassList("message-empty"); messages.Add(empty);
            }
            ResizePhotos();
            scroll.schedule.Execute(() => { ResizePhotos(); ScrollToBottom(); });
        }

        private VisualElement AddMessage(ContactDefinition contact, bool fromPlayer, string time)
        {
            var item = new VisualElement(); item.AddToClassList("message"); item.EnableInClassList("player", fromPlayer);
            var header = new VisualElement(); header.AddToClassList("message-header");
            if (!fromPlayer) header.Add(new Label(contact.Name));
            header.Add(new Label(time)); item.Add(header);
            messages.Add(item);
            return item;
        }

        private void AddTextMessage(ContactDefinition contact, ContactMessageNode node, DateTime sentAt)
        {
            var item = AddMessage(contact, node.Sender == Sender.Player,
                sentAt.ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture));
            var text = new Label(node.Message) { enableRichText = false };
            text.AddToClassList("message-body");
            item.Add(text);
        }

        private void AddPhotoMessage(GameSave save, ContactDefinition contact, ChatMessage message)
        {
            var item = AddMessage(contact, message.fromPlayer, $"{message.hour:00}:{message.minute:00}");
            var frame = new VisualElement(); frame.AddToClassList("message-photos");
            var grid = new VisualElement(); grid.AddToClassList("message-photo-grid"); frame.Add(grid);
            foreach (int index in message.photoIndices)
            {
                var texture = save.photos[index].RestorePhoto().Render(192); thumbnails.Add(texture);
                var photo = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit }; photo.AddToClassList("message-photo"); grid.Add(photo);
            }
            var count = new Label(message.photoIndices.Length == 0 ? "Photos removed from album" : $"{message.photoIndices.Length} photos");
            count.AddToClassList("message-photo-count"); frame.Add(count);
            foreach (string side in new[] { "top", "bottom" })
            {
                var corner = new VisualElement { pickingMode = PickingMode.Ignore }; corner.AddToClassList("photo-corner"); corner.AddToClassList(side); frame.Add(corner);
            }
            item.Add(frame); batches.Add((frame, grid, message.photoIndices.Length));
        }

        private void ResizePhotos()
        {
            float width = scroll.contentViewport.layout.width;
            if (!float.IsFinite(width) || width <= 0) return;
            float side = Mathf.Max(1, Mathf.Min(164, (width - 60) / 5 - 6));
            foreach (var batch in batches)
            {
                batch.grid.style.width = Mathf.Max(1, Math.Min(5, batch.count)) * (side + 6);
                foreach (var image in batch.grid.Children()) image.style.width = image.style.height = side;
            }
        }
        public void Clear()
        {
            messages.Clear(); batches.Clear();
            foreach (var texture in thumbnails) AnimalPhotoProcessing.Release(texture);
            thumbnails.Clear();
        }
        public void Dispose() => Clear();
    }
}
