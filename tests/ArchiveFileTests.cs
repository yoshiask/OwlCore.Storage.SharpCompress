using SharpCompress.Archives;
using SharpCompress.Archives.GZip;
using SharpCompress.Archives.Tar;
using SharpCompress.Archives.Zip;

namespace OwlCore.Storage.SharpCompress.Tests;

[TestClass]
public class ZipFileTests : CommonArchiveFileTests
{
    protected override IWritableArchive CreateArchive() => ZipArchive.Create();
}

[TestClass]
public class GZipFileTests : CommonArchiveFileTests
{
    protected override IWritableArchive CreateArchive() => GZipArchive.Create();
}

[TestClass]
public class TarFileTests : CommonArchiveFileTests
{
    protected override IWritableArchive CreateArchive() => TarArchive.Create();
}