using CitizenFX.Core;
using CitizenFX.MsgPack.Formatters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Security;

namespace CitizenFX.MsgPack
{
    /// <summary>
    /// Serializer class to serialize any data to the MsgPack format.
    /// </summary>
    public class MsgPackSerializer
    {
        // NOTE:
        //   1. When adding any Serialize(T) method, make sure there's an equivalent T DeserializeAsT() in the MsgPackDeserializer class.
        //   2. Serialize(T) write headers, sizes, and pick the correct Write*([MsgPackCode,] T) method.
        //   3. Write*([MsgPackCode,] T) write directly to memory, these should stay private.
        //   4. See specifications: https://github.com/msgpack/msgpack/blob/master/spec.md

        // TODO: look into and profile non-pinned alternatives for interop with C++
        byte[] m_buffer;
        ulong m_position;

        public MsgPackSerializer()
        {
            m_buffer = new byte[256];
        }

        public byte[] AcquireBuffer()
        {
            return m_buffer;
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[m_position];
            Array.Copy(m_buffer, result, (int)m_position);
            return result;
        }

        public void Reset()
        {
            m_position = 0;
        }

        private void EnsureCapacity(uint size)
        {
            ulong requiredCapacity = m_position + size;
            if (requiredCapacity >= (ulong)m_buffer.LongLength)
            {
                byte[] oldBuffer = m_buffer;
                m_buffer = new byte[oldBuffer.Length * 2];
                Array.Copy(oldBuffer, m_buffer, oldBuffer.Length);
            }
        }

        public static byte[] SerializeToByteArray(object value)
        {
            var serializer = new MsgPackSerializer();
            serializer.Serialize(value);
            return serializer.ToArray();
        }

        #region Basic type serialization

        public void Serialize(bool value)
        {
            Write(value ? (byte)MsgPackCode.True : (byte)MsgPackCode.False);
        }

        public void Serialize(object v) => MsgPackRegistry.Serialize(this, v);

        public void Serialize(sbyte v)
        {
            if (v < 0)
            {
                if (v >= unchecked((sbyte)MsgPackCode.FixIntNegativeMin))
                    Write(unchecked((byte)v));
                else
                    WriteBigEndian(MsgPackCode.Int8, (sbyte)v);
            }
            else
                Write(unchecked((byte)v));
        }

        public void Serialize(byte v)
        {
            if (v <= (byte)MsgPackCode.FixIntPositiveMax)
                Write(v);
            else
                Write(MsgPackCode.UInt8, v);
        }

        public void Serialize(short v)
        {
            if (v < 0)
            {
                if (v >= unchecked((sbyte)MsgPackCode.FixIntNegativeMin))
                    Write(unchecked((byte)v));
                else if (v >= sbyte.MinValue)
                    WriteBigEndian(MsgPackCode.Int8, (sbyte)v);
                else
                    WriteBigEndian(MsgPackCode.Int16, v);
            }
            else
                Serialize(unchecked((ushort)v));
        }

        public void Serialize(Delegate d)
        {
            // this works only if msgpack lib is built within fivem, it's not really elegant imho
            // but it works, so let's not break it now, shall we?
            // Do we want to make this work without depending from ReferenceFunctionManager
            // and make our own Canonicalization of the reference id?
#if REMOTE_FUNCTION_ENABLED
            ulong callbackId = d.Target is _RemoteHandler _pf
                ? _pf.m_id
                : ExternalsManager.RegisterRemoteFunction(d.Method.ReturnType, new DynFunc(args =>
                    args.Length == 1 || args[1] == null ? dynFunc(args[0]) : null));

            var bytes = Encoding.UTF8.GetBytes(callbackId.ToString());
            uint size = (uint)bytes.LongLength;
            EnsureCapacity((uint)bytes.Length);
            WriteExtraTypeHeader(size);
            Write((byte)10);
            Array.Copy(bytes, 0, m_buffer, (int)m_position, size);
            m_position += size;
#else
            var remote = MsgPackReferenceRegistrar.Register(MsgPackDeserializer.CreateDelegate(d));
            uint size = (uint)remote.Value.LongLength;
            EnsureCapacity((uint)remote.Value.Length);
            WriteExtraTypeHeader(size);
            Write((byte)11);
            Array.Copy(remote.Value, 0, m_buffer, (int)m_position, size);
            m_position += size;
#endif
        }

