using System;
using System.Text;

namespace AFCP.Core.Transformers
{
    public class HTTPTranformer : ApresentationTransformer
    {
        private static readonly byte[] _httpServerResponse = """ 
HTTP/1.1 200 OK
Content-Type: application/octet-stream
Transfer-Encoding: chunked
Server: nginx/1.18.0
Connection: keep-alive

"""u8.ToArray();

        private static readonly byte[] _httpClientRequest = """ 
GET /api/stream/video HTTP/1.1
Host: example.com
Accept: application/octet-stream 
Connection: keep-alive  

"""u8.ToArray();

        public HTTPTranformer(Streamy baseStream) : base(baseStream)
        {
        }

        public override ApresentationTransformer Initialize(StreamyParameters parameters)
        {
            if (parameters.IsServer)
            {
                //Read the HTTP request
                Span<byte> request = new byte[_httpClientRequest.Length];
                _baseStream.Read(request);

                //Send HTTP response
                _baseStream.Write(_httpServerResponse);
            }
            else
            {
                //Send HTTP request
                _baseStream.Write(_httpClientRequest);

                //Read the HTTP response
                Span<byte> response = new byte[_httpServerResponse.Length];
                _baseStream.Read(response);
            }

            //Initialize the parent stream
            _baseStream.Initialize(parameters);

            return this;
        }

        public override int Read(Span<byte> buffer)
        {
            //No need to change the data
            return _baseStream.Read(buffer);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            //No need to change the data
            _baseStream.Write(buffer);
        }

        //Function used to read all data from the stream. It can get handled by the main stream most times.
        public override ReadOnlySpan<byte> ReadAll()
        {
            //No need to change the data
            return _baseStream.ReadAll();
        }
    }
}
