using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SubtitleTranslator;

/// <summary>
/// ONNX-based translator using MarianMT encoder-decoder architecture.
/// Loads INT8 quantized encoder + merged decoder models.
/// </summary>
public sealed class OnnxTranslator : IDisposable
{
    private readonly InferenceSession _encoderSession;
    private readonly InferenceSession _decoderSession;
    private readonly SimpleTokenizer _tokenizer;
    private readonly int _maxLength;
    private readonly int _maxSourceLength;
    private readonly ModelManifest _manifest;

    // Decoder input metadata (discovered at load time)
    private readonly HashSet<string> _decoderInputNames;
    private readonly bool _hasCacheBranch;
    private readonly List<(string Name, int[] Shape)> _cacheInputs;

    public SimpleTokenizer Tokenizer => _tokenizer;
    public ModelManifest Manifest => _manifest;

    public OnnxTranslator(
        string modelDir,
        string sourceLang,
        string targetLang,
        int maxLength = 128,
        int intraOpThreads = 4,
        bool verifyModelHashes = true)
    {
        string pairDir = ModelManifest.ResolveModelDirectory(modelDir, sourceLang, targetLang);
        _manifest = ModelManifest.LoadAndValidate(pairDir, sourceLang, targetLang, verifyModelHashes);

        _maxLength = Math.Clamp(maxLength, 1, _manifest.MaximumOutputTokens);
        _maxSourceLength = _manifest.MaximumSourceTokens;

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Clamp(intraOpThreads, 1, Environment.ProcessorCount)
        };

        // Load encoder and decoder sessions
        _encoderSession = new InferenceSession(
            _manifest.GetFilePath(pairDir, "encoder"), options);
        _decoderSession = new InferenceSession(
            _manifest.GetFilePath(pairDir, "decoder"), options);

        // Load tokenizer
        _tokenizer = new SimpleTokenizer(_manifest.GetFilePath(pairDir, "tokenizer"));

        _manifest.ValidateTensorNames(
            _encoderSession.InputMetadata.Keys,
            _encoderSession.OutputMetadata.Keys,
            _decoderSession.InputMetadata.Keys,
            _decoderSession.OutputMetadata.Keys);

