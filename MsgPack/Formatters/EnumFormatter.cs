using System;
using System.Reflection;
using System.Reflection.Emit;

namespace CitizenFX.MsgPack.Formatters
{
	internal static class EnumFormatter
	{
		public static Tuple<Serializer, MethodInfo> Build(Type enumType, Type unused = null)
		{
			string name = $"EnumFormatter_{enumType.FullName}";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			MethodInfo methodSerialize, methodDeserialize, methodObjectSerialize;

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);

				methodSerialize = BuildSerializer(enumType, typeBuilder);
				BuildDeserializer(enumType, typeBuilder);
				BuildObjectSerializer(enumType, methodSerialize, typeBuilder);

				buildType = typeBuilder.CreateType();
			}

			methodSerialize = buildType.GetMethod("Serialize", new[] { typeof(MsgPackSerializer), enumType });
			methodDeserialize = buildType.GetMethod("Deserialize");
			methodObjectSerialize = buildType.GetMethod("Serialize", new[] { typeof(MsgPackSerializer), typeof(object) });

			Serializer serializer = new Serializer(methodSerialize, methodObjectSerialize);

			MsgPackRegistry.RegisterSerializer(enumType, serializer);
			MsgPackRegistry.RegisterDeserializer(enumType, methodDeserialize);

			return new Tuple<Serializer, MethodInfo>(serializer, methodDeserialize);
		}

		private static MethodInfo BuildObjectSerializer(Type enumType, MethodInfo methodSerialize, TypeBuilder typeBuilder)
		{
			MethodBuilder method = typeBuilder.DefineMethod("Serialize",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(void),
				new[] { typeof(MsgPackSerializer), typeof(object) });

			var g = method.GetILGenerator();
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldarg_1);
			g.Emit(OpCodes.Unbox_Any, enumType);
			g.EmitCall(OpCodes.Call, methodSerialize, null);
			g.Emit(OpCodes.Ret);

			return method;
		}

		private static MethodInfo BuildSerializer(Type enumType, TypeBuilder typeBuilder)
		{
			MethodBuilder method = typeBuilder.DefineMethod("Serialize",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(void),
				new[] { typeof(MsgPackSerializer), enumType });

			var g = method.GetILGenerator();
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldarg_1);
			g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateSerializer(typeof(uint)), null);
			g.Emit(OpCodes.Ret);
			return method;
		}

		private static MethodInfo BuildDeserializer(Type enumType, TypeBuilder typeBuilder)
		{
			MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize",
				MethodAttributes.Public | MethodAttributes.Static,
				enumType, new[] { typeof(MsgPackDeserializer).MakeByRefType() });
			var g = methodDeserialize.GetILGenerator();
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(typeof(uint)), null);
			g.Emit(OpCodes.Ret);
			return methodDeserialize;
		}
	}
}
