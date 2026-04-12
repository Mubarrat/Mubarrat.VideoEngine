namespace Mubarrat.VideoEngine.Encoders;

public delegate VideoEncoder EncoderConstructor(ushort width, ushort height, ushort fps, string outputPath);
