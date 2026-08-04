# PgOperator — PostgreSQL 运维管理工具

中文 | [English](README.en.md)

跨平台桌面程序，通过 SSH 远程管理 Ubuntu/Debian 上的 PostgreSQL 数据库。

![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0%2B%20%7C%209.0%20SDK-purple)
![License](https://img.shields.io/badge/license-MIT-green)
![Tests](https://img.shields.io/badge/tests-42%2F42-brightgreen)

## 功能模块

| 模块 | 说明 |
|------|------|
| 📦 PG部署 | 远程一键安装 PG 14/15/16/17，含环境检测、apt源配置、pg_hba配置、自动配置PG连接 |
| 🔍 一键诊断 | 33项检查覆盖 OS/配置/PG引擎三层，生成结构化报告（Critical/Warning/Info/Pass） |
| 🤖 AI分析 | 诊断结果交 DeepSeek/OpenAI/Claude/Ollama 分析，输出根因分析、操作步骤、风险评估 |
| 💾 备份恢复 | 逻辑备份(pg_dump) + 物理备份(pg_basebackup)，磁盘空间预检，PITR检查，备份过期清理 |
| 📊 SQL查询 | SQL编辑执行，EXPLAIN ANALYZE 执行计划分析 |
| ⚙️ 配置管理 | 远程编辑 postgresql.conf / pg_hba.conf，支持 Reload |
| 👤 用户权限 | 角色/用户管理，密码修改，权限变更，有效期设置 |
| 🔄 复制集群 | 流复制状态、复制槽管理、逻辑复制监控、复制主机自动修复 |
| 🔧 日常维护 | VACUUM / ANALYZE / REINDEX / 表膨胀检查 |
| 📁 对象浏览 | 数据库/表/索引/函数列表浏览 |
| 📥 导入导出 | CSV 导入导出 (COPY 命令) |
| ⏰ 定时任务 | 定时备份、定时诊断，支持 Cron 表达式 |

## 快速开始

### 环境要求

- macOS 14+ (ARM64) 或 Windows 10/11 x64
- 目标服务器：Ubuntu 20.04+ / Debian 11+
- .NET 9.0 SDK（开发）/ 自包含发布无需安装运行时

### 运行

```bash
# 开发运行（需要 .NET 9 SDK）
git clone https://github.com/Diego-Yan/PgOperator.git
cd PgOperator
dotnet run --project PgOperator.App

# macOS 发布版双击运行
dotnet publish PgOperator.App -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
bash bundle-macos.sh osx-arm64
open publish/PgOperator.app

# Windows 发布版
dotnet publish PgOperator.App -c Release -r win-x64 --self-contained true -o publish/win-x64
# 双击 publish/win-x64/PgOperator.App.exe
```

### 使用步骤

1. 启动程序 → 点击 `+` 添加服务器（SSH 地址/账号/密码，支持密码/密钥文件/密钥内容三种认证）
2. 测试连接 → 点击 `▶ 进入`
3. **配置PG密码** → 填入数据库连接信息
4. 各功能入口按钮激活，开始使用

### 首次部署 PG

1. 进入仪表盘 → 点击 `📦 PG部署`
2. 填写部署配置（PG版本、端口、密码、sudo密码）→ `🔍 环境检测` → 确认无误 → `📦 一键安装`
3. 安装完成后自动配置 PG 连接信息

## AI 配置

支持四种 LLM 后端：

| 后端 | API 地址 | 模型 |
|------|----------|------|
| DeepSeek | https://api.deepseek.com/v1 | deepseek-chat |
| OpenAI | https://api.openai.com/v1 | gpt-4o |
| Claude | https://api.anthropic.com/v1 | claude-sonnet-4-6 |
| Ollama | http://localhost:11434/v1 | llama3 |
| 自定义 | 任意 OpenAI 兼容地址 | 自定义 |

在 `🤖 AI设置` 中配置 API Key 即可使用。支持激进/平衡/保守三种分析偏好，性能/安全/成本三种关注重点。

## 诊断引擎

三层共33项检查：

| 层级 | 范围 | 检查项举例 |
|------|------|-----------|
| Layer 1 — OS | CPU/内存/磁盘/网络/时钟/内核 | CPU使用率、IO等待、内存、Swap、HugePages、THP、磁盘使用率、SSD/HDD检测、网络延迟、NTP同步、shmmax、swappiness、overcommit |
| Layer 2 — 配置 | PG参数配置/安全 | shared_buffers、effective_cache_size、work_mem、wal_level、max_wal_size、max_connections、慢查询日志、pg_hba.conf信任/通配规则 |
| Layer 3 — 引擎 | 事务/VACUUM/WAL/复制/锁 | XID回卷风险、长事务、idle-in-transaction、autovacuum、死元组膨胀、WAL归档、流复制状态、复制槽堆积、锁等待 |

支持 quick / standard / deep 三种诊断深度。

## 技术栈

- **UI**: Avalonia 12 + Material.Avalonia（跨平台，macOS + Windows 像素级一致）
- **架构**: MVVM (CommunityToolkit.Mvvm)
- **SSH**: SSH.NET (Renci.SshNet)
- **存储**: SQLite + Dapper
- **日志**: Serilog (滚动文件)
- **AI**: HttpClient 直连各 LLM API（OpenAI 兼容格式 + Claude Messages API）
- **测试**: MSTest (42 个单元测试)

## 项目结构

```
PgOperator/
├── PgOperator.Core/         # 领域模型、接口、业务服务（BackupService）
├── PgOperator.Infra/        # SSH（SshService）、存储（DatabaseService + DatabaseInitializer）
├── PgOperator.Diagnostics/  # 诊断引擎（3层33项检查 + MetricsSnapshot）
├── PgOperator.AI/           # AI 分析模块（多Provider：OpenAI兼容/Claude）
├── PgOperator.App/          # Avalonia 界面（MainWindow + Views + 16 ViewModels）
├── PgOperator.Tests/        # 单元测试（42 tests）
├── bundle-macos.sh          # macOS .app 打包脚本
└── CLAUDE.md                # AI 辅助开发指南
```

## 构建发布

```bash
# macOS ARM64
dotnet publish PgOperator.App -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
bash bundle-macos.sh osx-arm64

# Windows x64
dotnet publish PgOperator.App -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## 贡献

欢迎提交 Issue 和 PR！参与前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

### 开发环境

```bash
# 需要 .NET 9.0 SDK
git clone https://github.com/Diego-Yan/PgOperator.git
cd PgOperator
dotnet build   # 首次编译
dotnet test    # 运行 42 个单元测试
dotnet run --project PgOperator.App  # 启动 GUI
```

### 代码提交流程

1. Fork → 创建 feature 分支
2. 确保 `dotnet test` 全部通过
3. 提交 PR，描述改动内容和原因

## License

MIT — 欢迎自由使用、修改和分发。
