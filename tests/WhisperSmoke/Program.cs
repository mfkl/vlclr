using Whisper.net;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: WhisperSmoke <ggml-model.bin> <16-kHz-mono-pcm.wav>");
    return 1;
}

string modelPath = Path.GetFullPath(args[0]);
string audioPath = Path.GetFullPath(args[1]);
if (!File.Exists(modelPath) || !File.Exists(audioPath))
{
    Console.Error.WriteLine($"Missing model or audio file: model={modelPath}, audio={audioPath}");
    return 2;
}

using var factory = WhisperFactory.FromPath(modelPath);
using var processor = factory.CreateBuilder()
    .WithLanguageDetection()
    .WithTranslate()
    .WithThreads(Math.Min(4, Environment.ProcessorCount))
    .Build();

using var audio = File.OpenRead(audioPath);
int segmentCount = 0;
await foreach (SegmentData segment in processor.ProcessAsync(audio))
{
    string text = segment.Text.Trim();
    if (text.Length == 0)
        continue;

    segmentCount++;
    Console.WriteLine($"[{segment.Start:c} --> {segment.End:c}] {text}");
}

Console.WriteLine($"Whisper segments: {segmentCount}");
return segmentCount > 0 ? 0 : 3;
