using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

// We're explicitly casting to make our intent clear and spot errors quicker, hence the word "explicit"
// Sadly generics are quite limiting or else we could've turn all these in 1 function.
#pragma warning disable IDE0004 // Remove unnecessary cast

namespace MsgPack
{
	delegate object DeserializeFunc(ref MsgPackDeserializer deserializer);
	delegate T GetMethod<out T>(ref MsgPackDeserializer deserializer);
	unsafe delegate byte* GetAdvancePointer(ref MsgPackDeserializer deserializer, uint amount);

	public partial struct MsgPackDeserializer
	{
		static readonly Dictionary<Type, KeyValuePair<MethodInfo, MethodInfo>> s_typeConversions = new Dictionary<Type, KeyValuePair<MethodInfo, MethodInfo>>();

		#region Remove these

		[Obsolete("32 or 64 bits are as fast or faster")]
		public unsafe byte DeserializeToUInt8()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return type;
			else if (type > 0xDF)
				return (byte)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return 0;
				case 0xc3: return 1;
				case 0xca: return (byte)ReadSingle();
				case 0xcb: return (byte)ReadDouble();
				case 0xcc: return (byte)ReadUInt8();
				case 0xcd: return (byte)ReadUInt16();
				case 0xce: return (byte)ReadUInt32();
				case 0xcf: return (byte)ReadUInt64();
				case 0xd0: return (byte)ReadInt8();
				case 0xd1: return (byte)ReadInt16();
				case 0xd2: return (byte)ReadInt32();
				case 0xd3: return (byte)ReadInt64();
				default: return 0;
			}
		}

		[Obsolete("32 or 64 bits are as fast or faster")]
		public unsafe ushort DeserializeToUInt16()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) return type;
			else if (type > 0xDF) return (ushort)(type - 256);
			else switch (type)
				{
					case 0xc0: // null
					case 0xc2: return 0;
					case 0xc3: return 1;
					case 0xca: return (ushort)ReadSingle();
					case 0xcb: return (ushort)ReadDouble();
					case 0xcc: return (ushort)ReadUInt8();
					case 0xcd: return (ushort)ReadUInt16();
					case 0xce: return (ushort)ReadUInt32();
					case 0xcf: return (ushort)ReadUInt64();
					case 0xd0: return (ushort)ReadInt8();
					case 0xd1: return (ushort)ReadInt16();
					case 0xd2: return (ushort)ReadInt32();
					case 0xd3: return (ushort)ReadInt64();
					default: return 0;
				}
		}

		[Obsolete("32 or 64 bits are as fast or faster")]
		public unsafe sbyte DeserializeToInt8()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (sbyte)type;
			else if (type > 0xDF)
				return (sbyte)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return 0;
				case 0xc3: return 1;
				case 0xca: return (sbyte)ReadSingle();
				case 0xcb: return (sbyte)ReadDouble();
				case 0xcc: return (sbyte)ReadUInt8();
				case 0xcd: return (sbyte)ReadUInt16();
				case 0xce: return (sbyte)ReadUInt32();
				case 0xcf: return (sbyte)ReadUInt64();
				case 0xd0: return (sbyte)ReadInt8();
				case 0xd1: return (sbyte)ReadInt16();
				case 0xd2: return (sbyte)ReadInt32();
				case 0xd3: return (sbyte)ReadInt64();
				default: return 0;
			}
		}

		[Obsolete("32 or 64 bits are as fast or faster")]
		public unsafe short DeserializeToInt16()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return type;
			else if (type > 0xDF)
				return (short)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return 0;
				case 0xc3: return 1;
				case 0xca: return (short)ReadSingle();
				case 0xcb: return (short)ReadDouble();
				case 0xcc: return (short)ReadUInt8();
				case 0xcd: return (short)ReadUInt16();
				case 0xce: return (short)ReadUInt32();
				case 0xcf: return (short)ReadUInt64();
				case 0xd0: return (short)ReadInt8();
				case 0xd1: return (short)ReadInt16();
				case 0xd2: return (short)ReadInt32();
				case 0xd3: return (short)ReadInt64();
				default: return 0;
			}
		}

		#endregion

		#region Integer based

		public unsafe bool DeserializeToBool()
		{
			byte type = ReadByte();
			if (type < 0x80) // positive fixint
				return type != 0;
			else if (type > 0xDF) // negative fixint
				return true; // is always != 0

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return false;
				case 0xc3: return true;
				case 0xd0: // int8
				case 0xcc: // uint8
					return *(byte*)AdvancePointer(1) != 0;
				case 0xd1: // int16
				case 0xcd: // uint16
					return *(ushort*)AdvancePointer(2) != 0;
				case 0xd2: // int32
				case 0xce: // uint32
				case 0xca: // float
					return *(uint*)AdvancePointer(4) != 0;
				case 0xd3: // int64
				case 0xcf: // uint64
				case 0xcb: // double
					return *(ulong*)AdvancePointer(8) != 0;
				default:
					return false;
			}

			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(bool)}");
		}

		public unsafe uint DeserializeToUInt32()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (uint)type;
			else if (type > 0xDF)
				return (uint)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return (uint)0;
				case 0xc3: return (uint)1;
				case 0xca: return (uint)ReadSingle();
				case 0xcb: return (uint)ReadDouble();
				case 0xcc: return (uint)ReadUInt8();
				case 0xcd: return (uint)ReadUInt16();
				case 0xce: return (uint)ReadUInt32();
				case 0xcf: return (uint)ReadUInt64();
				case 0xd0: return (uint)ReadInt8();
				case 0xd1: return (uint)ReadInt16();
				case 0xd2: return (uint)ReadInt32();
				case 0xd3: return (uint)ReadInt64();
				default: return 0;
			}

			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(uint)}");
		}

		public unsafe ulong DeserializeToUInt64()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (ulong)type;
			else if (type > 0xDF)
				return (ulong)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return (ulong)0;
				case 0xc3: return (ulong)1;
				case 0xca: return (ulong)ReadSingle();
				case 0xcb: return (ulong)ReadDouble();
				case 0xcc: return (ulong)ReadUInt8();
				case 0xcd: return (ulong)ReadUInt16();
				case 0xce: return (ulong)ReadUInt32();
				case 0xcf: return (ulong)ReadUInt64();
				case 0xd0: return (ulong)ReadInt8();
				case 0xd1: return (ulong)ReadInt16();
				case 0xd2: return (ulong)ReadInt32();
				case 0xd3: return (ulong)ReadInt64();
				default: return 0;
			}
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(ulong)}");
		}

		public unsafe int DeserializeToInt32()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (int)type;
			else if (type > 0xDF)
				return (int)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return (int)0;
				case 0xc3: return (int)1;
				case 0xca: return (int)ReadSingle();
				case 0xcb: return (int)ReadDouble();
				case 0xcc: return (int)ReadUInt8();
				case 0xcd: return (int)ReadUInt16();
				case 0xce: return (int)ReadUInt32();
				case 0xcf: return (int)ReadUInt64();
				case 0xd0: return (int)ReadInt8();
				case 0xd1: return (int)ReadInt16();
				case 0xd2: return (int)ReadInt32();
				case 0xd3: return (int)ReadInt64();
				default: return 0;
			}

			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(int)}");
		}

		public unsafe long DeserializeToInt64()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (long)type;
			else if (type > 0xDF)
				return (long)(type - 256); // fix negative number

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return (long)0;
				case 0xc3: return (long)1;
				case 0xca: return (long)ReadSingle();
				case 0xcb: return (long)ReadDouble();
				case 0xcc: return (long)ReadUInt8();
				case 0xcd: return (long)ReadUInt16();
				case 0xce: return (long)ReadUInt32();
				case 0xcf: return (long)ReadUInt64();
				case 0xd0: return (long)ReadInt8();
				case 0xd1: return (long)ReadInt16();
				case 0xd2: return (long)ReadInt32();
				case 0xd3: return (long)ReadInt64();
				default: return 0;
			}

			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(long)}");
		}

		#endregion
		public unsafe string DeserializeToString()
		{
			MsgPackCode type = (MsgPackCode)ReadByte();
			if (type <= MsgPackCode.FixStrMax)
			{
				if (type <= MsgPackCode.FixIntPositiveMax)
					return ((byte)type).ToString();
				else if (type <= MsgPackCode.FixMapMax)
				{
					SkipArray((byte)type % 16u);
					return "Dictionary<object, object>";
				}
				else if (type <= MsgPackCode.FixArrayMax)
				{
					SkipArray((byte)type % 16u);
					return "object[]";
				}
				else
					return ReadString((byte)type % 32u);
			}
			else if (type >= MsgPackCode.FixIntNegativeMin)
			{
				return unchecked((sbyte)type).ToString(); // fix negative number
			}

			switch (type)
			{
				case MsgPackCode.Nil: return null;
				case MsgPackCode.False: return "false";
				case MsgPackCode.True: return "true";
				case MsgPackCode.Float32: return ReadSingle().ToString();
				case MsgPackCode.Float64: return ReadDouble().ToString();
				case MsgPackCode.UInt8: return ReadUInt8().ToString();
				case MsgPackCode.UInt16: return ReadUInt16().ToString();
				case MsgPackCode.UInt32: return ReadUInt32().ToString();
				case MsgPackCode.UInt64: return ReadUInt64().ToString();
				case MsgPackCode.Int8: return ReadInt8().ToString();
				case MsgPackCode.Int16: return ReadInt16().ToString();
				case MsgPackCode.Int32: return ReadInt32().ToString();
				case MsgPackCode.Int64: return ReadInt64().ToString();

				case MsgPackCode.FixExt1: return ReadExtraTypeToString(1);
				case MsgPackCode.FixExt2: return ReadExtraTypeToString(2);
				case MsgPackCode.FixExt4: return ReadExtraTypeToString(4);
				case MsgPackCode.FixExt8: return ReadExtraTypeToString(8);
				case MsgPackCode.FixExt16: return ReadExtraTypeToString(16);

				case MsgPackCode.Str8: return ReadString(ReadUInt8());
				case MsgPackCode.Str16: return ReadString(ReadUInt16());
				case MsgPackCode.Str32: return ReadString(ReadUInt32());

				case MsgPackCode.Array16: SkipArray(ReadUInt16()); return "object[]";
				case MsgPackCode.Array32: SkipArray(ReadUInt32()); return "object[]";

				case MsgPackCode.Map16: SkipMap(ReadUInt16()); return nameof(Dictionary<object, object>);
				case MsgPackCode.Map32: SkipMap(ReadUInt32()); return nameof(Dictionary<object, object>);
			}

			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(string)}");
		}

		#region Container

		public unsafe static Dictionary<string, string> DeserializeToDictionaryStringString()
		{
			Dictionary<string, string> result = new Dictionary<string, string>();



			return result;
		}

		public unsafe static void DeserializeDictionary(out Dictionary<string, string> result)
		{
			result = new Dictionary<string, string>();

			result.Add("key1", "key2");
			result.Add("key2", "key2");
			result.Add("key3", "key2");
			result.Add("key4", "key2");
			result.Add("key5", "key2");
			result.Add("key6", "key2");
			result.Add("key7", "key2");
		}

		#endregion

		#region Floating point number based

		public unsafe float DeserializeToFloat32()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (float)type;
			else if (type > 0xDF) // negative fixint
				return (float)unchecked((sbyte)type);

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return (float)0;
				case 0xc3: return (float)1;
				case 0xca: return (float)ReadSingle();
				case 0xcb: return (float)ReadDouble();
				case 0xcc: return (float)ReadUInt8();
				case 0xcd: return (float)ReadUInt16();
				case 0xce: return (float)ReadUInt32();
				case 0xcf: return (float)ReadUInt64();
				case 0xd0: return (float)ReadInt8();
				case 0xd1: return (float)ReadInt16();
				case 0xd2: return (float)ReadInt32();
				case 0xd3: return (float)ReadInt64();
				default: return (float)0;
			}
		}

		public unsafe double DeserializeToFloat64()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (double)type;
			else if (type > 0xDF) // negative fixint
				return (float)unchecked((sbyte)type);

			switch (type)
			{
				case 0xc0: // null
				case 0xc2: return (double)0;
				case 0xc3: return (double)1;
				case 0xca: return (double)ReadSingle();
				case 0xcb: return (double)ReadDouble();
				case 0xcc: return (double)ReadUInt8();
				case 0xcd: return (double)ReadUInt16();
				case 0xce: return (double)ReadUInt32();
				case 0xcf: return (double)ReadUInt64();
				case 0xd0: return (double)ReadInt8();
				case 0xd1: return (double)ReadInt16();
				case 0xd2: return (double)ReadInt32();
				case 0xd3: return (double)ReadInt64();
				default: return (double)0;
			}
		}

		#endregion
	}

	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public class IndexAttribute : Attribute
	{
		private readonly uint m_index = uint.MaxValue;
		private readonly byte[] m_key = null;

		public uint Index => m_index;
		public string Key => Encoding.UTF8.GetString(m_key);

		public IndexAttribute(uint index) => m_index = index;
		public IndexAttribute(string key) => m_key = Encoding.UTF8.GetBytes(key);
	}
}

#pragma warning restore IDE0004 // Remove unnecessary cast