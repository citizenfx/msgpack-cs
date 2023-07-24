using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;

namespace MsgPack.Formatters
{
	internal static class DictionaryFormatter
	{
		public static Tuple<Serializer, Deserializer> Build(Type typeKey, Type typeValue)
		{
			Type typeKeyValuePair = typeof(KeyValuePair<,>).MakeGenericType(typeKey, typeValue);
			Type typeIEnumerator = typeof(IEnumerator<>).MakeGenericType(typeKeyValuePair);
			Type typeIEnumerable = typeof(IEnumerable<>).MakeGenericType(typeKeyValuePair);
			Type typeDictionary = typeof(Dictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIDictionary = typeof(IDictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIReadOnlyDictionary = typeof(IReadOnlyDictionary<,>).MakeGenericType(typeKey, typeValue);
			Type typeIReadOnlyCollection = typeof(IReadOnlyCollection<>).MakeGenericType(typeKeyValuePair);

			string name = $"DictionaryFormatter<{typeKey.FullName}, {typeValue.FullName}>";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);
				{
					// IDictionary<K, V> (de)serialization
					MethodBuilder methodSerialize = typeBuilder.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), typeIDictionary });
					{
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
						g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteMapHeader", new[] { typeof(uint) }), null);

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
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetSerializerMethod(typeKey), null);

							// serialize .Value
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldloca_S, (byte)1);
							g.EmitCall(OpCodes.Call, typeKeyValuePair.GetProperty("Value", Type.EmptyTypes).GetMethod, null);
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetSerializerMethod(typeValue), null);

							// enumerator.MoveNext() condition
							g.MarkLabel(whileCond);
							g.Emit(OpCodes.Ldloc_0);
							g.EmitCall(OpCodes.Callvirt, typeof(IEnumerator).GetMethod("MoveNext", Type.EmptyTypes), null);
							g.Emit(OpCodes.Brtrue_S, whileLoop);
						}
						g.Emit(OpCodes.Ret);

						// write nil
						g.MarkLabel(nilWrite);
						g.Emit(OpCodes.Ldarg_0);
						g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteNil", Type.EmptyTypes), null);
						g.Emit(OpCodes.Ret);
					}

					MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize", MethodAttributes.Public | MethodAttributes.Static,
						typeDictionary, new[] { typeof(MsgPackDeserializer).MakeByRefType() });
					{
						var g = methodDeserialize.GetILGenerator();
						g.Emit(OpCodes.Newobj, typeDictionary.GetConstructor(Type.EmptyTypes));
						g.Emit(OpCodes.Ret);
					}

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

					MethodBuilder methodDeserializeObject = typeBuilder.DefineMethod("DeserializeObject", MethodAttributes.Public | MethodAttributes.Static,
						typeof(object), new[] { typeof(MsgPackDeserializer).MakeByRefType() });
					{
						var g = methodDeserializeObject.GetILGenerator();
						g.Emit(OpCodes.Ldarg_0);
						g.EmitCall(OpCodes.Call, methodDeserialize, null);
						g.Emit(OpCodes.Ret);
					}
				}

				buildType = typeBuilder.CreateType();
			}

			Serializer serializeMethod = new Serializer(buildType.GetMethod("Serialize"),
				(MsgPackObjectSerializer)buildType.GetMethod("SerializeObject").CreateDelegate(typeof(MsgPackObjectSerializer)));

			Deserializer deserializeMethod = new Deserializer(buildType.GetMethod("Deserialize"),
				(MsgPackObjectDeserializer)buildType.GetMethod("DeserializeObject").CreateDelegate(typeof(MsgPackObjectDeserializer)));

			MsgPackRegistry.RegisterSerializer(typeDictionary, serializeMethod);
			MsgPackRegistry.RegisterSerializer(typeIDictionary, serializeMethod);
			MsgPackRegistry.RegisterSerializer(typeIReadOnlyDictionary, serializeMethod);

			MsgPackRegistry.RegisterDeserializer(typeDictionary, deserializeMethod);
			MsgPackRegistry.RegisterDeserializer(typeIDictionary, deserializeMethod);
			MsgPackRegistry.RegisterDeserializer(typeIReadOnlyDictionary, deserializeMethod);

			return new Tuple<Serializer, Deserializer>(serializeMethod, deserializeMethod);
		}
	}
}
