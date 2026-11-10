using UnityEngine;
using UnityEngine.UIElements;

namespace AnimalGame.Animals.UI
{
    public sealed class ArchiveColumnWave
    {
        private const float Period = 4.8f;
        private const float Amplitude = 4f;
        private readonly VisualElement grid;
        private readonly int columns;
        private readonly float verticalOffset;
        private readonly IVisualElementScheduledItem animation;

        public ArchiveColumnWave(VisualElement grid, int columns, float verticalOffset = 0f)
        {
            this.grid = grid;
            this.columns = columns;
            this.verticalOffset = verticalOffset;
            animation = grid.schedule.Execute(Animate).Every(16);
            animation.Pause();
            grid.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
        }

        public void Start() { Animate(); animation.Resume(); }
        public void Stop() => animation.Pause();

        private void Animate()
        {
            float phase = (Time.unscaledTime % Period) * (Mathf.PI * 2f / Period);
            for (int i = 0; i < grid.childCount; i++)
            {
                float offset = Mathf.Sin(phase - (i % columns) * Mathf.PI * 2f / columns) * Amplitude;
                grid[i].style.translate = new Translate(0, verticalOffset + offset);
            }
        }
    }
}
