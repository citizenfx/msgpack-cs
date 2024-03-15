using CitizenFX.MsgPack.Formatters;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace CitizenFX.MsgPack
{

	internal delegate void MsgPackObjectSerializer(MsgPackSerializer serializer, object value);
	internal delegate object MsgPackObjectDeserializer(in MsgPackDeserializer deserializer);

	internal readonly struct Serializer
	{
		public readonly MsgPackObjectSerializer m_objectSerializer;
		public readonly MethodInfo m_method;

		public Serializer(MethodInfo serializer, MsgPackObjectSerializer objectSerializer)
		{
			m_method = serializer;
			m_objectSerializer = objectSerializer;
		}

		public static Serializer CreateWithObjectWrapper(MethodInfo method)
		{
			var parameters = method.GetParameters();
			if (parameters.Length == 0)
				throw new ArgumentException("incorrect method was given");

			DynamicMethod dynamicMethod = new DynamicMethod(method.DeclaringType.Name,
				typeof(void), new[] { typeof(MsgPackSerializer), typeof(object) }, typeof(Serializer).Module, true);

			var g = dynamicMethod.GetILGenerator();
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldarg_1);
			g.Emit(OpCodes.Unbox_Any, (method.CallingConvention & CallingConventions.HasThis) != 0
				? parameters[0].ParameterType
				: parameters[1].ParameterType);
			g.EmitCall(OpCodes.Call, method, null);
			g.Emit(OpCodes.Ret);

			return new Serializer(method, (MsgPackObjectSerializer)dynamicMethod.CreateDelegate(typeof(MsgPackObjectSerializer)));

		}
	}

	public static class MsgPackRegistry
	{
		private static readonly Dictionary<Type, Serializer> m_serializers = new Dictionary<Type, Serializer>();
		private static readonly Dictionary<Type, MethodInfo> m_deserializers = new Dictionary<Type, MethodInfo>();

		internal static readonly AssemblyBuilder m_assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CitizenFX.MsgPack.Dynamic"), AssemblyBuilderAccess.RunAndCollect);
		internal static readonly ModuleBuilder m_moduleBuilder = m_assemblyBuilder.DefineDynamicModule("main");

		static MsgPackRegistry()
		{
			MethodInfo[] methods = typeof(MsgPackSerializer).GetMethods();
			for (uint i = 0; i < methods.Length; ++i)
			{
				var method = methods[i];
				var parameters = method.GetParameters();
				if (parameters.Length == 1 && method.Name == "Serialize")
				{
					m_serializers.Add(parameters[0].ParameterType, Serializer.CreateWithObjectWrapper(method));
				}
			}

			methods = typeof(MsgPackDeserializer).GetMethods(BindingFlags.Instance | BindingFlags.Public);
			for (uint i = 0; i < methods.Length; ++i)
			{
				var method = methods[i];
				var parameters = method.GetParameters();
				if (parameters.Length == 0 && method.Name.StartsWith("Deserialize"))
				{
					m_deserializers.Add(method.ReturnType, method);
				}
			}
		}

		private static bool ImplementsGenericTypeDefinition(Type type, Type genericTypeDefinition)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition)
				return true;

			Type[] interfaces = type.GetInterfaces();
			for (uint i = 0; i < interfaces.Length; ++i)
			{
				Type iface = interfaces[i];
				if (iface.IsGenericType && iface.GetGenericTypeDefinition() == genericTypeDefinition)
					return true;
			}

			return type.BaseType != null && ImplementsGenericTypeDefinition(type.BaseType, genericTypeDefinition);
		}

		internal static void Serialize(MsgPackSerializer serializer, object obj)
		{
			if (obj != null)
			{
				Type type = obj.GetType();
				if (type.IsPrimitive)
				{
					switch(obj)
					{
						case bool v: serializer.Serialize(v); break;
						case char v: serializer.Serialize(v); break;

						case byte v: serializer.Serialize(v); break;
						case ushort v: serializer.Serialize(v); break;
						case uint v: serializer.Serialize(v); break;
						case ulong v: serializer.Serialize(v); break;

						case sbyte v: serializer.Serialize(v); break;
						case short v: serializer.Serialize(v); break;
						case int v: serializer.Serialize(v); break;
						case long v: serializer.Serialize(v); break;

						case float v: serializer.Serialize(v); break;
						case double v: serializer.Serialize(v); break;
					}
				}
				else if (TryGetSerializer(type, out var methodInfo))
				{
					methodInfo.m_objectSerializer(serializer, obj);
				}
				else
				{
					var newSerializer = CreateSerializer(type);
					if (newSerializer != null && newSerializer.Item1.m_objectSerializer != null)
						newSerializer.Item1.m_objectSerializer(serializer, obj);
					else
						throw new SerializationException($"Type {type.Name} is not serializable");
				}
			}
			else
				serializer.WriteNil();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static MsgPackObjectSerializer GetOrCreateObjectSerializer<T>() => GetOrCreateObjectSerializer(typeof(T));
		internal static MsgPackObjectSerializer GetOrCreateObjectSerializer(Type type)
		{
			return TryGetSerializer(type, out var methodInfo)
				? methodInfo.m_objectSerializer
				: CreateSerializer(type)?.Item1.m_objectSerializer;
		}

		public static bool EnsureSerializer(Type type) => TryGetSerializer(type, out var _) || CreateSerializer(type)?.Item1.m_method != null;

		internal static MethodInfo GetOrCreateSerializer(Type type)
		{
			return TryGetSerializer(type, out var methodInfo)
				? methodInfo.m_method
				: CreateSerializer(type)?.Item1.m_method;
		}

		internal static MethodInfo GetOrCreateDeserializer(Type type)
		{
			return TryGetDeserializer(type, out var methodInfo)
				? methodInfo
				: CreateSerializer(type)?.Item2;
		}

		private static Tuple<Serializer, MethodInfo> CreateSerializer(Type type)
		{
			if (type.IsPrimitive)
			{
				throw new NotSupportedException("Should've already been registered");
			}
			else if (type.IsArray)
			{
				switch (type.GetArrayRank())
				{
					case 1:
						return ArrayFormatter.Build(type.GetElementType());
				}
			}
			else if (type.IsGenericType)
			{
				var genericTypes = type.GetGenericArguments();
				switch (genericTypes.Length)
				{
					case 1:
						break;
					case 2:
						{
							if (ImplementsGenericTypeDefinition(type, typeof(IDictionary<,>)))
								return DictionaryFormatter.Build(genericTypes[0], genericTypes[1]);

							break;
						}
				}
			}
			else if (type.IsValueType)
			{
				return TypeFormatter.Build(type);
			}
			else
				return TypeFormatter.Build(type);

			return null;
		}

		internal static bool TryGetSerializer(Type type, out Serializer serializer) => m_serializers.TryGetValue(type, out serializer);
		internal static bool TryGetDeserializer(Type type, out MethodInfo deserializer) => m_deserializers.TryGetValue(type, out deserializer);

		internal static void RegisterSerializer(Type type, MethodInfo serializer) => m_serializers.Add(type, Serializer.CreateWithObjectWrapper(serializer));
		internal static void RegisterSerializer(Type type, Serializer serializer) => m_serializers.Add(type, serializer);
		internal static void RegisterDeserializer(Type type, MethodInfo deserializer) => m_deserializers.Add(type, deserializer);
	}
}
