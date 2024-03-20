using CitizenFX.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

// We're explicitly casting to make our intent clear and spot errors quicker, hence the word "explicit"
// Sadly generics are quite limiting or else we could've turn all these in 1 function.
#pragma warning disable IDE0004 // Remove unnecessary cast

namespace CitizenFX.MsgPack
{
	public partial struct MsgPackDeserializer
	{
		// NOTE:
		//   1. When adding any T DeserializeAsT() method, make sure there's an equivalent Serialize(T) in the MsgPackSerializer class.
		//   2. DeserializeAsT() methods also read the type header, sizes, and pick the correct ReadU() method.
		//   3. U ReadU() methods interpret the memory as if it's of type U and uses optionally given size/length data, no other checks are done, these should stay private.
		//   4. See specifications: https://github.com/msgpack/msgpack/blob/master/spec.md

		#region Direct buffer deserialization

		/// <summary>
		/// Deserialize from given data
		/// </summary>
		/// <param name="data">MsgPacked byte data</param>
		/// <param name="netSource">From whom came this?</param>
		/// <returns>(Array of) argument(s) that can be passed into dynamic delegates</returns>
		internal static unsafe object DeserializeAsObject(byte[] data, string netSource = null)
		{
			if (data?.Length > 0)
			{
				fixed (byte* dataPtr = data)
					return DeserializeAsObject(dataPtr, data.Length, netSource);
			}

			return null;
		}

		/// <param name="data">Pointer to MsgPacked byte data</param>
		/// <param name="size">Size of MsgPacked byte data</param>
		/// <param name="netSource">From whom came this?</param>
		/// <inheritdoc cref="DeserializeAsObject(byte[], string)"/>
		internal static unsafe object DeserializeAsObject(byte* data, long size, string netSource = null)
		{
			if (data != null && size > 0)
			{
				var deserializer = new MsgPackDeserializer(data, (ulong)size, netSource);
				return deserializer.DeserializeAsObject();
			}

			return null;
		}

		/// <inheritdoc cref="DeserializeAsObject(byte*, long, string)"/>
		public static unsafe object[] DeserializeAsObjectArray(byte* data, long size, string netSource = null)
		{
			if (data != null && size > 0)
			{
				var deserializer = new MsgPackDeserializer(data, (ulong)size, netSource);
				return deserializer.DeserializeAsObjectArray();
			}

			return new object[0];
		}

		#endregion

		#region Basic types

		public object DeserializeAsObject()
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
					return ReadMapAsExpando(type % 16u);
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

				case 0xDE: return ReadMapAsExpando(ReadUInt16());
				case 0xDF: return ReadMapAsExpando(ReadUInt32());
			}

