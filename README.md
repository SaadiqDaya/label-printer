# VanGo Label Designer

A WPF (.NET 8) label designer and thermal-label printer for **VanGo Production** — the in-house
replacement for BarTender. Designs and prints labels on **Zebra ZD621** thermal printers, with
variable data from Excel/CSV, computed data sources, conditional printing, a read-only shop-floor
**Print Station**, and **JaneERP** integration over a local named pipe.

> **Users / operators / IT:** see the full manual in **[`docs/SOP.md`](docs/SOP.md)**
> (or the Word version `docs/LabelDesigner_SOP.docx`). This README is for developers/maintainers.

---

## Status

- Build: **0 warnings / 0 errors**. Tests: **xUnit, all green** (`LabelDesigner.Tests`).
- All template changes are **additive** with `OnDeserialized` backfill — old `.lbl` files keep loading.
- Default print path (GDI through the Windows driver) is the proven one; **native ZPL is opt-in**.

## Solution layout

| Project | Type | Contents |
|---|---|---|
| `LabelDesigner.Core` | .NET 8 class library | Models (`LabelTemplate`, elements, `DataSourceDefinition`, `FieldDefinition`, `PrinterProfile`, `TemplateRoute`, `LabelJob`) and pure services (`TemplateService`, `BarcodeValidator`, `FieldValidator`, `FormulaEvaluator`, `SerialFormatting`, `TemplateRouter`). No WPF. |
| `LabelDesigner` | .NET 8 WPF (WinExe) | ViewModels, Views, Designer canvas, Behaviors (attached properties), and services (`PrintService`, `ZplRenderer`, `RawPrinter`, `IpcServer`, `SerialCounterStore`, `PrintHistoryService`, `WatchFolderService`, `PrintJobParser`, `JobPrinter`, `TemplateRouteStore`, `AppConfig`, `UserSettings`, `LogService`, importers). |
| `LabelDesigner.Tests` | xUnit | Unit tests for the pure logic (validators, conditions, formulas, serial formatting, ZPL generation, template round-trip, template routing, job parsing, watch-folder file mechanics). |

## Build, test, run

```sh
dotnet build LabelDesigner.sln
dotnet test  LabelDesigner.sln
```

Run modes (same exe):

```sh
LabelDesigner.exe              # Designer (back office) — also runs the JaneERP listener
LabelDesigner.exe --operator   # Print Station (shop floor) — read-only operator window
```

Dependencies: `ClosedXML` (Excel), `ZXing.Net.Bindings.Windows.Compatibility` (barcodes),
`System.Text.Json` (templates).

## Configuration & file locations

Resolved by `AppConfig` — precedence: **environment variable → `appsettings.json` (next to the exe) → default**.

| Setting | Env var | Default |
|---|---|---|
| Templates dir | `LABELDESIGNER_TEMPLATES_DIR` | `Documents\LabelDesigner\Templates` |
| Data dir (counters / history / logs) | `LABELDESIGNER_DATA_DIR` | `%APPDATA%\LabelDesigner` |
| IPC pipe name | `LABELDESIGNER_PIPE` | `LabelDesigner` |

`appsettings.json` example (point all stations at one share for shop-wide serials/history):

```json
{ "TemplatesDir": "\\\\SERVER\\Labels\\Templates", "DataDir": "\\\\SERVER\\Labels\\Data" }
```

The **Data dir** can also be set per-machine via **File ▸ Settings** (Local vs Shared). Serial counters
(`counters.json`) and print history (`history.json`) are file-locked, so a shared **SMB/UNC** path is
safe across machines.

