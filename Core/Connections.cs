using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PersonalDownloader;

public sealed record Adapter(string Id, string Name)
{
    public NetworkInterfaceType Type { get; init; }
    public string Description { get; init; } = "";
    public override string ToString() => Name;
}

public static class Connections
{
    private static IEnumerable<NetworkInterface> Active() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

    public static Adapter[] List() => Active().Where(HasAddress).Select(n => new Adapter(n.Id, n.Name)
        { Type = n.NetworkInterfaceType, Description = n.Description }).ToArray();

    static bool HasAddress(NetworkInterface network)
    {
        try { return network.GetIPProperties().UnicastAddresses.Count > 0; }
        catch (NetworkInformationException) { return false; }
    }

    public static HttpClient Create(string? interfaceId, Action<string>? observeSystemRoute = null)
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) };
        if (interfaceId == null && observeSystemRoute != null)
        {
            handler.ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, token);
                    var ip = ((IPEndPoint)socket.LocalEndPoint!).Address;
                    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
                    string route = "Windows route";
                    foreach (var nic in Active())
                    {
                        try
                        {
                            if (nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(ip)))
                            { route = nic.Name; break; }
                        }
                        catch (NetworkInformationException) { }
                    }
                    observeSystemRoute(route);
                    return new NetworkStream(socket, true);
                }
                catch { socket.Dispose(); throw; }
            };
        }
        if (interfaceId != null)
        {
            handler.UseProxy = false;
            handler.ConnectCallback = async (context, token) =>
            {
                var properties = Active().FirstOrDefault(n => n.Id == interfaceId)?.GetIPProperties()
                    ?? throw new IOException("Selected network is offline.");
                Exception? last = null;
                foreach (var remote in await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token))
                {
                    var local = properties.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == remote.AddressFamily);
                    if (local == null) continue;
                    var socket = new Socket(remote.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            bool v4 = remote.AddressFamily == AddressFamily.InterNetwork;
                            int index = v4 ? properties.GetIPv4Properties().Index : properties.GetIPv6Properties().Index;
                            socket.SetSocketOption(v4 ? SocketOptionLevel.IP : SocketOptionLevel.IPv6,
                                (SocketOptionName)31, v4 ? IPAddress.HostToNetworkOrder(index) : index);
                        }
                        socket.Bind(new IPEndPoint(local.Address, 0));
                        await socket.ConnectAsync(new IPEndPoint(remote, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, true);
                    }
                    catch (Exception e) { socket.Dispose(); token.ThrowIfCancellationRequested(); last = e; }
                }
                throw new IOException("Could not connect through the selected network.", last);
            };
        }
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
