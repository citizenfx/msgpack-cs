using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using static CitizenFX.MsgPack.Detail.Helper;

namespace CitizenFX.MsgPack.Formatters
{
	internal static class DictionaryFormatter
	{
		public static Tuple<Serializer, MethodInfo> Build(Type typeKey, Type typeValue)
		{
			Type typeKeyValuePair = typeof(KeyValuePair<,>).MakeGenericType(typeKey, typeValue);
			Type typeDictionary = typeof(Dictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIDictionary = typeof(IDictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIReadOnlyDictionary = typeof(IReadOnlyDictionary<,>).MakeGenericType(typeKey, typeValue);

			string name = $"DictionaryFormatter<{typeKey.FullName}, {typeValue.FullName}>";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);
				{
					MethodInfo methodSerialize = BuildSerializer(typeKey, typeValue, typeKeyValuePair, typeIDictionary, typeBuilder);
					BuildDeserializer(typeKey, typeValue, typeDictionary, typeBuilder);

					// object (de)serialization
					MethodBuilder methodSerializeObject = typeBuilder.DefineMethod("SerializeObject", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), typeof(object) });
					{
						var g = methodSerializeObject.GetILGenerator();
						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldarg_1);
						g.Emit(OpCodes.Unbox_Any, typeIDictionary);
						g.EmitCall(OpCodes.Call, methodSerialize, null);
						g.Emit(OpCodes.Ret);
					}
				}

