namespace VLCLR.ObjectDetection;

public sealed record YoloXDecoderOptions(
    int InputWidth = 416,
    int InputHeight = 416,
    float ConfidenceThreshold = 0.30f,
    float NonMaximumSuppressionThreshold = 0.45f,
    int MaximumDetections = 100,
    bool ClassAgnosticSuppression = true,
    bool OutputCoordinatesAreDecoded = false);

public readonly record struct YoloXImageTransform(
    int SourceWidth,
    int SourceHeight,
    float ContentX,
    float ContentY,
    float ContentWidth,
    float ContentHeight)
{
    public static YoloXImageTransform CreateCenteredLetterbox(
        int sourceWidth,
        int sourceHeight,
        int inputWidth,
        int inputHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputHeight);

        float scale = MathF.Min(
            (float)inputWidth / sourceWidth,
            (float)inputHeight / sourceHeight);
        float contentWidth = MathF.Round(sourceWidth * scale);
        float contentHeight = MathF.Round(sourceHeight * scale);

        return new YoloXImageTransform(
            sourceWidth,
            sourceHeight,
            MathF.Floor((inputWidth - contentWidth) / 2),
            MathF.Floor((inputHeight - contentHeight) / 2),
            contentWidth,
            contentHeight);
    }
}

public sealed class YoloXOutputDecoder : IObjectDetectionOutputDecoder
{
    private static readonly int[] Strides = [8, 16, 32];

    private readonly ObjectClassCatalog _catalog;
    private readonly YoloXDecoderOptions _options;
    private readonly GridCell[] _grid;

    public YoloXOutputDecoder(
        ObjectClassCatalog catalog,
        YoloXDecoderOptions? options = null)
    {
        _catalog = catalog ??
            throw new ArgumentNullException(nameof(catalog));
        _options = options ?? new YoloXDecoderOptions();
        ValidateOptions(_options);
        ValidateCatalog(_catalog);
        _grid = CreateGrid(_options.InputWidth, _options.InputHeight);
    }

    public int ProposalCount => _grid.Length;

    public int ValuesPerProposal => 5 + _catalog.Classes.Count;

    public int InputWidth => _options.InputWidth;

    public int InputHeight => _options.InputHeight;

    public ObjectDetectionInputResizeMode InputResizeMode =>
        ObjectDetectionInputResizeMode.CenteredLetterbox;

    public int ExpectedOutputLength => ProposalCount * ValuesPerProposal;

    public IReadOnlyList<ObjectDetection> Decode(
        ReadOnlySpan<float> output,
        int sourceWidth,
        int sourceHeight)
    {
        YoloXImageTransform transform =
            YoloXImageTransform.CreateCenteredLetterbox(
                sourceWidth,
                sourceHeight,
                _options.InputWidth,
                _options.InputHeight);
        return Decode(output, transform);
    }

    public IReadOnlyList<ObjectDetection> Decode(
        ReadOnlySpan<float> output,
        YoloXImageTransform transform)
    {
        ValidateTransform(transform);
        if (output.Length != ExpectedOutputLength)
        {
            throw new ArgumentException(
                $"Expected {ExpectedOutputLength} output values " +
                $"({ProposalCount} proposals x {ValuesPerProposal}), " +
                $"but received {output.Length}.",
                nameof(output));
        }

        var candidates = new List<Candidate>();
        int valuesPerProposal = ValuesPerProposal;
        for (int proposalIndex = 0;
             proposalIndex < ProposalCount;
             proposalIndex++)
        {
            int offset = proposalIndex * valuesPerProposal;
            float objectness = output[offset + 4];
            if (!float.IsFinite(objectness) ||
                objectness < _options.ConfidenceThreshold)
            {
                continue;
            }

            int classId = 0;
            float bestClassScore = float.NegativeInfinity;
            for (int candidateClass = 0;
                 candidateClass < _catalog.Classes.Count;
                 candidateClass++)
            {
                float classScore = output[offset + 5 + candidateClass];
                if (classScore > bestClassScore)
                {
                    classId = candidateClass;
                    bestClassScore = classScore;
                }
            }

            float confidence = objectness * bestClassScore;
            if (!float.IsFinite(confidence) ||
                confidence < _options.ConfidenceThreshold)
            {
                continue;
            }

            GridCell gridCell = _grid[proposalIndex];
            float centerX = output[offset];
            float centerY = output[offset + 1];
            float width = output[offset + 2];
            float height = output[offset + 3];
            if (!_options.OutputCoordinatesAreDecoded)
            {
                centerX = (centerX + gridCell.X) * gridCell.Stride;
                centerY = (centerY + gridCell.Y) * gridCell.Stride;
                width = MathF.Exp(width) * gridCell.Stride;
                height = MathF.Exp(height) * gridCell.Stride;
            }

            float left = centerX - width / 2;
            float top = centerY - height / 2;
            float right = centerX + width / 2;
            float bottom = centerY + height / 2;
            if (!TryMapToSource(
                    transform,
                    ref left,
                    ref top,
                    ref right,
                    ref bottom))
            {
                continue;
            }

            candidates.Add(new Candidate(
                classId,
                confidence,
                left,
                top,
                right,
                bottom));
        }

        candidates.Sort(static (left, right) =>
            right.Confidence.CompareTo(left.Confidence));
        return ApplySuppression(candidates);
    }

