using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GenICam.Net.GigEVision.Gvsp;
using Microsoft.Extensions.Logging;

namespace GenICam.Net.Tests.GigEVision.Gvsp;

[TestFixture]
public class GvspStreamSessionTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    [Test]
    public async Task Start_NoPacketsArrive_LogsWarningAfterDelay()
    {
        var logger = new CapturingLogger<GvspStreamSession>();
        using var session = new GvspStreamSession(logger) { NoPacketWarningDelayMs = 100 };

        session.Start(0);
        await Task.Delay(500);

        Assert.That(
            logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("No GVSP packets received")),
            Is.True,
            "A no-packets warning should be logged when nothing arrives within the delay.");
    }

    [Test]
    public async Task Start_PacketArrives_DoesNotWarnAndLogsFirstPacket()
    {
        var logger = new CapturingLogger<GvspStreamSession>();
        using var session = new GvspStreamSession(logger) { NoPacketWarningDelayMs = 300 };

        var port = session.Start(0);

        using var sender = new UdpClient();
        await sender.SendAsync(new byte[] { 1, 2, 3, 4 }, 4, new IPEndPoint(IPAddress.Loopback, port));

        // Wait for the packet to be counted, then past the warning deadline.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (session.PacketStats.ReceivedPacketCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await Task.Delay(500);

        Assert.That(session.PacketStats.ReceivedPacketCount, Is.GreaterThan(0),
            "The test packet should have been received.");
        Assert.That(
            logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("No GVSP packets received")),
            Is.False,
            "No warning should be logged when a packet arrives in time.");
        Assert.That(
            logger.Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains("First GVSP packet received")),
            Is.True,
            "The first packet should be logged at information level.");
    }

    [Test]
    public async Task Stop_BeforeWarningDeadline_DoesNotWarn()
    {
        var logger = new CapturingLogger<GvspStreamSession>();
        using var session = new GvspStreamSession(logger) { NoPacketWarningDelayMs = 200 };

        session.Start(0);
        session.Stop();
        await Task.Delay(500);

        Assert.That(
            logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("No GVSP packets received")),
            Is.False,
            "Stopping before the deadline should suppress the no-packets warning.");
    }
}
