using SubtitleTranslator;

namespace SubtitleTranslator.UnitTests;

public sealed class TranslationServiceTests
{
    [Fact]
    public void TimedOutWork_CompletesAndPopulatesCache()
    {
        using var gate = new ManualResetEventSlim(false);
        var engine = new RecordingEngine(text =>
        {
            gate.Wait(TimeSpan.FromSeconds(2));
            return $"translated:{text}";
        });
        using var service = new TranslationService(engine, new TranslationServiceOptions
        {
            CacheCapacity = 4,
            QueueCapacity = 2
        });

        TranslationResponse timedOut = service.Translate("hello", TimeSpan.FromMilliseconds(10));
        Assert.Equal(TranslationOutcome.DeadlineExceeded, timedOut.Outcome);

        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => service.CachedCueCount == 1, TimeSpan.FromSeconds(2)));

        TranslationResponse cached = service.Translate("hello", TimeSpan.FromMilliseconds(10));
        Assert.Equal(TranslationOutcome.CacheHit, cached.Outcome);
        Assert.Equal("translated:hello", cached.Text);
        Assert.Equal(1, engine.CallCount);
    }

    [Fact]
    public async Task Queue_IsBoundedWhileDecoderIsBusy()
    {
        using var entered = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var engine = new RecordingEngine(text =>
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(2));
            return text;
        });
        using var service = new TranslationService(engine, new TranslationServiceOptions
        {
            CacheCapacity = 4,
            QueueCapacity = 1
        });

        Task<TranslationResponse> first = Task.Run(() => service.Translate("one", TimeSpan.FromSeconds(2)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
        Task<TranslationResponse> second = Task.Run(() => service.Translate("two", TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => service.PendingCueCount >= 2, TimeSpan.FromSeconds(1)));

        TranslationResponse third = service.Translate("three", TimeSpan.FromMilliseconds(20));
        Assert.Equal(TranslationOutcome.QueueFull, third.Outcome);

        gate.Set();
        Assert.True((await first).IsSuccess);
        Assert.True((await second).IsSuccess);
    }

    [Fact]
    public async Task DuplicateInflightCue_IsTranslatedOnce()
    {
        using var entered = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var engine = new RecordingEngine(text =>
        {
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(2));
            return text + "!";
        });
        using var service = new TranslationService(engine);

        Task<TranslationResponse> first = Task.Run(() => service.Translate("same", TimeSpan.FromSeconds(2)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(1)));
        Task<TranslationResponse> second = Task.Run(() => service.Translate("same", TimeSpan.FromSeconds(2)));
        gate.Set();

        Assert.Equal("same!", (await first).Text);
        Assert.Equal("same!", (await second).Text);
        Assert.Equal(1, engine.CallCount);
    }

    [Fact]
    public void Registry_SharesOneProcessWideService()
    {
        int factoryCalls = 0;
        var key = new TranslationServiceKey(
            Path.Combine(Path.GetTempPath(), "vlclr-shared-model"),
            "EN",
            "FR",
            "CPU",
            4,
            128,
            128,
            16,
            2);

        using TranslationServiceLease first = TranslationServiceRegistry.Acquire(
            key,
            () =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new RecordingEngine(text => text);
            });
        using TranslationServiceLease second = TranslationServiceRegistry.Acquire(
            key,
            () => throw new InvalidOperationException("Factory must not run twice."));

        Assert.Same(first.Service, second.Service);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, TranslationServiceRegistry.ActiveServiceCount);
    }
}

internal sealed class RecordingEngine(Func<string, string> translate) : ITranslationEngine
{
    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    public TranslationResult TranslateDetailed(string text)
    {
        Interlocked.Increment(ref _callCount);
        string result = translate(text);
        return new TranslationResult(
            result,
            Array.Empty<int>(),
            1,
            1,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.Zero);
    }

    public void Dispose() { }
}
