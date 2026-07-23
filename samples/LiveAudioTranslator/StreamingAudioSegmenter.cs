namespace LiveAudioTranslator;

internal readonly record struct TimedAudioSegment(
    float[] Samples,
    long StartMediaTicks,
    long EndMediaTicks,
    bool ForcedSplit);

/// <summary>
/// Downmixes and resamples decoded VLC audio to Whisper's 16-kHz mono format,
/// then emits bounded speech utterances using a lightweight energy VAD.
/// </summary>
internal sealed class StreamingAudioSegmenter
{
    public const int OutputSampleRate = 16_000;
    private const int VadFrameSamples = 320; // 20 ms
    private const int PreRollSamples = 4_800; // 300 ms
    private const int MinimumUtteranceSamples = 4_000; // 250 ms

    private const int ForcedSplitOverlapSamples = 4_000; // 250 ms

    private readonly Action<TimedAudioSegment> _onUtterance;
    private readonly float _vadThreshold;
    private readonly int _silenceSamples;
    private readonly int _maximumUtteranceSamples;
    private readonly float[] _vadFrame = new float[VadFrameSamples];
    private readonly float[] _preRoll = new float[PreRollSamples];
    private readonly long[] _preRollTicks = new long[PreRollSamples];
    private readonly List<float> _utterance;
    private int _vadFrameCount;
    private int _preRollCount;
    private int _preRollWrite;
    private int _silentSamples;
    private int _inputRate;
    private double _nextInputFrame;
    private bool _speaking;
    private long _vadFrameStartTicks;
    private long _vadFrameEndTicks;
    private long _utteranceStartTicks;
    private long _lastVoicedEndTicks;

    public StreamingAudioSegmenter(
        float vadThreshold,
        int silenceMilliseconds,
        int maximumUtteranceMilliseconds,
        Action<float[]> onUtterance)
        : this(vadThreshold, silenceMilliseconds, maximumUtteranceMilliseconds,
            segment => onUtterance(segment.Samples))
    {
    }

    public StreamingAudioSegmenter(
        float vadThreshold,
        int silenceMilliseconds,
        int maximumUtteranceMilliseconds,
        Action<TimedAudioSegment> onUtterance)
    {
        _vadThreshold = vadThreshold;
        _silenceSamples = OutputSampleRate * silenceMilliseconds / 1_000;
        _maximumUtteranceSamples = OutputSampleRate * maximumUtteranceMilliseconds / 1_000;
        _onUtterance = onUtterance;
        _utterance = new List<float>(_maximumUtteranceSamples + PreRollSamples);
    }

    public void PushFloat32(ReadOnlySpan<float> interleaved, int sampleRate, int channels)
    {
        long duration = channels > 0 && sampleRate > 0
            ? interleaved.Length / channels * 1_000_000L / sampleRate
            : 0;
        PushFloat32(interleaved, sampleRate, channels, 0, duration);
    }

    public void PushFloat32(
        ReadOnlySpan<float> interleaved,
        int sampleRate,
        int channels,
        long firstSampleMediaPts,
        long blockDurationTicks)
    {
        int frameCount = channels > 0 ? interleaved.Length / channels : 0;
        if (!PrepareResampling(frameCount, sampleRate, channels, out double position, out double step))
            return;

        while (position < frameCount)
        {
            long tick = MapInputPositionToTick(position, frameCount, firstSampleMediaPts, blockDurationTicks);
            AddResampledSample(DownmixFloat(interleaved, (int)position, channels), tick);
            position += step;
        }

        _nextInputFrame = position - frameCount;
    }

    public void PushPcm16(ReadOnlySpan<short> interleaved, int sampleRate, int channels)
    {
        long duration = channels > 0 && sampleRate > 0
            ? interleaved.Length / channels * 1_000_000L / sampleRate
            : 0;
        PushPcm16(interleaved, sampleRate, channels, 0, duration);
    }

    public void PushPcm16(
        ReadOnlySpan<short> interleaved,
        int sampleRate,
        int channels,
        long firstSampleMediaPts,
        long blockDurationTicks)
    {
        int frameCount = channels > 0 ? interleaved.Length / channels : 0;
        if (!PrepareResampling(frameCount, sampleRate, channels, out double position, out double step))
            return;

        while (position < frameCount)
        {
            long tick = MapInputPositionToTick(position, frameCount, firstSampleMediaPts, blockDurationTicks);
            AddResampledSample(DownmixPcm16(interleaved, (int)position, channels), tick);
            position += step;
        }

        _nextInputFrame = position - frameCount;
    }

    public void Reset()
    {
        _vadFrameCount = 0;
        _preRollCount = 0;
        _preRollWrite = 0;
        _silentSamples = 0;
        _nextInputFrame = 0;
        _inputRate = 0;
        _speaking = false;
        _vadFrameStartTicks = 0;
        _vadFrameEndTicks = 0;
        _utteranceStartTicks = 0;
        _lastVoicedEndTicks = 0;
        _utterance.Clear();
    }

    public void Flush()
    {
        if (_speaking)
            CompleteUtterance(forcedSplit: false);
        _vadFrameCount = 0;
    }

