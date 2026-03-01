using System;
using OwlCore.Storage;
using SharpCompress.Common;

namespace OwlCore.Storage.SharpCompress;

/// <summary>Creation timestamp property for archive entries.</summary>
/// <remarks>
/// Returns null when the archive format does not support this timestamp (e.g., TAR, GZip, Arc).
/// Supported formats: Zip, Rar, 7zip, Arj.
/// </remarks>
public sealed class ArchiveEntryCreatedAtProperty(IStorable owner, IEntry entry)
    : SimpleStorageProperty<DateTime?>(
        id: owner.Id + "/" + nameof(ICreatedAt.CreatedAt),
        name: nameof(ICreatedAt.CreatedAt),
        getter: () => entry.CreatedTime
    ), ICreatedAtProperty;

/// <summary>Creation timestamp property (DateTimeOffset) for archive entries.</summary>
public sealed class ArchiveEntryCreatedAtOffsetProperty(IStorable owner, IEntry entry)
    : SimpleStorageProperty<DateTimeOffset?>(
        id: owner.Id + "/" + nameof(ICreatedAtOffset.CreatedAtOffset),
        name: nameof(ICreatedAtOffset.CreatedAtOffset),
        getter: () => entry.CreatedTime.HasValue ? new DateTimeOffset(entry.CreatedTime.Value) : null
    ), ICreatedAtOffsetProperty;
