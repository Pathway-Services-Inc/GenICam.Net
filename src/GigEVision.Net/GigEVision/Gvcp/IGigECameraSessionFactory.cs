namespace GenICam.Net.GigEVision.Gvcp;

/// <summary>
/// Creates connected GigE Vision camera sessions.
/// </summary>
public interface IGigECameraSessionFactory
{
    /// <param name="camera">Camera to connect to, as returned by discovery.</param>
    /// <param name="xmlSaveDirectory">Directory to save the camera XML to; defaults to <c>camera-xml</c> under the application base directory.</param>
    /// <param name="prefetchNodeValues">
    /// When true (default) every readable node value is read once after connecting so later accesses
    /// are served from cache. This costs one GVCP round-trip per node, which can take many seconds on
    /// cameras with 1000+ nodes; pass false to defer reads until each node is first used.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IGigECameraSession> ConnectAsync(
        GigECameraInfo camera,
        string? xmlSaveDirectory = null,
        bool prefetchNodeValues = true,
        CancellationToken cancellationToken = default);
}
