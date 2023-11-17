namespace OwlCore.Storage.SharpCompress.Tests;

[TestClass]
public class ZipFolderTests : CommonArchiveFolderTests
{
    protected override IWritableArchive CreateArchive() => ZipArchive.Create();
}

[TestClass]
public class TarFolderTests : CommonArchiveFolderTests
{
    protected override IWritableArchive CreateArchive() => TarArchive.Create();
}