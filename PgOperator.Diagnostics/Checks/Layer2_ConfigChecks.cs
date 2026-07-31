using PgOperator.Core.Models;

namespace PgOperator.Diagnostics.Checks;

// [REVIEW-FIX] 提取重复的 ParsePgSizeToMb 为共享工具方法，消除4处重复代码
file static class PgSizeParser
{
    public static double ParsePgSizeToMb(string size)
    {
        size = size.Trim().ToUpper();
        var style = System.Globalization.NumberStyles.Float;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (size.EndsWith("GB")) return double.TryParse(size[..^2].Trim(), style, culture, out var gb) ? gb * 1024 : 128;
        if (size.EndsWith("MB")) return double.TryParse(size[..^2].Trim(), style, culture, out var mb) ? mb : 128;
        if (size.EndsWith("KB")) return double.TryParse(size[..^2].Trim(), style, culture, out var kb) ? kb / 1024 : 128;
        if (size.EndsWith("TB")) return double.TryParse(size[..^2].Trim(), style, culture, out var tb) ? tb * 1024 * 1024 : 128;
        return double.TryParse(size, style, culture, out var n) ? n / (1024 * 1024) : 128;
    }
}

// ─── Shared Buffers Check ────────────────────────────────

public class SharedBuffersCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-001";
    public override string CheckName => "shared_buffers";
    public override string Title => "shared_buffers配置检查";
    public override int Layer => 2;
    public override string Category => "config_memory";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW shared_buffers;");
        if (!r.Success) return Warning("无法获取shared_buffers");

        var rMem = await ctx.ExecAsync("free -m | awk 'NR==2{print $2}'");
        var totalMemMb = rMem.Success && double.TryParse(rMem.Output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tMem) ? tMem : 8192;
        var sbStr = r.Output.Trim();
        var sbMb = PgSizeParser.ParsePgSizeToMb(sbStr);

        var recommended = totalMemMb * 0.25; // 25% for dedicated server — ideal target
        var minimumRecommended = recommended * 0.5; // warn below 12.5% (half the ideal)
        if (sbMb < minimumRecommended)
            return Warning($"shared_buffers={sbStr} (仅{sbMb:F0}MB)，服务器有{totalMemMb:F0}MB内存，建议设为{recommended:F0}MB (25%)，当前仅为{minimumRecommended:F0}MB以下",
                "缓存命中率低，大量磁盘I/O",
                new DiagnosticMetric { CurrentValue = sbMb, Unit = "MB", Threshold = minimumRecommended },
                new DiagnosticSuggestion { Action = "increase_shared_buffers", Commands = new() { $"ALTER SYSTEM SET shared_buffers = '{recommended:F0}MB';", "需重启PG生效" }, Risk = "中(需重启)" });

        if (sbMb > totalMemMb * 0.4)
            return Warning($"shared_buffers={sbMb:F0}MB 超过物理内存40%，可能导致OOM",
                metric: new DiagnosticMetric { CurrentValue = sbMb, Unit = "MB", Threshold = totalMemMb * 0.4, Direction = "above" });

        return Ok($"shared_buffers={sbStr} (服务器内存{totalMemMb:F0}MB)");
    }
}

public class EffectiveCacheSizeCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-002";
    public override string CheckName => "effective_cache_size";
    public override string Title => "effective_cache_size配置检查";
    public override int Layer => 2;
    public override string Category => "config_memory";
    public override int Priority => 10;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW effective_cache_size;");
        if (!r.Success) return Warning("无法获取effective_cache_size");

        var rMem = await ctx.ExecAsync("free -m | awk 'NR==2{print $7}'");
        var availMb = rMem.Success && double.TryParse(rMem.Output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var aMem) ? aMem : 4096;
        var ecsStr = r.Output.Trim();
        var ecsMb = PgSizeParser.ParsePgSizeToMb(ecsStr);

        if (ecsMb < availMb * 0.5)
            return Info($"effective_cache_size={ecsStr}，可用内存{availMb:F0}MB。建议设为此值的50-75%以优化查询计划",
                new DiagnosticSuggestion { Action = "increase_effective_cache_size", Commands = new() { $"ALTER SYSTEM SET effective_cache_size = '{availMb * 0.7:F0}MB';", "SELECT pg_reload_conf();" }, Risk = "低" });

        return Ok($"effective_cache_size={ecsStr} (可用内存{availMb:F0}MB)");
    }
}

