using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace MsgPack.Tests
{
	[TestClass]
	public class Deserialize
	{
		static Deserialize()
		{
			MsgPackRegistry.GetOrCreateDeserializerMethod(typeof(Player));
		}

		[TestMethod]
		public unsafe void DeserializeTest()
		{
			byte[] input = new byte[] {
				0x97, // fixarray
				0x7,
				0xcc, 0xFE,
				0xd2, 0x01, 0x02, 0x03, 0x04,
				0xc0,
				0xc2,
				0xc3,
				0xcc, 0xFE,
			};

			fixed (byte* ptr = input)
			{
				var deserializer = new MsgPackDeserializer(ptr, (ulong)input.Length, "");
				byte type = deserializer.ReadByte();
				uint length;

				if (type >= 0x90 && type < 0xA0)
					length = (uint)(type % 16);
				else if (type == 0xDC)
					length = deserializer.ReadUInt16();
				else if (type == 0xDD)
					length = deserializer.ReadUInt32();
				else
					Assert.Fail();


				void MsgPackConversionInvoke(short a, object b, int c, byte d, uint e, ulong f, Player player)
				{
					Assert.AreEqual((short)7, a);
					Assert.AreEqual(254L, ((IConvertible)b).ToInt64(null));
					Assert.AreEqual(16_909_060, c);
					Assert.AreEqual((byte)0, d);
					Assert.AreEqual(0u, e);
					Assert.AreEqual(1uL, f);
					Assert.AreEqual(player?.m_id, 254u);
				}

				if (MsgPackRegistry.TryGetDeserializer(typeof(Player), out var methods))
				{
					var a = (short)deserializer.DeserializeToInt32();
					var b = deserializer.Deserialize();
					var c = deserializer.DeserializeToInt32();
					var d = (byte)deserializer.DeserializeToUInt32();
					var e = deserializer.DeserializeToUInt32();
					var f = deserializer.DeserializeToUInt64();
					var g = methods.m_dynamic(ref deserializer);
					var player = g as Player;

					MsgPackConversionInvoke(a, b, c, d, e, f, player);
				}
				else
					Assert.Fail();
			}
		}
	}
}
