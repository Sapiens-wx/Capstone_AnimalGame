using UnityEngine;

namespace AnimalGame.MainUI.Inventory
{
    [CreateAssetMenu(fileName = "InventoryItem", menuName = "Animal Game/Inventory/Item")]
    public class InventoryItem : ScriptableObject
    {
        [SerializeField] private string itemName;
        [SerializeField] private Sprite image;
        [SerializeField, TextArea(3, 12)] private string info;
        [SerializeField, Min(1)] private int maxStack = 1;

        public string ItemName => itemName;
        public Sprite Image => image;
        public string Info => info;
        public int MaxStack => Mathf.Max(1, maxStack);
    }
}