public class WorkMemCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-003";
    public override string CheckName => "work_mem";
    public override string Title => "work_mem配置检查";
    public override int Layer => 2;
    public override string Category => "config_memory";
    public override int Priority => 12;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var rWm = await ctx.QueryAsync("SHOW work_mem;");
        var rMc = await ctx.QueryAsync("SHOW max_connections;");
        var rMem = await ctx.ExecAsync("free -m | awk 'NR==2{print $7}'");

        if (!rWm.Success || !rMc.Success) return Warning("无法获取work_mem或max_connections");

        var wmMb = PgSizeParser.ParsePgSizeToMb(rWm.Output.Trim());
        var maxConn = int.TryParse(rMc.Output.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mc) ? mc : 100;
        var availMb = rMem.Success && double.TryParse(rMem.Output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var am) ? am : 4096;

        var safePerConn = availMb / maxConn / 4; // 25% of available per connection
        if (wmMb > safePerConn * 2)
            return Warning($"work_mem={rWm.Output.Trim()}，可能偏大。在{maxConn}连接并发时最多消耗{wmMb * maxConn}MB (可用{availMb}MB)",
                "多并发排序/哈希操作可能导致OOM",
                suggestion: new DiagnosticSuggestion { Action = "reduce_work_mem", Commands = new() { $"ALTER SYSTEM SET work_mem = '{safePerConn:F0}MB';", "SELECT pg_reload_conf();" }, Risk = "低" });

        if (wmMb < 4 && availMb > 2048)
            return Info($"work_mem={rWm.Output.Trim()} (仅{wmMb}MB)。对于{availMb:F0}MB可用内存来说偏小，可能导致不必要的磁盘排序",
                new DiagnosticSuggestion { Action = "increase_work_mem", Commands = new() { "ALTER SYSTEM SET work_mem = '16MB';", "SELECT pg_reload_conf();" }, Risk = "低" });

        return Ok($"work_mem={rWm.Output.Trim()} (max_connections={maxConn}, 可用内存{availMb:F0}MB)");
    }
}

// ─── WAL & Checkpoint Checks ─────────────────────────────

public class WalLevelCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-010";
    public override string CheckName => "wal_level";
    public override string Title => "wal_level配置检查";
    public override int Layer => 2;
    public override string Category => "config_wal";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW wal_level;");
        if (!r.Success) return Warning("无法获取wal_level");

        var level = r.Output.Trim();
        if (level == "minimal")
            return Warning("wal_level=minimal — 不支持流复制和PITR！生产环境应至少使用replica",
                "无法搭建主从复制，无法进行PITR恢复",
                suggestion: new DiagnosticSuggestion { Action = "increase_wal_level", Commands = new() { "ALTER SYSTEM SET wal_level = 'replica';", "需重启PG生效" }, Risk = "中(需重启)" });

        return Ok($"wal_level={level}");
    }
}

public class MaxWalSizeCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-011";
    public override string CheckName => "max_wal_size";
    public override string Title => "max_wal_size配置检查";
    public override int Layer => 2;
    public override string Category => "config_wal";
    public override int Priority => 10;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW max_wal_size;");
        if (!r.Success) return Warning("无法获取max_wal_size");

        var sizeMb = ParsePgSizeToMb(r.Output.Trim());
        if (sizeMb < 1024)
            return Info($"max_wal_size={r.Output.Trim()} 偏小，可能导致频繁checkpoint",
                new DiagnosticSuggestion { Action = "increase_max_wal_size", Commands = new() { "ALTER SYSTEM SET max_wal_size = '4GB';", "SELECT pg_reload_conf();" }, Risk = "低" });

        return Ok($"max_wal_size={r.Output.Trim()}");
    }

        // [REVIEW-FIX] 使用共享 PgSizeParser 替代原来的 SharedBuffersCheck_Inner
        private static double ParsePgSizeToMb(string size) => PgSizeParser.ParsePgSizeToMb(size);
    }

