using System.Reflection;
using Xunit;

namespace Diagnostics.Tests;

public class LibraryTests
{
    // Each by its name rather than by a type of its own, so the test reads that the project
    // reference puts the assembly beside the tests.
    [Theory]
    [InlineData("3DEngine.Diagnostics")]
    [InlineData("3DEngine.Diagnostics.ImGui")]
    public void The_Library_Loads(string name)
    {
        var library = Assembly.Load(name);
        Assert.Equal(name, library.GetName().Name);
    }
}
