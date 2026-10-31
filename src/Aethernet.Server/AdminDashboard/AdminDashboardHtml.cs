using System.Reflection;

namespace Aethernet.Server.AdminDashboard;

/// <summary>
/// Serves the web admin dashboard (overview metrics, user browser, online list, syncshells,
/// reports, audit log). The page itself is <c>dashboard.html</c> next to this file, compiled in as
/// an embedded resource so it's a normal HTML file to edit but still ships inside the hub's DLL —
/// no wwwroot, no build step. It talks to this service's /admin/* REST surface same-origin, and to
/// the auth service's /auth/login cross-origin (Cors:AllowedOrigins + Admin:AuthBaseUrl).
/// </summary>
public static class AdminDashboardHtml
{
    private static readonly Lazy<string> Template = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Aethernet.Server.AdminDashboard.dashboard.html")
            ?? throw new InvalidOperationException("dashboard.html embedded resource missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Render(string authBaseUrl)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(authBaseUrl ?? "");
        return Template.Value.Replace("\"__AUTH_BASE_URL__\"", json);
    }
}
