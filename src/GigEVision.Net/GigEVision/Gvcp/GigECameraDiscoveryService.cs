using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenICam.Net.GigEVision.Gvcp;

/// <summary>
/// Application-level discovery facade for GigE Vision cameras.
/// </summary>
/// <remarks>
/// The GVCP discovery broadcast (255.255.255.255) egresses only one interface
/// per socket, chosen by the OS routing table. On multi-homed hosts — e.g. one
/// NIC per camera — a single unbound socket therefore only ever discovers the
/// cameras on one subnet. This service binds one socket per local IPv4 address
/// and broadcasts on all of them concurrently, merging the responses.
/// </remarks>
public sealed class GigECameraDiscoveryService : IGigECameraDiscoveryService
{
    private readonly ILogger<GigECameraDiscoveryService> _logger;
    private readonly Func<IReadOnlyList<IPAddress>> _localAddressProvider;
    private readonly Func<IPAddress, IUdpTransport> _transportFactory;

    public GigECameraDiscoveryService(ILogger<GigECameraDiscoveryService>? logger = null)
        : this(logger, GetLocalIPv4Addresses, CreateBoundTransport)
    {
    }

    /// <summary>
    /// Test seam: inject the local-address enumeration and the per-address
    /// transport factory so discovery can run without real sockets.
    /// </summary>
    internal GigECameraDiscoveryService(
        ILogger<GigECameraDiscoveryService>? logger,
        Func<IReadOnlyList<IPAddress>> localAddressProvider,
        Func<IPAddress, IUdpTransport> transportFactory)
    {
        _logger = logger ?? NullLogger<GigECameraDiscoveryService>.Instance;
        _localAddressProvider = localAddressProvider;
        _transportFactory = transportFactory;
    }

    public async Task<IReadOnlyList<GigECameraInfo>> DiscoverAsync(
        int timeoutMs = 3000,
        CancellationToken cancellationToken = default)
    {
        var localAddresses = _localAddressProvider();
        if (localAddresses.Count == 0)
        {
            // No usable interfaces enumerated — fall back to a socket bound to
            // 0.0.0.0 and let the OS pick the egress interface (old behavior).
            _logger.LogWarning("No local IPv4 addresses found; broadcasting discovery on the default interface only");
            localAddresses = [IPAddress.Any];
        }
        else
        {
            _logger.LogInformation("Broadcasting discovery on {Count} local interface(s)", localAddresses.Count);
        }

        var results = await Task.WhenAll(
            localAddresses.Select(addr => DiscoverOnInterfaceAsync(addr, timeoutMs, cancellationToken)));

        // The same camera can answer on more than one interface when NICs share
        // a broadcast domain, so dedupe across interfaces by MAC address.
        var cameras = new List<GigECameraInfo>();
        var seen = new HashSet<string>();
        foreach (var cam in results.SelectMany(r => r))
        {
            var key = cam.MacAddress.Length > 0
                ? Convert.ToHexString(cam.MacAddress)
                : cam.IpAddress.ToString();
            if (!seen.Add(key))
                continue;

            _logger.LogInformation(
                "Discovered camera: {Vendor} {Model} at {IpAddress}",
                cam.ManufacturerName,
                cam.ModelName,
                cam.IpAddress);
            cameras.Add(cam);
        }

        return cameras.AsReadOnly();
    }

    private async Task<IReadOnlyList<GigECameraInfo>> DiscoverOnInterfaceAsync(
        IPAddress localAddress,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            using var transport = _transportFactory(localAddress);
            using var discovery = new GigEDiscovery(transport);
            return await discovery.DiscoverAsync(timeoutMs: timeoutMs, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            // Some adapters (VPN, virtual switches) refuse broadcast or vanish
            // mid-discovery; skip them rather than failing the whole scan.
            _logger.LogWarning(ex, "Discovery failed on local interface {Address}; skipping it", localAddress);
            return Array.Empty<GigECameraInfo>();
        }
    }

    private static IReadOnlyList<IPAddress> GetLocalIPv4Addresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(ua => ua.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static IUdpTransport CreateBoundTransport(IPAddress localAddress)
        => new UdpTransportAdapter(new UdpClient(new IPEndPoint(localAddress, 0)));
}
