using testeMulti.Interfaces;
using testeMulti.Streams;

namespace testeMulti.Request;

public class RequestStreamServer(IMultiStreamServer server) : RequestBasedStreamServer(server)
{
    public void HandleRequests()
    {
        var client = MultiStreamServer.HandleConnections();
        ICountableStream stream = new DataCompletionStream(client);
        stream = new CheckSumBasedStream(stream);

        RaiseRequestEvent(new RequestEventArgs(stream));
        client.Close();
        client.Dispose();
    }
}

public class RequestStreamClient(IMultiStreamClient client) : RequestBasedStreamClient(client)
{
    public override ReadOnlySpan<byte> SendRequest(ReadOnlySpan<byte> requestData, CancellationToken ct = default)
    {
        var client = MultiStreamClient.CreateStream();

        ICountableStream stream = new DataCompletionStream(client);
        stream = new CheckSumBasedStream(stream);

        stream.Write(requestData);
        var data = stream.Read();


        client.Close();
        client.Dispose();

        return data;
    }
}