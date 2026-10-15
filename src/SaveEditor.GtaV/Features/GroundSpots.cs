namespace SaveEditor.GtaV;

/// <summary>
/// Places a character can stand, with the right height. The save stores a full x, y, z position, and a point picked on
/// a flat map has no height, so a character is moved to the nearest of these instead of being dropped into the air or
/// under the ground.
/// </summary>
public static class GroundSpots
{
    // Pickups the player walks up to. Nuclear waste and submarine pieces are underwater; bridges and knife flights are
    // flown through.
    private static readonly CollectibleKind[] OnFoot = [CollectibleKind.LetterScraps, CollectibleKind.SpaceshipParts, CollectibleKind.EpsilonTracts];

    /// <summary>The ground-level collectible pickup positions (game metres), with the kind each came from.</summary>
    public static IReadOnlyList<(CollectibleKind Kind, float X, float Y, float Z)> Pickups { get; } =
        OnFoot.SelectMany(k => CollectibleLocations.Of(k).Values.Select(p => (k, p.X, p.Y, p.Z)))
              .Where(p => p.Z > 1) // below 1 m is at or under sea level
              .ToList();
}
