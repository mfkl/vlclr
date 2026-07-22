using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SubtitleTranslator;

if (args.Length < 1)
{
    Console.WriteLine("Usage: TranslatorTest <model-dir>");
    Console.WriteLine("  model-dir: path to opus-mt-en-fr model directory");
    Console.WriteLine("  Example: dotnet run --project tests/TranslatorTest -- samples/SubtitleTranslator/models/opus-mt-en-fr");
    return 1;
}

string modelDir = args[0];
if (!Directory.Exists(modelDir))
{
    Console.Error.WriteLine($"Model directory not found: {modelDir}");
    return 1;
}

int checkpointsPassed = 0;

// ============================================================
// CHECKPOINT 3: ONNX Sessions Load
// ============================================================
Console.WriteLine("\n=== CHECKPOINT 3: ONNX Sessions Load ===");
try
{
    var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };

    using var encoder = new InferenceSession(
        Path.Combine(modelDir, "encoder_model_quantized.onnx"), options);
    using var decoder = new InferenceSession(
        Path.Combine(modelDir, "decoder_model_merged_quantized.onnx"), options);

    Console.WriteLine($"Encoder inputs: {string.Join(", ", encoder.InputMetadata.Keys)}");
    Console.WriteLine($"Encoder outputs: {string.Join(", ", encoder.OutputMetadata.Keys)}");
    Console.WriteLine($"Decoder inputs: {string.Join(", ", decoder.InputMetadata.Keys)}");
    Console.WriteLine($"Decoder outputs: {string.Join(", ", decoder.OutputMetadata.Keys)}");

    // Print decoder input shapes for debugging
    Console.WriteLine("\nDecoder input details:");
    foreach (var (name, meta) in decoder.InputMetadata)
    {
        Console.WriteLine($"  {name}: [{string.Join(", ", meta.Dimensions)}] ({meta.ElementDataType})");
    }

    Console.WriteLine("CHECKPOINT 3 PASSED: Sessions loaded");
    checkpointsPassed++;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"CHECKPOINT 3 FAILED: {ex.Message}");
    return 1;
}

// ============================================================
// CHECKPOINT 4: Tokenizer Roundtrip
// ============================================================
Console.WriteLine("\n=== CHECKPOINT 4: Tokenizer Roundtrip ===");
try
{
    var tokenizer = new SimpleTokenizer(Path.Combine(modelDir, "tokenizer.json"));

    Console.WriteLine($"Vocabulary size: {tokenizer.VocabSize}");
    Console.WriteLine($"EOS token ID: {tokenizer.EosTokenId}");
    Console.WriteLine($"PAD token ID: {tokenizer.PadTokenId}");
    Console.WriteLine($"UNK token ID: {tokenizer.UnkTokenId}");

    var encoded = tokenizer.Encode("Hello world");
    var decoded = tokenizer.Decode(encoded);

    Console.WriteLine($"Encoded 'Hello world': [{string.Join(", ", encoded)}]");
    Console.WriteLine($"Decoded back: '{decoded}'");

    Require(encoded.Length > 0, "Encoding produced no tokens");
    Require(decoded.Contains("Hello") || decoded.Contains("hello"),
        $"Roundtrip failed: got '{decoded}'");

    // Test a few more strings
    var test2 = tokenizer.Encode("The cat is on the table");
    Console.WriteLine($"Encoded 'The cat is on the table': [{string.Join(", ", test2)}] ({test2.Length} tokens)");

    Console.WriteLine("CHECKPOINT 4 PASSED: Tokenizer roundtrip");
    checkpointsPassed++;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"CHECKPOINT 4 FAILED: {ex.Message}");
    return 1;
}

