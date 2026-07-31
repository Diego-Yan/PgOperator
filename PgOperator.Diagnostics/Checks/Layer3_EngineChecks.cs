using PgOperator.Core.Models;

namespace PgOperator.Diagnostics.Checks;

// ─── Transaction / XID Checks ────────────────────────────

public class XidWraparoundCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-TXN-001";
    public override string CheckName => "xid_wraparound_risk";
    public override string Title => "事务ID回卷风险检查";
    public override int Layer => 3;
    public override string Category => "engine_transaction";
    public override int Priority => 1; // Highest priority

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT datname, age(datfrozenxid) AS xid_age, " +
            "round(100.0 * age(datfrozenxid) / 2147483648, 1) AS pct " +
            "FROM pg_database ORDER BY age(datfrozenxid) DESC LIMIT 1;");

        if (!r.Success) return Warning("无法检查事务ID回卷风险");

        var parts = r.Output.Trim().Split('|');
        if (parts.Length < 3) return Warning($"无法解析XID信息: {r.Output}");

        var dbName = parts[0].Trim();
        // [REVIEW-FIX] 使用 TryParse 保护解析，避免异常输出导致诊断引擎崩溃
        if (!long.TryParse(parts[1].Trim(), out var xidAge))
            return Warning($"无法解析XID age: {r.Output}");
        if (!double.TryParse(parts[2].Trim(), out var pct))
            return Warning($"无法解析XID百分比: {r.Output}");

        if (pct > 85)
            return Critical(
                $"数据库 {dbName} 事务ID使用率 {pct}%！仅剩 {2147483648 - xidAge} 个XID。按当前消耗速率即将触发只读模式！",
                "数据库进入只读模式，所有写入操作被拒绝。这是最紧急的PG问题！",
                new DiagnosticMetric { CurrentValue = pct, Unit = "percent", Threshold = 85, Direction = "above" },
                new DiagnosticSuggestion
                {
                    Action = "emergency_vacuum_freeze",
                    Commands = new() {
                        $"VACUUM FREEZE;",
                        "检查并终止长时间运行的事务: SELECT pid, now()-xact_start AS age, query FROM pg_stat_activity WHERE state='idle in transaction' ORDER BY age DESC;",
                        "调整autovacuum_freeze_max_age = 200000000"
                    },
                    Risk = "低(VACUUM FREEZE不阻塞读写)"
                });

        if (pct > 50)
            return Warning($"数据库 {dbName} 事务ID使用率 {pct}%，需关注",
                metric: new DiagnosticMetric { CurrentValue = pct, Unit = "percent", Threshold = 50, Direction = "above" },
                suggestion: new DiagnosticSuggestion
                {
                    Action = "plan_vacuum_freeze",
                    Commands = new() { "VACUUM FREEZE;", "确保autovacuum正常运行" },
                    Risk = "低"
                });

        return Ok($"事务ID使用率 {pct}% (数据库: {dbName})");
    }
}

public class LongRunningTransactionCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-TXN-002";
    public override string CheckName => "long_running_transaction";
    public override string Title => "长事务检测";
    public override int Layer => 3;
    public override string Category => "engine_transaction";
    public override int Priority => 3;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT count(*), max(extract(epoch from now()-xact_start))::int " +
            "FROM pg_stat_activity WHERE xact_start IS NOT NULL " +
            "AND state != 'idle' AND now()-xact_start > interval '30 minutes';");

        if (!r.Success) return Warning("无法检查长事务");

        var parts = r.Output.Trim().Split('|');
        if (!int.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count))
            return Warning("无法解析长事务计数");
        var maxSec = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var s) ? s : 0;

        if (count > 0)
            return Warning($"检测到 {count} 个超过30分钟的事务 (最长{maxSec / 60}分钟)",
                "长事务阻止VACUUM回收死元组，导致表膨胀和XID消耗加速",
                suggestion: new DiagnosticSuggestion
                {
                    Action = "terminate_long_tx",
                    Commands = new() {
                        "SELECT pid, now()-xact_start AS age, state, query FROM pg_stat_activity WHERE now()-xact_start > interval '30 minutes' ORDER BY xact_start;",
                        "考虑终止这些事务: SELECT pg_terminate_backend(pid);"
                    },
                    Risk = "中(可能丢失事务中的修改)"
                });

        return Ok("无长时间运行的事务");
    }
}

