using SharpCompress.Archives;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace OwlCore.Storage.SharpCompress;

public class ArchiveFolder : ReadOnlyArchiveFolder, IModifiableFolder
{
    protected IWritableArchive WritableArchive => (IWritableArchive)Archive;

    public ArchiveFolder(IWritableArchive archive, string id, string name) : base(archive, id, name)
    {
    }

    protected ArchiveFolder(ArchiveFolder parent, string name) : base(parent, name)
    {
    }

    public Task DeleteAsync(IStorableChild item, CancellationToken cancellationToken = default)
    {
        var key = GetKey(item.Id);
        RemoveSubfolder(key);

        return Task.CompletedTask;
    }

    public Task<IChildFolder> CreateFolderAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var id = Id + name + ZIP_DIRECTORY_SEPARATOR;
        var key = GetKey(id);

        if (GetSubfolders().TryGetValue(key, out var folder))
        {
            if (!overwrite)
                return Task.FromResult(folder);

            RemoveSubfolder(key);
        }
        
        return Task.FromResult(AddSubfolder(key, name));
    }

    public Task<IChildFile> CreateFileAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var id = Id + name;
        var key = GetKey(id);

        var entry = Archive.Entries.FirstOrDefault(e => e.Key == key);
        if (entry is not null)
        {
            if (!overwrite && entry.IsDirectory)
                throw new Exception("Cannot return a folder from CreateFileAsync and overwrite was not specified.");

            WritableArchive.RemoveEntry(entry);
        }

        entry ??= WritableArchive.AddEntry(key, new MemoryStream(), false);
        IChildFile file = new ArchiveFile(entry, this, id, name);

        return Task.FromResult(file);
    }

    protected override ReadOnlyArchiveFolder WrapSubfolder(string name) => new ArchiveFolder(this, name);

    protected void RemoveSubfolder(IArchiveEntry entry)
    {
        WritableArchive.RemoveEntry(entry);
        GetSubfolders().Remove(entry.Key);
    }

    protected void RemoveSubfolder(string key)
    {
        var entry = Archive.Entries.FirstOrDefault(e => e.Key == key);
        if (entry is not null)
            WritableArchive.RemoveEntry(entry);

        GetSubfolders().Remove(key);
    }

    protected IChildFolder AddSubfolder(string key, string name)
    {
        WritableArchive.AddEntry(key, new MemoryStream(Array.Empty<byte>()), false);

        ArchiveFolder folder = new(this, name);
        GetSubfolders().Add(key, folder);

        return folder;
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
