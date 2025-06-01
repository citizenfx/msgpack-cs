using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using static CitizenFX.MsgPack.Detail.Helper;
using static CitizenFX.MsgPack.Detail.SerializerAccess;

namespace CitizenFX.MsgPack.Formatters
{
	internal static class DictionaryFormatter
	{
		public static Tuple<Serializer, MethodInfo> Build(Type typeKey, Type typeValue)
		{
#if IS_FXSERVER
			MethodInfo methodSerialize, methodDeserialize, methodObjectSerialize;
#else
			MethodInfo methodDeserialize;
#endif

			Type typeKeyValuePair = typeof(KeyValuePair<,>).MakeGenericType(typeKey, typeValue);
			Type typeDictionary = typeof(Dictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIDictionary = typeof(IDictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIReadOnlyDictionary = typeof(IReadOnlyDictionary<,>).MakeGenericType(typeKey, typeValue);

			string name = $"DictionaryFormatter_{typeKey.FullName}_{typeValue.FullName}";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);

#if IS_FXSERVER
				methodSerialize = BuildSerializer(typeKey, typeValue, typeKeyValuePair, typeIDictionary, typeBuilder);
				BuildDeserializer(typeKey, typeValue, typeDictionary, typeBuilder);
				BuildObjectSerializer(typeIDictionary, methodSerialize, typeBuilder);
#else
				BuildDeserializer(typeKey, typeValue, typeDictionary, typeBuilder);
#endif

				buildType = typeBuilder.CreateType();
			}

#if IS_FXSERVER
			methodSerialize = buildType.GetMethod("Serialize", new[] { typeof(MsgPackSerializer), typeIDictionary });
			methodDeserialize = buildType.GetMethod("Deserialize");
			methodObjectSerialize = buildType.GetMethod("Serialize", new[] { typeof(MsgPackSerializer), typeof(object) });
#else
			methodDeserialize = buildType.GetMethod("Deserialize");
#endif


#if IS_FXSERVER
			Serializer serializeMethod = new Serializer(methodSerialize, methodObjectSerialize);
			MsgPackRegistry.RegisterSerializer(typeDictionary, serializeMethod);
			MsgPackRegistry.RegisterSerializer(typeIDictionary, serializeMethod);
			MsgPackRegistry.RegisterSerializer(typeIReadOnlyDictionary, serializeMethod);
#else
			Serializer serializeMethod = new Serializer();
#endif

			MsgPackRegistry.RegisterDeserializer(typeDictionary, methodDeserialize);
			MsgPackRegistry.RegisterDeserializer(typeIDictionary, methodDeserialize);
			MsgPackRegistry.RegisterDeserializer(typeIReadOnlyDictionary, methodDeserialize);

			return new Tuple<Serializer, MethodInfo>(serializeMethod, methodDeserialize);
		}

		/// <summary>
		/// Simply unpacks and calls <paramref name="methodSerialize"/>
		/// </summary>
		/// <param name="typeIDictionary">Type we're serializing</param>
		/// <param name="methodSerialize">Method to call once the object is unpacked</param>
		/// <param name="typeBuilder">Building type to add this method to</param>
		/// <returns></returns>
		private static MethodInfo BuildObjectSerializer(Type typeIDictionary, MethodInfo methodSerialize, TypeBuilder typeBuilder)
		{
			MethodBuilder methodSerializeObject = typeBuilder.DefineMethod("Serialize",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(void), new[] { typeof(MsgPackSerializer), typeof(object) });

			var g = methodSerializeObject.GetILGenerator();
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldarg_1);
			g.Emit(OpCodes.Unbox_Any, typeIDictionary);
			g.EmitCall(OpCodes.Call, methodSerialize, null);
			g.Emit(OpCodes.Ret);

			return methodSerializeObject;
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
			g.EmitCall(OpCodes.Callvirt, typeIReadOnlyCollection.GetProperty("Count", Type.EmptyTypes).GetGetMethod(), null);

			// write header
			g.EmitCall(OpCodes.Call, GetVoidMethod<uint>(WriteMapHeader), null);

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
				g.EmitCall(OpCodes.Callvirt, typeIEnumerator.GetProperty("Current", Type.EmptyTypes).GetGetMethod(), null);
				g.Emit(OpCodes.Stloc_1);

				// serialize .Key
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloca_S, (byte)1);
				g.EmitCall(OpCodes.Call, typeKeyValuePair.GetProperty("Key", Type.EmptyTypes).GetGetMethod(), null);
				g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateSerializer(typeKey), null);

				// serialize .Value
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloca_S, (byte)1);
				g.EmitCall(OpCodes.Call, typeKeyValuePair.GetProperty("Value", Type.EmptyTypes).GetGetMethod(), null);
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
			g.EmitCall(OpCodes.Call, GetVoidMethod(WriteNil), null);
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
			g.EmitCall(OpCodes.Call, GetResultMethod(ReadByte), null);
			g.Emit(OpCodes.Stloc_0);

			// if (array == null) return null
			Label nilWrite = g.DefineLabel();
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, (int)MsgPackCode.Nil);
			g.Emit(OpCodes.Beq, nilWrite);

			// get size and create array with the read size
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Ldloc_0);
			g.EmitCall(OpCodes.Call, GetResultMethod<byte, uint>(ReadMapSize), null);
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
