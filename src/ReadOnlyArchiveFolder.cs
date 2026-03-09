using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OwlCore.ComponentModel;
using SharpCompress.Archives;
using SharpCompress.Archives.GZip;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Factories;
using SharpCompress.Readers;

namespace OwlCore.Storage.SharpCompress;

/// <summary>
/// A read-only folder implementation backed by an archive.
/// </summary>
public class ReadOnlyArchiveFolder : IFolder, IChildFolder, IGetItem, IGetFirstByName, IGetItemRecursive, ICreatedAt, ILastModifiedAt, ILastAccessedAt, IDisposable
{
    /// <summary>
    /// The directory separator as defined by the ZIP standard.
    /// This is constant no matter the operating system (see 4.4.17.1).
    /// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
    /// </summary>
    internal const char ZIP_DIRECTORY_SEPARATOR = '/';

    protected IFile? SourceFile { get; }
    protected Stream? BackingStream { get; }

    private readonly string _key;
    private readonly IFolder? _parent;
    private IArchive? _archive;
    private Dictionary<string, IChildFolder>? _subfolders;
    
    /// <summary>
    /// The directory entry backing this folder, if one exists.
    /// Some archive formats have explicit directory entries with timestamps,
    /// others only have implicit directories inferred from file paths.
    /// </summary>
    private IEntry? _directoryEntry;

    // Streams we create when opening from a SourceFile (ownership stays with this folder)
    private Stream? _rootStream;           // The original stream returned by SourceFile.OpenStreamAsync
    private Stream? _compositeStream;      // The top-most wrapped rewindable/decompression stream actually passed to Factory.Open
    private bool _ownsStreams;             // True when we created the streams (SourceFile ctor path)
    
    // Timestamp properties (lazily initialized)
    private ArchiveEntryCreatedAtProperty? _createdAt;
    private ArchiveEntryCreatedAtOffsetProperty? _createdAtOffset;
    private ArchiveEntryLastModifiedAtProperty? _lastModifiedAt;
    private ArchiveEntryLastModifiedAtOffsetProperty? _lastModifiedAtOffset;
    private ArchiveEntryLastAccessedAtProperty? _lastAccessedAt;
    private ArchiveEntryLastAccessedAtOffsetProperty? _lastAccessedAtOffset;

    protected Stream? RootStream 
    { 
        get => _rootStream; 
        set => _rootStream = value; 
    }
    
    protected Stream? CompositeStream 
    { 
        get => _compositeStream; 
        set => _compositeStream = value; 
    }
    
    protected bool OwnsStreams 
    { 
        get => _ownsStreams; 
        set => _ownsStreams = value; 
    }

