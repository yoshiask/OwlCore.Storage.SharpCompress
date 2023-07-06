# OwlCore.Storage.SharpCompress
OwlCore.Storage implementations for SharpCompress. Allows you to treat an archive file as a normal folder.

## Install

Published releases are available on [NuGet](https://www.nuget.org/packages/OwlCore.Storage.SharpCompress). To install, run the following command in the [Package Manager Console](https://docs.nuget.org/docs/start-here/using-the-package-manager-console).

    PM> Install-Package OwlCore.Storage.SharpCompress
    
Or using [dotnet](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet)

    > dotnet add package OwlCore.Storage.SharpCompress

## Basic usage

```cs
// Can be SystemFile, WindowsStorageFile, OneDriveFile, HttpFile, IpfsFile, FtpFile, StreamFile, etc.
IFile file = new SystemFile("C:\\archive.7z");

// Read supported: rar, 7zip, zip, tar, bzip2, gzip, lzip
var archiveFolder = new ReadOnlyArchiveFolder(file);

// Read/Write supported: zip, tar, bzip2, gzip, lzip
var archiveFolder2 = new ArchiveFolder(file);

await foreach (var item in archiveFolder.GetItemsAsync())
{
    if (item is IFile file)
    {
        // ...   
    }

    if (item is IFolder folder)
    {
        // ...   
    }
}
```