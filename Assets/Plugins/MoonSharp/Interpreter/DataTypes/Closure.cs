using System;
using System.Collections.Generic;
using System.Text;
using MoonSharp.Interpreter.Execution;
using MoonSharp.Interpreter.Execution.VM;
using UnityEngine;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// A class representing a script function
	/// </summary>
	public class Closure : RefIdObject, IScriptPrivateResource
	{
		/// <summary>
		/// Type of closure based on upvalues
		/// </summary>
		public enum UpvaluesType
		{
			/// <summary>
			/// The closure has no upvalues (thus, technically, it's a function and not a closure!)
			/// </summary>
			None,
			/// <summary>
			/// The closure has _ENV as its only upvalue
			/// </summary>
			Environment,
			/// <summary>
			/// The closure is a "real" closure, with multiple upvalues
			/// </summary>
			Closure
		}


		/// <summary>
		/// Gets the entry point location in bytecode .
		/// </summary>
		public int EntryPointByteCodeLocation { get; private set; }


		/// <summary>
		/// Gets the script owning this function
		/// </summary>
		public Script OwnerScript { get; private set; }


		/// <summary>
		/// Shortcut for an empty closure
		/// </summary>
		private static ClosureContext emptyClosure = new ClosureContext();

		/// <summary>
		/// The current closure context
		/// </summary>
		internal ClosureContext ClosureContext { get; private set; }


		/// <summary>
		/// Initializes a new instance of the <see cref="Closure"/> class.
		/// </summary>
		/// <param name="script">The script.</param>
		/// <param name="idx">The index.</param>
		/// <param name="symbols">The symbols.</param>
		/// <param name="resolvedLocals">The resolved locals.</param>
		internal Closure(Script script, int idx, SymbolRef[] symbols, IEnumerable<DynValue> resolvedLocals)
		{
			OwnerScript = script;

			EntryPointByteCodeLocation = idx;

			if (symbols.Length > 0)
				ClosureContext = new ClosureContext(symbols, resolvedLocals);
			else
				ClosureContext = emptyClosure;
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call()
		{
			return OwnerScript.Call(this);
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <param name="args">The arguments to pass to the function.</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call(params object[] args)
		{
			return OwnerScript.Call(this, args);
		}

		/// <summary>
		/// Calls this function with the specified args
		/// </summary>
		/// <param name="args">The arguments to pass to the function.</param>
		/// <returns></returns>
		/// <exception cref="System.ArgumentException">Thrown if function is not of DataType.Function</exception>
		public DynValue Call(params DynValue[] args)
		{
			return OwnerScript.Call(this, args);
		}


		/// <summary>
		/// Gets a delegate wrapping calls to this scripted function
		/// </summary>
		/// <returns></returns>
		public ScriptFunctionDelegate GetDelegate()
		{
			return args => this.Call(args).ToObject();
		}

		/// <summary>
		/// Gets a delegate wrapping calls to this scripted function
		/// </summary>
		/// <typeparam name="T">The type of return value of the delegate.</typeparam>
		/// <returns></returns>
		public ScriptFunctionDelegate<T> GetDelegate<T>()
		{
			return args => this.Call(args).ToObject<T>();
		}

		/// <summary>
		/// Gets the number of upvalues in this closure
		/// </summary>
		/// <returns>The number of upvalues in this closure</returns>
		public int GetUpvaluesCount()
		{
			return ClosureContext.Count;
		}

		/// <summary>
		/// Gets the name of the specified upvalue.
		/// </summary>
		/// <param name="idx">The index of the upvalue.</param>
		/// <returns>The upvalue name</returns>
		public string GetUpvalueName(int idx)
		{
			return ClosureContext.Symbols[idx];
		}

		/// <summary>
		/// Gets the value of an upvalue. To set the value, use GetUpvalue(idx).Assign(...);
		/// </summary>
		/// <param name="idx">The index of the upvalue.</param>
		/// <returns>The value of an upvalue </returns>
		public DynValue GetUpvalue(int idx)
		{
			return ClosureContext[idx];
		}

		/// <summary>
		/// Gets the type of the upvalues contained in this closure
		/// </summary>
		/// <returns></returns>
		public UpvaluesType GetUpvaluesType()
		{
			int count = GetUpvaluesCount();

			if (count == 0)
				return UpvaluesType.None;
			else if (count == 1 && GetUpvalueName(0) == WellKnownSymbols.ENV)
				return UpvaluesType.Environment;
			else
				return UpvaluesType.Closure;
		}
		
#if UNITY_EDITOR
		public void DumpClosureInfo()
		{
			var closure = this;
			var dumpResult = new StringBuilder();
			
			var byteCode = OwnerScript.ByteCode;
			if (byteCode == null)
			{
				dumpResult.AppendLine("[Error] Cannot locate ByteCode.");
				return;
			}

			int entry = closure.EntryPointByteCodeLocation;
			dumpResult.AppendLine($"[Function Dump]");
			dumpResult.AppendLine($"  EntryPointByteCodeLocation = {entry}");
			dumpResult.AppendLine($"  UpvalueCount = {closure.GetUpvaluesCount()}");
			dumpResult.AppendLine($"  ByteCode length = {byteCode.Code.Count}");
			dumpResult.AppendLine(new string('-', 50));

			// 打印 entryPoint 附近的 10 条指令
			for (int i = Math.Max(0, entry - 5); i < Math.Min(byteCode.Code.Count, entry + 10); i++)
			{
				var ins = byteCode.Code[i];
				dumpResult.AppendLine($"{i:D4}: {ins.OpCode,-15}  NumVal={ins.NumVal,-3}  NumVal2={ins.NumVal2,-3}  Symb={(ins.Symbol == null ? "null" : ins.Symbol.i_Name)}");
			}

			Debug.Log(dumpResult.ToString());
		}
#endif
		
		public int GetParameterCount()
		{
			var closure = this;

			var byteCode = OwnerScript.ByteCode;
			if (byteCode == null)
				return - 1;

			int entry = closure.EntryPointByteCodeLocation;

			for (int i = entry; i < Math.Min(byteCode.Code.Count, entry + 10); i++)
			{
				var ins = byteCode.Code[i];
				if (ins.OpCode != OpCode.BeginFn) continue;
				return ins.NumVal;
			}

			return -1;
		}

		public string GetFirstLocalName()
		{
			var closure = this;

			var byteCode = OwnerScript.ByteCode;
			if (byteCode == null)
				return "";

			int entry = closure.EntryPointByteCodeLocation;

			for (int i = entry; i < Math.Min(byteCode.Code.Count, entry + 10); i++)
			{
				var ins = byteCode.Code[i];
				if (ins.OpCode != OpCode.Local) continue;
				return ins.Symbol == null ? "" : ins.Symbol.i_Name;
			}

			return "";
		}
	}
}
