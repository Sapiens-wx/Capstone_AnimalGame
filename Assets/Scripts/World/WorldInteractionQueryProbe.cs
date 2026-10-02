#if UNITY_EDITOR
using AnimalGame.MapTest;
using UnityEngine;

namespace AnimalGame.World
{
    // Regression instrumentation: count actual shape reads, not tree implementation details.
    [AddComponentMenu("")]
    public sealed class WorldInteractionQueryProbe : WorldInteraction
    {
        public int ShapeReads;
        // SPATIAL INDEX: tests deliberately use the manual notification contract here.
        public bool UseCustomShape;
        public InteractionShape CustomShape;

        public override InteractionShape GetShape(MapTestSceneController map)
        {
            ShapeReads++;
            return UseCustomShape ? CustomShape : base.GetShape(map);
        }
    }
}
#endif
