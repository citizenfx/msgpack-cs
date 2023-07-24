using System;
using System.Reflection.Emit;
using System.Reflection;
using ConstructorChoice = System.Collections.Generic.KeyValuePair<System.Reflection.ConstructorInfo, System.Reflection.Emit.OpCode>;

namespace MsgPack.Formatters
{
	internal static class TypeFormatter
	{
		//private static readonly MethodInfo s_methodReadByte = typeof(MsgPackDeserializer).GetMethod("ReadByte", BindingFlags.Static | BindingFlags.Public);

		enum ConstructorOptions
		{
			Null = 0,
			Bool,
			Int,
			UInt,
			Float32,
			Float64,

			Count
		}

		enum JumpLabels
		{
			Null = 0,
			Unused_0xC1,
			False, True,
			Bin8, Bin16, Bin32,
			Ext8, Ext16, Ext32,
			Float32, Float64,
			UInt8, UInt16, UInt32, UInt64,
			Int8, Int16, Int32, Int64,
			//FixExt1, FixExt2, FixExt4, FixExt8, FixExt16,
			//Str8, Str16, Str32,
			//Array8, Array16, Array32,
			//Map16, Map32,

			Count,
			First = 0xC0,
			Last = First + Count,
		}

		public static Tuple<Serializer, Deserializer> Build(Type type)
		{
			string name = $"TypeFormatter<{type.FullName}>";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);
				{
					MethodBuilder methodSerialize = typeBuilder.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), type });
					{
						var g = methodSerialize.GetILGenerator();						
						g.Emit(OpCodes.Ret);
					}

					MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize", MethodAttributes.Public | MethodAttributes.Static,
						type, new[] { typeof(MsgPackDeserializer).MakeByRefType() });
					{
						ConstructorChoice[] constructors = SelectConstructors(type);

						var g = methodDeserialize.GetILGenerator();
						g.DeclareLocal(typeof(byte));

						Label lblFixIntPositive = g.DefineLabel(), lblFixIntNegative = g.DefineLabel();
						Label[] labels = new Label[(uint)JumpLabels.Count];

						for (uint i = 0; i < labels.Length; ++i)
						{
							labels[i] = g.DefineLabel();
						}

						g.Emit(OpCodes.Ldarg_0);
						g.EmitCall(OpCodes.Call, ((GetMethod<byte>)MsgPackDeserializer.ReadByte).Method, null);
						g.Emit(OpCodes.Stloc_0);

						// < 0x80 positive fixint
						g.Emit(OpCodes.Ldloc_0);
						g.Emit(OpCodes.Ldc_I4, 0x80);
						g.Emit(OpCodes.Blt_S, lblFixIntPositive);

						// > 0xDF negative fixint
						g.Emit(OpCodes.Ldloc_0);
						g.Emit(OpCodes.Ldc_I4, 0xDF);
						g.Emit(OpCodes.Bgt_S, lblFixIntNegative);

						// jump-table / switch(type - 0xc0)
						g.Emit(OpCodes.Ldloc_0);
						g.Emit(OpCodes.Ldc_I4, (int)JumpLabels.First);
						g.Emit(OpCodes.Sub);
						g.Emit(OpCodes.Switch, labels);

						// can we put the default here?
						g.MarkLabel(labels[(uint)JumpLabels.Unused_0xC1]);
						g.MarkLabel(labels[(uint)JumpLabels.Bin8]);
						g.MarkLabel(labels[(uint)JumpLabels.Bin16]);
						g.MarkLabel(labels[(uint)JumpLabels.Bin32]);
						g.MarkLabel(labels[(uint)JumpLabels.Ext8]);
						g.MarkLabel(labels[(uint)JumpLabels.Ext16]);
						g.MarkLabel(labels[(uint)JumpLabels.Ext32]);

						g.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes));
						g.Emit(OpCodes.Throw);

						var c = constructors[(uint)ConstructorOptions.Int].Key;
						if (c != null)
						{
							// case < 0x80: positive fixint
							g.MarkLabel(lblFixIntPositive);
							g.Emit(OpCodes.Ldloc_0);
							g.Emit(OpCodes.Conv_U4);
							g.Emit(OpCodes.Newobj, c);
							g.Emit(OpCodes.Ret);

							// case > 0xDF negative fixint
							g.MarkLabel(lblFixIntNegative);
							g.Emit(OpCodes.Ldloc_0);
							g.Emit(OpCodes.Ldc_I4, 256);
							g.Emit(OpCodes.Sub);
							g.Emit(OpCodes.Newobj, c);
							g.Emit(OpCodes.Ret);
						}
						else
						{
							g.MarkLabel(lblFixIntPositive);
							g.MarkLabel(lblFixIntNegative);
							g.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes));
							g.Emit(OpCodes.Throw);
						}

						// case 0xc0: null
						g.MarkLabel(labels[(uint)JumpLabels.Null]);
						if (type.IsValueType)
						{
							g.Emit(OpCodes.Newobj, typeof(ArgumentNullException).GetConstructor(Type.EmptyTypes));
							g.Emit(OpCodes.Throw);
						}
						else
						{
							g.Emit(OpCodes.Ldnull);
							g.Emit(OpCodes.Ret);
						}

						// case 0xc2: false
						g.MarkLabel(labels[(uint)JumpLabels.False]);
						SwitchCase(g, OpCodes.Ldc_I4_0, constructors[(uint)ConstructorOptions.Bool]);

						// case 0xc2: true
						g.MarkLabel(labels[(uint)JumpLabels.True]);
						SwitchCase(g, OpCodes.Ldc_I4_1, constructors[(uint)ConstructorOptions.Bool]);

						// case 0xca: float32
						g.MarkLabel(labels[(uint)JumpLabels.Float32]);
						SwitchCase(g, ((GetMethod<float>)MsgPackDeserializer.ReadSingle).Method, constructors[(uint)ConstructorOptions.Float32]);

						// case 0xcb: float64
						g.MarkLabel(labels[(uint)JumpLabels.Float64]);
						SwitchCase(g, ((GetMethod<double>)MsgPackDeserializer.ReadDouble).Method, constructors[(uint)ConstructorOptions.Float64]);

						// case 0xcc: uint8
						g.MarkLabel(labels[(uint)JumpLabels.UInt8]);
						SwitchCase(g, ((GetMethod<byte>)MsgPackDeserializer.ReadUInt8).Method, constructors[(uint)ConstructorOptions.UInt]);

						// case 0xcd: uint16
						g.MarkLabel(labels[(uint)JumpLabels.UInt16]);
						SwitchCase(g, ((GetMethod<ushort>)MsgPackDeserializer.ReadUInt16).Method, constructors[(uint)ConstructorOptions.UInt]);

						// case 0xce: uint32
						g.MarkLabel(labels[(uint)JumpLabels.UInt32]);
						SwitchCase(g, ((GetMethod<uint>)MsgPackDeserializer.ReadUInt32).Method, constructors[(uint)ConstructorOptions.UInt]);

						// case 0xcf: uint64
						g.MarkLabel(labels[(uint)JumpLabels.UInt64]);
						SwitchCase(g, ((GetMethod<ulong>)MsgPackDeserializer.ReadUInt64).Method, constructors[(uint)ConstructorOptions.UInt]);

						// case 0xd0: int8
						g.MarkLabel(labels[(uint)JumpLabels.Int8]);
						SwitchCase(g, ((GetMethod<sbyte>)MsgPackDeserializer.ReadInt8).Method, constructors[(uint)ConstructorOptions.Int]);

						// case 0xd1: int16
						g.MarkLabel(labels[(uint)JumpLabels.Int16]);
						SwitchCase(g, ((GetMethod<short>)MsgPackDeserializer.ReadInt16).Method, constructors[(uint)ConstructorOptions.Int]);

						// case 0xd2: int32
						g.MarkLabel(labels[(uint)JumpLabels.Int32]);
						SwitchCase(g, ((GetMethod<int>)MsgPackDeserializer.ReadInt32).Method, constructors[(uint)ConstructorOptions.Int]);

						// case 0xd3: int64
						g.MarkLabel(labels[(uint)JumpLabels.Int64]);
						SwitchCase(g, ((GetMethod<long>)MsgPackDeserializer.ReadInt64).Method, constructors[(uint)ConstructorOptions.Int]);

						g.Emit(OpCodes.Ret);
					}

					MethodBuilder methodSerializeObject = typeBuilder.DefineMethod("SerializeObject", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), typeof(object) });
					{
						var g = methodSerializeObject.GetILGenerator();
						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldarg_1);
						g.Emit(OpCodes.Unbox_Any, type);
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

			MsgPackRegistry.RegisterSerializer(type, serializeMethod);
			MsgPackRegistry.RegisterDeserializer(type, deserializeMethod);

			return new Tuple<Serializer, Deserializer>(serializeMethod, deserializeMethod);
		}



		/// <summary>
		/// Constructor selection logic only considering single parameter constructors.<br />
		///  * signed and unsigned integers favor biggest storage types.<br />
		///  * <see cref="float"/> and <see cref="double"/> favor itself and falls back to the other if not found.<br />
		///  * Other types only consider their own type.
		/// </summary>
		/// <param name="type">type to construct </param>
		/// <returns>Constructors and conversion operations, indexed by <see cref="ConstructorOptions"/> values.</returns>
		private static ConstructorChoice[] SelectConstructors(Type type)
		{
			var result = new ConstructorChoice[(uint)ConstructorOptions.Count];

			ConstructorInfo[] constructors = type.GetConstructors();
			for (uint i = 0; i < constructors.Length; ++i)
			{
				ConstructorInfo curConstructor = constructors[i];
				ParameterInfo[] parameters = curConstructor.GetParameters();
				if (parameters.Length == 1)
				{
					Type parameterType = parameters[0].ParameterType;
					if (parameterType == typeof(bool))
					{
						result[(uint)ConstructorOptions.Bool] = new ConstructorChoice(curConstructor, OpCodes.Pop);
					}
					else if (parameterType == typeof(byte))
					{
						ref var c = ref result[(uint)ConstructorOptions.UInt];
						if (OpCodes.Conv_U1.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_U1);
					}
					else if (parameterType == typeof(ushort))
					{
						ref var c = ref result[(uint)ConstructorOptions.UInt];
						if (OpCodes.Conv_U2.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_U2);
					}
					else if (parameterType == typeof(uint))
					{
						ref var c = ref result[(uint)ConstructorOptions.UInt];
						if (OpCodes.Conv_U4.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_U4);
					}
					else if (parameterType == typeof(ulong))
					{
						ref var c = ref result[(uint)ConstructorOptions.UInt];
						if (OpCodes.Conv_U8.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_U8);
					}
					else if (parameterType == typeof(sbyte))
					{
						ref var c = ref result[(uint)ConstructorOptions.Int];
						if (OpCodes.Conv_I1.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_I1);
					}
					else if (parameterType == typeof(short))
					{
						ref var c = ref result[(uint)ConstructorOptions.Int];
						if (OpCodes.Conv_I2.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_I2);
					}
					else if (parameterType == typeof(int))
					{
						ref var c = ref result[(uint)ConstructorOptions.Int];
						if (OpCodes.Conv_I4.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_I4);
					}
					else if (parameterType == typeof(long))
					{
						ref var c = ref result[(uint)ConstructorOptions.Int];
						if (OpCodes.Conv_I8.Value > c.Value.Value)
							c = new ConstructorChoice(curConstructor, OpCodes.Conv_I8);
					}
					else if (parameterType == typeof(float))
					{
						result[(uint)ConstructorOptions.Float32] = new ConstructorChoice(curConstructor, OpCodes.Conv_R4);
					}
					else if (parameterType == typeof(double))
					{
						result[(uint)ConstructorOptions.Float64] = new ConstructorChoice(curConstructor, OpCodes.Conv_R8);
					}
				}
			}

			{
				ref var c = ref result[(uint)ConstructorOptions.Float32];
				if (c.Key == null) c = result[(uint)ConstructorOptions.Float64];

				c = ref result[(uint)ConstructorOptions.Float64];
				if (c.Key == null) c = result[(uint)ConstructorOptions.Float32];
			}

			return result;
		}

		private static void SwitchCase(ILGenerator g, OpCode op0, ConstructorChoice constructor)
		{
			if (constructor.Key != null)
			{
				g.Emit(op0);
				g.Emit(OpCodes.Newobj, constructor.Key);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes));
				g.Emit(OpCodes.Throw);
			}
		}

		private static void SwitchCase(ILGenerator g, MethodInfo readMethod, ConstructorChoice constructor)
		{
			if (constructor.Key != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, readMethod, null);
				g.Emit(constructor.Value);
				g.Emit(OpCodes.Newobj, constructor.Key);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes));
				g.Emit(OpCodes.Throw);
			}
		}
	}
}
