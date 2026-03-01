using SharpCompress.Archives;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using CommunityToolkit.Diagnostics;
using OwlCore.ComponentModel;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace OwlCore.Storage.SharpCompress;

public class ArchiveFolder : ReadOnlyArchiveFolder, IModifiableFolder, IFlushable
{
    public ArchiveFolder(IArchive archive, string id, string name) : base(archive, id, name)
    {
    }

    /// <summary>
    /// Initializes an <see cref="ArchiveFolder"/> bound to a source <see cref="IFile"/>.
    /// </summary>
    /// <remarks>
    /// For write scenarios, persistence relies on the IFile-based open path: changes are saved to a backing stream and then
    /// finalized when the internally-owned streams are disposed (see <see cref="FlushAsync(CancellationToken)"/>).
    /// Ensure your <see cref="IFile"/> implementation (or hosting layer) persists the backing contents to the underlying file
    /// when the stream returned by <see cref="IFile.OpenStreamAsync(FileAccess, CancellationToken)"/> is disposed.
    /// <para />
    /// If you need explicit control of the write buffer, or you expect large archives (&gt; 2 GB) that should avoid in-memory buffering
    /// and <see cref="int.MaxValue"/>-bounded buffers, prefer <see cref="ArchiveFolder(IFile, Stream)"/>
    /// and supply a backing stream to catch and dump writes efficiently.
    /// </remarks>
    public ArchiveFolder(IFile sourceFile) : base(sourceFile)
    {
    }

    /// <summary>
    /// Initializes an <see cref="ArchiveFolder"/> bound to a source <see cref="IFile"/> with an explicit backing stream for writes.
    /// </summary>
    /// <param name="sourceFile">The underlying file for archive persistence.</param>
    /// <param name="backingStream">A caller-provided buffer used to accumulate write operations prior to persistence.</param>
    /// <remarks>
    /// Use this overload when you plan to write to the archive. <see cref="FlushAsync(CancellationToken)"/> saves changes to
    /// <paramref name="backingStream"/>, then disposes the internally-owned streams to release file handles and allow your host
    /// to persist the backing contents to <paramref name="sourceFile"/>. This is recommended for large archives (&gt; 2 GB) to
    /// avoid memory-based buffering and <see cref="int.MaxValue"/> limitations.
    /// </remarks>
    public ArchiveFolder(IFile sourceFile, Stream backingStream) : base(sourceFile, backingStream)
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
            if (entry.Key != null && (entry.Key == key || IsChild(entry.Key, key)))
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

        if (archive is not IWritableArchive writableArchive)
            throw new IOException($"Archive '{Name}' ({Id}) is not writable.");

        return writableArchive;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// For folders created from an <see cref="IFile"/>, this method writes changes to the backing stream using
    /// format-appropriate options, then disposes the internally-owned streams to release file handles and trigger
    /// persistence of the backing contents to the underlying file. Instances not created from an <see cref="IFile"/>
    /// cannot be flushed.
    /// </remarks>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!CanFlush())
            throw new InvalidOperationException($"Only {nameof(ArchiveFolder)}s created from files can be flushed.");

        // Get the already-loaded writable archive (contains all our changes in memory)
        var archive = await OpenWritableArchiveAsync(cancellationToken);

        // If we don't have a composite stream (e.g. if we constructed from IArchive instead of IFile),
        // we can't flush because we don't own the underlying file
        if (CompositeStream == null || SourceFile == null)
        {
            throw new InvalidOperationException($"Cannot flush {nameof(ArchiveFolder)} that was not created from a file.");
        }

        // Save archive changes to backing stream before disposal
        if (BackingStream != null && archive is IWritableArchive writableArchive)
        {
            BackingStream.Position = 0;
            BackingStream.SetLength(0);
            
            var fileName = SourceFile.Name;
            
            // Handle layered formats (.tar.gz/.tgz) - mirror FlushToAsync logic
            if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                // For .tar.gz, write TAR data through GZip compression directly to backing stream
                // CRITICAL: GZipStream must be disposed to write GZIP footer properly
                using (var gzipStream = new GZipStream(BackingStream, CompressionMode.Compress, leaveOpen: true))
                {
                    writableArchive.SaveTo(gzipStream, new WriterOptions(CompressionType.None));
                    gzipStream.Flush();
                } // GZipStream disposal writes the footer and finalizes the GZIP file
            }
            else
            {
                // Single-layer formats
                var compressionType = archive.Type switch
                {
                    ArchiveType.Zip => CompressionType.Deflate,
                    ArchiveType.Tar => CompressionType.None,
                    _ => CompressionType.None
                };
                
                writableArchive.SaveTo(BackingStream, new WriterOptions(compressionType));
            }
            
