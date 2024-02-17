using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using ConstructorChoice = System.Collections.Generic.KeyValuePair<System.Reflection.ConstructorInfo, System.Reflection.Emit.OpCode>;
using System.Linq;

using static MsgPack.Details.Helper;

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
			Str8, Str16, Str32,
			Array8, Array16, Array32,
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

		public static Tuple<Serializer, MethodInfo> Build(Type type)
		{
			string name = $"TypeFormatter<{type.FullName}>";
			Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

			if (buildType == null)
			{
				TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);
				{
					MethodInfo methodSerialize = BuildSerializer(type, typeBuilder);
					MethodInfo methodDeserialize = BuildDeserializer(type, typeBuilder);
					
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

			MethodInfo deserializeMethod = buildType.GetMethod("Deserialize");

			MsgPackRegistry.RegisterSerializer(type, serializeMethod);
			MsgPackRegistry.RegisterDeserializer(type, deserializeMethod);

			return new Tuple<Serializer, MethodInfo>(serializeMethod, deserializeMethod);
		}

		#region Serialize

		private static MethodInfo BuildSerializer(Type type, TypeBuilder typeBuilder)
		{
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

				return methodSerialize;
			}
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
		private static void BuildArraySerializeBody(Type type, ILGenerator g, MemberInfo[] members, MethodInfo currentSerializer)
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
		private static void BuildMapSerializeBody(Type type, ILGenerator g, MemberInfo[] members, MethodInfo currentSerializer)
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

		#endregion

		#region Deserialize

		private static MethodInfo BuildDeserializer(Type type, TypeBuilder typeBuilder)
		{
			MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize",
				MethodAttributes.Public | MethodAttributes.Static,
				type, new[] { typeof(MsgPackDeserializer).MakeByRefType() });

			ConstructorChoice[] constructors = SelectConstructors(type);
			Layout layout = type.GetCustomAttribute<MsgPackSerializableAttribute>()?.Layout ?? Layout.Default;

			var g = methodDeserialize.GetILGenerator();
			g.DeclareLocal(typeof(byte));
			g.DeclareLocal(typeof(uint));
			g.DeclareLocal(type);
			g.DeclareLocal(typeof(MsgPackDeserializer.RestorePoint));

			Label lblFixIntPositive = g.DefineLabel(),
				lblFixIntNegative = g.DefineLabel(),
				lblDefault = g.DefineLabel();

			Label[] labels = new Label[(uint)JumpLabels.Count];
			for (uint i = 0; i < labels.Length; ++i)
				labels[i] = g.DefineLabel();

			CallAndStore(g, GetResultMethod(MsgPackDeserializer.ReadByte), OpCodes.Stloc_0);

			// < 0x80 positive fixint
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0x80);
			g.Emit(OpCodes.Blt, lblFixIntPositive);

			// > 0xDF negative fixint
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0xDF);
			g.Emit(OpCodes.Bgt, lblFixIntNegative);

			// jump-table / switch(type - 0xc0)
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, (uint)JumpLabels.First);
			g.Emit(OpCodes.Sub);
			g.Emit(OpCodes.Switch, labels);

			// default, and non existing construction options
			{
				g.MarkLabel(lblDefault);
				g.MarkLabel(labels[(uint)JumpLabels.Unused_0xC1]);
				g.MarkLabel(labels[(uint)JumpLabels.Bin8]);
				g.MarkLabel(labels[(uint)JumpLabels.Bin16]);
				g.MarkLabel(labels[(uint)JumpLabels.Bin32]);

				g.Emit(OpCodes.Newobj, typeof(InvalidCastException).GetConstructor(Type.EmptyTypes));
				g.Emit(OpCodes.Throw);
			}

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
				g.Emit(OpCodes.Br, lblDefault);
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

			SwitchCase(labels[(uint)JumpLabels.False], g, OpCodes.Ldc_I4_0, constructors[(uint)ConstructorOptions.Bool], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.True], g, OpCodes.Ldc_I4_1, constructors[(uint)ConstructorOptions.Bool], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.Float32], g, GetResultMethod(MsgPackDeserializer.ReadSingle), constructors[(uint)ConstructorOptions.Float32], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.Float64], g, GetResultMethod(MsgPackDeserializer.ReadDouble), constructors[(uint)ConstructorOptions.Float64], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.UInt8], g, GetResultMethod(MsgPackDeserializer.ReadUInt8), constructors[(uint)ConstructorOptions.UInt], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.UInt16], g, GetResultMethod(MsgPackDeserializer.ReadUInt16), constructors[(uint)ConstructorOptions.UInt], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.UInt32], g, GetResultMethod(MsgPackDeserializer.ReadUInt32), constructors[(uint)ConstructorOptions.UInt], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.UInt64], g, GetResultMethod(MsgPackDeserializer.ReadUInt64), constructors[(uint)ConstructorOptions.UInt], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.Int8], g, GetResultMethod(MsgPackDeserializer.ReadInt8), constructors[(uint)ConstructorOptions.Int], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.Int16], g, GetResultMethod(MsgPackDeserializer.ReadInt16), constructors[(uint)ConstructorOptions.Int], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.Int32], g, GetResultMethod(MsgPackDeserializer.ReadInt32), constructors[(uint)ConstructorOptions.Int], lblDefault);
			SwitchCase(labels[(uint)JumpLabels.Int64], g, GetResultMethod(MsgPackDeserializer.ReadInt64), constructors[(uint)ConstructorOptions.Int], lblDefault);

			BuildExtraTypesDeserializeBody(g, type, labels, lblDefault);
			BuildStringDeserializeBody(g, type, labels, lblDefault);
			BuildArrayDeserializeBody(g, type, layout, labels, lblDefault);
			
			return methodDeserialize;
		}

		private static void BuildExtraTypesDeserializeBody(ILGenerator g, Type type, Label[] labels, Label defaultLabel)
		{
			Label extraType = g.DefineLabel();

			// case 0xc7: Extra 8 bit
			g.MarkLabel(labels[(uint)JumpLabels.Ext8]);
			CallAndStore(g, GetResultMethod(MsgPackDeserializer.ReadUInt8), OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xc8: Extra 16 bit
			g.MarkLabel(labels[(uint)JumpLabels.Ext16]);
			CallAndStore(g, GetResultMethod(MsgPackDeserializer.ReadUInt16), OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xc9: Extra 32 bit
			g.MarkLabel(labels[(uint)JumpLabels.Ext32]);
			CallAndStore(g, GetResultMethod(MsgPackDeserializer.ReadUInt32), OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xd4: fixed extension type
			g.MarkLabel(labels[(uint)JumpLabels.FixExt1]);
			Store(g, 1, OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xd5: fixed size extension type
			g.MarkLabel(labels[(uint)JumpLabels.FixExt2]);
			Store(g, 2, OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xd6: fixed size extension type
			g.MarkLabel(labels[(uint)JumpLabels.FixExt4]);
			Store(g, 4, OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xd7: fixed size extension type
			g.MarkLabel(labels[(uint)JumpLabels.FixExt8]);
			Store(g, 8, OpCodes.Stloc_1);
			g.Emit(OpCodes.Br, extraType);

			// case 0xd8: fixed size extension type
			g.MarkLabel(labels[(uint)JumpLabels.FixExt16]);
			Store(g, 16, OpCodes.Stloc_1);

			g.MarkLabel(extraType);
			SwitchCaseExtraType(g, type, defaultLabel);
		}

		private static void BuildStringDeserializeBody(ILGenerator g, Type type, Label[] labels, Label defaultLabel)
		{
			ConstructorInfo constructorString = type.GetConstructor(new Type[] { typeof(string) });
			if (constructorString != null)
			{
				Label stringType = g.DefineLabel();

				// case 0xd9: string  with 8 byte sized length
				g.MarkLabel(labels[(uint)JumpLabels.Str8]);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte), null);
				g.Emit(OpCodes.Br, stringType);

				// case 0xda: string  with 16 byte sized length
				g.MarkLabel(labels[(uint)JumpLabels.Str16]);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt16), null);
				g.Emit(OpCodes.Br, stringType);

				// case 0xdb: string  with 32 byte sized length
				g.MarkLabel(labels[(uint)JumpLabels.Str32]);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt32), null);

				g.MarkLabel(stringType);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, GetResultMethod<uint, string>(MsgPackDeserializer.ReadString), null);
				g.Emit(OpCodes.Newobj, constructorString);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.MarkLabel(labels[(uint)JumpLabels.Str8]);
				g.MarkLabel(labels[(uint)JumpLabels.Str16]);
				g.MarkLabel(labels[(uint)JumpLabels.Str32]);
				g.Emit(OpCodes.Br, defaultLabel);
			}
		}

		private static void BuildArrayDeserializeBody(ILGenerator g, Type type, Layout layout, Label[] labels, Label defaultLabel)
		{
			Label objectArrayType = g.DefineLabel();

			g.MarkLabel(labels[(uint)JumpLabels.Array8]);
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte), null);
			g.Emit(OpCodes.Br, objectArrayType);

			g.MarkLabel(labels[(uint)JumpLabels.Array16]);
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt16), null);
			g.Emit(OpCodes.Br, objectArrayType);

			g.MarkLabel(labels[(uint)JumpLabels.Array32]);
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt32), null);

			g.MarkLabel(objectArrayType);
			g.Emit(OpCodes.Stloc_1);

			ConstructorInfo constructorArray = type.GetConstructor(new Type[] { typeof(object[]) });

			// Allow construction by arrays when the type is marked as indexed
			if (layout == Layout.Indexed)
			{
				var members = new Details.DynamicArray<MemberInfo>(type.GetMembers(BindingFlags.Instance | BindingFlags.Public));
				members.RemoveAll(m => (m.MemberType & (MemberTypes.Field | MemberTypes.Property)) == 0
					|| (m.GetCustomAttribute<IgnoreAttribute>() != null && m.GetCustomAttribute<IndexAttribute>() == null));
				members.Sort((l, r) => (long)l.GetCustomAttribute<IndexAttribute>().Index - r.GetCustomAttribute<IndexAttribute>().Index);

				Type[] constructionTypes = new Type[members.Count];
				for (int i = 0; i < members.Count; ++i)
				{
					var member = members[i];
					constructionTypes[i] = (member as FieldInfo)?.FieldType ?? ((PropertyInfo)member).PropertyType;
				}

				Label lengthNotEquals = g.DefineLabel();

				g.Emit(OpCodes.Ldloc_S, (byte)1);
				g.Emit(OpCodes.Ldc_I4, members.Count);
				g.Emit(OpCodes.Bge_Un, lengthNotEquals);

				if (constructorArray != null)
				{
					g.Emit(OpCodes.Ldarg_0);
					g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.CreateRestorePoint), null);
					g.Emit(OpCodes.Stloc_3);
				}

				g.BeginExceptionBlock();
				{
					ConstructorInfo constructor = type.GetConstructor(constructionTypes);
					if (constructor != null)
					{
						for (int i = 0; i < constructionTypes.Length; ++i)
						{
							g.Emit(OpCodes.Ldarg_0);
							var deserializer = MsgPackRegistry.GetOrCreateDeserializer(constructionTypes[i])
								?? throw new NullReferenceException($"Couldn't find deserializer for {constructionTypes[i]}");

							g.EmitCall(OpCodes.Call, deserializer, null);
						}

						g.Emit(OpCodes.Newobj, constructor);
						g.Emit(OpCodes.Stloc_2);
					}
					// create a default object and assign the fields and properties
					else if ((constructor = type.GetConstructor(Type.EmptyTypes)) != null)
					{
						g.Emit(OpCodes.Newobj, constructor);

						for (int i = 0; i < members.Count; ++i)
						{
							var member = members[i];

							g.Emit(OpCodes.Ldarg_0);
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(constructionTypes[i]), null);
							if (member is FieldInfo field)
								g.Emit(OpCodes.Stfld, field);
							else
								g.EmitCall(OpCodes.Call, (member as PropertyInfo).SetMethod, null);
						}

						g.Emit(OpCodes.Stloc_2);
					}
				}
				g.BeginCatchBlock(typeof(Exception));
				{
					if (constructorArray != null)
					{
						g.Emit(OpCodes.Pop); // remove exception

						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldloc_S, (byte)3);
						g.EmitCall(OpCodes.Call, GetVoidMethod<MsgPackDeserializer.RestorePoint>(MsgPackDeserializer.Restore), null);

						g.Emit(OpCodes.Leave, lengthNotEquals);
					}
					else
						g.Emit(OpCodes.Throw); // rethrow, we're not going to try another constructor
				}
				g.EndExceptionBlock();
				g.Emit(OpCodes.Ldloc_S, (byte)2);
				g.Emit(OpCodes.Ret);

				g.MarkLabel(lengthNotEquals);
			}
						
			if (constructorArray != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_S, (byte)1);
				g.EmitCall(OpCodes.Call, GetResultMethod<uint, object[]>(MsgPackDeserializer.ReadObjectArray), null);
				g.Emit(OpCodes.Newobj, constructorArray);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Br, defaultLabel);
			}
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

		private static void SwitchCase(Label label, ILGenerator g, OpCode op0, ConstructorChoice constructor, Label defaultLabel)
		{
			g.MarkLabel(label);

			if (constructor.Key != null)
			{
				g.Emit(op0);
				g.Emit(OpCodes.Newobj, constructor.Key);
				g.Emit(OpCodes.Ret);
			}
			else
				g.Emit(OpCodes.Br, defaultLabel);

		}

		private static void SwitchCase(Label label, ILGenerator g, MethodInfo readMethod, ConstructorChoice constructor, Label defaultLabel)
		{
			g.MarkLabel(label);

			if (constructor.Key != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, readMethod, null);
				g.Emit(constructor.Value);
				g.Emit(OpCodes.Newobj, constructor.Key);
				g.Emit(OpCodes.Ret);
			}
			else
				g.Emit(OpCodes.Br, defaultLabel);
		}

		private static void SwitchCaseExtraType(ILGenerator g, Type type, Label defaultLabel)
		{
			Label isNot10Label = g.DefineLabel(),
				isNot11Label = g.DefineLabel();

			Label[] labels = new Label[(uint)JumpLabelsExtra.Count];
			for (uint i = 0; i < labels.Length; ++i)
				labels[i] = g.DefineLabel();

			var methodReadString = GetResultMethod<uint, string>(MsgPackDeserializer.ReadString);
			var methodSkipString = GetVoidMethod<uint>(MsgPackDeserializer.SkipString);
			var methodReadSingle = GetResultMethod(MsgPackDeserializer.ReadSingleLE);
			ConstructorInfo constructor = null;
			ConstructorInfo constructorCallback = typeof(Callback).GetConstructor(new Type[] { typeof(string) });

			// type code
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte));
			g.Emit(OpCodes.Stloc_0);

			// case 10: RemoteFunc
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4_S, (byte)10);
			g.Emit(OpCodes.Bne_Un, isNot10Label);
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
				g.Emit(OpCodes.Br, defaultLabel);
			}

			// case 11: LocalFunc
			g.MarkLabel(isNot10Label);
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4_S, (byte)11);
			g.Emit(OpCodes.Bne_Un, isNot11Label);
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
				g.Emit(OpCodes.Br, defaultLabel);
			}

			// switch
			g.MarkLabel(isNot11Label);
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, (int)JumpLabelsExtra.First);
			g.Emit(OpCodes.Sub);
			g.Emit(OpCodes.Switch, labels);
			g.Emit(OpCodes.Br, defaultLabel);

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
				g.EmitCall(OpCodes.Call, GetVoidMethod(MsgPackDeserializer.SkipVector2), null);
				g.Emit(OpCodes.Br, defaultLabel);
			}

			// case 21: Vector3
			g.MarkLabel(labels[(int)JumpLabelsExtra.Vector3]);

			if ((constructor = type.GetConstructor(new Type[] { typeof(float), typeof(float), typeof(float) })) != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, methodReadSingle, null);
				g.Emit(OpCodes.Newobj, constructor);
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
				g.EmitCall(OpCodes.Call, GetVoidMethod(MsgPackDeserializer.SkipVector3), null);
				g.Emit(OpCodes.Br, defaultLabel);
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
				g.EmitCall(OpCodes.Call, GetVoidMethod(MsgPackDeserializer.SkipVector4), null);
				g.Emit(OpCodes.Br, defaultLabel);
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
				g.EmitCall(OpCodes.Call, GetVoidMethod(MsgPackDeserializer.SkipQuaternion), null);
				g.Emit(OpCodes.Br, defaultLabel);
			}
		}

		#endregion

		#region General / Helper

		private static void Store(ILGenerator g, byte size, OpCode storeLoc)
		{
			g.Emit(OpCodes.Ldc_I4_S, size);
			g.Emit(storeLoc);
		}

		private static void CallAndStore(ILGenerator g, MethodInfo methodReadInteger, OpCode storeLoc)
		{
			g.Emit(OpCodes.Ldarg_0);
			g.Emit(OpCodes.Call, methodReadInteger);
			g.Emit(storeLoc);
		}

		#endregion
	}
}
