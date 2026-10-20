# Changelog

All notable changes to the C# version are documented in this file. Changes to the Python version
are in the [root CHANGELOG.md](../CHANGELOG.md).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The C# version has no tagged release yet.

## [Unreleased]

### Added

- C# version of the converter in `csharp/`. It reads `.xlsx` files, merges the specifications and writes the same IDS files as the Python version on `dev`.
- Command line with the options `--excel_path`, `--excel_name`, `--excel_format` and `--output_path`, the defaults and messages of `IDS4ALL-main.py`, and the exit codes 0, 1 and 2.
- IDS output through Python `ifctester` with Python.NET. Set `IDS4ALL_PYTHON_DLL` and, for a virtual environment, `IDS4ALL_PYTHON_SITE_PACKAGES`.
- `Ids4AllConverter.Convert`: the whole conversion from a workbook to IDS records in one call. The command line uses it.
- `XlsxWorkbook.Open(Stream)`: read a workbook from a stream, for hosts that hold the bytes in memory.
- `IdsWriter.WriteToString`: get the IDS file as text with the schema check result, without keeping a file.
- `Specification.Description`: an optional description that a host can set. The Excel import leaves it empty.
- `PythonRuntimeOptions.ExtraModules` and `ExtraPaths`: a host that embeds the converter names its own Python packages and folders.

[unreleased]: https://github.com/pkoska/IDS-converter/compare/main...csharp
