using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Diagnostics.Tests;

/// <summary>
/// The core stands on .NET alone, so a host with no ImGui and any graphics API takes it. Beside
/// <see cref="ModuleTests"/>, which refuses the names of engines, windows and graphics APIs, these
/// allow .NET's own assemblies alone, no call into native code and no reference in the project
/// file, and hold the library beside the core to the core and ImGui.
/// </summary>
public class ReferenceTests
{
    private static readonly Assembly Core = Assembly.Load("3DEngine.Diagnostics");

    [Fact]
    public void The_Core_References_Only_Dotnets_Own_Assemblies()
    {
        var foreign = Core.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n is not "netstandard" and not "mscorlib")
            .ToList();
        Assert.True(foreign.Count == 0, $"the core references {string.Join(", ", foreign)}");
    }

    [Fact]
    public void The_Core_Calls_No_Native_Code()
    {
        // A window and a graphics API are reached from .NET through a call into a native library,
        // which a method marked to be imported from one makes.
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var native = Core.GetTypes()
            .SelectMany(t => t.GetMethods(all))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
            .ToList();
        Assert.True(native.Count == 0, $"the core calls native code from {string.Join(", ", native)}");
    }

    [Fact]
    public void The_Cores_Project_Takes_No_Package_No_Project_And_No_Assembly()
    {
        foreach (var file in new[] { Path.Combine("Diagnostics", "Diagnostics.csproj"), "Directory.Build.props" })
        {
            var taken = References(file);
            Assert.True(taken.Count == 0, $"{file} takes {string.Join(", ", taken)}");
        }
    }

    [Fact]
    public void The_ImGui_Librarys_Project_Takes_The_Core_And_ImGui_Alone()
    {
        var taken = References(Path.Combine("Diagnostics.ImGui", "Diagnostics.ImGui.csproj"));
        Assert.Equal(
            [@"PackageReference Twizzle.ImGui-Bundle.NET", @"ProjectReference ..\Diagnostics\Diagnostics.csproj"],
            taken.Order(StringComparer.Ordinal));
    }

    private static List<string> References(string file) =>
        XDocument.Load(Path.Combine(Repository.Root, file)).Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference" or "Reference" or "FrameworkReference")
            .Select(e => $"{e.Name.LocalName} {e.Attribute("Include")?.Value}")
            .ToList();
}
