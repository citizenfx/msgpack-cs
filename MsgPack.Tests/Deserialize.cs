using Microsoft.VisualStudio.TestTools.UnitTesting;
using CitizenFX.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CitizenFX.MsgPack.Tests
{
	public delegate TResult TypeDeserializer<out TResult>(ref MsgPackDeserializer arg);

	public static class DeserializeExtensions
	{
		public static T Deserialize<T>(this ref MsgPackDeserializer deserializer)
		{
			var deleg = MsgPackRegistry.GetOrCreateDeserializer(typeof(T)).CreateDelegate(typeof(TypeDeserializer<T>));
			return ((TypeDeserializer<T>)deleg)(ref deserializer);
		}

		public static void ValidateException<T, E>(this ref MsgPackDeserializer deserializer)
		{
			var restorePoint = deserializer.CreateRestorePoint();

			try
			{
				T value = deserializer.Deserialize<T>();
				Assert.Fail($"Deserialize did not result in an exception, got {value} instead.");
			}
			catch (Exception ex)
			{
				if (!(ex is E))
					Assert.Fail($"Deserialize threw exception {ex.GetType()} but wasn't the expected {typeof(E)}.");

				deserializer.Restore(restorePoint);
				deserializer.SkipObject();
			}
		}

		public static uint ValidateArrayHeader(this ref MsgPackDeserializer deserializer)
		{
			byte type = deserializer.ReadByte();

			if (type >= 0x90 && type < 0xA0)
				return (uint)(type % 16);
			else if (type == 0xDC)
				return deserializer.ReadUInt16();
			else if (type == 0xDD)
				return deserializer.ReadUInt32();

			Assert.Fail();
			throw new AssertFailedException(); // Assert.Fail throws this as well
		}

		public static void Validate<T>(this ref MsgPackDeserializer deserializer, T expected)
		{
			Assert.AreEqual(expected, deserializer.Deserialize<T>());
		}

		public static void Validate<T>(this ref MsgPackDeserializer deserializer, T[] expected)
		{
			var result = deserializer.Deserialize<T[]>();
			Assert.IsTrue(result == expected || result.SequenceEqual(expected));
		}

		public static void Validate<T>(this ref MsgPackDeserializer deserializer, List<T> expected)
		{
			var result = deserializer.Deserialize<List<T>>();
			Assert.IsTrue(result == expected || result.SequenceEqual(expected));
		}

		public static void Validate<K, V>(this ref MsgPackDeserializer deserializer, Dictionary<K, V> expected)
		{
			var result = deserializer.Deserialize<Dictionary<K, V>>();
			if (result == expected)
				return;

			if (expected?.Count != result?.Count)
				Assert.Fail("Counts aren't the same");

			foreach (var p in expected)
			{
				if (result.TryGetValue(p.Key, out var v))
				{
					if (!p.Value.Equals(v))
						Assert.Fail($"expected[{p.Key}]'s value '{p.Value}' does not equal '{v}'.");
				}
				else
					Assert.Fail($"Key {p.Key} does not exist in deserialized dictionary.");
			}
		}
	}

	[TestClass]
	public class Deserialize
	{
		static Deserialize()
		{
			MsgPackRegistry.GetOrCreateDeserializer(typeof(uint[]));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(List<uint>));
			MsgPackRegistry.GetOrCreateDeserializer(typeof(Dictionary<string, uint>));
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
				0xcc, 56,
				0xde, 0, 1, 0xd9, 1, (byte)'x', 0xca, 0x3F, 0x80, 0x00, 0x00,
			};

			fixed (byte* ptr = input)
			{
				var deserializer = new MsgPackDeserializer(ptr, (ulong)input.Length, "");
				deserializer.ValidateArrayHeader();

				deserializer.Validate(7);
				Assert.AreEqual(254L, ((IConvertible)deserializer.DeserializeAsObject()).ToInt64(null));
				deserializer.Validate(16_909_060);
				deserializer.Validate(0u);
				deserializer.Validate(0u);
				deserializer.Validate(1uL);
				deserializer.Validate(new Player(254u));
				deserializer.Validate(new Vector3(1.0f, 2.0f, 3.0f));
				deserializer.Validate(new Vector2(1.0f, 2.0f));
				deserializer.Validate(new Vector4(1.0f, 2.0f, 3.0f, 4.0f));
				deserializer.Validate(new Player(56u));
				deserializer.Validate(new Vector3(1.0f, 0.0f, 0.0f));
			}
		}

		[TestMethod]
		public unsafe void DeserializePlayer()
		{
			byte[] input = new byte[] {
				0x97, // fixarray, size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
				0xcc, 0xFE,
				0xcc, 0xFE,
				0xcd, 0x00, 0xFE,
				0xce, 0x00, 0x00, 0x00, 0xFE,
				0xcf, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFE,
				0xcc, 56,
			};

			fixed (byte* ptr = input)
			{
				var deserializer = new MsgPackDeserializer(ptr, (ulong)input.Length, "");
				deserializer.ValidateArrayHeader();

				deserializer.Validate(new Player(254u));
				deserializer.Validate(new Player(254u));
				deserializer.Validate(new Player(254u));
				deserializer.Validate(new Player(254u));
				deserializer.Validate(new Player(254u));
				deserializer.Validate(new Player(56u));
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
				deserializer.ValidateArrayHeader();

				deserializer.Validate(new Vector3(1.0f, 2.0f, 3.0f));
				deserializer.Validate(new Vector3(1.0f, 2.0f, 3.0f));
				deserializer.Validate(new Vector3(1.0f, 2.0f, 3.0f));
				deserializer.Validate(new Vector3(1.0f, 0.0f, 0.0f));
				deserializer.Validate(new Vector3(0.0f, 1.0f, 0.0f));
				deserializer.Validate(new Vector3(0.0f, 0.0f, 1.0f));
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
				deserializer.ValidateArrayHeader();

				deserializer.Validate(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f));
				deserializer.Validate(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f));
				deserializer.Validate(new Quaternion(1.0f, 2.0f, 3.0f, 4.0f));
				deserializer.Validate(new Quaternion(1.0f, 0.0f, 0.0f, 0.0f));
				deserializer.Validate(new Quaternion(0.0f, 1.0f, 0.0f, 0.0f));
				deserializer.Validate(new Quaternion(0.0f, 0.0f, 1.0f, 0.0f));
				deserializer.Validate(new Quaternion(0.0f, 0.0f, 0.0f, 1.0f));
				deserializer.Validate(new Quaternion(1.0f, 0.0f, 0.0f, 1.0f));
				deserializer.ValidateException<Quaternion, InvalidCastException>();
			}
		}

		[TestMethod]
		public unsafe void DeserializeUIntArray()
		{
			byte[] input = new byte[] {
				0x97, // fixarray, size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
				0x94, 0xcc, 0xFE, 0xcc, 0xFD, 0xcc, 0xFC, 0xcc, 0x3,
				0x91, 0xcd, 0x00, 0xFE,
				0x91, 0xce, 0x00, 0x00, 0x00, 0xFE,
				0x91, 0xcf, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFE,
				0x92, 0xcc, 0xFE, 0xcc, 0x01,
				0xcc, 0xFE,
				0x92, 0xc0, 0xc0,
				0xc0,
				0x92, 0xcc, 0x3E, 0xcc, 0x68,
			};

			fixed (byte* ptr = input)
			{
				var deserializer = new MsgPackDeserializer(ptr, (ulong)input.Length, "");
				deserializer.ValidateArrayHeader();

				deserializer.Validate(new uint[] { 254, 253, 252, 3 });
				deserializer.Validate(new uint[] { 254 });
				deserializer.Validate(new uint[] { 254 });
				deserializer.Validate(new uint[] { 254 });
				deserializer.Validate(new uint[] { 254, 1 });
				deserializer.ValidateException<uint[], InvalidCastException>();
				deserializer.Validate(new uint[] { 0, 0 });
				deserializer.Validate(null as uint[]);
				deserializer.Validate(new List<uint> { 62, 104 });
			}
		}

		[TestMethod]
		public unsafe void DeserializeDictionaryStringUInt32()
		{
			byte[] input = new byte[] {
				0x97, // fixarray, size 7 (this calling code must abide by this number, but we don't need to for these testing purposes)
				0x84, 0xa1, (byte)'1', 0xcc, 0xFE, 0xa1, (byte)'2', 0xcc, 0xFD, 0xa1, (byte)'3', 0xcc, 0xFC, 0xa1, (byte)'4', 0xcc, 0x3,
				0xcc, 0xFE,
				0x82, 0xa2, (byte)'h', (byte)'i', 0xcc, 0x68, 0xa3, (byte)'b', (byte)'y', (byte)'e', 0xcc, 0x3E,
				0xc0,
			};

			fixed (byte* ptr = input)
			{
				var deserializer = new MsgPackDeserializer(ptr, (ulong)input.Length, "");
				deserializer.ValidateArrayHeader();

				deserializer.Validate(new Dictionary<string, uint> { { "1", 254 }, { "2", 253 }, { "3", 252 }, { "4", 3 } });
				deserializer.ValidateException<uint[], InvalidCastException>();
				deserializer.Validate(new Dictionary<string, uint> { { "hi", 104 }, { "bye", 62 } });
				deserializer.Validate(null as Dictionary<string, uint>);
			}
		}
	}
}
