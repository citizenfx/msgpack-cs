using CitizenFX.Core;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace CitizenFX.MsgPack
{
	// Can be a struct as it's merely used for for temporary storage
	[SecuritySafeCritical]
	public partial struct MsgPackDeserializer
	{
		internal struct RestorePoint
		{
			public unsafe byte* Ptr { get; private set; }
			internal unsafe RestorePoint(byte* ptr) => this.Ptr = ptr;
		}

		private unsafe byte* m_ptr;
		private readonly unsafe byte* m_end;
		private readonly string m_netSource;

		internal unsafe MsgPackDeserializer(byte* data, ulong size, string netSource)
		{
			m_ptr = data;
			m_end = data + size;
			m_netSource = netSource;
		}

		#region Read basic types

		internal unsafe float ReadSingle()
		{
			var v = ReadUInt32();
			return *(float*)&v;
		}

		/// <summary>
		/// Read a <see cref="Single"/> stored as little endian, used for custom vectors
		/// </summary>
		internal unsafe float ReadSingleLE()
		{
			uint v = *(uint*)AdvancePointer(4);

			if (!BitConverter.IsLittleEndian)
			{
				v = unchecked((v >> 16) | (v << 16)); // swap adjacent 16-bit blocks
				v = unchecked(((v & 0xFF00FF00u) >> 8) | ((v & 0x00FF00FFu) << 8)); // swap adjacent 8-bit blocks
			}

			return *(float*)&v;
		}

		internal unsafe double ReadDouble()
		{
			var v = ReadUInt64();
			return *(double*)&v;
		}

		internal unsafe byte ReadByte()
		{
			byte v = *AdvancePointer(1);
			return v;
		}

		internal byte ReadUInt8() => ReadByte();

		internal unsafe ushort ReadUInt16()
		{
			uint v = *(ushort*)AdvancePointer(2);

			if (BitConverter.IsLittleEndian)
				v = unchecked((ushort)((v >> 8) | (v << 8))); // swap adjacent 8-bit blocks

			return (ushort)v;
		}

		internal unsafe uint ReadUInt32()
		{
			uint v = *(uint*)AdvancePointer(4);

			if (BitConverter.IsLittleEndian)
			{
				v = unchecked((v >> 16) | (v << 16)); // swap adjacent 16-bit blocks
				v = unchecked(((v & 0xFF00FF00u) >> 8) | ((v & 0x00FF00FFu) << 8)); // swap adjacent 8-bit blocks
			}

			return v;
		}

		internal unsafe ulong ReadUInt64()
		{
			ulong v = *(ulong*)AdvancePointer(8);

			if (BitConverter.IsLittleEndian)
			{
				v = unchecked((v >> 32) | (v << 32)); // swap adjacent 32-bit blocks
				v = unchecked(((v & 0xFFFF0000FFFF0000u) >> 16) | ((v & 0x0000FFFF0000FFFFu) << 16)); // swap adjacent 16-bit blocks
				v = unchecked(((v & 0xFF00FF00FF00FF00u) >> 8) | ((v & 0x00FF00FF00FF00FFu) << 8)); // swap adjacent 8-bit blocks
			}

			return v;
		}

		internal sbyte ReadInt8() => unchecked((sbyte)ReadUInt8());

		internal short ReadInt16() => unchecked((short)ReadUInt16());

		internal int ReadInt32() => unchecked((int)ReadUInt32());

		internal long ReadInt64() => unchecked((long)ReadUInt64());

		internal unsafe string ReadString(uint length)
		{
			sbyte* v = (sbyte*)AdvancePointer(length);
			return new string(v, 0, (int)length);
		}

		internal unsafe CString ReadCString(uint length)
		{
			byte* v = AdvancePointer(length);
			return CString.Create(v, length);
		}

		internal unsafe bool ReadStringAsTrueish(uint length)
		{
			sbyte* v = (sbyte*)AdvancePointer(length);

			if (length == 1)
				return v[0] == (byte)'1';
			else if (length == 4)
			{
				return v[0] == (byte)'t'
					&& v[1] == (byte)'r'
					&& v[2] == (byte)'u'
					&& v[3] == (byte)'e';
			}

			return false;
		}

		internal unsafe void SkipString(uint length) => AdvancePointer(length);

		#endregion

		#region Read complex type operations

		internal IDictionary<string, object> ReadMap(uint length)
		{
			var retobject = new ExpandoObject() as IDictionary<string, object>;

			for (var i = 0; i < length; i++)
			{
				var key = DeserializeAsString();
				var value = DeserializeAsObject();

				retobject.Add(key, value);
			}

			return retobject;
		}

		internal Dictionary<string, string> ReadDictionaryStringString(uint length)
		{
			var retobject = new Dictionary<string, string>();

			for (var i = 0; i < length; i++)
			{
				var key = DeserializeAsString();
				var value = DeserializeAsString();

				retobject.Add(key, value);
			}

			return retobject;
		}

		internal unsafe void SkipMap(uint length)
		{
			for (var i = 0; i < length; i++)
			{
				SkipObject();
				SkipObject();
			}
		}

		internal unsafe byte[] ReadBytes(uint length)
		{
			var ptr = (IntPtr)AdvancePointer(length);

			byte[] retobject = new byte[length];
			Marshal.Copy(ptr, retobject, 0, (int)length);

			return retobject;
		}

		internal unsafe void SkipBytes(uint length) => AdvancePointer(length);

		internal object[] ReadObjectArray(uint length)
		{
			object[] retobject = new object[length];

			for (var i = 0; i < length; i++)
			{
				retobject[i] = DeserializeAsObject();
			}

			return retobject;
		}

		internal string[] ReadStringArray(uint length)
		{
			string[] retobject = new string[length];

			for (var i = 0; i < length; i++)
			{
				retobject[i] = DeserializeAsString();
			}

			return retobject;
		}

		internal unsafe void SkipArray(uint length)
		{
			for (var i = 0; i < length; i++)
			{
				SkipObject();
			}
		}

		internal uint ReadArraySize(byte type)
		{

			// should start with an array
			if (type >= 0x90 && type < 0xA0)
				return type % 16u;
			else if (type == 0xDC)
				return ReadUInt16();
			else if (type == 0xDD)
				return ReadUInt32();

			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into a non-array type");
		}

		internal uint ReadArraySize() => ReadArraySize(ReadByte());

		#endregion

		#region Extra types

		internal object ReadExtraType(uint length)
		{
			var extType = ReadByte();
			switch (extType)
			{
				case 10: // remote funcref
				case 11: // local funcref
					return ReadCallback(length);
				case 20: return ReadVector2();
				case 21: return ReadVector3();
				case 22: return ReadVector4();
				case 23: return ReadQuaternion();
			}

			SkipExtraType(length);
			throw new InvalidOperationException($"Extension type {extType} not supported.");
		}

		internal string ReadExtraTypeToString(uint length)
		{
			var extType = ReadByte();
			switch (extType)
			{
				case 10: // remote funcref
				case 11: // local funcref
					SkipCallback(length);
					return nameof(Callback);
				case 20: return ReadVector2().ToString();
				case 21: return ReadVector3().ToString();
				case 22: return ReadVector4().ToString();
				case 23: return ReadQuaternion().ToString();
			}
			
			SkipExtraType(length);
			throw new InvalidOperationException($"Extension type {extType} not supported.");
		}

		internal long ReadExtraTypeAsInt64(uint length)
		{
			var extType = ReadByte();
			switch (extType)
			{
				case 20: return (long)ReadVector2().X;
				case 21: return (long)ReadVector3().X;
				case 22: return (long)ReadVector4().X;
				case 23: return (long)ReadQuaternion().X;
			}

			SkipExtraType(length);
			throw new InvalidOperationException($"Extension type {extType} not supported.");
		}

		internal float ReadExtraTypeAsFloat32(uint length)
		{
			var extType = ReadByte();
			switch (extType)
			{
				case 20: return ReadVector2().X;
				case 21: return ReadVector3().X;
				case 22: return ReadVector4().X;
				case 23: return ReadQuaternion().X;
			}

			SkipExtraType(length);
			throw new InvalidOperationException($"Extension type {extType} not supported.");
		}

		internal unsafe void SkipExtraType(uint length) => AdvancePointer(length + 1);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal Callback ReadCallback(uint length)
		{
			var refFunc = ReadString(length);
#if true
			return null;
#else
					return m_netSource is null
						? _LocalFunction.Create(refFunc)
#if REMOTE_FUNCTION_ENABLED
						: _RemoteFunction.Create(refFunc, m_netSource);
#else
						: null;
#endif
#endif
		}

		private void SkipCallback(uint length) => SkipString(length);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private Vector2 ReadVector2() => new Vector2(ReadSingleLE(), ReadSingleLE());
		private unsafe void SkipVector2() => AdvancePointer(2 * sizeof(float));


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private Vector3 ReadVector3() => new Vector3(ReadSingleLE(), ReadSingleLE(), ReadSingleLE());
		private unsafe void SkipVector3() => AdvancePointer(3 * sizeof(float));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private Vector4 ReadVector4() => new Vector4(ReadSingleLE(), ReadSingleLE(), ReadSingleLE(), ReadSingleLE());
		private unsafe void SkipVector4() => AdvancePointer(4 * sizeof(float));

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private Quaternion ReadQuaternion() => new Quaternion(ReadSingleLE(), ReadSingleLE(), ReadSingleLE(), ReadSingleLE());
		private unsafe void SkipQuaternion() => AdvancePointer(4 * sizeof(float));

		#endregion

		#region Buffer pointer control

		internal unsafe RestorePoint CreateRestorePoint() => new RestorePoint(m_ptr);
		internal unsafe void Restore(RestorePoint restorePoint) => m_ptr = restorePoint.Ptr;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private unsafe byte* AdvancePointer(uint amount)
		{
			byte* curPtr = m_ptr;
			m_ptr += amount;
			if (m_ptr > m_end)
			{
				m_ptr -= amount; // reverse damage
				throw new ArgumentException($"MsgPackDeserializer tried to retrieve {amount} bytes while only {m_end - m_ptr} bytes remain");
			}

			return curPtr;
		}

		#endregion

		#region Statics (easier access with current IL generation)

		public static uint ReadArraySize(ref MsgPackDeserializer deserializer) => deserializer.ReadArraySize();
		public static uint ReadArraySize(ref MsgPackDeserializer deserializer, byte type) => deserializer.ReadArraySize(type);

		public static byte ReadByte(ref MsgPackDeserializer deserializer) => deserializer.ReadByte();
		public static float ReadSingle(ref MsgPackDeserializer deserializer) => deserializer.ReadSingle();
		public static double ReadDouble(ref MsgPackDeserializer deserializer) => deserializer.ReadDouble();
		public static byte ReadUInt8(ref MsgPackDeserializer deserializer) => deserializer.ReadByte();
		public static ushort ReadUInt16(ref MsgPackDeserializer deserializer) => deserializer.ReadUInt16();
		public static uint ReadUInt32(ref MsgPackDeserializer deserializer) => deserializer.ReadUInt32();
		public static ulong ReadUInt64(ref MsgPackDeserializer deserializer) => deserializer.ReadUInt64();
		public static sbyte ReadInt8(ref MsgPackDeserializer deserializer) => deserializer.ReadInt8();
		public static short ReadInt16(ref MsgPackDeserializer deserializer) => deserializer.ReadInt16();
		public static int ReadInt32(ref MsgPackDeserializer deserializer) => deserializer.ReadInt32();
		public static long ReadInt64(ref MsgPackDeserializer deserializer) => deserializer.ReadInt64();

		public static string ReadString(ref MsgPackDeserializer deserializer, uint length) => deserializer.ReadString(length);
		public static void SkipString(ref MsgPackDeserializer deserializer, uint length) => deserializer.SkipString(length);
		public static CString ReadCString(ref MsgPackDeserializer deserializer, uint length) => deserializer.ReadCString(length);
		public static float ReadSingleLE(ref MsgPackDeserializer deserializer) => deserializer.ReadSingleLE();

		public static object[] ReadObjectArray(ref MsgPackDeserializer deserializer, uint length) => deserializer.ReadObjectArray(length);

		public static void SkipObject(ref MsgPackDeserializer deserializer) => deserializer.SkipObject();
		public static void SkipObjects(ref MsgPackDeserializer deserializer, uint size)
		{
			for (uint i = 0; i < size; ++i)
				deserializer.SkipObject();
		}

		public static void SkipVector2(ref MsgPackDeserializer deserializer) => deserializer.SkipVector2();
		public static void SkipVector3(ref MsgPackDeserializer deserializer) => deserializer.SkipVector3();
		public static void SkipVector4(ref MsgPackDeserializer deserializer) => deserializer.SkipVector4();
		public static void SkipQuaternion(ref MsgPackDeserializer deserializer) => deserializer.SkipQuaternion();

		internal static unsafe RestorePoint CreateRestorePoint(ref MsgPackDeserializer deserializer) => deserializer.CreateRestorePoint();
		internal static unsafe void Restore(ref MsgPackDeserializer deserializer, RestorePoint restorePoint) => deserializer.Restore(restorePoint);

		#endregion
	}
}