        // Discover decoder input structure
        _decoderInputNames = new HashSet<string>(_decoderSession.InputMetadata.Keys);
        _hasCacheBranch = _decoderInputNames.Contains("use_cache_branch");
        _cacheInputs = DiscoverCacheInputs();
    }

    /// <summary>
    /// Translate text from source to target language.
    /// </summary>
    public string Translate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 1. Tokenize
        var inputIds = _tokenizer.Encode(text);
        if (inputIds.Length > _maxSourceLength)
        {
            throw new ArgumentException(
                $"Source cue contains {inputIds.Length} tokens; maximum is {_maxSourceLength}.",
                nameof(text));
        }

        // 2. Run encoder
        var (encoderOutput, encoderDims) = RunEncoder(inputIds);

        // 3. Greedy decode
        var outputIds = GreedyDecode(encoderOutput, encoderDims, inputIds.Length);

        // 4. Decode tokens to text
        return _tokenizer.Decode(outputIds);
    }

    /// <summary>
    /// Pre-warm the model with a dummy translation to avoid first-call latency.
    /// </summary>
    public void Warmup()
    {
        Translate("Hello");
    }

    private (float[] Data, int[] Dims) RunEncoder(int[] inputIds)
    {
        int seqLen = inputIds.Length;

        // Create input tensors
        var inputIdTensor = new DenseTensor<long>(new[] { 1, seqLen });
        var attentionMask = new DenseTensor<long>(new[] { 1, seqLen });

        for (int i = 0; i < seqLen; i++)
        {
            inputIdTensor[0, i] = inputIds[i];
            attentionMask[0, i] = 1;
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        };

        using var results = _encoderSession.Run(inputs);
        var output = results.First().AsTensor<float>();
        var dims = output.Dimensions.ToArray();

        // Copy data (results are disposed after Run)
        var data = new float[output.Length];
        int idx = 0;
        foreach (var val in output)
            data[idx++] = val;

        return (data, dims);
    }

    private int[] GreedyDecode(float[] encoderOutput, int[] encoderDims, int sourceLength)
    {
        int hiddenDim = encoderDims[2];
        var decoderInputIds = new List<int> { _tokenizer.PadTokenId };

        for (int step = 0; step < _maxLength; step++)
        {
            var inputs = CreateDecoderInputs(decoderInputIds, encoderOutput, encoderDims, sourceLength);

            using var results = _decoderSession.Run(inputs);

            // Find logits output
            var logitsResult = results.FirstOrDefault(r => r.Name == "logits")
                ?? results.First();
            var logits = logitsResult.AsTensor<float>();

            // Get vocab size and last position
            int vocabSize = logits.Dimensions[2];
            int lastPos = decoderInputIds.Count - 1;

            // Argmax over vocabulary for last position
            int bestId = 0;
            float bestScore = float.MinValue;
            for (int v = 0; v < vocabSize; v++)
            {
                float score = logits[0, lastPos, v];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = v;
                }
            }

            if (bestId == _tokenizer.EosTokenId)
                break;

            decoderInputIds.Add(bestId);
        }

        // Skip the initial pad/BOS token
        return decoderInputIds.Skip(1).ToArray();
    }

    private List<NamedOnnxValue> CreateDecoderInputs(
        List<int> decoderInputIds, float[] encoderOutput, int[] encoderDims, int sourceLength)
    {
        int decoderSeqLen = decoderInputIds.Count;
        int hiddenDim = encoderDims[2];

        // Decoder input_ids
        var inputIdTensor = new DenseTensor<long>(new[] { 1, decoderSeqLen });
        for (int i = 0; i < decoderSeqLen; i++)
            inputIdTensor[0, i] = decoderInputIds[i];

        // Encoder hidden states
        var encoderHiddenTensor = new DenseTensor<float>(
            encoderOutput, encoderDims);

        // Encoder attention mask
        var encoderMask = new DenseTensor<long>(new[] { 1, sourceLength });
        for (int i = 0; i < sourceLength; i++)
            encoderMask[0, i] = 1;

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdTensor),
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenTensor),
            NamedOnnxValue.CreateFromTensor("encoder_attention_mask", encoderMask)
        };

        // Handle cache branch if the merged model requires it
        if (_hasCacheBranch)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("use_cache_branch",
                new DenseTensor<bool>(new[] { false }, new[] { 1 })));
        }

        // Add empty past_key_values tensors if required
        foreach (var (name, shape) in _cacheInputs)
        {
            // Create empty tensor with seq_len dimension = 0
            var emptyTensor = new DenseTensor<float>(shape);
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, emptyTensor));
        }

        return inputs;
    }

    /// <summary>
    /// Discover past_key_values inputs from decoder metadata.
    /// For the no-cache path, we need to provide empty tensors.
    /// </summary>
    private List<(string Name, int[] Shape)> DiscoverCacheInputs()
    {
        var cacheInputs = new List<(string, int[])>();

        foreach (var (name, meta) in _decoderSession.InputMetadata)
        {
            if (!name.StartsWith("past_key_values"))
                continue;

            // Build shape with 0 for the sequence dimension
            // Typical shape: [batch_size, num_heads, past_seq_len, head_dim]
            var shape = meta.Dimensions.ToArray();
            for (int i = 0; i < shape.Length; i++)
            {
                if (shape[i] == -1) // Dynamic dimension
                {
                    // batch_size = 1, past_seq_len = 0, others from metadata
                    shape[i] = (i == 0) ? 1 : 0;
                }
            }

            cacheInputs.Add((name, shape));
        }

        return cacheInputs;
    }

    /// <summary>
    /// Get encoder session input/output metadata (for debugging/checkpoints).
    /// </summary>
    public (IReadOnlyDictionary<string, NodeMetadata> EncoderInputs,
            IReadOnlyDictionary<string, NodeMetadata> EncoderOutputs,
            IReadOnlyDictionary<string, NodeMetadata> DecoderInputs,
            IReadOnlyDictionary<string, NodeMetadata> DecoderOutputs) GetMetadata()
    {
        return (_encoderSession.InputMetadata, _encoderSession.OutputMetadata,
                _decoderSession.InputMetadata, _decoderSession.OutputMetadata);
    }

    public void Dispose()
    {
        _encoderSession.Dispose();
        _decoderSession.Dispose();
    }
}
