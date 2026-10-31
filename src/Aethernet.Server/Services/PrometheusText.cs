using System.Globalization;

namespace Aethernet.Server.Services;

/// <summary>
/// Just enough of the Prometheus text exposition format to read our own /metrics endpoints:
/// <c>name{label="v",...} value [timestamp]</c>, comments skipped. Not a general-purpose parser.
/// </summary>
public sealed class PrometheusText
{
    private readonly Dictionary<string, List<(Dictionary<string, string> Labels, double Value)>> _series = new();

    public static PrometheusText Parse(string text)
    {
        var p = new PrometheusText();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var nameEnd = line.IndexOfAny(new[] { '{', ' ' });
            if (nameEnd <= 0) continue;
            var name   = line[..nameEnd];
            var labels = new Dictionary<string, string>();
            var rest   = line[nameEnd..];

            if (rest.StartsWith('{'))
            {
                var close = FindLabelsEnd(rest);
                if (close < 0) continue;
                ParseLabels(rest[1..close], labels);
                rest = rest[(close + 1)..];
            }

            var valueToken = rest.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (valueToken is null || !double.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;

            if (!p._series.TryGetValue(name, out var list)) p._series[name] = list = new();
            list.Add((labels, value));
        }
        return p;
    }

    /// <summary>Sum across every label set. 0 when absent — a labeled counter has no samples
    /// until its first increment, which means "nothing happened yet", not "unknown".</summary>
    public double Sum(string name) =>
        _series.TryGetValue(name, out var list) ? list.Sum(x => x.Value) : 0;

    /// <summary>Sum of the label sets where <paramref name="label"/> matches the predicate.</summary>
    public double SumWhere(string name, string label, Func<string, bool> match) =>
        _series.TryGetValue(name, out var list)
            ? list.Where(x => x.Labels.TryGetValue(label, out var v) && match(v)).Sum(x => x.Value)
            : 0;

    private static int FindLabelsEnd(string s)
    {
        var inQuotes = false;
        for (var i = 1; i < s.Length; i++)
        {
            if (s[i] == '\\' && inQuotes) { i++; continue; }
            if (s[i] == '"') inQuotes = !inQuotes;
            else if (s[i] == '}' && !inQuotes) return i;
        }
        return -1;
    }

    private static void ParseLabels(string body, Dictionary<string, string> into)
    {
        var i = 0;
        while (i < body.Length)
        {
            var eq = body.IndexOf('=', i);
            if (eq < 0) return;
            var key = body[i..eq].Trim().TrimStart(',').Trim();
            var q1 = body.IndexOf('"', eq);
            if (q1 < 0) return;
            var sb = new System.Text.StringBuilder();
            var j = q1 + 1;
            for (; j < body.Length; j++)
            {
                if (body[j] == '\\' && j + 1 < body.Length) { sb.Append(body[++j]); continue; }
                if (body[j] == '"') break;
                sb.Append(body[j]);
            }
            into[key] = sb.ToString();
            i = j + 1;
        }
    }
}
