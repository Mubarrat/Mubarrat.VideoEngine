# Mubarrat.VideoEngine

`Mubarrat.VideoEngine` is a high-performance, code-first 2D vector rendering and animation engine for generating H.264 video.

It combines:
- a timeline-driven animation model,
- a custom rasterization pipeline for vector paths,
- and FFmpeg-based encoding with hardware-accelerated fallback selection.

## Key Capabilities

- **Frame generation pipeline** via `IFrameSource`
- **Timeline animation** with easing functions and interpolated state transitions
- **Vector drawing model** with:
  - path fill and stroke rendering,
  - transforms and hierarchical opacity,
  - solid, linear, radial, and conic gradient brushes
- **Shape morphing** through interpolation of geometric primitives
- **Video export** to file or stream
- **Encoder auto-selection** across available H.264 backends (`h264_qsv`, `h264_nvenc`, `h264_vaapi`, `h264_amf`, `h264_videotoolbox`, `libx264`)
- **OpenType parser foundation** (cmap, glyf, GSUB, GPOS, metrics, and related tables)

## Project Status

This project is actively evolving.

Current strengths:
- strong low-level rendering and animation core,
- robust FFmpeg encoding integration,
- extensive OpenType table parsing infrastructure.

Planned expansion areas:
- text shaping engine,
- advanced text layout/handling,
- semantic animation elements.

## Target Framework

- **.NET 11**

## Installation

Clone the repository and build:

1. Restore dependencies
2. Build the project

The project depends on `FFmpeg.AutoGen` and expects FFmpeg binaries in the configured output path (`libs/ffmpeg/*.dll` copied to output).

## Quick Start (Conceptual)

1. Create a `Video` instance.
2. Set width, height, FPS, frame count.
3. Provide an `IFrameSource` implementation (for example, `TimelineSource`).
4. Export to a file path or output stream.

## Architecture Overview

- **Core primitives**: `Point`, `Vector2D`, `Matrix2D`, `Rect`, `Color32`
- **Drawing system**: `Drawing`, `PathDrawing`, `GroupDrawing`, `DrawingContext`
- **Path system**: `PathBuilder`, immutable `Path2D`/`Subpath`/`Edge`
- **Animation system**: `TimelineSource`, `TimelineLayer`, `TimelineCommand`, easing + lerpers
- **Encoding**: `VideoEncoder` base + backend-specific encoder implementations + `EncoderFactory`
- **Typography foundation**: OpenType reader and table parsers

## Design Goals

- deterministic, code-driven rendering
- efficient frame throughput
- portable encoding strategy with graceful fallback
- extensibility for advanced text and semantic animation pipelines

## Contributing

Contributions are welcome. For significant feature work, open an issue first to discuss scope and approach.

## License

[MIT License](LICENSE.md)
