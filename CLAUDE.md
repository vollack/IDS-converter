# CLAUDE.md

IDS4ALL converter: a Python CLI that converts information requirements in Excel sheets into
buildingSMART IDS files (`.ids`, XML). It uses `ifctester` to build and write the IDS.

## Commands

Use Python 3.10, 3.11 or 3.12 (see [README.md](README.md)). Run all commands from the repository root.

```sh
python -m venv venv
venv\Scripts\activate          # Linux: source venv/bin/activate
pip install -r requirements.txt
python IDS4ALL-main.py         # uses the defaults in IDS4ALL-main.py lines 9-11
python IDS4ALL-main.py --excel_path ./Excel-files/ --excel_name IDS4ALL-Template --output_path ./output/
```

The project has no test suite. To check a change, run the converter on both files in
[Excel-files/](Excel-files/) and compare the output with [Example-IDS-files/](Example-IDS-files/).
The `date` attribute in the output is always today, so ignore that difference.

## Code map

| File | Content |
| ---- | ------- |
| [IDS4ALL-main.py](IDS4ALL-main.py) | CLI arguments, metadata from the `IDS4ALL` sheet, one IDS file per separation key. |
| [custom_functions.py](custom_functions.py) | Excel parsing, specification merge logic, IDS facet creation, `datatype_base_dict`. |
| [Excel-files/](Excel-files/) | Template with the user manual for the column structure, and the City of Vienna example. |
| [Example-IDS-files/](Example-IDS-files/) | Reference output for the two Excel files. |

Pipeline in `excel_to_spec_list`:

1. Read the input sheet. Columns use the prefixes `A.` (applicability) and `R.` (requirements).
   `Phase`, `Role` and `Usecase` are general data. `Specification*` columns are specification data.
2. Merge rows with identical applicability. Values are joined with `|`.
3. Split values: `\&` separates AND values (one facet each), `|` separates OR values (enumeration).
4. Split specifications by the `File separators` from the `IDS4ALL` sheet, then merge them again
   per separation key.
5. Restructure specifications by IFC version, then copy requirements into more general specifications.

Value keywords: `_none_` and empty cells mean no value. `pattern=`, `\<`, `\<=`, `\>`, `\>=`,
`length=`, `length<=` and `length>=` are complex restrictions. A complex restriction cannot be part
of an enumeration.

## Conventions

- Match the style of the surrounding code: snake_case functions, `STRING_*` constants for column names.
- Read `R.`/`A.` column names through the `STRING_*` constants, never as literals.
- The Excel files are data under CC BY 4.0. The code is under the MIT License. Do not change the
  example Excel files to make a code change pass.

## Knowledge base

| File | Use for |
| ---- | ------- |
| [TODO.md](TODO.md) | Known bugs and open cleanup work. Remove an item in the commit that fixes it. |
| [CHANGELOG.md](CHANGELOG.md) | User-visible changes, Keep a Changelog format, under `## [Unreleased]`. |
| [DEPENDENCIES.md](DEPENDENCIES.md) | Why each package in `requirements.txt` is there. Update it with every dependency change. |

## Branches

- `main` is the default branch on GitHub. Do not commit to it directly.
- `dev` is the integration branch. Do each change on a local `claude/<short-description>` branch,
  then merge it into `dev` with `--no-ff`. Push only `dev`.
- A local `pre-push` hook in `.git/hooks/` rejects any push of a `claude/*` branch.
