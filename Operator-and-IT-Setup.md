# VanGo Label Designer — Operator & IT Setup (1-page)

## Two ways to run the app

| Mode | How to launch | Who | What they can do |
|------|---------------|-----|------------------|
| **Designer** (back office) | Run `LabelDesigner.exe` normally | Template authors | Create/edit templates, fields, data sources, layers, preview, print, **Settings**, open Print Station. Runs the JaneERP pipe listener. |
| **Print Station** (shop floor) | Run `LabelDesigner.exe --operator` (make a desktop shortcut with that flag) | Line operators | Pick a template, fill the prompts, preview, **Print**, **Reprint**. **Read-only — cannot edit or save templates**, so masters can't be corrupted. |

> The Designer can also open a Print Station window from **File ▸ Open Print Station** for testing. Shop-floor PCs should use the `--operator` shortcut so the designer never opens there.

## Serial numbers — pick the behaviour per data source (Data Sources panel)

- **Reset per batch** — every print job starts again at the Start Value (e.g. 1). **No shared folder needed, works fully offline.** Use this when each batch is numbered on its own.
- **Continuous** — the counter keeps climbing across jobs/sessions. Needs a stable store. For **one** label PC, Local is fine. For **multiple** PCs sharing one sequence, use a **shared folder** (below).

## Sharing across stations (only for Continuous serials on multiple PCs)

1. On an **always-on** machine (your server, or the JaneERP host), make a folder e.g. `C:\LabelData\Data` and **share it**:
   `net share LabelData=C:\LabelData /GRANT:"Domain Users",CHANGE`
2. Give the operator accounts **Modify** NTFS permission on that folder.
3. On each station: **File ▸ Settings ▸ Shared network folder**, Browse to `\\SERVER\LabelData\Data`. (It runs a write-test before saving.)
   - Or pin it for all stations via `appsettings.json` next to the exe:
     `{ "TemplatesDir": "\\\\SERVER\\LabelData\\Templates", "DataDir": "\\\\SERVER\\LabelData\\Data" }`
4. **Use a UNC path (`\\SERVER\...`), not a mapped drive.**
5. **Do NOT use a OneDrive/SharePoint-synced folder** for `DataDir` — sync doesn't honor cross-machine file locks and will create duplicate serials. Templates in OneDrive are tolerable; the counter must be on a real file share.

If the shared folder is **offline**, a Continuous job now **refuses to print** (clear message) instead of silently restarting serials — fix the share and retry.

## Where files live (per `DataDir`)
- `counters.json` — serial counters · `history.json` — print history · `logs\label-YYYYMMDD.log` — diagnostics
- Local default: `%APPDATA%\LabelDesigner`. **Back these up** (a nightly copy to the share is enough).

## Daily operator loop (Print Station)
1. Pick the template → 2. Fill the prompts (required ones show `*` and block printing if blank) → 3. Check the preview → 4. Set Qty → **Print** → 5. To recover jammed labels, find the job in **Recent prints** and click **Reprint** (reproduces the *original* IDs).

## "Why didn't it print?" — quick checks
- A red message appeared → read it (bad barcode value, printer not ready, missing required field, or shared store offline). Nothing was printed in those cases.
- Open the latest file in `…\logs\` — every job, rejection, and error is logged with a timestamp.
- Printer shows offline/paused in Windows → the app refuses the job rather than reporting a false "printed."

## Settings / config quick reference
- **File ▸ Settings** — Local vs Shared serial/history storage (per machine).
- **Label Setup dialog** — label size + **DPI** (203 or 300 to match the Zebra).
- `appsettings.json` (optional, next to exe) — `TemplatesDir`, `DataDir`, `PipeName` for admin-pinned deployment.
