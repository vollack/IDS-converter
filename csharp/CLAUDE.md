# CLAUDE.md (C# version)

C# port of the IDS4ALL converter. It uses `ifctester` through Python.NET to write the IDS files.

## Branches

These rules replace the branch rules in the root [CLAUDE.md](../CLAUDE.md) for C# work.

- `csharp` is the permanent branch of the C# version. `main` and `dev` are for the Python version only.
- Do each C# change on a local `claude/<short-description>` branch. Merge it into `csharp` with `--no-ff`.
- Never merge `csharp` into `dev` or `main`. Merge `dev` into `csharp` to get Python fixes.
- Change only files under `csharp/` on the `csharp` branch. Python fixes go to `dev` first.

## Knowledge base

| File | Use for |
| ---- | ------- |
| [DEPENDENCIES.md](DEPENDENCIES.md) | NuGet and Python packages, Python versions, environment variables. Update it with every dependency change. |
