using OwlCore.Storage.CommonTests;

namespace OwlCore.Storage.SharpCompress.Tests;

public abstract class CommonArchiveFileTests : CommonIFileTests
{
    protected abstract IWritableArchive CreateArchive();

    /// <summary>
    /// Archive format timestamps may or may not be available depending on the format.
    /// TAR/GZip/Arc don't support CreatedAt. Zip/Rar/7zip/Arj do.
    /// </summary>
    public override PropertyValueAvailability CreatedAtAvailability => PropertyValueAvailability.Maybe;

    /// <summary>
    /// Most archive formats support LastModifiedAt, but Arc doesn't.
    /// </summary>
    public override PropertyValueAvailability LastModifiedAtAvailability => PropertyValueAvailability.Maybe;

    /// <summary>
    /// Archive format timestamps may or may not be available depending on the format.
    /// TAR/GZip/Arc don't support LastAccessedAt. Zip/Rar/7zip/Arj do.
    /// </summary>
    public override PropertyValueAvailability LastAccessedAtAvailability => PropertyValueAvailability.Maybe;

    public override Task<IFile> CreateFileAsync()
    {
        var archive = CreateArchive();
        var entry = archive.AddEntry($"{Guid.NewGuid()}", new MemoryStream(), false);

        using (var entryStream = entry.OpenEntryStream())
        {
            var randomData = GenerateRandomData(256_000);
            entryStream.Write(randomData);
        }

        var folder = new ReadOnlyArchiveFolder(archive, $"root_{Guid.NewGuid()}", "root");
        var file = new ArchiveFile(entry, folder);

        return Task.FromResult<IFile>(file);
    }

    /// <summary>
    /// Creates a file with a specific LastModifiedAt timestamp.
    /// Archive formats support setting modification time at entry creation.
    /// </summary>
    public override Task<IFile?> CreateFileWithLastModifiedAtAsync(DateTime lastModifiedAt)
    {
        var archive = CreateArchive();
        
        // SharpCompress AddEntry signature: AddEntry(string key, Stream source, bool closeStream, long size, DateTime? modified)
        // We use the overload that accepts a modification time
        var content = new MemoryStream(GenerateRandomData(256_000));
        var entry = archive.AddEntry($"{Guid.NewGuid()}", content, false, content.Length, lastModifiedAt);

        var folder = new ReadOnlyArchiveFolder(archive, $"root_{Guid.NewGuid()}", "root");
        var file = new ArchiveFile(entry, folder);

        return Task.FromResult<IFile?>(file);
    }

    // CreatedAt and LastAccessedAt cannot be set via SharpCompress AddEntry API.
    // Those timestamps come from reading existing archives that have extended timestamp data.
    public override Task<IFile?> CreateFileWithCreatedAtAsync(DateTime createdAt) => Task.FromResult<IFile?>(null);
    public override Task<IFile?> CreateFileWithLastAccessedAtAsync(DateTime lastAccessedAt) => Task.FromResult<IFile?>(null);

    internal static byte[] GenerateRandomData(int length)
    {
        var rand = new Random();
        var b = new byte[length];
        rand.NextBytes(b);

        return b;
    }
}
