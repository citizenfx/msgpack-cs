using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CitizenFX.MsgPack.Tests
{
	[TestClass]
	public class Serialize
	{
		private struct SerializeTest<T>
		{
			public T Input { private set; get; }
			public byte[] ExpectedResult { private set; get; }

			public SerializeTest(T input, byte[] expectedResult)
			{
				Input = input;
				ExpectedResult = expectedResult;
			}

			public void AssertValidate(MsgPackSerializer serializer)
			{
				byte[] result = serializer.ToArray();
                Assert.IsTrue(ExpectedResult.SequenceEqual(result), $"{Input} is incorrectly serialized, got: `{BitConverter.ToString(result)}`, expected: `{BitConverter.ToString(ExpectedResult)}`");
			}
        }

		[TestInitialize]
		public void _Dummy()
		{
			MsgPackRegistry.GetOrCreateSerializer(typeof(Dictionary<string, string>));
			MsgPackRegistry.GetOrCreateSerializer(typeof(string[]));

			Assert.IsTrue(MsgPackRegistry.GetOrCreateSerializer(typeof(Dictionary<string, string>)) != null);
			Assert.IsTrue(MsgPackRegistry.GetOrCreateSerializer(typeof(string[])) != null);
		}

		[TestMethod]
		public void SerializeIntegers()
		{
            SerializeTest<int>[] tests =
            {
				new SerializeTest<int>(1, new byte[] { 0x01 }),
				new SerializeTest<int>(-1, new byte[] { 0xFF }),
				new SerializeTest<int>(-2, new byte[] { 0xFE }),
				new SerializeTest<int>(-128, new byte[] { 0xD0, 0x80 }),
				new SerializeTest<int>(-129, new byte[] { 0xD1, 0xFF, 0x7F }),
				
			};

			for (int i = 0; i < tests.Length; ++i)
			{
				var test = tests[i];

				MsgPackSerializer serializer = new MsgPackSerializer();
				serializer.Serialize(test.Input);
				test.AssertValidate(serializer);
			}
		}

		[TestMethod]
		public void SerializeFloat()
		{
			{
				MsgPackSerializer serializer = new MsgPackSerializer();
				var test = new SerializeTest<float>(9999.2312312f, new byte[] { 0xCA, 0x46, 0x1C, 0x3C, 0xED });
				serializer.Serialize(test.Input);
                test.AssertValidate(serializer);
            }

			{
				MsgPackSerializer serializer = new MsgPackSerializer();
                var test = new SerializeTest<double>(123456.455463, new byte[] { 0xCB, 0x40, 0xFE, 0x24, 0x07, 0x49, 0x93, 0x92, 0x19 });
                serializer.Serialize(test.Input);
                test.AssertValidate(serializer);
            }
		}

		[TestMethod]
		public void SerializeDictionaryStringStringAsObject()
		{
			MsgPackSerializer serializer = new MsgPackSerializer();

			serializer.Serialize(new Dictionary<string, string>
			{
				{ "hello0", "item0" },
				{ "hello1", "item1" },
				{ "hello2", "item2" },
				{ "hello3", "item3" },
			});

			byte[] result = serializer.ToArray();
			byte[] expect = new byte[]
			{
				0x84,                                     // map of 4 items
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x30, // key
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x30,       // value
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x31,
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x31,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x32,
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x32,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x33,
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x33
			};

			Assert.IsTrue(result.SequenceEqual(expect), $"Incorrectly serialized, got: `{BitConverter.ToString(result)}`, expected: `{BitConverter.ToString(expect)}`");
		}

		[TestMethod]
		public void SerializeDictionaryStringIntAsObject()
		{
			MsgPackSerializer serializer = new MsgPackSerializer();

			serializer.Serialize(new Dictionary<int, string>
			{
				{ -2, "item0" },
				{ 200_000, "item1" },
				{ 1234, "item2" },
				{ -9000, "item3" },
			});

			byte[] result = serializer.ToArray();
			byte[] expect = new byte[]
			{
				0x84,                                     // map of 4 items
				0xFE,                                     // negative fixint -2
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x30,       // value
				0xCE, 0x00, 0x03, 0x0D, 0x40,             // uint32 200,000
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x31,
				0xCD, 0x04, 0xD2,                         // uint16 1234
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x32,
				0xD1, 0xDC, 0xD8,                         // int16 -9000
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x33
			};

			Assert.IsTrue(result.SequenceEqual(expect), $"Incorrectly serialized, got: `{BitConverter.ToString(result)}`, expected: `{BitConverter.ToString(expect)}`");
		}


		[TestMethod]
		public void SerializeArrayStringAsObject()
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

			byte[] result = serializer.ToArray();
			byte[] expect = new byte[]
			{
				0x96,                                     // array of 6 items
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x30, // values
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x31,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x32,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x33,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x34,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x35
			};

			Assert.IsTrue(result.SequenceEqual(expect), $"Incorrectly serialized, got: `{BitConverter.ToString(result)}`, expected: `{BitConverter.ToString(expect)}`");
		}


		[TestMethod]
		public void SerializeObjectArray()
		{
			MsgPackSerializer serializer = new MsgPackSerializer();

			object[] arguments = new object[]
			{
				new string[]
				{
					"hello0",
					"hello1",
					"hello2",
					"hello3",
					"hello4",
					"hello5",
				},
				new Dictionary<int, string>
				{
					{ -2, "item0" },
					{ 200_000, "item1" },
					{ 1234, "item2" },
					{ -9000, "item3" },
				},
				true,
				false,
				-1,
				1,
				99999,
				123456778,
				null,
				12345.0,
				87391973912.23342f
			};

			serializer.Serialize(arguments);

			byte[] result = serializer.ToArray();
			byte[] expect = new byte[]
			{
				0x9B,                                     // array of 11 items
				
				0x96,                                     // array of 6 items
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x30, // string[] values
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x31,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x32,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x33,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x34,
				0xA6, 0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x35,

				0x84,                                     // map of 4 items
				0xFE,                                     // negative fixint -2
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x30,       // value
				0xCE, 0x00, 0x03, 0x0D, 0x40,             // uint32 200,000
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x31,
				0xCD, 0x04, 0xD2,                         // uint16 1234
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x32,
				0xD1, 0xDC, 0xD8,                         // int16 -9000
				0xA5, 0x69, 0x74, 0x65, 0x6D, 0x33,

				0xC3,                                     // true
				0xC2,                                     // false
				0xFF,                                     // -1
				0x01,                                     // 1
				0xCE, 0x00, 0x01, 0x86, 0x9F,             // 99999
				0xCE, 0x07, 0x5B, 0xCD, 0x0A,             // 123456778
				0xC0,                                     // null

				0xCB, 0x40, 0xC8, 0x1C, 0x80, 0x00, 0x00, 0x00, 0x00, // 12345.0
				0xCA, 0x51, 0xA2, 0xC7, 0xBE              // 87391973912.23342f
			};

			Assert.IsTrue(result.SequenceEqual(expect), $"Incorrectly serialized, got: `{BitConverter.ToString(result)}`, expected: `{BitConverter.ToString(expect)}`");
		}
	}
}
