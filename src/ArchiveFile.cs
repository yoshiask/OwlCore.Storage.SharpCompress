using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace OwlCore.Storage.SharpCompress;

public class ArchiveFile : IChildFile
{
    private readonly IArchiveEntry _entry;
    private readonly IFolder _parent;
    
    public string Id { get; }
    public string Name { get; }

    public ArchiveFile(IArchiveEntry entry, ArchiveFolder parent)
    {
        _entry = entry;
        _parent = parent;

        Name = ArchiveFolder.GetName(entry.Key);
        Id = parent.GetRootId() + ArchiveFolder.ZIP_DIRECTORY_SEPARATOR + Name;
    }
    
    internal ArchiveFile(IArchiveEntry entry, IFolder parent, string name)
        : this(entry, parent, ArchiveFolder.CombinePath(false, parent.Id, name), name)
    {
    }
    
    internal ArchiveFile(IArchiveEntry entry, IFolder parent, string id, string name)
    {
        _entry = entry;
        _parent = parent;
        
        Id = id;
        Name = name;
    }

    public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IFolder?>(_parent);

    public Task<Stream> OpenStreamAsync(FileAccess accessMode = FileAccess.Read, CancellationToken cancellationToken = default)
    {
        if (accessMode == 0 || (int)accessMode > 3)
            throw new ArgumentOutOfRangeException(nameof(accessMode));

        var stream = _entry.OpenEntryStream();
        cancellationToken.ThrowIfCancellationRequested();

        if (accessMode.HasFlag(FileAccess.Read) && !stream.CanRead)
            throw new NotSupportedException();
        if (accessMode.HasFlag(FileAccess.Write) && !stream.CanWrite)
            throw new NotSupportedException();

        return Task.FromResult(stream);
    }
}