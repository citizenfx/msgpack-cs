using System.Reflection;

namespace CitizenFX.MsgPack.Detail
{
	internal class Helper
	{
		public delegate void MethodVoid(ref MsgPackDeserializer deserializer);
		public delegate void MethodVoid<A>(ref MsgPackDeserializer deserializer, A a);
		public delegate R MethodResult<out R>(ref MsgPackDeserializer deserializer);
		public delegate R MethodResult<A, out R>(ref MsgPackDeserializer deserializer, A a);
		public delegate MsgPackDeserializer.RestorePoint MethodRestoreGet(ref MsgPackDeserializer deserializer);
		public delegate void MethodRestoreSet(ref MsgPackDeserializer deserializer, MsgPackDeserializer.RestorePoint restorePoint);

		public delegate void SerializerMethodVoid(MsgPackSerializer deserializer);
		public delegate void SerializerMethodVoid<A>(MsgPackSerializer deserializer, A a);

		public static MethodInfo GetVoidMethod(MethodVoid method) => method.Method;
		public static MethodInfo GetVoidMethod<A>(MethodVoid<A> method) => method.Method;
		public static MethodInfo GetResultMethod<R>(MethodResult<R> method) => method.Method;
		public static MethodInfo GetResultMethod<A, R>(MethodResult<A, R> method) => method.Method;

		public static MethodInfo GetResultMethod(MethodRestoreGet method) => method.Method;
		public static MethodInfo GetVoidMethod(MethodRestoreSet method) => method.Method;

		public static MethodInfo GetVoidMethod(SerializerMethodVoid method) => method.Method;
		public static MethodInfo GetVoidMethod<A>(SerializerMethodVoid<A> method) => method.Method;
	}
}
