using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassScreenGuard.Controls;
using ClassScreenGuard.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassScreenGuard;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton(new GuardSettingsService(PluginConfigFolder));
        services.AddSingleton<ScreenGuardService>();
        services.AddHostedService(provider => provider.GetRequiredService<ScreenGuardService>());
        services.AddSettingsPage<GuardSettingsPage>();
    }
}
