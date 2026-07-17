namespace Musync.Domain;

/// <summary>Values written to <see cref="JobRun.Status"/>.</summary>
public static class JobStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string DryRun = "dry-run";
    public const string Partial = "partial";
}
