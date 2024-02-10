using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MsgPack.Tests
{
	public class Player
	{
		public uint m_id = 0;

		public Player() => m_id = 0;
		public Player(int id) => m_id = (uint)id;
		public Player(uint id) => m_id = id;
		public Player(string id) => m_id = uint.Parse(id);
	}
}
