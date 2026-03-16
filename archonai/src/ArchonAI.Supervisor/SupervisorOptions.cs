namespace ArchonAI.Supervisor;

public sealed class SupervisorOptions
{
    public int MaxExecutionsInWindow { get; set; } = 50;
    public int RunawayWindowSeconds { get; set; } = 60;
    public int ConsecutiveFailuresBeforeRestart { get; set; } = 3;
    public int MaxRestartsPerHour { get; set; } = 5;
    public string UnsafeErrorKeyword { get; set; } = "unsafe";
}
