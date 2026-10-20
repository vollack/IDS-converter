# IDS4ALL converter (C# version)

C# port of the IDS4ALL converter. It reads information requirements from an Excel file and writes
buildingSMART IDS files. The Excel import, the grouping and the merging of specifications run in C#.
The IDS model, the XML output and the schema validation run in Python (`ifctester`), called through
[Python.NET](https://pythonnet.github.io/).

The C# version gives the same IDS files as the Python version on `dev`. The Excel files and their
column structure are the same. See the [root README](../README.md).

## Requirements

- .NET SDK 10.0
- Python 3.12 or later, with the packages of [DEPENDENCIES.md](DEPENDENCIES.md): `ifctester`,
  `ifcopenshell` and `xmlschema`
- Input files in the `.xlsx` format. The C# version does not read `.xls`.

## Set up Python

1. Create a virtual environment and install the packages. On Linux, use `source venv/bin/activate`.

   ```sh
   python -m venv venv
   venv\Scripts\activate
   pip install -r requirements.txt
   ```

2. Set the path of the Python library and, for a virtual environment, of its packages.

   Windows (PowerShell):

   ```powershell
   $env:IDS4ALL_PYTHON_DLL = "C:\Path\To\Python\python312.dll"
   $env:IDS4ALL_PYTHON_SITE_PACKAGES = "C:\Path\To\venv\Lib\site-packages"
   ```

   Linux:

   ```sh
   export IDS4ALL_PYTHON_DLL=/usr/lib/libpython3.12.so
   export IDS4ALL_PYTHON_SITE_PACKAGES=/path/to/venv/lib/python3.12/site-packages
   ```

`PYTHONNET_PYDLL` also works instead of `IDS4ALL_PYTHON_DLL`. Python versions and differences from
`requirements.txt` are in [DEPENDENCIES.md](DEPENDENCIES.md).

## Build

Run all commands from the repository root.

```sh
dotnet build csharp/IDS4ALL.sln
```

## Run

```sh
dotnet run --project csharp/src/IDS4ALL.Cli -- --excel_path ./Excel-files/ --excel_name IDS4ALL-Template --output_path ./output/
```

The options and the defaults are the same as in `IDS4ALL-main.py`.

| Option | Default | Use |
| ------ | ------- | --- |
| `--excel_path` | `./Excel-files/` | Folder of the Excel file. |
| `--excel_name` | `Requirements-openBIM-building-permit-City-of-Vienna-extended` | Name of the Excel file, without extension. |
| `--excel_format` | `.xlsx` | Extension of the Excel file. |
| `--output_path` | `./output/` | Folder for the IDS files. The program creates it. |

Relative paths start at the current folder. Use `-h` to show the help.

The output file name is `<excel_name>_<Sheet name><separation key>.ids`, for example
`IDS4ALL-Template_Specifications_PhaseLP3.ids`.

### Exit codes

| Code | Meaning |
| ---- | ------- |
| 0 | The conversion ran. A file that cannot be written prints `Error:  <message>`, and the run continues. |
| 1 | The workbook, a facet or the Python set-up has an error. The message is on stderr. |
| 2 | An argument is invalid. |

## Test

```sh
dotnet test csharp/IDS4ALL.sln
```

Tests that need Python skip when `IDS4ALL_PYTHON_DLL` and `PYTHONNET_PYDLL` are not set. Set
`IDS4ALL_PYTHON_SITE_PACKAGES` too, to a Python environment with the packages of
[../requirements.txt](../requirements.txt). The differential tests compare the C# results with
`custom_functions.py` and `IDS4ALL-main.py`.

| Variable | Use |
| -------- | --- |
| `IDS4ALL_FUZZ_SEED` | Seed of the random sheets. |
| `IDS4ALL_FUZZ_RUNS` | Number of random cases. Each random test uses a part of it. |

## Differences from the Python version

The C# version gives the same IDS files for valid input. For invalid input it stops with a clear message
where Python stops with a traceback or later.

- A sheet where empty AND values were removed from some columns only gives an `ImportException`. Python stops with `IndexError`.
- A description, URI, cardinality, relation or data type with several values or a restriction gives an error when the facet is created. Python fails when it writes the file.
- A length restriction above the `int` range gives an error. Python takes any integer.
- A number as column name, a number as `Sheet name` and a `Skipped rows` value that is not a whole number give an error or are read as text. Python stops with an error.
- `Upper` and `Lower` use the Unicode data of Python 3.14. Python 3.12 has older data and differs for a few characters that Unicode 16 added.
- ClosedXML and openpyxl read a few special cells in another way. The cases are tests in
  `tests/IDS4ALL.Tests/Excel/XlsxReaderDifferenceTests.cs`.

## Project structure

| Project | Content |
| ------- | ------- |
| `src/IDS4ALL.Core` | Excel import, parsing, merging of specifications, IDS file model. |
| `src/IDS4ALL.IfcTester` | Python.NET bridge. It writes the IDS files with `ifctester`. |
| `src/IDS4ALL.Cli` | Command line. |
| `tests/IDS4ALL.Tests` | Unit tests and differential tests against the Python version. |

## License

The code is under the MIT License. The Excel files are data under CC BY 4.0.
