using System;

namespace CitizenFX.MsgPack
{
	public enum Layout
	{
		/// <summary>
		/// Maps all public fields and properties, except those marked with <see cref="IgnoreAttribute"/>
		/// </summary>
		Default = 0,

		/// <summary>
		/// Maps all fields and properties marked with <see cref="KeyAttribute"/>.
		/// </summary>
		Keyed,

		/// <summary>
		/// (De)serializes all fields and properties marked with <see cref="IndexAttribute"/> to/from an array.<br />
		/// Great to reduce data and increase networking performance.
		/// </summary>
		Indexed
	}

	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
	public class MsgPackSerializableAttribute : Attribute
	{
		public Layout Layout { get; }

		public MsgPackSerializableAttribute(Layout layout) => Layout = layout;
	}
}
