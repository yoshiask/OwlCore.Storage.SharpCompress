using OwlCore.Storage.CommonTests;
using SharpCompress.Archives.Zip;

namespace OwlCore.Storage.SharpCompress.Tests;

[TestClass]
public class ZipFileTests : CommonIFileTests
{
    // Required for base class to perform common tests.
    public override Task<IFile> CreateFileAsync()
    {
        var archive = ZipArchive.Create();
        var entry = archive.AddEntry($"{Guid.NewGuid()}", new MemoryStream());

        using (var entryStream = entry.OpenEntryStream())
        {
            var randomData = GenerateRandomData(256_000);
            entryStream.Write(randomData);
        }

        var folder = new ArchiveFolder(archive, $"root_{Guid.NewGuid()}", "root");
        var file = new ArchiveFile(entry, folder);

        return Task.FromResult<IFile>(file);

        static byte[] GenerateRandomData(int length)
        {
            var rand = new Random();
            var b = new byte[length];
            rand.NextBytes(b);

            return b;
        }
    }
}