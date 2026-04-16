using FFmpeg.AutoGen;
using Mubarrat.VideoEngine.Encoders;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine;

public class Video
{
    static Video() => ffmpeg.av_log_set_level(ffmpeg.AV_LOG_TRACE);

    public ushort Width { get; set; }

    public ushort Height { get; set; }

    public uint TotalFrames { get; set; }

    public ushort FramesPerSecond { get; set; } = 60;

    public IFrameSource? FrameSource { get; set; }

    public EncoderConstructor EncoderConstructor { get; set; }

    public Video()
    {
        // Detect best available encoder.
        EncoderConstructor ??= EncoderFactory.CreateBestEncoder();
        // Only QSV is implemented for now, but we can add more encoders in the future and choose based on system capabilities.
    }

    public void Export(Stream outputStream)
    {
        if (FrameSource == null)
            throw new InvalidOperationException("FrameSource is null.");
        string tempMp4 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".mp4");
        Export(tempMp4);
        using (var fileStream = File.OpenRead(tempMp4)) fileStream.CopyTo(outputStream); // Copy MP4 to the output stream
        File.Delete(tempMp4);
    }

    public unsafe void Export(string outputPath)
    {
        if (FrameSource is null) throw new InvalidOperationException("FrameSource is null.");
        if (File.Exists(outputPath)) File.Delete(outputPath);

        uint frameSize = (uint)Width * Height * 4;

        // Choosing Environment.ProcessorCount to match environment.
        // Over number means cpu has to decide which thread should it run and it can add overhead.
        // Under number means not efficiently using full cpu to render all frames.
        // For debugger, we should use only 1 thread because it becomes very hard to debug if there's multiple thread.
        uint framesPerChunk = Debugger.IsAttached ? 1u : (uint)Environment.ProcessorCount - 1; // Leave 1 core because it's main thread

        // Allocate a large buffer for a chunk of frames. Each frame is Width * Height * 4 bytes (BGRA).
        // Using a single large buffer for the chunk reduces overhead compared to allocating separate buffers for each frame.
        // The producer will write frames into this buffer in parallel, and the consumer will read from it sequentially.
        // The producer and consumer will synchronize access to this buffer using ManualResetEventSlim arrays,
        // where each index corresponds to a frame slot in the chunk.
        // Don't use a List or array of byte[] for frames, as that would cause a lot of small allocations and GC overhead and also repeated pinning.
        // Instead, use a single large buffer and calculate offsets for each frame.
        byte* chunkBuffer = (byte*)NativeMemory.Alloc((nuint)((ulong)frameSize * framesPerChunk));
        ManualResetEventSlim[] producerEvents = new ManualResetEventSlim[framesPerChunk], consumerEvents = new ManualResetEventSlim[framesPerChunk];

        for (int i = 0; i < framesPerChunk; i++)
        {
            // Initially, producers are not ready (false) and consumer is ready (true)
            producerEvents[i] = new ManualResetEventSlim(false, 1000);
            consumerEvents[i] = new ManualResetEventSlim(true, 1000);
            // Set spin count to 1000 to reduce kernel waiting. because renderer should not
            // take much time to render a frame, so spinning is more efficient than sleeping.
        }

        Stopwatch swTotal = Stopwatch.StartNew();

        ConcurrentBag<Exception> exceptions = [];

        try
        {
            // Producer task (render frames in parallel)
            Task.Run(() =>
            {
                //for (uint start = 0; start < TotalFrames; start += framesPerChunk)
                //{
                //    double chunkStartTime = swTotal.Elapsed.TotalSeconds;
                //    uint total = Math.Min(framesPerChunk, TotalFrames - start);
                //    Parallel.For(0, total, i =>
                //    {
                //        consumerEvents[i].Wait();

                //        byte* framePtr = chunkBuffer + (ulong)i * frameSize;
                //        NativeMemory.Clear(framePtr, frameSize); // Clear the frame buffer before rendering

                //        try
                //        {
                //            FrameSource.RenderFrame(start + (uint)i, (Color32*)framePtr, Width, Height);
                //        }
                //        catch (Exception ex)
                //        {
                //            exceptions.Add(ex); // flow exception from producer to main thread
                //            Log("Producer", ConsoleColor.Red, $"Failed to render frame {start + i + 1}/{TotalFrames}");
                //        }

                //        producerEvents[i].Set();
                //        consumerEvents[i].Reset();
                //    });
                //    double chunkEndTime = swTotal.Elapsed.TotalSeconds;
                //    Log("Producer", ConsoleColor.Cyan, $"Rendered {start + 1}-{start + total} out of {TotalFrames} frames in {chunkEndTime - chunkStartTime:F2}s. Speedup: {(total / (double)FramesPerSecond) / (chunkEndTime - chunkStartTime):F2}x");
                //}

                Parallel.For(0, framesPerChunk, i =>
                {
                    for (uint j = (uint)i; j < TotalFrames; j += framesPerChunk)
                    {
                        consumerEvents[i].Wait();

                        byte* framePtr = chunkBuffer + (ulong)i * frameSize;
                        NativeMemory.Clear(framePtr, frameSize); // Clear the frame buffer before rendering

                        try
                        {
                            FrameSource.RenderFrame(j, (Color32*)framePtr, Width, Height);
                            if (i == framesPerChunk - 1)
                                Log("Producer", ConsoleColor.Cyan, $"Rendered frame {j + 1}/{TotalFrames}");
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex); // flow exception from producer to main thread
                            Log("Producer", ConsoleColor.Red, $"Failed to render frame {j + 1}/{TotalFrames}");
                        }

                        producerEvents[i].Set();
                        consumerEvents[i].Reset();
                    }
                });
            });

            using (var encoder = EncoderConstructor(Width, Height, FramesPerSecond, outputPath)!)
            {
                // Encoder task (sequential)
                double prevTime = swTotal.Elapsed.TotalSeconds;
                for (uint i = 0; i < TotalFrames; i++)
                {
                    uint j = i % framesPerChunk;
                    producerEvents[j].Wait();

                    if (!exceptions.IsEmpty)
                        throw new AggregateException(exceptions);

                    encoder.SendFrame(chunkBuffer + j * frameSize);

                    if ((i + 1) % FramesPerSecond == 0)
                    {
                        double nextTime = swTotal.Elapsed.TotalSeconds;
                        Log("Encoder", ConsoleColor.Yellow, $"Encoded {i - FramesPerSecond + 2}-{i + 1} out of {TotalFrames} frames in {nextTime - prevTime:F2}s. Speedup: {1 / (nextTime - prevTime):F2}x");
                        prevTime = nextTime;
                    }

                    consumerEvents[j].Set();
                    producerEvents[j].Reset();
                }
            }

            swTotal.Stop();
            Log("Main", ConsoleColor.White, $"Encoding complete: {TotalFrames} frames in {swTotal.Elapsed.TotalSeconds:F2}s. Speedup: {TotalFrames / (double)FramesPerSecond / swTotal.Elapsed.TotalSeconds:F2}x");
        }
        finally
        {
            NativeMemory.Free(chunkBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Log(string workerName, ConsoleColor color, string message)
    {
        lock (Console.Out)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{workerName}]: {message}");
            Console.ResetColor();
        }
    }
}
