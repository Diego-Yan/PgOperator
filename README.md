# PgOperator — PostgreSQL 运维管理工具

Windows 桌面程序，通过 SSH 远程管理 Ubuntu/Debian 上的 PostgreSQL 数据库。

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
| 🚨 告警规则 | 自定义告警指标和阈值，支持冷却期和通知 |

## 快速开始

### 环境要求

- Windows 10/11 x64
- 目标服务器：Ubuntu 20.04+ / Debian 11+
- .NET 8.0（自包含发布无需安装）

### 使用步骤

1. 双击 `PgOperator.App.exe` 启动
2. 点击 `+` 添加服务器（SSH 地址/账号/密码，支持密码/密钥文件/密钥内容三种认证）
3. 测试连接 → 点击 `▶ 进入`
4. **配置PG密码** → 填入数据库连接信息
5. 各功能入口按钮激活，开始使用

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

## 安全说明

- 个人工具，凭证明文存储在本地 SQLite 数据库（`%LocalAppData%\PgOperator\data\pgoperator.db`）
- SSH 密码和 PG 密码在命令拼接时进行了 Shell 注入防护（单引号转义）
- SQL 查询中的数据库名进行了 SQL 注入防护（单引号双写转义）
- 物理备份需 pg_hba.conf 有 replication 条目（部署时自动配置）

## 技术栈

- **UI**: WPF + MaterialDesignInXamlToolkit
- **架构**: MVVM (CommunityToolkit.Mvvm)
- **SSH**: SSH.NET (Renci.SshNet)
- **存储**: SQLite + Dapper
- **日志**: Serilog (滚动文件，保存在 `%LocalAppData%\PgOperator\logs\`)
- **AI**: HttpClient 直连各 LLM API（OpenAI 兼容格式 + Claude Messages API）

## 项目结构

```
PgOperator/
├── PgOperator.Core/         # 领域模型、接口、业务服务（BackupService）
├── PgOperator.Infra/        # SSH（SshService）、存储（DatabaseService + DatabaseInitializer）
├── PgOperator.Diagnostics/  # 诊断引擎（3层33项检查 + MetricsSnapshot）
├── PgOperator.AI/           # AI 分析模块（多Provider：OpenAI兼容/Claude）
├── PgOperator.App/          # WPF 界面（MainWindow + Views + ViewModels）
├── PgOperator.Tests/        # 单元测试
└── publish/                 # 自包含发布输出（win-x64）
```

## 构建发布

```bash
dotnet publish PgOperator.App -c Release -r win-x64 --self-contained true -o publish
```

## 注意事项

- 个人工具，凭证明文存储在本地 SQLite 数据库中
- 物理备份需 pg_hba.conf 有 replication 条目（部署时自动配置）
- 导入导出使用 SQL COPY 命令，需 superuser 权限
- 物理备份目录需用 `rm -rf` 删除（不是 `rm -f`）
- 远程命令中 `$HOME` 变量在 C# `$""` 插值字符串中冲突，需用字符串拼接避免

## License

MIT
