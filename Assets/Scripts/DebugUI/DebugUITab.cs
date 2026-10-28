using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.AnimalDebug
{
    // Add a component derived from this class beside DebugUI to register another tab.
    public abstract class DebugUITab : MonoBehaviour
    {
        public abstract string Title { get; }
        public abstract VisualElement Content { get; }
        public abstract void Bind(VisualElement root);
        public abstract void Show();
        public abstract void Hide();
        public abstract bool Back();
        public abstract void HandleInput(int move, bool submit, bool click, Vector2 mouse);
    }
}
