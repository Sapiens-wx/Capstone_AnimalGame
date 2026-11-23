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
        [SerializeField] private int nodeId;
        [SerializeField] private long gameTimeTicks;

        public int NodeId { get => nodeId; internal set => nodeId = value; }
        public DateTime GameTime
        {
            get => new DateTime(gameTimeTicks);
            internal set => gameTimeTicks = value.Ticks - value.Ticks % TimeSpan.TicksPerMinute;
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
            var previousDates = new Dictionary<int, ContactHistoryDate>();
            foreach (var date in historyDates) previousDates.Add(date.NodeId, date);

            var visited = new HashSet<int>();
            var updatedDates = new List<ContactHistoryDate>();
            int id = historyEntryNodeId;
            while (id != 0 && visited.Add(id))
            {
                AddDate(id);
                var node = GetNode(id);
                if (node.Children.Count == 0 || node.HasChild(entryNodeId)) break;
                id = node.Children[0];
            }
            if (entryNodeId != 0 && !visited.Contains(entryNodeId)) AddDate(entryNodeId);
            historyDates = updatedDates;

            void AddDate(int nodeId)
            {
                updatedDates.Add(previousDates.TryGetValue(nodeId, out var date)
                    ? date
                    : new ContactHistoryDate { NodeId = nodeId, GameTime = GameSave.InitialGameTime });
            }
        }

        public void SetHistoryDate(int nodeId, DateTime gameTime)
        {
            historyDates.Find(date => date.NodeId == nodeId).GameTime = gameTime;
        }

        public bool CanConnect(int fromId, int toId)
        {
            var source = GetNode(fromId);
            return fromId != toId && source.Sender != GetNode(toId).Sender
                && !source.HasChild(toId);
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
            historyDates.RemoveAll(date => date.NodeId == id);
            foreach (var date in historyDates) date.NodeId = RemapReference(date.NodeId);

            int RemapReference(int reference) => reference == id ? 0 : reference == lastId ? id : reference;
        }

        public bool CanSetSender(int id, Sender sender)
        {
            var node = GetNode(id);
            return sender == node.Sender
                || (node.Children.Count == 0 && !nodes.Exists(parent => parent.HasChild(id)));
        }

        public bool TrySetSender(int id, Sender sender)
        {
            if (!CanSetSender(id, sender)) return false;
            GetNode(id).Sender = sender;
            return true;
        }

        public void SetMessage(int id, string message) => GetNode(id).Message = message;
        public void SetPosition(int id, Vector2 position) => GetNode(id).Position = position;
        public void SetSize(int id, Vector2 size) => GetNode(id).Size = size;
    }
}