    public string Id { get; }
    public string Name { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns null if the archive format doesn't support this timestamp or if the folder
    /// is implicit (no explicit directory entry exists in the archive).
    /// </remarks>
    public ICreatedAtProperty CreatedAt => _createdAt ??= new ArchiveEntryCreatedAtProperty(this, _directoryEntry);

    /// <inheritdoc/>
    public ICreatedAtOffsetProperty CreatedAtOffset => _createdAtOffset ??= new ArchiveEntryCreatedAtOffsetProperty(this, _directoryEntry);

    /// <inheritdoc/>
    /// <remarks>
    /// Returns null if the archive format doesn't support this timestamp or if the folder
    /// is implicit (no explicit directory entry exists in the archive).
    /// </remarks>
    public ILastModifiedAtProperty LastModifiedAt => _lastModifiedAt ??= new ArchiveEntryLastModifiedAtProperty(this, _directoryEntry);

    /// <inheritdoc/>
    public ILastModifiedAtOffsetProperty LastModifiedAtOffset => _lastModifiedAtOffset ??= new ArchiveEntryLastModifiedAtOffsetProperty(this, _directoryEntry);

    /// <inheritdoc/>
    /// <remarks>
    /// Returns null if the archive format doesn't support this timestamp or if the folder
    /// is implicit (no explicit directory entry exists in the archive).
    /// </remarks>
    public ILastAccessedAtProperty LastAccessedAt => _lastAccessedAt ??= new ArchiveEntryLastAccessedAtProperty(this, _directoryEntry);

    /// <inheritdoc/>
    public ILastAccessedAtOffsetProperty LastAccessedAtOffset => _lastAccessedAtOffset ??= new ArchiveEntryLastAccessedAtOffsetProperty(this, _directoryEntry);

    public ReadOnlyArchiveFolder(IArchive archive, string id, string name) : this(id, name)
    {
        _parent = null;
        _archive = archive;
    }

    public ReadOnlyArchiveFolder(IFile sourceFile)
        : this(sourceFile.Id.Hash(), Path.GetFileNameWithoutExtension(sourceFile.Name))
    {
        SourceFile = sourceFile;
        _ownsStreams = true;
    }

    public ReadOnlyArchiveFolder(IFile sourceFile, Stream backingStream)
        : this(sourceFile.Id.Hash(), Path.GetFileNameWithoutExtension(sourceFile.Name))
    {
        SourceFile = sourceFile;
        BackingStream = backingStream;
        _ownsStreams = true;
    }

    protected ReadOnlyArchiveFolder(ReadOnlyArchiveFolder parent, string name) : this(parent._archive!, CombinePath(true, parent.Id, name), name)
    {
        _parent = parent;
        
        // Try to find the directory entry for this subfolder
        // The key for this folder (e.g., "subfolder/") may have an explicit entry
        if (parent._archive != null)
        {
            _directoryEntry = parent._archive.Entries.FirstOrDefault(e => 
                e.Key == _key || e.Key == _key.TrimEnd(ZIP_DIRECTORY_SEPARATOR));
        }
    }

    protected ReadOnlyArchiveFolder(string id, string name)
    {
        Name = name;
        Id = EnsureTrailingSeparator(id);
        _key = GetKey(Id);
    }

    public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (type == StorableType.None)
            throw new ArgumentOutOfRangeException(nameof(type), $"{nameof(StorableType)}.{type} is not valid here.");

        var archive = await OpenArchiveAsync(cancellationToken);

        if (type.HasFlag(StorableType.Folder))
        {
            // No need to ensure children, GetSubfolders already filters for us
            var subfolders = await GetSubfoldersAsync(cancellationToken);

            foreach (var subfolder in subfolders.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return subfolder;
            }
        }

        if (type.HasFlag(StorableType.File))
        {
            foreach (var entry in archive.Entries)
            {
                // Only look at children of this current folder
                if (entry.Key is null || !IsChild(entry.Key, _key) || IsDirectory(entry))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                yield return new ArchiveFile(entry, this, GetName(entry.Key));
            }
        }
    }

    public async Task<IStorableChild> GetItemAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        var key = GetKey(id);
        _archive = await OpenArchiveAsync(cancellationToken);

        IArchiveEntry? entry = _archive.Entries.FirstOrDefault(e => e.Key == key);

        if (entry is null)
        {
            // Not every archive format requires separate entries for each directory
            var subfolders = await GetSubfoldersAsync(cancellationToken);
            if (subfolders.TryGetValue(key, out var subfolder))
                return subfolder;

            throw new FileNotFoundException($"No storage item with the ID \"{id}\" could be found.");
        }

        var name = GetName(id);

