using System;
using System.IO.Compression;
using System.Security.Cryptography;
using Diffusion.Common;

namespace Diffusion.IO;

public class ModelScanner
{
    private static readonly string[] ModelExtensions = { ".ckpt", ".safetensors" };

    /// <summary>
    /// The models under a folder, hashed.
    /// </summary>
    /// <remarks>
    /// Hashes come from <see cref="ModelHashCache"/>, so only models that are new or have changed
    /// since the last scan are opened. Without it every startup opened and seeked into every
    /// checkpoint, which on a large folder took the best part of a minute - and far longer, or
    /// indefinitely, when the folder lives on a network or removable drive.
    /// </remarks>
    public static IEnumerable<Model> Scan(string path)
    {
        var cache = ModelHashCache.Load(AppInfo.ModelHashCachePath);

        var seen = new HashSet<string>();
        var models = new List<Model>();

        // One walk of the tree, filtered by extension, rather than a walk per extension. The size
        // and write time the cache checks against come back with the enumeration, so an unchanged
        // model costs no file access at all.
        var files = new DirectoryInfo(path)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => ModelExtensions.Contains(f.Extension.ToLowerInvariant()));

        var hashed = 0;

        foreach (var file in files)
        {
            seen.Add(file.FullName);

            var hash = cache.GetOrAdd(file, f =>
            {
                hashed++;

                try
                {
                    return HashFunctions.CalculateHash(f);
                }
                catch (Exception)
                {
                    return null;
                }
            });

            models.Add(new Model()
            {
                Path = Path.GetRelativePath(path, file.FullName),
                Filename = Path.GetFileNameWithoutExtension(file.FullName),
                Hash = hash,
                IsLocal = true
            });
        }

        // Built eagerly rather than yielded: the cache below has to be written even if the caller
        // only looks at part of the result
        cache.Retain(seen);
        cache.Save();

        Logger.Log($"Model scan: {models.Count} models, {hashed} hashed, {models.Count - hashed} from cache");

        return models;
    }
}
