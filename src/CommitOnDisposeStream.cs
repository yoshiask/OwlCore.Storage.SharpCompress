using System;
using System.IO;
using System.Threading.Tasks;

namespace OwlCore.Storage.SharpCompress;

internal sealed class CommitOnDisposeMemoryStream : MemoryStream
{
    private readonly Func<byte[], Task> _commitAsync;
    private bool _committed;

    public CommitOnDisposeMemoryStream(byte[] initialBytes, Func<byte[], Task> commitAsync)
    {
        _commitAsync = commitAsync;

        if (initialBytes.Length > 0)
        {
            Write(initialBytes, 0, initialBytes.Length);
            Position = 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_committed)
        {
            _committed = true;
            _commitAsync(ToArray()).GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}