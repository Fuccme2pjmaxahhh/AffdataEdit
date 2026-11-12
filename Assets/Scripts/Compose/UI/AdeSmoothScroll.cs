using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // ScrollRect retains ownership of bounds/scrollbars/listeners. Only wheel steps are eased.
    [DisallowMultipleComponent]
    public sealed class AdeSmoothScroll : MonoBehaviour, IScrollHandler, IBeginDragHandler
    {
        private const float Sharpness = 18f;
        // TMP single-line fields call this cached interface directly, bypassing ExecuteEvents.
        private static readonly FieldInfo InputScrollParent = typeof(TMP_InputField).GetField(
            "m_IScrollHandlerParent", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<TMP_InputField, IScrollHandler> inputs = new Dictionary<TMP_InputField, IScrollHandler>();
        private ScrollRect scroll;
        // TMP clones its dropdown while the template is active (native sensitivity is then zero).
        // Preserve the real value across that clone instead of inheriting a permanently frozen view.
        [SerializeField, HideInInspector] private float sensitivity;
        [SerializeField, HideInInspector] private bool hasSensitivity;
        private bool ownsSensitivity;
        private Vector2 pending;
        private PointerEventData wheel;
        private EventSystem wheelSystem;

        private void Awake() => scroll = GetComponent<ScrollRect>();

        private void OnEnable()
        {
            if (!scroll) { enabled = false; return; }
            if (!hasSensitivity || scroll.scrollSensitivity != 0) sensitivity = scroll.scrollSensitivity;
            hasSensitivity = true;
            ownsSensitivity = true;
            scroll.scrollSensitivity = 0;
        }

        private void RestoreSensitivity()
        {
            if (scroll && ownsSensitivity)
            {
                if (scroll.scrollSensitivity != 0) sensitivity = scroll.scrollSensitivity;
                scroll.scrollSensitivity = sensitivity;
            }
            ownsSensitivity = false;
        }

        private void OnDestroy()
        {
            RestoreSensitivity();
            if (InputScrollParent != null)
                foreach (var input in inputs)
                    if (input.Key && ReferenceEquals(InputScrollParent.GetValue(input.Key), this))
                        InputScrollParent.SetValue(input.Key, input.Value);
            inputs.Clear();
        }

        private void OnDisable() { pending = Vector2.zero; RestoreSensitivity(); }

        internal static ScrollRect NearestScroll(Transform child)
        {
            for (var parent = child; parent; parent = parent.parent)
                if (parent.TryGetComponent<ScrollRect>(out var candidate) && candidate.IsActive()
                    && candidate.content && (candidate.vertical || candidate.horizontal)) return candidate;
            return null;
        }

        internal void BindInput(TMP_InputField input)
        {
            if (!input || input.lineType != TMP_InputField.LineType.SingleLine || InputScrollParent == null) return;
            var previous = InputScrollParent.GetValue(input) as IScrollHandler;
            if (ReferenceEquals(previous, this)) return;
            // Re-enabling/reparenting a field can overwrite TMP's cache. Restore its native parent
            // on uninstall, never another smoothing component.
            inputs[input] = previous is AdeSmoothScroll ? NearestScroll(input.transform.parent) : previous;
            InputScrollParent.SetValue(input, this);
            var lifetime = input.GetComponent<AdeSmoothScrollInput>();
            if (!lifetime) lifetime = input.gameObject.AddComponent<AdeSmoothScrollInput>();
            lifetime.Bind(this, input);
        }

        internal void UnbindInput(TMP_InputField input)
        {
            if (!inputs.TryGetValue(input, out var previous)) return;
            inputs.Remove(input);
            if (input && InputScrollParent != null && ReferenceEquals(InputScrollParent.GetValue(input), this))
                InputScrollParent.SetValue(input, previous);
        }

        public void OnScroll(PointerEventData data)
        {
            if (!scroll) return;
            if (!scroll.IsActive())
            {
                var parent = NearestScroll(transform.parent);
                if (!parent) return;
                if (parent.TryGetComponent<AdeSmoothScroll>(out var smooth)) smooth.OnScroll(data);
                else parent.OnScroll(data);
                return;
            }
            // TMP can retain a direct reference while smoothing is disabled.
            if (!isActiveAndEnabled) { scroll.OnScroll(data); return; }
            CaptureSensitivity();
            pending += data.scrollDelta;
            var viewport = scroll.viewport ? scroll.viewport : (RectTransform)scroll.transform;
            float extent = Mathf.Max(Mathf.Max(viewport.rect.width, scroll.content.rect.width),
                Mathf.Max(viewport.rect.height, scroll.content.rect.height));
            float cap = extent / Mathf.Max(Mathf.Abs(sensitivity), .001f);
            pending = Vector2.ClampMagnitude(pending, Mathf.Max(1, cap));
        }

        public void OnBeginDrag(PointerEventData data) => pending = Vector2.zero;

        private void CaptureSensitivity()
        {
            if (scroll.scrollSensitivity == 0) return;
            sensitivity = scroll.scrollSensitivity;
            scroll.scrollSensitivity = 0;
        }

        internal void PrepareWheel()
        {
            if (isActiveAndEnabled && scroll) CaptureSensitivity();
        }

        private void LateUpdate() => Advance(Time.unscaledDeltaTime);

        internal void Advance(float deltaTime)
        {
            if (!isActiveAndEnabled || !scroll || !scroll.IsActive() || !scroll.content) return;
            CaptureSensitivity();
            if (pending == Vector2.zero) return;
            Vector2 step = pending.sqrMagnitude < .0004f ? pending
                : pending * (1 - Mathf.Exp(-Sharpness * Mathf.Max(0, deltaTime)));
            if (step == Vector2.zero) return;
            pending -= step;
            if (wheel == null || wheelSystem != EventSystem.current)
            {
                wheelSystem = EventSystem.current;
                wheel = new PointerEventData(wheelSystem);
            }
            wheel.Reset();
            wheel.scrollDelta = step;
            Vector2 before = scroll.content.anchoredPosition;
            scroll.scrollSensitivity = sensitivity;
            try { scroll.OnScroll(wheel); }
            finally { if (scroll && isActiveAndEnabled) scroll.scrollSensitivity = 0; }
            if (!scroll || !scroll.content) return;
            if (scroll.movementType == ScrollRect.MovementType.Clamped
                && (scroll.content.anchoredPosition - before).sqrMagnitude < .000001f)
                pending = Vector2.zero;
        }
    }
}
