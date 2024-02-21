using System;

namespace CitizenFX.MsgPack
{
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public class IgnoreAttribute : Attribute
	{
	}
}
