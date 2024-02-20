using BenchmarkDotNet.Disassemblers;
using MessagePack;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using static MsgPack.MsgPackDeserializer;

namespace MsgPack
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

		public unsafe MsgPackDeserializer(byte* data, ulong size, string netSource)
		{
			m_ptr = data;
			m_end = data + size;
			m_netSource = netSource;
		}

		internal static unsafe object Deserialize(byte[] data, string netSource = null)
		{
			if (data?.Length > 0)
			{
				fixed (byte* dataPtr = data)
					return Deserialize(dataPtr, data.Length, netSource);
			}

			return null;
		}

		internal static unsafe object Deserialize(byte* data, long size, string netSource = null)
		{
			if (data != null && size > 0)
			{
				var deserializer = new MsgPackDeserializer(data, (ulong)size, netSource);
				return deserializer.Deserialize();
			}

			return null;
		}

		/// <summary>
		/// Starts deserialization from an array type
		/// </summary>
		/// <param name="data">ptr to byte data</param>
		/// <param name="size">size of byte data</param>
		/// <param name="netSource">from whom came this?</param>
		/// <returns>arguments that can be passed into dynamic delegates</returns>
		public static unsafe object[] DeserializeArray(byte* data, long size, string netSource = null)
		{
			if (data != null && size > 0)
			{
				var deserializer = new MsgPackDeserializer(data, (ulong)size, netSource);
				return deserializer.DeserializeArray();
			}

			return new object[0];
		}

		private unsafe object[] DeserializeArray()
		{
			int length;
			var type = ReadByte();

			// should start with an array
			if (type >= 0x90 && type < 0xA0)
				length = type % 16;
			else if (type == 0xDC)
				length = ReadUInt16();
			else if (type == 0xDD)
				length = ReadInt32();
			else
				return new object[0];

			object[] array = new object[length];
			for (var i = 0; i < length; ++i)
			{
				array[i] = Deserialize();
			}

			return array;
		}

		public object Deserialize()
		{
			var type = ReadByte();

			if (type < 0xC0)
			{
				if (type < 0x80)
				{
					return type;
				}
				else if (type < 0x90)
				{
					return ReadMap(type % 16u);
				}
				else if (type < 0xA0)
				{
					return ReadObjectArray(type % 16u);
				}

				return ReadString(type % 32u);
			}
			else if (type > 0xDF)
			{
				return type - 256; // fix negative number
			}

			switch (type)
			{
				case 0xC0: return null;

				case 0xC2: return false;
				case 0xC3: return true;

				case 0xC4: return ReadBytes(ReadUInt8());
				case 0xC5: return ReadBytes(ReadUInt16());
				case 0xC6: return ReadBytes(ReadUInt32());

				case 0xC7: return ReadExtraType(ReadUInt8());
				case 0xC8: return ReadExtraType(ReadUInt16());
				case 0xC9: return ReadExtraType(ReadUInt32());

				case 0xCA: return ReadSingle();
				case 0xCB: return ReadDouble();

				case 0xCC: return ReadUInt8();
				case 0xCD: return ReadUInt16();
				case 0xCE: return ReadUInt32();
				case 0xCF: return ReadUInt64();

				case 0xD0: return ReadInt8();
				case 0xD1: return ReadInt16();
				case 0xD2: return ReadInt32();
				case 0xD3: return ReadInt64();

				case 0xD4: return ReadExtraType(1);
				case 0xD5: return ReadExtraType(2);
				case 0xD6: return ReadExtraType(4);
				case 0xD7: return ReadExtraType(8);
				case 0xD8: return ReadExtraType(16);

				case 0xD9: return ReadString(ReadUInt8());
				case 0xDA: return ReadString(ReadUInt16());
				case 0xDB: return ReadString(ReadUInt32());

				case 0xDC: return ReadObjectArray(ReadUInt16());
				case 0xDD: return ReadObjectArray(ReadUInt32());

				case 0xDE: return ReadMap(ReadUInt16());
				case 0xDF: return ReadMap(ReadUInt32());
			}

			throw new InvalidOperationException($"Tried to decode invalid MsgPack type {type}");
		}

		internal unsafe void SkipObject()
		{
			var type = ReadByte();

			if (type < 0xC0)
			{
				if (type < 0x90)
					SkipMap(type % 16u);
				else if (type < 0xA0)
					SkipArray(type % 16u);
				else
					SkipString(type % 32u);

				return;
			}
			else if (type > 0xDF)
			{
				return;
			}

			switch (type)
			{
				case 0xC4: AdvancePointer(ReadUInt8()); return;
				case 0xC5: AdvancePointer(ReadUInt16()); return;
				case 0xC6: AdvancePointer(ReadUInt32()); return;

				case 0xC7: SkipExtraType(ReadUInt8()); return;
				case 0xC8: SkipExtraType(ReadUInt16()); return;
				case 0xC9: SkipExtraType(ReadUInt32()); return;

				case 0xCA: AdvancePointer(4); return;
				case 0xCB: AdvancePointer(8); return;

				case 0xCC: AdvancePointer(1); return;
				case 0xCD: AdvancePointer(2); return;
				case 0xCE: AdvancePointer(3); return;
				case 0xCF: AdvancePointer(4); return;

				case 0xD0: AdvancePointer(1); return;
				case 0xD1: AdvancePointer(2); return;
				case 0xD2: AdvancePointer(3); return;
				case 0xD3: AdvancePointer(4); return;

				case 0xD4: SkipExtraType(1); return;
				case 0xD5: SkipExtraType(2); return;
				case 0xD6: SkipExtraType(4); return;
				case 0xD7: SkipExtraType(8); return;
				case 0xD8: SkipExtraType(16); return;

				case 0xD9: SkipString(ReadUInt8()); return;
				case 0xDA: SkipString(ReadUInt16()); return;
				case 0xDB: SkipString(ReadUInt32()); return;

				case 0xDC: SkipArray(ReadUInt16()); return;
				case 0xDD: SkipArray(ReadUInt32()); return;

				case 0xDE: SkipMap(ReadUInt16()); return;
				case 0xDF: SkipMap(ReadUInt32()); return;
			}

			throw new InvalidOperationException($"Tried to decode invalid MsgPack type {type}");
		}

		internal IDictionary<string, object> ReadMap(uint length)
		{
			var retobject = new ExpandoObject() as IDictionary<string, object>;

			for (var i = 0; i < length; i++)
			{
				var key = Deserialize().ToString();
				var value = Deserialize();

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
				retobject[i] = Deserialize();
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
				v = (v >> 16) | (v << 16); // swap adjacent 16-bit blocks
				v = ((v & 0xFF00FF00u) >> 8) | ((v & 0x00FF00FFu) << 8); // swap adjacent 8-bit blocks
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
				v = (ushort)((v >> 8) | (v << 8)); // swap adjacent 8-bit blocks

			return (ushort)v;
		}

		internal unsafe uint ReadUInt32()
		{
			uint v = *(uint*)AdvancePointer(4);

			if (BitConverter.IsLittleEndian)
			{
				v = (v >> 16) | (v << 16); // swap adjacent 16-bit blocks
				v = ((v & 0xFF00FF00u) >> 8) | ((v & 0x00FF00FFu) << 8); // swap adjacent 8-bit blocks
			}

			return v;
		}

		internal unsafe ulong ReadUInt64()
		{
			ulong v = *(ulong*)AdvancePointer(8);

			if (BitConverter.IsLittleEndian)
			{
				v = (v >> 32) | (v << 32); // swap adjacent 32-bit blocks
				v = ((v & 0xFFFF0000FFFF0000u) >> 16) | ((v & 0x0000FFFF0000FFFFu) << 16); // swap adjacent 16-bit blocks
				v = ((v & 0xFF00FF00FF00FF00u) >> 8) | ((v & 0x00FF00FF00FF00FFu) << 8); // swap adjacent 8-bit blocks
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

		internal unsafe void SkipString(uint length) => AdvancePointer(length);

		/*[SecuritySafeCritical]
		private unsafe CString ReadCString(uint length)
		{
			byte* v = AdvancePointer(length);
			return CString.Create(v, length);
		}*/

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
				default: throw new InvalidOperationException($"Extension type {extType} not supported.");
			}
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
				default: throw new InvalidOperationException($"Extension type {extType} not supported.");
			}
		}

		internal void SkipExtraType(uint length)
		{
			var extType = ReadByte();
			switch (extType)
			{
				case 10: // remote funcref
				case 11: // local funcref
					SkipCallback(length); return;
				case 20: SkipVector2(); return;
				case 21: SkipVector3(); return;
				case 22: SkipVector4(); return;
				case 23: SkipQuaternion(); return;
				default: throw new InvalidOperationException($"Extension type {extType} not supported.");
			}
		}

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

		#region statics (easier access)

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
