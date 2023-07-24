using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using MsgPack.Formatters;

namespace MsgPack
{

	internal delegate void MsgPackObjectSerializer(MsgPackSerializer serializer, object value);
	internal delegate object MsgPackObjectDeserializer(ref MsgPackDeserializer deserializer);

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

	internal readonly struct Deserializer
	{
		public readonly MsgPackObjectDeserializer m_dynamic;
		public readonly MethodInfo m_method;

		public Deserializer(MethodInfo deserializer, MsgPackObjectDeserializer objectDeserializer)
		{
			m_method = deserializer;
			m_dynamic = objectDeserializer;
		}

		public Deserializer(MethodInfo method)
		{
			m_method = method;

			DynamicMethod dynamicMethod = new DynamicMethod(method.DeclaringType.Name,
				typeof(object), new[] { typeof(MsgPackDeserializer).MakeByRefType() }, typeof(Deserializer).Module, true);
			var g = dynamicMethod.GetILGenerator();
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, method, null);
			g.Emit(OpCodes.Box, method.ReturnType);
			g.Emit(OpCodes.Ret);
			m_dynamic = (MsgPackObjectDeserializer)dynamicMethod.CreateDelegate(typeof(MsgPackObjectDeserializer));
		}
	}

	internal static class MsgPackRegistry
	{
		static readonly Dictionary<Type, Serializer> m_serializers = new Dictionary<Type, Serializer>();
		static readonly Dictionary<Type, Deserializer> m_deserializers = new Dictionary<Type, Deserializer>();

		internal static readonly AssemblyBuilder m_assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AwaitResearch"), AssemblyBuilderAccess.RunAndCollect);
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
		}

		public static bool ImplementsGenericTypeDefinition(Type type, Type genericTypeDefinition)
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
					if (type == typeof(bool))
						serializer.Serialize((bool)obj);
					else if (type == typeof(byte))
						serializer.Serialize((byte)obj);
					else if (type == typeof(sbyte))
						serializer.Serialize((sbyte)obj);
					else if (type == typeof(ushort))
						serializer.Serialize((ushort)obj);
					else if (type == typeof(short))
						serializer.Serialize((short)obj);
					else if (type == typeof(uint))
						serializer.Serialize((uint)obj);
					else if (type == typeof(int))
						serializer.Serialize((int)obj);
					else if (type == typeof(ulong))
						serializer.Serialize((ulong)obj);
					else if (type == typeof(long))
						serializer.Serialize((long)obj);
					else if (type == typeof(float))
						serializer.Serialize((float)obj);
					else if (type == typeof(double))
						serializer.Serialize((double)obj);
					else if (type == typeof(char))
						serializer.Serialize((char)obj);
				}
				else if (TryGetSerializer(type, out var methodInfo))
				{
					methodInfo.m_objectSerializer(serializer, obj);
				}
				else
				{
					var newSerializer = BuildSerializer(type);
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
		internal static MsgPackObjectSerializer GetObjectSerializer<T>() => GetObjectSerializer(typeof(T));
		internal static MsgPackObjectSerializer GetObjectSerializer(Type type)
		{
			if (TryGetSerializer(type, out var methodInfo))
				return methodInfo.m_objectSerializer;

			return BuildSerializer(type)?.Item1.m_objectSerializer;
		}

		internal static MethodInfo GetSerializerMethod(Type type)
		{
			if (TryGetSerializer(type, out var methodInfo))
				return methodInfo.m_method;

			return BuildSerializer(type)?.Item1.m_method;
		}

		internal static MethodInfo GetDeserializerMethod(Type type)
		{
			if (TryGetDeserializer(type, out var methodInfo))
				return methodInfo.m_method;

			return BuildSerializer(type)?.Item2.m_method;
		}

		private static Tuple<Serializer, Deserializer> BuildSerializer(Type type)
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

			}
			else
				return TypeFormatter.Build(type);

			return null;
		}

		internal static bool TryGetSerializer(Type type, out Serializer serializer) => m_serializers.TryGetValue(type, out serializer);
		internal static bool TryGetDeserializer(Type type, out Deserializer deserializer) => m_deserializers.TryGetValue(type, out deserializer);

		internal static void RegisterSerializer(Type type, MethodInfo serializer) => m_serializers.Add(type, Serializer.CreateWithObjectWrapper(serializer));
		internal static void RegisterSerializer(Type type, Serializer serializer) => m_serializers.Add(type, serializer);
		internal static void RegisterDeserializer(Type type, MethodInfo deserializer) => m_deserializers.Add(type, new Deserializer(deserializer));
		internal static void RegisterDeserializer(Type type, Deserializer deserializer) => m_deserializers.Add(type, deserializer);
	}
}
