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

    public ArchiveFile(IArchiveEntry entry, IFolder parent, string name)
        : this(entry, parent, ArchiveFolder.CombinePath(false, parent.Id, name), name)
    {
    }
    
    public ArchiveFile(IArchiveEntry entry, IFolder parent, string id, string name)
    {
        _entry = entry;
        _parent = parent;
        
        Id = id;
        Name = name;
    }

    public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IFolder?>(_parent);

    public Task<Stream> OpenStreamAsync(FileAccess accessMode = FileAccess.Read, CancellationToken cancellationToken = new CancellationToken())
        => Task.FromResult(_entry.OpenEntryStream());
}