using System.Buffers.Binary;
using SaveEditor.GtaV;

namespace SaveEditor.Tests;

public class MalformedSaveTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    public void TruncatedChecksum_IsReportedAsInvalidFormat(int checksumLength)
    {
        var body = new byte[8 + checksumLength];
        "CHKS"u8.CopyTo(body);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4), body.Length);

        Assert.Throws<SaveFormatException>(() => BlockContainer.Parse(body));
    }

    [Fact]
    public void OverflowingBlockLength_IsReportedAsInvalidFormat()
    {
        var body = new byte[24];
        "PSIN"u8.CopyTo(body);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4), 16);
        "CHKS"u8.CopyTo(body.AsSpan(16));
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(20), int.MaxValue);

        Assert.Throws<SaveFormatException>(() => BlockContainer.Parse(body));
    }

    [Fact]
    public void TruncatedChecksumAfterParsing_IsReportedAsInvalidFormat()
    {
        var body = BlockContainer.Parse(GtaVSaveTests.BuildBody(("PSIN", new byte[16])));
        body.Checksum.Data = new byte[4];

        Assert.Throws<SaveFormatException>(() => body.Verify());
    }
}
