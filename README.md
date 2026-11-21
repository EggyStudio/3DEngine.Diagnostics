[Devlog](https://github.com/EggyStudio/3DEngine.Diagnostics/blob/main/DEVLOG.md) [Cheatsheet](https://github.com/EggyStudio/3DEngine.Diagnostics/blob/main/CHEATSHEET.md)

# Diagnostics

Diagnostics is a library an engine takes as a git submodule, for what a developer and a player
reach at the keyboard, console commands and settings in one registry, and the content of the
console, the debug overlay and the settings panel as plain data a host draws. It references no
engine, and an engine binds it by filling the hooks it declares and drawing what it gives.

The repository holds its documents and no library yet, so none of the above is implemented.

## Taking it into an engine

An engine adds the repository as a git submodule under `modules/`.

```bash
git submodule add https://github.com/EggyStudio/3DEngine.Diagnostics.git modules/Diagnostics
```

A clone of the engine fetches the module with `git clone --recurse-submodules`, or with
`git submodule update --init` in a clone made without it, and a workflow checks it out with
`submodules: true` on `actions/checkout`. A change the engine uses is committed and pushed here
before the engine's commit that moves its pointer, so the engine's history never names a commit
this repository does not have.

## License

The Mozilla Public License 2.0, in [LICENSE](LICENSE).
