using System;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.MainUI.Inventory
{
    [CreateAssetMenu(fileName = "InventoryRewards", menuName = "Animal Game/Inventory/Rewards")]
    public sealed class InventoryRewardTable : ScriptableObject
    {
        [Serializable]
        public struct Reward
        {
            public InventoryItem item;
            [Min(1)] public int quantity;
            [Min(0)] public float weight;
        }
        [Serializable]
        public sealed class Rule
        {
            public RecyclableSize size;
            public Reward[] guaranteed = Array.Empty<Reward>();
            [Min(0)] public int randomRolls = 1;
            public Reward[] random = Array.Empty<Reward>();
        }
        [SerializeField] private Rule[] rules = Array.Empty<Rule>();

        /// <summary>Overflow is intentionally ignored. Return value reports it to callers/tests.</summary>
        public InventoryStore.AddResult Grant(RecyclableSize size, InventoryStore inventory, Func<float> random01 = null)
        {
            int added = 0, remaining = 0;
            if (inventory == null) return new InventoryStore.AddResult(0, 0);
            random01 ??= () => UnityEngine.Random.value;
            foreach (Rule rule in rules)
            {
                if (rule.size != size) continue;
                foreach (Reward reward in rule.guaranteed) Add(reward);
                float total = 0;
                foreach (Reward reward in rule.random)
                    if (reward.item != null && reward.quantity > 0) total += Mathf.Max(0, reward.weight);
                for (int roll = 0; roll < rule.randomRolls && total > 0; roll++)
                {
                    float sample = Mathf.Clamp(random01(), 0, .999999f) * total;
                    foreach (Reward reward in rule.random)
                    {
                        if (reward.item == null || reward.quantity <= 0 || reward.weight <= 0) continue;
                        sample -= reward.weight;
                        if (sample < 0) { Add(reward); break; }
                    }
                }
                break;
            }
            return new InventoryStore.AddResult(added, remaining);

            void Add(Reward reward)
            {
                var result = inventory.Add(reward.item, reward.quantity);
                added += result.Added; remaining += result.Remaining;
            }
        }
    }
}
