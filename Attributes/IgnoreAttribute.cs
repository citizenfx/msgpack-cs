using System;

namespace MsgPack
{
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public class IgnoreAttribute : Attribute
	{
	}
}