// ─── Connection & Timeout Checks ─────────────────────────

public class MaxConnectionsCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-020";
    public override string CheckName => "max_connections";
    public override string Title => "max_connections配置检查";
    public override int Layer => 2;
    public override string Category => "config_connection";
    public override int Priority => 10;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var rMc = await ctx.QueryAsync("SHOW max_connections;");
        var rUsed = await ctx.QueryAsync("SELECT count(*) FROM pg_stat_activity;");

        if (!rMc.Success) return Warning("无法获取max_connections");
        var maxConn = int.TryParse(rMc.Output.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mxc) ? mxc : 100;
        var used = rUsed.Success && int.TryParse(rUsed.Output.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var u) ? u : 0;
        var usagePct = (double)used / maxConn * 100;

        if (maxConn > 300)
            return Info($"max_connections={maxConn} 偏大，建议使用连接池(pgBouncer)减少PG进程开销",
                new DiagnosticSuggestion { Action = "use_connection_pool", Commands = new() { "部署pgBouncer", "减少max_connections到200以下" }, Risk = "中" });

        if (usagePct > 80)
            return Warning($"连接使用率 {usagePct:F1}% ({used}/{maxConn})",
                metric: new DiagnosticMetric { CurrentValue = usagePct, Unit = "percent", Threshold = 80, Direction = "above" });

        return Ok($"max_connections={maxConn} (当前{used}连接)");
    }
}

public class StatementTimeoutCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-021";
    public override string CheckName => "statement_timeout";
    public override string Title => "statement_timeout配置检查";
    public override int Layer => 2;
    public override string Category => "config_connection";
    public override int Priority => 18;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW statement_timeout;");
        if (!r.Success) return Warning("无法获取statement_timeout");

        var timeout = r.Output.Trim();
        if (timeout == "0" || timeout == "0ms")
            return Info("statement_timeout未设置(0=无限)。建议设置全局超时(如30s)防止长时间查询占用资源",
                new DiagnosticSuggestion { Action = "set_statement_timeout", Commands = new() { "ALTER SYSTEM SET statement_timeout = '30s';", "SELECT pg_reload_conf();" }, Risk = "低(可能中断长报表)" });

        return Ok($"statement_timeout={timeout}");
    }
}

public class IdleInTransactionTimeoutCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-022";
    public override string CheckName => "idle_in_transaction_timeout";
    public override string Title => "idle_in_transaction_session_timeout检查";
    public override int Layer => 2;
    public override string Category => "config_connection";
    public override int Priority => 20;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW idle_in_transaction_session_timeout;");
        if (!r.Success) return Warning("无法获取idle_in_transaction_session_timeout");

        var timeout = r.Output.Trim();
        if (timeout == "0" || timeout == "0ms")
            return Info("idle_in_transaction_session_timeout未设置。建议设置为5min，防止长时间idle-in-transaction阻塞vacuum",
                new DiagnosticSuggestion { Action = "set_idle_timeout", Commands = new() { "ALTER SYSTEM SET idle_in_transaction_session_timeout = '5min';", "SELECT pg_reload_conf();" }, Risk = "低" });

        return Ok($"idle_in_transaction_session_timeout={timeout}");
    }
}

// ─── Logging Checks ──────────────────────────────────────

