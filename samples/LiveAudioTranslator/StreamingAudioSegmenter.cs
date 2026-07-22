namespace LiveAudioTranslator;

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

    private readonly Action<float[]> _onUtterance;
    private readonly float _vadThreshold;
    private readonly int _silenceSamples;
    private readonly int _maximumUtteranceSamples;
    private readonly float[] _vadFrame = new float[VadFrameSamples];
    private readonly float[] _preRoll = new float[PreRollSamples];
    private readonly List<float> _utterance;
    private int _vadFrameCount;
    private int _preRollCount;
    private int _preRollWrite;
    private int _silentSamples;
    private int _inputRate;
    private double _nextInputFrame;
    private bool _speaking;

    public StreamingAudioSegmenter(
        float vadThreshold,
        int silenceMilliseconds,
        int maximumUtteranceMilliseconds,
        Action<float[]> onUtterance)
    {
        _vadThreshold = vadThreshold;
        _silenceSamples = OutputSampleRate * silenceMilliseconds / 1_000;
        _maximumUtteranceSamples = OutputSampleRate * maximumUtteranceMilliseconds / 1_000;
        _onUtterance = onUtterance;
        _utterance = new List<float>(_maximumUtteranceSamples + PreRollSamples);
    }

    public void PushFloat32(ReadOnlySpan<float> interleaved, int sampleRate, int channels)
    {
        int frameCount = channels > 0 ? interleaved.Length / channels : 0;
        if (!PrepareResampling(frameCount, sampleRate, channels, out double position, out double step))
            return;

        while (position < frameCount)
        {
            AddResampledSample(DownmixFloat(interleaved, (int)position, channels));
            position += step;
        }

        _nextInputFrame = position - frameCount;
    }

    public void PushPcm16(ReadOnlySpan<short> interleaved, int sampleRate, int channels)
    {
        int frameCount = channels > 0 ? interleaved.Length / channels : 0;
        if (!PrepareResampling(frameCount, sampleRate, channels, out double position, out double step))
            return;

        while (position < frameCount)
        {
            AddResampledSample(DownmixPcm16(interleaved, (int)position, channels));
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
        _utterance.Clear();
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

    private void AddResampledSample(float sample)
    {
        _vadFrame[_vadFrameCount++] = Math.Clamp(sample, -1f, 1f);
        if (_vadFrameCount != VadFrameSamples)
            return;

        ProcessVadFrame(_vadFrame);
        _vadFrameCount = 0;
    }

    private void ProcessVadFrame(ReadOnlySpan<float> frame)
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
            }

            Append(frame);
            _silentSamples = 0;
        }
        else if (_speaking)
        {
            Append(frame);
            _silentSamples += frame.Length;
            if (_silentSamples >= _silenceSamples)
                CompleteUtterance();
        }
        else
        {
            AppendPreRollFrame(frame);
        }

        if (_speaking && _utterance.Count >= _maximumUtteranceSamples)
            CompleteUtterance();
    }

    private void CompleteUtterance()
    {
        if (_utterance.Count >= MinimumUtteranceSamples)
            _onUtterance(_utterance.ToArray());

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

    private void AppendPreRollFrame(ReadOnlySpan<float> samples)
    {
        foreach (float sample in samples)
        {
            _preRoll[_preRollWrite] = sample;
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
