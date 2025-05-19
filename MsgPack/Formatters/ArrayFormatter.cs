using System;
using System.Reflection.Emit;
using System.Reflection;
using static CitizenFX.MsgPack.Detail.Helper;
using static CitizenFX.MsgPack.Detail.SerializerAccess;
using System.Runtime.InteropServices;

namespace CitizenFX.MsgPack.Formatters
{
    internal static class ArrayFormatter
    {
        public static Tuple<Serializer, MethodInfo> Build(Type type, Type typeArray)
        {
            MethodInfo methodSerialize, methodDeserialize, methodObjectSerialize;

            string name = $"ArrayFormatter_{typeArray.FullName}";
            Type buildType = MsgPackRegistry.m_moduleBuilder.GetType(name);

            if (buildType == null)
            {
                TypeBuilder typeBuilder = MsgPackRegistry.m_moduleBuilder.DefineType(name);

                methodSerialize = BuildSerializer(type, typeArray, typeBuilder);
                BuildDeserializer(type, typeArray, typeBuilder);
                BuildObjectSerializer(typeArray, methodSerialize, typeBuilder);

                buildType = typeBuilder.CreateType();
            }

            methodSerialize = buildType.GetMethod("Serialize", new[] { typeof(MsgPackSerializer), type });
            methodDeserialize = buildType.GetMethod("Deserialize");
            methodObjectSerialize = buildType.GetMethod("Serialize", new[] { typeof(MsgPackSerializer), typeof(object) });

            Serializer serializeMethods = new Serializer(methodSerialize, methodObjectSerialize);

            MsgPackRegistry.RegisterSerializer(typeArray, serializeMethods);
            MsgPackRegistry.RegisterDeserializer(typeArray, methodDeserialize);

            return new Tuple<Serializer, MethodInfo>(serializeMethods, methodDeserialize);
        }

        /// <summary>
        /// Simply unpacks and calls <paramref name="methodSerialize"/>
        /// </summary>
        /// <param name="typeArray">Type we're serializing</param>
        /// <param name="methodSerialize">Method to call once the object is unpacked</param>
        /// <param name="typeBuilder">Building type to add this method to</param>
        /// <returns></returns>
        private static MethodInfo BuildObjectSerializer(Type typeArray, MethodInfo methodSerialize, TypeBuilder typeBuilder)
        {
            MethodBuilder methodSerializeObject = typeBuilder.DefineMethod("Serialize",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void), new[] { typeof(MsgPackSerializer), typeof(object) });

            var g = methodSerializeObject.GetILGenerator();
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldarg_1);
            g.Emit(OpCodes.Unbox_Any, typeArray);
            g.EmitCall(OpCodes.Call, methodSerialize, null);
            g.Emit(OpCodes.Ret);

            return methodSerializeObject;
        }

        private static MethodInfo BuildSerializer(Type type, Type typeArray, TypeBuilder typeBuilder)
        {
            MethodBuilder methodSerialize = typeBuilder.DefineMethod("Serialize",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void), new[] { typeof(MsgPackSerializer), typeArray });

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
            if (typeArray.IsArray)
                g.Emit(OpCodes.Ldlen);
            else
                g.EmitCall(OpCodes.Callvirt, typeArray.GetProperty("Count")?.GetGetMethod(), null);
            g.Emit(OpCodes.Stloc_0);

            // write header
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldloc_0);
            g.EmitCall(OpCodes.Call, GetVoidMethod<uint>(WriteArrayHeader), null);

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

                if (typeArray.IsArray)
                {
                    g.Emit(OpCodes.Ldloc_1);
                    g.Emit(OpCodes.Ldelem, type);
                }
                else
                {
                    var getter = typeArray.GetProperty("Item").GetGetMethod();
                    g.Emit(OpCodes.Ldloc_1);
                    g.EmitCall(OpCodes.Callvirt, getter, null);
                }

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
            g.EmitCall(OpCodes.Call, GetVoidMethod(WriteNil), null);
            g.Emit(OpCodes.Ret);

            return methodSerialize;
        }

        private static MethodInfo BuildDeserializer(Type type, Type typeArray, TypeBuilder typeBuilder)
        {
            MethodBuilder methodDeserialize = typeBuilder.DefineMethod("Deserialize",
                MethodAttributes.Public | MethodAttributes.Static,
                typeArray, new[] { typeof(MsgPackDeserializer).MakeByRefType() });

            bool genericArray = !typeArray.IsArray;

            var g = methodDeserialize.GetILGenerator();
            g.DeclareLocal(typeof(uint)); // type first size after
            g.DeclareLocal(typeof(uint));
            g.DeclareLocal(typeArray);

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
            g.EmitCall(OpCodes.Call, GetResultMethod<byte, uint>(ReadArraySize), null);
            g.Emit(OpCodes.Stloc_0); // use loc_0 as size now

            if (!genericArray)
            {
                // loc_2 = new T[loc_0];
                g.Emit(OpCodes.Ldloc_0);
                g.Emit(OpCodes.Newarr, typeArray);
                g.Emit(OpCodes.Stloc_2);
            }
            else
            {
                // loc_2 = new List<T>(loc_0);
                g.Emit(OpCodes.Ldloc_0);
                g.Emit(OpCodes.Newobj, typeArray.GetConstructor(new[] { typeof(int) }));
                g.Emit(OpCodes.Stloc_2);
            }

            // i = 0
            g.Emit(OpCodes.Ldc_I4_0);
            g.Emit(OpCodes.Stloc_1);

            // for (uint i = 0; i < length; ++i)
            {
                Label whileCond = g.DefineLabel();
                Label whileLoop = g.DefineLabel();
                g.Emit(OpCodes.Br_S, whileCond);
                g.MarkLabel(whileLoop);

                // array[loc_0] prestacking [ array, index, value ]
                g.Emit(OpCodes.Ldloc_2);

                if (!genericArray)
                    g.Emit(OpCodes.Ldloc_1);

                // deserialize value
                g.Emit(OpCodes.Ldarg_0);
                g.EmitCall(OpCodes.Call, MsgPackRegistry.GetOrCreateDeserializer(type), null);

                if (!genericArray)
                    g.Emit(OpCodes.Stelem, type); // array[loc_0] = deserialized value
                else
                    g.EmitCall(OpCodes.Callvirt, typeArray.GetMethod("Add", new[] { type }), null);

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