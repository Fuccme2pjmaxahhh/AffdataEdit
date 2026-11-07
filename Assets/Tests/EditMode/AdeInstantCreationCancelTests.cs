using System;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Aff;
using Arcade.Compose;
using Arcade.Compose.Operation;
using Arcade.Gameplay;
using Arcade.Gameplay.Chart;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class AdeInstantCreationCancelTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void EscapeExitsOnArcTapModeWithoutAnOngoingOperation(bool shortcutsConsumeInput)
    {
        var objects = new List<GameObject>();
        var restore = new List<Action>();
        AdeInputControl controls = null;
        Keyboard keyboard = null;
        Keyboard previousKeyboard = Keyboard.current;
        AdeInputManager input = null;
        var originalSettings = InputSystem.settings;
        const string editModeInput = "RUN_PLAYER_UPDATES_IN_EDIT_MODE";
        bool previousFeature = (bool)typeof(InputSettings).GetMethod("IsFeatureEnabled", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(originalSettings, new object[] { editModeInput });
        bool previousConsumption = originalSettings.shortcutKeysConsumeInput;
        originalSettings.shortcutKeysConsumeInput = shortcutsConsumeInput;
        originalSettings.SetInternalFeatureFlag(editModeInput, true);
        T Component<T>() where T : Component
        {
            var owner = new GameObject(typeof(T).Name + " cancel test");
            owner.SetActive(false); objects.Add(owner);
            return owner.AddComponent<T>();
        }
        void Singleton<T>(T instance)
        {
            var property = typeof(T).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            var previous = property.GetValue(null);
            restore.Add(() => property.SetValue(null, previous));
            property.SetValue(null, instance);
        }
        try
        {
            var game = Component<ArcGameplayManager>(); Singleton(game);
            game.Chart = new ArcChart(new RawAffChart());
            var compose = Component<ArcadeComposeManager>(); Singleton(compose);
            compose.IsEditorMode = true;
            var cursor = Component<AdeCursorManager>(); Singleton(cursor);
            cursor.ArcTapCursorRenderer = Component<MeshRenderer>();
            cursor.SfxArcTapCursorRenderer = Component<MeshRenderer>();
            var create = Component<AdeClickToCreate>(); Singleton(create);
            create.Enable = true; create.Mode = ClickToCreateMode.ArcTap;
            create.ArctapMode = ClickToCreateArctapMode.OnArc;
            cursor.ArcTapCursorEnabled = true;
            var operation = Component<AdeOperationManager>(); Singleton(operation);
            operation.operations = Array.Empty<AdeOperation>();
            Assert.IsFalse(operation.HasOngoingOperation);
            input = Component<AdeInputManager>(); Singleton(input);
            controls = new AdeInputControl(); input.Controls = controls;
            input.EventSystem = Component<EventSystem>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            controls.Enable();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            Assert.IsTrue(keyboard.escapeKey.wasPressedThisFrame, "The actual keyboard press must be processed");
            Assert.IsTrue(input.CheckHotkeyActionPressed(input.Inputs.Cancel), "Escape must reach the real cancel action");
            typeof(AdeOperationManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(operation, null);
            Assert.AreEqual(ClickToCreateMode.Idle, create.Mode);
            Assert.IsFalse(cursor.ArcTapCursorEnabled);
            Assert.IsTrue(create.Enable, "Escape exits the creation mode without changing the point-to-create toggle");
        }
        finally
        {
            if (input) input.Controls = null;
            if (controls != null) { controls.Disable(); UnityEngine.Object.DestroyImmediate(controls.asset); }
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            previousKeyboard?.MakeCurrent();
            originalSettings.SetInternalFeatureFlag(editModeInput, previousFeature);
            originalSettings.shortcutKeysConsumeInput = previousConsumption;
            foreach (var reset in restore) reset();
            foreach (var owner in objects) UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
