using LiveAudioTranslator.Runner;

try
{
    RunnerOptions options = RunnerOptions.Parse(args);
    return await TranslationRunner.RunAsync(options);
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"event=runner outcome=failed error={RunnerLog.Sanitize($"{ex.GetType().Name}:{ex.Message}")}");
    return 1;
}
