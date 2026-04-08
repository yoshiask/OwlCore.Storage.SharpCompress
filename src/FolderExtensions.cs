using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace OwlCore.Storage.SharpCompress;

public static class FolderExtensions
{
    /// <inheritdoc cref="ArchiveFolder.CreateArchiveAsync"/>
    public static async Task<ArchiveFolder> CreateArchiveAsync(this IModifiableFolder parentFolder, string name,
        ArchiveType archiveType, CancellationToken cancellationToken = default)
    {
        return await ArchiveFolder.CreateArchiveAsync(parentFolder, name, archiveType, cancellationToken);
    }
    
    /// <inheritdoc cref="ArchiveFolder.FlushToAsync(IFile, IWritableAsyncArchive, CancellationToken)"/>
    public static async Task FlushToAsync(this IWritableAsyncArchive archive, IFile archiveFile,
        CancellationToken cancellationToken = default)
    {
        await ArchiveFolder.FlushToAsync(archiveFile, archive, cancellationToken);
    }

    /// <inheritdoc cref="ArchiveFolder.FlushToAsync(IFile, IWritableArchive{WriterOptions}, CancellationToken)"/>
    public static async Task FlushToAsync(this IWritableArchive<WriterOptions> archive, IFile archiveFile,
        CancellationToken cancellationToken = default)
    {
        await ArchiveFolder.FlushToAsync(archiveFile, archive, cancellationToken);
    }
}