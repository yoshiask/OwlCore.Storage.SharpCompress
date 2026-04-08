namespace OwlCore.Storage.SharpCompress.Tests;

[TestClass]
public class ZipFolderTests : CommonArchiveFolderTests
{
    protected override IWritableAsyncArchive CreateArchive() => (IWritableAsyncArchive)ZipArchive.CreateArchive();
}

[TestClass]
public class TarFolderTests : CommonArchiveFolderTests
{
    protected override IWritableAsyncArchive CreateArchive() => (IWritableAsyncArchive)TarArchive.CreateArchive();
}