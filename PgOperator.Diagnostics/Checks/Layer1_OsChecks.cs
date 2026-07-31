using PgOperator.Core.Models;

namespace PgOperator.Diagnostics.Checks;

// ─── CPU Checks ──────────────────────────────────────────

public class CpuUsageCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-CPU-001";
    public override string CheckName => "cpu_usage";
    public override string Title => "CPU使用率检查";
    public override int Layer => 1;
    public override string Category => "os_cpu";
    public override int Priority => 10;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("top -bn1 | grep 'Cpu(s)' | awk '{print $2+$4}'");
        if (!r.Success) return Warning("无法获取CPU使用率", "监控数据缺失");

        if (double.TryParse(r.Output.Trim(), out var cpu))
        {
            if (cpu > 90) return Critical($"CPU使用率 {cpu:F1}%，系统严重过载", "可能导致PG响应超时",
                new DiagnosticMetric { CurrentValue = cpu, Unit = "percent", Threshold = 90, Direction = "above" },
                new DiagnosticSuggestion { Action = "check_cpu", Commands = new() { "top -o %CPU", "检查高CPU进程" }, Risk = "低" });
            if (cpu > 70) return Warning($"CPU使用率 {cpu:F1}%，建议关注",
                metric: new DiagnosticMetric { CurrentValue = cpu, Unit = "percent", Threshold = 70, Direction = "above" });
            return Ok($"CPU使用率 {cpu:F1}%");
        }
        return Warning($"无法解析CPU使用率: {r.Output}");
    }
}

public class IoWaitCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-CPU-002";
    public override string CheckName => "iowait";
    public override string Title => "IO等待率检查";
    public override int Layer => 1;
    public override string Category => "os_cpu";
    public override int Priority => 15;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("top -bn1 | grep 'Cpu(s)' | awk '{print $10}' | sed 's/%//'");
        if (!r.Success) return Warning("无法获取IO等待率");

        if (double.TryParse(r.Output.Trim(), out var iowait))
        {
            if (iowait > 30) return Critical($"IO等待率 {iowait}%，磁盘严重瓶颈！", "所有磁盘操作都会阻塞，PG性能急剧下降",
                new DiagnosticMetric { CurrentValue = iowait, Unit = "percent", Threshold = 30, Direction = "above" },
                new DiagnosticSuggestion { Action = "check_disk_io", Commands = new() { "iostat -x 1 3", "检查磁盘性能" }, Risk = "低" });
            if (iowait > 15) return Warning($"IO等待率 {iowait}%，磁盘可能成为瓶颈",
                metric: new DiagnosticMetric { CurrentValue = iowait, Unit = "percent", Threshold = 15, Direction = "above" });
            return Ok($"IO等待率 {iowait}%");
        }
        return Warning($"无法解析IO等待率: {r.Output}");
    }
}

public class StealTimeCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-CPU-003";
    public override string CheckName => "steal_time";
    public override string Title => "CPU窃取率检查 (云环境)";
    public override int Layer => 1;
    public override string Category => "os_cpu";
    public override int Priority => 20;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("top -bn1 | grep 'Cpu(s)' | awk '{print $16}' | sed 's/%//'");
        if (!r.Success) return Ok("无法获取steal time (非云环境可忽略)");

        if (double.TryParse(r.Output.Trim(), out var steal))
        {
            if (steal > 10) return Critical($"CPU窃取率 {steal}%，云主机CPU严重超卖！",
                "PG性能无法保障，建议联系云服务商或迁移实例",
                new DiagnosticMetric { CurrentValue = steal, Unit = "percent", Threshold = 10, Direction = "above" },
                new DiagnosticSuggestion { Action = "migrate_instance", Commands = new() { "联系云服务商检查超卖" }, Risk = "高" });
            if (steal > 5) return Warning($"CPU窃取率 {steal}%，存在资源竞争",
                metric: new DiagnosticMetric { CurrentValue = steal, Unit = "percent", Threshold = 5, Direction = "above" });
            return Ok($"CPU窃取率 {steal}%");
        }
        return Warning($"无法解析steal time: {r.Output}");
    }
}

// ─── Memory Checks ───────────────────────────────────────

