using System;
using System.Reflection;
using LitMotion;
using UnityEditor;

[InitializeOnLoad]
internal static class LitMotionEditorCleanup
{
    static LitMotionEditorCleanup()
    {
        // LitMotion 2.0.2 releases these allocators on assembly reload, but not editor quit.
        // Reuse its cleanup without modifying the UPM cache or its player lifecycle.
        var factory = typeof(MotionDispatcher).Assembly.GetType("LitMotion.RewindableAllocatorFactory", true);
        var dispose = factory.GetMethod("Dispose", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(factory.FullName, "Dispose");
        EditorApplication.quitting += (Action)dispose.CreateDelegate(typeof(Action));
    }
}
