using CitizenFX.MsgPack;
using MessagePack;

namespace CitizenFX.Core
{
	[MessagePackObject]
	[MsgPackSerializable(Layout.Indexed)]
	public class Player
	{
		[MessagePack.Key(0)] public uint m_id = 0;

		public Player() => m_id = 0;
		//public Player(object[] array) => m_id = (uint)array[0];
		public Player(int id) => m_id = (uint)id;
		public Player(uint id) => m_id = id;
		public Player(string id) => m_id = uint.Parse(id);

		public override string ToString() => $"{nameof(Player)}({m_id})";
		public static bool operator ==(Player l, Player r) => l.m_id == r.m_id;
		public static bool operator !=(Player l, Player r) => !(l == r);
		public override bool Equals(object o) => o is Player v && this == v;
		public override int GetHashCode() => (int)m_id;
	}
}
