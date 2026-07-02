using System.Collections.Concurrent;
using System.Net;

namespace AFCP;

/// <summary>
/// Selects an <see cref="IConnection"/> from a target spec string. Lets a peer
/// express "how to reach the other side" without the upper layers knowing the
/// transport — the multi-transport selection the protocol needs (WiFi, Ethernet,
/// serial).
///
/// Supported schemes:
/// <list type="bullet">
/// <item><c>tcp://host:port</c>           — TCP (WiFi or Ethernet, same API).</item>
/// <item><c>serial:///dev/ttyUSB0?baud=115200</c> — serial port.</item>
/// <item><c>inmem://&lt;key&gt;</c>         — in-memory pair (tests; registered via <see cref="RegisterInMemory"/>).</item>
/// </list>
///
/// Additional schemes can be registered with <see cref="Register"/>.
/// </summary>
public static class TransportRegistry
{
    private static readonly ConcurrentDictionary<string, Func<Uri, IConnection>> _schemes = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, (InMemoryConnection A, InMemoryConnection B)> _inMem = new();

    static TransportRegistry()
    {
        _schemes.TryAdd("tcp", uri =>
        {
            var host = uri.Host;
            var port = uri.IsDefaultPort ? 0 : uri.Port;
            if (port == 0) throw new ArgumentException($"tcp:// target '{uri}' needs an explicit port");
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length == 0) throw new ArgumentException($"Cannot resolve hostname '{host}'.");
            var endpoint = new IPEndPoint(addresses[0], port);
            return new TcpConnection(endpoint);
        });
        _schemes.TryAdd("serial", uri =>
        {
            var device = uri.LocalPath;
            var baud = 115200;
            var query = uri.Query.TrimStart('?');
            if (!string.IsNullOrEmpty(query))
            {
                foreach (var pair in query.Split('&'))
                {
                    var kv = pair.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("baud", StringComparison.OrdinalIgnoreCase))
                        baud = int.Parse(kv[1]);
                }
            }
            return new SerialConnection(device, baud);
        });
    }

    /// <summary>Open a connection to the given target spec.</summary>
    public static IConnection Open(string target)
    {
        var uri = new Uri(target);
        if (_schemes.TryGetValue(uri.Scheme, out var factory))
            return factory(uri);
        throw new ArgumentException($"Unknown transport scheme '{uri.Scheme}' in '{target}'. Register it via TransportRegistry.Register.");
    }

    /// <summary>Register a custom scheme handler. Thread-safe.</summary>
    public static void Register(string scheme, Func<Uri, IConnection> factory)
        => _schemes.AddOrUpdate(scheme.ToLowerInvariant(), factory, (_, _) => factory);

    /// <summary>
    /// Register an in-memory pair under a key (for tests). Returns the two endpoints
    /// — open one here, the peer opens the other via <c>inmem://key</c>.
    /// Thread-safe; supports multiple keys concurrently.
    /// </summary>
    public static (IConnection A, IConnection B) RegisterInMemory(string key)
    {
        var (a, b) = InMemoryConnection.CreatePair();
        _inMem[key] = (a, b);

        Register("inmem", uri =>
        {
            var lookupKey = uri.Host;
            if (!_inMem.TryGetValue(lookupKey, out var pair))
                throw new InvalidOperationException($"No in-memory pair registered for key '{lookupKey}'.");
            return pair.B;
        });

        return (a, b);
    }
}
