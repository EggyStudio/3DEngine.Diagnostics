[Devlog](https://github.com/EggyStudio/3DEngine.Diagnostics/blob/main/DEVLOG.md) [Cheatsheet](https://github.com/EggyStudio/3DEngine.Diagnostics/blob/main/CHEATSHEET.md)

# Diagnostics

Diagnostics is a library an engine takes as a git submodule, for what a developer and a player
reach at the keyboard, console commands and settings in one registry, and the content of the
console, the debug overlay and the settings panel as plain data a host draws. It references no
engine, and an engine binds it by filling the hooks it declares and drawing what it gives.

The repository holds two libraries with no type in either yet, so none of the above is
implemented. The core, `3DEngine.Diagnostics`, stands on .NET alone with no package and no graphics
reference, which a test of its own holds, and `3DEngine.Diagnostics.ImGui` beside it is the one
project that references Dear ImGui, for a host that has an ImGui context.

## Taking it into an engine

An engine adds the repository as a git submodule under `modules/` and references the core's
project, and the second project where it draws with ImGui, here from a project one folder below the
engine's root.

```bash
git submodule add https://github.com/EggyStudio/3DEngine.Diagnostics.git modules/Diagnostics
```

```xml
<ProjectReference Include="..\modules\Diagnostics\Diagnostics\Diagnostics.csproj" />
<ProjectReference Include="..\modules\Diagnostics\Diagnostics.ImGui\Diagnostics.ImGui.csproj" />
```

The projects build the assemblies `3DEngine.Diagnostics` and `3DEngine.Diagnostics.ImGui`, whose
types are in the one namespace `Diagnostics`. The second takes the package
`Twizzle.ImGui-Bundle.NET` at 1.91.5.2, whose calls are in the namespace `ImGuiNET`, and a host
draws with it into the context it made with the same package.

A clone of the engine fetches the module with `git clone --recurse-submodules`, or with
`git submodule update --init` in a clone made without it, and a workflow checks it out with
`submodules: true` on `actions/checkout`. A change the engine uses is committed and pushed here
before the engine's commit that moves its pointer, so the engine's history never names a commit
this repository does not have.

## Building and testing

The tests take the checks every module keeps from the Tools module, a git submodule under
`modules/Tools`, so a clone fetches it with `git clone --recurse-submodules`, or with
`git submodule update --init` in a clone made without it. With the .NET 10 SDK:

```bash
dotnet build Diagnostics.slnx
dotnet test Diagnostics.Tests
```

## License

The Mozilla Public License 2.0, in [LICENSE](LICENSE).
