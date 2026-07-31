namespace VLCLR.ObjectDetection;

public sealed record SsdDetectionOutputDecoderOptions(
    int InputWidth = 300,
    int InputHeight = 300,
    int OutputDetectionCount = 200,
    float ConfidenceThreshold = 0.30f,
    int MaximumDetections = 100,
    ObjectDetectionInputResizeMode InputResizeMode =
        ObjectDetectionInputResizeMode.Stretch);

public sealed record SsdDetectionClassMapping(
    int ModelLabel,
    ObjectClassDescriptor ObjectClass);

/// <summary>
/// Decodes the common SSD DetectionOutput layout:
/// image ID, model label, confidence, and normalized box coordinates.
/// </summary>
public sealed class SsdDetectionOutputDecoder : IObjectDetectionOutputDecoder
{
    private const int ValuesPerDetection = 7;

    private readonly IReadOnlyDictionary<int, ObjectClassDescriptor>
        _classesByModelLabel;
    private readonly SsdDetectionOutputDecoderOptions _options;

    public SsdDetectionOutputDecoder(
        IEnumerable<SsdDetectionClassMapping> classes,
        SsdDetectionOutputDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(classes);
        _options = options ?? new SsdDetectionOutputDecoderOptions();
        ValidateOptions(_options);

        var mappings = new Dictionary<int, ObjectClassDescriptor>();
        foreach (SsdDetectionClassMapping mapping in classes)
        {
            if (mapping.ModelLabel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(classes),
                    mapping.ModelLabel,
                    "SSD model labels cannot be negative.");
            }
            ArgumentNullException.ThrowIfNull(mapping.ObjectClass);
            if (!mappings.TryAdd(mapping.ModelLabel, mapping.ObjectClass))
            {
                throw new ArgumentException(
                    $"SSD model label {mapping.ModelLabel} is mapped more " +
                    "than once.",
                    nameof(classes));
            }
        }

        if (mappings.Count == 0)
        {
            throw new ArgumentException(
                "At least one SSD class mapping is required.",
                nameof(classes));
        }
        _classesByModelLabel = mappings;
    }

    public int InputWidth => _options.InputWidth;

    public int InputHeight => _options.InputHeight;

    public ObjectDetectionInputResizeMode InputResizeMode =>
        _options.InputResizeMode;

    public int ExpectedOutputLength =>
        _options.OutputDetectionCount * ValuesPerDetection;

    public IReadOnlyList<ObjectDetection> Decode(
        ReadOnlySpan<float> output,
        int sourceWidth,
        int sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        if (output.Length != ExpectedOutputLength)
        {
            throw new ArgumentException(
                $"Expected {ExpectedOutputLength} output values " +
                $"({_options.OutputDetectionCount} detections x " +
                $"{ValuesPerDetection}), but received {output.Length}.",
                nameof(output));
        }

        YoloXImageTransform transform = InputResizeMode switch
        {
            ObjectDetectionInputResizeMode.CenteredLetterbox =>
                YoloXImageTransform.CreateCenteredLetterbox(
                    sourceWidth,
                    sourceHeight,
                    InputWidth,
                    InputHeight),
            ObjectDetectionInputResizeMode.Stretch =>
                new YoloXImageTransform(
                    sourceWidth,
                    sourceHeight,
                    0,
                    0,
                    InputWidth,
                    InputHeight),
            _ => throw new ArgumentOutOfRangeException(
                nameof(InputResizeMode))
        };
        var detections = new List<ObjectDetection>();
        for (int index = 0;
             index < _options.OutputDetectionCount;
             index++)
        {
            int offset = index * ValuesPerDetection;
            float imageId = output[offset];
            if (float.IsFinite(imageId) && imageId < 0)
            {
                break;
            }

            float modelLabelValue = output[offset + 1];
            float confidence = output[offset + 2];
            if (!float.IsFinite(modelLabelValue) ||
                !float.IsFinite(confidence) ||
                confidence < _options.ConfidenceThreshold)
            {
                continue;
            }

            double roundedModelLabel = Math.Round(modelLabelValue);
            if (roundedModelLabel < int.MinValue ||
                roundedModelLabel > int.MaxValue)
            {
                continue;
            }
            int modelLabel = (int)roundedModelLabel;
            if (!_classesByModelLabel.TryGetValue(
                    modelLabel,
                    out ObjectClassDescriptor? objectClass))
            {
                continue;
            }

            float left = output[offset + 3] * InputWidth;
            float top = output[offset + 4] * InputHeight;
            float right = output[offset + 5] * InputWidth;
            float bottom = output[offset + 6] * InputHeight;
            if (!TryMapToSource(
                    transform,
                    ref left,
                    ref top,
                    ref right,
                    ref bottom))
            {
                continue;
            }

            detections.Add(new ObjectDetection(
                objectClass.Id,
                objectClass.Label,
                confidence,
                new DetectionBox(
                    left,
                    top,
                    right - left,
                    bottom - top)));
            if (detections.Count == _options.MaximumDetections)
            {
                break;
            }
        }
        return detections;
    }

    private static bool TryMapToSource(
        YoloXImageTransform transform,
        ref float left,
        ref float top,
        ref float right,
        ref float bottom)
    {
        if (!float.IsFinite(left) ||
            !float.IsFinite(top) ||
            !float.IsFinite(right) ||
            !float.IsFinite(bottom))
        {
            return false;
        }

        float scaleX = transform.ContentWidth / transform.SourceWidth;
        float scaleY = transform.ContentHeight / transform.SourceHeight;
        left = (left - transform.ContentX) / scaleX;
        top = (top - transform.ContentY) / scaleY;
        right = (right - transform.ContentX) / scaleX;
        bottom = (bottom - transform.ContentY) / scaleY;

        left = Math.Clamp(left, 0, transform.SourceWidth);
        top = Math.Clamp(top, 0, transform.SourceHeight);
        right = Math.Clamp(right, 0, transform.SourceWidth);
        bottom = Math.Clamp(bottom, 0, transform.SourceHeight);
        return right > left && bottom > top;
    }

    private static void ValidateOptions(
        SsdDetectionOutputDecoderOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.InputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.InputHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.OutputDetectionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaximumDetections);
        if (!float.IsFinite(options.ConfidenceThreshold) ||
            options.ConfidenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Confidence threshold must be between zero and one.");
        }
        if (!Enum.IsDefined(options.InputResizeMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The input resize mode is not supported.");
        }
    }
}
