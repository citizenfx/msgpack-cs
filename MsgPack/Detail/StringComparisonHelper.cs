using System;
using System.Runtime.InteropServices;

namespace CitizenFX.MsgPack.Detail
{
	[StructLayout(LayoutKind.Explicit)]
	internal unsafe struct StringComparisonHelper
	{
		[FieldOffset(0)] public fixed byte u8[8];
		[FieldOffset(0)] public fixed ushort u16[4];
		[FieldOffset(0)] public fixed uint u32[2];
		[FieldOffset(0)] public fixed ulong u64[1];

		public StringComparisonHelper(string str, int offset = 0)
		{
			int length = str.Length - offset;
			if (length > 8)
				length = 8;

			for (int i = 0; i < length; ++i)
				u8[i] = (byte)str[i - offset];
		}
	}
}
