namespace OwlCore.Storage.SharpCompress.Tests;

[TestClass]
public class ZipFileTests : CommonArchiveFileTests
{
    protected override IWritableAsyncArchive CreateArchive() => (IWritableAsyncArchive)ZipArchive.CreateArchive();
}

[TestClass]
public class GZipFileTests : CommonArchiveFileTests
{
    protected override IWritableAsyncArchive CreateArchive() => (IWritableAsyncArchive)GZipArchive.CreateArchive();
}

[TestClass]
public class TarFileTests : CommonArchiveFileTests
{
    protected override IWritableAsyncArchive CreateArchive() => (IWritableAsyncArchive)TarArchive.CreateArchive();
}