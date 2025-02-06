
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
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Factories;
using SharpCompress.Readers;

namespace OwlCore.Storage.SharpCompress;

public class ReadOnlyArchiveFolder : IFolder, IChildFolder, IGetItem, IGetFirstByName, IGetItemRecursive, IDisposable
{
    /// <summary>
    /// The directory separator as defined by the ZIP standard.
    /// This is constant no matter the operating system (see 4.4.17.1).
    /// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
    /// </summary>
    internal const char ZIP_DIRECTORY_SEPARATOR = '/';

    protected IFile? SourceFile { get; }
    protected IArchive? Archive { get; private set; }
    
    private readonly string _key;
    private readonly IFolder? _parent;
    private Dictionary<string, IChildFolder>? _subfolders;

    public string Id { get; }
    public string Name { get; }

    public ReadOnlyArchiveFolder(IArchive archive, string id, string name) : this(id, name)
    {
        _parent = null;
        Archive = archive;
    }

    public ReadOnlyArchiveFolder(IFile sourceFile)
        : this(sourceFile.Id.Hash(), Path.GetFileNameWithoutExtension(sourceFile.Name))
    {
        SourceFile = sourceFile;
    }

    protected ReadOnlyArchiveFolder(ReadOnlyArchiveFolder parent, string name) : this(parent.Archive!, CombinePath(true, parent.Id, name), name)
    {
        _parent = parent;
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
                if (!IsChild(entry.Key, _key) || IsDirectory(entry))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                yield return new ArchiveFile(entry, this, GetName(entry.Key));
            }
        }
    }

    public async Task<IStorableChild> GetItemAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        var key = GetKey(id);
        Archive = await OpenArchiveAsync(cancellationToken);

        IArchiveEntry? entry = Archive.Entries.FirstOrDefault(e => e.Key == key);

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
        => Task.FromResult<IFolder?>(_parent);

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
        Archive = await OpenArchiveAsync(cancellationToken);

        foreach (var entry in Archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!entry.Key.StartsWith(_key))
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

        if (Archive is null)
        {
            if (SourceFile is null)
                throw new InvalidOperationException("ArchiveFolder requires either an archive or file.");

            var archiveStream = await SourceFile.OpenStreamAsync(FileAccess.Read, cancellationToken);

            Stream rewindableStream = new LazySeekStream(archiveStream);
            rewindableStream = new LengthOverrideStream(rewindableStream, archiveStream.Length);
            rewindableStream.Position = 0;

            if (GZipArchive.IsGZipFile(rewindableStream))
            {
                rewindableStream.Position = 0;

                rewindableStream = new GZipStream(rewindableStream, CompressionMode.Decompress);
                rewindableStream = new LengthOverrideStream(rewindableStream, archiveStream.Length);
                rewindableStream = new LazySeekStream(rewindableStream);

                rewindableStream.Position = 0;
            }

            var options = new ReaderOptions { LeaveStreamOpen = true };

            foreach (var factory in Factory.Factories.OfType<IArchiveFactory>())
            {
                rewindableStream.Position = 0;
                if (factory.IsArchive(rewindableStream, options.Password))
                {
                    rewindableStream.Position = 0;
                    Archive = factory.Open(rewindableStream, options);
                    break;
                }

                rewindableStream.Position = 0;
            }
        }

        if (Archive is null)
            throw new ArgumentNullException(nameof(Archive));

        
        cancellationToken.ThrowIfCancellationRequested();

        return Archive;
    }

    protected static string GetKey(string id) => id[(id.IndexOf(ZIP_DIRECTORY_SEPARATOR) + 1)..];

    protected static bool IsDirectory(IArchiveEntry entry) => entry.IsDirectory || entry.Key[^1] == ZIP_DIRECTORY_SEPARATOR;

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
        Archive?.Dispose();
        Archive = null;
    }
}