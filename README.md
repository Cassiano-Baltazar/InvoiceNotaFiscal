# Invoice App

[![Tests](https://github.com/Cassiano-Baltazar/InvoiceNotaFiscal/actions/workflows/tests.yml/badge.svg)](https://github.com/Cassiano-Baltazar/InvoiceNotaFiscal/actions/workflows/tests.yml)
[![Build](https://github.com/Cassiano-Baltazar/InvoiceNotaFiscal/actions/workflows/build.yml/badge.svg)](https://github.com/Cassiano-Baltazar/InvoiceNotaFiscal/actions/workflows/build.yml)

Local invoice management system for a small software services company billing foreign clients.

## Features

- Client management with per-client currency, amount, and Wise payment link
- Monthly invoice generation
- Invoice list with filters
- On-screen invoice preview (report)
- PDF export and save to `Empresa/{YYYY-MM}/PDF/`
- Invoice actions: edit, delete, cancel/reactivate, mark as sent, mark as paid
- Multiple items per invoice (description, quantity, unit, unit price); subtotal and total are calculated from the items

## Requirements

- .NET 10 SDK
- Microsoft Edge or Google Chrome (for PDF export)

## Run (development)

```powershell
cd src\InvoiceApp.Web
dotnet run
```

The app opens at **http://localhost:5099** (browser opens automatically in Release/published builds).

## Invoice statuses and allowed actions

| Status    | Edit | Delete | Cancel | Reactivate | Mark as Sent | Mark as Paid |
|-----------|------|--------|--------|------------|--------------|--------------|
| Not Sent  | yes  | yes    | yes    | -          | yes          | yes          |
| Sent      | yes* | -      | yes    | -          | -            | yes          |
| Paid      | -    | -      | -      | -          | -            | -            |
| Cancelled | -    | -      | -      | yes        | -            | -            |

\* Editing a sent invoice sets it back to **Not Sent**. Reactivating a cancelled invoice also sets it to **Not Sent**.
If the invoice already had a saved PDF, editing regenerates it. Deleting removes the saved PDF.

The rules live in `Services/InvoiceRules.cs`.

## Tests

```powershell
dotnet test
```

The test project (`tests/InvoiceApp.Tests`) uses xUnit, bUnit (Blazor pages and components) and
`WebApplicationFactory` (the PDF endpoint and page rendering). Tests run against an in-memory SQLite
database and a fake PDF exporter, so no browser or real database is needed.

## Build executable (.exe)

```powershell
.\publish.ps1
```

The executable is generated at:

```text
dist/InvoiceApp.exe
```

Double-click `InvoiceApp.exe` to start the server and open the browser.

Requirements on the target machine:
- Windows 64-bit
- Microsoft Edge or Google Chrome (for PDF export)

No .NET installation required (self-contained publish).

## Continuous integration

- **Tests** (`.github/workflows/tests.yml`): runs `dotnet test` on every push to `main` and on pull requests.
- **Build** (`.github/workflows/build.yml`): builds the solution on Windows and publishes `InvoiceApp.exe` as a downloadable artifact.

## Database

SQLite database is stored at:

`src/InvoiceApp.Web/App_Data/invoiceapp.db` (or `dist/App_Data/invoiceapp.db` when running the .exe)

The database is created on first run. Databases from older versions are upgraded automatically at startup
(`Data/SchemaUpgrader.cs`): for example, the `InvoiceItems` table is created and each existing invoice gets
one item with its original description and amount.

## Output folder

PDFs are saved under the configured **Output Base Path** (default: `Empresa/` folder):

```text
Empresa/
└── 2026-09/
    └── PDF/
        ├── 2026-09-001.pdf
        └── 2026-09-002.pdf
```

## Invoice numbering

Format: `INV-{issueYear}-{issueMonth}-{sequence}`

Example: `INV-2026-09-001`

The number reflects the **issue month**, not the service month.
