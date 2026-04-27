# Mubarrat.VideoEngine

`Mubarrat.VideoEngine` is a high-performance, code-first 2D vector rendering and animation engine for generating H.264 video.

It is built for deterministic, scriptable visuals with a focus on speed, predictable output, and programmatic control over every pixel.

## Purpose

Create animation-first videos from code, with a rendering and layout model tailored for technical visuals, math content, and precise motion design.

## Motivation

Most video tools favor timeline GUIs. This engine favors code: testable, reproducible, and automatable rendering that scales to large frame counts.

## Highlights

- Timeline-driven animation with easing and interpolated state transitions
- Vector drawing pipeline with path fills, strokes, transforms, and gradients
- Shape and drawing morphing between frames
- Layout framework objects (measure/arrange) with panels and borders
- Text rendering with OpenType shaping and fallback font chains
- LaTeX math rendering
- HarfBuzz shaping path with legacy OpenType fallback
- Video export to file or stream
- Encoder auto-selection across available H.264 backends

## Efficiency & Performance

- Multi-threaded frame rendering with chunked buffers
- Custom rasterization pipeline for vector paths
- Hardware-accelerated encoder selection with graceful fallback

## Roadmap

- Advanced text layout and shaping features
- Semantic animation primitives
- 3D rendering exploration

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

## What's Included

- **Core primitives**: `Point`, `Vector2D`, `Matrix2D`, `Rect`, `Color32`
- **Drawing system**: `Drawing`, `PathDrawing`, `GroupDrawing`, `DrawingContext`
- **Path system**: `PathBuilder`, immutable `Path2D`/`Subpath`/`Edge`
- **Layout object model**: `FrameworkObject` with measure/arrange + layout panels and borders
- **Text system**: `TextBlock`, `LatexBlock`, OpenType shaping (`OpenTypeTextShaper`) with HarfBuzz integration
- **Animation system**: `TimelineSource`, `TimelineLayer`, `TimelineCommand`, easing + lerpers
- **Encoding**: `VideoEncoder` base + backend-specific encoder implementations + `EncoderFactory`
- **Typography foundation**: OpenType reader and table parsers

## Contributing

Contributions are welcome. For significant feature work, open an issue first to discuss scope and approach.

## How It Differs From Other Tools

Compared to tools like Manim, After Effects, Premiere Pro, and DaVinci Resolve, this engine is built for code-first workflows and deterministic output:

- **Ease of iteration**: programmatic scenes can be refactored, tested, and generated at scale without manual timeline edits.
- **Performance**: multi-threaded frame generation with a custom rasterization pipeline and hardware-accelerated encoder selection.
- **Maintainability**: versioned, code-driven animations are easier to review, diff, and automate than binary project files.
- **Precision**: exact control over geometry, transforms, and text shaping for technical visuals and math-heavy content.
- **Automation-first**: designed for scripted pipelines and repeatable render output.

### Side-by-Side Comparison

| Feature/Aspect         | Mubarrat.VideoEngine | Manim         | After Effects / Premiere Pro / DaVinci Resolve |
|-----------------------|---------------------|---------------|-----------------------------------------------|
| Workflow              | Code-first, .NET    | Code-first, Python | GUI timeline, keyframes, effects             |
| Output Determinism    | Fully deterministic | Deterministic | Not guaranteed (manual edits, plugins)        |
| Performance           | Multi-threaded, hardware encoding | Single-threaded (mostly) | GPU-accelerated, but GUI overhead           |
| Automation            | Full (C#/F#)        | Full (Python) | Limited (scripting, but not core pipeline)    |
| Text/Math Rendering   | OpenType, LaTeX, HarfBuzz | LaTeX, basic text | Basic, plugins for advanced text/math        |
| Layout System         | Panels, borders, measure/arrange | None (manual) | Layer-based, no true layout system          |
| 3D Support            | Planned             | Basic         | Advanced (AE/Resolve), but not code-driven    |
| Maintainability       | Versionable code    | Versionable code | Binary project files, hard to diff           |
| Target Audience       | Developers, technical animators | Developers, educators | Video editors, motion designers             |

## License

[MIT License](LICENSE.md)
