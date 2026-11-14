using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Runtime.InteropServices;
using Process = System.Diagnostics.Process;
#endif

namespace Arcade.Compose
{
    [Serializable]
    public sealed class AdeWindowPlacement
    {
        public bool Maximized;
        public int Left, Top, Right, Bottom;

        internal bool HasBounds => Right > Left && Bottom > Top;
    }

    // The title-bar maximize button is a window state, independent of borderless
    // fullscreen and the resolution selected in Settings.
    public sealed class AdeWindowState : MonoBehaviour
    {
        public const string SettingsKey = "AffdataEdit.WindowPlacement";
        private static string startupResolution;
        private static bool startupFullscreen, restored;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            startupResolution = null;
            startupFullscreen = restored = false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            instance = null;
#endif
        }

        public static bool WasRestored(string resolution, bool fullscreen)
            => restored && startupResolution == resolution && startupFullscreen == fullscreen;

        public static IEnumerator RestoreStartup()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (Application.isBatchMode) yield break;
            var owner = new GameObject("WindowState");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<AdeWindowState>();
            try
            {
                instance.placement = JsonConvert.DeserializeObject<AdeWindowPlacement>(
                    AdeSettings.GetString(SettingsKey, "{}")) ?? new AdeWindowPlacement();
            }
            catch (JsonException error)
            {
                Debug.LogWarning("Invalid saved window placement; using the default window. " + error.Message);
                instance.placement = new AdeWindowPlacement();
            }
            ArcadePreference preference = ArcadeComposeManager.ReadPreferences();
            startupResolution = string.IsNullOrEmpty(preference.ScreenResolution)
                ? AdeScreenResolution.GetDefaultResolution(Screen.mainWindowDisplayInfo.workArea)
                : preference.ScreenResolution;
            startupFullscreen = preference.Fullscreen;
            if (!startupFullscreen && instance.placement.Maximized)
            {
                // Preserve the engine's maximized startup window. A Windowed
                // SetResolution here visibly restores it before maximizing again.
                // Native startup may already be maximized before Screen's cached
                // mode catches up. Reassigning that mode queues another OS resize.
                bool hasWindow = instance.TryGetWindow(out IntPtr window);
                bool nativeMaximized = hasWindow && IsZoomed(window);
                if (!nativeMaximized && Screen.fullScreenMode != FullScreenMode.MaximizedWindow)
                    Screen.fullScreenMode = FullScreenMode.MaximizedWindow;
                if (hasWindow && !nativeMaximized) ShowWindow(window, ShowMaximized);
                yield return null;
                yield return WaitForStableSize();
                if (instance.RestorePlacement())
                {
                    yield return null;
                    yield return WaitForStableSize();
                }
            }
            else
            {
                AdeScreenResolution.Apply(startupResolution, startupFullscreen);
                // Normal/fullscreen changes apply at frame end. Restore saved
                // normal bounds only after that request has completed.
                yield return null;
                yield return WaitForStableSize();
                if (!startupFullscreen && instance.RestorePlacement())
                {
                    yield return null;
                    yield return WaitForStableSize();
                }
            }
            restored = instance.TryGetWindow(out _);
            instance.captureAfter = Time.unscaledTime + .5f;
#else
            yield break;
#endif
        }

        public static IEnumerator WaitForStableSize()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            int stableFrames = 0;
            int lastWidth = -1, lastHeight = -1;
            double deadline = Time.realtimeSinceStartupAsDouble + 1.5;
            while (stableFrames < 2 && Time.realtimeSinceStartupAsDouble < deadline
                && instance && instance.TryGetWindow(out IntPtr window) && !IsIconic(window))
            {
                bool matches = GetClientRect(window, out NativeRect client)
                    && client.Right == Screen.width && client.Bottom == Screen.height;
                GuiThreadInfo info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
                bool moving = GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info)
                    && (info.Flags & 2) != 0;
                stableFrames = matches && !moving && lastWidth == Screen.width && lastHeight == Screen.height
                    ? stableFrames + 1 : 0;
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                yield return null;
            }
#else
            yield break;
#endif
        }

        public static void BeforeResolutionChange(bool fullscreen)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!instance) return;
            instance.CancelModeChange();
            instance.Capture();
            instance.captureAfter = Time.unscaledTime + .5f;
            if (!fullscreen && !IsFullscreen && instance.TryGetWindow(out IntPtr window) && IsZoomed(window))
                ShowWindow(window, 9); // An explicit resolution choice leaves maximized mode.
            if (!fullscreen)
            {
                // The frame-end resize may not run before an immediate exit. Never
                // let old maximized bounds override the newly selected resolution.
                instance.placement.Maximized = false;
                instance.placement.Left = instance.placement.Top = 0;
                instance.placement.Right = instance.placement.Bottom = 0;
                instance.changedAt = Time.unscaledTime;
                instance.dirty = true;
            }
