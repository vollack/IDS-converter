# Dependencies (C# version)

All projects target .NET 10 (`net10.0`). Development uses the .NET SDK 10.0.402. NuGet versions
are pinned in the `.csproj` files. The Python packages come from the Python environment that
`IDS4ALL_PYTHON_DLL` points to.

## NuGet packages

| Package | Version | Project | Use |
| ------- | ------- | ------- | --- |
| `ClosedXML` | 0.105.1 | `IDS4ALL.Core` | Reads the cells of the Excel files. |
| `SixLabors.Fonts` | 1.0.0 | `IDS4ALL.Core` | Required by `ClosedXML`. Pinned to the Apache 2.0 version, see below. |
| `pythonnet` | 3.2.1 | `IDS4ALL.IfcTester` | Starts the embedded Python and calls `ifctester` to build, write and validate the IDS. |
| `xunit` | 2.9.3 | `IDS4ALL.Tests` | Test framework. |
| `xunit.runner.visualstudio` | 3.1.4 | `IDS4ALL.Tests` | Runs the xunit tests in `dotnet test` and in Visual Studio. |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | `IDS4ALL.Tests` | Test host for `dotnet test`. |
| `coverlet.collector` | 6.0.4 | `IDS4ALL.Tests` | Collects code coverage with `dotnet test --collect:"XPlat Code Coverage"`. |

`IDS4ALL.Core` reads `.xlsx` files with `ClosedXML` in
[src/IDS4ALL.Core/Excel/XlsxWorkbook.cs](src/IDS4ALL.Core/Excel/XlsxWorkbook.cs). It converts the cell
values to the types that pandas gets from openpyxl. The known differences are in
[XlsxReaderDifferenceTests.cs](tests/IDS4ALL.Tests/Excel/XlsxReaderDifferenceTests.cs).
`IDS4ALL.Cli` gets `pythonnet` through its reference to `IDS4ALL.IfcTester`.

`ClosedXML` is MIT licensed. Its owners are igitur, jahav and pankraty. It allows `SixLabors.Fonts`
1.x and 2.x. Version 2 has the Six Labors Split License, so `IDS4ALL.Core` pins version 1.0.0
(Apache 2.0) with `[1.0.0]`. Do not raise it without a licence check.

### Transitive NuGet packages

These packages come with `ClosedXML` and with the test packages. `pythonnet` has no dependencies of its own.

| Package | Version | Pulled in by |
| ------- | ------- | ------------ |
| `ClosedXML.Parser` | 2.0.0 | `ClosedXML` |
| `DocumentFormat.OpenXml` | 3.1.1 | `ClosedXML` |
| `DocumentFormat.OpenXml.Framework` | 3.1.1 | `DocumentFormat.OpenXml` |
| `ExcelNumberFormat` | 1.1.0 | `ClosedXML` |
| `RBush.Signed` | 4.0.0 | `ClosedXML` |
| `System.IO.Packaging` | 8.0.1 | `DocumentFormat.OpenXml.Framework` |
| `Microsoft.CodeCoverage` | 17.14.1 | `Microsoft.NET.Test.Sdk` |
| `Microsoft.TestPlatform.ObjectModel` | 17.14.1 | `Microsoft.TestPlatform.TestHost` |
| `Microsoft.TestPlatform.TestHost` | 17.14.1 | `Microsoft.NET.Test.Sdk` |
| `Newtonsoft.Json` | 13.0.3 | `Microsoft.TestPlatform.TestHost` |
| `xunit.abstractions` | 2.0.3 | `xunit.extensibility.core` |
| `xunit.analyzers` | 1.18.0 | `xunit` |
| `xunit.assert` | 2.9.3 | `xunit` |
| `xunit.core` | 2.9.3 | `xunit` |
| `xunit.extensibility.core` | 2.9.3 | `xunit.core` |
| `xunit.extensibility.execution` | 2.9.3 | `xunit.core` |

## Python packages

The C# code imports these packages through Python.NET.

| Package | Used in | Use |
| ------- | ------- | --- |
| `ifctester` | `IdsWriter` | Builds the IDS objects and writes the XML (`ids.Ids`, facets, `ids.Restriction`, `to_xml`). |
| `ifcopenshell` | `PythonRuntime` | Required by `ifctester`. `PythonRuntime` checks that it loads. |
| `xmlschema` | `PythonRuntime` | Required by `ifctester` for the XSD validation. `PythonRuntime` checks that it loads. |

The tests also import `pandas`, `openpyxl` and the Python `custom_functions.py`. They compare the
C# results with the Python version. These tests need the full Python package set of
[../requirements.txt](../requirements.txt), including `deepdiff` and `deepmerge`.

## Python versions

The C# port uses a Python 3.14 environment. `ifcopenshell` 0.8.1.post1 has no wheel for Python
3.13 or later, so this environment cannot use the pinned versions.

| Package | [../requirements.txt](../requirements.txt) | C# development |
| ------- | ------------------------------------------ | -------------- |
| Python | 3.10 to 3.12 | 3.14.7 |
| `ifcopenshell` | 0.8.1.post1 | **0.9.0.post1** |
| `ifctester` | 0.8.1 | 0.8.1 |
| `xmlschema` | 3.4.3 | 3.4.3 |
| `elementpath` | 4.8.0 | 4.8.0 |
| `pandas` | 2.2.3 | **2.3.3** |
| `openpyxl` | 3.1.5 | 3.1.5 |
| `et_xmlfile` | 2.0.0 | 2.0.0 |
| `deepdiff` | 8.6.2 | 8.6.2 |
| `deepmerge` | 2.0 | 2.0 |
| `orderly-set` | 5.5.0 | 5.5.0 |
| `numpy` | 2.2.3 | **2.5.4** |
| `shapely` | 2.0.7 | **2.2.0** |
| `lark` | 1.2.2 | **1.3.1** |
| `isodate` | 0.7.2 | 0.7.2 |
| `pyparsing` | not used | **3.3.3**, new dependency of `ifcopenshell` 0.9 |
| `python-dateutil` | 2.9.0.post0 | 2.9.0.post0 |
| `six` | 1.17.0 | 1.17.0 |
| `pytz` | 2025.1 | **2026.5** |
| `tzdata` | 2025.1 | **2026.5** |
| `typing_extensions` | 4.12.2 | **4.16.0** |

Bold versions differ from `requirements.txt`. Before the first C# release, run the golden and
differential tests again with Python 3.12 and the pinned versions.

## Environment variables

| Variable | Use |
| -------- | --- |
| `IDS4ALL_PYTHON_DLL` | Path to the Python library, for example `python314.dll`. `PYTHONNET_PYDLL` also works. |
| `IDS4ALL_PYTHON_SITE_PACKAGES` | Optional. A `site-packages` folder that Python adds to `sys.path`, for example of a venv. |

The tests that need Python skip when neither `IDS4ALL_PYTHON_DLL` nor `PYTHONNET_PYDLL` is set.

## Rules for a change

- Before you add a package, make sure that it has recent commits and more than one maintainer.
- Update the `.csproj` file and this file in the same commit.
- A new Python import in the C# code needs a row in the Python table.
