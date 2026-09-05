namespace LightPet.Core.Interaction;

public static class PetInteractionContract
{
    public const double DefaultDisplaySize = 130;

    public const string BodyDragAction = "raise";

    public const string DragReleaseAction = "fall-land";

    public static string? DirectActionForRegion(string? region) =>
        region?.ToLowerInvariant() switch
        {
            "cheek" => "cheek-poke",
            "head" => "touch-head",
            "body" => "touch-body",
            _ => null,
        };
}