        return IsDirectory(entry)
            ? WrapSubfolder(name)
            : new ArchiveFile(entry, this, name);
    }

    public async Task<IStorableChild> GetFirstByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetItemAsync(Id + name, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return await GetItemAsync(Id + name + ZIP_DIRECTORY_SEPARATOR, cancellationToken);
        }
    }

    public Task<IStorableChild> GetItemRecursiveAsync(string id, CancellationToken cancellationToken = default)
        => GetItemAsync(Id, cancellationToken);

    public Task<IFolderWatcher> GetFolderWatcherAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_parent);

    internal string GetRootId() => Id[..Id.IndexOf(ZIP_DIRECTORY_SEPARATOR)];

    /// <summary>
    /// Wraps an existing subfolder of the given key.
    /// </summary>
    /// <param name="name">The name of the subfolder.</param>
    /// <returns>A <see cref="ReadOnlyArchiveFolder"/> or <see cref="ArchiveFolder"/>.</returns>
    protected virtual ReadOnlyArchiveFolder WrapSubfolder(string name) => new(this, name);

    protected async Task<Dictionary<string, IChildFolder>> GetSubfoldersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_subfolders is not null)
            return _subfolders;

        _subfolders = [];
        _archive = await OpenArchiveAsync(cancellationToken);

        foreach (var entry in _archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Key is null || !entry.Key.StartsWith(_key))
                continue;

            var relativeKey = entry.Key.Remove(0, _key.Length);
            int subfolderNameLength = relativeKey.IndexOf(ZIP_DIRECTORY_SEPARATOR);
            if (subfolderNameLength <= 0)
                continue;

            var subfolderName = relativeKey[..subfolderNameLength];
            if (subfolderName == Name)
                continue;

            var subfolderKey = _key + subfolderName + ZIP_DIRECTORY_SEPARATOR;
            if (!_subfolders.ContainsKey(subfolderKey))
                _subfolders.Add(subfolderKey, WrapSubfolder(subfolderName));
        }

        return _subfolders;
    }

    public virtual async Task<IArchive> OpenArchiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_archive is null)
        {
            if (SourceFile is null)
                throw new InvalidOperationException("ArchiveFolder requires either an archive or file.");

            // Base file stream (never wrapped yet)
            // track ownership
            var archiveStream = await SourceFile.OpenStreamAsync(FileAccess.Read, cancellationToken);
            _rootStream = archiveStream;
            
            Stream rewindableStream;
            if (BackingStream != null)
            {
                // Use provided backing stream for writes
                rewindableStream = new LazySeekStream(archiveStream, BackingStream);
            }
            else
            {
                // Fallback to memory-based backing stream (read-only scenario)
                rewindableStream = new LazySeekStream(archiveStream);
            }
            
            rewindableStream = new LengthOverrideStream(rewindableStream, archiveStream.Length);
            rewindableStream.Position = 0;

            if (GZipArchive.IsGZipFile(rewindableStream))
            {
                rewindableStream.Position = 0;

                // Decompression chain (still backed by _rootStream)
                rewindableStream = new GZipStream(rewindableStream, CompressionMode.Decompress);
                
                // Estimate decompressed length to handle extreme compression ratios
                // 20x covers pathological cases (95% compression) while ensuring minimum TAR detection size
                long estimatedLength = Math.Max(archiveStream.Length * 20, 1024);
                rewindableStream = new LengthOverrideStream(rewindableStream, estimatedLength);
                rewindableStream = new LazySeekStream(rewindableStream);

                rewindableStream.Position = 0;
            }

            // final stream handed to SharpCompress
            // we will manually dispose in Dispose()
            _compositeStream = rewindableStream;
            var options = new ReaderOptions { LeaveStreamOpen = true };
            
            // NOTE ON FACTORY ORDERING & LAYERED FORMATS (TAR.GZ/TGZ)
            // -----------------------------------------------------------------------------
            // SharpCompress exposes a set of IArchiveFactory implementations (ZipFactory, TarFactory, etc.)
            // via Factory.Factories. The default ordering is NOT guaranteed and ZipFactory commonly
            // appears before TarFactory. After we transparently decompress a .tar.gz stream, the resulting
            // inner stream now contains *raw TAR bytes*, but SharpCompress has no context that we already
            // performed a GZip decompression step externally.
            //
            // Consequence:
            //  - If we naively iterate factories in default order, ZipFactory.IsArchive() is invoked on TAR data.
            //  - Zip detection can perform multi-pass scanning and, on certain non-ZIP inputs, may block for
            //    a long time (observed hang) while attempting to validate central directory structures that
            //    simply do not exist in TAR.
            //  - This produced the production hang we captured in logs: detection stopped at
            //      "Trying factory ZipFactory" and never progressed to TarFactory.
            //
            // Design Implications:
            //  - We must supply *semantic hints* derived from the original filename extension BEFORE we stripped
            //    the compression layer. Relying purely on content sniffing after manual decompression introduces
            //    pathological detection paths.
            //  - We intentionally re-order the factories placing TarFactory first when the original filename
            //    ended with .tar.gz/.tgz. This is a pragmatic mitigation that avoids modifying SharpCompress internals.
            //  - Future layered formats (e.g., .tar.bz2, nested .zip.gz) will require extending this heuristic.
            //
            // Guarantees & Trade-offs:
            //  - Safe: TarFactory.IsArchive() on non-TAR is inexpensive and returns quickly.
            //  - Prevents: Long-running ZipFactory probes on TAR streams.
            //  - Limitation: If user renames a non-TAR GZip file to .tar.gz incorrectly, we may mis-prioritize
            //    TarFactory first (still falls back gracefully to others if TarFactory rejects).
            //
            // Maintenance Guidance:
            //  - DO NOT remove or reorder this prioritization without re-validating against TAR.GZ remount tests.
            //  - If adding new compression layers (BZ2/XZ), replicate extension-based ordering here.
            //  - If SharpCompress adds a native layered archive abstraction in the future, revisit this logic.
            // ----------------------------------------------------------------------------

            // Get all available archive factories
            var allFactories = Factory.Factories.OfType<IArchiveFactory>().ToList();
            
            // For .tar.gz/.tgz files (where we've decompressed), prioritize TAR factory
            var orderedFactories = allFactories;
            if (SourceFile?.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) == true ||
                SourceFile?.Name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) == true)
            {
                var tarFactory = allFactories.FirstOrDefault(f => f.GetType().Name.Contains("Tar"));
                if (tarFactory != null)
                {
                    orderedFactories = new[] { tarFactory }.Concat(allFactories.Where(f => f != tarFactory)).ToList();
                }
            }

            foreach (var factory in orderedFactories)
            {
                rewindableStream.Position = 0;
                if (factory.IsArchive(rewindableStream, options.Password))
                {
                    rewindableStream.Position = 0;
                    _archive = factory.Open(rewindableStream, options);
                    break;
                }

                rewindableStream.Position = 0;
            }
        }

        if (_archive is null)
            throw new ArgumentNullException(nameof(_archive));
            
        cancellationToken.ThrowIfCancellationRequested();
        
        return _archive;
    }

    protected static string GetKey(string id) => id[(id.IndexOf(ZIP_DIRECTORY_SEPARATOR) + 1)..];

    protected static bool IsDirectory(IArchiveEntry entry) => entry.IsDirectory || (entry.Key is not null && entry.Key[^1] == ZIP_DIRECTORY_SEPARATOR);

    internal static string GetName(string id)
    {
        var trimmedId = id.TrimEnd(ZIP_DIRECTORY_SEPARATOR);
        return trimmedId[(trimmedId.LastIndexOf(ZIP_DIRECTORY_SEPARATOR) + 1)..];
    }

    internal static string CombinePath(bool leaveTrailingSeparator, params string[] parts)
    {
        StringBuilder sb = new();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                throw new ArgumentException("Cannot combine an empty string in a path.");

            sb.Append(part);

            if (part[^1] != ZIP_DIRECTORY_SEPARATOR)
                sb.Append(ZIP_DIRECTORY_SEPARATOR);
        }

        if (!leaveTrailingSeparator)
            sb.Remove(sb.Length - 1, 1);

        return sb.ToString();
    }

    private static string EnsureTrailingSeparator(string id)
    {
        if (id[^1] != ZIP_DIRECTORY_SEPARATOR)
            return id + ZIP_DIRECTORY_SEPARATOR;
        return id;
    }

    /// <summary>
    /// Determines whether an entry with the given key is a direct
    /// child of the parent entry. 
    /// </summary>
    /// <param name="childKey">The archive key of the child.</param>
    /// <param name="parentKey">
    /// The archive key of the parent. Must already have trailing separator removed.
    /// </param>
    protected static bool IsChild(string childKey, string parentKey)
    {
        childKey = childKey.TrimEnd(ZIP_DIRECTORY_SEPARATOR);
        parentKey = parentKey.TrimEnd(ZIP_DIRECTORY_SEPARATOR);

        if (childKey.StartsWith(parentKey))
        {
            var childRelativeKey = childKey[parentKey.Length..];
            return !string.IsNullOrWhiteSpace(childRelativeKey)
                   && childRelativeKey.LastIndexOf(ZIP_DIRECTORY_SEPARATOR) <= 0;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Dispose archive first (may flush internal state)
        _archive?.Dispose();
        _archive = null;

        if (_ownsStreams)
        {
            // Disposing the top-most composite stream (LazySeekStream/GZip/LengthOverride chain)
            // will cascade disposal to all inner wrapped streams including the original file stream.
            try { _compositeStream?.Dispose(); } catch { }
        }

        _compositeStream = null;
        _rootStream = null;
    }
}