        public void Serialize(ushort v)
        {
            if (v <= (byte)MsgPackCode.FixIntPositiveMax)
                Write(unchecked((byte)v));
            else if (v <= byte.MaxValue)
                Write(MsgPackCode.UInt8, unchecked((byte)v));
            else
                WriteBigEndian(MsgPackCode.UInt16, v);
        }

        public void Serialize(int v)
        {
            if (v < 0)
            {
                if (v >= unchecked((sbyte)MsgPackCode.FixIntNegativeMin))
                    Write(unchecked((byte)v));
                else if (v >= sbyte.MinValue)
                    Write(MsgPackCode.Int8, unchecked((byte)v));
                else if (v >= short.MinValue)
                    WriteBigEndian(MsgPackCode.Int16, (short)v);
                else
                    WriteBigEndian(MsgPackCode.Int32, (short)v);
            }
            else
                Serialize(unchecked((uint)v));
        }

        public void Serialize(uint v)
        {
            if (v <= (byte)MsgPackCode.FixIntPositiveMax)
                Write(unchecked((byte)v));
            else if (v <= byte.MaxValue)
                Write(MsgPackCode.UInt8, unchecked((byte)v));
            else if (v <= ushort.MaxValue)
                WriteBigEndian(MsgPackCode.UInt16, unchecked((ushort)v));
            else
                WriteBigEndian(MsgPackCode.UInt32, v);
        }

        public void Serialize(long v)
        {
            if (v < 0)
            {
                if (v >= unchecked((sbyte)MsgPackCode.FixIntNegativeMin))
                    Write(unchecked((byte)v));
                else if (v >= sbyte.MinValue)
                    WriteBigEndian(MsgPackCode.Int8, (sbyte)v);
                else if (v >= short.MinValue)
                    WriteBigEndian(MsgPackCode.Int16, (short)v);
                else if (v >= int.MinValue)
                    WriteBigEndian(MsgPackCode.Int32, (int)v);
                else
                    WriteBigEndian(MsgPackCode.Int64, v);
            }
            else
                Serialize(unchecked((ulong)v));
        }

        public void Serialize(ulong v)
        {
            if (v <= (byte)MsgPackCode.FixIntPositiveMax)
                Write(unchecked((byte)v));
            else if (v <= byte.MaxValue)
                Write(MsgPackCode.Int8, unchecked((byte)v));
            else if (v <= ushort.MaxValue)
                WriteBigEndian(MsgPackCode.UInt16, unchecked((ushort)v));
            else if (v <= uint.MaxValue)
                WriteBigEndian(MsgPackCode.UInt32, unchecked((uint)v));
            else
                WriteBigEndian(MsgPackCode.UInt64, v);
        }

        public unsafe void Serialize(float v) => WriteBigEndian(MsgPackCode.Float32, *(uint*)&v);
        public unsafe void Serialize(double v) => WriteBigEndian(MsgPackCode.Float64, *(ulong*)&v);

