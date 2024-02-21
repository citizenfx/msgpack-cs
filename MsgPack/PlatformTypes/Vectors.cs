namespace MsgPack
{
    public struct Vector2
    {
        public float x, y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
		}

		public override string ToString() => $"{nameof(Vector2)}({x}, {y})";
		public static bool operator ==(Vector2 l, Vector2 r) => l.x == r.x && l.y == r.y;
		public static bool operator !=(Vector2 l, Vector2 r) => !(l == r);
		public override bool Equals(object o) => o is Vector2 v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
	}

    [MsgPackSerializable(Layout.Indexed)]
    public struct Vector3
    {
        [Index(0)] public float x;
		[Index(1)] public float y;
		[Index(2)] public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString() => $"{nameof(Vector3)}({x}, {y}, {z})";
        public static bool operator ==(Vector3 l, Vector3 r) => l.x == r.x && l.y == r.y && l.z == r.z;
        public static bool operator !=(Vector3 l, Vector3 r) => !(l == r);
        public override bool Equals(object o) => o is Vector3 v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
	}

    public struct Vector4
    {
        public float x, y, z, w;

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
		}

		public override string ToString() => $"{nameof(Vector4)}({x}, {y}, {z}, {w})";
		public static bool operator ==(Vector4 l, Vector4 r) => l.x == r.x && l.y == r.y && l.z == r.z && l.w == r.w;
		public static bool operator !=(Vector4 l, Vector4 r) => !(l == r);
		public override bool Equals(object o) => o is Vector4 v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode() ^ w.GetHashCode();
	}

	[MsgPackSerializable(Layout.Indexed)]
	public struct Quaternion
	{
		[Index(0)] public float x;
		[Index(1)] public float y;
		[Index(2)] public float z;
		[Index(3)] public float w;

		public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
		}

		public override string ToString() => $"{nameof(Quaternion)}({x}, {y}, {z}, {w})";
		public static bool operator ==(Quaternion l, Quaternion r) => l.x == r.x && l.y == r.y && l.z == r.z && l.w == r.w;
		public static bool operator !=(Quaternion l, Quaternion r) => !(l == r);
		public override bool Equals(object o) => o is Quaternion v && this == v;
		public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode() ^ w.GetHashCode();
	}
}
