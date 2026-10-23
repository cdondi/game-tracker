namespace GameTracker.Tests;

public static class TestData
{
    /// <summary>
    /// Returns a title no other test will use, so tests never depend on what else is in the table.
    /// </summary>
    public static string UniqueTitle(string prefix = "Game") => $"{prefix} {Guid.NewGuid():N}";
}
