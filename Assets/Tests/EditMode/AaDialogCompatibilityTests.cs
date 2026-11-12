using System.Reflection;
using Arcade.Compose.Scripting.Aa;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using NUnit.Framework;

public class AaDialogCompatibilityTests
{
    [Test]
    public void ExternalToolsCanResolveArcadeAlphaDialogShow()
    {
        var previous = UserData.RegistrationPolicy;
        try
        {
            UserData.RegistrationPolicy = InteropRegistrationPolicy.Automatic;
            var script = new Script();
            var globals = typeof(DialogAPI).Assembly.GetType("Arcade.Compose.Scripting.Aa.AaGlobals", true);
            globals.GetMethod("Assign", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { script.Globals });
            script.DoString("assert(Dialog ~= nil); assert(type(Dialog.Show) == 'function')");
        }
        finally { UserData.RegistrationPolicy = previous; }
    }
}