> ⚠️ Use a real SMB/UNC share for the shared data dir — **not** a OneDrive/SharePoint-synced folder
> (sync clients don't honour cross-machine file locks → duplicate serial numbers).

## Key concepts

- **Templates** (`.lbl`, JSON via `$type` polymorphism) hold elements, layers, fields, data sources, and a `PrinterProfile` (DPI, darkness/speed/media, output backend).
- **Print pipeline** (`PrintService`): validate → reserve serials → resolve per-label fields (data sources, field defaults/format, formulas) → render. Records print history with an exact-reprint snapshot.
- **Serials** (`SerialCounterStore`): reserved *before* printing (crash = safe gap, never duplicate), Continuous vs Reset-per-batch, prefix/suffix + base-36; reprints reproduce the exact original IDs.
- **Output backends:** GDI raster (default, device-DPI barcodes) or native **ZPL** (`ZplRenderer` + `RawPrinter`, opt-in via the printer profile).
- **Validation is fail-loud:** un-encodable barcodes, invalid/missing required fields, missing images, missing/offline printers, and unreachable shared serial stores all **block the print** with a clear message — never a silent blank or wrong-device print.
- **Print Station manual entry is type-aware:** each operator field renders the control that fits its `FieldDefinition.DataType` — allowed-values → dropdown, `Date` → calendar picker, `Number` → numeric-only box (see `Behaviors/NumericInput.cs`), else plain text. This is an entry-time guard only; `PrintService.ApplyFormat` + `FieldValidator` remain the formatting/validation authority.
- **Watch folders** (`WatchFolderService`, configured in File ▸ Settings, runs in the Print Station): any ERP drops a batch CSV into `<root>\inbox\`; the station claims it by an **atomic move** into `processing\<MACHINE>\` (the move is the lock — two stations can't double-print), then either auto-prints or queues it for operator release. Finished files move to `printed\` with a `.result.txt` audit sidecar, failures to `failed\` with `.error.txt` — **job files are never deleted**. Crash leftovers are returned to the inbox on next start.
- **Job CSV contract** (`CsvImportService.LoadGeneric` + `PrintJobParser`): header row of **field names** (order-independent), plus optional `Template` (per-row template), and `PrintQty`/`Qty`/`Copies` (blank = 1, `0` = skip). Rows route to a template by: `Template` column → routing rules (`TemplateRouter` + `TemplateRoutes.json` in the templates dir, edited via Template ▸ Template Routing…) → the folder's default template → reported as unroutable (never guessed).
- **Batch modes:** all-or-nothing (default — any bad row blocks the batch) or **skip-with-reason** (print the valid rows, list every skipped row + reason in the UI and the sidecar). Print Station rows are tickable with per-row quantity overrides ("(was N)").
- **Printed-by attribution:** every history entry records who printed (`PrintHistoryEntry.PrintedBy` — the Print Station operator name, Windows username, or `JaneERP` for IPC jobs); included in the history display and CSV export.

## Documentation

- **`docs/SOP.md`** — operator + IT manual (the source of truth).
- **`docs/LabelDesigner_SOP.docx`** — Word version for distribution.
- **`docs/generate_sop.py`** — regenerates the `.docx` from this script (`python generate_sop.py`).
  Drop screenshots in `docs/screenshots/<name>.png` to fill the figure placeholders. Keep `SOP.md`
  and `generate_sop.py` in sync.

## Conventions (please follow)

- **No lambda event subscriptions** — every `+=` uses a named method so it can be `-=`'d (prevents leaks).
- **Atomic file writes** (`*.tmp` + replace) for templates and stores.
- **Validate at boundaries**; model setters clamp; `OnDeserialized` backfills new fields (never break old `.lbl` load).
- **Never silently fall back** — surface the problem (see `PrintExceptions`, `PrinterNotFound` event, logging).
- **Render parity** — the canvas preview and the printed output must match.

## Known caveats / roadmap

- **Native ZPL output is best-effort until validated on a physical ZD621** with a scanner — barcode
  parameters and rotation need hardware confirmation. GDI is the default and unaffected.
- Watch-folder config changes apply when the Print Station next starts (no live reload yet).
- Not yet done: element naming/lock/group, smart-snap guides, GS1 AI builder, `--print` CLI,
  PNG/PDF export, curved text, signed installer/auto-update, guided `.btw` migration. See the
  team's roadmap notes for priorities.
