# Dependencies (C# version)

All projects target .NET 10 (`net10.0`). NuGet versions are pinned in the `.csproj` files. The
Python packages come from the Python environment that `IDS4ALL_PYTHON_DLL` points to.

## NuGet packages

| Package | Version | Project | Use |
| ------- | ------- | ------- | --- |
| `pythonnet` | 3.2.1 | `IDS4ALL.IfcTester` | Starts the embedded Python and calls `ifctester` to build, write and validate the IDS. |
| `xunit` | 2.9.3 | `IDS4ALL.Tests` | Test framework. |
| `xunit.runner.visualstudio` | 3.1.4 | `IDS4ALL.Tests` | Runs the xunit tests in `dotnet test` and in Visual Studio. |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | `IDS4ALL.Tests` | Test host for `dotnet test`. |
| `coverlet.collector` | 6.0.4 | `IDS4ALL.Tests` | Collects code coverage with `dotnet test --collect:"XPlat Code Coverage"`. |

`IDS4ALL.Core` has no NuGet packages. It reads `.xlsx` files with its own reader in
[src/IDS4ALL.Core/Excel/](src/IDS4ALL.Core/Excel/), which uses `System.IO.Compression` and
`System.Xml` from the .NET base library.

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

| Environment | Python | `ifcopenshell` | `ifctester` | `pandas` |
| ----------- | ------ | -------------- | ----------- | -------- |
| [../requirements.txt](../requirements.txt) (Python version) | 3.10 to 3.12 | 0.8.1.post1 | 0.8.1 | 2.2.3 |
| Development of the C# port | 3.14.7 | 0.9.0.post1 | 0.8.1 | 2.3.3 |

`ifcopenshell` 0.8.1.post1 has no wheel for Python 3.13 or later. Before the first C# release,
run the golden and differential tests again with Python 3.12 and the pinned versions.

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
