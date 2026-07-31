using System.Diagnostics;
using TerraFX.Interop;
using VLCLR.Native;
using VLCLR.ObjectDetection;
using static TerraFX.Interop.Windows;

namespace YoloObjectSearch;

internal sealed unsafe class GpuObjectDetector : IDisposable
{
    private sealed record PublishedDetectionBatch(
        long TimelineEpoch,
        DetectionBatch Batch);

    private readonly D3D11Nv12Scaler _scaler;
    private readonly ObjectDetectionModelProfile _profile;
    private readonly long _submissionPeriodTicks;
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly Thread _worker;

    private OpenVinoDetectionSession? _session;
    private PublishedDetectionBatch? _latest;
    private string? _failure;
    private long _generation;
    private long _nextSubmissionTimestamp;
    private long _warmupSkipped;
    private long _rateSkipped;
    private long _busySkipped;
    private long _submitted;
    private long _inferred;
    private long _totalBlitTicks;
    private long _maximumBlitTicks;
    private long _totalInferenceTicks;
    private long _maximumInferenceTicks;
    private long _mediaTime;
    private long _submittedTimelineEpoch;
    private long _timelineEpoch;
    private int _sourceWidth;
    private int _sourceHeight;
    private int _busy;
    private bool _stopping;
    private bool _ready;
    private bool _disposed;

