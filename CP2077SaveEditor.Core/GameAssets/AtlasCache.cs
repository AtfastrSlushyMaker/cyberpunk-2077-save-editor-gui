using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using WolvenKit.RED4.CR2W;
using WolvenKit.Common.Services;
using WolvenKit.RED4.Archive;
using WolvenKit.RED4.Archive.IO;
using EFileReadErrorCodes = WolvenKit.RED4.Archive.IO.EFileReadErrorCodes;

namespace CP2077SaveEditor.GameAssets;

/// <summary>
/// Decodes icon atlas textures from the game archives to PNG files in the cache folder (once per texture).
/// Note: decoded atlases are stored upside down; consumers must flip the V coordinate and the cropped image.
/// </summary>
public sealed class AtlasCache
{
    private readonly string _gameDir;
    private readonly string _dir;
    private readonly ConcurrentDictionary<string, Archive?> _archives = new();
    private readonly Lazy<HashService> _hashService = new(() => new HashService(), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly object _decodeLock = new();

    public AtlasCache(string gameDir)
    {
        _gameDir = gameDir;
        _dir = Path.Combine(GameAssetIndex.CacheDir(gameDir), "atlases");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Path to the decoded atlas PNG for an icon's texture, decoding it on first use; null if unavailable.</summary>
    public string? GetAtlasPng(IconRef icon)
    {
        var path = Path.Combine(_dir, $"{icon.Texture:X16}.png");
        if (File.Exists(path))
        {
            return path;
        }

        lock (_decodeLock)
        {
            if (File.Exists(path))
            {
                return path;
            }

            var archive = _archives.GetOrAdd(icon.Archive, name =>
                new ArchiveReader().ReadArchive(Path.Combine(GameInstall.ContentDir(_gameDir), name), _hashService.Value, out var a) == EFileReadErrorCodes.NoError ? a : null);
            if (archive == null || !archive.Files.TryGetValue(icon.Texture, out var file) || GameAssetIndex.Read(file) is not { } cr2w)
            {
                return null;
            }

            using var image = RedImage.FromRedFile(cr2w);
            var temp = path + ".tmp";
            image.SaveToPNG(temp);
            File.Move(temp, path, overwrite: true);
            return path;
        }
    }
}
