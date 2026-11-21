using System.Reflection;
using Tools;
using Xunit;

namespace Diagnostics.Tests;

/// <summary>
/// The checks every module keeps, from the Tools submodule, that the core takes no engine, window
/// or graphics API, names no engine in a public member or its documentation and has every public
/// type named in the README, that the cheatsheet is what the built library writes, that no tracked
/// text names an engine and that the README opens with its link row.
/// </summary>
/// <remarks>
/// The check of the README's binding block against a host's source joins these with the first
/// type a host binds, since the README has no such block before it.
/// </remarks>
public sealed class ModuleTests
{
    // The core's assembly and the checkout's root, which a module sets.
    private const string Library = "3DEngine.Diagnostics";
    private static readonly string Root = Repository.Root;

    private static readonly string Deps = Path.Combine(AppContext.BaseDirectory, Assembly.GetExecutingAssembly().GetName().Name + ".deps.json");
    private static readonly string Readme = File.ReadAllText(Path.Combine(Root, "README.md"));

    [Fact]
    public void The_Library_Takes_No_Engine_Window_Or_Graphics_Api()
    {
        var fault = ModuleChecks.ReferenceFault(Assembly.Load(Library), Deps);
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void The_Same_Check_Refuses_The_Library_That_Draws_With_ImGui()
    {
        // The check above passes a library with no reference at all, so it is read here against
        // the library whose project takes ImGui, which it names.
        var fault = ModuleChecks.ReferenceFault(Assembly.Load(Library + ".ImGui"), Deps);
        Assert.Contains("Twizzle.ImGui-Bundle.NET", fault);
    }

    [Fact]
    public void No_Public_Name_Or_Its_Documentation_Names_An_Engine()
    {
        var fault = ModuleChecks.EngineNameFault(Assembly.Load(Library));
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void The_Readme_Names_Every_Public_Type()
    {
        var fault = ModuleChecks.UnnamedFault(Assembly.Load(Library), Readme);
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void The_Cheatsheet_Is_What_The_Built_Library_Writes()
    {
        var fault = ModuleChecks.CheatsheetFault(Assembly.Load(Library), Readme, Path.Combine(Root, "CHEATSHEET.md"));
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void No_Tracked_Text_Names_An_Engine()
    {
        var fault = ModuleChecks.EngineNameInTextFault(Root);
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void The_Readme_Opens_With_Its_Link_Row()
    {
        var fault = ModuleChecks.LinkRowFault(Assembly.Load(Library), Readme);
        Assert.True(fault is null, fault);
    }
}
