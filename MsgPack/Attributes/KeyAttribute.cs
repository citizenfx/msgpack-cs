using System;

namespace CitizenFX.MsgPack
{
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
	public class KeyAttribute : Attribute
	{
		public string Key { get; } = null;

		public KeyAttribute(string key) => Key = key;
	}
}
