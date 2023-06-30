using OwlCore.Storage.CommonTests;
using SharpCompress.Archives;

namespace OwlCore.Storage.SharpCompress.Tests;

public abstract class CommonArchiveFileTests : CommonIFileTests
{
    protected abstract IWritableArchive CreateArchive();

    public override Task<IFile> CreateFileAsync()
    {
        var archive = CreateArchive();
        var entry = archive.AddEntry($"{Guid.NewGuid()}", new MemoryStream(), false);

        using (var entryStream = entry.OpenEntryStream())
        {
            var randomData = GenerateRandomData(256_000);
            entryStream.Write(randomData);
        }

        var folder = new ArchiveFolder(archive, $"root_{Guid.NewGuid()}", "root");
        var file = new ArchiveFile(entry, folder);

        return Task.FromResult<IFile>(file);
    }

    private static byte[] GenerateRandomData(int length)
    {
        var rand = new Random();
        var b = new byte[length];
        rand.NextBytes(b);

        return b;
    }
}