        [SecuritySafeCritical]
        public unsafe void Serialize(string v)
        {
            fixed (char* p_value = v)
            {
                uint size = (uint)CString.UTF8EncodeLength(p_value, v.Length);
                uint totalSize = size + 1;
                EnsureCapacity(totalSize);

                if (size < (MsgPackCode.FixStrMax - MsgPackCode.FixStrMin))
                    Write(unchecked((byte)((uint)MsgPackCode.FixStrMin + size)));
                else if (size <= byte.MaxValue)
                {
                    Write(unchecked((byte)(uint)MsgPackCode.Str8));
                    Write(unchecked((byte)size));
                }
                else if (size <= ushort.MaxValue)
                {
                    Write(unchecked((byte)(uint)MsgPackCode.Str16));
                    Write(unchecked((byte)(size >> 8)));
                    Write(unchecked((byte)(size & 0xFF)));
                }
                else
                {
                    Write(unchecked((byte)(uint)MsgPackCode.Str32));
                    Write(unchecked((byte)(size >> 24)));
                    Write(unchecked((byte)(size >> 16)));
                    Write(unchecked((byte)(size >> 8)));
                    Write(unchecked((byte)(size & 0xFF)));
                }
                fixed (byte* p_buffer = m_buffer)
                {
                    CString.UTF8Encode(p_buffer + m_position, p_value, v.Length);
                    m_position += size;
                }
            }
        }
        public unsafe void Serialize(CString v)
        {
            fixed (byte* p_value = v.value)
            {
                uint size = (uint)v.value.LongLength - 1u;

                if (size < (MsgPackCode.FixStrMax - MsgPackCode.FixStrMin))
                    Write(unchecked((byte)((uint)MsgPackCode.FixStrMin + size)));
                else if (size <= byte.MaxValue)
                    Write(MsgPackCode.Str8, unchecked((byte)size));
                else if (size <= ushort.MaxValue)
                    Write(MsgPackCode.Str16, unchecked((byte)size));
                else
                    Write(MsgPackCode.Str32, unchecked((byte)size));

                EnsureCapacity(size);
                Array.Copy(v.value, 0, m_buffer, (int)m_position, size);
                m_position += size;
            }
        }

        public unsafe void Serialize(IEnumerable enumerable)
        {
            if (enumerable == null)
            {
                WriteNil();
                return;
            }
            switch (enumerable)
            {
                case byte[] b:
                    fixed (byte* p_value = b)
                    {
                        var size = (uint)b.LongLength;
                        if (size <= byte.MaxValue)
                            Write(MsgPackCode.Bin8, unchecked((byte)size));
                        else if (size <= ushort.MaxValue)
                            Write(MsgPackCode.Bin16, unchecked((byte)size));
                        else
                            Write(MsgPackCode.Bin32, unchecked((byte)size));
                        EnsureCapacity(size);
                        Array.Copy(b, 0, m_buffer, (int)m_position, size);
                        m_position += size;
                    }
                    break;
                case IDictionary dictionary:
                    WriteMapHeader((uint)dictionary.Count);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        Serialize(entry.Key);
                        Serialize(entry.Value);
                    }
                    break;
                case IList list:
                    WriteArrayHeader((uint)list.Count);
                    for (int i = 0; i < list.Count; ++i)
                        Serialize(list[i]);
                    break;
            }
        }

        #endregion

        #region Premade associative array serializers

        public void Serialize(IReadOnlyDictionary<string, string> v)
        {
            WriteMapHeader((uint)v.Count);
            foreach (var keyValue in v)
            {
                Serialize(keyValue.Key);
                Serialize(keyValue.Value);
            }
        }

        public void Serialize(Dictionary<string, string> v) => Serialize((IReadOnlyDictionary<string, string>)v);
        public void Serialize(IDictionary<string, string> v) => Serialize((IReadOnlyDictionary<string, string>)v);

        #endregion

        #region Premade array serializers

        public void Serialize(object[] v)
        {
            WriteArrayHeader((uint)v.Length);

            for (int i = 0; i < v.Length; ++i)
            {
                Serialize(v[i]);
            }
        }

        public void Serialize(string[] v)
        {
            WriteArrayHeader((uint)v.Length);

            for (int i = 0; i < v.Length; ++i)
            {
                Serialize(v[i]);
            }
        }

        #endregion



        #region Extra types