            BackingStream.Flush();
        }

        // CRITICAL: Dispose existing streams to release file handles before writing
        // This triggers our disposal delegate which flushes the backing stream to the file
        Guard.IsNotNull(RootStream);
        RootStream.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Whether changes to this <see cref="ArchiveFolder"/> can be flushed to the underlying file.
    /// </summary>
    public bool CanFlush() => SourceFile is not null;

    /// <summary>
    /// Wraps a <see cref="Stream"/> with the appropriate archive implementation.
    /// </summary>
    /// <param name="stream">The archive stream to read.</param>
    /// <param name="id">The ID to assign to the created folder.</param>
    /// <param name="name">The name to assign to the created folder.</param>
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

    /// <summary>
    /// Creates an empty archive within the <paramref name="parentFolder"/>.
    /// </summary>
    /// <param name="parentFolder">The folder to create the archive in.</param>
    /// <param name="name">The name of the new archive.</param>
    /// <param name="archiveType">The type of archive to create.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the ongoing operation.</param>
    /// <returns>A task containing the new <see cref="ArchiveFolder"/>.</returns>
    /// <remarks>
    /// This is a convenience overload that returns an <see cref="ArchiveFolder"/> already bound to the created
    /// <see cref="IFile"/>, enabling immediate modification and reliable <see cref="FlushAsync(CancellationToken)"/>.
    /// <para />
    /// Binding to the source file ensures filename-based format hints (e.g., TAR-first detection for .tar.gz/.tgz),
    /// correct format-specific save options, and controlled stream ownership that releases file handles on flush.
    /// <para />
    /// Prefer this overload when you want a ready-to-use, modifiable archive with safe defaults.
    /// <para />
    /// Use <see cref="CreateArchiveAsync(IFile, ArchiveType, CancellationToken)"/>
    /// when you only need an initialized archive file and intend to manage archive opening/lifecycle yourself (e.g.,
    /// to control buffering, compression, or persistence strategy).
    /// </remarks>
    public static async Task<ArchiveFolder> CreateArchiveAsync(IModifiableFolder parentFolder, string name,
        ArchiveType archiveType, CancellationToken cancellationToken)
    {
        var archiveFile = await parentFolder.CreateFileAsync(name, overwrite: true, cancellationToken);
        var archive = ArchiveFactory.Create(archiveType);

        await FlushToAsync(archiveFile, archive, cancellationToken);

        // Create ArchiveFolder with the source file for proper lifecycle management
        var archiveFolder = new ArchiveFolder(archiveFile);
        return archiveFolder;
    }

    /// <summary>
    /// Creates an empty archive directly into the provided file and returns that file.
    /// </summary>
    /// <param name="archiveFile">The file to initialize as an archive. Existing contents will be overwritten.</param>
    /// <param name="archiveType">The type of archive to create.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the ongoing operation.</param>
    /// <returns>The same <see cref="IFile"/> instance, after being initialized as an archive.</returns>
    /// <remarks>
    /// This overload keeps lifecycle control with the caller by returning only the <see cref="IFile"/>.
    /// <para />
    /// If you intend to manage the archive lifecycle yourself, open the appropriate <see cref="IArchive"/> (handling any needed
    /// decompression and format selection, e.g., TAR after GZip for .tar.gz) and construct an <see cref="ArchiveFolder"/>
    /// using the <see cref="ArchiveFolder(IArchive, string, string)"/> constructor. Along this path, detection, rewind/length handling,
    /// and persistence semantics are your responsibility.
    /// <para />
    /// Avoid constructing directly from a raw <see cref="Stream"/> unless you also provide proper rewind/length/decompression wrappers.
    /// <para />
    /// Use <see cref="CreateArchiveAsync(IModifiableFolder, string, ArchiveType, CancellationToken)"/>
    /// when you want a ready-to-use <see cref="ArchiveFolder"/> with safe defaults, detection heuristics, and flush/handle
    /// management provided for you.
    /// </remarks>
    public static async Task<IFile> CreateArchiveAsync(IFile archiveFile, ArchiveType archiveType, CancellationToken cancellationToken)
    {
        var archive = ArchiveFactory.Create(archiveType);
        await FlushToAsync(archiveFile, archive, cancellationToken);
        return archiveFile;
    }

    /// <summary>
    /// Writes an archive to the supplied file.
    /// </summary>
    /// <param name="archiveFile">The file to save the archive to.</param>
    /// <param name="archive">The archive to save.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the ongoing operation.</param>
    public static async Task FlushToAsync(IFile archiveFile, IWritableArchive archive, CancellationToken cancellationToken)
    {
        using var archiveFileStream = await archiveFile.OpenReadWriteAsync(cancellationToken);
        Guard.IsEqualTo(archiveFileStream.Position, 0);

        var fileName = archiveFile.Name;

        // Handle layered formats (.tar.gz/.tgz) - stream directly to avoid memory issues
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            // Stream directly to GZip without using MemoryStream for large file support
            // CRITICAL: GZipStream must be disposed to write GZIP footer properly
            using (var gzipStream = new GZipStream(archiveFileStream, CompressionMode.Compress, leaveOpen: true))
            {
                archive.SaveTo(gzipStream, new WriterOptions(CompressionType.None));
                await gzipStream.FlushAsync(cancellationToken);
            } // GZipStream disposal writes the footer and finalizes the GZIP file
        }
        else
        {
            // Single-layer formats
            var compressionType = archive.Type switch
            {
                ArchiveType.Zip => CompressionType.Deflate,
                ArchiveType.Tar => CompressionType.None,
                _ => CompressionType.None // Default fallback
            };

            archive.SaveTo(archiveFileStream, new WriterOptions(compressionType));
        }

        // Ensure the data is actually written to the underlying storage
        await archiveFileStream.FlushAsync(cancellationToken);
    }
}
