namespace SaveEditor.Tests;

/// <summary>Local integration checks must show as skipped when private fixtures are unavailable.</summary>
public sealed class RealSaveFactAttribute : FactAttribute
{
    public RealSaveFactAttribute(bool needsKey = false)
    {
        var save = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        var key = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        if (string.IsNullOrEmpty(save) || !File.Exists(save))
            Skip = "Set GTAV_SAMPLE_SAVE to a local GTA V save to run this integration check.";
        else if (needsKey && (string.IsNullOrEmpty(key) || !File.Exists(key)))
            Skip = "Set GTAV_KEY_FILE to a local key file to run this integration check.";
    }
}
