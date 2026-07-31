# CLAUDE.md — PgOperator 项目开发指南

## 项目概述

Windows WPF 桌面程序，通过 SSH.NET 远程管理 Ubuntu/Debian 上的 PostgreSQL。

## 关键约束

- 个人工具，无需多用户/权限/加密考量
- 凭证明文存 SQLite，用于 SSH 和 PG 连接
- 启动直接进入服务器列表（无登录界面）
- UI 使用 Light 主题 + MaterialDesign

## 架构约定

### 导航

- `MainViewModel` 是 Singleton，`CurrentView` 绑定到主窗口的 `ContentControl`
- 子页面通过 `DashboardView.SetCtx<T>()` 反射调用 ViewModel 的 `SetContext(server, pgInstance)`
- 所有功能按钮使用 `Click` 事件而非 `Command` 绑定（避免 WPF 绑定问题）
- `DashboardViewModel` 是 Singleton（子页面共享 `SelectedServer`）

### SSH 执行

- `SshService.ExecuteCommandAsync` — 普通命令，等完成后返回
- `SshService.ExecuteCommandWithProgressAsync` — 带回调的流式输出
- `SshService.ExecutePsqlAsync` — 通过 SSH 执行 psql 命令
- 路径问题：使用 `$HOME/pg_backups` 而非 `~`（shell 展开兼容性）
- SSL 问题：需要时加 `PGSSLMODE=disable`
- 远程命令无 tty，`sudo` 需要 `-S` + 密码管道

### DI 注册

- Singleton: `MainViewModel`, `DashboardViewModel`, `ISshService`, `IDatabaseService`, `DiagnosticEngine`, `AiAnalysisService`, `BackupService`
- Transient: 其余所有 ViewModel 和 View

### 安全编码规范

- **Shell 注入防护**: 密码中的单引号必须用 `'\\''` 转义。典型模式：`(password).Replace("'", "'\\''")`
- **SQL 注入防护**: 数据库名/用户名中的单引号必须双写转义：`.Replace("'", "''")`
- **NRE 防护**: 反序列化后必须 `.Where(r => r != null)` 过滤后再 `.Cast<T>()`
- **数值解析**: 始终使用 `TryParse` 而非 `Parse`，特别是解析远程命令输出时
- **数组越界**: 索引访问前检查 `parts.Length >= expectedCount`
- **Sudo 管道**: `curl | echo 'pwd' | sudo gpg` 中 echo 丢弃 stdin，需用 `sudo sh -c 'curl | gpg'`

### 资源管理

- `App.OnExit` 中调用 `_serviceProvider?.Dispose()` 释放所有 Singleton 的 `IDisposable`（特别是 SshService 的 SSH 连接）
- `SshService.Dispose()` 遍历并关闭所有 `_activeClients`
- SSH 客户端池化管理：`GetOrCreateClientAsync` 复用已连接的客户端
- 数据库初始化在 `OnStartup` 中同步完成（`GetAwaiter().GetResult()`），避免 WPF 窗口提前创建的竞态

## 已踩过的坑

1. **Transient 导致多实例** — `MainViewModel`/`DashboardViewModel` 必须是 Singleton
2. **XAML Command 绑定不可靠** — 改用 Click 事件
3. **DataGrid SelectedItem 不触发 OnChanged** — 改用 SelectionChanged + 手动调用方法
4. **Shell 管道被 echo 吃掉** — `curl \| echo 'pwd' \| sudo gpg` 中 echo 丢弃 stdin，需用 `sudo sh -c 'curl \| gpg'`
5. **`$(lsb_release -cs)` 在单引号中不展开** — 先预取 codename 再拼接
6. **gpg 无 tty** — 需要 `--batch --yes` 参数
7. **pg_basebackup 需要 replication 条目** — 部署时自动加 `host replication all 0.0.0.0/0 md5`
8. **`$HOME` 在 C# `$""` 插值字符串中冲突** — shell 变量用字符串拼接避免被C#解析
9. **物理备份目录不能用 `rm -f` 删** — 用 `rm -rf`
10. **`ContentHost.Content = view` 绑定被覆盖** — 用 `NavigateTo()` 设置 `CurrentView`
11. **SSH.NET BeginExecute + Task.Delay 阻塞 UI** — 改用 `Task.Run` 隔离 SSH.NET 轮询（`ExecuteCommandAsync`）
12. **StreamReader.Read() 阻塞无输出卡死** — `ExecuteCommandWithProgressAsync` 命令完成后再统一读取输出
13. **JsonSerializer.Deserialize 返回可空类型** — 需 `.Where(r => r != null).Cast<T>()` 过滤
14. **df 命令输出解析不稳定** — 需 `TryParse` + 检查列数 + 兜底逻辑
15. **物理备份空间预估 ×2** — `CheckDiskSpaceAsync` 返回值需重新计算 `CanProceed` 再判断

## AI Provider 架构

- `IAiProvider` 接口：`ChatAsync(systemPrompt, userMessage, ct)` 返回文本
- `OpenAiCompatibleProvider`：支持所有 `/v1/chat/completions` 兼容 API（OpenAI, DeepSeek, Ollama, 自定义）
- `ClaudeProvider`：Anthropic Messages API（`/v1/messages`）
- `AiProviderFactory.Create(config)` 根据 `config.Provider` 字符串创建对应实例
- Ollama 不需要 API Key（本地服务），工厂传 `"ollama"` 作为占位 key

## 诊断引擎扩展

新增检查需实现 `DiagnosticCheckBase`：
- 设置 `CheckId`（唯一标识如 `L1-CPU-004`）、`Layer`（1-3）、`Priority`（数字越小优先级越高）
- 实现 `ExecuteAsync(DiagnosticContext ctx)`
- 使用 `ctx.ExecAsync(cmd)` 执行 shell 命令，`ctx.QueryAsync(sql)` 执行 SQL
- 使用 `Ok()`/`Warning()`/`Critical()`/`Info()` 工厂方法返回结果
- 共享工具方法放 `Layer2_ConfigChecks.cs` 的 `PgSizeParser` 中（file-scoped static class）

## 数据库 Schema

SQLite 数据库包含以下表（`DatabaseInitializer.cs` 定义）：
- `server_connections` — SSH 服务器连接信息（明文存密码）
- `pg_instances` — PG 实例连接信息
- `settings` — 键值对设置（如 AI 配置）
- `diagnostic_reports` — 诊断报告 JSON
- `scheduled_tasks` — 定时任务定义
- `task_history` — 任务执行历史
- `alert_rules` — 告警规则定义
- `alert_history` — 告警触发历史