public class MemoryUsageCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-MEM-001";
    public override string CheckName => "memory_usage";
    public override string Title => "内存使用率检查";
    public override int Layer => 1;
    public override string Category => "os_memory";
    public override int Priority => 10;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("free -m | awk 'NR==2{printf \"%.0f %.0f\", $2,$7}'");
        if (!r.Success) return Warning("无法获取内存信息");

        // [REVIEW-FIX] 使用 TryParse 替代直接 Parse，避免异常输出导致崩溃
        var parts = r.Output.Trim().Split(' ');
        if (parts.Length < 2) return Warning("无法解析内存信息");
        if (!double.TryParse(parts[0], out var total) || !double.TryParse(parts[1], out var avail))
            return Warning($"无法解析内存数值: {r.Output}");
        // Calculate real usage: (total - available) / total, not the "used" column which includes buffers/cache
        var usagePct = ((total - avail) / total) * 100;

        if (usagePct > 95) return Critical($"内存使用率 {usagePct:F1}% (总计{total}MB, 可用{avail}MB)", "可能导致OOM Killer杀死PG进程",
            new DiagnosticMetric { CurrentValue = usagePct, Unit = "percent", Threshold = 95, Direction = "above" },
            new DiagnosticSuggestion { Action = "add_memory_or_reduce_buffers", Commands = new() { "检查shared_buffers配置", "考虑增加物理内存" }, Risk = "中" });
        if (usagePct > 85) return Warning($"内存使用率 {usagePct:F1}% (总计{total}MB, 可用{avail}MB)",
            metric: new DiagnosticMetric { CurrentValue = usagePct, Unit = "percent", Threshold = 85, Direction = "above" });
        return Ok($"内存使用率 {usagePct:F1}% (总计{total}MB, 可用{avail}MB)");
    }
}

public class SwapUsageCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-MEM-002";
    public override string CheckName => "swap_usage";
    public override string Title => "Swap使用率检查";
    public override int Layer => 1;
    public override string Category => "os_memory";
    public override int Priority => 12;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("free -m | awk 'NR==3{printf \"%.0f %.0f\", $2,$3}'");
        if (!r.Success) return Warning("无法获取Swap信息");

        var parts = r.Output.Trim().Split(' ');
        if (parts.Length < 2) return Ok("系统未配置Swap");

        // [REVIEW-FIX] 使用 TryParse 保护 Swap 解析
        if (!double.TryParse(parts[0], out var total) || !double.TryParse(parts[1], out var used))
            return Warning($"无法解析Swap数值: {r.Output}");
        if (total == 0) return Ok("系统未配置Swap");

        var usagePct = (used / total) * 100;
        if (usagePct > 50) return Warning($"Swap使用率 {usagePct:F1}% ({used}MB/{total}MB) — 内存不足导致大量换页，性能下降",
            metric: new DiagnosticMetric { CurrentValue = usagePct, Unit = "percent", Threshold = 50, Direction = "above" },
            suggestion: new DiagnosticSuggestion { Action = "reduce_swap_usage", Commands = new() { "vm.swappiness=1", "增加物理内存或减少shared_buffers" }, Risk = "中" });
        return Ok($"Swap使用率 {usagePct:F1}%");
    }
}

public class HugePagesCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-MEM-003";
    public override string CheckName => "huge_pages";
    public override string Title => "大页(HugePages)配置检查";
    public override int Layer => 1;
    public override string Category => "os_memory";
    public override int Priority => 25;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("cat /proc/meminfo | grep -E '^(HugePages_Total|HugePages_Free|Hugepagesize)' | awk '{print $2}'");
        if (!r.Success) return Ok("无法获取HugePages信息");

        var lines = r.Output.Trim().Split('\n');
        if (lines.Length < 2) return Ok("HugePages未配置");

        if (!double.TryParse(lines[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var total) ||
            !double.TryParse(lines[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var free))
            return Ok("无法解析HugePages信息");
        var size = lines.Length >= 3 && double.TryParse(lines[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sz) ? sz : 2048;

        if (total == 0)
            return Info("未配置HugePages。PG建议启用huge_pages=on以减少页表开销",
                new DiagnosticSuggestion { Action = "enable_huge_pages", Commands = new() { "echo 'vm.nr_hugepages=...' >> /etc/sysctl.conf", "postgresql.conf: huge_pages=on" }, Risk = "低(需重启PG)" });

        var used = total - free;
        var usedMb = used * size / 1024;
        return Ok($"HugePages: {total}页({size}KB/页), 已用{used}页({usedMb:F0}MB), 可用{free}页");
    }
}

public class TransparentHugePageCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-MEM-004";
    public override string CheckName => "transparent_hugepage";
    public override string Title => "透明大页(THP)检查";
    public override int Layer => 1;
    public override string Category => "os_memory";
    public override int Priority => 30;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("cat /sys/kernel/mm/transparent_hugepage/enabled");
        if (!r.Success) return Ok("无法获取THP状态");

        if (r.Output.Contains("[never]"))
            return Ok("THP已设置为never (推荐)");

        return Warning("透明大页(THP)未关闭。PG官方建议设置THP=never，避免性能抖动和内存浪费",
            "THP可能导致PG进程内存占用异常增大，引起OOM",
            suggestion: new DiagnosticSuggestion { Action = "disable_thp", Commands = new() {
                "echo never > /sys/kernel/mm/transparent_hugepage/enabled",
                "修改/etc/default/grub: transparent_hugepage=never" }, Risk = "低" });
    }
}

