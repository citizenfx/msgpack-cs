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
	}
}
