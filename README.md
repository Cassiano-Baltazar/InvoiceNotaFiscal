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
- NFS-e (nota fiscal de serviço) linked to each invoice: draft created when the invoice is paid, import of the XML files issued on the city hall portal

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

## NFS-e

Notes are issued from the draft through the national API (Emissor Nacional, layout 1.01). Notes issued elsewhere
(the city hall portal or the Emissor Nacional website) can still be imported from their XML.

- **Draft on payment:** marking an invoice as paid creates an NFS-e draft linked to it, with the taker, description
  and fiscal codes (activity, service list item, NBS, tax situation, ISS rate) copied from the latest note of the same
  client. On the draft you can type the exchange rate to fill the amount in BRL. A draft can also be created from
  the invoice page or from **NFS-e > New Draft**.
- **Import:** **NFS-e > Import XML** accepts several files at once (for example, every file of the `NFSe` folder).
  For each note you choose the invoice it belongs to; the app suggests one from the client name and the service
  month in the description.
- **Rules:** an invoice has at most one issued note. Importing the issued note replaces the invoice's draft.
  Replaced (*substituída*) and cancelled notes are kept as history. The same XML cannot be imported twice
  (checked by the national access key, or by number and series). An invoice with an NFS-e cannot be deleted.
- The original XML is stored with the note and shown on its page.
- **Foreign taker and foreign trade:** the client record has the ISO country code and the tax id (NIF) in its
  country, or the reason it has none. A new draft copies them, together with the invoice currency and total
  (amount in the foreign currency), the service mode (cross-border by default), whether the company is related to
  the taker and the country where the result is used. These are the data the NFS-e needs for an exported service.
  On the draft, the exchange rate turns the foreign amount into the amount in BRL.
- **Issue through the Emissor Nacional:** **Issue NFS-e** on a draft checks the data, builds the DPS, validates it
  against the official XSD (`Schemas/Nfse`), signs it with the certificate (XMLDSig, RSA-SHA256) and sends it to the
  Sefin Nacional API with mutual TLS. The issued note's number, access key and XML are stored with it. Setup in
  **Settings > NFS-e (Emissor Nacional)**: environment, municipality code (IBGE, 4309308 for Guaíba), DPS series and
  next number, default national tax code (`010101`) and the approximate Simples Nacional rate (`pTotTribSN`).
  The DPS is built for a Simples Nacional ME/EPP company exporting a service: ISS as export (`tribISSQN` 3), foreign
  taker with NIF and foreign trade group (`comExt`, currency in the BACEN code).
  - **Restricted production** (default) sends test notes with no fiscal value: the draft stays a draft and the
    message shows the test number and key. Switch to **Production** once a test note is accepted.
  - The DPS number is reserved on the first attempt and kept on the draft. A retry after a rejection or a network
    error sends the same DPS, and first asks the API whether it was already processed, so a note is never issued twice.
  - Cancelling an issued note is done on the Emissor Nacional website.
- **Digital certificate:** **Settings > Digital Certificate (NFS-e)** stores the e-CNPJ A1 certificate (`.pfx`)
  that will be used to issue NFS-e. The file is checked with its password (it must contain the private key) and
  stored in the database, with holder, issuer and expiry date shown on the screen. The password is encrypted with
  the key in `App_Data/certificate.key` (created on first run; another path can be set in `App:CertificateKeyPath`).
  When moving the app to another machine, copy `certificate.key` together with the database; without it the
  certificate has to be uploaded again.

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
one item with its original description and amount, and the `Nfses` table is created.

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
