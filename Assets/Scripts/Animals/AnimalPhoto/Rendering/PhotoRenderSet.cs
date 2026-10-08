using System;
using UnityEngine;

namespace AnimalGame.Animals
{
    /// <summary>A reusable camera look. Capture copies its settings before a photograph is taken.</summary>
    public abstract class PhotoRenderSet : ScriptableObject
    {
        [Tooltip("Stable identity for this look. Duplicating a Set in the photo editor creates a new identity.")]
        [SerializeField] private string setId;
        [Tooltip("Authoring revision. Increment when intentionally publishing a new version of this look.")]
        [SerializeField, Min(1)] private int version = 1;

        public string SetId => string.IsNullOrEmpty(setId) ? GetType().Name : setId;
        public int Version => Mathf.Max(1, version);

        /// <summary>The returned object must own copies of all settings, not references to mutable assets.</summary>
        public abstract PhotoRenderSnapshot Capture(int seed);

        /// <summary>Use after duplicating a Set; ordinary parameter edits keep the existing identity.</summary>
        public void RegenerateIdentity()
        {
            setId = Guid.NewGuid().ToString("N");
        }

        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(setId)) RegenerateIdentity();
            version = Mathf.Max(1, version);
        }
    }
}
