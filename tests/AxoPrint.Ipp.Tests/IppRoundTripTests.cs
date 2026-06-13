using AxoPrint.Ipp;
using Xunit;

namespace AxoPrint.Ipp.Tests;

public class IppRoundTripTests
{
    private static IppMessage RoundTrip(IppMessage msg, bool asResponse)
    {
        byte[] bytes = IppWriter.Encode(msg);
        using var ms = new MemoryStream(bytes);
        return IppReader.Read(ms, asResponse);
    }

    [Fact]
    public void Request_Header_RoundTrips()
    {
        var req = new IppRequest(IppOperation.GetPrinterAttributes, requestId: 42)
        {
            Version = IppVersion.V2_0,
        };
        req.OperationGroup
            .Add(IppAttribute.Charset("attributes-charset", "utf-8"))
            .Add(IppAttribute.Language("attributes-natural-language", "en"))
            .Add(IppAttribute.Uri("printer-uri", "ipp://host/printers/test"));

        var back = (IppRequest)RoundTrip(req, asResponse: false);

        Assert.Equal(IppOperation.GetPrinterAttributes, back.Operation);
        Assert.Equal(42, back.RequestId);
        Assert.Equal(new IppVersion(2, 0), back.Version);
        Assert.Equal("utf-8", back.Find("attributes-charset")!.AsString());
        Assert.Equal("ipp://host/printers/test", back.Find("printer-uri")!.AsString());
    }

    [Fact]
    public void Response_Status_And_ScalarTypes_RoundTrip()
    {
        var res = new IppResponse(IppStatus.SuccessfulOk, 7);
        var pg = res.GetOrAddGroup(IppTag.PrinterAttributes);
        pg.Add(IppAttribute.Enum("printer-state", (int)IppPrinterState.Idle));
        pg.Add(IppAttribute.Boolean("printer-is-accepting-jobs", true));
        pg.Add(IppAttribute.Integer("queued-job-count", 3));
        pg.Add(new IppAttribute("printer-resolution-default", IppValue.Resolution(IppResolution.Dpi(600))));
        pg.Add(new IppAttribute("copies-supported", IppValue.Range(1, 999)));

        var back = (IppResponse)RoundTrip(res, asResponse: true);

        Assert.Equal(IppStatus.SuccessfulOk, back.Status);
        Assert.Equal((int)IppPrinterState.Idle, back.Find("printer-state")!.AsInt());
        Assert.True(back.Find("printer-is-accepting-jobs")!.AsBool());
        Assert.Equal(3, back.Find("queued-job-count")!.AsInt());
        var res2 = (IppResolution)back.Find("printer-resolution-default")!.First.Value!;
        Assert.Equal(600, res2.Feed);
        var range = (IppRange)back.Find("copies-supported")!.First.Value!;
        Assert.Equal(new IppRange(1, 999), range);
    }

    [Fact]
    public void OneSetOf_MultipleValues_RoundTrip()
    {
        var res = new IppResponse(IppStatus.SuccessfulOk, 1);
        var pg = res.GetOrAddGroup(IppTag.PrinterAttributes);
        pg.Add(IppAttribute.Mime("document-format-supported", "application/pdf", "image/pwg-raster", "image/jpeg"));

        var back = (IppResponse)RoundTrip(res, asResponse: true);

        var formats = back.Find("document-format-supported")!.AsStrings().ToArray();
        Assert.Equal(new[] { "application/pdf", "image/pwg-raster", "image/jpeg" }, formats);
    }

    [Fact]
    public void Collection_MediaCol_RoundTrips()
    {
        var res = new IppResponse(IppStatus.SuccessfulOk, 1);
        var pg = res.GetOrAddGroup(IppTag.PrinterAttributes);

        var size = new IppCollection()
            .Add(IppAttribute.Integer("x-dimension", 21000))
            .Add(IppAttribute.Integer("y-dimension", 29700));
        var mediaCol = new IppCollection()
            .Add(new IppAttribute("media-size", IppValue.Collection(size)))
            .Add(IppAttribute.Keyword("media-type", "stationery"));

        pg.Add(new IppAttribute("media-col-default", IppValue.Collection(mediaCol)));

        var back = (IppResponse)RoundTrip(res, asResponse: true);

        var col = back.Find("media-col-default")!.First.AsCollection();
        Assert.Equal("stationery", col["media-type"]!.AsString());
        var nested = col["media-size"]!.First.AsCollection();
        Assert.Equal(21000, nested["x-dimension"]!.AsInt());
        Assert.Equal(29700, nested["y-dimension"]!.AsInt());
    }

    [Fact]
    public void DocumentData_FollowsAttributes()
    {
        var req = new IppRequest(IppOperation.PrintJob, 1);
        req.OperationGroup.Add(IppAttribute.Charset("attributes-charset", "utf-8"));

        using var ms = new MemoryStream();
        IppWriter.Write(ms, req);
        byte[] doc = { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        ms.Write(doc);
        ms.Position = 0;

        var back = IppReader.Read(ms);
        Assert.Equal(IppOperation.PrintJob, ((IppRequest)back).Operation);

        var rest = new byte[4];
        Assert.Equal(4, ms.Read(rest));
        Assert.Equal(doc, rest);
    }
}
