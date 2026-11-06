using System;
using UnityEngine;

namespace PalmTheatre.Integration
{
    // Opt-in on the independent art-preview scene only. Existing automatic
    // costume/skin passes finish first; no shared source material is edited.
    [DefaultExecutionOrder(1000)]
    public sealed class HeroSurfaceRefinement : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public Renderer renderer;
            public Material[] refined;
            [NonSerialized] public Material[] baseline;
        }
        public Binding[] bindings;
        public bool applyOnStart = true;
        bool initialized;
        void Start() { SetRefined(applyOnStart); }
        public void SetRefined(bool refined)
        {
            if (!initialized)
            {
                foreach (var binding in bindings) binding.baseline = binding.renderer.sharedMaterials;
                initialized = true;
            }
            foreach (var binding in bindings)
                binding.renderer.sharedMaterials = refined ? binding.refined : binding.baseline;
        }
    }
}
