namespace Diagnostics.Tests;

/// <summary>The repository the tests run in, found above the tests' own folder.</summary>
internal static class Repository
{
    /// <summary>
    /// The repository's root, the folder above the tests that holds the solution.
    /// </summary>
    public static string Root { get; } = Find();

    private static string Find()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "Diagnostics.slnx")))
            at = at.Parent;
        return at?.FullName ?? throw new DirectoryNotFoundException("No Diagnostics.slnx above the tests.");
    }
}
