namespace LightPet.Core.Packs;

public sealed record LoadedFrame(string Path, int DurationMilliseconds);

public sealed record LoadedPhase(AnimationPhase Kind, IReadOnlyList<LoadedFrame> Frames);

public sealed class LoadedAction
{
    private readonly IReadOnlyDictionary<AnimationPhase, LoadedPhase> _phases;

    public LoadedAction(string id, IReadOnlyDictionary<AnimationPhase, LoadedPhase> phases)
    {
        Id = id;
        _phases = phases;
    }

    public string Id { get; }

    public LoadedPhase? GetPhase(AnimationPhase phase) =>
        _phases.TryGetValue(phase, out var loadedPhase) ? loadedPhase : null;
}

public sealed class LoadedPetPack
{
    private readonly IReadOnlyDictionary<string, LoadedAction> _actions;

    public LoadedPetPack(
        string rootPath,
        PetPackManifest manifest,
        IReadOnlyDictionary<string, LoadedAction> actions)
    {
        RootPath = rootPath;
        Manifest = manifest;
        _actions = actions;
        Actions = actions.Values.ToArray();
    }

    public string RootPath { get; }

    public PetPackManifest Manifest { get; }

    public IReadOnlyCollection<LoadedAction> Actions { get; }

    public LoadedAction GetAction(string id) =>
        _actions.TryGetValue(id, out var action)
            ? action
            : throw new KeyNotFoundException($"Unknown animation action '{id}'.");

    public bool HasAction(string id) => _actions.ContainsKey(id);

    public string? HitTest(double canvasX, double canvasY) =>
        Manifest.HitRegions.FirstOrDefault(region => region.Contains(canvasX, canvasY))?.Id;
}
