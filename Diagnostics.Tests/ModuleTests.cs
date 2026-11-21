using System.Reflection;
using Tools;
using Xunit;

namespace Diagnostics.Tests;

/// <summary>
/// The checks every module keeps, from the Tools submodule, that the core takes no engine, window
/// or graphics API, names no engine in a public member or its documentation and has every public
/// type named in the README, that the README's binding block is the host's code, that the
/// cheatsheet is what the built library writes, that no tracked text names an engine and that the
/// README opens with its link row.
/// </summary>
public sealed class ModuleTests
{
    // The core's assembly, the checkout's root and the source of the host that runs the README's
    // binding block, the three a module sets.
    private const string Library = "3DEngine.Diagnostics";
    private static readonly string Root = Repository.Root;
    private const string Host = "Diagnostics.Tests/BindingTests.cs";

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
    public void The_Library_That_Draws_With_ImGui_Names_No_Engine_And_Is_Named_In_The_Readme()
    {
        var drawing = Assembly.Load(Library + ".ImGui");
        var fault = ModuleChecks.EngineNameFault(drawing) ?? ModuleChecks.UnnamedFault(drawing, Readme);
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void The_Readme_Names_Every_Public_Type()
    {
        var fault = ModuleChecks.UnnamedFault(Assembly.Load(Library), Readme);
        Assert.True(fault is null, fault);
    }

    [Fact]
    public void The_Readmes_Binding_Block_Stands_Word_For_Word_In_The_Host()
    {
        var fault = ModuleChecks.BindingBlock(Readme, File.ReadAllText(Path.Combine(Root, Host)));
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
