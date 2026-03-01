using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace OwlCore.Storage.SharpCompress;

/// <summary>
/// An <see cref="IFile"/> implementation backed by an archive entry.
/// </summary>
public class ArchiveFile : IChildFile, ICreatedAt, ILastModifiedAt, ILastAccessedAt
{
    private readonly IArchiveEntry _entry;
    private readonly IFolder _parent;
    
    private ArchiveEntryCreatedAtProperty? _createdAt;
    private ArchiveEntryCreatedAtOffsetProperty? _createdAtOffset;
    private ArchiveEntryLastModifiedAtProperty? _lastModifiedAt;
    private ArchiveEntryLastModifiedAtOffsetProperty? _lastModifiedAtOffset;
    private ArchiveEntryLastAccessedAtProperty? _lastAccessedAt;
    private ArchiveEntryLastAccessedAtOffsetProperty? _lastAccessedAtOffset;

    /// <inheritdoc/>
    public string Id { get; }
    
    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>
    /// Creates a new instance of <see cref="ArchiveFile"/>.
    /// </summary>
    public ArchiveFile(IArchiveEntry entry, ReadOnlyArchiveFolder parent)
    {
        _entry = entry;
        _parent = parent;

        Name = ReadOnlyArchiveFolder.GetName(entry.Key!);
        Id = parent.GetRootId() + ReadOnlyArchiveFolder.ZIP_DIRECTORY_SEPARATOR + Name;
    }
    
    internal ArchiveFile(IArchiveEntry entry, IFolder parent, string name)
        : this(entry, parent, ReadOnlyArchiveFolder.CombinePath(false, parent.Id, name), name)
    {
    }
    
    internal ArchiveFile(IArchiveEntry entry, IFolder parent, string id, string name)
    {
        _entry = entry;
        _parent = parent;
        
        Id = id;
        Name = name;
    }

    /// <inheritdoc/>
    public ICreatedAtProperty CreatedAt => _createdAt ??= new ArchiveEntryCreatedAtProperty(this, _entry);

    /// <inheritdoc/>
    public ICreatedAtOffsetProperty CreatedAtOffset => _createdAtOffset ??= new ArchiveEntryCreatedAtOffsetProperty(this, _entry);

    /// <inheritdoc/>
    public ILastModifiedAtProperty LastModifiedAt => _lastModifiedAt ??= new ArchiveEntryLastModifiedAtProperty(this, _entry);

    /// <inheritdoc/>
    public ILastModifiedAtOffsetProperty LastModifiedAtOffset => _lastModifiedAtOffset ??= new ArchiveEntryLastModifiedAtOffsetProperty(this, _entry);

    /// <inheritdoc/>
    public ILastAccessedAtProperty LastAccessedAt => _lastAccessedAt ??= new ArchiveEntryLastAccessedAtProperty(this, _entry);

    /// <inheritdoc/>
    public ILastAccessedAtOffsetProperty LastAccessedAtOffset => _lastAccessedAtOffset ??= new ArchiveEntryLastAccessedAtOffsetProperty(this, _entry);

    /// <inheritdoc/>
    public Task<IFolder?> GetParentAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IFolder?>(_parent);

    /// <inheritdoc/>
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
