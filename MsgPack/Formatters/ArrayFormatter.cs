using System;
using System.Reflection.Emit;
using System.Reflection;

namespace MsgPack.Formatters
{
	internal static class ArrayFormatter
	{
		public static Tuple<Serializer, MethodInfo> Build(Type type)
		{
			Type typeArray = type.MakeArrayType();

			string name = $"ArrayFormatter<{type.FullName}>";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);
				{
					MethodBuilder methodSerialize = typeBuilder.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), typeArray });
					{
						var g = methodSerialize.GetILGenerator();
						g.DeclareLocal(typeof(uint)); // length
						g.DeclareLocal(typeof(uint)); // i

						// if (array == null) goto WriteNil()
						Label nilWrite = g.DefineLabel();
						g.Emit(OpCodes.Ldarg_1);
						g.Emit(OpCodes.Ldnull);
						g.Emit(OpCodes.Beq, nilWrite);

						// length = array.Length
						g.Emit(OpCodes.Ldarg_1);
						g.Emit(OpCodes.Ldlen);
						g.Emit(OpCodes.Stloc_0);

						// write header
						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldloc_0);
						g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteArrayHeader", new[] { typeof(uint) }), null);

						// i = 0
						g.Emit(OpCodes.Ldc_I4_0);
						g.Emit(OpCodes.Stloc_1);

						// for (uint i = 0; i < length; ++i)
						{
							Label whileCond = g.DefineLabel();
							Label whileLoop = g.DefineLabel();
							g.Emit(OpCodes.Br_S, whileCond);
							g.MarkLabel(whileLoop);

							// serialize value
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldarg_1);
							g.Emit(OpCodes.Ldloc_1);
							g.Emit(OpCodes.Ldelem, type);
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateSerializer(type), null);

							// ++i
							g.Emit(OpCodes.Ldloc_1);
							g.Emit(OpCodes.Ldc_I4_1);
							g.Emit(OpCodes.Add);
							g.Emit(OpCodes.Stloc_1);

							// i < length
							g.MarkLabel(whileCond);
							g.Emit(OpCodes.Ldloc_1);
							g.Emit(OpCodes.Ldloc_0);
							g.Emit(OpCodes.Blt_Un, whileLoop);
						}
						g.Emit(OpCodes.Ret);

						// write nil
						g.MarkLabel(nilWrite);
						g.Emit(OpCodes.Ldarg_0);
						g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteNil", Type.EmptyTypes), null);
						g.Emit(OpCodes.Ret);
					}

					MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize", MethodAttributes.Public | MethodAttributes.Static,
						typeArray, new[] { typeof(MsgPackDeserializer).MakeByRefType() });
					{
						var g = methodDeserialize.GetILGenerator();
						g.Emit(OpCodes.Ldc_I4_0);
						g.Emit(OpCodes.Newarr, typeArray);
						g.Emit(OpCodes.Ret);
					}

					MethodBuilder methodSerializeObject = typeBuilder.DefineMethod("SerializeObject", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), typeof(object) });
					{
						var g = methodSerializeObject.GetILGenerator();
						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldarg_1);
						g.Emit(OpCodes.Unbox_Any, typeArray);
						g.EmitCall(OpCodes.Call, methodSerialize, null);
						g.Emit(OpCodes.Ret);
					}
				}

				buildType = typeBuilder.CreateType();
			}

			Serializer serializeMethod = new Serializer(buildType.GetMethod("Serialize"),
				(MsgPackObjectSerializer)buildType.GetMethod("SerializeObject").CreateDelegate(typeof(MsgPackObjectSerializer)));

			MethodInfo deserializeMethod = buildType.GetMethod("Deserialize");

			MsgPackRegistry.RegisterSerializer(typeArray, serializeMethod);
			MsgPackRegistry.RegisterDeserializer(typeArray, deserializeMethod);

			return new Tuple<Serializer, MethodInfo>(serializeMethod, deserializeMethod);
		}
	}
}
