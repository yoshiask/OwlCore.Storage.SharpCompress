using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace OwlCore.Storage.SharpCompress;

public static class FolderExtensions
{
    /// <inheritdoc cref="ArchiveFolder.CreateArchiveAsync"/>
    public static Task<ArchiveFolder> CreateArchiveAsync(this IModifiableFolder parentFolder, string name, CancellationToken cancellationToken = default)
    {
        return ArchiveFolder.CreateArchiveAsync(parentFolder, name, cancellationToken);
    }
    
    /// <inheritdoc cref="ArchiveFolder.FlushToAsync(IFile, IWritableAsyncArchive, CancellationToken)"/>
    public static Task FlushToAsync(this IWritableAsyncArchive archive, IFile archiveFile,
        CancellationToken cancellationToken = default)
    {
        return ArchiveFolder.FlushToAsync(archiveFile, archive, cancellationToken);
    }

    /// <inheritdoc cref="ArchiveFolder.FlushToAsync(IFile, IWritableArchive{WriterOptions}, CancellationToken)"/>
    public static Task FlushToAsync(this IWritableArchive<WriterOptions> archive, IFile archiveFile,
        CancellationToken cancellationToken = default)
    {
        return ArchiveFolder.FlushToAsync(archiveFile, archive, cancellationToken);
    }
}