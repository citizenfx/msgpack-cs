using BenchmarkDotNet.Attributes;
using CitizenFX.Core;
using System;
using System.Collections.Generic;

namespace CitizenFX.MsgPack.Benchmarks
{
	public class MsgPackBenchmark
	{
		private const int Iterations = 1_000_000;

		public delegate TResult TypeDeserializer<out TResult>(MsgPackDeserializer arg);

		static TypeDeserializer<Player> deserializePlayer;
		static TypeDeserializer<Vector2> deserializeVector2;
		static TypeDeserializer<Vector3> deserializeVector3;
		static TypeDeserializer<Vector4> deserializeVector4;
		//static Deserialize.TypeDeserializer<Quaternion> deserializeQuaternion;

		public static TypeDeserializer<T> GetDelegate<T>()
		{
			if (MsgPackRegistry.TryGetDeserializer(typeof(T), out var deserializeMethod))
				return (TypeDeserializer<T>)deserializeMethod.CreateDelegate(typeof(TypeDeserializer<T>));

			throw new KeyNotFoundException();
		}

		[GlobalSetup]
		public void Setup()
		{
			MsgPackRegistry.EnsureSerializer(typeof(Dictionary<string, string>));
			MsgPackRegistry.EnsureSerializer(typeof(string[]));

			MsgPackRegistry.EnsureSerializer(typeof(Player));
			MsgPackRegistry.EnsureSerializer(typeof(Vector2));
			MsgPackRegistry.EnsureSerializer(typeof(Vector3));
			MsgPackRegistry.EnsureSerializer(typeof(Vector4));

			deserializePlayer = GetDelegate<Player>();
			deserializeVector2 = GetDelegate<Vector2>();
			deserializeVector3 = GetDelegate<Vector3>();
			deserializeVector4 = GetDelegate<Vector4>();
			//deserializeQuaternion = GetDelegate<Quaternion>();
		}

		[Benchmark(Description = "thorium Serialize(int)", OperationsPerInvoke = 5 * Iterations)]
		public void Custom_SerializeInt()
		{
			for (int i = 0; i < Iterations; i++)
			{
				new MsgPackSerializer().Serialize(1);
				new MsgPackSerializer().Serialize(-1);
				new MsgPackSerializer().Serialize(-2);
				new MsgPackSerializer().Serialize(-128);
				new MsgPackSerializer().Serialize(-129);
			}
		}

