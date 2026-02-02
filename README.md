# CSV Checker

**CSV Checker** is a free, self-serve tool for validating CSV files *before* you import them into another system.

It detects **all issues in one pass** (not fail-fast) and produces a **structured, human-readable report** so you know exactly what’s wrong and where.

This is built for developers, operators, founders, and data folks who deal with CSV imports in the real world.

---

## What it does

CSV Checker analyzes uploaded CSV files and reports issues such as:

- Invalid or inconsistent column counts
- Malformed or unclosed quotes
- Encoding problems (UTF-8, Latin-1, etc.)
- Delimiter mismatches
- Empty or malformed rows
- Line ending inconsistencies
- Import-specific issues (e.g. Excel, Shopify)

The goal is not just to say *“this file is invalid”*, but to explain **why** — with row/column context.

---

## Key characteristics

- ✅ Detects *all* issues in a single run
- ✅ Human-readable, structured error output
- ✅ No accounts, no file retention
- ✅ No AI processing, no data training
- ✅ Designed for real-world, messy CSVs

---

## Live version

The hosted version is available at:

👉 **https://csv-checker.com**  

The public site is intended for end users. This repository is the source code.

---

## Tech stack

- **ASP.NET / Blazor (Server)**
- Razor pages + reusable UI components
- SQLite (used for lightweight telemetry)
- Azure App Service (Linux)
- Tailwind CSS (abstracted behind components)

The app is intentionally boring in a good way: no queues, no workers, no exotic infra.

---

## Development setup

### Prerequisites

- .NET SDK (matching the target framework in the project)
- A local SMTP server or email disabled (see below)

