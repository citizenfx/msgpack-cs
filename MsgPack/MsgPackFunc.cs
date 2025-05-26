using CitizenFX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security;
using static CitizenFX.MsgPack.Detail.Helper;

namespace CitizenFX.MsgPack
{
	public delegate object MsgPackFunc(Remote remote, ref MsgPackDeserializer deserializer);

	public ref partial struct MsgPackDeserializer
	{
		private static readonly Dictionary<MethodInfo, MethodInfo> s_wrappedMethods = new Dictionary<MethodInfo, MethodInfo>();
		private static readonly Dictionary<MethodInfo, MethodInfo> s_dynfuncMethods = new Dictionary<MethodInfo, MethodInfo>();

		/// <summary>
		/// Creates a new MsgPack invokable function
		/// </summary>
		/// <param name="target">the instance object, may be null for static methods</param>
		/// <param name="method">method to make dynamically invokable</param>
		/// <returns>dynamically invokable delegate</returns>
		/// <exception cref="ArgumentNullException">when the given method is non-static and target is null or if given method is null</exception>
		[SecurityCritical]
		public static MsgPackFunc CreateDelegate(object target, MethodInfo method)
		{
			if (method is null)
			{
				throw new ArgumentNullException($"Given method is null.");
			}
			else if (!method.IsStatic && target is null)
			{
				string args = string.Join<string>(", ", method.GetParameters().Select(p => p.ParameterType.Name));
				throw new ArgumentNullException($"Can't create delegate for {method.DeclaringType.FullName}.{method.Name}({args}), it's a non-static method and it's missing a target instance.");
			}

			return ConstructDelegate(target, method);
		}

		/// <summary>
		/// Creates a new MsgPack invokable function or simply returns it
		/// </summary>
		/// <param name="deleg">delegate to make dynamically invokable</param>
		/// <returns>MsgPack invokable delegate or returns <paramref name="deleg"/> if it's aleady of type <see cref="MsgPackFunc"/></returns>
		/// <exception cref="ArgumentNullException">when the given method is non-static and target is null or if given method is null</exception>
		[SecuritySafeCritical]
		public static MsgPackFunc CreateDelegate(Delegate deleg) => deleg as MsgPackFunc ?? CreateDelegate(deleg.Target, deleg.Method);

		// no need to recreate it
		public static MsgPackFunc CreateDelegate(MsgPackFunc deleg) => deleg;

		public static MethodInfo GetMethodInfoFromDelegate(MsgPackFunc func)
		{
			foreach (var kvp in s_wrappedMethods)
			{
				if (kvp.Value == func.Method)
					return kvp.Key;
			}
			return null;
		}

		[SecurityCritical]
		private static MsgPackFunc ConstructDelegate(object target, MethodInfo method)
		{
			if (s_wrappedMethods.TryGetValue(method, out var existingMethod))
			{
				return (MsgPackFunc)existingMethod.CreateDelegate(typeof(MsgPackFunc), target);
			}

			ParameterInfo[] parameters = method.GetParameters();
			bool hasThis = (method.CallingConvention & CallingConventions.HasThis) != 0;

			var lambda = new DynamicMethod($"{method.DeclaringType.FullName}.{method.Name}", typeof(object),
				hasThis
				? new[] { typeof(object), typeof(Remote), typeof(MsgPackDeserializer).MakeByRefType() }
				: new[] { typeof(Remote), typeof(MsgPackDeserializer).MakeByRefType() });

			ILGenerator g = lambda.GetILGenerator();
			g.DeclareLocal(typeof(uint));

			OpCode ldarg_remote, ldarg_deserializer;
			if (hasThis)
			{
				g.Emit(OpCodes.Ldarg_0);
				ldarg_remote = OpCodes.Ldarg_1;
				ldarg_deserializer = OpCodes.Ldarg_2;
			}
			else
			{
				target = null;
				ldarg_remote = OpCodes.Ldarg_0;
				ldarg_deserializer = OpCodes.Ldarg_1;
			}

			// get size, will throw an exception if the input isn't an array
			g.Emit(ldarg_deserializer);
			g.EmitCall(OpCodes.Call, GetResultMethod<uint>(Detail.SerializerAccess.ReadArraySize), null);
			g.Emit(OpCodes.Stloc_0);

			for (int i = 0, p = 0; i < parameters.Length; ++i)
			{
				var parameter = parameters[i];
				var t = parameter.ParameterType;

				if (Attribute.IsDefined(parameter, typeof(SourceAttribute), true))
				{
					if (t == typeof(Remote))
					{
						g.Emit(ldarg_remote);
						continue;
					}
					else if (t == typeof(bool))
					{
						g.Emit(ldarg_remote);
						g.Emit(OpCodes.Call, ((Func<Remote, bool>)Remote.IsRemoteInternal).Method);
						continue;
					}
					else
					{
						var constructor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Remote) }, null);
						if (constructor != null)
						{
							g.Emit(ldarg_remote);
							g.Emit(OpCodes.Newobj, constructor);
							continue;
						}
					}

					throw new ArgumentException($"{nameof(SourceAttribute)} used on type {t}, this type can't be constructed with parameter Remote.");
				}