    private bool PrepareResampling(
        int frameCount,
        int sampleRate,
        int channels,
        out double position,
        out double step)
    {
        position = 0;
        step = 0;
        if (frameCount <= 0 || sampleRate <= 0 || channels <= 0)
            return false;

        if (_inputRate != sampleRate)
        {
            _inputRate = sampleRate;
            _nextInputFrame = 0;
        }

        step = sampleRate / (double)OutputSampleRate;
        position = _nextInputFrame;
        return true;
    }

    private void AddResampledSample(float sample, long mediaTick)
    {
        if (_vadFrameCount == 0)
            _vadFrameStartTicks = mediaTick;
        _vadFrameEndTicks = mediaTick + 1_000_000L / OutputSampleRate;
        _vadFrame[_vadFrameCount++] = Math.Clamp(sample, -1f, 1f);
        if (_vadFrameCount != VadFrameSamples)
            return;

        ProcessVadFrame(_vadFrame, _vadFrameStartTicks, _vadFrameEndTicks);
        _vadFrameCount = 0;
    }

    private void ProcessVadFrame(ReadOnlySpan<float> frame, long frameStartTicks, long frameEndTicks)
    {
        double sumSquares = 0;
        foreach (float sample in frame)
            sumSquares += sample * sample;
        float rms = (float)Math.Sqrt(sumSquares / frame.Length);
        bool voiced = rms >= _vadThreshold;

        if (voiced)
        {
            if (!_speaking)
            {
                _speaking = true;
                AppendPreRoll();
                _utteranceStartTicks = _preRollCount > 0
                    ? _preRollTicks[(_preRollWrite - _preRollCount + _preRoll.Length) % _preRoll.Length]
                    : frameStartTicks;
            }

            Append(frame);
            _lastVoicedEndTicks = frameEndTicks;
            _silentSamples = 0;
        }
        else if (_speaking)
        {
            Append(frame);
            _silentSamples += frame.Length;
            if (_silentSamples >= _silenceSamples)
                CompleteUtterance(forcedSplit: false);
        }
        else
        {
            AppendPreRollFrame(frame, frameStartTicks);
        }

        if (_speaking && _utterance.Count >= _maximumUtteranceSamples)
            CompleteUtterance(forcedSplit: true);
    }

    private void CompleteUtterance(bool forcedSplit)
    {
        if (_utterance.Count >= MinimumUtteranceSamples)
        {
            long end = forcedSplit ? _vadFrameEndTicks : Math.Max(_utteranceStartTicks + 1, _lastVoicedEndTicks);
            _onUtterance(new TimedAudioSegment(
                _utterance.ToArray(),
                _utteranceStartTicks,
                end,
                forcedSplit));
        }

        if (forcedSplit)
        {
            int overlap = Math.Min(ForcedSplitOverlapSamples, _utterance.Count);
            float[] tail = _utterance.GetRange(_utterance.Count - overlap, overlap).ToArray();
            _utterance.Clear();
            _utterance.AddRange(tail);
            _utteranceStartTicks = Math.Max(0, _vadFrameEndTicks - overlap * 1_000_000L / OutputSampleRate);
            _lastVoicedEndTicks = _vadFrameEndTicks;
            _silentSamples = 0;
            _preRollCount = 0;
            _preRollWrite = 0;
            return;
        }

        _utterance.Clear();
        _speaking = false;
        _silentSamples = 0;
        _preRollCount = 0;
        _preRollWrite = 0;
    }

    private void Append(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
            _utterance.Add(sample);
    }

    private void AppendPreRollFrame(ReadOnlySpan<float> samples, long frameStartTicks)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            _preRoll[_preRollWrite] = samples[index];
            _preRollTicks[_preRollWrite] = frameStartTicks + index * 1_000_000L / OutputSampleRate;
            _preRollWrite = (_preRollWrite + 1) % _preRoll.Length;
            _preRollCount = Math.Min(_preRollCount + 1, _preRoll.Length);
        }
    }

    private void AppendPreRoll()
    {
        int start = (_preRollWrite - _preRollCount + _preRoll.Length) % _preRoll.Length;
        for (int index = 0; index < _preRollCount; index++)
            _utterance.Add(_preRoll[(start + index) % _preRoll.Length]);
    }

    private static long MapInputPositionToTick(
        double position,
        int frameCount,
        long firstSampleMediaPts,
        long blockDurationTicks)
    {
        if (frameCount <= 0 || blockDurationTicks <= 0)
            return firstSampleMediaPts;
        double fraction = Math.Clamp(position / frameCount, 0d, 1d);
        return firstSampleMediaPts + (long)Math.Round(blockDurationTicks * fraction);
    }

    private static float DownmixFloat(ReadOnlySpan<float> samples, int frame, int channels)
    {
        int offset = frame * channels;
        float sum = 0;
        for (int channel = 0; channel < channels; channel++)
            sum += samples[offset + channel];
        return sum / channels;
    }

    private static float DownmixPcm16(ReadOnlySpan<short> samples, int frame, int channels)
    {
        int offset = frame * channels;
        float sum = 0;
        for (int channel = 0; channel < channels; channel++)
            sum += samples[offset + channel] / 32768f;
        return sum / channels;
    }
}