				buildType = typeBuilder.CreateType();
			}

			Serializer serializeMethod = new Serializer(buildType.GetMethod("Serialize"),
				(MsgPackObjectSerializer)buildType.GetMethod("SerializeObject").CreateDelegate(typeof(MsgPackObjectSerializer)));

			MethodInfo deserializeMethod = buildType.GetMethod("Deserialize");

			MsgPackRegistry.RegisterSerializer(typeDictionary, serializeMethod);
			MsgPackRegistry.RegisterSerializer(typeIDictionary, serializeMethod);
			MsgPackRegistry.RegisterSerializer(typeIReadOnlyDictionary, serializeMethod);

			MsgPackRegistry.RegisterDeserializer(typeDictionary, deserializeMethod);
			MsgPackRegistry.RegisterDeserializer(typeIDictionary, deserializeMethod);
			MsgPackRegistry.RegisterDeserializer(typeIReadOnlyDictionary, deserializeMethod);

			return new Tuple<Serializer, MethodInfo>(serializeMethod, deserializeMethod);
		}

		private static MethodInfo BuildSerializer(Type typeKey, Type typeValue, Type typeKeyValuePair, Type typeIDictionary, TypeBuilder typeBuilder)
		{
			Type typeIEnumerator = typeof(IEnumerator<>).MakeGenericType(typeKeyValuePair);
			Type typeIEnumerable = typeof(IEnumerable<>).MakeGenericType(typeKeyValuePair);
			Type typeIReadOnlyCollection = typeof(IReadOnlyCollection<>).MakeGenericType(typeKeyValuePair);

			// IDictionary<K, V> (de)serialization
			MethodBuilder methodSerialize = typeBuilder.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Static,
				typeof(void), new[] { typeof(MsgPackSerializer), typeIDictionary });
			
			var g = methodSerialize.GetILGenerator();
			g.DeclareLocal(typeIEnumerator);
			g.DeclareLocal(typeKeyValuePair);

			// if (dictionary == null) goto WriteNil()
			Label nilWrite = g.DefineLabel();
			g.Emit(OpCodes.Ldarg_1);
			g.Emit(OpCodes.Ldnull);
			g.Emit(OpCodes.Beq, nilWrite);

			g.Emit(OpCodes.Ldarg_0);

			// get count
			g.Emit(OpCodes.Ldarg_1);
			g.EmitCall(OpCodes.Callvirt, typeIReadOnlyCollection.GetProperty("Count", Type.EmptyTypes).GetMethod, null);

			// write header
			g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteMapHeader", BindingFlags.Instance | BindingFlags.NonPublic), null);

			// get dictionary enumerator
			g.Emit(OpCodes.Ldarg_1);
			g.EmitCall(OpCodes.Callvirt, typeIEnumerable.GetMethod("GetEnumerator", Type.EmptyTypes), null);
			g.Emit(OpCodes.Stloc_0);

			// while (enumerator.MoveNext())
			{
				Label whileCond = g.DefineLabel();
				Label whileLoop = g.DefineLabel();
				g.Emit(OpCodes.Br_S, whileCond);
				g.MarkLabel(whileLoop);

				// enumerator.Current
				g.Emit(OpCodes.Ldloc_0);
				g.EmitCall(OpCodes.Callvirt, typeIEnumerator.GetProperty("Current", Type.EmptyTypes).GetMethod, null);
				g.Emit(OpCodes.Stloc_1);

				// serialize .Key
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloca_S, (byte)1);
				g.EmitCall(OpCodes.Call, typeKeyValuePair.GetProperty("Key", Type.EmptyTypes).GetMethod, null);
				g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateSerializer(typeKey), null);

				// serialize .Value
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloca_S, (byte)1);
				g.EmitCall(OpCodes.Call, typeKeyValuePair.GetProperty("Value", Type.EmptyTypes).GetMethod, null);
				g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateSerializer(typeValue), null);

				// enumerator.MoveNext() condition
				g.MarkLabel(whileCond);
				g.Emit(OpCodes.Ldloc_0);
				g.EmitCall(OpCodes.Callvirt, typeof(IEnumerator).GetMethod("MoveNext", Type.EmptyTypes), null);
				g.Emit(OpCodes.Brtrue, whileLoop);
			}
			g.Emit(OpCodes.Ret);

			// write nil
			g.MarkLabel(nilWrite);
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteNil", Type.EmptyTypes), null);
			g.Emit(OpCodes.Ret);

			return methodSerialize;
		}

		private static MethodInfo BuildDeserializer(Type typeKey, Type typeValue, Type typeDictionary, TypeBuilder typeBuilder)
		{
			MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize",
				MethodAttributes.Public | MethodAttributes.Static,
				typeDictionary, new[] { typeof(MsgPackDeserializer).MakeByRefType() });
			
			var g = methodDeserialize.GetILGenerator();
			g.DeclareLocal(typeof(uint)); // type first size after
			g.DeclareLocal(typeof(uint));
			g.DeclareLocal(typeDictionary);

			// get type
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte), null);
			g.Emit(OpCodes.Stloc_0);

			// if (array == null) return null
			Label nilWrite = g.DefineLabel();
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, (int)MsgPackCode.Nil);
			g.Emit(OpCodes.Beq, nilWrite);

			// get size and create array with the read size
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldloc_0);
			g.EmitCall(OpCodes.Call, GetResultMethod<byte, uint>(MsgPackDeserializer.ReadMapSize), null);
			g.Emit(OpCodes.Stloc_0); // use loc_0 as size now

			// loc_2 = new Dictionary<K, V>();
			g.Emit(OpCodes.Newobj, typeDictionary.GetConstructor(Type.EmptyTypes));
			g.Emit(OpCodes.Stloc_2);

			// i = 0
			g.Emit(OpCodes.Ldc_I4_0);
			g.Emit(OpCodes.Stloc_1);

			// for (uint i = 0; i < length; ++i)
			{
				Label whileCond = g.DefineLabel();
				Label whileLoop = g.DefineLabel();
				g.Emit(OpCodes.Br_S, whileCond);
				g.MarkLabel(whileLoop);

				// dictionary prestacking [ array, value ]
				g.Emit(OpCodes.Ldloc_2);

				// deserialize key
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(typeKey), null);

				// deserialize value
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(typeValue), null);

				// store it
				g.EmitCall(OpCodes.Call, typeDictionary.GetMethod("Add", new[] { typeKey, typeValue }), null);

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

			g.Emit(OpCodes.Ldloc_2);
			g.Emit(OpCodes.Ret);

			// return null
			g.MarkLabel(nilWrite);
			g.Emit(OpCodes.Ldnull);
			g.Emit(OpCodes.Ret);

			return methodDeserialize;
		}
	}
}
