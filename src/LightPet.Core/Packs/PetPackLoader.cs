using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LightPet.Core.Packs;

public static partial class PetPackLoader
{
    private static readonly string[] RequiredPrototypeActions =
    [
        "idle",
        "walk-left",
        "walk-right",
        "touch-head",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static LoadedPetPack Load(string packRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packRoot);
        var root = Path.GetFullPath(packRoot);
        var manifestPath = Path.Combine(root, "pet.json");
        if (!File.Exists(manifestPath))
        {
            throw new PetPackException($"Missing pet manifest: {manifestPath}");
        }

        PetPackManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PetPackManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new PetPackException("The pet manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new PetPackException("The pet manifest contains invalid JSON.", exception);
        }

        ValidateManifest(manifest);

        var actions = new Dictionary<string, LoadedAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var actionDefinition in manifest.Actions)
        {
            if (actions.ContainsKey(actionDefinition.Id))
            {
                throw new PetPackException($"Duplicate action id '{actionDefinition.Id}'.");
            }

            var phases = new Dictionary<AnimationPhase, LoadedPhase>();
            foreach (var phaseDefinition in actionDefinition.Phases)
            {
                if (phases.ContainsKey(phaseDefinition.Kind))
                {
                    throw new PetPackException(
                        $"Action '{actionDefinition.Id}' contains duplicate phase '{phaseDefinition.Kind}'.");
                }

                var folder = ResolveInsideRoot(root, phaseDefinition.Folder);
                if (!Directory.Exists(folder))
                {
                    throw new PetPackException(
                        $"Action '{actionDefinition.Id}' is missing phase folder '{phaseDefinition.Folder}'.");
                }

                var frames = Directory.EnumerateFiles(folder, "*.png", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .Select(path => LoadFrame(path, manifest.Canvas))
                    .ToArray();
                if (frames.Length == 0)
                {
                    throw new PetPackException(
                        $"Action '{actionDefinition.Id}' phase '{phaseDefinition.Kind}' has no PNG frames.");
                }

                phases.Add(phaseDefinition.Kind, new LoadedPhase(phaseDefinition.Kind, frames));
            }

            ValidatePhases(actionDefinition.Id, phases);
            actions.Add(actionDefinition.Id, new LoadedAction(actionDefinition.Id, phases));
        }

        foreach (var requiredAction in RequiredPrototypeActions)
        {
            if (!actions.ContainsKey(requiredAction))
            {
                throw new PetPackException($"Prototype pack is missing required action '{requiredAction}'.");
            }
        }

        return new LoadedPetPack(root, manifest, actions);
    }

    private static void ValidateManifest(PetPackManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new PetPackException($"Unsupported pet schema version '{manifest.SchemaVersion}'.");
        }

        if (!PackIdPattern().IsMatch(manifest.Id))
        {
            throw new PetPackException("Pet id must contain only lowercase letters, digits, and hyphens.");
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            throw new PetPackException("Pet display name is required.");
        }

        if (manifest.Canvas.Width is < 64 or > 4096 || manifest.Canvas.Height is < 64 or > 4096)
        {
            throw new PetPackException("Canvas dimensions must be between 64 and 4096 pixels.");
        }

        if (manifest.Canvas.AnchorX < 0 || manifest.Canvas.AnchorX > manifest.Canvas.Width ||
            manifest.Canvas.AnchorY < 0 || manifest.Canvas.AnchorY > manifest.Canvas.Height)
        {
            throw new PetPackException("The default anchor must be inside the canvas.");
        }

        if (manifest.Display.MinimumSize <= 0 ||
            manifest.Display.MinimumSize > manifest.Display.DefaultSize ||
            manifest.Display.DefaultSize > manifest.Display.MaximumSize)
        {
            throw new PetPackException("Display size limits are invalid.");
        }

        var visibleBounds = manifest.Display.VisibleBounds;
        if (visibleBounds.Left < 0 || visibleBounds.Top < 0 ||
            visibleBounds.Right > 1 || visibleBounds.Bottom > 1 ||
            visibleBounds.Left >= visibleBounds.Right ||
            visibleBounds.Top >= visibleBounds.Bottom)
        {
            throw new PetPackException("Display visible bounds must be normalized inside the canvas.");
        }

        foreach (var region in manifest.HitRegions)
        {
            if (string.IsNullOrWhiteSpace(region.Id) || region.Width <= 0 || region.Height <= 0 ||
                region.X < 0 || region.Y < 0 ||
                region.X + region.Width > manifest.Canvas.Width ||
                region.Y + region.Height > manifest.Canvas.Height)
            {
                throw new PetPackException($"Hit region '{region.Id}' is invalid or outside the canvas.");
            }
        }
    }

    private static void ValidatePhases(
        string actionId,
        IReadOnlyDictionary<AnimationPhase, LoadedPhase> phases)
    {
        if (phases.ContainsKey(AnimationPhase.Single) && phases.Count != 1)
        {
            throw new PetPackException($"Action '{actionId}' cannot mix a single phase with staged phases.");
        }

        if (!phases.ContainsKey(AnimationPhase.Single) && !phases.ContainsKey(AnimationPhase.Loop))
        {
            throw new PetPackException($"Action '{actionId}' must contain a loop or single phase.");
        }
    }

    private static LoadedFrame LoadFrame(string path, CanvasDefinition canvas)
    {
        var match = FrameDurationPattern().Match(Path.GetFileName(path));
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var duration) || duration is < 16 or > 5000)
        {
            throw new PetPackException(
                $"Frame '{Path.GetFileName(path)}' must end with a duration between 16 and 5000ms.");
        }

        ValidatePngCanvas(path, canvas);
        return new LoadedFrame(Path.GetFullPath(path), duration);
    }

    private static void ValidatePngCanvas(string path, CanvasDefinition canvas)
    {
        Span<byte> header = stackalloc byte[24];
        try
        {
            using var stream = File.OpenRead(path);
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException exception)
        {
            throw new PetPackException($"Frame '{Path.GetFileName(path)}' is not a complete PNG file.", exception);
        }

        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        ReadOnlySpan<byte> imageHeader = [73, 72, 68, 82];
        if (!header[..8].SequenceEqual(signature) || !header.Slice(12, 4).SequenceEqual(imageHeader))
        {
            throw new PetPackException($"Frame '{Path.GetFileName(path)}' is not a valid PNG file.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
        if (width != canvas.Width || height != canvas.Height)
        {
            throw new PetPackException(
                $"Frame '{Path.GetFileName(path)}' is {width}x{height}; expected {canvas.Width}x{canvas.Height}.");
        }
    }

    private static string ResolveInsideRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new PetPackException("Phase folder must be a relative path inside the pet pack.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new PetPackException($"Phase folder escapes the pet pack: {relativePath}");
        }

        return fullPath;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackIdPattern();

    [GeneratedRegex("_(\\d+)\\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameDurationPattern();
}
