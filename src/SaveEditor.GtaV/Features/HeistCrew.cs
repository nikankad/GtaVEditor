
namespace SaveEditor.GtaV;

/// <summary>
/// Heist crew availability: the script data's crew struct holds bitsets of unlocked, used and dead crew members, one
/// bit per member (bits 1–13; every finished save has all 13 unlocked).
/// </summary>
public sealed class HeistCrew
{
    private const uint CrewStruct = 0xA3382ECE;
    public const int Members = 13;
    private const int AllMembers = ((1 << Members) - 1) << 1; // bits 1..13

    private readonly PsoNode _unlocked, _dead;

    private HeistCrew(PsoNode unlocked, PsoNode dead) => (_unlocked, _dead) = (unlocked, dead);

    public int Unlocked => Count(_unlocked.GetInt32());
    public int Dead => Count(_dead.GetInt32());

    /// <summary>Makes every crew member available to hire, as in a finished game.</summary>
    public void UnlockAll() => _unlocked.SetInt32(_unlocked.GetInt32() | AllMembers);

    /// <summary>Clears the "dead" mark so crew members killed during a heist can be hired again.</summary>
    public void ReviveAll() => _dead.SetInt32(_dead.GetInt32() & ~AllMembers);

    private static int Count(int bits) => System.Numerics.BitOperations.PopCount((uint)(bits & AllMembers));

    public static HeistCrew Load(PsoDocument pso)
    {
        var crew = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == CrewStruct)
                   ?? throw new SaveFormatException("This save has no heist crew data.");
        PsoNode Field(string name) =>
            crew.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash(name) && n.Type == PsoType.SInt)
            ?? throw new SaveFormatException("The heist crew data in this save is not in the expected layout.");
        return new HeistCrew(Field("Crew_Unlocked_Bitset"), Field("Crew_Dead_Bitset"));
    }
}
