using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;
using System.Diagnostics;

namespace CitizenFX.MsgPack.Benchmarks
{
	internal class Program
	{
#pragma warning disable CS0162 // unreachable code detected

		private const int IterationCount = 1;
		private const bool BenchmarkSerialization = true;
		private const bool BenchmarkSorting = false;

		private const bool RunOnDefault = false;
		private const bool RunOnMono = true;
		private const string MonoPath = "C:\\Program Files\\Mono\\bin\\mono.exe";

		static void Main()
		{
			IConfig config = CreateConfiguration();

			if (BenchmarkSerialization)
				BenchmarkRunner.Run<MsgPackBenchmark>(config);

			if (BenchmarkSorting)
				BenchmarkRunner.Run<SortingBenchmark>(config);

			if (Debugger.IsAttached)
				Console.ReadLine();
		}

		private static IConfig CreateConfiguration()
		{
			var config = DefaultConfig.Instance;

			if (RunOnDefault)
				config.AddJob(Job.Default.WithIterationCount(IterationCount));

			if (RunOnMono)
				config.AddJob(Job.Default.WithRuntime(new MonoRuntime("Mono", MonoPath)).WithIterationCount(IterationCount));

			return config;
		}

#pragma warning restore CS0162
	}
}
