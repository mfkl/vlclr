namespace LiveAudioTranslator;

internal readonly record struct PreparationLaunchDecision(bool Launch, bool RequireComplete, long RequiredLeadTicks);

internal static class LeadPolicy
{
    public const long InitialLeadTicks = 15_000_000;
    public const long MaximumStreamingLeadTicks = 120_000_000;

    public static PreparationLaunchDecision Decide(
        double realTimeFactor,
        long processedAudioTicks,
        long audioDurationTicks,
        bool complete,
        long minimumLeadTicks = InitialLeadTicks)
    {
        if (audioDurationTicks <= 0 || processedAudioTicks < 0 || minimumLeadTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(audioDurationTicks));
        if (complete)
            return new(true, false, Math.Min(audioDurationTicks, minimumLeadTicks));
        if (!double.IsFinite(realTimeFactor) || realTimeFactor >= 1d)
            return new(false, true, audioDurationTicks);

        long remaining = Math.Max(0, audioDurationTicks - processedAudioTicks);
        double pressure = Math.Clamp((realTimeFactor - 0.67d) / 0.33d, 0d, 1d);
        long safety = checked((long)Math.Min(long.MaxValue, remaining * pressure * 0.15d));
        long required = Math.Min(audioDurationTicks, Math.Max(minimumLeadTicks, minimumLeadTicks + safety));
        if (required > MaximumStreamingLeadTicks)
            return new(false, true, required);
        return new(processedAudioTicks >= required, false, required);
    }
}
