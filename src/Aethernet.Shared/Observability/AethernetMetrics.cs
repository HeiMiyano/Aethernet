using Prometheus;

namespace Aethernet.Shared.Observability;

/// <summary>
/// Centralized counter/gauge/histogram definitions so every service emits names with the
/// same labels and the Grafana dashboards Just Work.
/// </summary>
public static class AethernetMetrics
{
    public static readonly Gauge   HubConnectionsActive = Metrics
        .CreateGauge("aethernet_hub_connections_active",
                     "Live SignalR connections on this hub instance.");

    public static readonly Counter HubMessagesIn = Metrics
        .CreateCounter("aethernet_hub_messages_in_total",
                       "Hub methods invoked by clients.", "method");

    public static readonly Counter HubPushRecipients = Metrics
        .CreateCounter("aethernet_hub_push_recipients_total",
                       "Recipients fanned-out per UserPushData call.");

    public static readonly Histogram HubPushDuration = Metrics
        .CreateHistogram("aethernet_hub_push_duration_seconds",
                         "Wall-clock time spent dispatching a UserPushData.",
                         new HistogramConfiguration
                         {
                             Buckets = Histogram.ExponentialBuckets(0.005, 2, 12),
                         });

    public static readonly Counter HubErrors = Metrics
        .CreateCounter("aethernet_hub_errors_total",
                       "Hub method invocations that threw.", "method", "code");

    public static readonly Counter AuthLoginAttempts = Metrics
        .CreateCounter("aethernet_auth_login_attempts_total",
                       "Login attempts.", "outcome");

    public static readonly Counter AuthRegistrations = Metrics
        .CreateCounter("aethernet_auth_registrations_total",
                       "Account registrations.", "via");

    public static readonly Counter AuthRefreshes = Metrics
        .CreateCounter("aethernet_auth_refresh_total",
                       "Refresh-token rotations.", "outcome");

    public static readonly Counter FilesUploadBytes = Metrics
        .CreateCounter("aethernet_files_upload_bytes_total",
                       "Bytes accepted by the file server (post-decompression).");

    public static readonly Counter FilesDownloadBytes = Metrics
        .CreateCounter("aethernet_files_download_bytes_total",
                       "Bytes served by the file server.");

    public static readonly Counter FilesUploadDedupeHits = Metrics
        .CreateCounter("aethernet_files_upload_dedupe_total",
                       "Uploads where the hash was already present.");

    public static readonly Counter FilesOrphanGc = Metrics
        .CreateCounter("aethernet_files_orphan_gc_total",
                       "Blob GC actions taken.", "kind");
}
