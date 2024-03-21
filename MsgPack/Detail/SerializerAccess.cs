using static CitizenFX.MsgPack.MsgPackDeserializer;

namespace CitizenFX.MsgPack.Detail
{

	[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
	public static class SerializerAccess
	{
		public static uint ReadArraySize(ref MsgPackDeserializer deserializer) => deserializer.ReadArraySize();
		public static uint ReadArraySize(ref MsgPackDeserializer deserializer, byte type) => deserializer.ReadArraySize(type);
		public static uint ReadMapSize(ref MsgPackDeserializer deserializer, byte type) => deserializer.ReadMapSize(type);

		public static byte ReadByte(ref MsgPackDeserializer deserializer) => deserializer.ReadByte();
		public static float ReadSingle(ref MsgPackDeserializer deserializer) => deserializer.ReadSingle();
		public static double ReadDouble(ref MsgPackDeserializer deserializer) => deserializer.ReadDouble();
		public static byte ReadUInt8(ref MsgPackDeserializer deserializer) => deserializer.ReadByte();
		public static ushort ReadUInt16(ref MsgPackDeserializer deserializer) => deserializer.ReadUInt16();
		public static uint ReadUInt32(ref MsgPackDeserializer deserializer) => deserializer.ReadUInt32();
		public static ulong ReadUInt64(ref MsgPackDeserializer deserializer) => deserializer.ReadUInt64();
		public static sbyte ReadInt8(ref MsgPackDeserializer deserializer) => deserializer.ReadInt8();
		public static short ReadInt16(ref MsgPackDeserializer deserializer) => deserializer.ReadInt16();
		public static int ReadInt32(ref MsgPackDeserializer deserializer) => deserializer.ReadInt32();
		public static long ReadInt64(ref MsgPackDeserializer deserializer) => deserializer.ReadInt64();

		public static string ReadString(ref MsgPackDeserializer deserializer, uint length) => deserializer.ReadString(length);
		public static void SkipString(ref MsgPackDeserializer deserializer, uint length) => deserializer.SkipString(length);
		public static Core.CString ReadCString(ref MsgPackDeserializer deserializer, uint length) => deserializer.ReadCString(length);
		public static float ReadSingleLE(ref MsgPackDeserializer deserializer) => deserializer.ReadSingleLE();

		public static object[] ReadObjectArray(ref MsgPackDeserializer deserializer, uint length) => deserializer.ReadObjectArray(length);

		public static void SkipObject(ref MsgPackDeserializer deserializer) => deserializer.SkipObject();
		public static void SkipObjects(ref MsgPackDeserializer deserializer, uint size) => deserializer.SkipObjects(size);

		public static void SkipVector2(ref MsgPackDeserializer deserializer) => deserializer.SkipVector2();
		public static void SkipVector3(ref MsgPackDeserializer deserializer) => deserializer.SkipVector3();
		public static Core.Vector3 ReadVector3(ref MsgPackDeserializer deserializer) => deserializer.ReadVector3();
		public static void SkipVector4(ref MsgPackDeserializer deserializer) => deserializer.SkipVector4();
		public static void SkipQuaternion(ref MsgPackDeserializer deserializer) => deserializer.SkipQuaternion();

		public static RestorePoint CreateRestorePoint(ref MsgPackDeserializer deserializer) => deserializer.CreateRestorePoint();
		public static void Restore(ref MsgPackDeserializer deserializer, RestorePoint restorePoint) => deserializer.Restore(restorePoint);

		public static void WriteMapHeader(MsgPackSerializer serializer, uint size) => serializer.WriteMapHeader(size);
		public static void WriteArrayHeader(MsgPackSerializer serializer, uint size) => serializer.WriteArrayHeader(size);
		public static void WriteNil(MsgPackSerializer serializer) => serializer.WriteNil();
	}
}
