using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimalGame.MapTest
{
    // The flyover stays in the same undiscovered snow field as gameplay animals.
    internal sealed class CommunicationsRavenVisual : IDisposable
    {
        private static readonly int Seed = Shader.PropertyToID("_Seed");
        private static readonly int RevealProgress = Shader.PropertyToID("_RevealProgress");
        private static readonly int AnimationPhase = Shader.PropertyToID("_AnimationPhase");
        private static readonly int ShapePhase = Shader.PropertyToID("_ShapePhase");

        private readonly SpriteRenderer bird;
        private readonly Vector3 unitLocalScale;
        private readonly MaterialPropertyBlock properties = new();

        internal CommunicationsRavenVisual(Transform parent, SpriteRenderer unknownAppearance)
        {
            var view = new GameObject("Communications Raven") { hideFlags = HideFlags.DontSave };
            view.transform.SetParent(parent, true);
            bird = view.AddComponent<SpriteRenderer>();
            bird.sprite = unknownAppearance.sprite;
            bird.sharedMaterial = unknownAppearance.sharedMaterial;
            bird.color = unknownAppearance.color;
            bird.sortingLayerID = unknownAppearance.sortingLayerID;
            bird.sortingOrder = 1160;
            unitLocalScale = view.transform.localScale / bird.sprite.bounds.size.x;

            properties.SetFloat(Seed, 6.74f);
            properties.SetFloat(RevealProgress, 0f);
        }

        internal void Pose(Vector3 worldPosition, Vector2 worldDirection, float worldWingspan, float elapsed)
        {
            float heading = Vector2.SignedAngle(Vector2.up, worldDirection);
            bird.transform.SetPositionAndRotation(worldPosition, Quaternion.Euler(0f, 0f, heading));
            // Keep the two wingbeats and short glide as changes in the field's size.
            float phrase = Mathf.Repeat(elapsed * 2.65f, 3f);
            float stroke = Mathf.InverseLerp(0f, 2f, phrase);
            float tuck = (1f - Mathf.Cos(stroke * (4f * Mathf.PI))) * .5f;
            bird.transform.localScale = Vector3.Scale(unitLocalScale * worldWingspan,
                new Vector3(1f - .22f * tuck, 1f + .08f * tuck, 1f));

            properties.SetFloat(AnimationPhase, elapsed);
            properties.SetFloat(ShapePhase, elapsed * 2.2f);
            bird.SetPropertyBlock(properties);
        }

        public void Dispose()
        {
            if (bird == null) return;
            bird.gameObject.SetActive(false);
            Object.Destroy(bird.gameObject);
        }
    }
}
