﻿
namespace CitizenFX.MsgPack
{
	internal enum MsgPackCode : byte
	{
		FixIntPositiveMin = 0x00,
		FixIntPositiveMax = 0x7f,
		FixMapMin = 0x80,
		FixMapMax = 0x8f,
		FixArrayMin = 0x90,
		FixArrayMax = 0x9f,
		FixStrMin = 0xa0,
		FixStrMax = 0xbf,
		Nil = 0xc0,
		Unused_0xC1 = 0xc1,
		False = 0xc2,
		True = 0xc3,
		Bin8 = 0xc4,
		Bin16 = 0xc5,
		Bin32 = 0xc6,
		Ext8 = 0xc7,
		Ext16 = 0xc8,
		Ext32 = 0xc9,
		Float32 = 0xca,
		Float64 = 0xcb,
		UInt8 = 0xcc,
		UInt16 = 0xcd,
		UInt32 = 0xce,
		UInt64 = 0xcf,
		Int8 = 0xd0,
		Int16 = 0xd1,
		Int32 = 0xd2,
		Int64 = 0xd3,
		FixExt1 = 0xd4,
		FixExt2 = 0xd5,
		FixExt4 = 0xd6,
		FixExt8 = 0xd7,
		FixExt16 = 0xd8,
		Str8 = 0xd9,
		Str16 = 0xda,
		Str32 = 0xdb,
		Array16 = 0xdc,
		Array32 = 0xdd,
		Map16 = 0xde,
		Map32 = 0xdf,
		FixIntNegativeMin = 0xe0,
		FixIntNegativeMax = 0xff
    }

    enum MsgPackExtraTypeCode
    {
		RemoteFunc = 10,
		LocalFunc = 11,

		Vector2 = 20,
		Vector3 = 21,
		Vector4 = 22,
		Quaternion = 22
    }
}
