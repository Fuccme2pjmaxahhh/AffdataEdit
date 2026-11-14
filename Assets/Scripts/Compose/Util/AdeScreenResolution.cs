using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Arcade.Compose
{
	public static class AdeScreenResolution
	{
		public static void Apply(string resolution, bool fullscreen)
		{
			if (fullscreen)
			{
				DisplayInfo display = Screen.mainWindowDisplayInfo;
				Screen.SetResolution(display.width, display.height, FullScreenMode.FullScreenWindow);
				return;
			}
			Vector2Int size = GetWindowedSize(resolution, Screen.mainWindowDisplayInfo.workArea, ScaleFactor);
			Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
		}

		public static string GetDefaultResolution(RectInt workArea, float scaleFactor = 1)
		{
			// Match the window-fitting rules, including space for OS chrome.
			int width = Mathf.RoundToInt(1920 * Mathf.Max(1, scaleFactor));
			int height = Mathf.RoundToInt(1080 * Mathf.Max(1, scaleFactor));
			return workArea.width >= width + 32 && workArea.height >= height + 64
				? "1920x1080" : "1280x720";
		}

		// The resolution setting is in points; on a 2x Retina display a window takes twice the
		// pixels, which is what Screen.SetResolution and the work area use on macOS.
		public static Vector2Int GetWindowedSize(string resolution, RectInt workArea, float scaleFactor = 1)
		{
			string[] dimensions = resolution?.Split('x');
			if (dimensions == null || dimensions.Length != 2 ||
				!int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
				!int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
				width <= 0 || height <= 0)
			{
				width = 1280;
				height = 720;
			}
			if (scaleFactor > 1)
			{
				width = Mathf.RoundToInt(width * scaleFactor);
				height = Mathf.RoundToInt(height * scaleFactor);
			}

			// DPI is physical pixel density, not the operating system's window scale.
			// Leave room for window borders and the title bar, and preserve aspect ratio.
			if (workArea.width > 0 && workArea.height > 0)
			{
				int availableWidth = Mathf.Max(1, workArea.width - 32);
				int availableHeight = Mathf.Max(1, workArea.height - 64);
				float scale = Mathf.Min(1f, (float)availableWidth / width, (float)availableHeight / height);
				width = Mathf.Max(1, Mathf.FloorToInt(width * scale));
				height = Mathf.Max(1, Mathf.FloorToInt(height * scale));
			}

			return new Vector2Int(width, height);
		}

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
		private const string ObjC = "/usr/lib/libobjc.A.dylib";
		[DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
		[DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
		[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);
		[DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr receiver, IntPtr selector);

		// [[NSScreen mainScreen] backingScaleFactor]: 2 on Retina displays.
		public static float ScaleFactor
		{
			get
			{
				IntPtr screen = SendPointer(objc_getClass("NSScreen"), sel_registerName("mainScreen"));
				return screen == IntPtr.Zero ? 1 : (float)SendDouble(screen, sel_registerName("backingScaleFactor"));
			}
		}
#else
		public static float ScaleFactor => 1;
#endif
	}
}
