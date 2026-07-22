using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace SubtitleTranslator;

/// <summary>
/// CPU Marian/OPUS-MT engine using the allocation-conscious OrtValue API.
/// Production decoding retains Marian key/value tensors between token steps.
/// </summary>
public sealed class OnnxTranslator : ITranslationEngine
{
    private const string InputIdsName = "input_ids";
    private const string AttentionMaskName = "attention_mask";
    private const string EncoderAttentionMaskName = "encoder_attention_mask";
    private const string EncoderHiddenStatesName = "encoder_hidden_states";
    private const string UseCacheBranchName = "use_cache_branch";
    private const string LogitsName = "logits";

    private readonly object _sync = new();
    private readonly InferenceSession _encoderSession;
    private readonly InferenceSession _decoderSession;
    private readonly RunOptions _runOptions = new();
    private readonly SimpleTokenizer _tokenizer;
    private readonly ModelManifest _manifest;
    private readonly bool _useDecoderCache;
    private readonly bool _verifyDecoderCache;
    private readonly float _cacheParityGuardMargin;
    private readonly int _maxSourceLength;
    private readonly int _maxOutputLength;
    private readonly string[] _encoderInputNames;
    private readonly string[] _encoderOutputNames;
    private readonly OrtValue[] _encoderInputValues;
    private readonly string[] _decoderInputNames;
    private readonly string[] _decoderOutputNames;
    private readonly OrtValue[] _decoderInputValues;
    private readonly int _decoderInputIdsIndex;
    private readonly int _encoderAttentionMaskIndex;
    private readonly int _encoderHiddenStatesIndex;
    private readonly int _useCacheBranchIndex;
    private readonly int _logitsOutputIndex;
    private readonly List<CacheBinding> _cacheBindings;
    private readonly List<OrtValue> _emptyCacheValues = [];
    private readonly OrtValue _cacheBranchFalse;
    private readonly OrtValue _cacheBranchTrue;
    private readonly long[] _decoderTokenBuffer;
    private readonly long[] _decoderPrefixBuffer;
    private readonly long[][] _decoderInputShapes;
    private bool _disposed;

    public SimpleTokenizer Tokenizer => _tokenizer;
    public ModelManifest Manifest => _manifest;
    public bool UsesDecoderCache => _useDecoderCache;

    public OnnxTranslator(
        string modelDirectory,
        string sourceLanguage,
        string targetLanguage,
        int maxLength = 128,
        int intraOpThreads = 4,
        bool verifyModelHashes = true)
        : this(
            modelDirectory,
            sourceLanguage,
            targetLanguage,
            new OnnxTranslatorOptions
            {
                MaximumOutputTokens = maxLength,
                IntraOpThreads = intraOpThreads,
                VerifyModelHashes = verifyModelHashes
            })
    {
    }

