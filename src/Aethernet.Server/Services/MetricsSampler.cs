using Prometheus;

namespace Aethernet.Server.Services;

/// <summary>One minute's snapshot. Counter fields are the raw cumulative values (null = that
/// service couldn't be scraped this minute); <see cref="MetricsSampler.Buckets"/> turns them into rates.</summary>
public sealed record MetricsSample(
    DateTime At,
    int Online,
    double? MessagesIn,
    double? PushRecipients,
    double? UploadBytes,
    double? DownloadBytes,
    double? LoginsOk,
    double? LoginsFailed,
    double? Signups);

public sealed record MetricsBucket(
    DateTime At, int? Online, double? MessagesIn, double? PushRecipients,
    double? UploadBytes, double? DownloadBytes, double? LoginsOk, double? LoginsFailed, double? Signups);

/// <summary>
/// Feeds the dashboard's Overview tab without a Prometheus server: once a minute it reads this
/// process's own metrics plus the auth and file services' /metrics over the internal docker
/// network, and keeps the last 24 hours in memory. History therefore starts over whenever the hub
/// restarts — that's the trade-off for not running Prometheus + Grafana.
/// </summary>
public sealed class MetricsSampler : BackgroundService
{
    private const int Capacity = 24 * 60;   // 24h at one sample per minute
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IPresenceTracker _presence;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MetricsSampler> _log;
    private readonly List<(string Name, string Url)> _sources;

    private readonly object _gate = new();
    private readonly LinkedList<MetricsSample> _samples = new();
    private readonly Dictionary<string, string> _sourceStatus = new();

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public MetricsSampler(IPresenceTracker presence, IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<MetricsSampler> log)
    {
        _presence = presence; _httpFactory = httpFactory; _log = log;
        // "auth=http://auth:8080/metrics;files=http://files:8080/metrics" (compose service names).
        _sources = (cfg["Admin:MetricsSources"] ?? "auth=http://auth:8080/metrics;files=http://files:8080/metrics")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => (p[0].Trim(), p[1].Trim()))
            .ToList();
    }

    public IReadOnlyDictionary<string, string> SourceStatus
    {
        get { lock (_gate) return new Dictionary<string, string>(_sourceStatus); }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await SampleAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogWarning(ex, "metrics sample failed"); }
            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        // Kept per source on purpose: every process registers the shared AethernetMetrics
        // counters (at 0), so summing across sources would let a failed scrape of, say, the file
        // server read as "0 bytes ever" and then spike when it comes back.
        var bySource = new Dictionary<string, PrometheusText>();
        using (var ms = new MemoryStream())
        {
            await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(ms, ct);
            bySource["hub"] = PrometheusText.Parse(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }

        var http = _httpFactory.CreateClient(nameof(MetricsSampler));
        var status = new Dictionary<string, string> { ["hub"] = "ok" };
        foreach (var (name, url) in _sources)
        {
            try
            {
                bySource[name] = PrometheusText.Parse(await http.GetStringAsync(url, ct));
                status[name] = "ok";
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                status[name] = ex.Message;
            }
        }

        PrometheusText? Src(string n) => bySource.TryGetValue(n, out var t) ? t : null;
        var hub = Src("hub"); var auth = Src("auth"); var files = Src("files");

        var sample = new MetricsSample(
            At:             DateTime.UtcNow,
            Online:         await _presence.OnlineCountAsync(),
            MessagesIn:     hub?.Sum("aethernet_hub_messages_in_total"),
            PushRecipients: hub?.Sum("aethernet_hub_push_recipients_total"),
            UploadBytes:    files?.Sum("aethernet_files_upload_bytes_total"),
            DownloadBytes:  files?.Sum("aethernet_files_download_bytes_total"),
            LoginsOk:       auth?.SumWhere("aethernet_auth_login_attempts_total", "outcome", o => o is "success" or "discord_signin"),
            LoginsFailed:   auth?.SumWhere("aethernet_auth_login_attempts_total", "outcome", o => o is not ("success" or "discord_signin")),
            Signups:        auth?.Sum("aethernet_auth_registrations_total"));

        lock (_gate)
        {
            _samples.AddLast(sample);
            while (_samples.Count > Capacity) _samples.RemoveFirst();
            _sourceStatus.Clear();
            foreach (var (k, v) in status) _sourceStatus[k] = v;
        }
    }

    /// <summary>
    /// Groups the minute samples into <paramref name="bucketMinutes"/>-wide buckets. Counters become
    /// "how much happened in this bucket"; a counter that went DOWN means that service restarted,
    /// so its post-restart value is taken as the increase (same rule as Prometheus' increase()).
    /// Online is the bucket's peak.
    /// </summary>
    public IReadOnlyList<MetricsBucket> Buckets(int bucketMinutes)
    {
        MetricsSample[] s;
        lock (_gate) s = _samples.ToArray();
        if (s.Length == 0) return Array.Empty<MetricsBucket>();

        var width = TimeSpan.FromMinutes(Math.Max(1, bucketMinutes));
        var result = new List<MetricsBucket>();
        MetricsSample? prev = null;
        var first = true;
        foreach (var group in s.GroupBy(x => new DateTime(x.At.Ticks - x.At.Ticks % width.Ticks, DateTimeKind.Utc)))
        {
            // The oldest bucket is almost always partial (sampling started mid-bucket), so its
            // counter totals would read as a fake dip. Report its peak-online only.
            var partial = first; first = false;
            int? online = null;
            double? msg = null, push = null, up = null, down = null, ok = null, bad = null, sign = null;
            foreach (var cur in group)
            {
                online = Math.Max(online ?? 0, cur.Online);
                if (prev is not null && !partial)
                {
                    msg  = Add(msg,  Delta(prev.MessagesIn,     cur.MessagesIn));
                    push = Add(push, Delta(prev.PushRecipients, cur.PushRecipients));
                    up   = Add(up,   Delta(prev.UploadBytes,    cur.UploadBytes));
                    down = Add(down, Delta(prev.DownloadBytes,  cur.DownloadBytes));
                    ok   = Add(ok,   Delta(prev.LoginsOk,       cur.LoginsOk));
                    bad  = Add(bad,  Delta(prev.LoginsFailed,   cur.LoginsFailed));
                    sign = Add(sign, Delta(prev.Signups,        cur.Signups));
                }
                prev = cur;
            }
            result.Add(new MetricsBucket(group.Key, online, msg, push, up, down, ok, bad, sign));
        }
        return result;
    }

    private static double? Delta(double? before, double? after)
    {
        if (before is null || after is null) return null;
        return after >= before ? after - before : after;   // counter reset
    }

    private static double? Add(double? acc, double? d) => d is null ? acc : (acc ?? 0) + d;
}
