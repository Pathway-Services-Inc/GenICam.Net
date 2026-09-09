using System.Net;
using System.Net.Sockets;
using GenICam.Net.GigEVision.Gvcp;

namespace GenICam.Net.Tests.GigEVision.Gvcp;

[TestFixture]
public class GigECameraDiscoveryServiceTests
{
    private static readonly IPAddress Nic1 = IPAddress.Parse("192.168.1.10");
    private static readonly IPAddress Nic2 = IPAddress.Parse("192.168.2.10");

    private static GigECameraInfo MakeCamera(byte macSuffix, string serial, string ip) => new()
    {
        SpecVersionMajor = 2,
        SpecVersionMinor = 0,
        MacAddress = [0x00, 0x11, 0x22, 0x33, 0x44, macSuffix],
        ManufacturerName = "Vendor",
        ModelName = "Cam",
        SerialNumber = serial,
        IpAddress = IPAddress.Parse(ip),
        SubnetMask = IPAddress.Parse("255.255.255.0"),
    };

    private static byte[] BuildDiscoveryAck(GigECameraInfo camera)
    {
        var payload = camera.ToPayload();
        var header = new GvcpAckHeader(GvcpStatus.Success, GvcpCommandType.DiscoveryAck, (ushort)payload.Length, 1);
        var ack = new byte[GvcpConstants.AckHeaderSize + payload.Length];
        header.ToBytes().CopyTo(ack, 0);
        payload.CopyTo(ack, GvcpConstants.AckHeaderSize);
        return ack;
    }

    [Test]
    public async Task DiscoverAsync_BroadcastsOnEveryLocalInterface_AndMergesResults()
    {
        var transports = new Dictionary<IPAddress, FakeUdpTransport>
        {
            [Nic1] = new FakeUdpTransport(),
            [Nic2] = new FakeUdpTransport(),
        };
        transports[Nic1].EnqueueReceive(BuildDiscoveryAck(MakeCamera(0x01, "SN001", "192.168.1.100")));
        transports[Nic2].EnqueueReceive(BuildDiscoveryAck(MakeCamera(0x02, "SN002", "192.168.2.100")));

        var service = new GigECameraDiscoveryService(null, () => [Nic1, Nic2], addr => transports[addr]);
        var cameras = await service.DiscoverAsync(timeoutMs: 200);

        Assert.That(cameras, Has.Count.EqualTo(2));
        Assert.That(cameras.Select(c => c.SerialNumber), Is.EquivalentTo(new[] { "SN001", "SN002" }));
        Assert.That(transports[Nic1].SentPackets, Is.Not.Empty,
            "Discovery must broadcast on the first interface.");
        Assert.That(transports[Nic2].SentPackets, Is.Not.Empty,
            "Discovery must broadcast on the second interface.");
    }

    [Test]
    public async Task DiscoverAsync_SameCameraOnTwoInterfaces_IsDedupedByMac()
    {
        var camera = MakeCamera(0x01, "SN001", "192.168.1.100");
        var transports = new Dictionary<IPAddress, FakeUdpTransport>
        {
            [Nic1] = new FakeUdpTransport(),
            [Nic2] = new FakeUdpTransport(),
        };
        transports[Nic1].EnqueueReceive(BuildDiscoveryAck(camera));
        transports[Nic2].EnqueueReceive(BuildDiscoveryAck(camera));

        var service = new GigECameraDiscoveryService(null, () => [Nic1, Nic2], addr => transports[addr]);
        var cameras = await service.DiscoverAsync(timeoutMs: 200);

        Assert.That(cameras, Has.Count.EqualTo(1));
        Assert.That(cameras[0].SerialNumber, Is.EqualTo("SN001"));
    }

    [Test]
    public async Task DiscoverAsync_FailingInterface_DoesNotPreventDiscoveryOnOthers()
    {
        var workingTransport = new FakeUdpTransport();
        workingTransport.EnqueueReceive(BuildDiscoveryAck(MakeCamera(0x01, "SN001", "192.168.1.100")));

        var service = new GigECameraDiscoveryService(
            null,
            () => [Nic1, Nic2],
            addr => addr.Equals(Nic1) ? workingTransport : throw new SocketException((int)SocketError.AccessDenied));

        var cameras = await service.DiscoverAsync(timeoutMs: 200);

        Assert.That(cameras, Has.Count.EqualTo(1));
        Assert.That(cameras[0].SerialNumber, Is.EqualTo("SN001"));
    }

    [Test]
    public async Task DiscoverAsync_NoLocalAddresses_FallsBackToDefaultInterface()
    {
        var fallbackTransport = new FakeUdpTransport();
        fallbackTransport.EnqueueReceive(BuildDiscoveryAck(MakeCamera(0x01, "SN001", "192.168.1.100")));

        IPAddress? requestedAddress = null;
        var service = new GigECameraDiscoveryService(
            null,
            () => [],
            addr => { requestedAddress = addr; return fallbackTransport; });

        var cameras = await service.DiscoverAsync(timeoutMs: 200);

        Assert.That(requestedAddress, Is.EqualTo(IPAddress.Any),
            "With no enumerable interfaces the service should bind to 0.0.0.0 and let the OS route the broadcast.");
        Assert.That(cameras, Has.Count.EqualTo(1));
    }
}
