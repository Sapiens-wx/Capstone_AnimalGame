using System.Collections.Generic;
using AnimalGame.MapTest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AnimalGame.World
{
    // One shared tree for every kind in the current scene/map query context.
    // Switching contexts rebuilds it; coordinates from different maps never mix.
    internal sealed class WorldInteractionQuadTree
    {
        internal interface IVisitor
        {
            // Return true to stop traversal immediately.
            bool Visit(WorldInteraction item, InteractionShape shape);
        }

        private const int Capacity = 12;
        private const int MaxDepth = 10;
        private const float MinCellSize = .5f;
        private sealed class Entry
        {
            public WorldInteraction Item;
            public InteractionShape Shape;
            public Rect Bounds;
            public WorldInteractionKind Kind;
            public Node Node;
        }
        private sealed class Node
        {
            public Rect Bounds;
            public Node Parent;
            public int Depth;
            public WorldInteractionKind KindMask;
            public readonly List<Entry> Items = new();
            public Node[] Children;
        }

        private readonly Dictionary<WorldInteraction, Entry> entries = new();
        // OnValidate may enqueue from Unity's loading thread. Geometry is only read
        // on the main thread when a query flushes this queue.
        private readonly object dirtyLock = new();
        private readonly HashSet<WorldInteraction> dirty = new();
        private readonly List<WorldInteraction> pending = new();
        private volatile bool needsFullRefresh = true;
        private Node root;
        private Scene scene;
        private MapTestSceneController map;

        internal static Rect BoundsOf(InteractionShape shape)
        {
            Vector2 min = Vector2.Min(shape.A, shape.B), max = Vector2.Max(shape.A, shape.B);
            if (shape.IsBox)
            {
                min = Vector2.Min(min, Vector2.Min(shape.C, shape.D));
                max = Vector2.Max(max, Vector2.Max(shape.C, shape.D));
            }
            else
            {
                min -= Vector2.one * shape.Radius;
                max += Vector2.one * shape.Radius;
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool Contains(Rect outer, Rect inner) =>
            outer.xMin <= inner.xMin && outer.yMin <= inner.yMin &&
            outer.xMax >= inner.xMax && outer.yMax >= inner.yMax;

        // Rect.Overlaps excludes touching edges; Query treats contact and points as hits.
        private static bool Overlaps(Rect a, Rect b) =>
            a.xMin <= b.xMax && a.xMax >= b.xMin && a.yMin <= b.yMax && a.yMax >= b.yMin;

        internal void Clear()
        {
            entries.Clear();
            lock (dirtyLock) dirty.Clear();
            pending.Clear();
            needsFullRefresh = true;
            root = null;
            map = null;
            scene = default;
        }

        internal void Remove(WorldInteraction item)
        {
            lock (dirtyLock) dirty.Remove(item);
            if (!entries.TryGetValue(item, out Entry entry)) return;
            Detach(entry);
            entries.Remove(item);
            if (entries.Count == 0) root = null;
        }

        internal void MarkDirty(WorldInteraction item)
        {
            lock (dirtyLock) dirty.Add(item);
        }

        internal void Invalidate() => needsFullRefresh = true;

        // No movement polling. Only initial/context/global invalidation enumerates
        // Active; ordinary queries read geometry for explicitly dirty entries only.
        internal void FlushPending(MapTestSceneController queryMap, Scene queryScene)
        {
            if (needsFullRefresh || scene != queryScene || map != queryMap)
            {
                Clear();
                scene = queryScene;
                map = queryMap;
                needsFullRefresh = false;
                foreach (WorldInteraction item in WorldInteraction.Active)
                    if (item != null && item.gameObject.scene == queryScene) MarkDirty(item);
            }
            lock (dirtyLock)
            {
                pending.AddRange(dirty);
                dirty.Clear();
            }
            bool rebuild = root == null;
            foreach (WorldInteraction item in pending)
            {
                if (item == null || !item.isActiveAndEnabled || item.gameObject.scene != queryScene)
                {
                    if (!ReferenceEquals(item, null)) Remove(item);
                    continue;
                }
                InteractionShape shape = item.GetShape(queryMap);
                Rect bounds = BoundsOf(shape);
                WorldInteractionKind kind = item.Kind;
                if (!entries.TryGetValue(item, out Entry entry))
                {
                    entry = new Entry { Item = item, Bounds = bounds, Kind = kind };
                    entries.Add(item, entry);
                }
                else if (!entry.Bounds.Equals(bounds) || entry.Kind != kind)
                {
                    Detach(entry);
                    entry.Bounds = bounds;
                    entry.Kind = kind;
                }
                entry.Shape = shape;
                if (root == null || !Contains(root.Bounds, bounds)) rebuild = true;
                if (!rebuild && entry.Node == null) Insert(root, entry);
            }
            pending.Clear();
            if (rebuild) Rebuild();
        }

        private void Rebuild()
        {
            if (entries.Count == 0) { root = null; return; }
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            foreach (Entry entry in entries.Values)
            {
                min = Vector2.Min(min, entry.Bounds.min);
                max = Vector2.Max(max, entry.Bounds.max);
            }
            if (map != null && map.HasGeneratedMap)
            {
                min = Vector2.Min(min, InteractionShape.ToQuery(map.WorldBounds.min, map));
                max = Vector2.Max(max, InteractionShape.ToQuery(map.WorldBounds.max, map));
            }
            // Square root with slack so modest movement near its edge does not rebuild.
            float side = Mathf.Max(1f, Mathf.Max(max.x - min.x, max.y - min.y)) * 1.25f;
            root = new Node { Bounds = new Rect((min + max) * .5f - Vector2.one * side * .5f, Vector2.one * side) };
            foreach (Entry entry in entries.Values) Insert(root, entry);
        }

        private static void Insert(Node node, Entry entry)
        {
            node.KindMask |= entry.Kind;
            if (node.Children != null)
            {
                foreach (Node child in node.Children)
                    if (Contains(child.Bounds, entry.Bounds)) { Insert(child, entry); return; }
            }
            node.Items.Add(entry);
            entry.Node = node;
            if (node.Children != null || node.Items.Count <= Capacity || node.Depth >= MaxDepth ||
                node.Bounds.width * .5f < MinCellSize) return;
            node.Children = new Node[4];
            float half = node.Bounds.width * .5f;
            for (int i = 0; i < 4; i++)
                node.Children[i] = new Node { Parent = node, Depth = node.Depth + 1,
                    Bounds = new Rect(node.Bounds.xMin + (i % 2) * half,
                        node.Bounds.yMin + (i / 2) * half, half, half) };
            // Crossing objects remain at the parent, stored exactly once.
            for (int i = node.Items.Count - 1; i >= 0; i--)
            {
                Entry existing = node.Items[i];
                foreach (Node child in node.Children)
                {
                    if (!Contains(child.Bounds, existing.Bounds)) continue;
                    node.Items.RemoveAt(i);
                    Insert(child, existing);
                    break;
                }
            }
        }

        private static void Detach(Entry entry)
        {
            Node node = entry.Node;
            if (node == null) return;
            node.Items.Remove(entry);
            entry.Node = null;
            for (; node != null; node = node.Parent)
            {
                WorldInteractionKind mask = WorldInteractionKind.None;
                foreach (Entry remaining in node.Items) mask |= remaining.Kind;
                if (node.Children != null)
                {
                    bool empty = true;
                    foreach (Node child in node.Children)
                    {
                        mask |= child.KindMask;
                        if (child.Items.Count != 0 || child.Children != null) empty = false;
                    }
                    if (empty) node.Children = null;
                }
                node.KindMask = mask;
            }
        }

        internal void Query<T>(Rect bounds, WorldInteractionKind mask, ref T visitor) where T : struct, IVisitor
        {
            if (root != null) Visit(root, bounds, mask, ref visitor);
        }

        private static bool Visit<T>(Node node, Rect bounds, WorldInteractionKind mask, ref T visitor)
            where T : struct, IVisitor
        {
            if ((node.KindMask & mask) == 0 || !Overlaps(node.Bounds, bounds)) return false;
            foreach (Entry entry in node.Items)
                if ((entry.Kind & mask) != 0 && Overlaps(entry.Bounds, bounds) && visitor.Visit(entry.Item, entry.Shape))
                    return true;
            if (node.Children != null)
                foreach (Node child in node.Children)
                    if (Visit(child, bounds, mask, ref visitor)) return true;
            return false;
        }
    }
}
