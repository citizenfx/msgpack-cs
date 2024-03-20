
namespace CitizenFX.Core
{
	public struct Remote
	{
		internal string m_playerId;

		internal Remote(string playerId)
		{
			if (!string.IsNullOrEmpty(playerId))
			{
				if (playerId.StartsWith("net:"))
				{
					playerId = playerId.Substring(4);
				}
				else if (playerId.StartsWith("internal-net:"))
				{
					playerId = playerId.Substring(13);
				}

				m_playerId = playerId;
			}
			else
				m_playerId = null;
		}

		internal static Remote Create(ushort remote) => new Remote(remote.ToString());

		internal static bool IsRemoteInternal(Remote remote) => remote.m_playerId != null;

		internal string GetPlayerHandle() => m_playerId;

		public override string ToString() => $"Remote({m_playerId})";
	}
}
