using System;
using System.Collections.Generic;
using AnimalGame.Save;
using UnityEngine;

namespace AnimalGame.Animals.Communication
{
    public enum Sender { Player, Npc }

    [Serializable]
    public sealed class ContactMessageNode
    {
        [SerializeField] private Sender sender;
        [SerializeField] private string message = "";
        [SerializeField] private List<int> children = new();
        [SerializeField] private Vector2 position;
        [SerializeField] private Vector2 size;

        public Sender Sender { get => sender; internal set => sender = value; }
        public string Message { get => message; internal set => message = value; }
        public IReadOnlyList<int> Children => children;
        public Vector2 Position { get => position; internal set => position = value; }
        public Vector2 Size { get => size; internal set => size = value; }

        internal bool HasChild(int id) => children.Contains(id);
        internal void AddChild(int id) => children.Add(id);
        internal void RemoveChild(int id) => children.Remove(id);
        internal void ReplaceChild(int oldId, int newId)
        {
            int index = children.IndexOf(oldId);
            if (index >= 0) children[index] = newId;
        }
    }

    [Serializable]
    public sealed class ContactHistoryDate
    {
        [SerializeField] private long gameTimeTicks;

        public DateTime GameTime
        {
            get => new DateTime(gameTimeTicks);
            internal set => gameTimeTicks = value.Ticks - value.Ticks % TimeSpan.TicksPerSecond;
        }
    }

    public sealed class ContactOptions : ScriptableObject
    {
        // Node IDs are one-based list indices. Zero denotes an unset entry.
        [SerializeField, HideInInspector] private List<ContactMessageNode> nodes = new();
        [SerializeField, HideInInspector] private int entryNodeId;
        [SerializeField, HideInInspector] private int historyEntryNodeId;
        [SerializeField, HideInInspector] private List<ContactHistoryDate> historyDates = new();

        public int NodeCount => nodes.Count;
        public int EntryNodeId => entryNodeId;
        public int HistoryEntryNodeId => historyEntryNodeId;
        public IReadOnlyList<ContactHistoryDate> HistoryDates => historyDates;
        public ContactMessageNode GetNode(int id) => nodes[id - 1];
        public bool ContainsNode(int id) => id > 0 && id <= nodes.Count;

        public int AddNode(Sender sender, Vector2 position)
        {
            nodes.Add(new ContactMessageNode { Sender = sender, Position = position });
            return nodes.Count;
        }

        public void SetEntryNode(int id)
        {
            if (id != 0) GetNode(id);
            entryNodeId = id;
        }

        public void SetHistoryEntryNode(int id)
        {
            if (id != 0) GetNode(id);
            historyEntryNodeId = id;
            if (entryNodeId == 0) entryNodeId = id;
        }

        public void UpdateHistory()
        {
            int count = 0;
            foreach (int id in GetHistoryNodeIds()) count++;
            if (historyDates.Count > count) historyDates.RemoveRange(count, historyDates.Count - count);
            while (historyDates.Count < count)
                historyDates.Add(new ContactHistoryDate { GameTime = GameSave.InitialGameTime });
        }

        public IEnumerable<int> GetHistoryNodeIds()
        {
            var visited = new HashSet<int>();
            int id = historyEntryNodeId;
            while (id != 0 && visited.Add(id))
            {
                yield return id;
                var children = GetNode(id).Children;
                if (id == entryNodeId || children.Count == 0) yield break;
                id = children[0];
            }
        }

        public void SetHistoryDate(int index, DateTime gameTime) => historyDates[index].GameTime = gameTime;

        public int GetCurrentNodeId(ContactConversation conversation)
        {
            if (conversation != null)
                for (int i = conversation.messages.Count - 1; i >= 0; i--)
                {
                    var message = conversation.messages[i];
                    if (message.kind == MessageKind.Text && ContainsNode(message.nodeId)) return message.nodeId;
                }

            int currentId = entryNodeId;
            foreach (int id in GetHistoryNodeIds()) currentId = id;
            return currentId;
        }

        public const int MaxReplies = 3;

        public IEnumerable<int> GetReplyNodeIds(int currentId)
        {
            if (currentId == 0) yield break;
            var children = GetNode(currentId).Children;
            if (children.Count == 0 || GetNode(children[0]).Sender != Sender.Player) yield break;
            for (int i = 0; i < Math.Min(MaxReplies, children.Count); i++)
                yield return children[i];
        }

        public bool CanConnect(int fromId, int toId)
        {
            var source = GetNode(fromId);
            return fromId != toId && ContainsNode(toId) && !source.HasChild(toId);
        }

        public bool TryConnect(int fromId, int toId)
        {
            if (!CanConnect(fromId, toId)) return false;
            GetNode(fromId).AddChild(toId);
            return true;
        }

        public void Disconnect(int fromId, int toId) => GetNode(fromId).RemoveChild(toId);

        public void RemoveNode(int id)
        {
            int lastId = nodes.Count;
            nodes[id - 1] = nodes[lastId - 1];
            nodes.RemoveAt(lastId - 1);

            foreach (var node in nodes)
            {
                node.RemoveChild(id);
                if (id != lastId) node.ReplaceChild(lastId, id);
            }

            entryNodeId = RemapReference(entryNodeId);
            historyEntryNodeId = RemapReference(historyEntryNodeId);

            int RemapReference(int reference) => reference == id ? 0 : reference == lastId ? id : reference;
        }

        public void SetSender(int id, Sender sender) => GetNode(id).Sender = sender;
        public void SetMessage(int id, string message) => GetNode(id).Message = message;
        public void SetPosition(int id, Vector2 position) => GetNode(id).Position = position;
        public void SetSize(int id, Vector2 size) => GetNode(id).Size = size;
    }
}