// ─── Disk Checks ─────────────────────────────────────────

public class DiskUsageCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-DISK-001";
    public override string CheckName => "disk_usage";
    public override string Title => "磁盘使用率检查";
    public override int Layer => 1;
    public override string Category => "os_disk";
    public override int Priority => 5;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("df -h / /var/lib/postgresql 2>/dev/null | awk 'NR>1{print $6, $5, $4}' | sed 's/%//'");
        if (!r.Success) return Warning("无法获取磁盘信息");

        foreach (var line in r.Output.Trim().Split('\n'))
        {
            var parts = line.Trim().Split(' ');
            if (parts.Length < 3) continue;
            var mount = parts[0];
            // [REVIEW-FIX] 使用 TryParse 保护磁盘使用率解析
            if (!double.TryParse(parts[1], out var pct)) continue;
            var avail = parts[2];

            if (pct > 90)
                return Critical($"分区 {mount} 使用率 {pct}% (剩余{avail}) — 磁盘即将耗尽！",
                    "WAL写入和表空间扩展将失败，数据库停止写入",
                    new DiagnosticMetric { CurrentValue = pct, Unit = "percent", Threshold = 90, Direction = "above" },
                    new DiagnosticSuggestion { Action = "expand_or_cleanup", Commands = new() { "清理旧备份/WAL归档/临时文件", "扩容磁盘" }, Risk = "高" });
            if (pct > 75)
                return Warning($"分区 {mount} 使用率 {pct}% (剩余{avail})",
                    metric: new DiagnosticMetric { CurrentValue = pct, Unit = "percent", Threshold = 75, Direction = "above" });
        }
        return Ok("所有分区磁盘使用率正常");
    }
}

public class DiskTypeCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-DISK-002";
    public override string CheckName => "disk_type";
    public override string Title => "磁盘类型检测 (SSD vs HDD)";
    public override int Layer => 1;
    public override string Category => "os_disk";
    public override int Priority => 30;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("lsblk -d -o NAME,ROTA 2>/dev/null | awk 'NR>1 && $2==\"1\"{print $1}'");
        if (!r.Success) return Info("无法检测磁盘类型");

        var hdds = r.Output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (hdds.Length > 0)
            return Info($"检测到HDD磁盘: {string.Join(", ", hdds)}。建议调整random_page_cost=4.0 (当前PG配置可能不合适)",
                new DiagnosticSuggestion { Action = "adjust_for_hdd", Commands = new() { "ALTER SYSTEM SET random_page_cost = 4.0;", "SELECT pg_reload_conf();" }, Risk = "低" });

        r = await ctx.ExecAsync("lsblk -d -o NAME,ROTA 2>/dev/null | awk 'NR>1{print $1, $2}'");
        return Ok($"检测到SSD/NVMe磁盘: {r.Output.Trim().Replace("\n", ", ")}");
    }
}

// ─── Network Checks ──────────────────────────────────────

public class NetworkLatencyCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-NET-001";
    public override string CheckName => "network_latency";
    public override string Title => "网络延迟检查";
    public override int Layer => 1;
    public override string Category => "os_network";
    public override int Priority => 15;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("ping -c 3 -W 2 8.8.8.8 2>/dev/null | tail -1 | awk -F'/' '{print $5}'");
        if (!r.Success || string.IsNullOrEmpty(r.Output.Trim()))
            return Warning("无法进行外网延迟测试 (DNS或网络不通)");

        if (double.TryParse(r.Output.Trim(), out var latency))
        {
            if (latency > 100) return Warning($"外网平均延迟 {latency}ms — 较高，可能影响流复制",
                metric: new DiagnosticMetric { CurrentValue = latency, Unit = "ms", Threshold = 100, Direction = "above" });
            return Ok($"外网平均延迟 {latency}ms");
        }
        return Warning($"无法解析网络延迟: {r.Output}");
    }
}

// ─── NTP / Clock Checks ──────────────────────────────────

