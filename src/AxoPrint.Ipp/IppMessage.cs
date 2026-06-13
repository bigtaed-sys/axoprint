namespace AxoPrint.Ipp;

/// <summary>
/// Base for an IPP request/response message (everything before the document
/// data): version, request-id and the ordered attribute groups.
/// </summary>
public abstract class IppMessage
{
    public IppVersion Version { get; set; } = IppVersion.V1_1;
    public int RequestId { get; set; } = 1;
    public List<IppAttributeGroup> Groups { get; } = new();

    public IppAttributeGroup OperationGroup =>
        GetOrAddGroup(IppTag.OperationAttributes);

    public IppAttributeGroup GetOrAddGroup(IppTag tag)
    {
        var g = Groups.FirstOrDefault(x => x.Tag == tag);
        if (g is null)
        {
            g = new IppAttributeGroup(tag);
            Groups.Add(g);
        }
        return g;
    }

    public IppAttributeGroup? FirstGroup(IppTag tag) =>
        Groups.FirstOrDefault(g => g.Tag == tag);

    /// <summary>Look up an attribute by name across all groups.</summary>
    public IppAttribute? Find(string name)
    {
        foreach (var g in Groups)
            if (g[name] is { } a)
                return a;
        return null;
    }
}

public sealed class IppRequest : IppMessage
{
    public IppOperation Operation { get; set; }

    public IppRequest() { }

    public IppRequest(IppOperation operation, int requestId = 1)
    {
        Operation = operation;
        RequestId = requestId;
    }
}

public sealed class IppResponse : IppMessage
{
    public IppStatus Status { get; set; } = IppStatus.SuccessfulOk;

    public IppResponse() { }

    public IppResponse(IppStatus status, int requestId)
    {
        Status = status;
        RequestId = requestId;
    }
}
