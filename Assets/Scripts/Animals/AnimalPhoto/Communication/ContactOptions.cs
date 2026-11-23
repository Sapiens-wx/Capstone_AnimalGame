using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimalGame.Animals.Communication
{
    public enum Sender { Player, Npc }

    [Serializable]
    public sealed class ContactMessageNode
    {
        [SerializeField] private Sender sender;
        [SerializeField] private string message = "";
        [SerializeField] private int parent;
        [SerializeField] private List<int> children = new();
        [SerializeField] private Vector2 position;
        [SerializeField] private Vector2 size;

        public Sender Sender { get => sender; internal set => sender = value; }
        public string Message { get => message; internal set => message = value; }
        public int Parent { get => parent; internal set => parent = value; }
        public IReadOnlyList<int> Children => children;
        public Vector2 Position { get => position; internal set => position = value; }
        public Vector2 Size { get => size; internal set => size = value; }

        internal void AddChild(int id) => children.Add(id);
        internal void RemoveChild(int id) => children.Remove(id);
        internal void ReplaceChild(int oldId, int newId) => children[children.IndexOf(oldId)] = newId;
    }

    public sealed class ContactOptions : ScriptableObject
    {
        // Node IDs are one-based list indices. Zero denotes an unset entry or parent.
        [SerializeField, HideInInspector] private List<ContactMessageNode> nodes = new();
        [SerializeField, HideInInspector] private int entryNodeId;

        public int NodeCount => nodes.Count;
        public int EntryNodeId => entryNodeId;
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

        public bool CanConnect(int fromId, int toId)
        {
            return fromId != toId && GetNode(toId).Parent == 0
                && GetNode(fromId).Sender != GetNode(toId).Sender;
        }

        public bool TryConnect(int fromId, int toId)
        {
            if (!CanConnect(fromId, toId)) return false;
            GetNode(fromId).AddChild(toId);
            GetNode(toId).Parent = fromId;
            return true;
        }

        public void Disconnect(int fromId, int toId)
        {
            var child = GetNode(toId);
            if (child.Parent != fromId) return;
            GetNode(fromId).RemoveChild(toId);
            child.Parent = 0;
        }

        public void RemoveNode(int id)
        {
            var removed = GetNode(id);
            if (removed.Parent != 0) GetNode(removed.Parent).RemoveChild(id);
            foreach (int child in removed.Children) GetNode(child).Parent = 0;
            if (entryNodeId == id) entryNodeId = 0;

            int lastId = nodes.Count;
            if (id != lastId)
            {
                var moved = GetNode(lastId);
                nodes[id - 1] = moved;
                if (moved.Parent != 0) GetNode(moved.Parent).ReplaceChild(lastId, id);
                foreach (int child in moved.Children) GetNode(child).Parent = id;
                if (entryNodeId == lastId) entryNodeId = id;
            }
            nodes.RemoveAt(lastId - 1);
        }

        public bool CanSetSender(int id, Sender sender)
        {
            var node = GetNode(id);
            return sender == node.Sender || (node.Parent == 0 && node.Children.Count == 0);
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