public class LogMinDurationCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-030";
    public override string CheckName => "log_min_duration_statement";
    public override string Title => "慢查询日志配置检查";
    public override int Layer => 2;
    public override string Category => "config_logging";
    public override int Priority => 15;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW log_min_duration_statement;");
        if (!r.Success) return Warning("无法获取log_min_duration_statement");

        var val = r.Output.Trim();
        if (val == "-1")
            return Info("log_min_duration_statement=-1 (禁用)。建议设置为1000ms，记录所有超过1秒的查询",
                new DiagnosticSuggestion { Action = "enable_slow_query_log", Commands = new() { "ALTER SYSTEM SET log_min_duration_statement = '1000';", "SELECT pg_reload_conf();" }, Risk = "低" });

        return Ok($"log_min_duration_statement={val}ms");
    }
}

public class LogLockWaitsCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-CFG-031";
    public override string CheckName => "log_lock_waits";
    public override string Title => "锁等待日志检查";
    public override int Layer => 2;
    public override string Category => "config_logging";
    public override int Priority => 15;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW log_lock_waits;");
        if (!r.Success) return Warning("无法获取log_lock_waits");

        if (r.Output.Trim() != "on")
            return Info("log_lock_waits=off。建议开启以记录所有超过deadlock_timeout的锁等待",
                new DiagnosticSuggestion { Action = "enable_log_lock_waits", Commands = new() { "ALTER SYSTEM SET log_lock_waits = on;", "SELECT pg_reload_conf();" }, Risk = "低" });

        return Ok("log_lock_waits=on");
    }
}

// ─── pg_hba.conf Security Checks ─────────────────────────

public class HbaTrustCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-SEC-001";
    public override string CheckName => "hba_trust_auth";
    public override string Title => "pg_hba.conf trust认证检查";
    public override int Layer => 2;
    public override string Category => "security_hba";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW hba_file;");
        if (!r.Success) return Warning("无法获取pg_hba.conf路径");

        var hbaPath = r.Output.Trim();
        r = await ctx.ExecAsync($"grep -i '^[^#]*trust' {hbaPath} 2>/dev/null || echo 'NO_TRUST_FOUND'");

        if (r.Output.Contains("NO_TRUST_FOUND"))
            return Ok("pg_hba.conf未发现trust认证规则");

        return Critical($"pg_hba.conf中发现trust认证:\n{r.Output}",
            "使用trust认证意味着任何知道用户名的人都可以无密码登录！",
            suggestion: new DiagnosticSuggestion { Action = "replace_trust_with_scram", Commands = new() { "修改pg_hba.conf将trust改为scram-sha-256", "SELECT pg_reload_conf();" }, Risk = "低(但需确保有有效密码)" });
    }
}

public class HbaWildcardCheck : DiagnosticCheckBase
{
    public override string CheckId => "L2-SEC-002";
    public override string CheckName => "hba_wildcard";
    public override string Title => "pg_hba.conf 通配地址检查";
    public override int Layer => 2;
    public override string Category => "security_hba";
    public override int Priority => 10;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.QueryAsync("SHOW hba_file;");
        if (!r.Success) return Warning("无法获取pg_hba.conf路径");

        var hbaPath = r.Output.Trim();
        // Match only 0.0.0.0/0 and ::/0 in the address column — these are the true wildcard patterns.
        // We intentionally don't match "all" as address since grep can't reliably determine
        // which column "all" appears in across varying hba_file whitespace formats.
        r = await ctx.ExecAsync($"grep -E '^[^#]*0\\.0\\.0\\.0/0|^[^#]*::/0' {hbaPath} 2>/dev/null || echo 'NO_WILDCARD'");

        if (r.Output.Contains("NO_WILDCARD"))
            return Ok("pg_hba.conf未发现过于宽松的地址规则");

        return Warning($"pg_hba.conf中存在通配规则(0.0.0.0/0或host all all):\n{r.Output}",
            "过于宽松的访问控制可能导致未授权访问",
            suggestion: new DiagnosticSuggestion { Action = "restrict_hba", Commands = new() { "修改pg_hba.conf限制来源IP范围" }, Risk = "低" });
    }
}