public class IdleInTransactionCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-TXN-003";
    public override string CheckName => "idle_in_transaction";
    public override string Title => "Idle-in-Transaction会话检查";
    public override int Layer => 3;
    public override string Category => "engine_transaction";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT count(*) FROM pg_stat_activity " +
            "WHERE state = 'idle in transaction' AND now()-state_change > interval '5 minutes';");

        if (!r.Success) return Warning("无法检查idle-in-transaction会话");
        if (!int.TryParse(r.Output.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count))
            return Warning("无法解析idle-in-transaction计数");

        if (count > 3)
            return Warning($"检测到 {count} 个超过5分钟的idle-in-transaction会话",
                "这些会话持有锁和资源，阻止VACUUM工作，消耗XID",
                suggestion: new DiagnosticSuggestion
                {
                    Action = "terminate_idle_in_transaction",
                    Commands = new() {
                        "SELECT pid, usename, application_name, now()-state_change AS duration, query FROM pg_stat_activity WHERE state='idle in transaction' AND now()-state_change > interval '5 minutes';",
                        "SET idle_in_transaction_session_timeout = '5min';"
                    },
                    Risk = "中"
                });

        if (count > 0)
            return Info($"检测到 {count} 个idle-in-transaction会话 (5min以内)");

        return Ok("无idle-in-transaction会话");
    }
}

// ─── Vacuum & Bloat Checks ───────────────────────────────

public class AutovacuumStatusCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-VAC-001";
    public override string CheckName => "autovacuum_status";
    public override string Title => "Autovacuum运行状态检查";
    public override int Layer => 3;
    public override string Category => "engine_vacuum";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW autovacuum;");
        if (!r.Success) return Warning("无法获取autovacuum状态");

        if (r.Output.Trim() != "on")
            return Critical("autovacuum=off！VACUUM不会自动运行，将导致事务ID回卷风险！",
                "表膨胀、XID耗尽、数据库最终进入只读模式",
                suggestion: new DiagnosticSuggestion
                {
                    Action = "enable_autovacuum",
                    Commands = new() { "ALTER SYSTEM SET autovacuum = on;", "SELECT pg_reload_conf();" },
                    Risk = "低"
                });

        // Check last autovacuum per table
        r = await ctx.QueryAsync(
            "SELECT count(*) FROM pg_stat_user_tables " +
            "WHERE (last_autovacuum IS NULL OR last_autovacuum < now() - interval '7 days') AND n_live_tup > 1000;");

        if (r.Success && int.TryParse(r.Output.Trim(), out var staleCount) && staleCount > 0)
            return Warning($"autovacuum=on，但 {staleCount} 个表超过7天未被autovacuum处理",
                "这些表可能正在膨胀",
                suggestion: new DiagnosticSuggestion { Action = "check_stale_tables", Commands = new() { "VACUUM ANALYZE on affected tables" }, Risk = "低" });

        return Ok("autovacuum=on，各表vacuum状态正常");
    }
}

public class DeadTupleCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-VAC-002";
    public override string CheckName => "dead_tuples";
    public override string Title => "死元组(Dead Tuples)检查";
    public override int Layer => 3;
    public override string Category => "engine_vacuum";
    public override int Priority => 8;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT relname, n_dead_tup, n_live_tup, " +
            "round(100.0 * n_dead_tup / NULLIF(n_live_tup, 0), 1) AS dead_ratio " +
            "FROM pg_stat_user_tables WHERE n_dead_tup > 1000 " +
            "ORDER BY dead_ratio DESC LIMIT 5;");

        if (!r.Success) return Warning("无法获取死元组信息");

        var lines = r.Output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var worst = new List<(string table, double ratio, long dead)>();

        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 4) continue;
            var table = parts[0].Trim();
            var dead = long.TryParse(parts[1].Trim(), out var d) ? d : 0;
            var _ = parts[2].Trim(); // live
            var ratio = double.TryParse(parts[3].Trim(), out var r2) ? r2 : 0;
            worst.Add((table, ratio, dead));
        }

        if (worst.Count > 0 && worst[0].ratio > 50)
            return Warning(
                $"表 {worst[0].table} 死元组比例 {worst[0].ratio}% ({worst[0].dead} dead tuples) — 严重膨胀！\n" +
                $"Top 5: {string.Join(", ", worst.Select(w => $"{w.table}({w.ratio}%)"))}",
                "查询性能下降，磁盘空间浪费",
                suggestion: new DiagnosticSuggestion
                {
                    Action = "vacuum_bloated_tables",
                    Commands = new() { $"VACUUM (VERBOSE, ANALYZE) {worst[0].table};" },
                    Risk = "低"
                });

        if (worst.Count > 0 && worst[0].ratio > 20)
            return Info($"表 {worst[0].table} 死元组比例 {worst[0].ratio}%，建议VACUUM");

        return Ok("各表死元组比例正常");
    }
}

