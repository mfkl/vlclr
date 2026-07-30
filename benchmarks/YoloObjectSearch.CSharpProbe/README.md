# Pure-C# YOLO GPU Probe

This probe validates the object-search GPU path without any authored C or C++:

1. Select the Intel DXGI adapter from C#.
2. Create a decoder-style 1920 x 1080 NV12 D3D11 texture array.
3. Select array slice 3.
4. GPU-scale and center-letterbox it to an owned 416 x 416 NV12 texture with
   `ID3D11VideoProcessor`.
5. Create an OpenVINO D3D11 Remote Context through fixed-signature C# P/Invoke
   declarations for the public variadic C API.
6. Bind the NV12 Y and UV planes as GPU surface Remote Tensors.
7. Compile and execute YOLOX-Nano or YOLOX-Tiny on the Intel GPU.
8. Decode the 301,665-value output through the C# grid/NMS implementation.

No staging texture, `Map`, CPU pixel conversion, or CPU inference is used.

## Build

```powershell
dotnet build `
  benchmarks\YoloObjectSearch.CSharpProbe\YoloObjectSearch.CSharpProbe.csproj `
  -c Release
```

## Run

```powershell
$openVinoRoot = 'C:\openvino'
$env:OPENVINO_RUNTIME_DIR = `
  "$openVinoRoot\runtime\bin\intel64\Release"

dotnet run `
  --project benchmarks\YoloObjectSearch.CSharpProbe\YoloObjectSearch.CSharpProbe.csproj `
  -c Release `
  --no-build `
  -- C:\models\yolox_nano.onnx
```

The OpenVINO archive must retain its standard
`runtime\3rdparty\tbb\bin\tbb12.dll` layout.

## Validated reference output

On the Dell XPS 13 9310 / Intel Iris Xe:

```text
implementation_language=CSharp
authored_cpp=0
source_texture=NV12,1920x1080,array_slice=3
inference_texture=NV12,416x416
gpu_resize_and_letterbox=passed
output_elements=301665
postprocess_median_ms=0.029
postprocess_p95_ms=0.088
pure_csharp_yolox_inference=passed
pure_csharp_remote_tensor_probe=passed
```

Observed Nano inference varied by run; the live VLC path was typically 6-10 ms
after warm-up.
