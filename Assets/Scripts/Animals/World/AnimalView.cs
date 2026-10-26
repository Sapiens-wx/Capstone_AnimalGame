using UnityEngine;

namespace AnimalGame.Animals
{
    [DisallowMultipleComponent]
    public sealed class AnimalView : MonoBehaviour
    {
        public void ShowCorpse(AnimalAgent agent)
        {
            AnimalTurnDeformation turn = GetComponent<AnimalTurnDeformation>();
            if (turn != null) turn.enabled = false;
            agent.PlaceholderView?.RestoreVisibleAppearance();
            AnimalDiscoveryVisual discovery = GetComponent<AnimalDiscoveryVisual>();
            if (discovery != null) discovery.enabled = false;
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            AnimalPhotoSubject subject = GetComponent<AnimalPhotoSubject>();
            SpriteRenderer body = subject != null && subject.BodyRenderers.Count > 0 ? subject.BodyRenderers[0] : null;
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer == null) continue;
                bool isBody = subject == null;
                if (subject != null)
                    foreach (SpriteRenderer authored in subject.BodyRenderers)
                        if (authored == renderer) isBody = true;
                renderer.enabled = isBody;
                if (isBody)
                {
                    Color color = renderer.color;
                    color.a = 1f;
                    renderer.color = new Color(color.r * .55f, color.g * .55f, color.b * .55f, 1f);
                }
            }
            if (body != null && agent.Config.CorpseSprite != null) body.sprite = agent.Config.CorpseSprite;
        }
    }
}