// ─── WAL & Archiver Checks ───────────────────────────────

public class WalArchiveStatusCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-WAL-001";
    public override string CheckName => "wal_archive_status";
    public override string Title => "WAL归档状态检查";
    public override int Layer => 3;
    public override string Category => "engine_wal";
    public override int Priority => 4;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT archived_count, failed_count, " +
            "extract(epoch from now()-last_archived_time)::int AS seconds_since_last " +
            "FROM pg_stat_archiver;");

        if (!r.Success) return Warning("无法获取WAL归档状态");

        var parts = r.Output.Trim().Split('|');
        if (parts.Length < 3) return Info("WAL归档未启用或无归档记录");

        var archived = long.TryParse(parts[0].Trim(), out var arch) ? arch : 0;
        var failed = long.TryParse(parts[1].Trim(), out var fail) ? fail : 0;
        var secSinceLast = parts[2].Trim().Length > 0 && int.TryParse(parts[2].Trim(), out var secs) ? secs : 0;

        var failRate = archived + failed > 0 ? (double)failed / (archived + failed) * 100 : 0;

        if (failRate > 1)
            return Critical($"WAL归档失败率 {failRate:F1}% (成功{archived}, 失败{failed})！",
                "WAL持续堆积无法清理，磁盘将耗尽，且PITR不可用",
                suggestion: new DiagnosticSuggestion
                {
                    Action = "fix_archive_command",
                    Commands = new() {
                        "检查archive_command是否正确",
                        "检查归档目录磁盘空间和权限"
                    },
                    Risk = "高(可能导致WAL堆积)"
                });

        if (secSinceLast > 300 && archived > 0)
            return Warning($"最后一次WAL归档在 {secSinceLast / 60} 分钟前，可能存在归档延迟",
                "影响PITR恢复点目标(RPO)");

        return Ok($"WAL归档正常 (成功{archived}, 失败{failed}, 最后归档{secSinceLast}秒前)");
    }
}

// ─── Replication Checks ──────────────────────────────────

public class ReplicationStatusCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-REP-001";
    public override string CheckName => "replication_status";
    public override string Title => "流复制状态检查";
    public override int Layer => 3;
    public override string Category => "engine_replication";
    public override int Priority => 6;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        // PG12+
        var r = await ctx.QueryAsync(
            "SELECT application_name, state, sync_state, " +
            "pg_wal_lsn_diff(pg_current_wal_lsn(), sent_lsn) AS send_lag, " +
            "pg_wal_lsn_diff(pg_current_wal_lsn(), flush_lsn) AS flush_lag, " +
            "extract(epoch from write_lag)::int AS write_lag_sec " +
            "FROM pg_stat_replication;");

        if (!r.Success) return Warning("无法获取流复制状态");

        if (string.IsNullOrEmpty(r.Output.Trim()))
            return Info("未配置流复制");

        var lines = r.Output.Trim().Split('\n');
        var issues = new List<string>();

        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|');
            // [REVIEW-FIX] 修复数组越界：查询返回6列，原检查 < 5 会漏判第6列越界
            if (parts.Length < 6) continue;
            var name = parts[0].Trim();
            var state = parts[1].Trim();
            var syncState = parts[2].Trim();
            long.TryParse(parts[3].Trim(), out var sendLag);
            long.TryParse(parts[4].Trim(), out var flushLag);
            int.TryParse(parts[5].Trim(), out var writeLagSec);

            if (flushLag > 10 * 1024 * 1024) // >10MB
                issues.Add($"{name}: flush延迟{flushLag / 1024 / 1024}MB");
            if (writeLagSec > 30)
                issues.Add($"{name}: write延迟{writeLagSec}s");
            if (state != "streaming")
                issues.Add($"{name}: 状态={state}");
        }

        if (issues.Count > 0)
            return Warning($"流复制异常: {string.Join("; ", issues)}",
                "备库数据延迟，故障切换时可能丢失数据",
                suggestion: new DiagnosticSuggestion { Action = "check_replication", Commands = new() { "检查网络延迟", "检查备库磁盘I/O" }, Risk = "中" });

        return Ok($"流复制状态正常 ({lines.Length}个备库)");
    }
}

