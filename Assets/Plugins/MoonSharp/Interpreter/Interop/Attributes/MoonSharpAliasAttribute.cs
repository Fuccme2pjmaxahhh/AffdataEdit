using System;

namespace MoonSharp.Interpreter
{
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class,
        AllowMultiple = true
    )]
    public sealed class MoonSharpAliasAttribute : Attribute
    {
        public readonly string AliasName;

        public MoonSharpAliasAttribute(string alias)
        {
            AliasName = alias;
        }
    }
}
