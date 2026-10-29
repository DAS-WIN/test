using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.App.Services;

// Each adapter UI module registers a factory and a WPF DataTemplate for its returned ViewModel.
public interface IMinerOptionsViewModel
{
    MinerSettings Apply(MinerSettings settings);
}

public static class MinerOptionsRegistry
{
    private static readonly Dictionary<string, Func<MinerSettings, object?>> Factories = new(StringComparer.Ordinal);
    public static void Register(string minerId, Func<MinerSettings, object?> factory) => Factories.Add(minerId, factory);
    public static object? Create(MinerSettings settings) => Factories.TryGetValue(settings.MinerId, out var factory) ? factory(settings) : null;
}