#endif
        }

        public static bool ChangeFullscreen(string resolution, bool fullscreen)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!instance) return false;
            instance.CancelModeChange();
            instance.Capture();
            instance.Save();
            instance.captureAfter = float.PositiveInfinity;
            instance.modeChange = instance.StartCoroutine(instance.SwitchFullscreen(resolution, fullscreen));
            return true;
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int ShowNormal = 1, ShowMinimized = 2, ShowMaximized = 3;
        private static AdeWindowState instance;
        private AdeWindowPlacement placement;
        private IntPtr windowHandle;
        private float captureAfter;
        private float changedAt;
        private bool nativeFailureReported, dirty;
        private Coroutine modeChange;

        private static bool IsFullscreen => Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen
            || Screen.fullScreenMode == FullScreenMode.FullScreenWindow;

        private void Update()
        {
            if (!restored || Time.unscaledTime < captureAfter) return;
            captureAfter = Time.unscaledTime + .25f;
            Capture();
            if (Time.unscaledTime - changedAt >= .25f) Save();
        }

        private void OnApplicationQuit()
        {
            if (!restored) return;
            if (modeChange == null && Time.unscaledTime >= captureAfter - .25f) Capture();
            Save();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused || !restored || modeChange != null || Time.unscaledTime < captureAfter - .25f) return;
            Capture();
            Save();
        }

        private void Save()
        {
            if (!dirty) return;
            AdeSettings.SetString(SettingsKey, JsonConvert.SerializeObject(placement));
            dirty = false;
        }

        private void CancelModeChange()
        {
            if (modeChange == null) return;
            StopCoroutine(modeChange);
            modeChange = null;
        }

        private IEnumerator SwitchFullscreen(string resolution, bool fullscreen)
        {
            AdeScreenResolution.Apply(resolution, fullscreen);
            yield return null;
            yield return WaitForStableSize();
            if (!fullscreen && RestorePlacement())
            {
                yield return null;
                yield return WaitForStableSize();
            }
            captureAfter = Time.unscaledTime + .25f;
            modeChange = null;
        }

        private bool RestorePlacement()
        {
            if (!placement.HasBounds || !TryGetWindow(out IntPtr window)) return false;
            NativePlacement native = NewPlacement();
            native.ShowCommand = placement.Maximized ? ShowMaximized : ShowNormal;
            native.NormalPosition = new NativeRect
            {
                Left = placement.Left, Top = placement.Top, Right = placement.Right, Bottom = placement.Bottom
            };
            if (SetWindowPlacement(window, ref native)) return true;
            Debug.LogError("Unable to restore window placement: " + Marshal.GetLastWin32Error());
            return false;
        }

        private void Capture()
        {
            if (IsFullscreen || !TryGetWindow(out IntPtr window)) return;
            NativePlacement native = NewPlacement();
            if (!GetWindowPlacement(window, ref native))
            {
                if (!nativeFailureReported)
                    Debug.LogError("Unable to read window placement: " + Marshal.GetLastWin32Error());
                nativeFailureReported = true;
                return;
            }
            // GetWindowPlacement does not report the pre-minimize state in flags.
            // Keep the last visible state; never reopen an application minimized.
            if (native.ShowCommand == ShowMinimized) return;
            bool maximized = native.ShowCommand == ShowMaximized;
            NativeRect normal = native.NormalPosition;
            if (normal.Right <= normal.Left || normal.Bottom <= normal.Top) return;
            if (placement.Maximized == maximized && placement.Left == normal.Left && placement.Top == normal.Top
                && placement.Right == normal.Right && placement.Bottom == normal.Bottom) return;
            placement.Maximized = maximized;
            placement.Left = normal.Left;
            placement.Top = normal.Top;
            placement.Right = normal.Right;
            placement.Bottom = normal.Bottom;
            changedAt = Time.unscaledTime;
            dirty = true;
        }

        private bool TryGetWindow(out IntPtr window)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                using (Process process = Process.GetCurrentProcess()) windowHandle = process.MainWindowHandle;
            }
            window = windowHandle;
            return window != IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePlacement
        {
            public int Length, Flags, ShowCommand;
            public NativePoint MinPosition, MaxPosition;
            public NativeRect NormalPosition;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public NativeRect CaretRect;
        }
        private static NativePlacement NewPlacement() => new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr window, ref NativePlacement placement);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr window, ref NativePlacement placement);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
#endif
    }
}
