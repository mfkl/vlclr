# Offline subtitle translator

`SubtitleTranslator` is a CPU-first VLC 4 text-renderer plugin. It translates
subtitle cues offline with a quantized Marian/OPUS-MT ONNX model and renders a
compact ImageSharp subpicture region. Translation failures always leave the
original cue available for rendering.

## Model setup

The 225.6 MiB model bundle is intentionally not stored in Git. Download and
verify it against the committed manifest:

```powershell
pwsh samples/SubtitleTranslator/download-model.ps1
```

The manifest records exact file sizes, SHA-256 hashes, tensor names, language
pair, model source, and license. Tests never download the model implicitly;
pass its directory explicitly.

The managed and native ONNX Runtime packages are pinned together at version
1.27.1. Do not deploy an `onnxruntime.dll` from a different release; the plugin
resolver rejects version mismatches before session creation.

## Build and validate

```powershell
dotnet build samples/SubtitleTranslator -c Release
dotnet run --project tests/TranslatorTest -c Release -- samples/SubtitleTranslator/models/opus-mt-en-fr
dotnet publish samples/SubtitleTranslator -c Release -r win-x64
```

## Deploy

```powershell
pwsh samples/SubtitleTranslator/deploy.ps1 -VlcDirectory vlc-binaries/vlc-4.0.0-dev
```

The script validates the model and creates this layout:

```text
vlc-4.0.0-dev/
|-- onnxruntime.dll
|-- onnxruntime_providers_shared.dll
|-- models/opus-mt-en-fr/
|   |-- model-manifest.json
|   |-- encoder_model_quantized.onnx
|   |-- decoder_model_merged_quantized.onnx
|   `-- tokenizer.json
`-- plugins/spu/libdotnet_subtitle_translator_plugin.dll
```

The runtime and model are deliberately outside VLC's plugin tree. Regenerate
VLC's plugin cache after deployment.

Model: `onnx-community/opus-mt-en-fr`, Apache-2.0. ONNX Runtime is distributed
under the MIT license.
