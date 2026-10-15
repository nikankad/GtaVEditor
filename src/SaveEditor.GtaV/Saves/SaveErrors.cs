namespace SaveEditor.GtaV;

/// <summary>Integrity status of a save as loaded (checksums etc.).</summary>
public sealed record IntegrityReport(bool IsValid, string Summary);

/// <summary>The file is not a GTA V save, or its data is not in the layout the editor expects.</summary>
public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);
