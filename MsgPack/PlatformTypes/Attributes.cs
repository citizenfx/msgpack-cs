using System;

namespace CitizenFX.Core
{
	/// <summary>
	/// When used in events it'll be filled with the caller (source) of this event
	/// </summary>
	/// <example>
	///		Shared libraries
	///		<code>[Source] Remote remote</code>
	///	</example>
	/// <example>
	///		Server libraries
	///		<code>[Source] Player player</code>
	/// </example>
	/// <example>
	///		Shared libraries
	///		<code>[Source] bool isRemote</code>
	/// </example>
	[AttributeUsage(AttributeTargets.Parameter)]
	public class SourceAttribute : Attribute
	{ }
}
