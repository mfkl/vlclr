using LiveAudioTranslator.Worker;

try
{
    WorkerCommandLine options = WorkerCommandLine.Parse(args);
    if (options.Benchmark)
        return await ProviderBenchmark.RunAsync(options);

    var host = new WorkerHost(options);
    await host.RunAsync();
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"event=worker_exit outcome=failed error={WorkerLog.Sanitize($"{ex.GetType().Name}:{ex.Message}")}");
    return 1;
}
