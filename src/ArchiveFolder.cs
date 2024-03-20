using SharpCompress.Archives;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace OwlCore.Storage.SharpCompress;

public class ArchiveFolder : ReadOnlyArchiveFolder, IModifiableFolder
{
    public ArchiveFolder(IArchive archive, string id, string name) : base(archive, id, name)
    {
    }

    public ArchiveFolder(IFile sourceFile) : base(sourceFile)
    {
    }

    protected ArchiveFolder(ReadOnlyArchiveFolder parent, string name) : base(parent, name)
    {
    }

    public async Task DeleteAsync(IStorableChild item, CancellationToken cancellationToken = default)
    {
        var key = GetKey(item.Id);
        await RemoveSubfolder(key, cancellationToken);
    }

    public async Task<IChildFolder> CreateFolderAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Id + name + ZIP_DIRECTORY_SEPARATOR;
        var key = GetKey(id);
        var subfolders = await GetSubfoldersAsync(cancellationToken);
        
        // Folder doesn't already exist, simply create it
        if (!subfolders.TryGetValue(key, out var folder))
            return await AddSubfolder(key, name, cancellationToken);
        
        // Folder already exists and caller doesn't want to overwrite it
        if (!overwrite)
            return folder;
            
        // Folder already exists and caller wants to overwrite it,
        // so get the parent and attempt to delete the existing
        // one before creating a new one.
        var parent = await folder.GetParentAsync(cancellationToken);
        if (parent is not IModifiableFolder modifiableParent)
            throw new IOException($"A folder with the name '{name}' already exists in parent folder " +
                                  $"'{parent?.Id ?? null}' and is not modifiable.");
        await modifiableParent.DeleteAsync(folder, cancellationToken);

        return await AddSubfolder(key, name, cancellationToken);
    }

    public async Task<IChildFile> CreateFileAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Id + name;
        var key = GetKey(id);
        var archive = await OpenWritableArchiveAsync(cancellationToken);

        var entry = archive.Entries.FirstOrDefault(e => e.Key == key);
        if (entry is not null)
        {
            if (!overwrite && entry.IsDirectory)
                throw new Exception("Cannot return a folder from CreateFileAsync and overwrite was not specified.");

            if (overwrite)
            {
                archive.RemoveEntry(entry);
                entry = null;
            }
        }

        entry ??= archive.AddEntry(key, new MemoryStream(), false);

        return new ArchiveFile(entry, this, id, name);
    }

    protected override ReadOnlyArchiveFolder WrapSubfolder(string name) => new ArchiveFolder(this, name);

    protected async Task RemoveSubfolder(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        // Remove this and all child entries from archive.
        // Force enumeration with .ToList() since we're modifying the collection.
        var archive = await OpenWritableArchiveAsync(cancellationToken);
        var entries = archive.Entries.ToList();
        foreach (var entry in entries)
            if (entry.Key == key || IsChild(entry.Key, key))
                archive.RemoveEntry(entry);

        // Remove subfolder entry if one exists
        var subfolders = await GetSubfoldersAsync(cancellationToken);
        subfolders.Remove(key);
    }

    protected async Task<IChildFolder> AddSubfolder(string key, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var archive = await OpenWritableArchiveAsync(cancellationToken);
        archive.AddEntry(key, new MemoryStream(), false);

        ArchiveFolder folder = new(this, name);

        var subfolders = await GetSubfoldersAsync(cancellationToken);
        subfolders.Add(key, folder);

        return folder;
    }

    protected async Task<IWritableArchive> OpenWritableArchiveAsync(CancellationToken cancellationToken = default)
    {
        var archive = await OpenArchiveAsync(cancellationToken);
        return (IWritableArchive)archive;
    }

    /// <summary>
    /// Wraps a <see cref="Stream"/> with the appropriate archive implementation.
    /// </summary>
    /// <param name="stream">The archive stream to read.</param>
    /// <returns>
    /// The opened <see cref="IFolder"/>, or an <see cref="IModifiableFolder"/> if archive is writable.
    /// </returns>
    public static ReadOnlyArchiveFolder Open(Stream stream, string id, string name)
    {
        var archive = ArchiveFactory.Open(stream);

        if (stream.CanWrite && archive is IWritableArchive writableArchive)
            return new ArchiveFolder(writableArchive, id, name);

        return new ReadOnlyArchiveFolder(archive, id, name);
    }
}