        public unsafe void SerializeType(object obj)
        {
            if (obj is null)
            {
                WriteNil();
                return;
            }
            var type = obj.GetType();
            if (type.GetCustomAttribute<MsgPackSerializableAttribute>() is MsgPackSerializableAttribute serializable && serializable.Layout != Layout.Default)
            {
                if (serializable.Layout == Layout.Indexed)
                {
                    Detail.DynamicArray<MemberInfo> allMembers = TypeFormatter.GetReadableIndexMembers(type);
                    MemberInfo[] members = new MemberInfo[allMembers.Count];

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

                    if (members.Length == 0)
                        throw new ArgumentException($"Type {type} can't be serialized by arrays, no {nameof(IndexAttribute)} has been found on any field or property");

                    int length = members.Length;
                    WriteArrayHeader((uint)length);
                    for (var i = 0; i < length; i++)
                    {
                        switch (members[i])
                        {
                            case FieldInfo field:
                                Serialize(field.GetValue(obj));
                                break;
                            case PropertyInfo property:
                                Serialize(property.GetValue(obj));
                                break;

                            default:
                                throw new ArgumentException($"Member type {members[i].GetType()} is not supported");

                        }
                    }
                }
                else if (serializable.Layout == Layout.Keyed)
                {
                    serializeMembers(obj, TypeFormatter.GetReadAndWritableKeyedMembers(type));
                }
            }
            else
            {
                serializeMembers(obj, TypeFormatter.GetWritableMembers(type));
            }
        }

        private unsafe void serializeMembers(object obj, Detail.DynamicArray<MemberInfo> members)
        {
            var length = members.Count;
            WriteMapHeader((uint)length);
            for (var i = 0; i < length; i++)
            {
                var member = members[i];
                string memberName = member.GetCustomAttribute<KeyAttribute>()?.Key ?? member.Name;
                Serialize(memberName);
                switch (members[i])
                {
                    case FieldInfo field:
                        Serialize(field.GetValue(obj));
                        break;
                    case PropertyInfo property:
                        Serialize(property.GetValue(obj));
                        break;

                    default:
                        throw new ArgumentException($"Member type {members[i].GetType()} is not supported");
                }
            }

        }

        public unsafe void Serialize(KeyValuePair<object, object> v)
        {
            // Serializza come mappa con un solo elemento
            WriteMapHeader(1);
            Serialize(v.Key);
            Serialize(v.Value);
            return;
        }

        public void Serialize(Callback v)
        {
            //WriteExtraTypeHeader(v.)

            throw new InvalidCastException($"Can't serialize {nameof(Callback)}, unsupported at this moment");
        }

        #endregion

        #region Direct write operations

        internal void WriteMapHeader(uint v)
        {
            if (v < (MsgPackCode.FixMapMax - MsgPackCode.FixMapMin))
                Write(unchecked((byte)((uint)MsgPackCode.FixMapMin + v)));
            else if (v <= short.MaxValue)
                WriteBigEndian(MsgPackCode.Map16, (short)v);
            else
                WriteBigEndian(MsgPackCode.Map32, v);
        }

        internal void WriteArrayHeader(uint v)
        {
            if (v < (MsgPackCode.FixArrayMax - MsgPackCode.FixArrayMin))
                Write(unchecked((byte)((uint)MsgPackCode.FixArrayMin + v)));
            else if (v <= short.MaxValue)
                WriteBigEndian(MsgPackCode.Array16, (short)v);
            else
                WriteBigEndian(MsgPackCode.Array32, v);
        }

        internal void WriteExtraTypeHeader(uint length, byte extType = 0)
        {
            switch (length)
            {
                case 0: throw new ArgumentException("Extra type can't be 0 sized");
                case 1: Write((byte)MsgPackCode.FixExt1); break;
                case 2: Write((byte)MsgPackCode.FixExt2); break;
                case 4: Write((byte)MsgPackCode.FixExt4); break;
                case 8: Write((byte)MsgPackCode.FixExt8); break;
                case 16: Write((byte)MsgPackCode.FixExt16); break;
            }

            if (length <= 0xFFU)
                Write(MsgPackCode.Ext8, (byte)length);
            else if (length <= 0xFFFFU)
                WriteBigEndian(MsgPackCode.Ext16, (ushort)length);
            else
                WriteBigEndian(MsgPackCode.Ext32, length);
        }

        private void Write(byte code)
        {
            EnsureCapacity(1);
            m_buffer[m_position++] = code;
        }

        public void WriteNil()
        {
            Write((byte)MsgPackCode.Nil);
        }

        private void Write(MsgPackCode code, byte value)
        {
            EnsureCapacity(2);
            ulong pos = m_position;
            m_position += 2;
            m_buffer[pos] = (byte)code;
            m_buffer[pos + 1] = value;
        }

