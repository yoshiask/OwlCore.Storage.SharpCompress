using System;
using OwlCore.Storage;
using SharpCompress.Common;

namespace OwlCore.Storage.SharpCompress;

/// <summary>Last modified timestamp property for archive entries.</summary>
/// <remarks>
/// Supported by most archive formats. Returns null for Arc format.
/// </remarks>
public sealed class ArchiveEntryLastModifiedAtProperty(IStorable owner, IEntry? entry)
    : SimpleStorageProperty<DateTime?>(
        id: owner.Id + "/" + nameof(ILastModifiedAt.LastModifiedAt),
        name: nameof(ILastModifiedAt.LastModifiedAt),
        getter: () => entry?.LastModifiedTime
    ), ILastModifiedAtProperty;

/// <summary>Last modified timestamp property (DateTimeOffset) for archive entries.</summary>
public sealed class ArchiveEntryLastModifiedAtOffsetProperty(IStorable owner, IEntry? entry)
    : SimpleStorageProperty<DateTimeOffset?>(
        id: owner.Id + "/" + nameof(ILastModifiedAtOffset.LastModifiedAtOffset),
        name: nameof(ILastModifiedAtOffset.LastModifiedAtOffset),
        getter: () => entry is null ? null : entry.LastModifiedTime.HasValue ? new DateTimeOffset(entry.LastModifiedTime.Value) : null
    ), ILastModifiedAtOffsetProperty;
