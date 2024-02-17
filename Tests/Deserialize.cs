using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace MsgPack.Tests
{
	[TestClass]
	public class Deserialize
	{
		private delegate TResult TypeDeserializer<out TResult>(in MsgPackDeserializer arg);

		private T CallMethod<T>(in MsgPackDeserializer deserializer)
		{
			if (MsgPackRegistry.TryGetDeserializer(typeof(T), out var deserializeMethod))
				return ((TypeDeserializer<T>)deserializeMethod.CreateDelegate(typeof(TypeDeserializer<T>)))(deserializer);
			
			Assert.Fail();
			return default;
		}

		static Deserialize()
		{
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Player));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Vector2));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Vector3));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Vector4));
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
				0xc7, 12, 21, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x40, 0x40,
				0xc7, 8, 20, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40,
				0xc7, 16, 22, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x40, 0x40, 0x00, 0x00, 0x80, 0x40,
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

				// Deserialize
				{
					Assert.AreEqual((short)7,
						(short)deserializer.DeserializeToInt32());

					Assert.AreEqual(254L,
						((IConvertible)deserializer.Deserialize()).ToInt64(null));

					Assert.AreEqual(16_909_060,
						deserializer.DeserializeToInt32());

					Assert.AreEqual((byte)0,
						(byte)deserializer.DeserializeToUInt32());

					Assert.AreEqual(0u,
						deserializer.DeserializeToUInt32());

					Assert.AreEqual(1uL,						
						deserializer.DeserializeToUInt64());

					Assert.AreEqual(254u,
						CallMethod<Player>(deserializer)?.m_id);

					Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f),
						CallMethod<Vector3>(deserializer));

					Assert.AreEqual(new Vector2(1.0f, 2.0f),
						CallMethod<Vector2>(deserializer));

					Assert.AreEqual(new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
						CallMethod<Vector4>(deserializer));
				}
			}
		}
	}
}
