using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using MsgPack.Tests;
using System;

namespace MsgPack.Benchmarks
{
	internal class Program
	{
		static void Main()
		{
#pragma warning disable CS0162

			if (false)
			{
				new Deserialize().DeserializeTest();
				//SortingBenchmark.TestSorting();
			}

			//if (false)
			{
				//var config = DefaultConfig.Instance.AddJob(Job.Default.WithRuntime(new MonoRuntime("Mono", "C:\\Program Files\\Mono\\bin\\mono.exe")).WithIterationCount(20));
				var config = DefaultConfig.Instance.AddJob(Job.Default.WithIterationCount(20));
				Console.WriteLine(BenchmarkRunner.Run<MsgPackBenchmark>(config));
			}

			if (false)
			{
				var config = DefaultConfig.Instance.AddJob(Job.Default.WithIterationCount(20));
				Console.WriteLine(BenchmarkRunner.Run<SortingBenchmark>(config));
			}

			//(new Benchmark()).SerializeDictionaryStringStringAsObject2();

			Console.ReadLine();

#pragma warning restore CS0162
		}
	}
}