public class ReplicationSlotCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-REP-002";
    public override string CheckName => "replication_slot_overflow";
    public override string Title => "复制槽堆积检测";
    public override int Layer => 3;
    public override string Category => "engine_replication";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT slot_name, active, " +
            "pg_size_pretty(pg_wal_lsn_diff(pg_current_wal_lsn(), restart_lsn)) AS lag " +
            "FROM pg_replication_slots;");

        if (!r.Success) return Warning("无法获取复制槽信息");

        if (string.IsNullOrEmpty(r.Output.Trim()))
            return Ok("无复制槽");

        var lines = r.Output.Trim().Split('\n');
        foreach (var line in lines)
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 3) continue;
            var name = parts[0].Trim();
            var active = parts[1].Trim() == "t";
            var lag = parts[2].Trim();

            if (!active)
                return Warning($"复制槽 {name} 处于inactive状态，WAL堆积: {lag}",
                    "无消费者复制槽导致WAL无限堆积，磁盘将耗尽！",
                    suggestion: new DiagnosticSuggestion
                    {
                        Action = "cleanup_inactive_slot",
                        Commands = new() { $"检查备库连接状态", $"若确认不需要则删除: SELECT pg_drop_replication_slot('{name}');" },
                        Risk = "中(确认备库已废弃后操作)"
                    });
        }

        return Ok($"所有复制槽活跃 ({lines.Length}个)");
    }
}

// ─── Lock Checks ─────────────────────────────────────────

public class LockWaitCheck : DiagnosticCheckBase
{
    public override string CheckId => "L3-LCK-001";
    public override string CheckName => "lock_waiting";
    public override string Title => "锁等待检测";
    public override int Layer => 3;
    public override string Category => "engine_locks";
    public override int Priority => 7;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync(
            "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock';");

        if (!r.Success) return Warning("无法获取锁等待信息");
        if (!int.TryParse(r.Output.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var waiting))
            return Warning("无法解析锁等待计数");

        if (waiting > 5)
            return Warning($"检测到 {waiting} 个会话正在等待锁",
                "应用响应延迟，事务堆积",
                suggestion: new DiagnosticSuggestion
                {
                    Action = "investigate_locks",
                    Commands = new() {
                        "SELECT blocked.pid AS blocked_pid, blocked.query AS blocked_query, " +
                        "blocking.pid AS blocking_pid, blocking.query AS blocking_query " +
                        "FROM pg_stat_activity blocked JOIN pg_locks b_l ON blocked.pid=b_l.pid " +
                        "JOIN pg_locks bl_l ON b_l.locktype=bl_l.locktype AND b_l.database=bl_l.database " +
                        "AND b_l.relation=bl_l.relation AND b_l.page=bl_l.page " +
                        "AND b_l.tuple=bl_l.tuple AND b_l.virtualxid=bl_l.virtualxid " +
                        "AND b_l.transactionid=bl_l.transactionid AND b_l.classid=bl_l.classid " +
                        "AND b_l.objid=bl_l.objid AND b_l.objsubid=bl_l.objsubid AND b_l.pid<>bl_l.pid " +
                        "JOIN pg_stat_activity blocking ON bl_l.pid=blocking.pid " +
                        "WHERE NOT b_l.granted;"
                    },
                    Risk = "中"
                });

        if (waiting > 0)
            return Info($"检测到 {waiting} 个会话等待锁");

        return Ok("无锁等待");
    }
}
