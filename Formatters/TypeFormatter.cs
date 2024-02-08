using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using ConstructorChoice = System.Collections.Generic.KeyValuePair<System.Reflection.ConstructorInfo, System.Reflection.Emit.OpCode>;
using System.Linq;

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
			FixExt1, FixExt2, FixExt4, FixExt8, FixExt16,
			//Str8, Str16, Str32,
			//Array8, Array16, Array32,
			//Map16, Map32,

			Count,
			First = 0xC0,
			Last = First + Count,
		}
		enum JumpLabelsExtra
		{
			// Jump table
			Vector2,
			Vector3,
			Vector4,
			Quaternion,

			Count,
			First = 20,
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
					bool customSerialization = type.GetCustomAttribute<MsgPackSerializableAttribute>() != null;

					MethodBuilder methodSerialize = typeBuilder.DefineMethod("Serialize", MethodAttributes.Public | MethodAttributes.Static,
						typeof(void), new[] { typeof(MsgPackSerializer), type });
					{
						var g = methodSerialize.GetILGenerator();

						var allMembers = type.GetMembers(BindingFlags.Instance | BindingFlags.Public);

						if (type.GetCustomAttribute<MsgPackSerializableAttribute>() is MsgPackSerializableAttribute serializable && serializable.Layout != Layout.Default)
						{
							switch (serializable.Layout)
							{
								case Layout.Indexed:
									{
										uint maxIndex = allMembers.Max(m => m.GetCustomAttribute<IndexAttribute>() is IndexAttribute index ? index.Index : 0);
										MemberInfo[] members = new MemberInfo[maxIndex];

										for (uint i = 0; i < allMembers.Length; ++i)
										{
											var member = allMembers[i];
											if ((member.MemberType & (MemberTypes.Field | MemberTypes.Property)) != 0)
											{
												if (member.GetCustomAttribute<IndexAttribute>() is IndexAttribute index)
												{
													if (members[index.Index] == null)
														members[index.Index] = member;
													else
														throw new FormatException($"Duplicate index, can't add {member.Name} in slot {index.Index} as it's already taken by {members[index.Index].Name}");
												}
											}
										}

										BuildArraySerializeBody(type, g, members, methodSerialize);
									}
									break;

								case Layout.Keyed:
									{
										MemberInfo[] members = Array.FindAll(allMembers, m =>
											(m.MemberType & (MemberTypes.Field | MemberTypes.Property)) != 0
											&& m.GetCustomAttribute<KeyAttribute>() is KeyAttribute);

										BuildMapSerializeBody(type, g, members, methodSerialize);
									}
									break;
							}

						}
						else // no custom layout, fall back to serializing all public fields and properties (verbose)
						{
							MemberInfo[] members = Array.FindAll(allMembers, m =>
								(m.MemberType & (MemberTypes.Field | MemberTypes.Property)) != 0
								&& m.GetCustomAttribute<IgnoreAttribute>() == null);

							BuildMapSerializeBody(type, g, members, methodSerialize);
						}

						g.Emit(OpCodes.Ret);
					}

					MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize", MethodAttributes.Public | MethodAttributes.Static,
						type, new[] { typeof(MsgPackDeserializer).MakeByRefType() });
					{
						ConstructorChoice[] constructors = SelectConstructors(type);

						var g = methodDeserialize.GetILGenerator();
						g.DeclareLocal(typeof(byte));
						g.DeclareLocal(typeof(uint));

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

						// case 0xc3: true
						g.MarkLabel(labels[(uint)JumpLabels.True]);
						SwitchCase(g, OpCodes.Ldc_I4_1, constructors[(uint)ConstructorOptions.Bool]);

						// case 0xc7: Extra 8 bit
						g.MarkLabel(labels[(uint)JumpLabels.Ext8]);
						SwitchCaseExtraType(g, ((GetMethod<byte>)MsgPackDeserializer.ReadUInt8).Method, type);

						// case 0xc8: Extra 16 bit
						g.MarkLabel(labels[(uint)JumpLabels.Ext16]);
						SwitchCaseExtraType(g, ((GetMethod<ushort>)MsgPackDeserializer.ReadUInt16).Method, type);

						// case 0xc9: Extra 32 bit
						g.MarkLabel(labels[(uint)JumpLabels.Ext32]);
						SwitchCaseExtraType(g, ((GetMethod<uint>)MsgPackDeserializer.ReadUInt32).Method, type);

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

						// case 0xd4: fixed extension type
						g.MarkLabel(labels[(uint)JumpLabels.FixExt1]);
						SwitchCaseExtraType(g, 1, type);

						// case 0xd5: fixed size extension type
						g.MarkLabel(labels[(uint)JumpLabels.FixExt2]);
						SwitchCaseExtraType(g, 2, type);

						// case 0xd6: fixed size extension type
						g.MarkLabel(labels[(uint)JumpLabels.FixExt4]);
						SwitchCaseExtraType(g, 4, type);

						// case 0xd7: fixed size extension type
						g.MarkLabel(labels[(uint)JumpLabels.FixExt8]);
						SwitchCaseExtraType(g, 8, type);

						// case 0xd8: fixed size extension type
						g.MarkLabel(labels[(uint)JumpLabels.FixExt16]);
						SwitchCaseExtraType(g, 16, type);

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
						if (type.IsValueType)
							g.Emit(OpCodes.Box, type);

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
		/// Builds the IL code needed for the array serializing
		/// </summary>
		/// <param name="type">Type we're building this serializer for</param>
		/// <param name="g">ILGenerator for writing the body</param>
		/// <param name="members">Filtered and ordered array by <see cref="IndexAttribute"/>, null values are honored by writing <see cref="MsgPackCode.Nil"/></param>
		/// <param name="currentSerializer">Method for recursive calls, i.e.: the caller itself</param>
		/// <exception cref="NotSupportedException"></exception>
		/// <exception cref="ArgumentException"></exception>
		public static void BuildArraySerializeBody(Type type, ILGenerator g, MemberInfo[] members, MethodInfo currentSerializer)
		{
			var methodWriteNil = typeof(MsgPackSerializer).GetMethod("WriteNil");

			// write header
			g.Emit(OpCodes.Ldc_I4, members.Length);
			g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteArrayHeader", new[] { typeof(uint) }), null);

			for (uint i = 0; i < members.Length; ++i)
			{
				switch (members[i])
				{
					case FieldInfo field:
						{
							var methodFieldSerializer = type == field.FieldType
								? currentSerializer
								: MsgPackRegistry.GetOrCreateObjectSerializer(field.FieldType).Method;

							if (methodFieldSerializer == null)
								throw new NotSupportedException($"Requested serializer for {type.Name}.{field.Name} of type {field.FieldType} could not be found.");

							// Value
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldarg_1);
							g.Emit(OpCodes.Ldfld, field);
							g.EmitCall(OpCodes.Call, methodFieldSerializer, null);
						}
						break;

					case PropertyInfo property:
						{
							var methodFieldSerializer = type == property.PropertyType
								? currentSerializer
								: MsgPackRegistry.GetOrCreateObjectSerializer(property.PropertyType).Method;

							if (methodFieldSerializer == null)
								throw new NotSupportedException($"Requested serializer for {type.Name}.{property.Name} of type {property.PropertyType} could not be found.");

							// Value
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldarg_1);
							g.EmitCall(OpCodes.Call, property.GetMethod, null);
							g.EmitCall(OpCodes.Call, methodFieldSerializer, null);
						}
						break;

					case null:
						g.Emit(OpCodes.Ldarg_0);
						g.EmitCall(OpCodes.Call, methodWriteNil, null);
						break;

					default:
						throw new ArgumentException($"Member type {members[i].GetType()} is not supported");
				}
			}

			g.Emit(OpCodes.Ret);
		}

		/// <summary>
		/// Builds the IL code needed for the map serializing
		/// </summary>
		/// <param name="members">Filtered by <see cref="KeyAttribute"/>, null values are ignored</param>
		/// <inheritdoc cref="BuildArraySerializeBody"/>
		public static void BuildMapSerializeBody(Type type, ILGenerator g, MemberInfo[] members, MethodInfo currentSerializer)
		{
			var methodStringSerializer = typeof(MsgPackSerializer).GetMethod("Serialize", new Type[] { typeof(string) });

			// write header
			g.Emit(OpCodes.Ldc_I4, members.Length);
			g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteMapHeader", new[] { typeof(uint) }), null);

			for (uint i = 0; i < members.Length; ++i)
			{
				switch (members[i])
				{
					case FieldInfo field:
						{
							var methodFieldSerializer = type == field.FieldType
								? currentSerializer
								: MsgPackRegistry.GetOrCreateSerializer(field.FieldType);

							if (methodFieldSerializer == null)
								throw new NotSupportedException($"Requested serializer for {type.Name}.{field.Name} of type {field.FieldType} could not be found.");

							// Key
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldstr, field.Name);
							g.EmitCall(OpCodes.Call, methodStringSerializer, null);

							// Value
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldarg_1);
							g.Emit(OpCodes.Ldfld, field);
							g.EmitCall(OpCodes.Call, methodFieldSerializer, null);
						}
						break;

					case PropertyInfo property:
						{
							var methodFieldSerializer = type == property.PropertyType
								? currentSerializer
								: MsgPackRegistry.GetOrCreateSerializer(property.PropertyType);

							if (methodFieldSerializer == null)
								throw new NotSupportedException($"Requested serializer for {type.Name}.{property.Name} of type {property.PropertyType} could not be found.");

							// Key
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldstr, property.Name);
							g.EmitCall(OpCodes.Call, methodStringSerializer, null);

							// Value
							g.Emit(OpCodes.Ldarg_0);
							g.Emit(OpCodes.Ldarg_1);
							g.EmitCall(OpCodes.Call, property.GetMethod, null);
							g.EmitCall(OpCodes.Call, methodFieldSerializer, null);
						}
						break;

					case null:
						// fine, but ignore
						break;

					default:
						throw new ArgumentException($"Member type {members[i].GetType()} is not supported");
				}
			}

			g.Emit(OpCodes.Ret);
		}

		/// <summary>
		/// Constructor selection logic only considering single parameter constructors.<br />
		///  * signed and unsigned integers favor biggest storage types.<br />
		///  * <see cref="float"/> and <see cref="double"/> favor itself and fall back to the other if not found.<br />
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

		private static void SwitchCaseExtraType(ILGenerator g, byte size, Type type)
		{
			// size
			g.Emit(OpCodes.Ldc_I4_S, size);
			g.Emit(OpCodes.Stloc_1);

			SwitchCaseExtraTypeBody(g, type);
		}

		private static void SwitchCaseExtraType(ILGenerator g, MethodInfo methodReadInteger, Type type)
		{
			// size
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Call, methodReadInteger);
			g.Emit(OpCodes.Stloc_1);

			SwitchCaseExtraTypeBody(g, type);
		}


		private static void SwitchCaseExtraTypeBody(ILGenerator g, Type type)
		{
			/*
			g.MarkLabel(endLabel);
			g.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes));
			g.Emit(OpCodes.Ret);

			return;*/

			Label isNot10Label = g.DefineLabel(), isNot11Label = g.DefineLabel(), endLabel = g.DefineLabel();
			Label[] labels = new Label[(uint)JumpLabelsExtra.Count];
			for (uint i = 0; i < labels.Length; ++i)
			{
				labels[i] = g.DefineLabel();
			}

			var methodReadString = ((GetMethod<string, uint>)MsgPackDeserializer.ReadString).Method;
			var methodSkipString = ((GetMethodVoid<uint>)MsgPackDeserializer.SkipString).Method;
			var methodReadSingle = ((GetMethod<float>)MsgPackDeserializer.ReadSingleLE).Method;
			ConstructorInfo constructor = null;
			ConstructorInfo constructorCallback = typeof(Callback).GetConstructor(new Type[] { typeof(string) });

			// type code
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Call, ((GetMethod<byte>)MsgPackDeserializer.ReadByte).Method);
			g.Emit(OpCodes.Stloc_0);

			// case 10: RemoteFunc
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4_S, (byte)10);
			g.Emit(OpCodes.Bne_Un_S, isNot10Label);
			if ((constructor = type.GetConstructor(new Type[] { typeof(Callback) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_1);
				g.Emit(OpCodes.Newobj, constructorCallback);
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_1);
				g.EmitCall(OpCodes.Call, methodSkipString, null);
				g.Emit(OpCodes.Br, endLabel);
			}

			// case 11: LocalFunc
			g.MarkLabel(isNot10Label);
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4_S, (byte)11);
			g.Emit(OpCodes.Bne_Un_S, isNot11Label);
			if ((constructor = type.GetConstructor(new Type[] { typeof(Callback) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_1);
				g.Emit(OpCodes.Newobj, constructorCallback);
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_1);
				g.EmitCall(OpCodes.Call, methodSkipString, null);
				g.Emit(OpCodes.Br, endLabel);
			}

			// switch
			g.MarkLabel(isNot11Label);
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, (int)JumpLabelsExtra.First);
			g.Emit(OpCodes.Sub);
			g.Emit(OpCodes.Switch, labels);
			g.Emit(OpCodes.Br, endLabel);

			// case 20: Vector2
			g.MarkLabel(labels[(int)JumpLabelsExtra.Vector2]);

			if ((constructor = type.GetConstructor(new Type[] { typeof(float), typeof(float) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else if ((constructor = type.GetConstructor(new Type[] { typeof(Vector2) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, typeof(Vector2).GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float) }));
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, ((GetMethodVoid)MsgPackDeserializer.SkipVector2).Method, null);
				g.Emit(OpCodes.Br, endLabel);
			}

			// case 21: Vector3
			g.MarkLabel(labels[(int)JumpLabelsExtra.Vector3]);

			if ((constructor = type.GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float) })) != null)
			{
				g.DeclareLocal(typeof(Vector3));

				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);

				g.Emit(OpCodes.Newobj, constructor);
				g.EmitWriteLine(constructor.ToString());

				g.Emit(OpCodes.Ret);
			}
			else if ((constructor = type.GetConstructor(new Type[] { typeof(Vector3) })) != null)
			{
				g.Emit(OpCodes.Newobj, typeof(MsgPackDeserializer).GetMethod("ReadVector3", Type.EmptyTypes));
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, ((GetMethodVoid)MsgPackDeserializer.SkipVector3).Method, null);
				g.Emit(OpCodes.Br, endLabel);
			}

			// case 22: Vector4
			g.MarkLabel(labels[(int)JumpLabelsExtra.Vector4]);

			if ((constructor = type.GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else if ((constructor = type.GetConstructor(new Type[] { typeof(Vector4) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, typeof(Vector4).GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) }));
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, ((GetMethodVoid)MsgPackDeserializer.SkipVector4).Method, null);
				g.Emit(OpCodes.Br, endLabel);
			}

			// case 23: Quaternion
			g.MarkLabel(labels[(int)JumpLabelsExtra.Quaternion]);

			if ((constructor = type.GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else if ((constructor = type.GetConstructor(new Type[] { typeof(Quaternion) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, typeof(Quaternion).GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float), typeof(float) }));
				g.Emit(OpCodes.Newobj, constructor);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, ((GetMethodVoid)MsgPackDeserializer.SkipQuaternion).Method, null);
				g.Emit(OpCodes.Br, endLabel);
			}

			// Can't create our type, throw exception
			g.MarkLabel(endLabel);
			g.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes));
			g.Emit(OpCodes.Throw);
		}
	}
}
