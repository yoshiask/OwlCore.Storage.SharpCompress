using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace OwlCore.Storage.SharpCompress;

public class ReadOnlyArchiveFolder : IFolder, IChildFolder, IFastGetItem
{
    /// <summary>
    /// The directory separator as defined by the ZIP standard.
    /// This is constant no matter the operating system (see 4.4.17.1).
    /// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
    /// </summary>
    internal const char ZIP_DIRECTORY_SEPARATOR = '/';

    private readonly IFolder? _parent;

    public string Id { get; }
    public string Name { get; }
    protected IArchive Archive { get; }

    public ReadOnlyArchiveFolder(IArchive archive, string id, string name)
    {
        _parent = null;

        Archive = archive;
        Name = name;
        Id = EnsureTrailingSeparator(id);
    }

    protected ReadOnlyArchiveFolder(ReadOnlyArchiveFolder parent, string name) : this(parent.Archive, CombinePath(true, parent.Id, name), name)
    {
        _parent = parent;
    }

    public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default)
    {
        bool includeFiles = type.HasFlag(StorableType.File);
        bool includeFolders = type.HasFlag(StorableType.Folder);

        var thisKey = GetKey(Id);

        foreach (var entry in Archive.Entries)
        {
            // Only look at children of this current folder
            if (!IsChild(entry.Key, thisKey))
                continue;

            switch (entry.IsDirectory || entry.Key[^1] == ZIP_DIRECTORY_SEPARATOR)
            {
                case true when includeFolders:
                    yield return WrapSubfolder(GetName(entry.Key));
                    break;
                case false when includeFiles:
                    yield return new ArchiveFile(entry, this, GetName(entry.Key));
                    break;
            }
        }
    }

    public Task<IStorableChild> GetItemAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        var entry = Archive.Entries.FirstOrDefault(e => e.Key == GetKey(id))
            ?? throw new FileNotFoundException($"No storage item with the ID \"{id}\" could be found.");

        var name = GetName(id);

        IStorableChild item;
        if (entry.IsDirectory || entry.Key[^1] == ZIP_DIRECTORY_SEPARATOR)
            item = WrapSubfolder(name);
        else
            item = new ArchiveFile(entry, this, name);

        return Task.FromResult(item);
    }

    public Task<IFolderWatcher> GetFolderWatcherAsync(CancellationToken cancellationToken = default)
    {
        throw new System.NotImplementedException();
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

    protected static string GetKey(string id) => id[(id.IndexOf(ZIP_DIRECTORY_SEPARATOR) + 1)..];

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
    private static bool IsChild(string childKey, string parentKey)
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
}