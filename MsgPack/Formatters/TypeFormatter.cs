using CitizenFX.Core;
using System;
using System.Reflection.Emit;
using System.Reflection;
using ConstructorChoice = System.Collections.Generic.KeyValuePair<System.Reflection.ConstructorInfo, System.Reflection.Emit.OpCode>;
using System.Linq;

using static CitizenFX.MsgPack.Detail.Helper;
using CitizenFX.MsgPack.Detail;
using System.Runtime.InteropServices;

namespace CitizenFX.MsgPack.Formatters
{
	internal static class TypeFormatter
	{
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
			Array16, Array32,
			Map16, Map32,

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
					BuildDeserializer(type, typeBuilder);
					
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

				if (type.GetCustomAttribute<MsgPackSerializableAttribute>() is MsgPackSerializableAttribute serializable && serializable.Layout != Layout.Default)
				{
					switch (serializable.Layout)
					{
						case Layout.Indexed:
							{
								var allMembers = GetMembersIndexed(type);
								var members = new MemberInfo[allMembers.Count];

								for (uint i = 0; i < allMembers.Count; ++i)
								{
									var member = allMembers[i];
									if (member.GetCustomAttribute<IndexAttribute>() is IndexAttribute index)
									{
										if (members[index.Index] == null)
											members[index.Index] = member;
										else
											throw new FormatException($"Duplicate index, can't add {member.Name} in slot {index.Index} as it's already taken by {members[index.Index].Name}");
									}
								}

								BuildSerializeArrayBody(type, g, members, methodSerialize);
							}
							break;

						case Layout.Keyed:
							BuildSerializeMapBody(type, g, GetMembersMapped(type), methodSerialize);
							break;
					}

				}
				else // no custom layout, fall back to serializing all public fields and properties (verbose)
				{
					BuildSerializeMapBody(type, g, GetMembersDefault(type), methodSerialize);
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
		private static void BuildSerializeArrayBody(Type type, ILGenerator g, MemberInfo[] members, MethodInfo currentSerializer)
		{
			var methodWriteNil = typeof(MsgPackSerializer).GetMethod("WriteNil");

			// write header
			g.Emit(OpCodes.Ldc_I4, members.Length);
			g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteArrayHeader", BindingFlags.Instance | BindingFlags.NonPublic), null);

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
		/// <param name="type">Current type we are building a serializer for</param>
		/// <param name="g">Method IL generator</param>
		/// <param name="members">Filtered by <see cref="KeyAttribute"/>, null values are ignored</param>
		/// <param name="currentSerializer">The current method, to prevent direct recursive creation</param>
		/// <inheritdoc cref="BuildSerializeArrayBody"/>
		private static void BuildSerializeMapBody(Type type, ILGenerator g, DynamicArray<MemberInfo> members, MethodInfo currentSerializer)
		{
			var methodStringSerializer = typeof(MsgPackSerializer).GetMethod("Serialize", new Type[] { typeof(string) });

			// write header
			g.Emit(OpCodes.Ldc_I4, members.Count);
			g.EmitCall(OpCodes.Call, typeof(MsgPackSerializer).GetMethod("WriteMapHeader", BindingFlags.Instance | BindingFlags.NonPublic), null);

			for (uint i = 0; i < members.Count; ++i)
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
			g.DeclareLocal(typeof(uint)); // code and length
			g.DeclareLocal(typeof(uint)); // size and i iterator
			g.DeclareLocal(type);
			g.DeclareLocal(typeof(MsgPackDeserializer.RestorePoint));
			g.DeclareLocal(typeof(uint)); // sub size

			Label lblFixIntPositive = g.DefineLabel(),
				lblFixIntNegative = g.DefineLabel(),
				lblFixMap = g.DefineLabel(),
				lblFixArray = g.DefineLabel(),
				lblFixString = g.DefineLabel(),
				lblDefault = g.DefineLabel();

			Label[] labels = new Label[(uint)JumpLabels.Count];
			for (uint i = 0; i < labels.Length; ++i)
				labels[i] = g.DefineLabel();

			CallAndStore(g, GetResultMethod(MsgPackDeserializer.ReadByte), OpCodes.Stloc_0);

			// < 0x80 positive fixint
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0x80);
			g.Emit(OpCodes.Blt, lblFixIntPositive);

			// 0x80 - 0x8f fixmap
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0x90);
			g.Emit(OpCodes.Blt, lblFixMap);

			// 0x90 - 0x9f fixarray
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0xA0);
			g.Emit(OpCodes.Blt, lblFixArray);

