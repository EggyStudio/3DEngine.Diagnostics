# Diagnostics devlog

What landed in Diagnostics, by day and newest first, with the pictures taken on the way, the
captures the repository keeps under `.github/assets`. A line under Done names the item of
.github/REVIEW.md or .github/REVIEW-ARCHIVE.md and the commit that closed it.

## 2026-11-21

Commits that day

- `02be0c9` The core gains its registry of commands and settings in one namespace, the line a
  console runs with its quoted words, settings with a range, choices, a value for the next start and
  an environment variable that overrides them, the console's model with history and completion, and
  the settings file that keeps only changed values and unknown names, with the calls safe from any
  thread named in the README.
- `766abdf` The module gains its solution, an empty core library that takes no package, an empty
  library beside it that alone references ImGui, tests that both load, that the core stands on .NET
  alone and keeps the shared checks and that comment lines hold to 100 columns, the version at 0.1
  and a build workflow on Linux, Windows and macOS.
- `999769e` The module gains its documents, AGENTS.md, the README with its link row, STYLE.md,
  COMMITS.md and the code of conduct as the other modules keep them, an empty cheatsheet and devlog,
  the license, git's ignore and attribute files, the Tools module as a submodule, the review and the
  norm.
