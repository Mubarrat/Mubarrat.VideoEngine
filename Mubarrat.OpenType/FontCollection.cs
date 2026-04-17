using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Mubarrat.OpenType;

/// <summary>
/// Thread-safe scalable font collection with stream-based loading and caching support.
/// </summary>
public static class FontCollection
{
    private static readonly ConcurrentDictionary<string, FontFace> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Load a font from a stream into the collection.
    /// Returns cached instance if key is already present.
    /// </summary>
    public static FontFace LoadFont(string key, Stream stream) => cache.GetOrAdd(key, key => new FontFace(key, new OpenTypeReader(stream)));

    /// <summary>
    /// Load a font from a stream into the collection.
    /// Returns cached instance if key is already present.
    /// </summary>
    public static FontFace LoadFont(string key, OpenTypeReader reader) => cache.GetOrAdd(key, key => new FontFace(key, reader));

    /// <summary>
    /// Try get a font by its key.
    /// </summary>
    public static bool TryGetFont(string key, [MaybeNullWhen(false)] out FontFace font) => cache.TryGetValue(key, out font);

    /// <summary>
    /// Enumerate all loaded fonts.
    /// </summary>
    public static IEnumerable<FontFace> GetAllFonts() => cache.Values;
}