    private IReadOnlyList<ObjectDetection> ApplySuppression(
        IReadOnlyList<Candidate> candidates)
    {
        var selected = new List<Candidate>(
            Math.Min(candidates.Count, _options.MaximumDetections));
        foreach (Candidate candidate in candidates)
        {
            bool suppressed = false;
            foreach (Candidate accepted in selected)
            {
                if (!_options.ClassAgnosticSuppression &&
                    accepted.ClassId != candidate.ClassId)
                {
                    continue;
                }

                if (IntersectionOverUnion(candidate, accepted) >
                    _options.NonMaximumSuppressionThreshold)
                {
                    suppressed = true;
                    break;
                }
            }

            if (suppressed)
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count == _options.MaximumDetections)
            {
                break;
            }
        }

        ObjectDetection[] detections = new ObjectDetection[selected.Count];
        for (int index = 0; index < selected.Count; index++)
        {
            Candidate candidate = selected[index];
            detections[index] = new ObjectDetection(
                candidate.ClassId,
                _catalog.Classes[candidate.ClassId].Label,
                candidate.Confidence,
                new DetectionBox(
                    candidate.Left,
                    candidate.Top,
                    candidate.Right - candidate.Left,
                    candidate.Bottom - candidate.Top));
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

        return float.IsFinite(left) &&
            float.IsFinite(top) &&
            float.IsFinite(right) &&
            float.IsFinite(bottom) &&
            right > left &&
            bottom > top;
    }

    private static float IntersectionOverUnion(
        Candidate left,
        Candidate right)
    {
        float intersectionWidth = MathF.Max(
            0,
            MathF.Min(left.Right, right.Right) -
            MathF.Max(left.Left, right.Left) +
            1);
        float intersectionHeight = MathF.Max(
            0,
            MathF.Min(left.Bottom, right.Bottom) -
            MathF.Max(left.Top, right.Top) +
            1);
        float intersection = intersectionWidth * intersectionHeight;
        float leftArea =
            (left.Right - left.Left + 1) *
            (left.Bottom - left.Top + 1);
        float rightArea =
            (right.Right - right.Left + 1) *
            (right.Bottom - right.Top + 1);
        return intersection / (leftArea + rightArea - intersection);
    }

    private static GridCell[] CreateGrid(int inputWidth, int inputHeight)
    {
        var cells = new List<GridCell>();
        foreach (int stride in Strides)
        {
            int gridWidth = inputWidth / stride;
            int gridHeight = inputHeight / stride;
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    cells.Add(new GridCell(x, y, stride));
                }
            }
        }

        return cells.ToArray();
    }

    private static void ValidateCatalog(ObjectClassCatalog catalog)
    {
        for (int index = 0; index < catalog.Classes.Count; index++)
        {
            if (catalog.Classes[index].Id != index)
            {
                throw new ArgumentException(
                    "YOLOX class IDs must be contiguous and zero-based.",
                    nameof(catalog));
            }
        }
    }

    private static void ValidateOptions(YoloXDecoderOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.InputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.InputHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaximumDetections);

        foreach (int stride in Strides)
        {
            if (options.InputWidth % stride != 0 ||
                options.InputHeight % stride != 0)
            {
                throw new ArgumentException(
                    $"Input dimensions must be divisible by stride {stride}.",
                    nameof(options));
            }
        }

        if (!float.IsFinite(options.ConfidenceThreshold) ||
            options.ConfidenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Confidence threshold must be between zero and one.");
        }

        if (!float.IsFinite(options.NonMaximumSuppressionThreshold) ||
            options.NonMaximumSuppressionThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Suppression threshold must be between zero and one.");
        }
    }

    private static void ValidateTransform(YoloXImageTransform transform)
    {
        if (transform.SourceWidth <= 0 ||
            transform.SourceHeight <= 0 ||
            !float.IsFinite(transform.ContentX) ||
            !float.IsFinite(transform.ContentY) ||
            !float.IsFinite(transform.ContentWidth) ||
            !float.IsFinite(transform.ContentHeight) ||
            transform.ContentWidth <= 0 ||
            transform.ContentHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transform),
                "The source and model content dimensions must be positive.");
        }
    }

    private readonly record struct GridCell(int X, int Y, int Stride);

    private readonly record struct Candidate(
        int ClassId,
        float Confidence,
        float Left,
        float Top,
        float Right,
        float Bottom);
}
