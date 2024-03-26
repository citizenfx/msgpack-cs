using CitizenFX.MsgPack;

namespace CitizenFX.Core
{
	public struct Vector2
	{
		public float X, Y;

		public Vector2(float x, float y)
		{
			this.X = x;
			this.Y = y;
		}

		public override string ToString() => $"{nameof(Vector2)}({X}, {Y})";
		public static bool operator ==(Vector2 l, Vector2 r) => l.X == r.X && l.Y == r.Y;
		public static bool operator !=(Vector2 l, Vector2 r) => !(l == r);
		public override bool Equals(object o) => o is Vector2 v && this == v;
		public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();
	}

	[MsgPackSerializable(Layout.Indexed)]
	public struct Vector3
	{
		[Index(0)] public float X;
		[Index(1)] public float Y;
		[Index(2)] public float Z;

		public Vector3(float x, float y, float z)
		{
			this.X = x;
			this.Y = y;
			this.Z = z;
		}

		public override string ToString() => $"{nameof(Vector3)}({X}, {Y}, {Z})";
		public static bool operator ==(Vector3 l, Vector3 r) => l.X == r.X && l.Y == r.Y && l.Z == r.Z;
		public static bool operator !=(Vector3 l, Vector3 r) => !(l == r);
		public override bool Equals(object o) => o is Vector3 v && this == v;
		public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
	}

	public struct Vector4
	{
		public float X, Y, Z, W;

		public Vector4(float x, float y, float z, float w)
		{
			this.X = x;
			this.Y = y;
			this.Z = z;
			this.W = w;
		}

		public override string ToString() => $"{nameof(Vector4)}({X}, {Y}, {Z}, {W})";
		public static bool operator ==(Vector4 l, Vector4 r) => l.X == r.X && l.Y == r.Y && l.Z == r.Z && l.W == r.W;
		public static bool operator !=(Vector4 l, Vector4 r) => !(l == r);
		public override bool Equals(object o) => o is Vector4 v && this == v;
		public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
	}

	[MsgPackSerializable(Layout.Indexed)]
	public struct Quaternion
	{
		[Index(0)] public float X;
		[Index(1)] public float Y;
		[Index(2)] public float Z;
		[Index(3)] public float W;

		public Quaternion(float x, float y, float z, float w)
		{
			this.X = x;
			this.Y = y;
			this.Z = z;
			this.W = w;
		}

		public override string ToString() => $"{nameof(Quaternion)}({X}, {Y}, {Z}, {W})";
		public static bool operator ==(Quaternion l, Quaternion r) => l.X == r.X && l.Y == r.Y && l.Z == r.Z && l.W == r.W;
		public static bool operator !=(Quaternion l, Quaternion r) => !(l == r);
		public override bool Equals(object o) => o is Quaternion v && this == v;
		public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
	}
}
