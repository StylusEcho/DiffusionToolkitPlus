using System.Text.Json;
using Diffusion.Common;

namespace Diffusion.IO;

/// <summary>
/// Remembers the hash already computed for each model file.
/// </summary>
/// <remarks>
/// Hashing a model means opening it and seeking a megabyte in. That is cheap once and ruinous
/// several thousand times over, which is what a startup scan of a large model folder amounts to -
/// and on a network or removable drive those seeks are what makes startup crawl or hang outright.
/// An entry is trusted only while the file's size and write time still match, which is what
/// changes when a model is replaced.
/// </remarks>
public class ModelHashCache
{
    private readonly Dictionary<string, CacheEntry> _entries;
    private readonly string _path;
    private bool _dirty;

    private class CacheEntry
    {
        public long Size { get; set; }
        public long Ticks { get; set; }
        public string? Hash { get; set; }
    }

    private ModelHashCache(string path, Dictionary<string, CacheEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    public static ModelHashCache Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(path));

                if (entries != null)
                {
                    return new ModelHashCache(path, entries);
                }
            }
        }
        catch (Exception e)
        {
            // A cache that cannot be read is not worth failing a startup over - it rebuilds
            Logger.Log($"Could not read the model hash cache, rebuilding it: {e.Message}");
        }

        return new ModelHashCache(path, new Dictionary<string, CacheEntry>());
    }

    /// <summary>
    /// The hash for a file, computed only if it is not already known for this exact file.
    /// </summary>
    public string? GetOrAdd(FileInfo file, Func<string, string?> compute)
    {
        var key = file.FullName;
        var ticks = file.LastWriteTimeUtc.Ticks;

        if (_entries.TryGetValue(key, out var entry) && entry.Size == file.Length && entry.Ticks == ticks)
        {
            return entry.Hash;
        }

        var hash = compute(file.FullName);

        _entries[key] = new CacheEntry { Size = file.Length, Ticks = ticks, Hash = hash };
        _dirty = true;

        return hash;
    }

    /// <summary>
    /// Drops entries for files that are no longer there, so a renamed model folder does not leave
    /// the cache growing for ever.
    /// </summary>
    public void Retain(ICollection<string> seenPaths)
    {
        var stale = _entries.Keys.Where(k => !seenPaths.Contains(k)).ToList();

        foreach (var key in stale)
        {
            _entries.Remove(key);
            _dirty = true;
        }
    }

    public void Save()
    {
        if (!_dirty) return;

        try
        {
            var dir = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
            _dirty = false;
        }
        catch (Exception e)
        {
            // Losing the cache costs a slow startup next time, nothing more
            Logger.Log($"Could not write the model hash cache: {e.Message}");
        }
    }
}
