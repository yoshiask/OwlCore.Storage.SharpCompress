using System;
using OwlCore.Storage;
using SharpCompress.Common;

namespace OwlCore.Storage.SharpCompress;

/// <summary>Last accessed timestamp property for archive entries.</summary>
/// <remarks>
/// Returns null when the archive format does not support this timestamp (e.g., TAR, GZip, Arc).
/// Supported formats: Zip, Rar, 7zip, Arj.
/// </remarks>
public sealed class ArchiveEntryLastAccessedAtProperty(IStorable owner, IEntry entry)
    : SimpleStorageProperty<DateTime?>(
        id: owner.Id + "/" + nameof(ILastAccessedAt.LastAccessedAt),
        name: nameof(ILastAccessedAt.LastAccessedAt),
        getter: () => entry.LastAccessedTime
    ), ILastAccessedAtProperty;

/// <summary>Last accessed timestamp property (DateTimeOffset) for archive entries.</summary>
public sealed class ArchiveEntryLastAccessedAtOffsetProperty(IStorable owner, IEntry entry)
    : SimpleStorageProperty<DateTimeOffset?>(
        id: owner.Id + "/" + nameof(ILastAccessedAtOffset.LastAccessedAtOffset),
        name: nameof(ILastAccessedAtOffset.LastAccessedAtOffset),
        getter: () => entry.LastAccessedTime.HasValue ? new DateTimeOffset(entry.LastAccessedTime.Value) : null
    ), ILastAccessedAtOffsetProperty;
