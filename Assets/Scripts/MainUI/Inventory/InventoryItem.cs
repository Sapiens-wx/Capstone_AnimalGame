using UnityEngine;

namespace AnimalGame.MainUI.Inventory
{
    [CreateAssetMenu(fileName = "InventoryItem", menuName = "Animal Game/Inventory/Item")]
    public class InventoryItem : ScriptableObject
    {
        [SerializeField] private string itemName;
        [SerializeField] private Sprite image;

        public string ItemName => itemName;
        public Sprite Image => image;
    }
}
