namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// A read-only wrapper that stops handing bytes over once a budget is spent, and remembers that it did.
///
/// It reports end of stream instead of throwing on purpose: the reader on top of it is an <see cref="System.Xml.XmlReader"/>,
/// and letting it fail on an unexpected end of document is exactly the behaviour wanted - the caller then asks
/// <see cref="BudgetExceeded"/> to tell «this board is too big for one feed» apart from «this site answered with
/// something that is not the document it should be». Both are refusals to commit, but only the first one has a
/// fallback worth trying.
/// </summary>
public sealed class ByteBudgetStream(Stream inner, long budget) : Stream
{
    private long read;

    /// <summary>The budget ran out before the body ended, so whatever was parsed is not the whole document.</summary>
    public bool BudgetExceeded { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var allowed = Allowed(count);

        if (allowed == 0)
            return 0;

        var got = inner.Read(buffer, offset, allowed);
        read += got;

        return got;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var allowed = Allowed(buffer.Length);

        if (allowed == 0)
            return 0;

        var got = await inner.ReadAsync(buffer[..allowed], ct);
        read += got;

        return got;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Allowed(int count)
    {
        var left = budget - read;

        if (left <= 0)
        {
            BudgetExceeded = true;

            return 0;
        }

        return (int)Math.Min(count, left);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }
}
