using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace OwlCore.Storage.SharpCompress;

public static class FolderExtensions
{
    public static async Task<ArchiveFolder> CreateArchiveAsync(this IModifiableFolder parentFolder, string name,
        ArchiveType archiveType, CancellationToken cancellationToken = default)
    {
        return await ArchiveFolder.CreateArchiveAsync(parentFolder, name, archiveType, cancellationToken);
    }
    
    public static async Task FlushToAsync(this IWritableArchive archive, IFile file,
        CancellationToken cancellationToken = default)
    {
        await ArchiveFolder.FlushToAsync(file, archive, cancellationToken);
    }
}