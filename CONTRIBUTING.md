# 参与贡献

感谢你对 PgOperator 的关注！欢迎任何形式的贡献。

## 如何参与

### 报告 Bug

1. 在 [Issues](https://github.com/Diego-Yan/PgOperator/issues) 中搜索是否已有相同问题
2. 如果没有，创建新 Issue，包含：
   - 操作系统版本（macOS/Windows）
   - PgOperator 版本
   - 复现步骤
   - 期望行为 vs 实际行为
   - 截图或日志（如有）

### 提出功能建议

在 Issues 中创建，以 `[Feature]` 开头，描述使用场景和期望效果。

### 提交代码

1. Fork 仓库
2. 创建 feature 分支：`git checkout -b feature/your-feature`
3. 编写代码，确保：
   ```bash
   dotnet build   # 0 错误
   dotnet test    # 42/42 通过
   ```
4. 提交时遵循项目 commit 风格：
   ```
   feat: 添加 xxx 功能
   fix: 修复 xxx 问题
   ui: 优化 xxx 界面
   refactor: 重构 xxx
   test: 添加 xxx 测试
   docs: 更新 xxx 文档
   ```
5. Push 并创建 Pull Request

## 项目架构

```
PgOperator.Core/         ← 纯逻辑，不依赖任何框架
PgOperator.Infra/        ← SSH、SQLite 基础设施
PgOperator.Diagnostics/  ← 诊断引擎，新增检查需实现 DiagnosticCheckBase
PgOperator.AI/           ← AI 提供者，新增需实现 IAiProvider
PgOperator.App/          ← Avalonia UI，View + ViewModel
PgOperator.Tests/        ← 单元测试（MSTest）
```

### 新增诊断检查

1. 在 `PgOperator.Diagnostics/Checks/` 对应的 Layer 文件中创建类
2. 继承 `DiagnosticCheckBase`，设置 `CheckId`（如 `L1-CPU-005`）、`Layer`、`Priority`
3. 实现 `ExecuteAsync(DiagnosticContext ctx)`
4. 返回 `Ok()`/`Warning()`/`Critical()`/`Info()`

### 新增 AI 后端

1. 在 `PgOperator.AI/Providers/` 创建类
2. 实现 `IAiProvider` 接口
3. 在 `AiProviderFactory.Create()` 中注册

## 技术栈

| 层 | 技术 | 备注 |
|-----|------|------|
| UI | Avalonia 12 + Material.Avalonia 3 | 跨平台像素级一致 |
| MVVM | CommunityToolkit.Mvvm 8 | [ObservableProperty] + [RelayCommand] |
| 数据库 | SQLite + Dapper | 本地存储 |
| SSH | SSH.NET 2024 | 远程命令执行 |
| 日志 | Serilog | 滚动文件 |
| 测试 | MSTest | 单元测试 |

## 联系方式

- Issues: https://github.com/Diego-Yan/PgOperator/issues
- Discussions: https://github.com/Diego-Yan/PgOperator/discussions
