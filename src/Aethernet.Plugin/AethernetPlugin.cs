using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.IPC;
using Aethernet.Plugin.Services;
using Aethernet.Plugin.UI;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin;

/// <summary>
/// Plugin entry point. Dalamud constructs this; the constructor wires up a DI container that
/// owns every service. Disposal walks back through the container so background tasks (the hub
/// connection, file workers) shut down cleanly when the user disables the plugin.
/// </summary>
public sealed class AethernetPlugin : IDalamudPlugin
{
    public string Name => "Aethernet";

    private readonly ServiceProvider _services;
    private readonly IDalamudPluginInterface _pi;

    public AethernetPlugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IObjectTable objectTable,
        IFramework framework,
        IPluginLog pluginLog,
        IChatGui chat,
        ICondition condition,
        INotificationManager notifications,
        INamePlateGui nameplate,
        IGameGui gameGui)
    {
        _pi = pluginInterface;

        var services = new ServiceCollection();
        services.AddSingleton(pluginInterface);
        services.AddSingleton(commandManager);
        services.AddSingleton(clientState);
        services.AddSingleton(objectTable);
        services.AddSingleton(framework);
        services.AddSingleton(pluginLog);
        services.AddSingleton(chat);
        services.AddSingleton(condition);
        services.AddSingleton(notifications);
        services.AddSingleton(nameplate);
        services.AddSingleton(gameGui);

        services.AddLogging(b => b.AddProvider(new DalamudLoggerProvider(pluginLog)));

        services.AddSingleton<AethernetConfig>(_ =>
            pluginInterface.GetPluginConfig() as AethernetConfig ?? new AethernetConfig());

        // ---- IPC bridges ----
        services.AddSingleton<PenumbraIpc>();
        services.AddSingleton<GlamourerIpc>();
        services.AddSingleton<CustomizePlusIpc>();
        services.AddSingleton<HonorificIpc>();
        services.AddSingleton<HeelsIpc>();
        services.AddSingleton<MoodlesIpc>();
        services.AddSingleton<PetNamesIpc>();
        services.AddSingleton<BrioIpc>();

        // ---- core services ----
        services.AddSingleton<FileCacheService>();
        services.AddSingleton<HubConnectionService>();
        services.AddSingleton<FileTransferService>();
        services.AddSingleton<CharacterDataCollector>();
        services.AddSingleton<CharacterDataApplier>();
        services.AddSingleton<PairManager>();
        services.AddSingleton<GroupManager>();
        services.AddSingleton<VisibleUserManager>();
        services.AddSingleton<ZoneObserver>();
        services.AddSingleton<SyncOrchestrator>();
        services.AddSingleton<NameplateDecorator>();

        // ---- UI ----
        services.AddSingleton<MainWindow>();
        services.AddSingleton<SettingsWindow>();
        services.AddSingleton<EditProfileWindow>();
        services.AddSingleton<DownloadStatusWindow>();
        services.AddSingleton<ProfileViewerWindow>();
        services.AddSingleton<GroupAdminWindow>();
        services.AddSingleton<BlockListWindow>();
        services.AddSingleton<FirstRunWizard>();
        services.AddSingleton<UiBootstrapper>();
        services.AddSingleton<ThemeApplier>();
        services.AddSingleton<DownloadOverlay>();
        services.AddSingleton<UploadOverlay>();

        _services = services.BuildServiceProvider();

        _services.GetRequiredService<UiBootstrapper>().Initialize();
        _services.GetRequiredService<DownloadOverlay>().Initialize();
        _services.GetRequiredService<UploadOverlay>().Initialize();

        // Eagerly instantiate long-lived background services so their constructor-time event
        // subscriptions take effect immediately.
        _services.GetRequiredService<VisibleUserManager>();
        _services.GetRequiredService<ZoneObserver>();
        _services.GetRequiredService<SyncOrchestrator>();
        _services.GetRequiredService<NameplateDecorator>();
        _services.GetRequiredService<ThemeApplier>();

        _ = _services.GetRequiredService<HubConnectionService>().StartAsync();
    }

    public void Dispose()
    {
        try { _services.GetRequiredService<HubConnectionService>().StopAsync().GetAwaiter().GetResult(); }
        catch { /* swallow during teardown */ }
        // HubConnectionService is IAsyncDisposable only — calling _services.Dispose()
        // would throw 'type only implements IAsyncDisposable'. Use DisposeAsync.
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
