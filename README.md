<div align="center">

<br/>

# Mubarrat.VideoEngine

### A code-first 2D animation engine for .NET 11.
#### Write scenes. Render video. No GUI. No timeline editor. No binary project files.

<br/>

[![.NET 11](https://img.shields.io/badge/.NET-11-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-3DA639?style=flat-square)](LICENSE.md)
[![Language: C#](https://img.shields.io/badge/Language-C%23-239120?style=flat-square&logo=csharp&logoColor=white)](https://github.com/Mubarrat/Mubarrat.VideoEngine)
[![Status: Pre-release](https://img.shields.io/badge/Status-Pre--release-orange?style=flat-square)](https://github.com/Mubarrat/Mubarrat.VideoEngine)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows&logoColor=white)](https://github.com/Mubarrat/Mubarrat.VideoEngine)

<br/>

</div>

---

`Mubarrat.VideoEngine` (MVE) is a high-performance, code-first 2D vector animation engine that renders H.264 video directly from C# programs. It is built around a zero-allocation unmanaged pixel pipeline, a multi-threaded producer-consumer frame kernel, a document-style layout system, and native OpenType text shaping — producing output at **2× to 5× real-time** on multi-core hardware, and approximately real-time even in single-threaded mode.

This is not a GUI tool you use manually. It is an engine you program against. You describe geometry, layout, animation, and typography in code. MVE handles rasterization, composition, shaping, and encoding. The result is a deterministic `.mp4` file — identical input always produces identical output, down to the last byte of the encoded bitstream.

---

## Table of Contents

- [Why MVE Exists](#why-mve-exists)
- [Performance](#performance)
- [Architecture](#architecture)
- [Feature Reference](#feature-reference)
- [Quick Start](#quick-start)
- [Compared to Everything Else](#compared-to-everything-else)
- [Project Layout](#project-layout)
- [Requirements](#requirements)
- [Roadmap](#roadmap)
- [Contributing](#contributing)

---

## Why MVE Exists

Every major video tool makes one of two bets:

**GUI tools** (Premiere Pro, DaVinci Resolve, After Effects) bet that your content already exists — footage, assets, motion presets — and that you will compose it manually inside a timeline editor. They are excellent at what they are designed for. But they were not designed for generating structured technical content programmatically. You cannot `git diff` a Premiere project. You cannot run a Premiere project inside a CI pipeline. You cannot render 200 variations of an animated diagram by changing a parameter.

**Code-first tools** (Manim) bet that your content is generated, not captured. You write the scene, the tool renders it. This is the right model for math animations, technical explainers, theorem proofs, and diagram-heavy educational content. MVE belongs to this category — but it makes a different set of technical bets than Manim does, described in detail [below](#compared-to-everything-else).

MVE was built because none of the existing tools provided all three of: a .NET-native programming model, a document-style layout system for composing complex multi-element scenes, and a rendering pipeline fast enough that iteration doesn't mean waiting.

---

## Performance

MVE is built to render faster than the video it produces.

### What the numbers mean

A 60 fps, 1920×1080 video requires generating 60 complete frames per second of content. "Real-time" means the engine can produce those 60 frames in one wall-clock second. Faster than real-time means video renders in less time than it plays.

### MVE's rendering profile

| Configuration | Rendering Speed |
|---|---|
| Single-threaded | ~Real-time (≈1× wall-clock) |
| Multi-threaded (typical 4–8 core machine) | **2× – 5× real-time** |

A 5-minute video at 60 fps renders in roughly 1–2.5 minutes, not 5. At 30 fps or lower resolutions the multiplier increases further.

### Why it's fast

Every design decision in MVE's hot path is oriented around keeping allocations, GC pauses, and synchronization overhead out of the render loop.

**Zero-allocation unmanaged pixel pipeline.** Frames are written through `Color32*` — raw, unmanaged pointers into native memory. There is no per-frame managed heap allocation. The .NET garbage collector never runs mid-render. A GC pause that would cause a dropped frame in a naïve implementation simply doesn't happen here, because nothing on the hot path is allocated on the managed heap.

**Multi-threaded producer-consumer frame kernel.** Frame production and encoding run concurrently in a producer-consumer arrangement. Producer threads rasterize frames; consumer threads feed the encoder. The frame queue is managed with 1000-iteration spin locks — tight enough to avoid OS-level context switches for short waits, avoiding scheduler overhead when frames arrive in rapid succession.

**Scanline rasterizer, not a retained-mode scene graph.** MVE does not use a GPU. It rasterizes vector paths on CPU using a scanline fill algorithm, computing anti-aliased coverage analytically per edge crossing. This is intentional: CPU rasterization is deterministic across machines, requires no GPU context or driver, and is embarrassingly parallel — scanline strips are independent and can be distributed across threads with no contention. Hardware-accelerated encoding is used at the output stage (auto-selected from available H.264 backends), but rendering itself is a pure CPU pipeline.

**Hardware H.264 encoding with automatic backend selection.** The encoded side of the pipeline uses whatever H.264 backend is available on the machine — NVENC, AMF, QSV — and falls back to `libx264` when none is present. The engine selects the fastest available option without user configuration.

---

## Architecture

MVE is organized as a pipeline. Each stage is separated, independently composable, and has no hidden global state.

```
┌─────────────────────────────────────────────────────────────────┐
│                        Your C# Code                             │
│          (TimelineSource, PathBuilder, FrameworkObject)         │
└────────────────────────────┬────────────────────────────────────┘
                             │ IFrameSource
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Frame Production                           │
│                                                                 │
│  TimelineLayer → DrawingContext → Rasterizer (Color32*)         │
│                                                                 │
│  • Layout pass: measure() → arrange() on FrameworkObjects       │
│  • Drawing pass: PathDrawing, GroupDrawing composed to tree     │
│  • Rasterize pass: scanline fill, per-edge AA coverage          │
│  • Output: raw Color32* frame buffer per thread                 │
└────────────────────────────┬────────────────────────────────────┘
                             │ Color32* frame buffers
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│               Producer-Consumer Kernel (1000-spin)              │
│                                                                 │
│  Producer threads ──► frame queue ──► Consumer threads          │
│  (rasterize N frames)                (encode N frames)          │
└────────────────────────────┬────────────────────────────────────┘
                             │ YUV frame data
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                    H.264 Encoder (VideoEncoder)                 │
│                                                                 │
│  NVENC → AMF → QSV → libx264 (auto-selected)                    │
│  FFmpeg.AutoGen backend, file or stream output                  │
└─────────────────────────────────────────────────────────────────┘
```

### The drawing model

Everything visible in a frame is a `Drawing`. Drawings form a tree:

- `PathDrawing` — a single vector path with fill, stroke, transform, and gradient
- `GroupDrawing` — a composite of child drawings, with shared transform and clip
- `DrawingContext` — the rendering surface; accepts drawing commands and produces a rasterized frame

Paths are constructed through `PathBuilder`, which produces immutable `Path2D` objects. Immutability matters: the same `Path2D` can be referenced across multiple frames, animation layers, and threads without copying or locking.

### The layout model

MVE includes a complete `measure` / `arrange` layout pipeline modeled on the WPF layout system — but running entirely in software, as part of the rendering pipeline rather than a UI framework.

`FrameworkObject` is the base. Every layout element measures its desired size given an available size constraint, then receives a final arranged rect from its parent. Layout passes are incremental and cache-aware; a scene with 40 text blocks and 20 panels does not re-measure everything when a single element changes.

Available containers:

- `StackPanel` — horizontal or vertical stacking with spacing
- `Grid` — row/column layout with proportional and fixed sizing
- `DockPanel` — edge-docked children with a fill remainder
- `WrapPanel` — line-breaking flow layout
- `RelativePanel` — position children relative to each other or the panel
- `Border` — uniform padding, border, and background around a single child
- `Viewbox` — uniform scale a child to fill available space

This matters for technical content. Placing a LaTeX equation to the left of a graph, aligned to the graph's midpoint, with a label below both — this is a layout problem, not a positioning problem. MVE solves it as layout, not as arithmetic.

### The animation model

Animation is built on `TimelineSource` and `TimelineLayer`.

A `TimelineSource` drives an `IFrameSource`. Each `TimelineLayer` holds a sequence of `TimelineCommand` objects — each command describes what to render at a given frame, with what drawing, and how to interpolate toward the next command.

Easing and interpolation are typed: MVE ships lerpers for scalars, colors, points, vectors, matrices, and paths. Morphing between two `Drawing` objects uses `DrawingMorpher`, which matches compatible shapes and interpolates their geometry frame by frame. `Path2D` morphing works at the contour level.

### Typography

Text in MVE goes through a full shaping pipeline:

1. **Font loading** — OpenType tables are parsed natively in `Mubarrat.OpenType`. No managed wrapper, no third-party font library. Glyph outlines, kerning pairs, GSUB/GPOS feature tables, and metrics are read directly.
2. **Shaping** — HarfBuzz handles complex script shaping (ligatures, mark positioning, bidirectional text). MVE falls back to its own OpenType shaper for scripts where HarfBuzz is not needed or not available.
3. **Fallback chains** — fonts are resolved through a priority-ordered fallback chain, so missing glyphs in one font are filled from the next without user configuration.
4. **Layout** — shaped glyphs pass into `TextBlock`'s line layout, which handles wrapping, alignment, and integration with the `FrameworkObject` measure/arrange pipeline.

`LatexBlock` renders LaTeX math by atomizing the expression and laying out math atoms according to TeX spacing rules, then rasterizing each atom through the same font pipeline.

---

## Feature Reference

### Geometry

- `PathBuilder` for constructing vector paths: lines, arcs, Bézier curves, ellipses, rectangles, polygons
- `Path2D` — immutable, thread-safe vector path; reference across layers and frames safely
- `PathContour` / `Subpath` — individual contour access for per-contour operations
- `Field2D` — implicit geometry using signed-distance functions; union, intersection, subtraction, smooth blending of shapes without explicit path construction
- Stroke properties: `LineCap` (flat, round, square), `LineJoin` (miter, bevel, round), `Pen` with dash arrays
- Fill types: `SolidColorBrush`, linear and radial gradients
- 2D transforms via `Matrix2D`

### Layout

- `FrameworkObject` base with full measure/arrange pipeline
- `StackPanel`, `Grid`, `DockPanel`, `WrapPanel`, `RelativePanel`, `Border`, `Viewbox`
- `HorizontalAlignment`, `VerticalAlignment`, `Margin`, `Padding`, `MinWidth`, `MaxWidth`
- Layout-aware clipping and overflow handling

### Typography

- `TextBlock` — multi-line text with OpenType font, size, weight, color, alignment, wrapping
- `LatexBlock` — LaTeX math rendering with TeX spacing rules
- Native OpenType table parser in `Mubarrat.OpenType` — no managed wrapper dependency
- HarfBuzz shaping integration with legacy OpenType fallback
- Per-font fallback chain configuration

### Animation

- `TimelineSource` with FPS configuration
- `TimelineLayer` with ordered `TimelineCommand` sequences
- Easing functions: linear, ease-in, ease-out, ease-in-out, custom cubic Bézier
- Typed lerpers for `float`, `double`, `Color32`, `Point`, `Vector2D`, `Matrix2D`, `Rect`
- `DrawingMorpher` for drawing-level morphing
- `Path2D` contour-level morphing for vector shape transitions

### Rendering & Export

- Zero-allocation `Color32*` frame buffer pipeline
- Scanline rasterizer with analytical anti-aliasing
- Multi-threaded producer-consumer frame kernel, 1000-iteration spin locks
- `VideoEncoder` base with hardware backend detection and fallback
- Encoder auto-selection: NVENC → AMF → QSV → libx264
- `Video` class for file or stream export
- Fully deterministic output — same input, same file, every time

---

## Quick Start

Clone and build:

```bash
git clone https://github.com/Mubarrat/Mubarrat.VideoEngine.git
cd Mubarrat.VideoEngine
dotnet build
```

Place FFmpeg native binaries in `libs/ffmpeg/` relative to your output directory (they are copied automatically if placed correctly).

### A minimal scene

```csharp
using Mubarrat.VideoEngine;
using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Path;
using Mubarrat.VideoEngine.Timeline;

// 60 fps timeline
var timeline = new TimelineSource(60);

// Add a layer: a red-filled circle, starting at frame 0
timeline.NewLayer.To(0, new PathDrawing
{
    Path = PathBuilder.Circle((960, 540), 300).BuildPath(),
    Fill = new SolidColorBrush(0xFFFFD6D6),
    Stroke = new Pen(new SolidColorBrush(0xFFCC0000), 8)
});

// Configure and export
var video = new Video
{
    Width = 1920,
    Height = 1080,
    TotalFrames = 180,   // 3 seconds at 60 fps
    FramesPerSecond = 60,
    FrameSource = timeline
};

video.Export("output.mp4");
```

### Layout and text

```csharp
FontFace latinModernMathFont;

using (var stream = File.OpenRead("path\\to\\latinmodern-math.otf"))
{
    latinModernMathFont = FontCollection.LoadFont("Latin Modern Math", stream);
}

// A StackPanel composing a label above a diagram
var panel = new StackPanel
{
    Orientation = Orientation.Vertical,
    Spacing = 16,
    Children =
    {
        new TextBlock
        {
			FontFace = latinModernMathFont,
            Text = "The Pythagorean Theorem",
            FontSize = 48,
            Foreground = new SolidColorBrush(0xFF1A1A2E)
        },
        new LatexBlock
        {
			FontFace = latinModernMathFont,
            Latex = @"a^2 + b^2 = c^2",
            FontSize = 64,
            Foreground = new SolidColorBrush(0xFFFFFFFF)
        }
    }
};

timeline.NewLayer.To(0, panel);
```

### Animated morphing

```csharp
var layer = timeline.NewLayer;

// Frame 0: circle
layer.To(0, new PathDrawing
{
    Path = PathBuilder.Circle((960, 540), 200).BuildPath(),
    Fill = new SolidColorBrush(0xFF4FC3F7)
});

// Frame 90: morph to square over 1.5 seconds (eased)
layer.To(90, new PathDrawing
{
    Path = PathBuilder.Rectangle(760, 340, 400, 400).BuildPath(),
    Fill = new SolidColorBrush(0xFFEF9A9A)
}, Easing.EaseInOut);
```

---

## Compared to Everything Else

### vs. After Effects / Premiere Pro / DaVinci Resolve

These tools are for editors, not generators. The workflow is: import footage, arrange clips, add effects, export. Everything is manual. Everything lives in a binary project file that cannot be version-controlled in any meaningful way.

MVE is for content that doesn't exist yet — content that is *computed* from data, formulas, or program logic. The two tools solve different problems. If you have footage to edit, use a video editor. If you have structured content to animate, MVE is built for that.

| | MVE | GUI Video Editors |
|---|---|---|
| Workflow | Write C# code, run, get video | Import assets, arrange manually |
| Reproducibility | Fully deterministic | Depends on project state, plugins |
| Version control | Plain text code | Binary project files |
| Automation | Native (loops, parameters, CI) | Limited scripting, not core |
| Math / diagrams | First-class | Plugins, workarounds |
| Iteration speed | Edit code, re-run | Open project, adjust, re-export |

### vs. Manim

Manim is the reference point for code-first math animation. It is a well-established Python library with a strong community and a large library of example scenes. If you are working in Python and Manim's feature set meets your needs, use it.

MVE makes different technical bets and is targeting a different performance and composability profile. The comparison is honest:

| | MVE | Manim (Community Edition) |
|---|---|---|
| Language | C# / .NET 11 | Python |
| Status | Pre-release, active development | Stable, widely used |
| Rendering speed | 2×–5× real-time (multi-threaded) | Slower than real-time (mostly single-threaded) |
| GC pressure | Zero (native memory, `Color32*`) | Managed Python heap |
| Geometry | Explicit paths + `Field2D` implicit SDF shapes | Mostly explicit paths (VMobject) |
| Layout | `measure` / `arrange` document pipeline | Manual positioning, no layout system |
| Text rendering | Native OpenType parser, HarfBuzz, font fallback chains | LaTeX-heavy; basic text through Pango |
| Math rendering | `LatexBlock` integrated into layout | LaTeX via external TeX installation |
| Encoder | Auto-selected hardware H.264 backend | FFmpeg via subprocess |
| Determinism | Byte-identical output across runs | Deterministic within a run |
| Ecosystem | Early-stage | Large, mature, active community |

The layout system gap is the most significant practical difference for complex scenes. Manim scenes with multiple aligned elements — an equation next to a diagram with a shared baseline, a grid of animated plots with shared axis labels — are typically assembled by manually computing offsets and positions. In MVE, the same scene is expressed as a `Grid` or `StackPanel` hierarchy and the layout engine computes alignment automatically, exactly as a document or UI framework would.

MVE's target is to surpass what Manim can produce in terms of rendering fidelity, layout correctness, and per-frame throughput — while keeping the same code-first, deterministic, developer-native workflow. That work is ongoing.

---

## Project Layout

```
Mubarrat.VideoEngine/
│
├── Draw/              Drawing model
│                      Drawing, PathDrawing, GroupDrawing
│                      DrawingContext, brushes, Pen, gradients
│
├── Field/             Implicit geometry
│                      Field2D, signed-distance operations
│                      union, intersection, subtraction, smooth blending
│
├── Objects/           Layout system
│                      FrameworkObject (measure/arrange base)
│                      StackPanel, Grid, DockPanel, WrapPanel
│                      RelativePanel, Border, Viewbox, Panel
│
├── Path/              Vector path system
│                      PathBuilder, Path2D, PathContour, Subpath, Edge
│                      DrawingMorpher, path morphing
│
├── Timeline/          Animation system
│                      TimelineSource, TimelineLayer, TimelineCommand
│                      Easing, lerpers, interpolation
│
├── Latex/             Math rendering
│                      LatexBlock, math atom layout
│
└── Encoders/          H.264 encoding
                       VideoEncoder base, backend implementations
                       EncoderFactory, FFmpeg.AutoGen integration

Mubarrat.OpenType/
└──                    Font parsing and shaping
                       OpenType table reader (native, no wrapper)
                       Glyph data, metrics, GSUB/GPOS
                       HarfBuzz integration, OpenType shaper fallback
```

---

## Requirements

- **.NET 11**
- **FFmpeg native binaries** — embedded with repo but you can change in `libs/ffmpeg/` relative to output; copied automatically on build
- **H.264 encoder** — NVENC, AMF, or QSV preferred; `libx264` fallback requires no configuration
- Cross-platform in principle but native binary only supports Windows, but you can change it.

---

## Roadmap

MVE is pre-release and under active development.

**Near-term**
- Advanced bidirectional and complex-script text layout
- Semantic animation primitives (write-on, fade, transform sequences as first-class operations)
- Extended easing library and spring physics interpolation
- NuGet package publication

**Medium-term**
- Scene composition from data sources (render a graph from a `double[]`, a table from a `DataTable`)
- Animation preview mode (low-res fast render for iteration)
- Scene testing utilities (assert frame equality, detect regressions)

**Longer-term**
- 3D rendering exploration
- More efficient possible implementation

---

## Contributing

MVE is in early development. Contributions are welcome, particularly in:

- Additional layout containers and layout edge cases
- Font shaping improvements and complex script support
- Additional easing and interpolation implementations
- Documentation, examples, and test scenes

For significant feature work, open a Discussion first to align on design before implementation. For bugs, open an Issue with a minimal reproducing scene if possible.

---

## License

[MIT](LICENSE.md) — free to use, modify, and distribute.
