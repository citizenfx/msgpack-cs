using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;

namespace MsgPack.Benchmarks
{
	internal class Program
	{
		static void Main()
		{
#pragma warning disable CS0162

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

			Console.ReadLine();

#pragma warning restore CS0162
		}
	}
}