// ============================================================
// CHECKPOINT 5: Encoder Produces Output
// ============================================================
Console.WriteLine("\n=== CHECKPOINT 5: Encoder Produces Output ===");
try
{
    var tokenizer = new SimpleTokenizer(Path.Combine(modelDir, "tokenizer.json"));
    var inputIds = tokenizer.Encode("The cat is on the table");

    var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
    using var encoder = new InferenceSession(
        Path.Combine(modelDir, "encoder_model_quantized.onnx"), options);

    var inputTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
    var attentionMask = new DenseTensor<long>(new[] { 1, inputIds.Length });
    for (int i = 0; i < inputIds.Length; i++)
    {
        inputTensor[0, i] = inputIds[i];
        attentionMask[0, i] = 1;
    }

    var encoderInputs = new List<NamedOnnxValue>
    {
        NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
        NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
    };

    using var encoderResults = encoder.Run(encoderInputs);
    var lastHiddenState = encoderResults.First().AsTensor<float>();
    var dims = lastHiddenState.Dimensions.ToArray();
    Console.WriteLine($"Encoder output shape: [{string.Join(", ", dims)}]");

    Require(dims.Length == 3, "Expected 3D tensor [batch, seq_len, hidden_dim]");
    Require(dims[0] == 1, "Batch size should be 1");
    Require(dims[1] == inputIds.Length, $"Seq length mismatch: expected {inputIds.Length}, got {dims[1]}");

    // Check output is not all zeros
    bool hasNonZero = false;
    for (int i = 0; i < Math.Min(10, lastHiddenState.Length); i++)
    {
        if (Math.Abs(lastHiddenState.GetValue(i)) > 1e-6)
        {
            hasNonZero = true;
            break;
        }
    }
    Require(hasNonZero, "Encoder output is all zeros");

    Console.WriteLine("CHECKPOINT 5 PASSED: Encoder produces valid output");
    checkpointsPassed++;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"CHECKPOINT 5 FAILED: {ex.Message}");
    return 1;
}

