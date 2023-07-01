using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace OwlCore.Storage.SharpCompress;

public class ReadOnlyArchiveFolder : IFolder, IChildFolder, IFastGetItem, IFastGetFirstByName, IFastGetItemRecursive
{
    /// <summary>
    /// The directory separator as defined by the ZIP standard.
    /// This is constant no matter the operating system (see 4.4.17.1).
    /// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
    /// </summary>
    internal const char ZIP_DIRECTORY_SEPARATOR = '/';

    private readonly string _key;
    private readonly IFolder? _parent;
    private Dictionary<string, IChildFolder>? _subfolders;

    public string Id { get; }
    public string Name { get; }
    protected IArchive Archive { get; }

    public ReadOnlyArchiveFolder(IArchive archive, string id, string name)
    {
        _parent = null;

        Archive = archive;
        Name = name;
        Id = EnsureTrailingSeparator(id);
        _key = GetKey(Id);
    }

    protected ReadOnlyArchiveFolder(ReadOnlyArchiveFolder parent, string name) : this(parent.Archive, CombinePath(true, parent.Id, name), name)
    {
        _parent = parent;
    }

    public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default)
    {
        if (type.HasFlag(StorableType.Folder))
        {
            // No need to ensure children, GetSubfolders already filters for us
            foreach (var subfolder in GetSubfolders().Values)
                yield return subfolder;
        }

        if (type.HasFlag(StorableType.File))
        {
            foreach (var entry in Archive.Entries)
            {
                // Only look at children of this current folder
                if (!IsChild(entry.Key, _key) || IsDirectory(entry))
                    continue;

                yield return new ArchiveFile(entry, this, GetName(entry.Key));
            }
        }
    }

    public Task<IStorableChild> GetItemAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        var key = GetKey(id);
        IArchiveEntry? entry = Archive.Entries.FirstOrDefault(e => e.Key == key);

        if (entry is null)
        {
            // Not every archive format requires separate entries for each directory
            if (GetSubfolders().TryGetValue(key, out var subfolder))
                return Task.FromResult<IStorableChild>(subfolder);
            throw new FileNotFoundException($"No storage item with the ID \"{id}\" could be found.");
        }

        var name = GetName(id);

        IStorableChild item;
        if (IsDirectory(entry))
            item = WrapSubfolder(name);
        else
            item = new ArchiveFile(entry, this, name);

        return Task.FromResult(item);
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

    protected Dictionary<string, IChildFolder> GetSubfolders()
    {
        if (_subfolders is null)
        {
            _subfolders = new();

            foreach (var entry in Archive.Entries)
            {
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
        }

        return _subfolders!;
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