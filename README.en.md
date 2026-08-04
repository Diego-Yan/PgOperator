# PgOperator — PostgreSQL Operations Management Tool

[中文版](README.md) | English

Cross-platform desktop application for remotely managing PostgreSQL databases on Ubuntu/Debian via SSH.

![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%2B%20%7C%209.0%20SDK-purple)
![License](https://img.shields.io/badge/license-MIT-green)
![Tests](https://img.shields.io/badge/tests-42%2F42-brightgreen)

## Features

| Module | Description |
|--------|-------------|
| 📦 PG Deploy | One-click remote install of PG 14/15/16/17 with env detection, apt source config, pg_hba setup |
| 🔍 Diagnostics | 33 checks across 3 layers (OS/Config/Engine), structured reports (Critical/Warning/Info/Pass) |
| 🤖 AI Analysis | Submit diagnostic results to DeepSeek/OpenAI/Claude/Ollama for root cause analysis & recommendations |
| 💾 Backup & Restore | Logical (pg_dump) + Physical (pg_basebackup) backups, disk space pre-check, PITR, retention cleanup |
| 📊 SQL Query | SQL editor with EXPLAIN ANALYZE support |
| ⚙️ Config Management | Remote editing of postgresql.conf / pg_hba.conf with Reload support |
| 👤 User Management | Role/user management, password changes, privilege grants, validity settings |
| 🔄 Replication | Streaming replication status, slot management, auto-fix replication hosts |
| 🔧 Maintenance | VACUUM / ANALYZE / REINDEX / table bloat checks |
| 📁 Object Browser | Browse databases, tables, indexes, functions |
| 📥 Import/Export | CSV import/export via COPY command |

## Quick Start

### Requirements

- macOS 14+ (ARM64) or Windows 10/11 x64
- Target servers: Ubuntu 20.04+ / Debian 11+
- .NET 9.0 SDK (development) / self-contained publish requires no runtime

### Run

```bash
# Development (requires .NET 9 SDK)
git clone https://github.com/Diego-Yan/PgOperator.git
cd PgOperator
dotnet run --project PgOperator.App

# macOS release (double-clickable .app)
dotnet publish PgOperator.App -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
bash bundle-macos.sh osx-arm64
open publish/PgOperator.app

# Windows release
dotnet publish PgOperator.App -c Release -r win-x64 --self-contained true -o publish/win-x64
# Double-click publish/win-x64/PgOperator.App.exe
```

### Usage

1. Launch → click `+` to add a server (supports password, private key file, or key content auth)
2. Test connection → click `▶ Enter`
3. **Configure PG password** → enter database connection info
4. All feature buttons activate and you're ready to go

## AI Configuration

Supports four LLM backends:

| Backend | API Endpoint | Model |
|---------|-------------|-------|
| DeepSeek | https://api.deepseek.com/v1 | deepseek-chat |
| OpenAI | https://api.openai.com/v1 | gpt-4o |
| Claude | https://api.anthropic.com/v1 | claude-sonnet-4-6 |
| Ollama | http://localhost:11434/v1 | llama3 |
| Custom | Any OpenAI-compatible endpoint | custom |

Configure your API key in `🤖 AI Settings`. Supports three analysis preferences (aggressive/balanced/conservative) and three focus areas (performance/security/cost).

## Diagnostic Engine

33 checks across 3 layers:

| Layer | Scope | Examples |
|-------|-------|----------|
| Layer 1 — OS | CPU/Memory/Disk/Network/Clock/Kernel | CPU usage, IO wait, memory, Swap, HugePages, THP, disk usage, SSD/HDD detection, network latency, NTP sync, shmmax, swappiness, overcommit |
| Layer 2 — Config | PG parameters & security | shared_buffers, effective_cache_size, work_mem, wal_level, max_wal_size, max_connections, slow query logging, pg_hba.conf trust/wildcard rules |
| Layer 3 — Engine | Transactions/VACUUM/WAL/Replication/Locks | XID wraparound risk, long transactions, idle-in-transaction, autovacuum, dead tuple bloat, WAL archiving, streaming replication, slot accumulation, lock waits |

Supports three diagnostic depths: quick / standard / deep.

## Tech Stack

- **UI**: Avalonia 12 + Material.Avalonia (pixel-identical across macOS & Windows)
- **Architecture**: MVVM (CommunityToolkit.Mvvm)
- **SSH**: SSH.NET (Renci.SshNet)
- **Storage**: SQLite + Dapper
- **Logging**: Serilog (rolling file)
- **AI**: HttpClient direct to LLM APIs (OpenAI-compatible + Claude Messages API)
- **Testing**: MSTest (42 unit tests)

## Project Structure

```
PgOperator/
├── PgOperator.Core/         # Domain models, interfaces, BackupService
├── PgOperator.Infra/        # SSH (SshService), storage (DatabaseService + Initializer)
├── PgOperator.Diagnostics/  # Diagnostic engine (33 checks across 3 layers)
├── PgOperator.AI/           # AI analysis (multi-provider: OpenAI-compatible/Claude)
├── PgOperator.App/          # Avalonia UI (MainWindow + 13 Views + 16 ViewModels)
├── PgOperator.Tests/        # Unit tests (42 tests, MSTest)
├── bundle-macos.sh          # macOS .app bundling script
└── CLAUDE.md                # AI-assisted development guide
```

## Build & Publish

```bash
# macOS ARM64
dotnet publish PgOperator.App -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
bash bundle-macos.sh osx-arm64

# Windows x64
dotnet publish PgOperator.App -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## Contributing

Issues and PRs are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) ([English](CONTRIBUTING.en.md)) for guidelines.

## License

MIT — free to use, modify, and distribute.
