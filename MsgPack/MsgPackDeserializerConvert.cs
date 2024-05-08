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
	public ref partial struct MsgPackDeserializer
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
			MsgPackCode type = ReadType();

			if (type <= MsgPackCode.FixStrMax)
			{
				if (type <= MsgPackCode.FixIntPositiveMax)
					return type;
				else if (type <= MsgPackCode.FixMapMax)
					return ReadMapAsExpando((uint)type % 16u);
				else if (type < MsgPackCode.FixArrayMax)
					return ReadObjectArray((uint)type % 16u);

				return ReadString((uint)type % 32u);
			}
			else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
			{
				return unchecked((sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: return null;

				case MsgPackCode.False: return false;
				case MsgPackCode.True: return true;

				case MsgPackCode.Bin8: return ReadBytes(ReadUInt8());
				case MsgPackCode.Bin16: return ReadBytes(ReadUInt16());
				case MsgPackCode.Bin32: return ReadBytes(ReadUInt32());

				case MsgPackCode.Ext8: return ReadExtraType(ReadUInt8());
				case MsgPackCode.Ext16: return ReadExtraType(ReadUInt16());
				case MsgPackCode.Ext32: return ReadExtraType(ReadUInt32());

				case MsgPackCode.Float32: return ReadSingle();
				case MsgPackCode.Float64: return ReadDouble();

				case MsgPackCode.UInt8: return ReadUInt8();
				case MsgPackCode.UInt16: return ReadUInt16();
				case MsgPackCode.UInt32: return ReadUInt32();
				case MsgPackCode.UInt64: return ReadUInt64();

				case MsgPackCode.Int8: return ReadInt8();
				case MsgPackCode.Int16: return ReadInt16();
				case MsgPackCode.Int32: return ReadInt32();
				case MsgPackCode.Int64: return ReadInt64();

				case MsgPackCode.FixExt1: return ReadExtraType(1);
				case MsgPackCode.FixExt2: return ReadExtraType(2);
				case MsgPackCode.FixExt4: return ReadExtraType(4);
				case MsgPackCode.FixExt8: return ReadExtraType(8);
				case MsgPackCode.FixExt16: return ReadExtraType(16);

				case MsgPackCode.Str8: return ReadString(ReadUInt8());
				case MsgPackCode.Str16: return ReadString(ReadUInt16());
				case MsgPackCode.Str32: return ReadString(ReadUInt32());

				case MsgPackCode.Array16: return ReadObjectArray(ReadUInt16());
				case MsgPackCode.Array32: return ReadObjectArray(ReadUInt32());

				case MsgPackCode.Map16: return ReadMapAsExpando(ReadUInt16());
				case MsgPackCode.Map32: return ReadMapAsExpando(ReadUInt32());
			}

			throw new InvalidOperationException($"Tried to decode invalid MsgPack type {type}");
		}

		internal unsafe void SkipObject(MsgPackCode type)
		{
			if (type <= MsgPackCode.FixStrMax)
			{
				if (type <= MsgPackCode.FixMapMax)
					SkipMap((uint)type % 16u);
				else if (type <= MsgPackCode.FixArrayMax)
					SkipArray((uint)type % 16u);
				else
					SkipString((uint)type % 32u);

				return;
			}
			else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
			{
				return;
			}

			switch (type)
			{
				case MsgPackCode.Bin8: AdvancePointer(ReadUInt8()); return;
				case MsgPackCode.Bin16: AdvancePointer(ReadUInt16()); return;
				case MsgPackCode.Bin32: AdvancePointer(ReadUInt32()); return;

				case MsgPackCode.Ext8: SkipExtraType(ReadUInt8()); return;
				case MsgPackCode.Ext16: SkipExtraType(ReadUInt16()); return;
				case MsgPackCode.Ext32: SkipExtraType(ReadUInt32()); return;

				case MsgPackCode.Float32: AdvancePointer(4); return;
				case MsgPackCode.Float64: AdvancePointer(8); return;

				case MsgPackCode.UInt8: AdvancePointer(1); return;
				case MsgPackCode.UInt16: AdvancePointer(2); return;
				case MsgPackCode.UInt32: AdvancePointer(3); return;
				case MsgPackCode.UInt64: AdvancePointer(4); return;

				case MsgPackCode.Int8: AdvancePointer(1); return;
				case MsgPackCode.Int16: AdvancePointer(2); return;
				case MsgPackCode.Int32: AdvancePointer(3); return;
				case MsgPackCode.Int64: AdvancePointer(4); return;

				case MsgPackCode.FixExt1: SkipExtraType(1); return;
				case MsgPackCode.FixExt2: SkipExtraType(2); return;
				case MsgPackCode.FixExt4: SkipExtraType(4); return;
				case MsgPackCode.FixExt8: SkipExtraType(8); return;
				case MsgPackCode.FixExt16: SkipExtraType(16); return;

				case MsgPackCode.Str8: SkipString(ReadUInt8()); return;
				case MsgPackCode.Str16: SkipString(ReadUInt16()); return;
				case MsgPackCode.Str32: SkipString(ReadUInt32()); return;

				case MsgPackCode.Array16: SkipArray(ReadUInt16()); return;
				case MsgPackCode.Array32: SkipArray(ReadUInt32()); return;

				case MsgPackCode.Map16: SkipMap(ReadUInt16()); return;
				case MsgPackCode.Map32: SkipMap(ReadUInt32()); return;
			}

			throw new InvalidOperationException($"Tried to decode invalid MsgPack type {type}");
		}

		internal void SkipObject() => SkipObject((MsgPackCode)ReadByte());

		internal void SkipObjects(uint size)
		{
			for (uint i = 0; i < size; ++i)
				SkipObject();
		}

		public unsafe bool DeserializeAsBool()
		{
			MsgPackCode type = ReadType();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return type != 0;
			else if (type <= MsgPackCode.FixStrMax) // fixstr
				return ReadStringAsTrueish((uint)type % 32u);
			else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
				return true; // is always != 0

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return false;
				case MsgPackCode.True: return true;
				case MsgPackCode.Int8: // int8
				case MsgPackCode.UInt8: // uint8
					return *(byte*)AdvancePointer(1) != 0;
				case MsgPackCode.Int16: // int16
				case MsgPackCode.UInt16: // uint16
					return *(ushort*)AdvancePointer(2) != 0;
				case MsgPackCode.Int32: // int32
				case MsgPackCode.UInt32: // uint32
				case MsgPackCode.Float32: // float
					return *(uint*)AdvancePointer(4) != 0;
				case MsgPackCode.Int64: // int64
				case MsgPackCode.UInt64: // uint64
				case MsgPackCode.Float64: // double
					return *(ulong*)AdvancePointer(8) != 0;

				case MsgPackCode.Str8: return ReadStringAsTrueish(ReadUInt8());
				case MsgPackCode.Str16: return ReadStringAsTrueish(ReadUInt16());
				case MsgPackCode.Str32: return ReadStringAsTrueish(ReadUInt32());
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(bool)}");
		}

		public uint DeserializeAsUInt32()
		{
			MsgPackCode type = ReadType();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return (uint)type;
			else if (type >= MsgPackCode.FixStrMin) // fixstr
			{
				if (type <= MsgPackCode.FixStrMax)
					return uint.Parse(ReadString((uint)type - (uint)MsgPackCode.FixStrMin));
				else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
					return unchecked((uint)(sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return (uint)0;
				case MsgPackCode.True: return (uint)1;
				case MsgPackCode.Float32: return (uint)ReadSingle();
				case MsgPackCode.Float64: return (uint)ReadDouble();
				case MsgPackCode.UInt8: return (uint)ReadUInt8();
				case MsgPackCode.UInt16: return (uint)ReadUInt16();
				case MsgPackCode.UInt32: return (uint)ReadUInt32();
				case MsgPackCode.UInt64: return (uint)ReadUInt64();
				case MsgPackCode.Int8: return (uint)ReadInt8();
				case MsgPackCode.Int16: return (uint)ReadInt16();
				case MsgPackCode.Int32: return (uint)ReadInt32();
				case MsgPackCode.Int64: return (uint)ReadInt64();

				case MsgPackCode.FixExt1: return (uint)ReadExtraTypeAsInt64(1);
				case MsgPackCode.FixExt2: return (uint)ReadExtraTypeAsInt64(2);
				case MsgPackCode.FixExt4: return (uint)ReadExtraTypeAsInt64(4);
				case MsgPackCode.FixExt8: return (uint)ReadExtraTypeAsInt64(8);
				case MsgPackCode.FixExt16: return (uint)ReadExtraTypeAsInt64(16);

				case MsgPackCode.Str8: return uint.Parse(ReadString(ReadUInt8()));
				case MsgPackCode.Str16: return uint.Parse(ReadString(ReadUInt16()));
				case MsgPackCode.Str32: return uint.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(uint)}");
		}

		public ulong DeserializeAsUInt64()
		{
			MsgPackCode type = ReadType();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return (ulong)type;
			else if (type >= MsgPackCode.FixStrMin) // fixstr
			{
				if (type <= MsgPackCode.FixStrMax)
					return ulong.Parse(ReadString((uint)type - (uint)MsgPackCode.FixStrMin));
				else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
					return unchecked((ulong)(sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return (ulong)0;
				case MsgPackCode.True: return (ulong)1;
				case MsgPackCode.Float32: return (ulong)ReadSingle();
				case MsgPackCode.Float64: return (ulong)ReadDouble();
				case MsgPackCode.UInt8: return (ulong)ReadUInt8();
				case MsgPackCode.UInt16: return (ulong)ReadUInt16();
				case MsgPackCode.UInt32: return (ulong)ReadUInt32();
				case MsgPackCode.UInt64: return (ulong)ReadUInt64();
				case MsgPackCode.Int8: return (ulong)ReadInt8();
				case MsgPackCode.Int16: return (ulong)ReadInt16();
				case MsgPackCode.Int32: return (ulong)ReadInt32();
				case MsgPackCode.Int64: return (ulong)ReadInt64();

				case MsgPackCode.FixExt1: return (ulong)ReadExtraTypeAsInt64(1);
				case MsgPackCode.FixExt2: return (ulong)ReadExtraTypeAsInt64(2);
				case MsgPackCode.FixExt4: return (ulong)ReadExtraTypeAsInt64(4);
				case MsgPackCode.FixExt8: return (ulong)ReadExtraTypeAsInt64(8);
				case MsgPackCode.FixExt16: return (ulong)ReadExtraTypeAsInt64(16);

				case MsgPackCode.Str8: return ulong.Parse(ReadString(ReadUInt8()));
				case MsgPackCode.Str16: return ulong.Parse(ReadString(ReadUInt16()));
				case MsgPackCode.Str32: return ulong.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(ulong)}");
		}

		public int DeserializeAsInt32()
		{
			MsgPackCode type = ReadType();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return (int)type;
			else if (type >= MsgPackCode.FixStrMin) // fixstr
			{
				if (type <= MsgPackCode.FixStrMax)
					return int.Parse(ReadString((uint)type - (uint)MsgPackCode.FixStrMin));
				else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
					return unchecked((int)(sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return (int)0;
				case MsgPackCode.True: return (int)1;
				case MsgPackCode.Float32: return (int)ReadSingle();
				case MsgPackCode.Float64: return (int)ReadDouble();
				case MsgPackCode.UInt8: return (int)ReadUInt8();
				case MsgPackCode.UInt16: return (int)ReadUInt16();
				case MsgPackCode.UInt32: return (int)ReadUInt32();
				case MsgPackCode.UInt64: return (int)ReadUInt64();
				case MsgPackCode.Int8: return (int)ReadInt8();
				case MsgPackCode.Int16: return (int)ReadInt16();
				case MsgPackCode.Int32: return (int)ReadInt32();
				case MsgPackCode.Int64: return (int)ReadInt64();

				case MsgPackCode.FixExt1: return (int)ReadExtraTypeAsInt64(1);
				case MsgPackCode.FixExt2: return (int)ReadExtraTypeAsInt64(2);
				case MsgPackCode.FixExt4: return (int)ReadExtraTypeAsInt64(4);
				case MsgPackCode.FixExt8: return (int)ReadExtraTypeAsInt64(8);
				case MsgPackCode.FixExt16: return (int)ReadExtraTypeAsInt64(16);

				case MsgPackCode.Str8: return int.Parse(ReadString(ReadUInt8()));
				case MsgPackCode.Str16: return int.Parse(ReadString(ReadUInt16()));
				case MsgPackCode.Str32: return int.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(int)}");
		}

		public long DeserializeAsInt64()
		{
			MsgPackCode type = ReadType();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return (long)type;
			else if (type >= MsgPackCode.FixStrMin) // fixstr
			{
				if (type <= MsgPackCode.FixStrMax)
					return long.Parse(ReadString((uint)type - (uint)MsgPackCode.FixStrMin));
				else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
					return unchecked((long)(sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return (long)0;
				case MsgPackCode.True: return (long)1;
				case MsgPackCode.Float32: return (long)ReadSingle();
				case MsgPackCode.Float64: return (long)ReadDouble();
				case MsgPackCode.UInt8: return (long)ReadUInt8();
				case MsgPackCode.UInt16: return (long)ReadUInt16();
				case MsgPackCode.UInt32: return (long)ReadUInt32();
				case MsgPackCode.UInt64: return (long)ReadUInt64();
				case MsgPackCode.Int8: return (long)ReadInt8();
				case MsgPackCode.Int16: return (long)ReadInt16();
				case MsgPackCode.Int32: return (long)ReadInt32();
				case MsgPackCode.Int64: return (long)ReadInt64();

				case MsgPackCode.FixExt1: return (long)ReadExtraTypeAsInt64(1);
				case MsgPackCode.FixExt2: return (long)ReadExtraTypeAsInt64(2);
				case MsgPackCode.FixExt4: return (long)ReadExtraTypeAsInt64(4);
				case MsgPackCode.FixExt8: return (long)ReadExtraTypeAsInt64(8);
				case MsgPackCode.FixExt16: return (long)ReadExtraTypeAsInt64(16);

				case MsgPackCode.Str8: return long.Parse(ReadString(ReadUInt8()));
				case MsgPackCode.Str16: return long.Parse(ReadString(ReadUInt16()));
				case MsgPackCode.Str32: return long.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(long)}");
		}

		public float DeserializeAsFloat32()
		{
			MsgPackCode type = (MsgPackCode)ReadByte();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return (float)type;
			else if (type >= MsgPackCode.FixStrMin) // fixstr
			{
				if (type <= MsgPackCode.FixStrMax)
					return float.Parse(ReadString((uint)type - (uint)MsgPackCode.FixStrMin));
				else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
					return unchecked((float)(sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return (float)0;
				case MsgPackCode.True: return (float)1;
				case MsgPackCode.Float32: return (float)ReadSingle();
				case MsgPackCode.Float64: return (float)ReadDouble();
				case MsgPackCode.UInt8: return (float)ReadUInt8();
				case MsgPackCode.UInt16: return (float)ReadUInt16();
				case MsgPackCode.UInt32: return (float)ReadUInt32();
				case MsgPackCode.UInt64: return (float)ReadUInt64();
				case MsgPackCode.Int8: return (float)ReadInt8();
				case MsgPackCode.Int16: return (float)ReadInt16();
				case MsgPackCode.Int32: return (float)ReadInt32();
				case MsgPackCode.Int64: return (float)ReadInt64();

				case MsgPackCode.FixExt1: return (float)ReadExtraTypeAsFloat32(1);
				case MsgPackCode.FixExt2: return (float)ReadExtraTypeAsFloat32(2);
				case MsgPackCode.FixExt4: return (float)ReadExtraTypeAsFloat32(4);
				case MsgPackCode.FixExt8: return (float)ReadExtraTypeAsFloat32(8);
				case MsgPackCode.FixExt16: return (float)ReadExtraTypeAsFloat32(16);

				case MsgPackCode.Str8: return float.Parse(ReadString(ReadUInt8()));
				case MsgPackCode.Str16: return float.Parse(ReadString(ReadUInt16()));
				case MsgPackCode.Str32: return float.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(float)}");
		}

		public double DeserializeAsFloat64()
		{
			MsgPackCode type = ReadType();
			if (type <= MsgPackCode.FixIntPositiveMax) // positive fixint
				return (double)type;
			else if (type >= MsgPackCode.FixStrMin) // fixstr
			{
				if (type <= MsgPackCode.FixStrMax)
					return double.Parse(ReadString((uint)type - (uint)MsgPackCode.FixStrMin));
				else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
					return unchecked((double)(sbyte)type);
			}

			switch (type)
			{
				case MsgPackCode.Nil: // null
				case MsgPackCode.False: return (double)0;
				case MsgPackCode.True: return (double)1;
				case MsgPackCode.Float32: return (double)ReadSingle();
				case MsgPackCode.Float64: return (double)ReadDouble();
				case MsgPackCode.UInt8: return (double)ReadUInt8();
				case MsgPackCode.UInt16: return (double)ReadUInt16();
				case MsgPackCode.UInt32: return (double)ReadUInt32();
				case MsgPackCode.UInt64: return (double)ReadUInt64();
				case MsgPackCode.Int8: return (double)ReadInt8();
				case MsgPackCode.Int16: return (double)ReadInt16();
				case MsgPackCode.Int32: return (double)ReadInt32();
				case MsgPackCode.Int64: return (double)ReadInt64();

				case MsgPackCode.FixExt1: return (double)ReadExtraTypeAsFloat32(1);
				case MsgPackCode.FixExt2: return (double)ReadExtraTypeAsFloat32(2);
				case MsgPackCode.FixExt4: return (double)ReadExtraTypeAsFloat32(4);
				case MsgPackCode.FixExt8: return (double)ReadExtraTypeAsFloat32(8);
				case MsgPackCode.FixExt16: return (double)ReadExtraTypeAsFloat32(16);

				case MsgPackCode.Str8: return double.Parse(ReadString(ReadUInt8()));
				case MsgPackCode.Str16: return double.Parse(ReadString(ReadUInt16()));
				case MsgPackCode.Str32: return double.Parse(ReadString(ReadUInt32()));
			}

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(double)}");
		}

		public string DeserializeAsString()
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
			else if (type >= MsgPackCode.FixIntNegativeMin) // anything at the end of our byte
			{
				return unchecked((sbyte)type).ToString();
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

			SkipObject(type);
			throw new InvalidCastException($"MsgPack type {type} could not be deserialized into type {typeof(string)}");
		}

		#endregion

		#region Premade associative array deserializers

		public Dictionary<string, string> DeserializeAsDictionaryStringString()
		{
			MsgPackCode type = ReadType();

			if (type >= MsgPackCode.FixMapMin && type <= MsgPackCode.FixMapMax)
				return ReadMapAsDictStringString((uint)type % 16u);

			switch (type)
			{
				case MsgPackCode.Nil: return null;

				case MsgPackCode.Map16: return ReadMapAsDictStringString(ReadUInt16());
				case MsgPackCode.Map32: return ReadMapAsDictStringString(ReadUInt32());
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
			MsgPackCode type = ReadType();
			switch (type)
			{
				case MsgPackCode.Ext8: SkipBytes(1); goto case MsgPackCode.FixExt16;
				case MsgPackCode.Ext16: SkipBytes(2); goto case MsgPackCode.FixExt16;
				case MsgPackCode.Ext32: SkipBytes(4); goto case MsgPackCode.FixExt16;

				case MsgPackCode.FixExt1: // 1
				case MsgPackCode.FixExt2: // 2
				case MsgPackCode.FixExt4: // 4
				case MsgPackCode.FixExt8: // 8
				case MsgPackCode.FixExt16: // 16
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