			throw new InvalidOperationException($"Tried to decode invalid MsgPack type {type}");
		}

		internal unsafe void SkipObject(byte type)
		{
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

		internal void SkipObject() => SkipObject(ReadByte());

		public unsafe bool DeserializeAsBool()
		{
			byte type = ReadByte();
			if (type < 0x80) // positive fixint
				return type != 0;
			else if (type < 0xC0) // fixstr
				return ReadStringAsTrueish(type % 32u);
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

				case 0xd9: return ReadStringAsTrueish(ReadUInt8());
				case 0xda: return ReadStringAsTrueish(ReadUInt16());
				case 0xdb: return ReadStringAsTrueish(ReadUInt32());
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(bool)}");
		}

		public unsafe uint DeserializeAsUInt32()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (uint)type;
			else if (type > 0xA0) // fixstr
			{
				if (type < 0xC0)
					return uint.Parse(ReadString((uint)type - 0xA0));
				else if (type > 0xDF)
					return unchecked((uint)(sbyte)type); // fix negative number
			}

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

				case 0xd4: return (uint)ReadExtraTypeAsInt64(1);
				case 0xd5: return (uint)ReadExtraTypeAsInt64(2);
				case 0xd6: return (uint)ReadExtraTypeAsInt64(4);
				case 0xd7: return (uint)ReadExtraTypeAsInt64(8);
				case 0xd8: return (uint)ReadExtraTypeAsInt64(16);

				case 0xd9: return uint.Parse(ReadString(ReadUInt8()));
				case 0xda: return uint.Parse(ReadString(ReadUInt16()));
				case 0xdb: return uint.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(uint)}");
		}

		public unsafe ulong DeserializeAsUInt64()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (ulong)type;
			else if (type > 0xA0) // fixstr
			{
				if (type < 0xC0)
					return ulong.Parse(ReadString((uint)type - 0xA0));
				else if (type > 0xDF)
					return unchecked((ulong)(sbyte)type); // fix negative number
			}

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

				case 0xd4: return (ulong)ReadExtraTypeAsInt64(1);
				case 0xd5: return (ulong)ReadExtraTypeAsInt64(2);
				case 0xd6: return (ulong)ReadExtraTypeAsInt64(4);
				case 0xd7: return (ulong)ReadExtraTypeAsInt64(8);
				case 0xd8: return (ulong)ReadExtraTypeAsInt64(16);

				case 0xd9: return ulong.Parse(ReadString(ReadUInt8()));
				case 0xda: return ulong.Parse(ReadString(ReadUInt16()));
				case 0xdb: return ulong.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(ulong)}");
		}

		public unsafe int DeserializeAsInt32()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (int)type;
			else if (type > 0xA0) // fixstr
			{
				if (type < 0xC0)
					return int.Parse(ReadString((uint)type - 0xA0));
				else if (type > 0xDF)
					return unchecked((int)(sbyte)type); // fix negative number
			}

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

				case 0xd4: return (int)ReadExtraTypeAsInt64(1);
				case 0xd5: return (int)ReadExtraTypeAsInt64(2);
				case 0xd6: return (int)ReadExtraTypeAsInt64(4);
				case 0xd7: return (int)ReadExtraTypeAsInt64(8);
				case 0xd8: return (int)ReadExtraTypeAsInt64(16);

				case 0xd9: return int.Parse(ReadString(ReadUInt8()));
				case 0xda: return int.Parse(ReadString(ReadUInt16()));
				case 0xdb: return int.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(int)}");
		}

		public unsafe long DeserializeAsInt64()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (long)type;
			else if (type > 0xA0) // fixstr
			{
				if (type < 0xC0)
					return long.Parse(ReadString((uint)type - 0xA0));
				else if (type > 0xDF)
					return unchecked((long)(sbyte)type); // fix negative number
			}

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

				case 0xd4: return (long)ReadExtraTypeAsInt64(1);
				case 0xd5: return (long)ReadExtraTypeAsInt64(2);
				case 0xd6: return (long)ReadExtraTypeAsInt64(4);
				case 0xd7: return (long)ReadExtraTypeAsInt64(8);
				case 0xd8: return (long)ReadExtraTypeAsInt64(16);

				case 0xd9: return long.Parse(ReadString(ReadUInt8()));
				case 0xda: return long.Parse(ReadString(ReadUInt16()));
				case 0xdb: return long.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(long)}");
		}

		public unsafe float DeserializeAsFloat32()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (float)type;
			else if (type > 0xA0) // fixstr
			{
				if (type < 0xC0)
					return float.Parse(ReadString((uint)type - 0xA0));
				else if (type > 0xDF)
					return unchecked((float)(sbyte)type); // negative fixint
			}

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

				case 0xd4: return (float)ReadExtraTypeAsFloat32(1);
				case 0xd5: return (float)ReadExtraTypeAsFloat32(2);
				case 0xd6: return (float)ReadExtraTypeAsFloat32(4);
				case 0xd7: return (float)ReadExtraTypeAsFloat32(8);
				case 0xd8: return (float)ReadExtraTypeAsFloat32(16);

				case 0xd9: return float.Parse(ReadString(ReadUInt8()));
				case 0xda: return float.Parse(ReadString(ReadUInt16()));
				case 0xdb: return float.Parse(ReadString(ReadUInt32()));
			}

			SkipObject((byte)type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(float)}");
		}

		public unsafe double DeserializeAsFloat64()
		{
			byte type = *AdvancePointer(1);
			if (type < 0x80) // positive fixint
				return (double)type;
			else if (type > 0xA0) // fixstr
			{
				if (type < 0xC0)
					return double.Parse(ReadString((uint)type - 0xA0));
				else if (type > 0xDF)
					return unchecked((double)(sbyte)type); // negative fixint
			}

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

				case 0xd4: return (double)ReadExtraTypeAsFloat32(1);
				case 0xd5: return (double)ReadExtraTypeAsFloat32(2);
				case 0xd6: return (double)ReadExtraTypeAsFloat32(4);
				case 0xd7: return (double)ReadExtraTypeAsFloat32(8);
				case 0xd8: return (double)ReadExtraTypeAsFloat32(16);

				case 0xd9: return double.Parse(ReadString(ReadUInt8()));
				case 0xda: return double.Parse(ReadString(ReadUInt16()));
				case 0xdb: return double.Parse(ReadString(ReadUInt32()));
			}

			SkipObject((byte)type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(double)}");
		}

		public unsafe string DeserializeAsString()
		{
			MsgPackCode type = (MsgPackCode)ReadByte();
			if (type <= MsgPackCode.FixStrMax)
			{
				if (type <= MsgPackCode.FixIntPositiveMax)
					return ((byte)type).ToString();
				else if (type <= MsgPackCode.FixMapMax)
				{
					SkipMap((byte)type % 16u);
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

			SkipObject((byte)type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(string)}");
		}

		#endregion

		#region Premade associative array deserializers

		public Dictionary<string, string> DeserializeAsDictionaryStringString()
		{
			var type = ReadByte();

			if (type >= 0x80 && type < 0x90)
				return ReadMapAsDictStringString(type % 16u);

			switch (type)
			{
				case 0xC0: return null;

				case 0xDE: return ReadMapAsDictStringString(ReadUInt16());
				case 0xDF: return ReadMapAsDictStringString(ReadUInt32());
			}

			throw new InvalidOperationException($"Tried to decode invalid MsgPack type {type}");
		}

		public IReadOnlyDictionary<string, string> DeserializeAsIReadOnlyDictionaryStringString() => DeserializeAsDictionaryStringString();
		public IDictionary<string, string> DeserializeAsIDictionaryStringString() => DeserializeAsDictionaryStringString();

		#endregion

		#region Premade array deserializers

		public object[] DeserializeAsObjectArray()
		{
			uint length = ReadArraySize();

			object[] array = new object[length];
			for (var i = 0; i < length; ++i)
				array[i] = DeserializeAsObject();

			return array;
		}

		public string[] DeserializeAsStringArray()
		{
			uint length = ReadArraySize();

			string[] array = new string[length];
			for (var i = 0; i < length; ++i)
				array[i] = DeserializeAsString();

			return array;
		}

		#endregion

		#region Extra types

		public Callback DeserializeAsCallback()
		{
			byte type = ReadByte();
			switch (type)
			{
				case 0xC7: SkipBytes(1); goto case 0xD8;
				case 0xC8: SkipBytes(2); goto case 0xD8;
				case 0xC9: SkipBytes(4); goto case 0xD8;

				case 0xD4: // 1
				case 0xD5: // 2
				case 0xD6: // 4
				case 0xD7: // 8
				case 0xD8: // 16
					{
						var extType = ReadByte();
						return extType == 10 || extType == 11
							? ReadCallback(ReadUInt8())
							: throw new InvalidCastException($"MsgPack extra type {extType} could not be deserialized into type {typeof(Callback)}");
					}
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(Callback)}");
		}

		#endregion

		#region Statics (easier access with current IL generation)

		public static uint DeserializeAsUInt32(ref MsgPackDeserializer deserializer) => deserializer.DeserializeAsUInt32();
		public static string DeserializeAsString(ref MsgPackDeserializer deserializer) => deserializer.DeserializeAsString();
		public static string[] DeserializeAsStringArray(ref MsgPackDeserializer deserializer) => deserializer.DeserializeAsStringArray();

		#endregion
	}
}

#pragma warning restore IDE0004 // Remove unnecessary cast