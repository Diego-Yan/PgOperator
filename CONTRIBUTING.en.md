# Contributing

[中文版](CONTRIBUTING.md) | English

Thanks for your interest in PgOperator! All forms of contribution are welcome.

## How to Contribute

### Report a Bug

1. Search [Issues](https://github.com/Diego-Yan/PgOperator/issues) to see if it's already reported
2. If not, create a new issue with:
   - OS version (macOS/Windows)
   - PgOperator version
   - Steps to reproduce
   - Expected vs actual behavior
   - Screenshots or logs (if available)

### Feature Requests

Create an issue prefixed with `[Feature]`, describing the use case and desired outcome.

### Submit Code

1. Fork the repo
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Write code, ensuring:
   ```bash
   dotnet build   # 0 errors
   dotnet test    # 42/42 passing
   ```
4. Follow the project commit style:
   ```
   feat: add xxx feature
   fix: fix xxx issue
   ui: improve xxx UI
   refactor: restructure xxx
   test: add xxx tests
   docs: update xxx docs
   ```
5. Push and create a Pull Request

## Project Architecture

```
PgOperator.Core/         ← Pure logic, no framework dependency
PgOperator.Infra/        ← SSH, SQLite infrastructure
PgOperator.Diagnostics/  ← Diagnostic engine — new checks extend DiagnosticCheckBase
PgOperator.AI/           ← AI providers — new backends implement IAiProvider
PgOperator.App/          ← Avalonia UI (View + ViewModel)
PgOperator.Tests/        ← Unit tests (MSTest)
```

### Adding a Diagnostic Check

1. Create a class in `PgOperator.Diagnostics/Checks/` under the appropriate Layer file
2. Extend `DiagnosticCheckBase`, set `CheckId` (e.g. `L1-CPU-005`), `Layer`, `Priority`
3. Implement `ExecuteAsync(DiagnosticContext ctx)`
4. Return one of `Ok()` / `Warning()` / `Critical()` / `Info()`

### Adding an AI Backend

1. Create a class in `PgOperator.AI/Providers/`
2. Implement the `IAiProvider` interface
3. Register it in `AiProviderFactory.Create()`

## Tech Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| UI | Avalonia 12 + Material.Avalonia 3 | Pixel-identical cross-platform |
| MVVM | CommunityToolkit.Mvvm 8 | [ObservableProperty] + [RelayCommand] |
| Database | SQLite + Dapper | Local storage |
| SSH | SSH.NET 2024 | Remote command execution |
| Logging | Serilog | Rolling file |
| Testing | MSTest | Unit tests |

## Contact

- Issues: https://github.com/Diego-Yan/PgOperator/issues
- Discussions: https://github.com/Diego-Yan/PgOperator/discussions
