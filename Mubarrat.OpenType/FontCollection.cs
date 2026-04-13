using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Mubarrat.OpenType;

/// <summary>
/// Thread-safe scalable font collection with stream-based loading and caching support.
/// </summary>
public sealed class FontCollection : IDisposable
{
    private readonly ConcurrentDictionary<string, FontFace> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load a font from a stream into the collection.
    /// Returns cached instance if key is already present.
    /// </summary>
    public FontFace LoadFont(string key, Stream stream) => cache.GetOrAdd(key, key => new FontFace(key, new OpenTypeReader(stream)));

    /// <summary>
    /// Load a font from a stream into the collection.
    /// Returns cached instance if key is already present.
    /// </summary>
    public FontFace LoadFont(string key, OpenTypeReader reader) => cache.GetOrAdd(key, key => new FontFace(key, reader));

    /// <summary>
    /// Try get a font by its key.
    /// </summary>
    public bool TryGetFont(string key, [MaybeNullWhen(false)] out FontFace font) => cache.TryGetValue(key, out font);

    /// <summary>
    /// Enumerate all loaded fonts.
    /// </summary>
    public IEnumerable<FontFace> GetAllFonts() => cache.Values;

    /// <summary>
    /// Clears cache and disposes all loaded fonts.
    /// </summary>
    public void Clear() => cache.Clear();

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }
}
