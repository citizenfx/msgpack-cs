using BenchmarkDotNet.Attributes;
using CitizenFX.Core;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CitizenFX.MsgPack.Benchmarks
{
	public class SortingBenchmark
	{
#pragma warning disable CS0649
		private class TypeWithManyMembers
		{
			public int a, b, c, d, e;
			public float f;
			public Core.CString g { get; set; }
			public float h;
			public int i, j;
			public double k { set; get; }
			public CString l;
			public string m { set; get; }
			public int n;
			public string o { set; get; }
			public string p { set; get; }
			public uint q { set; get; }
		}
#pragma warning restore CS0649

		public class LambdaComparer<T> : IComparer<T>
		{
			private readonly Func<T, T, int> comparer;
			public LambdaComparer(Func<T, T, int> comparer) => this.comparer = comparer;
			public int Compare(T l, T r) => comparer(l, r);
		}

		private static MemberInfo[] s_members;
		private static MemberInfo[] s_membersSameSize;

		public static void TestSorting()
		{
			Console.WriteLine("hi");

			s_members = typeof(TypeWithManyMembers).GetMembers(BindingFlags.Instance | BindingFlags.Public);

			var array1 = new MemberInfo[s_members.Length];
			Array.Copy(s_members, array1, s_members.Length);
			var arr1 = new Detail.DynamicArray<MemberInfo>(array1);
			arr1.Sort((l, r) => l.MetadataToken - r.MetadataToken);

			var arr2 = new MemberInfo[s_members.Length];
			Array.Copy(s_members, arr2, s_members.Length);
			Array.Sort(arr2, 0, arr2.Length, new LambdaComparer<MemberInfo>((l, r) => l.MetadataToken - r.MetadataToken));

			Console.WriteLine($"{"Same",-10} {"QuickSort",-30} Arry.Sort");
			for (int i = 0; i < arr1.Count; ++i)
				Console.WriteLine($"{arr1[i] == arr2[i], -10} {arr1[i],-30} {arr2[i]}");

		}

		[GlobalSetup]
		public void Setup()
		{
			s_members = typeof(TypeWithManyMembers).GetMembers(BindingFlags.Instance | BindingFlags.Public);
			s_membersSameSize = new MemberInfo[s_members.Length];
		}

		[Benchmark(Description = "QuickSort")]
		public void QuickSort()
		{
			Array.Copy(s_members, s_membersSameSize, s_members.Length);
			var arr = new Detail.DynamicArray<MemberInfo>(s_membersSameSize);
			arr.Sort((l, r) => l.MetadataToken - r.MetadataToken);
		}

		[Benchmark(Description = "Array.Sort")]
		public void ArraySort()
		{
			Array.Copy(s_members, s_membersSameSize, s_members.Length);
			Array.Sort(s_membersSameSize, 0, s_members.Length, new LambdaComparer<MemberInfo>((l, r) => l.MetadataToken - r.MetadataToken));
		}
	}
}