			// 0xa0 - 0xbf fixstr
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0xC0);
			g.Emit(OpCodes.Blt, lblFixString);

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

			BuildDeserializeExtraTypesBody(g, type, labels, lblDefault);
			BuildDeserializeStringBody(g, type, labels, lblFixString, lblDefault);
			BuildDeserializeArrayBody(g, type, layout, labels, lblFixArray, lblDefault);
			BuildDeserializeMapBody(g, type, layout, labels, lblFixMap, lblDefault);
			
			return methodDeserialize;
		}

		private static void BuildDeserializeExtraTypesBody(ILGenerator g, Type type, Label[] labels, Label defaultLabel)
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

		private static void BuildDeserializeStringBody(ILGenerator g, Type type, Label[] labels, Label fixStrLabel, Label defaultLabel)
		{
			ConstructorInfo constructorString = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public	| BindingFlags.NonPublic,
				null, CallingConventions.HasThis, new Type[] { typeof(string) }, null);

			if (constructorString != null)
			{
				Label stringType = g.DefineLabel();

				// 0xa0 - 0xbf
				g.MarkLabel(fixStrLabel);
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_0);
				g.Emit(OpCodes.Ldc_I4, 0xa0);
				g.Emit(OpCodes.Sub);
				g.Emit(OpCodes.Br, stringType);

				// case 0xd9: string with 8 byte sized length
				g.MarkLabel(labels[(uint)JumpLabels.Str8]);
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Dup);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte), null);
				g.Emit(OpCodes.Br, stringType);

				// case 0xda: string with 16 byte sized length
				g.MarkLabel(labels[(uint)JumpLabels.Str16]);
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Dup);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt16), null);
				g.Emit(OpCodes.Br, stringType);

				// case 0xdb: string with 32 byte sized length
				g.MarkLabel(labels[(uint)JumpLabels.Str32]);
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Dup);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt32), null);

				g.MarkLabel(stringType);
				g.EmitCall(OpCodes.Call, GetResultMethod<uint, string>(MsgPackDeserializer.ReadString), null);
				g.Emit(OpCodes.Newobj, constructorString);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.MarkLabel(fixStrLabel);
				g.MarkLabel(labels[(uint)JumpLabels.Str8]);
				g.MarkLabel(labels[(uint)JumpLabels.Str16]);
				g.MarkLabel(labels[(uint)JumpLabels.Str32]);
				g.Emit(OpCodes.Br, defaultLabel);
			}
		}

		private static void BuildDeserializeArrayBody(ILGenerator g, Type type, Layout layout, Label[] labels, Label fixArrayLabel, Label defaultLabel)
		{
			Label arrayLabel = g.DefineLabel();

			// 0x90 - 0x9f
			g.MarkLabel(fixArrayLabel);
			g.Emit(OpCodes.Ldloc_0);
			g.Emit(OpCodes.Ldc_I4, 0x90);
			g.Emit(OpCodes.Sub);
			g.Emit(OpCodes.Br, arrayLabel);

			g.MarkLabel(labels[(uint)JumpLabels.Array16]);
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt16), null);
			g.Emit(OpCodes.Br, arrayLabel);

			g.MarkLabel(labels[(uint)JumpLabels.Array32]);
			g.Emit(OpCodes.Ldarg_0);
			g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt32), null);

			g.MarkLabel(arrayLabel);
			g.Emit(OpCodes.Stloc_1);

			ConstructorInfo constructorArray = type.GetConstructor(new Type[] { typeof(object[]) });

			// Allow construction by arrays when the type is marked as indexed
			if (layout == Layout.Indexed)
			{
				var members = GetMembersIndexed(type);
				members.Sort((l, r) => (long)l.GetCustomAttribute<IndexAttribute>().Index - r.GetCustomAttribute<IndexAttribute>().Index);

				Type[] constructionTypes = new Type[members.Count];
				for (int i = 0; i < members.Count; ++i)
				{
					var member = members[i];
					constructionTypes[i] = (member as FieldInfo)?.FieldType ?? ((PropertyInfo)member).PropertyType;
				}

				Label arrayNotBigEnough = g.DefineLabel();

				g.Emit(OpCodes.Ldloc_1);
				g.Emit(OpCodes.Ldc_I4, members.Count);
				g.Emit(OpCodes.Blt_Un, arrayNotBigEnough);

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
						int valueIndex = 0;
						for (; valueIndex < constructionTypes.Length; ++valueIndex)
						{
							g.Emit(OpCodes.Ldarg_0);
							var deserializer = MsgPackRegistry.GetOrCreateDeserializer(constructionTypes[valueIndex])
								?? throw new NullReferenceException($"Couldn't find deserializer for {constructionTypes[valueIndex]}");

							g.EmitCall(OpCodes.Call, deserializer, null);
						}

						// skip remaining objects in this array
						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldloc_1);
						g.Emit(OpCodes.Ldc_I4, valueIndex);
						g.Emit(OpCodes.Sub);
						g.EmitCall(OpCodes.Call, GetVoidMethod<uint>(MsgPackDeserializer.SkipObjects), null);

						g.Emit(OpCodes.Newobj, constructor);
						g.Emit(OpCodes.Stloc_2);
					}
					// create a default object and assign the fields and properties
					else if ((constructor = type.GetConstructor(Type.EmptyTypes)) != null)
					{
						g.Emit(OpCodes.Newobj, constructor);

						int valueIndex = 0;
						for (; valueIndex < members.Count; ++valueIndex)
						{
							var member = members[valueIndex];

							g.Emit(OpCodes.Ldarg_0);
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(constructionTypes[valueIndex]), null);
							if (member is FieldInfo field)
								g.Emit(OpCodes.Stfld, field);
							else
								g.EmitCall(OpCodes.Call, (member as PropertyInfo).SetMethod, null);
						}

						// skip remaining objects in this array
						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldloc_1);
						g.Emit(OpCodes.Ldc_I4, valueIndex);
						g.Emit(OpCodes.Sub);
						g.EmitCall(OpCodes.Call, GetVoidMethod<uint>(MsgPackDeserializer.SkipObjects), null);

						g.Emit(OpCodes.Stloc_2);
					}
				}
				g.BeginCatchBlock(typeof(Exception));
				{
					if (constructorArray != null)
					{
						g.Emit(OpCodes.Pop); // remove exception

						g.Emit(OpCodes.Ldarg_0);
						g.Emit(OpCodes.Ldloc_3);
						g.EmitCall(OpCodes.Call, GetVoidMethod<MsgPackDeserializer.RestorePoint>(MsgPackDeserializer.Restore), null);

						g.Emit(OpCodes.Leave, arrayNotBigEnough);
					}
					else
						g.Emit(OpCodes.Throw); // rethrow, we're not going to try another constructor
				}
				g.EndExceptionBlock();
				g.Emit(OpCodes.Ldloc_2);
				g.Emit(OpCodes.Ret);

				g.MarkLabel(arrayNotBigEnough);
			}
						
			if (constructorArray != null)
			{
				g.Emit(OpCodes.Ldarg_0);
				g.Emit(OpCodes.Ldloc_1);
				g.EmitCall(OpCodes.Call, GetResultMethod<uint, object[]>(MsgPackDeserializer.ReadObjectArray), null);
				g.Emit(OpCodes.Newobj, constructorArray);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.Emit(OpCodes.Br, defaultLabel);
			}
		}

		private static unsafe void BuildDeserializeMapBody(ILGenerator g, Type type, Layout layout, Label[] labels, Label fixMapLabel, Label defaultLabel)
		{
			ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes);
			if (constructor != null || type.IsValueType)
			{
				var members = layout == Layout.Keyed ? GetMembersMapped(type) : GetMembersIndexed(type);
				members.Sort((l, r) => (l.Name.Length - r.Name.Length) * 1024 + l.Name.CompareTo(r.Name));

				Label mapType = g.DefineLabel();

				// 0x80 - 0x8f
				g.MarkLabel(fixMapLabel);
				g.Emit(OpCodes.Ldloc_0);
				g.Emit(OpCodes.Ldc_I4, 0x80);
				g.Emit(OpCodes.Sub);
				g.Emit(OpCodes.Br, mapType);

				g.MarkLabel(labels[(uint)JumpLabels.Map16]);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt16), null);
				g.Emit(OpCodes.Br, mapType);

				g.MarkLabel(labels[(uint)JumpLabels.Map32]);
				g.Emit(OpCodes.Ldarg_0);
				g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt32), null);

				g.MarkLabel(mapType);
				g.Emit(OpCodes.Stloc_0);

				// reset i
				g.Emit(OpCodes.Ldc_I4_0);
				g.Emit(OpCodes.Stloc_1);

				// create object
				if (type.IsValueType)
				{
					g.Emit(OpCodes.Ldloca_S, (byte)2);
					g.Emit(OpCodes.Initobj, type);
				}
				else
				{
					g.Emit(OpCodes.Newobj, constructor);
					g.Emit(OpCodes.Stloc_2);
				}

				// iterate key/values				
				Label whileCond = g.DefineLabel();
				Label whileLoop = g.DefineLabel();
				g.Emit(OpCodes.Br, whileCond);
				g.MarkLabel(whileLoop);

				// load string
				{
					var lblStringCases = new Label[] { g.DefineLabel(), g.DefineLabel(), g.DefineLabel() };
					
					g.Emit(OpCodes.Ldarg_0);
					g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte), null);

					g.Emit(OpCodes.Ldc_I4, (uint)MsgPackCode.Str8);
					g.Emit(OpCodes.Sub);
					g.Emit(OpCodes.Switch, lblStringCases);
					g.Emit(OpCodes.Br, defaultLabel);

					Label stringType = g.DefineLabel();
					// case 0xd9: string  with 8 byte sized length
					g.MarkLabel(lblStringCases[0]);
					g.Emit(OpCodes.Ldarg_0);
					g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadByte), null);
					g.Emit(OpCodes.Br, stringType);

					// case 0xda: string  with 16 byte sized length
					g.MarkLabel(lblStringCases[1]);
					g.Emit(OpCodes.Ldarg_0);
					g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt16), null);
					g.Emit(OpCodes.Br, stringType);

					// case 0xdb: string  with 32 byte sized length
					g.MarkLabel(lblStringCases[2]);
					g.Emit(OpCodes.Ldarg_0);
					g.EmitCall(OpCodes.Call, GetResultMethod(MsgPackDeserializer.ReadUInt32), null);

					g.MarkLabel(stringType);
					g.Emit(OpCodes.Stloc_S, (byte)4);

					g.Emit(OpCodes.Ldarg_0);
					g.Emit(OpCodes.Ldloc_S, (byte)4);
					g.EmitCall(OpCodes.Call, GetResultMethod<uint, CString>(MsgPackDeserializer.ReadCString), null);
				}

				int memberLeft = 0, memberEnd = 0;
				int memberCurSize = members[memberEnd].Name.Length, memberNextSize = memberCurSize;

				while (memberEnd < members.Count)
				{
					// keep searching until we hit a name that's not of the same length
					while (++memberEnd < members.Count && (memberNextSize = members[memberEnd].Name.Length) == memberCurSize) ;

					Label lblSizeNotEqual = g.DefineLabel();
					g.Emit(OpCodes.Ldloc_S, (byte)4);
					g.Emit(OpCodes.Ldc_I4, memberCurSize);
					g.Emit(OpCodes.Bne_Un, lblSizeNotEqual);

					for (; memberLeft < memberEnd; ++memberLeft)
					{
						var member = members[memberLeft];

						var lblNotThisMember = g.DefineLabel();

						g.Emit(OpCodes.Dup); // duplicate CString
						g.Emit(OpCodes.Ldstr, member.Name);
						g.EmitCall(OpCodes.Call, ((Func<CString, string, bool>)CString.CompareASCIICaseInsensitive).Method, null);
						g.Emit(OpCodes.Brfalse, lblNotThisMember);

						if (member is FieldInfo field)
						{
							if (type.IsValueType)
								g.Emit(OpCodes.Ldloca_S, (byte)2);
							else
								g.Emit(OpCodes.Ldloc_2);

							g.Emit(OpCodes.Ldarg_0);
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(field.FieldType), null);
							g.Emit(OpCodes.Stfld, field);
						}
						else
						{
							var property = member as PropertyInfo;
							if (type.IsValueType)
								g.Emit(OpCodes.Ldloca_S, (byte)2);
							else
								g.Emit(OpCodes.Ldloc_2);

							g.Emit(OpCodes.Ldarg_0);
							g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(property.PropertyType), null);
							g.EmitCall(OpCodes.Call, property.SetMethod, null);
						}

						g.MarkLabel(lblNotThisMember);
					}

					g.MarkLabel(lblSizeNotEqual);

					g.Emit(OpCodes.Pop); // remove CString

					memberCurSize = memberNextSize;
				}

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

				// end
				g.Emit(OpCodes.Ldloc_2);
				g.Emit(OpCodes.Ret);
			}
			else
			{
				g.MarkLabel(fixMapLabel);
				g.MarkLabel(labels[(uint)JumpLabels.Map16]);
				g.MarkLabel(labels[(uint)JumpLabels.Map32]);
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
		private static DynamicArray<MemberInfo> GetMembersDefault(Type type)
		{
			var members = new DynamicArray<MemberInfo>(type.GetMembers(BindingFlags.Instance | BindingFlags.Public));
			members.RemoveAll(m => !((m is FieldInfo || (m is PropertyInfo p && p.CanWrite && p.GetMethod?.GetParameters().Length == 0))
					&& m.GetCustomAttribute<IgnoreAttribute>() == null));

			return members;
		}

		private static DynamicArray<MemberInfo> GetMembersMapped(Type type)
		{
			var members = new DynamicArray<MemberInfo>(type.GetMembers(BindingFlags.Instance | BindingFlags.Public));
			members.RemoveAll(m => !((m is FieldInfo || (m is PropertyInfo p && p.CanWrite && p.GetMethod?.GetParameters().Length == 0))
					&& m.GetCustomAttribute<IgnoreAttribute>() == null
					&& m.GetCustomAttribute<KeyAttribute>() != null));

			return members;
		}

		private static DynamicArray<MemberInfo> GetMembersIndexed(Type type)
		{
			var members = new DynamicArray<MemberInfo>(type.GetMembers(BindingFlags.Instance | BindingFlags.Public));
			members.RemoveAll(m => !((m is FieldInfo || (m is PropertyInfo p && p.CanWrite && p.GetMethod?.GetParameters().Length == 0))
				&& m.GetCustomAttribute<IgnoreAttribute>() == null));

			return members;
		}

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
