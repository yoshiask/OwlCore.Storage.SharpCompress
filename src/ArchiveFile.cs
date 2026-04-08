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
    private IArchiveEntry _entry;
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
    public async Task<Stream> OpenStreamAsync(FileAccess accessMode = FileAccess.Read, CancellationToken cancellationToken = default)
    {
        if (accessMode == 0 || (int)accessMode > 3)
            throw new ArgumentOutOfRangeException(nameof(accessMode));

        cancellationToken.ThrowIfCancellationRequested();

        var stream = await OpenEntryStreamWithFallbackAsync(cancellationToken).ConfigureAwait(false);

        if (accessMode.HasFlag(FileAccess.Write) && !stream.CanWrite)
        {
            if (_parent is not ArchiveFolder)
                throw new NotSupportedException();

            byte[] initialBytes = [];
            if (stream.CanRead)
            {
                using (stream)
                {
                    using MemoryStream temp = new();
                    cancellationToken.ThrowIfCancellationRequested();
                    await stream.CopyToAsync(temp, 81920, cancellationToken).ConfigureAwait(false);
                    initialBytes = temp.ToArray();
                }
            }
            else
            {
                stream.Dispose();
            }

            return new CommitOnDisposeMemoryStream(initialBytes, bytes => ReplaceEntryContentAsync(bytes, CancellationToken.None));
        }

        if (accessMode.HasFlag(FileAccess.Read) && !stream.CanRead)
            throw new NotSupportedException();

        if (_parent is ArchiveFolder)
            return stream;

        return new NonDisposableStreamWrapper(stream);
    }

    private async Task<Stream> OpenEntryStreamWithFallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _entry.OpenEntryStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NotImplementedException)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _entry.OpenEntryStream();
    }

    private async Task ReplaceEntryContentAsync(byte[] content, CancellationToken cancellationToken)
    {
        if (_parent is not ArchiveFolder parent)
            throw new NotSupportedException();

        var archive = await parent.OpenArchiveAsync(cancellationToken).ConfigureAwait(false);
        var writableArchive = archive as IWritableArchive ?? throw new NotSupportedException();

        var key = _entry.Key ?? throw new InvalidOperationException("Entry key is unavailable.");
        writableArchive.RemoveEntry(_entry);

        var replacementStream = new MemoryStream(content);
        _entry = writableArchive.AddEntry(key, replacementStream, false);
    }
}