    public GpuObjectDetector(
        nint sourceTexture,
        int sourceWidth,
        int sourceHeight,
        ObjectDetectionModelProfile profile,
        float targetRate)
    {
        if (sourceTexture == 0)
        {
            throw new ArgumentNullException(nameof(sourceTexture));
        }

        _profile = profile ??
            throw new ArgumentNullException(nameof(profile));
        if (_profile.Decoder.InputWidth <= 0 ||
            _profile.Decoder.InputHeight <= 0 ||
            (_profile.Decoder.InputWidth & 1) != 0 ||
            (_profile.Decoder.InputHeight & 1) != 0)
        {
            throw new ArgumentException(
                "The detector input dimensions must be positive and even.",
                nameof(profile));
        }

        ID3D11Texture2D* texture = (ID3D11Texture2D*)sourceTexture;
        ID3D11Device* device = null;
        texture->GetDevice(&device);
        if (device is null)
        {
            throw new InvalidOperationException(
                "The VLC texture has no D3D11 device.");
        }

        try
        {
            _scaler = new D3D11Nv12Scaler(
                device,
                checked((uint)sourceWidth),
                checked((uint)sourceHeight),
                checked((uint)_profile.Decoder.InputWidth),
                checked((uint)_profile.Decoder.InputHeight),
                _profile.Decoder.InputResizeMode);
        }
        finally
        {
            device->Release();
        }

        _submissionPeriodTicks = Math.Max(
            1,
            checked((long)Math.Round(
                Stopwatch.Frequency / targetRate)));
        _worker = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = $"VLCLR {_profile.Name} GPU worker"
        };
        _worker.Start();
    }

    public DetectionBatch? Latest
    {
        get
        {
            PublishedDetectionBatch? published =
                Volatile.Read(ref _latest);
            return published is not null &&
                published.TimelineEpoch ==
                Volatile.Read(ref _timelineEpoch)
                ? published.Batch
                : null;
        }
    }

    public string? Failure => Volatile.Read(ref _failure);

    public DetectorStatistics Statistics => new(
        Interlocked.Read(ref _warmupSkipped),
        Interlocked.Read(ref _rateSkipped),
        Interlocked.Read(ref _busySkipped),
        Interlocked.Read(ref _submitted),
        Interlocked.Read(ref _inferred),
        ToMilliseconds(Interlocked.Read(ref _totalBlitTicks)),
        ToMilliseconds(Interlocked.Read(ref _maximumBlitTicks)),
        ToMilliseconds(Interlocked.Read(ref _totalInferenceTicks)),
        ToMilliseconds(Interlocked.Read(ref _maximumInferenceTicks)));

    public bool TrySubmit(
        VLCD3D11Surface surface,
        int sourceWidth,
        int sourceHeight,
        long mediaTime)
    {
        if (!Volatile.Read(ref _ready) ||
            Volatile.Read(ref _stopping))
        {
            Interlocked.Increment(ref _warmupSkipped);
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        long due = Volatile.Read(ref _nextSubmissionTimestamp);
        if (due != 0 && now < due)
        {
            Interlocked.Increment(ref _rateSkipped);
            return false;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            Interlocked.Increment(ref _busySkipped);
            return false;
        }

        try
        {
            long blitStart = Stopwatch.GetTimestamp();
            _scaler.Blit(
                (ID3D11Texture2D*)surface.Texture,
                surface.ArraySlice);
            long blitTicks = Stopwatch.GetTimestamp() - blitStart;
            Interlocked.Add(ref _totalBlitTicks, blitTicks);
            SetMaximum(ref _maximumBlitTicks, blitTicks);
            Interlocked.Increment(ref _submitted);

            _sourceWidth = sourceWidth;
            _sourceHeight = sourceHeight;
            _mediaTime = mediaTime;
            _submittedTimelineEpoch =
                Volatile.Read(ref _timelineEpoch);
            long nextDue = due == 0 ||
                now - due > _submissionPeriodTicks * 2
                ? now + _submissionPeriodTicks
                : due + _submissionPeriodTicks;
            Volatile.Write(ref _nextSubmissionTimestamp, nextDue);
            _workAvailable.Set();
            return true;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _failure, exception.Message);
            Volatile.Write(ref _busy, 0);
            return false;
        }
    }

    public void ResetTimeline()
    {
        Interlocked.Increment(ref _timelineEpoch);
        Volatile.Write(ref _latest, null);
        Volatile.Write(ref _nextSubmissionTimestamp, 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Volatile.Write(ref _stopping, true);
        _workAvailable.Set();
        _worker.Join();
        _workAvailable.Dispose();
        _scaler.Dispose();
        _disposed = true;
    }

    private void WorkerMain()
    {
        try
        {
            _session = new OpenVinoDetectionSession(
                _scaler.Device,
                _scaler.OutputTexture,
                _profile);
            Volatile.Write(ref _ready, true);

            while (true)
            {
                _workAvailable.WaitOne();
                if (Volatile.Read(ref _stopping))
                {
                    break;
                }

                try
                {
                    long timelineEpoch =
                        Volatile.Read(ref _submittedTimelineEpoch);
                    long generation = Interlocked.Increment(
                        ref _generation);
                    DetectionBatch batch = _session.Infer(
                        generation,
                        _sourceWidth,
                        _sourceHeight,
                        TimeSpan.FromTicks(_mediaTime * 10));
                    long inferenceTicks = checked((long)Math.Round(
                        batch.InferenceDuration.TotalSeconds *
                        Stopwatch.Frequency));
                    Interlocked.Add(
                        ref _totalInferenceTicks,
                        inferenceTicks);
                    SetMaximum(
                        ref _maximumInferenceTicks,
                        inferenceTicks);
                    Interlocked.Increment(ref _inferred);
                    if (timelineEpoch ==
                        Volatile.Read(ref _timelineEpoch))
                    {
                        Volatile.Write(
                            ref _latest,
                            new PublishedDetectionBatch(
                                timelineEpoch,
                                batch));
                    }
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref _failure, exception.Message);
                }
                finally
                {
                    Volatile.Write(ref _busy, 0);
                }
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(
                ref _failure,
                $"OpenVINO GPU startup failed for {_profile.Name}. Verify " +
                "the validated runtime, " +
                "the Intel GPU driver, and same-adapter D3D11 remote-context " +
                $"support. {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _ready, false);
            _session?.Dispose();
            _session = null;
            Volatile.Write(ref _busy, 0);
        }
    }

    private static double ToMilliseconds(long stopwatchTicks)
    {
        return stopwatchTicks * 1000.0 / Stopwatch.Frequency;
    }

    private static void SetMaximum(ref long target, long value)
    {
        long current = Volatile.Read(ref target);
        while (value > current)
        {
            long observed = Interlocked.CompareExchange(
                ref target,
                value,
                current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

internal readonly record struct DetectorStatistics(
    long WarmupSkipped,
    long RateSkipped,
    long BusySkipped,
    long Submitted,
    long Inferred,
    double TotalBlitMilliseconds,
    double MaximumBlitMilliseconds,
    double TotalInferenceMilliseconds,
    double MaximumInferenceMilliseconds)
{
    public double AverageBlitMilliseconds => Submitted == 0
        ? 0
        : TotalBlitMilliseconds / Submitted;

    public double AverageInferenceMilliseconds => Inferred == 0
        ? 0
        : TotalInferenceMilliseconds / Inferred;
}