				MethodInfo deserializer = MsgPackRegistry.GetOrCreateDeserializer(t);
				if (deserializer != null)
				{
					Label lblOutOfRange = g.DefineLabel(), lblNextArgument = g.DefineLabel();

					g.Emit(OpCodes.Ldc_I4, p++);
					g.Emit(OpCodes.Ldloc_0);
					g.Emit(OpCodes.Bge_Un, lblOutOfRange);

					g.Emit(ldarg_deserializer);
					g.EmitCall(OpCodes.Call, deserializer, null);
					g.Emit(OpCodes.Br, lblNextArgument);

					g.MarkLabel(lblOutOfRange);
					if (parameter.IsOptional)
					{
						switch (parameter.DefaultValue)
						{
							case null: g.Emit(OpCodes.Ldnull); break;
							case string v: g.Emit(OpCodes.Ldstr, v); break;

							case byte v: g.Emit(OpCodes.Ldc_I4, v); break;
							case ushort v: g.Emit(OpCodes.Ldc_I4, v); break;
							case uint v: g.Emit(OpCodes.Ldc_I4, v); break;
							case ulong v: g.Emit(OpCodes.Ldc_I4, v); break;
							case sbyte v: g.Emit(OpCodes.Ldc_I4, v); break;
							case short v: g.Emit(OpCodes.Ldc_I4, v); break;
							case int v: g.Emit(OpCodes.Ldc_I4, v); break;
						}

						// Generic way to also support Nullable<> types
						if (t.IsGenericType)
						{
							Type[] genericArguments = t.GetGenericArguments();
							if (genericArguments.Length == 1)
								g.Emit(OpCodes.Newobj, t.GetConstructor(genericArguments));
							else
								throw new ArgumentException($"Default value for {t} is unsupported.");
						}
					}
					else
					{
						g.Emit(OpCodes.Newobj, typeof(IndexOutOfRangeException).GetConstructor(Type.EmptyTypes));
						g.Emit(OpCodes.Throw);
					}

					g.MarkLabel(lblNextArgument);
				}
				else
				{
					throw new ArgumentException($"Unable to find or create a deserializer for type {t}");
				}
			}

			g.EmitCall(OpCodes.Call, method, null);

			if (method.ReturnType == typeof(void))
				g.Emit(OpCodes.Ldnull);
			else
				g.Emit(OpCodes.Box, method.ReturnType);

			g.Emit(OpCodes.Ret);

			Delegate dynFunc = lambda.CreateDelegate(typeof(MsgPackFunc), target);

			s_wrappedMethods.Add(method, dynFunc.Method);
			s_dynfuncMethods.Add(dynFunc.Method, method);

			return (MsgPackFunc)dynFunc;
		}

		#region Func<,> creators, C# why?!
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<Ret>(Func<Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, Ret>(Func<A, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, Ret>(Func<A, B, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, Ret>(Func<A, B, C, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, Ret>(Func<A, B, C, D, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, Ret>(Func<A, B, C, D, E, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, Ret>(Func<A, B, C, D, E, F, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, Ret>(Func<A, B, C, D, E, F, G, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, Ret>(Func<A, B, C, D, E, F, G, H, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, Ret>(Func<A, B, C, D, E, F, G, H, I, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, Ret>(Func<A, B, C, D, E, F, G, H, I, J, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, Ret>(Func<A, B, C, D, E, F, G, H, I, J, K, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, Ret>(Func<A, B, C, D, E, F, G, H, I, J, K, L, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, Ret>(Func<A, B, C, D, E, F, G, H, I, J, K, L, M, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, N, Ret>(Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, Ret>(Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, Ret> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Ret>(Func<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Ret> method) => CreateDelegate(method.Target, method.Method);
		#endregion

		#region Action<> creators, C# again, why?!
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate(Action method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A>(Action<A> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B>(Action<A, B> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C>(Action<A, B, C> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D>(Action<A, B, C, D> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E>(Action<A, B, C, D, E> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F>(Action<A, B, C, D, E, F> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G>(Action<A, B, C, D, E, F, G> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H>(Action<A, B, C, D, E, F, G, H> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I>(Action<A, B, C, D, E, F, G, H, I> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J>(Action<A, B, C, D, E, F, G, H, I, J> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K>(Action<A, B, C, D, E, F, G, H, I, J, K> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L>(Action<A, B, C, D, E, F, G, H, I, J, K, L> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M>(Action<A, B, C, D, E, F, G, H, I, J, K, L, M> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, N>(Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O>(Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O> method) => CreateDelegate(method.Target, method.Method);
		[SecuritySafeCritical] public static MsgPackFunc CreateDelegate<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P>(Action<A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P> method) => CreateDelegate(method.Target, method.Method);
		#endregion

		[SecurityCritical]
		internal static MsgPackFunc CreateCommandDelegate(object target, MethodInfo method, bool remap)
			=> remap ? ConstructCommandDelegateWithSource(target, method) : ConstructDelegate(target, method);

		/// <summary>
		/// If enabled creates a <see cref="MsgPackFunc"/> that remaps input (<see cref="ushort"/> source, <see cref="string"/>[] arguments, <see cref="string"/> raw) to known types:<br />
		/// <b>source</b>: <see cref="ushort"/>, <see cref="uint"/>, <see cref="int"/>, <see cref="bool"/>, <see cref="Remote"/>, or any type constructable from <see cref="Remote"/> including Player types.<br />
		/// <b>arguments</b>: <see cref="string"/>[].<br />
		/// <b>raw</b>: <see cref="string"/>
		/// </summary>
		/// <param name="target">Method's associated instance</param>
		/// <param name="method">Method to wrap</param>
		/// <returns><see cref="MsgPackFunc"/> with remapping and conversion support.</returns>
		/// <exception cref="ArgumentException">When <see cref="SourceAttribute"/> is used on a non supported type.</exception>
		/// <exception cref="TargetParameterCountException">When any requested parameter isn't supported.</exception>
		[SecurityCritical]
		private static MsgPackFunc ConstructCommandDelegateWithSource(object target, MethodInfo method)
		{
			if (s_wrappedMethods.TryGetValue(method, out var existingMethod))
			{
				return (MsgPackFunc)existingMethod.CreateDelegate(typeof(MsgPackFunc), target);
			}

			// Explanation:
			// We get the MsgPack data as follows: [ ushort source, string[] argumentsSplit, string argumentFullLine ]
			// First we read `source` and store it as a local, then we pass argumentsSplit or skip it and then we pass argumentFullLine or ignore it.
			// When there's a parameter annotated by `SourceAttribute` we read our locally stored source and pass (construct) it in place.

			ParameterInfo[] parameters = method.GetParameters();
			bool hasThis = (method.CallingConvention & CallingConventions.HasThis) != 0;

			var lambda = new DynamicMethod($"{method.DeclaringType.FullName}.{method.Name}", typeof(object),
				hasThis
				? new[] { typeof(object), typeof(Remote), typeof(MsgPackDeserializer).MakeByRefType() }
				: new[] { typeof(Remote), typeof(MsgPackDeserializer).MakeByRefType() });

			ILGenerator g = lambda.GetILGenerator();
			g.DeclareLocal(typeof(uint));
			g.DeclareLocal(typeof(RestorePoint));

			OpCode ldarg_deserializer;
			if (hasThis)
			{
				g.Emit(OpCodes.Ldarg_0);
				ldarg_deserializer = OpCodes.Ldarg_2;
			}
			else
			{
				target = null;
				ldarg_deserializer = OpCodes.Ldarg_1;
			}

			g.Emit(ldarg_deserializer);
			g.Emit(OpCodes.Call, GetResultMethod<uint>(DeserializeAsUInt32));
			g.Emit(OpCodes.Stloc_0);

			for (int i = 0, p = 1; i < parameters.Length; ++i)
			{
				var parameter = parameters[i];
				var t = parameter.ParameterType;

				if (Attribute.IsDefined(parameter, typeof(SourceAttribute), true)) // source
				{
					g.Emit(OpCodes.Ldloc_0);

					if (t.IsPrimitive)
					{
						if (t == typeof(int) || t == typeof(uint) || t == typeof(ushort))
						{
							// 16 bit integers are pushed onto the evaluation stack as 32 bit integers
							continue;
						}
						else if (t == typeof(bool))
						{
							g.Emit(OpCodes.Ldc_I4_0);
							g.Emit(OpCodes.Cgt_Un);
							continue;
						}
					}
					else if (t == typeof(Remote))
					{
						g.Emit(OpCodes.Call, ((Func<ushort, Remote>)Remote.Create).Method);
						continue;
					}
					else
					{
						var constructor = t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Remote) }, null);
						if (constructor != null)
						{
							g.Emit(OpCodes.Call, ((Func<ushort, Remote>)Remote.Create).Method);
							g.Emit(OpCodes.Newobj, constructor);
							continue;
						}
					}

					throw new ArgumentException($"{nameof(SourceAttribute)} used on type {t}, this type can't be constructed with parameter Remote.");
				}
				else if (t == typeof(string[]) || t == typeof(object[])) // arguments; convert to string[]
				{
					if (p != 1)
						throw new ArgumentException($"Parameter of type {t} was found at position {p} while it should only be present at position 1");

					++p;

					g.Emit(ldarg_deserializer);
					g.Emit(OpCodes.Call, GetResultMethod<string[]>(DeserializeAsStringArray));
				}
				else if (t == typeof(byte[]))
				{
                    ++p;

                    g.Emit(ldarg_deserializer);
                    g.Emit(OpCodes.Call, GetResultMethod<byte[]>(DeserializeByteArray));
                }
                else if (t == typeof(List<byte>))
                {
                    ++p;

                    g.Emit(ldarg_deserializer);
                    g.Emit(OpCodes.Call, GetResultMethod<List<byte>>(DeserializeAsByteList));
                }
                else if (t == typeof(string) || t == typeof(object)) // raw data; simply pass it on
				{
					if (p == 1)
					{
						// we skip the split command
						g.Emit(ldarg_deserializer);
						g.Emit(OpCodes.Call, GetVoidMethod(Detail.SerializerAccess.SkipObject));
					}
					else if (p != 2)
						throw new ArgumentException($"Parameter of type {t} was found at position {p} while it should only be present at position 2");

					p = 3;

					g.Emit(ldarg_deserializer);
					g.Emit(OpCodes.Call, GetResultMethod<string>(DeserializeAsString));
				}
				else
					throw new TargetParameterCountException($"Command can't be registered with requested remapping, type {t} is not supported.");
			}

			g.EmitCall(OpCodes.Call, method, null);

			if (method.ReturnType == typeof(void))
				g.Emit(OpCodes.Ldnull);
			else
				g.Emit(OpCodes.Box, method.ReturnType);

			g.Emit(OpCodes.Ret);

			Delegate dynFunc = lambda.CreateDelegate(typeof(MsgPackFunc), target);

			s_wrappedMethods.Add(method, dynFunc.Method);
			s_dynfuncMethods.Add(dynFunc.Method, method);

			return (MsgPackFunc)dynFunc;
		}
	}
}