    public OnnxTranslator(
        string modelDirectory,
        string sourceLanguage,
        string targetLanguage,
        OnnxTranslatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string pairDirectory = ModelManifest.ResolveModelDirectory(modelDirectory, sourceLanguage, targetLanguage);
        _manifest = ModelManifest.LoadAndValidate(
            pairDirectory,
            sourceLanguage,
            targetLanguage,
            options.VerifyModelHashes);

        _maxSourceLength = Math.Clamp(
            options.MaximumSourceTokens,
            1,
            _manifest.MaximumSourceTokens);
        _maxOutputLength = Math.Clamp(
            options.MaximumOutputTokens,
            1,
            _manifest.MaximumOutputTokens);
        _useDecoderCache = options.UseDecoderCache;
        _verifyDecoderCache = options.VerifyDecoderCache;
        _cacheParityGuardMargin = Math.Max(0, options.CacheParityGuardMargin);

        InferenceSession? encoder = null;
        InferenceSession? decoder = null;
        try
        {
            using SessionOptions sessionOptions = OnnxSessionFactory.CreateCpuOptions(options.IntraOpThreads);
            encoder = new InferenceSession(_manifest.GetFilePath(pairDirectory, "encoder"), sessionOptions);
            decoder = new InferenceSession(_manifest.GetFilePath(pairDirectory, "decoder"), sessionOptions);
        }
        catch
        {
            encoder?.Dispose();
            decoder?.Dispose();
            throw;
        }

        _encoderSession = encoder;
        _decoderSession = decoder;
        _tokenizer = new SimpleTokenizer(_manifest.GetFilePath(pairDirectory, "tokenizer"));

        _manifest.ValidateTensorNames(
            _encoderSession.InputMetadata.Keys,
            _encoderSession.OutputMetadata.Keys,
            _decoderSession.InputMetadata.Keys,
            _decoderSession.OutputMetadata.Keys);

        _encoderInputNames = _encoderSession.InputNames.ToArray();
        _encoderOutputNames = _encoderSession.OutputNames.ToArray();
        _encoderInputValues = new OrtValue[_encoderInputNames.Length];
        _decoderInputNames = _decoderSession.InputNames.ToArray();
        _decoderOutputNames = _decoderSession.OutputNames.ToArray();
        _decoderInputValues = new OrtValue[_decoderInputNames.Length];

        _decoderInputIdsIndex = RequireIndex(_decoderInputNames, InputIdsName);
        _encoderAttentionMaskIndex = RequireIndex(_decoderInputNames, EncoderAttentionMaskName);
        _encoderHiddenStatesIndex = RequireIndex(_decoderInputNames, EncoderHiddenStatesName);
        _useCacheBranchIndex = RequireIndex(_decoderInputNames, UseCacheBranchName);
        _logitsOutputIndex = RequireIndex(_decoderOutputNames, LogitsName);
        _cacheBindings = CreateCacheBindings();

        _cacheBranchFalse = OrtValue.CreateTensorValueFromMemory(new[] { false }, new long[] { 1 });
        _cacheBranchTrue = OrtValue.CreateTensorValueFromMemory(new[] { true }, new long[] { 1 });
        _decoderTokenBuffer = new long[_maxOutputLength + 1];
        _decoderPrefixBuffer = new long[_maxOutputLength + 1];
        _decoderInputShapes = new long[_maxOutputLength + 2][];
        for (int length = 1; length < _decoderInputShapes.Length; length++)
            _decoderInputShapes[length] = new long[] { 1, length };
    }

    public string Translate(string text) => TranslateDetailed(text).Text;

