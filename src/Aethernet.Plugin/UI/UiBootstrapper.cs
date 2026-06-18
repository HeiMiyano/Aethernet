using Aethernet.Plugin.Configuration;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Aethernet.Plugin.UI;

/// <summary>
/// Owns the WindowSystem, registers the slash command, and wires the draw callback.
/// </summary>
public sealed class UiBootstrapper : IDisposable
{
    private const string Command = "/aethernet";

    private readonly IDalamudPluginInterface _pi;
    private readonly ICommandManager _cmd;
    private readonly AethernetConfig _config;
    private readonly WindowSystem _windows = new("Aethernet");

    private readonly MainWindow _main;
    private readonly SettingsWindow _settings;
    private readonly EditProfileWindow _profile;
    private readonly DownloadStatusWindow _downloads;
    private readonly ProfileViewerWindow _profileViewer;
    private readonly GroupAdminWindow _groupAdmin;
    private readonly BlockListWindow _blocks;
    private readonly FirstRunWizard _wizard;

    public UiBootstrapper(
        IDalamudPluginInterface pi, ICommandManager cmd, AethernetConfig config,
        MainWindow main, SettingsWindow settings, EditProfileWindow profile, DownloadStatusWindow downloads,
        ProfileViewerWindow profileViewer, GroupAdminWindow groupAdmin, BlockListWindow blocks,
        FirstRunWizard wizard)
    {
        _pi = pi; _cmd = cmd; _config = config;
        _main = main; _settings = settings; _profile = profile; _downloads = downloads;
        _profileViewer = profileViewer; _groupAdmin = groupAdmin; _blocks = blocks; _wizard = wizard;
    }

    public void Initialize()
    {
        _windows.AddWindow(_main);
        _windows.AddWindow(_settings);
        _windows.AddWindow(_profile);
        _windows.AddWindow(_downloads);
        _windows.AddWindow(_profileViewer);
        _windows.AddWindow(_groupAdmin);
        _windows.AddWindow(_blocks);
        _windows.AddWindow(_wizard);

        _pi.UiBuilder.Draw          += _windows.Draw;
        _pi.UiBuilder.OpenConfigUi  += () => _settings.IsOpen = true;
        _pi.UiBuilder.OpenMainUi    += () => _main.IsOpen     = true;

        _cmd.AddHandler(Command, new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage =
                "Open the Aethernet window. Subcommands: settings, profile, downloads, blocks, wizard.",
        });

        // First-run gate: open the wizard automatically if there are no credentials.
        if (string.IsNullOrEmpty(_config.Uid) || string.IsNullOrEmpty(_config.SecretKey))
            _wizard.IsOpen = true;
    }

    private void OnCommand(string _, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "settings":  _settings.IsOpen   = true; break;
            case "profile":   _profile.IsOpen    = true; break;
            case "downloads": _downloads.IsOpen  = true; break;
            case "blocks":    _blocks.IsOpen     = true; break;
            case "wizard":    _wizard.IsOpen     = true; break;
            default:          _main.IsOpen       = true; break;
        }
    }

    public void Dispose()
    {
        _cmd.RemoveHandler(Command);
        _pi.UiBuilder.Draw -= _windows.Draw;
        _windows.RemoveAllWindows();
    }
}
