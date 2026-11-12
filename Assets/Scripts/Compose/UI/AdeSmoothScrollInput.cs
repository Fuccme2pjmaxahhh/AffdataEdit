using TMPro;
using UnityEngine;

namespace Arcade.Compose
{
    // Lifetime bookkeeping only: implementing IScrollHandler here would change TMP's own
    // parent-handler search and deliver the same wheel step twice.
    [DisallowMultipleComponent]
    internal sealed class AdeSmoothScrollInput : MonoBehaviour
    {
        private AdeSmoothScroll owner;
        private TMP_InputField input;

        internal void Bind(AdeSmoothScroll value, TMP_InputField field)
        {
            if (owner && owner != value) owner.UnbindInput(input);
            owner = value;
            input = field;
        }

        private void Release()
        {
            if (owner) owner.UnbindInput(input);
            owner = null;
            input = null;
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();
    }
}