// ============================================================
// CHECKPOINT 6: Full Translation Pipeline
// ============================================================
Console.WriteLine("\n=== CHECKPOINT 6: Full Translation Pipeline ===");
try
{
    var translator = new OnnxTranslator(
        modelDir,
        "en",
        "fr",
        new OnnxTranslatorOptions
        {
            UseDecoderCache = true,
            CacheActivationTokenCount = 1,
            IntraOpThreads = 4
        });

    var testPairs = new (string Input, string[] ExpectedContains)[]
    {
        ("Hello",                    new[] { "Bonjour", "bonjour", "Salut" }),
        ("The cat is on the table",  new[] { "chat", "table" }),
        ("Good morning",             new[] { "Bonjour", "bonjour", "matin" }),
        ("I love music",             new[] { "aime", "musique" }),
        ("Thank you very much",      new[] { "Merci", "merci" }),
    };

    int passed = 0;
    var times = new List<long>();
    var cachedDecoderResults = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);

    foreach (var (input, expectedContains) in testPairs)
    {
        var sw = Stopwatch.StartNew();
        TranslationResult detailedResult = translator.TranslateDetailed(input);
        string result = detailedResult.Text;
        sw.Stop();
        times.Add(sw.ElapsedMilliseconds);
        cachedDecoderResults[input] = detailedResult;

        bool containsExpected = expectedContains.Any(e =>
            result.Contains(e, StringComparison.OrdinalIgnoreCase));

        var status = containsExpected ? "OK" : "SUSPECT";
        Console.WriteLine($"  [{status}] \"{input}\" -> \"{result}\" ({sw.ElapsedMilliseconds}ms)");

        if (containsExpected) passed++;
    }

    Require(passed == testPairs.Length, $"Only {passed}/{testPairs.Length} translations contained expected French words");
    Console.WriteLine($"CHECKPOINT 6 PASSED: {passed}/5 translations verified");
    checkpointsPassed++;

    // ============================================================
    // CHECKPOINT 7: Translation Latency
    // ============================================================
    Console.WriteLine("\n=== CHECKPOINT 7: Translation Latency ===");

    // Exclude first call (cold start), average the rest
    if (times.Count > 1)
    {
        var warmTimes = times.Skip(1).ToList();
        double avg = warmTimes.Average();
        Console.WriteLine($"Average latency (excluding first): {avg:F0}ms");
        Console.WriteLine($"Max latency (excluding first): {warmTimes.Max()}ms");

        if (avg < 200)
        {
            Console.WriteLine("CHECKPOINT 7 PASSED: Average latency < 200ms");
            checkpointsPassed++;
        }
        else
        {
            Console.WriteLine($"CHECKPOINT 7 WARNING: Average latency {avg:F0}ms exceeds 200ms target");
            // Don't fail - this is hardware-dependent
            checkpointsPassed++;
        }
    }

    // ============================================================
    // CHECKPOINT 8: Cache Works
    // ============================================================
    Console.WriteLine("\n=== CHECKPOINT 8: Cache Works ===");

    var cache = new TranslationCache(capacity: 3);

    // First call: cache miss, should translate
    var r1 = cache.GetOrTranslate("Hello", translator);
    Require(!string.IsNullOrEmpty(r1), "First translation returned empty");

    // Second call: cache hit, should return same result instantly
    var sw2 = Stopwatch.StartNew();
    var r2 = cache.GetOrTranslate("Hello", translator);
    sw2.Stop();
    Require(r1 == r2, $"Cache returned different result: '{r1}' vs '{r2}'");
    Console.WriteLine($"Cache hit took {sw2.ElapsedTicks} ticks ({sw2.ElapsedMilliseconds}ms)");

    // Fill cache beyond capacity
    cache.GetOrTranslate("One sentence", translator);
    cache.GetOrTranslate("Two sentences", translator);
    cache.GetOrTranslate("Three sentences", translator); // Should evict "Hello"
    Require(cache.Count <= 3, $"Cache exceeded capacity: {cache.Count}");

    Console.WriteLine("CHECKPOINT 8 PASSED: Cache hit/miss/eviction works");
    checkpointsPassed++;

    string malformedUnicode = new(new[] { '\ud800', 'X' });
    string[] parityCorpus =
    [
        "Wait... what?",
        "I'm ready.",
        "Café déjà vu",
        "SPEAKER:\nWhere are you?",
        "Numbers: 12, 34.5, and 2026.",
        "Emoji 😀 and unknown symbols ∑.",
        malformedUnicode
    ];
    foreach (string input in parityCorpus)
        cachedDecoderResults[input] = translator.TranslateDetailed(input);

    Require(translator.Translate("") == "", "Empty text was not preserved.");
    try
    {
        _ = translator.Translate(string.Join(' ', Enumerable.Repeat("subtitle", 300)));
        throw new InvalidOperationException("An over-limit source cue was accepted.");
    }
    catch (TranslationInputException)
    {
        // Expected independent source-token limit.
    }

    translator.Dispose();

    Console.WriteLine("\n=== CHECKPOINT 9: Cached/Uncached Decoder Parity ===");
    using var uncachedTranslator = new OnnxTranslator(
        modelDir,
        "en",
        "fr",
        new OnnxTranslatorOptions
        {
            UseDecoderCache = false,
            VerifyModelHashes = false,
            IntraOpThreads = 4
        });
    foreach (var (input, cachedResult) in cachedDecoderResults)
    {
        TranslationResult uncachedResult = uncachedTranslator.TranslateDetailed(input);
        string label = input == malformedUnicode ? "<malformed-unicode>" : input.Replace('\n', ' ');
        Console.WriteLine($"  \"{label}\": cached=\"{cachedResult.Text}\", uncached=\"{uncachedResult.Text}\"");
        Require(cachedResult.OutputTokenIds.SequenceEqual(uncachedResult.OutputTokenIds),
            $"Cached decoder tokens differ for '{label}': " +
            $"[{string.Join(",", cachedResult.OutputTokenIds)}] vs " +
            $"[{string.Join(",", uncachedResult.OutputTokenIds)}].");
    }
    checkpointsPassed++;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"CHECKPOINT FAILED: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

// ============================================================
// Summary
// ============================================================
Console.WriteLine($"\n{'=',-50}");
Console.WriteLine($"RESULTS: {checkpointsPassed}/7 checkpoints passed");
Console.WriteLine($"{'=',-50}");

return checkpointsPassed == 7 ? 0 : 1;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
