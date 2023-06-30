namespace OwlCore.Storage.SharpCompress.Tests.Disk;

[TestClass]
public abstract class CommonDiskArchiveFolderTests
{
    protected abstract IArchive OpenArchive();

    [TestMethod]
    public async Task GetItemsAsyncText_FolderWithNestedItems()
    {
        using var archive = OpenArchive();
        ReadOnlyArchiveFolder root = new(archive, $"Disk{archive.Type}_root", "root");

        var docs = await root.GetFirstByNameAsync("docs") as IFolder;
        {
            Assert.IsNotNull(docs);

            var docsItems = await docs.GetItemsAsync().ToListAsync();
            Assert.AreEqual(1, docsItems.Count);

            var fileA = docsItems[0];
            Assert.IsInstanceOfType<IFile>(fileA);
            Assert.AreEqual($"{root.Id}docs/Astir Magis.xml", fileA.Id);
        }

        var index = await root.GetFirstByNameAsync("index") as IFile;
        {
            Assert.IsNotNull(index);
            Assert.AreEqual($"{root.Id}index", index.Id);

            using var indexStream = await index.OpenStreamAsync();
            using StreamReader reader = new(indexStream);
            string indexText = await reader.ReadToEndAsync();
        }
    }
}
