using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    [PreferBinarySerialization]
    public sealed class OfficialMapWaterLayout : ScriptableObject
    {
        internal const string AssetPath = "Assets/Maps/Official_Map_01/Hydrology/Editor/OfficialWaterLayout.asset";
        internal const int OriginX = 500, OriginZ = 0, Width = 2501, Height = 3001, Step = 1;
        private const int BytesPerSample = sizeof(int) + sizeof(ushort);

        [SerializeField] private byte[] packedStages = Array.Empty<byte>();
        [NonSerialized] private Dictionary<int, ushort> stages;
        [NonSerialized] private bool dirty;

        internal static OfficialMapWaterLayout Load() => AssetDatabase.LoadAssetAtPath<OfficialMapWaterLayout>(AssetPath);

        internal static ushort EncodeStage(float meters) => (ushort)Mathf.Clamp(Mathf.RoundToInt(meters * 100f), 1, ushort.MaxValue);

        internal ushort ReadStage(int x, int z)
        {
            int index = Index(x, z);
            return index >= 0 && Stages.TryGetValue(index, out ushort stage) ? stage : (ushort)0;
        }

        internal bool SetStage(int x, int z, ushort stage)
        {
            int index = Index(x, z);
            if (index < 0) return false;
            Stages.TryGetValue(index, out ushort previous);
            if (previous == stage) return false;
            if (stage == 0) Stages.Remove(index);
            else Stages[index] = stage;
            dirty = true;
            return true;
        }

        internal int SampleCount => Stages.Count;

        internal void Commit(string undoName)
        {
            if (!dirty) return;
            using var stream = new MemoryStream(Stages.Count * BytesPerSample);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                foreach (int key in Stages.Keys.OrderBy(key => key))
                {
                    writer.Write(key);
                    writer.Write(Stages[key]);
                }
            }
            byte[] next = stream.ToArray();
            dirty = false;
            if (next.SequenceEqual(packedStages)) return;
            Undo.RegisterCompleteObjectUndo(this, undoName);
            packedStages = next;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        internal void Reload()
        {
            stages = null;
            dirty = false;
        }

        internal void Initialize(byte[] data)
        {
            var decoded = Decode(data);
            packedStages = data;
            stages = decoded;
            dirty = false;
        }

        private Dictionary<int, ushort> Stages => stages ??= Decode(packedStages);

        private static Dictionary<int, ushort> Decode(byte[] data)
        {
            if (data.Length % BytesPerSample != 0) throw new InvalidDataException("Invalid Official Map water layout length.");
            using var reader = new BinaryReader(new MemoryStream(data, false));
            var result = new Dictionary<int, ushort>(data.Length / BytesPerSample);
            int previous = -1;
            while (reader.BaseStream.Position < data.Length)
            {
                int index = reader.ReadInt32();
                ushort stage = reader.ReadUInt16();
                if (index <= previous || index >= Width * Height || stage == 0)
                    throw new InvalidDataException("Invalid Official Map water layout sample.");
                result.Add(index, stage);
                previous = index;
            }
            return result;
        }

        private static int Index(int x, int z)
        {
            x -= OriginX;
            z -= OriginZ;
            return (uint)x < Width && (uint)z < Height ? z * Width + x : -1;
        }
    }
}
