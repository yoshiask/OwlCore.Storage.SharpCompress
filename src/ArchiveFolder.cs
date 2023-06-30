using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace OwlCore.Storage.SharpCompress;

public class ArchiveFolder : IModifiableFolder, IChildFolder, IFastGetItem
{
    /// <summary>
    /// The directory separator as defined by the ZIP standard.
    /// This is constant no matter the operating system (see 4.4.17.1).
    /// https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
    /// </summary>
    internal const char ZIP_DIRECTORY_SEPARATOR = '/';
    
    private readonly IWritableArchive _archive;
    private readonly IFolder? _parent;
    
    public string Id { get; }
    public string Name { get; }

    public ArchiveFolder(IWritableArchive archive, string id, string name)
    {
        _archive = archive;
        _parent = null;
        Name = name;
        
        Id = EnsureTrailingSeparator(id);
    }

    private ArchiveFolder(ArchiveFolder parent, string name) : this(parent._archive, CombinePath(true, parent.Id, name), name)
    {
        _parent = parent;
    }

    public async IAsyncEnumerable<IStorableChild> GetItemsAsync(StorableType type = StorableType.All, CancellationToken cancellationToken = default)
    {
        bool includeFiles = type.HasFlag(StorableType.File);
        bool includeFolders = type.HasFlag(StorableType.Folder);

        var idInfo = SplitId(Id);

        foreach (var entry in _archive.Entries)
        {
            // Only look at children of this current folder
            if (!IsChild(entry.Key, idInfo.key))
                continue;

            switch (entry.IsDirectory || entry.Key[^1] == ZIP_DIRECTORY_SEPARATOR)
            {
                case true when includeFolders:
                    yield return new ArchiveFolder(this, GetName(entry.Key));
                    break;
                case false when includeFiles:
                    yield return new ArchiveFile(entry, this, GetName(entry.Key));
                    break;
            }
        }
    }

    public Task<IStorableChild> GetItemAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        var entry = _archive.Entries.FirstOrDefault(e => e.Key == GetKey(id));
        if (entry is null)
            throw new FileNotFoundException($"No storage item with the ID \"{id}\" could be found.");

        var name = GetName(id);
        IStorableChild item;
        if (entry.IsDirectory || entry.Key[^1] == ZIP_DIRECTORY_SEPARATOR)
            item = new ArchiveFolder(this, name);
        else
            item = new ArchiveFile(entry, this, name);
        
        return Task.FromResult(item);
    }

    public Task<IFolderWatcher> GetFolderWatcherAsync(CancellationToken cancellationToken = default)
    {
        throw new System.NotImplementedException();
    }

    public Task DeleteAsync(IStorableChild item, CancellationToken cancellationToken = default)
    {
        var entry = _archive.Entries.FirstOrDefault(e => e.Key == GetKey(item.Id));
        if (entry is not null)
           _archive.RemoveEntry(entry);

        return Task.CompletedTask;
    }

    public Task<IChildFolder> CreateFolderAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var id = Id + name + ZIP_DIRECTORY_SEPARATOR;
        var key = GetKey(id);

        var entry = _archive.Entries.FirstOrDefault(e => e.Key == key);
        if (entry is not null)
        {
            if (!overwrite && !entry.IsDirectory)
                throw new Exception("Cannot return a file from CreateFolderAsync and overwrite was not specified.");
            
            _archive.RemoveEntry(entry);
        }

        entry ??= _archive.AddEntry(key, new MemoryStream(Array.Empty<byte>()), false);
        IChildFolder folder = new ArchiveFolder(this, name);
        
        return Task.FromResult(folder);
    }

    public Task<IChildFile> CreateFileAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var id = Id + name;
        var key = GetKey(id);

        var entry = _archive.Entries.FirstOrDefault(e => e.Key == key);
        if (entry is not null)
        {
            if (!overwrite && entry.IsDirectory)
                throw new Exception("Cannot return a folder from CreateFileAsync and overwrite was not specified.");
            
            _archive.RemoveEntry(entry);
        }

        entry ??= _archive.AddEntry(key, new MemoryStream(), false);
        IChildFile file = new ArchiveFile(entry, this, id, name);
        
        return Task.FromResult(file);
    }

    public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IFolder?>(_parent);

    internal string GetRootId() => Id[..Id.IndexOf(ZIP_DIRECTORY_SEPARATOR)];

    private static string GetKey(string id) => id[(id.IndexOf(ZIP_DIRECTORY_SEPARATOR) + 1)..];

    internal static string GetName(string id)
    {
        var trimmedId = id.TrimEnd(ZIP_DIRECTORY_SEPARATOR);
        return trimmedId[(trimmedId.LastIndexOf(ZIP_DIRECTORY_SEPARATOR) + 1)..];
    }

    private static (string rootId, string key, string path, string name) SplitId(string id)
    {
        int rootIdLength = id.IndexOf(ZIP_DIRECTORY_SEPARATOR);
        if (rootIdLength <= 0)
            return (id, string.Empty, string.Empty, string.Empty);
        
        string rootId = id[..rootIdLength];
        string key = id[(rootIdLength + 1)..];

        string trimmedKey = key.TrimEnd(ZIP_DIRECTORY_SEPARATOR);
        int pathLength = trimmedKey.LastIndexOf(ZIP_DIRECTORY_SEPARATOR);
        if (pathLength <= 0)
            return (rootId, key, string.Empty, trimmedKey);
        
        string path = trimmedKey[..pathLength];
        string name = trimmedKey[(pathLength + 1)..];

        return (rootId, key, path, name);
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