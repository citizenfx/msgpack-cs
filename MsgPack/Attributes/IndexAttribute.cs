using System;

namespace CitizenFX.MsgPack
{
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public class IndexAttribute : Attribute
	{
		public uint Index { get; } = uint.MaxValue;

		public IndexAttribute(uint index) => Index = index;
	}
}