    public TranslationResult TranslateDetailed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TranslationResult(
                text,
                Array.Empty<int>(),
                0,
                0,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long tokenizeStarted = Stopwatch.GetTimestamp();
            int[] tokenIds = _tokenizer.Encode(text);
            TimeSpan tokenizeDuration = Stopwatch.GetElapsedTime(tokenizeStarted);
            if (tokenIds.Length > _maxSourceLength)
            {
                throw new TranslationInputException(
                    $"Source cue contains {tokenIds.Length} tokens; maximum is {_maxSourceLength}.");
            }

            long[] inputIds = new long[tokenIds.Length];
            long[] attentionMask = new long[tokenIds.Length];
            for (int index = 0; index < tokenIds.Length; index++)
            {
                inputIds[index] = tokenIds[index];
                attentionMask[index] = 1;
            }

            long[] sourceShape = new long[] { 1, tokenIds.Length };
            using OrtValue inputIdsValue = OrtValue.CreateTensorValueFromMemory(inputIds, sourceShape);
            using OrtValue attentionMaskValue = OrtValue.CreateTensorValueFromMemory(attentionMask, sourceShape);
            SetEncoderInput(InputIdsName, inputIdsValue);
            SetEncoderInput(AttentionMaskName, attentionMaskValue);

            long encoderStarted = Stopwatch.GetTimestamp();
            using IDisposableReadOnlyCollection<OrtValue> encoderOutputs = _encoderSession.Run(
                _runOptions,
                _encoderInputNames,
                _encoderInputValues,
                _encoderOutputNames);
            TimeSpan encoderDuration = Stopwatch.GetElapsedTime(encoderStarted);

            int encoderOutputIndex = RequireIndex(_encoderOutputNames, _manifest.Tensors.EncoderOutputs[0]);
            long decoderStarted = Stopwatch.GetTimestamp();
            int[] outputIds = GreedyDecode(encoderOutputs[encoderOutputIndex], attentionMaskValue);
            TimeSpan decoderDuration = Stopwatch.GetElapsedTime(decoderStarted);

            long detokenizeStarted = Stopwatch.GetTimestamp();
            string translated = _tokenizer.Decode(outputIds);
            TimeSpan detokenizeDuration = Stopwatch.GetElapsedTime(detokenizeStarted);
            return new TranslationResult(
                translated,
                outputIds,
                tokenIds.Length,
                outputIds.Length,
                tokenizeDuration,
                encoderDuration,
                decoderDuration,
                detokenizeDuration);
        }
    }

    public void Warmup() => _ = Translate("Hello");

    private int[] GreedyDecode(OrtValue encoderHiddenStates, OrtValue encoderAttentionMask)
    {
        var generated = new int[_maxOutputLength];
        int generatedCount = 0;
        _decoderTokenBuffer[0] = _tokenizer.PadTokenId;
        _decoderPrefixBuffer[0] = _tokenizer.PadTokenId;
        IDisposableReadOnlyCollection<OrtValue>? initialCacheOutputs = null;
        IDisposableReadOnlyCollection<OrtValue>? latestDecoderOutputs = null;

        try
        {
            for (int step = 0; step < _maxOutputLength; step++)
            {
                bool useCacheBranch = _useDecoderCache && step > 0;
                int decoderSequenceLength = useCacheBranch ? 1 : generatedCount + 1;
                if (useCacheBranch)
                    _decoderTokenBuffer[0] = generated[generatedCount - 1];

                Memory<long> decoderInputMemory = useCacheBranch
                    ? _decoderTokenBuffer.AsMemory(0, 1)
                    : _decoderPrefixBuffer.AsMemory(0, decoderSequenceLength);
                using OrtValue decoderInputIds = OrtValue.CreateTensorValueFromMemory(
                    OrtMemoryInfo.DefaultInstance,
                    decoderInputMemory,
                    _decoderInputShapes[decoderSequenceLength]);

                PopulateDecoderInputs(
                    decoderInputIds,
                    encoderHiddenStates,
                    encoderAttentionMask,
                    initialCacheOutputs,
                    latestDecoderOutputs,
                    useCacheBranch);

                IDisposableReadOnlyCollection<OrtValue> currentOutputs;
                try
                {
                    currentOutputs = _decoderSession.Run(
                        _runOptions,
                        _decoderInputNames,
                        _decoderInputValues,
                        _decoderOutputNames);
                }
                catch (OnnxRuntimeException ex) when (useCacheBranch && latestDecoderOutputs != null)
                {
                    throw new InvalidOperationException(
                        $"Cached decoder step {step} failed. Cache shapes: {DescribeCacheShapes(latestDecoderOutputs)}",
                        ex);
                }

                if (initialCacheOutputs == null)
                {
                    // Cross-attention present tensors are emitted only by the no-cache
                    // branch. Keep this first result collection alive for every step.
                    initialCacheOutputs = currentOutputs;
                    latestDecoderOutputs = currentOutputs;
                }
                else
                {
                    if (!ReferenceEquals(latestDecoderOutputs, initialCacheOutputs))
                        latestDecoderOutputs?.Dispose();
                    latestDecoderOutputs = currentOutputs;
                }

                TokenDecision cachedDecision = ArgMaxLastToken(currentOutputs[_logitsOutputIndex]);
                int bestId = cachedDecision.TokenId;
                if (useCacheBranch &&
                    (_verifyDecoderCache || cachedDecision.Margin <= _cacheParityGuardMargin))
                {
                    TokenDecision fullPrefixDecision = RunUncachedTokenDecision(
                        encoderHiddenStates,
                        encoderAttentionMask,
                        generatedCount + 1);
                    if (cachedDecision.Margin <= _cacheParityGuardMargin)
                    {
                        // Quantized cached/full-prefix branches can reorder nearly
                        // tied logits. Use the baseline full-prefix decision only
                        // for those ambiguous steps and retain the cache state.
                        bestId = fullPrefixDecision.TokenId;
                    }
                    else if (bestId != fullPrefixDecision.TokenId)
                    {
                        throw new InvalidOperationException(
                            $"KV-cached token {bestId} (score {cachedDecision.BestScore:F4}, " +
                            $"margin {cachedDecision.Margin:F4}) differs from uncached token " +
                            $"{fullPrefixDecision.TokenId} (score {fullPrefixDecision.BestScore:F4}, " +
                            $"margin {fullPrefixDecision.Margin:F4}) at decoder step {step}.");
                    }
                }
                if (bestId == _tokenizer.EosTokenId)
                    break;

                generated[generatedCount++] = bestId;
                _decoderPrefixBuffer[generatedCount] = bestId;
            }
        }
        finally
        {
            Array.Clear(_decoderInputValues);
            if (!ReferenceEquals(latestDecoderOutputs, initialCacheOutputs))
                latestDecoderOutputs?.Dispose();
            initialCacheOutputs?.Dispose();
        }

        return generated.AsSpan(0, generatedCount).ToArray();
    }

    private TokenDecision RunUncachedTokenDecision(
        OrtValue encoderHiddenStates,
        OrtValue encoderAttentionMask,
        int prefixLength)
    {
        using OrtValue fullPrefix = OrtValue.CreateTensorValueFromMemory(
            OrtMemoryInfo.DefaultInstance,
            _decoderPrefixBuffer.AsMemory(0, prefixLength),
            _decoderInputShapes[prefixLength]);
        PopulateDecoderInputs(
            fullPrefix,
            encoderHiddenStates,
            encoderAttentionMask,
            null,
            null,
            useCacheBranch: false);
        using IDisposableReadOnlyCollection<OrtValue> outputs = _decoderSession.Run(
            _runOptions,
            _decoderInputNames,
            _decoderInputValues,
            _decoderOutputNames);
        return ArgMaxLastToken(outputs[_logitsOutputIndex]);
    }

    private void PopulateDecoderInputs(
        OrtValue decoderInputIds,
        OrtValue encoderHiddenStates,
        OrtValue encoderAttentionMask,
        IDisposableReadOnlyCollection<OrtValue>? initialCacheOutputs,
        IDisposableReadOnlyCollection<OrtValue>? latestDecoderOutputs,
        bool useCacheBranch)
    {
        _decoderInputValues[_decoderInputIdsIndex] = decoderInputIds;
        _decoderInputValues[_encoderHiddenStatesIndex] = encoderHiddenStates;
        _decoderInputValues[_encoderAttentionMaskIndex] = encoderAttentionMask;
        _decoderInputValues[_useCacheBranchIndex] = useCacheBranch ? _cacheBranchTrue : _cacheBranchFalse;

        foreach (CacheBinding binding in _cacheBindings)
        {
            _decoderInputValues[binding.InputIndex] = useCacheBranch
                ? (binding.IsEncoderCache ? initialCacheOutputs! : latestDecoderOutputs!)[binding.OutputIndex]
                : binding.EmptyValue;
        }
    }

    private List<CacheBinding> CreateCacheBindings()
    {
        var bindings = new List<CacheBinding>();
        for (int inputIndex = 0; inputIndex < _decoderInputNames.Length; inputIndex++)
        {
            string inputName = _decoderInputNames[inputIndex];
            if (!inputName.StartsWith("past_key_values.", StringComparison.Ordinal))
                continue;

            string outputName = "present." + inputName["past_key_values.".Length..];
            int outputIndex = RequireIndex(_decoderOutputNames, outputName);
            NodeMetadata metadata = _decoderSession.InputMetadata[inputName];
            long[] emptyShape = new long[metadata.Dimensions.Length];
            for (int dimension = 0; dimension < emptyShape.Length; dimension++)
            {
                int declared = metadata.Dimensions[dimension];
                emptyShape[dimension] = declared >= 0 ? declared : dimension == 0 ? 1 : 0;
            }

            OrtValue emptyValue = OrtValue.CreateTensorValueFromMemory(Array.Empty<float>(), emptyShape);
            _emptyCacheValues.Add(emptyValue);
            bindings.Add(new CacheBinding(
                inputIndex,
                outputIndex,
                inputName.Contains(".encoder.", StringComparison.Ordinal),
                emptyValue));
        }

        return bindings;
    }

    private static TokenDecision ArgMaxLastToken(OrtValue logits)
    {
        OrtTensorTypeAndShapeInfo shapeInfo = logits.GetTensorTypeAndShape();
        long[] shape = shapeInfo.Shape;
        if (shape.Length != 3 || shape[^1] <= 0 || shape[^1] > int.MaxValue)
            throw new InvalidOperationException($"Unexpected logits shape: [{string.Join(", ", shape)}]");

        int vocabularySize = (int)shape[^1];
        ReadOnlySpan<float> values = logits.GetTensorDataAsSpan<float>();
        ReadOnlySpan<float> lastToken = values[^vocabularySize..];
        int bestIndex = 0;
        float bestScore = float.NegativeInfinity;
        float secondBestScore = float.NegativeInfinity;
        for (int index = 0; index < lastToken.Length; index++)
        {
            if (lastToken[index] > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = lastToken[index];
                bestIndex = index;
            }
            else if (lastToken[index] > secondBestScore)
            {
                secondBestScore = lastToken[index];
            }
        }

        return new TokenDecision(bestIndex, bestScore, secondBestScore);
    }

    private string DescribeCacheShapes(IDisposableReadOnlyCollection<OrtValue> outputs) =>
        string.Join(
            "; ",
            _cacheBindings.Select(binding =>
                $"{_decoderOutputNames[binding.OutputIndex]}=" +
                $"[{string.Join(",", outputs[binding.OutputIndex].GetTensorTypeAndShape().Shape)}]"));

    private void SetEncoderInput(string name, OrtValue value) =>
        _encoderInputValues[RequireIndex(_encoderInputNames, name)] = value;

    private static int RequireIndex(IReadOnlyList<string> names, string expected)
    {
        for (int index = 0; index < names.Count; index++)
        {
            if (string.Equals(names[index], expected, StringComparison.Ordinal))
                return index;
        }

        throw new ModelValidationException($"Required tensor is missing: {expected}");
    }

    public (IReadOnlyDictionary<string, NodeMetadata> EncoderInputs,
            IReadOnlyDictionary<string, NodeMetadata> EncoderOutputs,
            IReadOnlyDictionary<string, NodeMetadata> DecoderInputs,
            IReadOnlyDictionary<string, NodeMetadata> DecoderOutputs) GetMetadata() =>
        (_encoderSession.InputMetadata, _encoderSession.OutputMetadata,
         _decoderSession.InputMetadata, _decoderSession.OutputMetadata);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _cacheBranchFalse.Dispose();
            _cacheBranchTrue.Dispose();
            foreach (OrtValue emptyValue in _emptyCacheValues)
                emptyValue.Dispose();
            _runOptions.Dispose();
            _encoderSession.Dispose();
            _decoderSession.Dispose();
        }
    }

    private readonly record struct CacheBinding(
        int InputIndex,
        int OutputIndex,
        bool IsEncoderCache,
        OrtValue EmptyValue);

    private readonly record struct TokenDecision(int TokenId, float BestScore, float SecondBestScore)
    {
        public float Margin => BestScore - SecondBestScore;
    }
}

public sealed class TranslationInputException : ArgumentException
{
    public TranslationInputException(string message) : base(message) { }
}
