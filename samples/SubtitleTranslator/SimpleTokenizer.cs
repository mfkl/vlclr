using System.Text;
using System.Text.Json;

namespace SubtitleTranslator;

/// <summary>
/// SentencePiece Unigram tokenizer that parses HuggingFace tokenizer.json format.
/// Uses Viterbi algorithm for optimal segmentation.
/// </summary>
public sealed class SimpleTokenizer
{
    private readonly Dictionary<string, (int Id, float Score)> _vocab;
    private readonly Dictionary<int, string> _reverseVocab;
    private int _maxTokenLength;

    // Special token IDs
    public int EosTokenId { get; }  // </s>
    public int PadTokenId { get; }  // <pad>
    public int UnkTokenId { get; }  // <unk>
    public float UnkScore { get; }

    public int VocabSize => _vocab.Count;

    // SentencePiece metaspace character
    private const char Metaspace = '\u2581'; // ▁
    private const string MetaspaceStr = "\u2581";

    public SimpleTokenizer(string tokenizerJsonPath)
    {
        _vocab = new Dictionary<string, (int, float)>();
        _reverseVocab = new Dictionary<int, string>();
        _maxTokenLength = 0;

        LoadTokenizerJson(tokenizerJsonPath);

        // Discover special tokens
        EosTokenId = _vocab.TryGetValue("</s>", out var eos) ? eos.Id : 0;
        PadTokenId = _vocab.TryGetValue("<pad>", out var pad) ? pad.Id : _reverseVocab.Keys.Max();
        UnkTokenId = _vocab.TryGetValue("<unk>", out var unk) ? unk.Id : 1;
        UnkScore = _vocab.TryGetValue("<unk>", out var unkEntry) ? unkEntry.Score : -100f;
    }

    private void LoadTokenizerJson(string path)
    {
        var json = File.ReadAllBytes(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("model", out var model))
            throw new InvalidOperationException("tokenizer.json missing 'model' section");

        // Parse Unigram vocabulary: array of [token_string, score]
        if (!model.TryGetProperty("vocab", out var vocab))
            throw new InvalidOperationException("tokenizer.json model missing 'vocab'");

        int id = 0;
        foreach (var entry in vocab.EnumerateArray())
        {
            // Each entry is [token_string, score] or could be just a string
            string token;
            float score;

            if (entry.ValueKind == JsonValueKind.Array)
            {
                var enumerator = entry.EnumerateArray();
                enumerator.MoveNext();
                token = enumerator.Current.GetString()!;
                enumerator.MoveNext();
                score = enumerator.Current.GetSingle();
            }
            else
            {
                token = entry.GetString()!;
                score = 0f;
            }

            _vocab[token] = (id, score);
            _reverseVocab[id] = token;

            if (token.Length > _maxTokenLength)
                _maxTokenLength = token.Length;

            id++;
        }

        // Also register added_tokens
        if (root.TryGetProperty("added_tokens", out var addedTokens))
        {
            foreach (var tokenObj in addedTokens.EnumerateArray())
            {
                if (tokenObj.TryGetProperty("content", out var content) &&
                    tokenObj.TryGetProperty("id", out var tokenId))
                {
                    string tokenStr = content.GetString()!;
                    int tid = tokenId.GetInt32();
                    _vocab.TryAdd(tokenStr, (tid, 0f));
                    _reverseVocab.TryAdd(tid, tokenStr);
                }
            }
        }
    }

    /// <summary>
    /// Encode text to token IDs using Unigram Viterbi segmentation. Appends EOS token.
    /// </summary>
    public int[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [EosTokenId];

        // SentencePiece normalization: prepend metaspace, replace spaces with metaspace
        var normalized = MetaspaceStr + text.Replace(" ", MetaspaceStr);

        // Viterbi segmentation
        var tokens = ViterbiSegment(normalized);

        // Map to IDs and append EOS
        var ids = new List<int>(tokens.Count + 1);
        foreach (var token in tokens)
        {
            if (_vocab.TryGetValue(token, out var entry))
                ids.Add(entry.Id);
            else
                ids.Add(UnkTokenId);
        }
        ids.Add(EosTokenId);

        return ids.ToArray();
    }

    /// <summary>
    /// Decode token IDs back to text. Strips special tokens.
    /// </summary>
    public string Decode(int[] ids)
    {
        var sb = new StringBuilder();
        foreach (int id in ids)
        {
            if (id == EosTokenId || id == PadTokenId)
                continue;

            if (_reverseVocab.TryGetValue(id, out string? token))
                sb.Append(token);
        }

        // Replace metaspace with space and trim leading space
        string result = sb.ToString().Replace(Metaspace, ' ');
        return result.TrimStart();
    }

    /// <summary>
    /// Viterbi algorithm for optimal Unigram segmentation.
    /// Finds the segmentation that maximizes the sum of token scores.
    /// </summary>
    private List<string> ViterbiSegment(string text)
    {
        int n = text.Length;
        if (n == 0)
            return [];

        // best[i] = best score to reach position i
        // prev[i] = the start position of the best token ending at position i
        var best = new float[n + 1];
        var prev = new int[n + 1];

        Array.Fill(best, float.NegativeInfinity);
        best[0] = 0;
        Array.Fill(prev, -1);

        for (int end = 1; end <= n; end++)
        {
            // Try all possible token lengths ending at 'end'
            int maxLen = Math.Min(end, _maxTokenLength);
            for (int len = 1; len <= maxLen; len++)
            {
                int start = end - len;
                if (float.IsNegativeInfinity(best[start]))
                    continue;

                string substr = text.Substring(start, len);
                float score;

                if (_vocab.TryGetValue(substr, out var entry))
                {
                    score = entry.Score;
                }
                else if (len == 1)
                {
                    // Single character fallback to UNK
                    score = UnkScore;
                }
                else
                {
                    continue; // Multi-char substring not in vocab, skip
                }

                float totalScore = best[start] + score;
                if (totalScore > best[end])
                {
                    best[end] = totalScore;
                    prev[end] = start;
                }
            }
        }

        // Backtrack to reconstruct tokens
        var tokens = new List<string>();
        int pos = n;
        while (pos > 0)
        {
            int start = prev[pos];
            if (start < 0)
            {
                // Unreachable position, emit single character as UNK
                tokens.Add(text.Substring(pos - 1, 1));
                pos--;
            }
            else
            {
                tokens.Add(text.Substring(start, pos - start));
                pos = start;
            }
        }

        tokens.Reverse();
        return tokens;
    }
}
