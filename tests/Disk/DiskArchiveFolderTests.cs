using SharpCompress.Archives.SevenZip;

namespace OwlCore.Storage.SharpCompress.Tests.Disk;

[TestClass]
public class Disk7Zip_7ZipFolderTests : CommonDiskArchiveFolderTests
{
    protected override IAsyncArchive OpenArchive() => (IAsyncArchive)SevenZipArchive.OpenArchive(new MemoryStream(SampleData.SevenZip_7Zip));
}

[TestClass]
public class DiskTar_7ZipFolderTests : CommonDiskArchiveFolderTests
{
    protected override IAsyncArchive OpenArchive() => (IAsyncArchive)TarArchive.OpenArchive(new MemoryStream(SampleData.Tar_7Zip));
}

[TestClass]
public class DiskZip_7ZipFolderTests : CommonDiskArchiveFolderTests
{
    protected override IAsyncArchive OpenArchive() => (IAsyncArchive)ZipArchive.OpenArchive(new MemoryStream(SampleData.Zip_7Zip));
}

[TestClass]
public class DiskZip_WindowsFolderTests : CommonDiskArchiveFolderTests
{
    protected override IAsyncArchive OpenArchive() => (IAsyncArchive)ZipArchive.OpenArchive(new MemoryStream(SampleData.Zip_Windows));
}