public class NtpSyncCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-NTP-001";
    public override string CheckName => "ntp_sync";
    public override string Title => "时钟同步(NTP)检查";
    public override int Layer => 1;
    public override string Category => "os_clock";
    public override int Priority => 20;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("timedatectl show -p NTP -p NTPSynchronized 2>/dev/null");
        if (!r.Success)
        {
            // Fallback: check chrony
            r = await ctx.ExecAsync("chronyc tracking 2>/dev/null | grep -E 'Reference ID|Last offset'");
            if (r.Success && !string.IsNullOrEmpty(r.Output))
                return Ok("NTP同步正常 (chrony)");
            return Warning("无法检测NTP同步状态。时钟不同步可能导致流复制和日志关联异常");
        }

        if (r.Output.Contains("NTPSynchronized=yes"))
            return Ok("NTP时间同步正常");

        return Warning("NTP未同步！时钟偏差可能导致流复制异常、WAL归档时间戳错乱",
            "流复制延迟显示不准确，PITR恢复时间点定位错误",
            suggestion: new DiagnosticSuggestion { Action = "enable_ntp", Commands = new() { "timedatectl set-ntp true", "systemctl restart systemd-timesyncd" }, Risk = "低" });
    }
}

// ─── Kernel Parameter Checks ─────────────────────────────

public class KernelShmmaxCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-KERN-001";
    public override string CheckName => "kernel_shmmax";
    public override string Title => "共享内存上限(shmmax)检查";
    public override int Layer => 1;
    public override string Category => "os_kernel";
    public override int Priority => 20;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("sysctl -n kernel.shmmax 2>/dev/null");
        if (!r.Success) return Warning("无法获取shmmax值");

        // shmmax can be > long.MaxValue on 64-bit systems (unsigned long)
        if (!double.TryParse(r.Output.Trim(), out var shmmax))
            return Warning($"无法解析shmmax值: {r.Output}");

        var rMem = await ctx.ExecAsync("free -b | awk 'NR==2{print $2}'");
        var totalMem = rMem.Success && long.TryParse(rMem.Output.Trim(), out var tm) ? tm : 0;

        if (totalMem > 0 && shmmax < totalMem)
            return Info($"shmmax={shmmax / 1024 / 1024:F0}MB，小于物理内存{totalMem / 1024 / 1024}MB。建议增大以避免PG共享内存不足",
                new DiagnosticSuggestion { Action = "increase_shmmax", Commands = new() { $"sysctl -w kernel.shmmax={totalMem}" }, Risk = "低" });

        return Ok($"shmmax={shmmax / 1024 / 1024:F0}MB");
    }
}

public class SwappinessCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-KERN-002";
    public override string CheckName => "vm_swappiness";
    public override string Title => "Swappiness参数检查";
    public override int Layer => 1;
    public override string Category => "os_kernel";
    public override int Priority => 18;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("sysctl -n vm.swappiness 2>/dev/null");
        if (!r.Success) return Warning("无法获取swappiness值");

        var val = int.TryParse(r.Output.Trim(), out var swappinessVal) ? swappinessVal : -1;
        if (val < 0)
            return Warning($"无法解析swappiness值: {r.Output}");
        if (val > 10)
            return Info($"vm.swappiness={val} (建议≤1)。数据库服务器应尽量减少Swap使用以保证性能",
                new DiagnosticSuggestion { Action = "reduce_swappiness", Commands = new() { "sysctl -w vm.swappiness=1", "echo 'vm.swappiness=1' >> /etc/sysctl.conf" }, Risk = "低" });

        return Ok($"vm.swappiness={val}");
    }
}

public class OvercommitCheck : DiagnosticCheckBase
{
    public override string CheckId => "L1-KERN-003";
    public override string CheckName => "vm_overcommit";
    public override string Title => "内存过载(overcommit)检查";
    public override int Layer => 1;
    public override string Category => "os_kernel";
    public override int Priority => 22;

    public override async Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext ctx)
    {
        var r = await ctx.ExecAsync("sysctl -n vm.overcommit_memory 2>/dev/null");
        if (!r.Success) return Warning("无法获取overcommit_memory值");

        var val = int.TryParse(r.Output.Trim(), out var overcommitVal) ? overcommitVal : -1;
        if (val < 0)
            return Warning($"无法解析overcommit_memory值: {r.Output}");
        if (val != 2)
            return Info($"vm.overcommit_memory={val}。PG建议设置为2配合overcommit_ratio，避免OOM Killer误杀PG进程",
                new DiagnosticSuggestion { Action = "set_overcommit", Commands = new() { "sysctl -w vm.overcommit_memory=2", "echo 'vm.overcommit_memory=2' >> /etc/sysctl.conf" }, Risk = "低" });

        return Ok($"vm.overcommit_memory={val} (推荐值)");
    }
}
