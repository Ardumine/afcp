using testeMulti.Interfaces;

namespace testeMulti.Request;

public abstract class RequestBasedStreamClient
{
    private protected readonly IMultiStreamClient MultiStreamClient;

    internal RequestBasedStreamClient(IMultiStreamClient client)
    {
        MultiStreamClient = client;
    }

    public abstract ReadOnlySpan<byte> SendRequest(ReadOnlySpan<byte> requestData, CancellationToken ct = default);
}

public abstract class RequestBasedStreamServer(IMultiStreamServer server)
{
    public delegate void RequestEvent(object sender, RequestEventArgs e);

    public event RequestEvent? OnRequest;
    protected readonly IMultiStreamServer MultiStreamServer = server;

    protected virtual void RaiseRequestEvent(RequestEventArgs e)
    {
        OnRequest?.Invoke(this, e);
    }
}