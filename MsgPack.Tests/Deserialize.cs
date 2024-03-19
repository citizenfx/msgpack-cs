using Microsoft.VisualStudio.TestTools.UnitTesting;
using CitizenFX.Core;
using System;

namespace CitizenFX.MsgPack.Tests
{
	[TestClass]
	public class Deserialize
	{
		public delegate TResult TypeDeserializer<out TResult>(in MsgPackDeserializer arg);

		public static T CallMethod<T>(in MsgPackDeserializer deserializer)
		{
			if (MsgPackRegistry.TryGetDeserializer(typeof(T), out var deserializeMethod))
				return ((TypeDeserializer<T>)deserializeMethod.CreateDelegate(typeof(TypeDeserializer<T>)))(deserializer);
			
			Assert.Fail();
			return default;
		}

		public static Exception ReturnException<T>(in MsgPackDeserializer deserializer)
		{
			try
			{
				CallMethod<T>(deserializer);
			}
			catch (Exception ex)
			{
				return ex;
			}

			return null;
		}

		static Deserialize()
		{
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Player));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Vector2));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Vector3));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Vector4));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Quaternion));
		}

		[TestMethod]
		public unsafe void DeserializeMultiple()
		{
			byte[] input = new byte[] {
				0x97, // fixarray size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
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
				0xde, 0, 1, 0xd9, 4, (byte)'m', (byte)'_', (byte)'i', (byte)'d', 0xcc, 56,
				0xde, 0, 1, 0xd9, 1, (byte)'x', 0xca, 0x3F, 0x80, 0x00, 0x00,
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
					Assert.AreEqual(7, deserializer.DeserializeAsInt32());
					Assert.AreEqual(254L, ((IConvertible)deserializer.DeserializeAsObject()).ToInt64(null));
					Assert.AreEqual(16_909_060, deserializer.DeserializeAsInt32());
					Assert.AreEqual(0u, deserializer.DeserializeAsUInt32());
					Assert.AreEqual(0u, deserializer.DeserializeAsUInt32());
					Assert.AreEqual(1uL, deserializer.DeserializeAsUInt64());
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), CallMethod<Vector3>(deserializer));
					Assert.AreEqual(new Vector2(1.0f, 2.0f), CallMethod<Vector2>(deserializer));
					Assert.AreEqual(new Vector4(1.0f, 2.0f, 3.0f, 4.0f), CallMethod<Vector4>(deserializer));
					Assert.AreEqual(56u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(new Vector3(1.0f, 0.0f, 0.0f), CallMethod<Vector3>(deserializer));
				}
			}
		}

		[TestMethod]
		public unsafe void DeserializePlayer()
		{
			byte[] input = new byte[] {
				0x97, // fixarray, size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
				0xcc, 0xFE,
				0x91, 0xcc, 0xFE,
				0x91, 0xcd, 0x00, 0xFE,
				0x91, 0xce, 0x00, 0x00, 0x00, 0xFE,
				0x91, 0xcf, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFE,
				0x92, 0xcc, 0xFE, 0xcc, 0x01, // ignore other values in the array
				0xde, 0, 1, 0xd9, 4, (byte)'m', (byte)'_', (byte)'i', (byte)'d', 0xcc, 56,
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
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);
					Assert.AreEqual(254u, CallMethod<Player>(deserializer)?.m_id);

					Assert.AreEqual(56u, CallMethod<Player>(deserializer)?.m_id);
				}
			}
		}

		[TestMethod]
		public unsafe void DeserializeVector3()
		{
			byte[] input = new byte[] {
				0x97, // fixarray, size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
				0xc7, 12, 21, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x40, 0x40,
				0x93, 0xca, 0x3F, 0x80, 0x00, 0x00, 0xca, 0x40, 0x00, 0x00, 0x00, 0xca, 0x40, 0x40, 0x00, 0x00,
				0x94, 0xcc, 1, 0xcc, 2, 0xcc, 3, 0xcc, 4,
				0xde, 0, 1, 0xd9, 1, (byte)'x', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xde, 0, 1, 0xd9, 1, (byte)'y', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xde, 0, 1, 0xd9, 1, (byte)'z', 0xca, 0x3F, 0x80, 0x00, 0x00,
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
					Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), CallMethod<Vector3>(deserializer));
					Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), CallMethod<Vector3>(deserializer));
					Assert.AreEqual(new Vector3(1.0f, 2.0f, 3.0f), CallMethod<Vector3>(deserializer));
					Assert.AreEqual(new Vector3(1.0f, 0.0f, 0.0f), CallMethod<Vector3>(deserializer));
					Assert.AreEqual(new Vector3(0.0f, 1.0f, 0.0f), CallMethod<Vector3>(deserializer));
					Assert.AreEqual(new Vector3(0.0f, 0.0f, 1.0f), CallMethod<Vector3>(deserializer));
				}
			}
		}

		[TestMethod]
		public unsafe void DeserializeQuaternion()
		{
			byte[] input = new byte[] {
				0x97, // fixarray, size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
				0xc7, 16, 22, 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x40, 0x40, 0x00, 0x00, 0x80, 0x40,
				0x94, 0xca, 0x3F, 0x80, 0x00, 0x00, 0xca, 0x40, 0x00, 0x00, 0x00, 0xca, 0x40, 0x40, 0x00, 0x00, 0xca, 0x40, 0x80, 0x00, 0x00,
				0x94, 0xcc, 1, 0xcc, 2, 0xcc, 3, 0xcc, 4,
				0xde, 0, 1, 0xd9, 1, (byte)'x', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xde, 0, 1, 0xd9, 1, (byte)'y', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xde, 0, 1, 0xd9, 1, (byte)'z', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xde, 0, 1, 0xd9, 1, (byte)'w', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xde, 0, 2, 0xd9, 1, (byte)'w', 0xca, 0x3F, 0x80, 0x00, 0x00, 0xd9, 1, (byte)'x', 0xca, 0x3F, 0x80, 0x00, 0x00,
				0xd9, 2, (byte)'n', (byte) 'o'
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
					Assert.AreEqual(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(1.0f, 0.0f, 0.0f, 0.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(0.0f, 1.0f, 0.0f, 0.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(0.0f, 0.0f, 1.0f, 0.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(0.0f, 0.0f, 0.0f, 1.0f), CallMethod<Quaternion>(deserializer));
					Assert.AreEqual(new Quaternion(1.0f, 0.0f, 0.0f, 1.0f), CallMethod<Quaternion>(deserializer));
					Assert.IsNotNull(ReturnException<Quaternion>(deserializer) is InvalidCastException);
				}
			}
		}
	}
}