        private void WriteBigEndian(MsgPackCode code, ushort value)
        {
            EnsureCapacity(3);
            ulong pos = m_position;
            m_position += 3;
            m_buffer[pos] = (byte)code;
            if (BitConverter.IsLittleEndian)
            {
                m_buffer[pos + 1] = unchecked((byte)(value >> 8));
                m_buffer[pos + 2] = unchecked((byte)(value));
            }
            else
            {
                m_buffer[pos + 1] = unchecked((byte)value);
                m_buffer[pos + 2] = unchecked((byte)(value >> 8));
            }
        }

        private unsafe void WriteBigEndian(MsgPackCode code, uint v)
        {
            EnsureCapacity(5);
            fixed (byte* p_buffer = m_buffer)
            {
                byte* ptr = p_buffer + m_position;
                m_position += 5;

                if (BitConverter.IsLittleEndian)
                {
                    v = (v >> 16) | (v << 16); // swap adjacent 16-bit blocks
                    v = ((v & 0xFF00FF00u) >> 8) | ((v & 0x00FF00FFu) << 8); // swap adjacent 8-bit blocks
                }

                *ptr = (byte)code;
                *(uint*)(ptr + 1) = v;
            }
        }

        private unsafe void WriteBigEndian(MsgPackCode code, ulong v)
        {
            EnsureCapacity(9);
            fixed (byte* p_buffer = m_buffer)
            {
                byte* ptr = p_buffer + m_position;
                m_position += 9;

                if (BitConverter.IsLittleEndian)
                {
                    v = (v >> 32) | (v << 32); // swap adjacent 32-bit blocks
                    v = ((v & 0xFFFF0000FFFF0000u) >> 16) | ((v & 0x0000FFFF0000FFFFu) << 16); // swap adjacent 16-bit blocks
                    v = ((v & 0xFF00FF00FF00FF00u) >> 8) | ((v & 0x00FF00FF00FF00FFu) << 8); // swap adjacent 8-bit blocks
                }

                *ptr = (byte)code;
                *(ulong*)(ptr + 1) = v;
            }
        }

        private void PrivatePackExtendedTypeValueCore(byte typeCode, byte[] body)
        {
            switch (body.Length)
            {
                case 1:
                    Write(212);
                    break;
                case 2:
                    Write(213);
                    break;
                case 4:
                    Write(214);
                    break;
                case 8:
                    Write(215);
                    break;
                case 16:
                    Write(216);
                    break;
                default:
                    if (body.Length < 256)
                    {
                        Write(199);
                        Write((byte)((uint)body.Length & 0xFFu));
                    }
                    else if (body.Length < 65536)
                    {
                        Write(200);
                        Write((byte)((uint)(body.Length >> 8) & 0xFFu));
                        Write((byte)((uint)body.Length & 0xFFu));
                    }
                    else
                    {
                        Write(201);
                        Write((byte)((uint)(body.Length >> 24) & 0xFFu));
                        Write((byte)((uint)(body.Length >> 16) & 0xFFu));
                        Write((byte)((uint)(body.Length >> 8) & 0xFFu));
                        Write((byte)((uint)body.Length & 0xFFu));
                    }

                    break;
            }

            Write(typeCode);
            foreach (byte value2 in body)
            {
                Write(value2);
            }
        }
        private void WriteBigEndian(MsgPackCode code, sbyte value) => WriteBigEndian(code, unchecked((byte)value));
        private void WriteBigEndian(MsgPackCode code, short value) => WriteBigEndian(code, unchecked((ushort)value));
        private void WriteBigEndian(MsgPackCode code, int value) => WriteBigEndian(code, unchecked((uint)value));
        private void WriteBigEndian(MsgPackCode code, long value) => WriteBigEndian(code, unchecked((ulong)value));
        private unsafe void WriteBigEndian(MsgPackCode code, float value) => WriteBigEndian(code, *(uint*)&value);
        private unsafe void WriteBigEndian(MsgPackCode code, double value) => WriteBigEndian(code, *(ulong*)&value);

        #endregion
    }
}
