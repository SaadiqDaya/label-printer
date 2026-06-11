# VanGo Label Designer — Standard Operating Procedure (SOP)

**Version:** 1.0 (draft — to be expanded)
**Last Updated:** May 2026
**Applies To:** Label designers (back office), shop-floor operators, and IT staff

---

## Table of Contents

1. [Overview](#1-overview)
2. [The Two Ways to Run the App](#2-the-two-ways-to-run-the-app)
3. [Getting Started](#3-getting-started)
4. [The Designer Workspace](#4-the-designer-workspace)
5. [Creating, Opening & Saving Templates](#5-creating-opening--saving-templates)
6. [Label Elements](#6-label-elements)
7. [Barcodes](#7-barcodes)
8. [Images](#8-images)
9. [Layers & Conditional Printing](#9-layers--conditional-printing)
10. [Fields & Data (Excel / CSV)](#10-fields--data-excel--csv)
11. [Data Sources (Dates, Serials, Formulas)](#11-data-sources-dates-serials-formulas)
12. [Printing & Print Preview](#12-printing--print-preview)
13. [The Print Station (Shop Floor)](#13-the-print-station-shop-floor)
14. [Serial Numbers & Reprints](#14-serial-numbers--reprints)
15. [Label Setup, Settings & Shared Storage](#15-label-setup-settings--shared-storage)
16. [Native ZPL Output (Advanced / Opt-In)](#16-native-zpl-output-advanced--opt-in)
17. [JaneERP Integration](#17-janeerp-integration)
18. [Troubleshooting & FAQs](#18-troubleshooting--faqs)
19. [IT Setup & Deployment](#19-it-setup--deployment)

---

## 1. Overview

VanGo Label Designer is a Windows desktop application for designing and printing labels on thermal printers (Zebra ZD621). It is the in-house replacement for BarTender. It handles:

- **WYSIWYG label design** — text, barcodes, images, shapes, and tables on a canvas
- **Variable data** — fields filled from Excel/CSV files or typed by an operator, with `{{token}}` substitution and bound fields
- **Computed data sources** — dates, relative ("best-before") dates, serial counters, fixed values, and formulas
- **Conditional printing** — show/hide elements or whole layers based on the data
- **Batch printing** — print one label per record across a whole spreadsheet, honouring a per-row quantity
- **A read-only Print Station** for shop-floor operators
- **Print history with exact reprints**, and integration with **JaneERP** over a local named pipe

The app saves templates as `.lbl` files. Old templates always continue to load as the software is updated.

---

## 2. The Two Ways to Run the App

The same program runs in two modes depending on how it is launched:

| Mode | How it launches | Who uses it | What they can do |
|---|---|---|---|
| **Designer** | Run `LabelDesigner.exe` normally | Back office / template authors | Create and edit templates, set up data, manage fields, configure settings, print, and open the Print Station. Also runs the JaneERP listener. |
| **Print Station** | Run `LabelDesigner.exe --operator` (a desktop shortcut with that flag) | Line operators | Pick a template, fill or load data, preview, and print. **Read-only — cannot edit or save templates**, so master designs can't be changed by mistake. |

> The Designer can also open a Print Station window from **File ▸ Open Print Station** for testing. Shop-floor PCs should use the `--operator` shortcut.

---

## 3. Getting Started

### 3.1 Opening the App
Double-click the LabelDesigner shortcut. The Designer opens with the template list, the design canvas, and the properties panel.

### 3.2 The Main Window Layout
- **Left** — your saved templates and recent files.
- **Centre** — the design canvas (the label) with the record-navigation bar beneath it.
- **Right** — the Properties panel for the selected element, plus tabs for Layers, Data Sources, and the Element list.
- **Top** — the menu bar (File, Edit, Arrange, Template) and toolbar.

### 3.3 Keyboard Shortcuts
| Shortcut | Action |
|---|---|
| Ctrl+N / Ctrl+O / Ctrl+S | New / Open / Save template |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+C / Ctrl+V | Copy / Paste element |
| Ctrl+D | Duplicate selected element |
| Ctrl+G / Ctrl+Shift+G | Group / Ungroup selected elements |
| Ctrl+M | Manage Fields |
| Ctrl+P | Print |
| Delete | Delete selected element(s) |

---

## 4. The Designer Workspace

### 4.1 Adding Elements
Use the toolbar (or Add menu) to add a Text, Barcode, Image, Rectangle, Line, or Table element. Click an element to select it; its properties appear on the right.

### 4.2 Moving, Resizing, Rotating
- **Move** — drag the element. Hold to multi-select, or rubber-band-drag on empty canvas / Ctrl-click to select several.
- **Resize** — drag the handles around a selected element.
- **Rotate** — set the **R°** field in the Position & Size panel (e.g. 0, 90, 180, 270, or any angle). Rotation shows on the canvas exactly as it will print.

### 4.3 Aligning & Arranging (the Arrange menu)
Select two or more elements, then use **Arrange**:
- **Align** left / right / top / bottom / centre-horizontally / centre-vertically
- **Distribute** horizontally / vertically (3+ elements, even spacing)
- **Bring to Front / Send to Back** (and Move Forward/Backward for one step)
- **Duplicate** (Ctrl+D)

### 4.4 Snap to Grid, Smart Guides & Zoom
- **Snap to Grid** — toggle in the toolbar and set the grid size; dragging then moves elements in grid steps.
- **Smart guides** (the **Guides** toolbar toggle, on by default) — while dragging, the selection snaps to other elements' edges and centres and to the canvas edges/centre, and a pink dashed guide line shows what it snapped to. Turn the toggle off for free-hand placement.
- Use the zoom controls (or Ctrl+scroll) to zoom the canvas; this does not change the label size.

### 4.5 Naming, Locking & Grouping Elements
- **Name** — give an element a name in the **Element** box at the top of the Properties panel (e.g. "Lot barcode"). The name shows in the Elements list, which helps a lot on busy labels.
- **Lock** — tick **Locked** in the Properties panel. A locked element can't be moved, resized, or deleted on the canvas (its selection border turns grey and a 🔒 shows in the Elements list) — but it still prints normally. Use it to protect finished artwork from accidental nudges. Untick to edit again.
- **Group** — select two or more elements and press **Ctrl+G** (Arrange ▸ Group). From then on, clicking any member selects the whole group, and the group moves as one. Groups are **saved with the template**. **Ctrl+Shift+G** (Arrange ▸ Ungroup) dissolves the group. Ctrl-click still toggles a single member in or out of the selection when you need to adjust one element.

---

## 5. Creating, Opening & Saving Templates

### 5.1 New Template
1. **File ▸ New Template** (Ctrl+N).
2. Enter the label **name**, **width** and **height** (mm), and any starting field names.
3. Click **Create**.

### 5.2 Open / Recent
- **File ▸ Open** (Ctrl+O) to browse for a `.lbl` file, or pick from the template list / **Recent Templates**.

### 5.3 Save
- **File ▸ Save** (Ctrl+S) or **Save As**. Templates are written safely (atomic write) so a crash mid-save can't corrupt the file.

### 5.4 Label Setup (size, DPI, printer profile)
**Template ▸ Resize Canvas** opens **Label Setup**, where you set the label size, **printer resolution (DPI)** (203 or 300 for the ZD621), the **output backend** (GDI or ZPL), and optional **darkness / speed / ZPL host**. See Section 15.

### 5.5 Exporting a Label (PNG / PDF / ZPL)
**File ▸ Export** renders the label with the **same data the canvas preview shows** (the loaded data row, or the template's test data):
- **PNG** — an image at the template's DPI (e.g. for a spec sheet or a customer proof).
- **PDF** — a one-page PDF at the exact label size in mm, pixel-identical to the print preview.
- **ZPL** — the native ZPL II commands (see Section 16.2).

---

## 6. Label Elements

All elements share **Position & Size** (X, Y, W, H, Z-order, **Rotation**), an optional **Background** colour, a **Layer** assignment, and an optional **Print Condition** (Section 9).

### 6.1 Text
- **Default Value** — the static text, with `{{fieldName}}` tokens substituted from data.
- **Bound Field** — bind the whole text to one field (overrides Default Value at print time).
- Font family, size, bold/italic/underline, colour, alignment.
- **Fit to Box** — scale the text to fill the element.
- **Multi-line** — wrap long text onto multiple lines.

### 6.2 Barcode — see Section 7.

### 6.3 Image — see Section 8.

### 6.4 Shapes
Rectangle (with corner radius), Ellipse, Line, Triangle, Arrow, Diamond. Set fill colour, stroke colour, and stroke thickness. Lines follow your drag direction.

### 6.5 Table
Columns (header + bound field + width), row height, header/cell colours, borders, fonts. Enter static rows, or bind columns to fields for data-driven cells.

**Editing the data:** **double-click the table on the canvas** to open the spreadsheet-style row editor (click a cell and type; + Row / − Row; OK applies). The same rows can also be edited in the Properties panel's **Table Rows** section.

---

## 7. Barcodes

### 7.1 Choosing a Symbology
Select the **Format**. Supported: Code 128, **GS1-128**, Code 39, Code 93, ITF (Interleaved 2 of 5), Codabar, EAN-13, UPC-A, QR Code, Data Matrix, PDF417, Aztec.

### 7.2 Value
- **Value (preview)** — a static value, or
- **Bound Field** — the barcode value comes from a data field per record.

### 7.3 Scannability Settings
- **Show human-readable text** (1-D codes) with its own font.
- **X-dimension (mm)** — narrow-bar width; 0 = auto-fit. Setting it snaps bars to whole printer dots.
- **Quiet zone (mm)** — the clear margin each side (standards want ~10× the X-dimension).
- **2-D error correction** — for QR/Aztec.

### 7.4 Live Validation
As you type, a red banner warns if the value can't encode (e.g. EAN-13 needs 12–13 digits; ITF needs an even count). **A label with an un-encodable barcode is blocked from printing** and the offending field is named — it will never print a blank where a scannable code should be. EAN-13 / UPC-A **check digits are added automatically** when omitted.

### 7.5 GS1-128
Type the value with Application Identifiers in parentheses, e.g. `(01)09501101...(17)260531(10)LOT42`. The parentheses are display-only; the printer encodes the correct FNC1 separators.

> Always verify a new barcode on the physical printer with a scanner before relying on it in production.

---

## 8. Images

### 8.1 Static Image
Set **Image Path** (Browse). The image prints at the element size.

### 8.2 Data-Driven Image
- **Bound Field** — the image file path comes from a data field per record.
- **Base Folder** (optional) — combined with the field value, e.g. base `C:\icons` + field value `milk.png`.
- If the resolved file is missing, the element shows a **red placeholder** and the **print is blocked** with a clear message — never a silent blank.

### 8.3 Thermal Conditioning
Thermal printers are black-and-white. Set **Thermal mode** to get crisp output:
- **Color** — untouched (for colour/laser printers).
- **Grayscale** — convert to grey.
- **Threshold** — hard black/white at the **Threshold** cutoff.
- **Dither** (Floyd–Steinberg) — best for photos/logos on mono thermal.
- **Invert** — flip black/white.

What you see on the canvas is what prints.

---

## 9. Layers & Conditional Printing

### 9.1 Layers
On the **Layers** tab, add named layers, set visibility, and assign elements to layers. Layer order controls front/back stacking.

### 9.2 Print Conditions
Give an element (or a whole layer) a **Print Condition** so it only prints when the data matches. Supported syntax:

```
{Field} == "value"        {Field} != "value"
{Field} == {OtherField}   {Field} != {OtherField}
{Field} >= 10  (also >, <, <=)
{Field}        (field is non-empty)     !{Field}  (field is empty)
condA && condB            condA || condB
( ... )  grouping         !( ... )  NOT a group
```

Example: `({Color} == "Red" || {Color} == "Blue") && {Size} == "L"`.

> Use conditions to keep one template for many variants instead of many near-duplicate templates.

---

## 10. Fields & Data (Excel / CSV)

### 10.1 Manage Fields (Ctrl+M)
Open **Template ▸ Manage Fields**. Here you:
- Declare the **field names** the template uses.
- Map each field to a **column** in your data file (Excel `.xlsx` **or** CSV/TSV).
- Optionally choose a **PrintQty column** (how many labels per row; 0 or blank = skip that row).
- Set up a **secondary lookup file** (two-file join) keyed by a join column.
- Set **sample/test values** shown in the canvas when no live data is loaded.
- Set per-field **Required**, **Default value**, **Format**, and **validation rules** (length / numeric range / allowed values / pattern). Required/invalid data is caught before printing.

### 10.2 Excel vs CSV
Excel and CSV/TSV are interchangeable — the same column mapping works for both. Column letters (A, B, C…) map to the 1st, 2nd, 3rd column.

### 10.3 Loading Data in the Designer
The template remembers its data file. Use the record-navigation bar under the canvas to step through rows, jump to a record number, or open the **Record Browser**. The canvas previews the selected record.

### 10.4 Format & Default
If a field has a **Format** (e.g. `F2`, `dd/MM/yyyy`) it is applied when printing (e.g. `12.5` → `12.50`). If a field is blank and has a **Default value**, the default is used.

---

## 11. Data Sources (Dates, Serials, Formulas)

On the **Data Sources** tab, create computed fields that bind to elements just like normal fields. Types:

| Type | What it produces |
|---|---|
| **Current Date** / **Current Time** | Today's date / now, in the chosen format |
| **Relative Date** | Today ± months/days (e.g. a best-before date) |
| **Serial** | A counter (see Section 14) |
| **Fixed Value** | A constant string |
| **Formula** | A value computed from other fields (see below) |

### 11.1 Formula Fields
Set the **Formula** expression. Supports:
- `{Field}` references and `"text"` literals
- `&` to join text, `+ - * /` for maths, parentheses
- Functions: `UPPER LOWER TRIM LEN LEFT RIGHT MID CONCAT ROUND ABS`

Examples:
```
UPPER({sku}) & "-" & {lot}
{qty} * {price}
LEFT({code}, 3)
```
A formula error shows blank in the preview (it never crashes a print run).

### 11.2 Serial Options
See Section 14 for serial behaviour (continuous vs reset), increment, prefix/suffix, and alphanumeric serials.

---

## 12. Printing & Print Preview

### 12.1 Print Preview
**File ▸ Print** (Ctrl+P) opens the Print Preview window:
- Choose the **printer** (a Zebra is preferred automatically).
- Set **quantity**, or use **Print all records** to print every loaded row honouring each row's PrintQty.
- The preview renders exactly what will print. In **Print all records** mode, use the **◄ ►** arrows (top right) to step through every record **in the job** — rows with PrintQty 0 are excluded, because they won't print.
- Click **Print**.

### 12.2 What's Checked Before Printing
- Every barcode that will print is validated; an un-encodable one **blocks the whole job** and names the field.
- Required fields and field rules are enforced.
- For batches, the **whole batch is validated first** — a bad row stops the run **before** any label prints (it names the row number), so you never get a half-printed batch.
- If the chosen printer is missing/offline, you get a clear error — **no silent diversion to PDF or the default printer.**

### 12.3 Quantities
A row's PrintQty of **0 (or blank) means "skip"** — it is never treated as 1.

---

## 13. The Print Station (Shop Floor)

Launch with the **`--operator`** shortcut. The Print Station is read-only — operators cannot change templates.

### 13.1 Daily Loop
1. Type your name in the **Operator** box (it is remembered, and recorded on every print in the history).
2. **Search and pick a template** from the list.
3. The Station **auto-loads the template's data file** (Excel/CSV) if one is set, and shows the records. (If the template has no data file, you get simple input fields to type instead — required fields are marked `*` and dropdowns appear where allowed values are set.)
4. **Load file…** — point at today's data file if it differs (same columns).
5. Each record has a **tick box** (untick to leave it out) and an editable **quantity** — when you change a quantity, "(was N)" shows the file's original value. Rows the file marked `PrintQty 0` arrive unticked.
6. **Pick a record** and click **Print selected**, or set a **record range** and click **Print all in range** (prints every **ticked** row in the range at its quantity).
7. **Skip invalid rows (report them)** — normally a single bad row blocks the whole batch. Tick this to print the good rows instead; every skipped row and its reason is listed afterwards.
8. **Test print 1** — print one proof label to check registration/darkness/scannability **without using up a serial number**.
9. **Reprint** — from **Recent prints**, click **Reprint** to reproduce an earlier job **with the exact same serial numbers and date**.
10. **Export CSV** — export the print history for reconciliation/audit.

### 13.2 What the operator can rely on
- Required fields must be filled before printing.
- A missing data file or offline printer produces a clear message, not a silent failure.
- Reprints reproduce the original IDs; test prints don't consume serials.
- Every print records **who** printed it (the Operator box) in the history.

### 13.3 The Job Queue (watch folders)
If IT has configured **watch folders** (Section 19.6), batch jobs sent by the ERP appear automatically in the **Jobs** tab (next to Templates):

1. A new job shows the file name, when it arrived, and how many rows/template groups it contains.
2. **Click the job** — its rows appear grouped by template (each group also shows which printer it will use). Tick/untick rows and adjust quantities exactly like a data file. Click any row to preview that label.
3. Rows that could not be matched to a template are listed in red at the top — they will not print; the rest of the job still can.
4. **PRINT JOB** prints every ticked row, group by group. **Reject** moves the file to the `failed` folder without printing. **Close** leaves the job waiting in the queue.
5. When a job finishes, its file moves to the `printed` folder with a `.result.txt` report (what printed, what was skipped and why). Failures move to `failed` with a `.error.txt` explaining the problem. **Job files are never deleted.**

> Folders set to **auto-print** skip the queue: valid jobs print as soon as they arrive, and the status bar + history show the result.

---

## 14. Serial Numbers & Reprints

### 14.1 Serial Behaviour (set per Serial data source)
- **Continuous** — the counter keeps climbing across jobs/sessions. Needs a stable store (Section 15). Use for carton/lot sequences that must never repeat.
- **Reset per batch** — every print job starts again at the Start Value (e.g. 1). Needs no shared storage at all.

### 14.2 Options
- **Start Value** and **Increment** (amount added per label).
- **Prefix / Suffix** (e.g. `CTN-0001-A`).
- **Alphanumeric** (base-36, `0-9 A-Z`) with a **pad width** (e.g. `0001 → 000Z → 0010`).

### 14.3 Integrity Guarantees
- Serials are **reserved before printing**, so if a batch fails partway you get a safe **gap**, never a **duplicate** on retry.
- **Reprints reproduce the exact original serials and date** and do **not** advance the counter.
- A **test print** uses the current value without consuming it.
- If a shared serial store is unreachable, the print is **refused** (rather than silently restarting at the start value).

---

## 15. Label Setup, Settings & Shared Storage

### 15.1 Label Setup (per template)
**Template ▸ Resize Canvas** → set label size, **DPI** (203/300), **output backend** (GDI/ZPL), **darkness**, **speed**, **media type**, and a **ZPL network host** (Section 16). The registration **offset** can nudge a consistently mis-aligned label.

### 15.2 Settings (per workstation) — File ▸ Settings
Choose where **continuous serial counters and print history** are stored:
- **Local — this PC only** (default).
- **Shared network folder** — so several stations share one continuous sequence and a shop-wide audit trail. Browse to a UNC path (it is write-tested before saving).

> **Reset-per-batch** serials never use shared storage — only **Continuous** serials across multiple stations need it.

### 15.3 Where Files Live
By default, under `%APPDATA%\LabelDesigner`:
- `counters.json` — serial counters
- `history.json` — print history
- `logs\label-YYYYMMDD.log` — diagnostics

Templates live in `Documents\LabelDesigner\Templates` by default. Both locations can be pointed at a shared folder (Section 19).

---

## 16. Native ZPL Output (Advanced / Opt-In)

By default the app prints through the Windows Zebra driver (GDI). It can also emit **native ZPL II** for Zebra printers — printer-engine barcodes, smaller jobs, and direct-to-IP printing.

### 16.1 Enabling ZPL
In **Label Setup**, set **Output backend = Native ZPL**. Optionally set a **ZPL network host** (printer IP) to send over TCP port 9100; otherwise it goes to the selected Windows queue as raw data.

### 16.2 Inspecting ZPL Without a Printer
In the Print Station, **Export ZPL** saves the exact ZPL for the current label to a `.zpl` file so you can review it.

> ⚠️ **Validate ZPL output on a physical ZD621 with a scanner before using it in production.** The barcode parameters and rotation are best-effort until proven on hardware. The default GDI path is unaffected.

---

## 17. JaneERP Integration

JaneERP can send print jobs to the running Designer over a local named pipe (`\\.\pipe\LabelDesigner`, ACL-restricted to the current user).

- A **LabelJob** carries the template name, field values, quantity, printer, and an optional **JobId**.
- The Designer validates required fields, prints (or shows a preview), and returns a structured outcome (**printed / rejected / error**) keyed by JobId, logged for audit.
- Unattended jobs **fail loud** on a missing printer (no PDF diversion).

> The Designer (full app) runs the listener. Operator stations launched with `--operator` do not.

---

## 18. Troubleshooting & FAQs

### Nothing printed and I got a red message
Read the message — it means a barcode value couldn't encode, a required field was blank, the printer wasn't ready, a data file/image was missing, or (for continuous serials) the shared store was unreachable. **In all these cases nothing was printed**, so you can fix it and retry safely.

### A barcode won't scan
1. In the Designer, check the live validation banner on the barcode.
2. Increase the element size or set an explicit **X-dimension** and **quiet zone**.
3. For thermal, prefer 203/300 DPI matching the printer, and test on the real device with a scanner.

### The wrong number of labels printed
A row's **PrintQty of 0/blank means skip**. Check the PrintQty column mapping in Manage Fields and the per-row values.

### Serials repeated / jumped
- Multiple stations on the same **Continuous** template must point at the **same shared folder** (File ▸ Settings). See Section 19.
- A **reprint** reproduces the original serials on purpose. A new run continues from the next value.
- A batch that failed partway leaves a **gap** (safe), not a duplicate.

### A logo looks muddy on the label
Set the image's **Thermal mode** to **Dither** (or Threshold) so the app converts it to clean black/white instead of the driver halftoning it.

### "Why didn't this print?" — where to look
Open the latest file in `%APPDATA%\LabelDesigner\logs` (or your shared data folder). Every job, rejection, and error is logged with a timestamp.

### The Print Station can't be edited
That's by design — it's read-only so operators can't change master templates. Use the Designer to edit.

---

## 19. IT Setup & Deployment

### 19.1 Installing
Copy the built application folder to each workstation (an installer is planned). The app is a .NET 8 Windows desktop app; the Zebra Windows driver should be installed for GDI printing.

### 19.2 Two Shortcuts
- **Designer** — `LabelDesigner.exe` (back-office PCs).
- **Print Station** — `LabelDesigner.exe --operator` (shop-floor PCs).

### 19.3 Shared Folders (multi-station)
To share templates, serial counters, and print history across stations, put them on a **Windows file share (UNC path)** hosted on an always-on machine, and configure each station via **`appsettings.json`** next to the exe:
```json
{
  "TemplatesDir": "\\\\SERVER\\Labels\\Templates",
  "DataDir": "\\\\SERVER\\Labels\\Data"
}
```
or per-station via **File ▸ Settings** (data/serial/history location), or environment variables `LABELDESIGNER_TEMPLATES_DIR` / `LABELDESIGNER_DATA_DIR`.

> **Use a real SMB/UNC share — not a OneDrive/SharePoint-synced folder.** Sync clients don't honour cross-machine file locks and would cause duplicate serial numbers. The share host must be always-on; if it's unreachable, continuous-serial jobs are refused rather than risking duplicates.

### 19.4 Backups
Back up the **Templates** folder and the **Data** folder (`counters.json`, `history.json`, logs). A nightly copy to the share or a backup target is sufficient.

### 19.5 File Locations Summary
| What | Default location | Configurable via |
|---|---|---|
| Templates (`.lbl`) | `Documents\LabelDesigner\Templates` | appsettings `TemplatesDir` / env var |
| Serial counters, history, logs | `%APPDATA%\LabelDesigner` | appsettings `DataDir` / Settings / env var |
| Per-machine settings (incl. watch folders, operator name) | `%APPDATA%\LabelDesigner\settings.json` | (written by Settings dialog) |
| Template routing rules | `TemplateRoutes.json` in the Templates folder | Template ▸ Template Routing… |

### 19.6 Watch Folders (ERP job drop — works with ANY ERP)
A watch folder lets any system print labels by simply **writing a CSV file into a folder** — no integration code. Configure per station in **File ▸ Settings ▸ Watch folders** (takes effect when the Print Station next starts).

**Folder layout** (created automatically under the root you choose):
```
<root>\inbox\        ← the ERP drops job CSVs here
<root>\processing\<STATION>\   ← a station claims a job by moving it here (atomic = no double-printing)
<root>\printed\      ← completed jobs + .result.txt audit report
<root>\failed\       ← failed/rejected jobs + .error.txt with the reasons
```
Files are **moved, never deleted** — `printed\` is the audit trail (purge it on your own schedule). If a station crashes mid-job, its claimed files are returned to the inbox on the next start.

**Job CSV contract** — a header row of **field names** (matching the template's fields, any column order), plus optional columns:
- `Template` — the template name for that row (rows with different templates are fine in one file).
- `PrintQty` (or `Qty` / `Copies`) — labels for that row; blank = 1, `0` = skip, non-numeric = skip.

```csv
ItemName,BestBefore,PrintQty,Template
Chocolate Box,2026-09-01,12,DoorTreats 50x25
Gift Tag,2026-08-15,4,GiftTag 76x51
```

**How a row finds its template** (first hit wins): the row's `Template` column → the shared **routing rules** (Designer ▸ Template ▸ Template Routing…) → the watch folder's **default template** → otherwise the row is reported as unroutable and never guessed.

Routing rule conditions: **Equals**, **Contains**, **StartsWith** ("name begins with DT- → DoorTreats label"), **EndsWith**, and **NumericRange** ("Volume 0–60 → 50ml label" — unit suffixes like `50ml` parse fine). Rules are evaluated top to bottom; the first match wins.

**Per-folder options** (Settings): **Auto-print** (print on arrival vs wait for the operator) and **Skip bad rows** (print valid rows and report the skipped ones vs refuse the whole job — the safer default).

> The watch folder can live on a network share. Multiple stations may watch the **same** folder safely — the atomic claim guarantees each job prints exactly once.

### 19.7 HTTP Print API (for external systems)
The Print Station can serve a small local web API, compatible with the **BarTender shim** contract — so a system that already prints through that shim can switch to LabelDesigner by changing **one base URL** in its configuration. Off by default; enable it in **File ▸ Settings ▸ HTTP print API** (takes effect when the Print Station next starts).

| Endpoint | Purpose |
|---|---|
| `GET /health` | Liveness check → `{"status":"ok","service":"LabelDesigner","version":"…"}` |
| `GET /printers` | Installed printer names → `{"success":true,"printers":[…]}` |
| `POST /api/print` | Print a job (see below) |

**Print request** (JSON body):
```json
{
  "templatePath": "DoorTreats-50ml.btw",
  "printerName": "ZDesigner ZD621-203dpi ZPL",
  "jobName": "Order 1234",
  "labels": [ { "ProductName": "Mint", "LotNumber": "L1" },
              { "ProductName": "Mint", "LotNumber": "L1" } ]
}
```
- Each entry in `labels` is **one physical label** — send the same data twice to get two labels.
- Only the **file name** of `templatePath` matters: `…\DoorTreats-50ml.btw` (or `.lbl`, or a bare name) is matched to the LabelDesigner template **named** `DoorTreats-50ml`. Recreate each `.btw` design once in LabelDesigner under the same name.
- `printerName` is optional — the template's profile printer, then the station's selected printer, are used otherwise. A missing/offline printer **fails the job loudly**; it never falls back to a different device.
- The whole request is validated before anything prints (all-or-nothing). Success → `{"success":true,"labelsRendered":N,"printer":"…"}`; failure → `{"success":false,"error":"…"}`.
- Every printed label lands in the normal print history (source `HTTP`), so reprints work as usual.

**Security:** the API listens on **localhost only** — other machines cannot reach it. Run the calling system on the same PC as the Print Station (that is also how the BarTender shim is deployed).

---

*This SOP is a living document and will be expanded as the software grows. Screenshots and additional walkthroughs will be added. For technical issues or errors, check the log files and contact IT.*