		[Benchmark(Description = "MessagePack Serialize<int>(int)", OperationsPerInvoke = 5 * Iterations)]
		public void MessagePack_SerializeInt()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MessagePack.MessagePackSerializer.Serialize(1);
				MessagePack.MessagePackSerializer.Serialize(-1);
				MessagePack.MessagePackSerializer.Serialize(-2);
				MessagePack.MessagePackSerializer.Serialize(-128);
				MessagePack.MessagePackSerializer.Serialize(-129);
			}
		}

		[Benchmark(Description = "thorium Serialize((object)Dictionary<string, string>)", OperationsPerInvoke = Iterations)]
		public void Custom_SerializeDictionaryStringStringAsObject()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MsgPackSerializer serializer = new MsgPackSerializer();

				serializer.Serialize(new Dictionary<string, string>
				{
					{ "hello0", "item0" },
					{ "hello1", "item1" },
					{ "hello2", "item2" },
					{ "hello3", "item3" },
				});
			}
		}

		[Benchmark(Description = "MessagePack Serialize<object>(Dictionary<string, string>)", OperationsPerInvoke = Iterations)]
		public void MessagePack_SerializeDictionaryStringStringAsObject()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MessagePack.MessagePackSerializer.Serialize<object>(new Dictionary<string, string>
				{
					{ "hello0", "item0" },
					{ "hello1", "item1" },
					{ "hello2", "item2" },
					{ "hello3", "item3" },
				});
			}
		}

		[Benchmark(Description = "thorium Serialize((object)Dictionary<int, string>)", OperationsPerInvoke = Iterations)]
		public void Custom_SerializeDictionaryStringIntAsObject()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MsgPackSerializer serializer = new MsgPackSerializer();

				serializer.Serialize(new Dictionary<int, string>
				{
					{ -2, "item0" },
					{ 200_000, "item1" },
					{ 1234, "item2" },
					{ -9000, "item3" },
				});
			}
		}

		[Benchmark(Description = "MessagePack Serialize<object>(Dictionary<int, string>)", OperationsPerInvoke = Iterations)]
		public void MessagePack_SerializeDictionaryStringIntAsObject()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MessagePack.MessagePackSerializer.Serialize<object>(new Dictionary<int, string>
				{
					{ -2, "item0" },
					{ 200_000, "item1" },
					{ 1234, "item2" },
					{ -9000, "item3" },
				});
			}
		}

		[Benchmark(Description = "thorium Serialize((object)string[])", OperationsPerInvoke = Iterations)]
		public void Custom_SerializeArrayStringAsObject()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MsgPackSerializer serializer = new MsgPackSerializer();

				serializer.Serialize(new string[]
				{
					"hello0",
					"hello1",
					"hello2",
					"hello3",
					"hello4",
					"hello5",
				});
			}
		}

		[Benchmark(Description = "MessagePack Serialize<object>(string[])", OperationsPerInvoke = Iterations)]
		public void MessagePack_SerializeArrayStringAsObject()
		{
			for (int i = 0; i < Iterations; i++)
			{
				MessagePack.MessagePackSerializer.Serialize<object>(new string[]
				{
					"hello0",
					"hello1",
					"hello2",
					"hello3",
					"hello4",
					"hello5",
				});
			}
		}

		static readonly byte[] input = new byte[] {
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
			0xcc, 56,
			0xde, 0, 1, 0xd9, 1, (byte)'x', 0xca, 0x3F, 0x80, 0x00, 0x00,
		};

		[Benchmark(Description = "thorium Deserialize Several", OperationsPerInvoke = Iterations)]
		public unsafe void MessagePack_DeserializeSeveral()
		{

			for (int i = 0; i < Iterations; i++)
			{
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
						throw new FormatException();

					// Deserialize
					{
						deserializer.DeserializeAsInt32();
						deserializer.DeserializeAsObject();
						deserializer.DeserializeAsInt32();
						deserializer.DeserializeAsUInt32();
						deserializer.DeserializeAsUInt32();
						deserializer.DeserializeAsUInt64();
						deserializePlayer(deserializer);
						deserializeVector3(deserializer);
						deserializeVector2(deserializer);
						deserializeVector4(deserializer);
						deserializePlayer(deserializer);
						deserializeVector3(deserializer);
						/*Deserialize.CallMethod<Player>(deserializer);
						Deserialize.CallMethod<Vector3>(deserializer);
						Deserialize.CallMethod<Vector2>(deserializer);
						Deserialize.CallMethod<Vector4>(deserializer);
						Deserialize.CallMethod<Player>(deserializer);
						Deserialize.CallMethod<Vector3>(deserializer);*/
					}
				}
			}
		}

		[Benchmark(Description = "MessagePack Deserialize Several", OperationsPerInvoke = Iterations)]
		public unsafe void Custom_DeserializeSeveral()
		{
			for (int i = 0; i < Iterations; i++)
			{
				var reader = new MessagePack.MessagePackReader(input);
				reader.ReadArrayHeader();

				MessagePack.MessagePackSerializer.Deserialize<int>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<int>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<object>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<object>(ref reader); // nil, doesn't allow deserialization to integers
				MessagePack.MessagePackSerializer.Deserialize<bool>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<bool>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<ulong>(ref reader);
				/*MessagePack.MessagePackSerializer.Deserialize<Player>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<Vector3>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<Vector2>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<Vector4>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<Player>(ref reader);
				MessagePack.MessagePackSerializer.Deserialize<Vector3>(ref reader);*/
			}
		}
	}
}
