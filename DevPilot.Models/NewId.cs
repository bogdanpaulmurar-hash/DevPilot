namespace DevPilot.Models;

public static class NewId
{
    public static string Short() => Guid.NewGuid().ToString("N")[..8];
}
