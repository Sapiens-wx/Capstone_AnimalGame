using UnityEngine;

namespace AnimalGame.MainUI.Inventory
{
    [CreateAssetMenu(fileName = "InventoryItem", menuName = "Animal Game/Inventory/Item")]
    public class InventoryItem : ScriptableObject
    {
        [SerializeField] private string itemName;
        [SerializeField] private Sprite image;
        [SerializeField, Min(1)] private int maxStack = 1;

        public string ItemName => itemName;
        public Sprite Image => image;
        public int MaxStack => Mathf.Max(1, maxStack);
    }
}
