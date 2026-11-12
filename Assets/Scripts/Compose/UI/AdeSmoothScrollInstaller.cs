using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Arcade.Compose
{
    // Discover scenes once and dynamic views on wheel input, without recurring whole-scene scans.
    [DefaultExecutionOrder(-1000)]
    internal sealed class AdeSmoothScrollInstaller : MonoBehaviour
    {
        private static AdeSmoothScrollInstaller instance;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<ScrollRect> views = new List<ScrollRect>();
        private PointerEventData pointer;
        private EventSystem pointerSystem;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance) return;
            var go = new GameObject("Smooth Scroll");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AdeSmoothScrollInstaller>();
            for (int i = 0; i < SceneManager.sceneCount; i++) instance.InstallScene(SceneManager.GetSceneAt(i));
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallScene(scene);

        private void InstallScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            roots.Clear();
            scene.GetRootGameObjects(roots);
            foreach (var root in roots)
            {
                views.Clear();
                root.GetComponentsInChildren(true, views);
                foreach (var scroll in views) Ensure(scroll);
            }
            roots.Clear();
            views.Clear();
        }

        internal static AdeSmoothScroll Ensure(ScrollRect scroll)
        {
            if (!scroll) return null;
            AdeScrollbarThumb.Ensure(scroll.verticalScrollbar);
            AdeScrollbarThumb.Ensure(scroll.horizontalScrollbar);
            if (!scroll.TryGetComponent<AdeSmoothScroll>(out var smooth)) smooth = scroll.gameObject.AddComponent<AdeSmoothScroll>();
            smooth.PrepareWheel();
            return smooth;
        }

        internal static void EnsureUnder(Transform root)
        {
            if (!root) return;
            // One local walk when a dialog is built/opened, not a periodic scene walk.
            foreach (var scroll in root.GetComponentsInChildren<ScrollRect>(true)) Ensure(scroll);
        }

        private void Update()
        {
            var system = EventSystem.current;
            if (!system || !system.isActiveAndEnabled) return;
            var module = system.currentInputModule as InputSystemUIInputModule;
            Vector2 delta = module && module.scrollWheel && module.scrollWheel.action != null
                ? module.scrollWheel.action.ReadValue<Vector2>()
                : Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;
            if (delta == Vector2.zero) return;
            Vector2 position = module && module.point && module.point.action != null
                ? module.point.action.ReadValue<Vector2>()
                : Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            if (pointer == null || pointerSystem != system)
            {
                pointerSystem = system;
                pointer = new PointerEventData(system);
            }
            pointer.Reset();
            pointer.position = position;
            hits.Clear();
            system.RaycastAll(pointer, hits);
            if (hits.Count > 0) PrepareWheelTarget(hits[0].gameObject);
            hits.Clear();
        }

        internal static void PrepareWheelTarget(GameObject hit)
        {
            if (!hit) return;
            var handler = ExecuteEvents.GetEventHandler<IScrollHandler>(hit);
            if (!handler) return;
            if (handler.TryGetComponent<TMP_InputField>(out var input))
            {
                var parent = AdeSmoothScroll.NearestScroll(input.transform.parent);
                if (parent) Ensure(parent).BindInput(input);
            }
            else if (handler.TryGetComponent<ScrollRect>(out var scroll)) Ensure(scroll);
        }
    }
}
