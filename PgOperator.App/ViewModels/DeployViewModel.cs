using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class DeployViewModel : ObservableObject
{
    private readonly ISshService _ssh;
    private readonly IDatabaseService _db;
    private ServerConnection? _server;

    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _pgVersion = "16";
    [ObservableProperty] private int _port = 5432;
    [ObservableProperty] private string _pgPassword = "postgres";
    [ObservableProperty] private string _listenAddress = "*";
    [ObservableProperty] private string _sudoPassword = string.Empty;

    public List<string> PgVersions { get; } = new() { "17", "16", "15", "14" };

    public DeployViewModel(ISshService ssh, IDatabaseService db) { _ssh = ssh; _db = db; }
    public void SetContext(ServerConnection s, PgInstance? _) => _server = s;

    private string Esc(string s) => s.Replace("'", "'\\''");
    private string Sudo(string cmd) =>
        string.IsNullOrEmpty(SudoPassword)
            ? $"sudo {cmd} 2>&1"
            : $"echo '{Esc(SudoPassword)}' | sudo -S sh -c '{Esc(cmd)}' 2>&1";
    // SudoPipe: whole pipeline runs inside sh -c (for curl|gpg etc.)
    private string SudoPipe(string pipeline) =>
        string.IsNullOrEmpty(SudoPassword)
            ? $"sudo sh -c '{Esc(pipeline)}' 2>&1"
            : $"echo '{Esc(SudoPassword)}' | sudo -S sh -c '{Esc(pipeline)}' 2>&1";

    private string SudoSu(string cmd) =>
        string.IsNullOrEmpty(SudoPassword)
            ? $"sudo -u postgres {cmd} 2>&1"
            : $"echo '{Esc(SudoPassword)}' | sudo -S -u postgres sh -c '{Esc(cmd)}' 2>&1";

    [RelayCommand]
    private async Task CheckEnvAsync()
    {
        if (_server == null) { Status = "错误: 未连接服务器"; return; }
        IsRunning = true; Status = "环境检测中..."; Output = "";
        var sb = new System.Text.StringBuilder();
        try
        {
            async Task Append(string title, string cmd, Func<string, bool>? isBad = null)
            {
                sb.AppendLine($"## {title}");
                sb.AppendLine($"$ {cmd}");
                var r = await _ssh.ExecuteCommandAsync(_server, cmd);
                var output = r.Output.Trim();
                sb.AppendLine(output);
                var bad = !r.Success || string.IsNullOrEmpty(output) || (isBad?.Invoke(output) ?? false);
                sb.AppendLine(bad ? "→ ⚠️ 注意" : "→ ✅ OK");
                sb.AppendLine();
            }

            await Append("操作系统", "lsb_release -d 2>/dev/null || cat /etc/os-release | head -2");
            await Append("CPU架构", "nproc && uname -m");
            await Append("内存", "free -h | awk 'NR==2{print $2}'");
            await Append("磁盘 /", "df -h / | tail -1 | awk '{print \"可用:\"$4\" 使用率:\"$5}'");
            await Append($"端口{Port}检查", $"ss -tlnp | grep :{Port} || echo '端口{Port}空闲'",
                isBad: o => o.Contains("LISTEN"));
            await Append("外网连通", "curl -sI --max-time 5 https://apt.postgresql.org 2>&1 | head -1 || echo '无法访问'",
                isBad: o => string.IsNullOrEmpty(o) || o.Contains("无法访问") || o.Contains("Could not"));
            await Append("Locale", "locale | grep -E 'LANG|LC_ALL' 2>/dev/null || echo '未设置'");

            // sudo test
            if (!string.IsNullOrEmpty(SudoPassword))
            {
                var sudoTest = $"echo '{Esc(SudoPassword)}' | sudo -S echo 'SUDO_OK' 2>&1";
                await Append("Sudo权限", sudoTest,
                    isBad: o => !o.Contains("SUDO_OK"));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("## Sudo权限");
                sb.AppendLine("⚠️ 未提供sudo密码");
                sb.AppendLine();
            }

            await Append("已有PG安装", "dpkg -l | grep postgresql | awk '{print $2, $3}' || echo '未安装'",
                isBad: o => o.Contains("未安装"));

            Output = sb.ToString();
            Status = "环境检测完成";
        }
        catch (Exception ex) { Status = $"检测异常: {ex.Message}"; Output += $"\n异常: {ex}"; }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task InstallPgAsync()
    {
        if (_server == null) { Status = "错误: 未连接服务器"; return; }
        if (string.IsNullOrEmpty(SudoPassword)) { Status = "请先输入sudo密码！"; return; }

        IsRunning = true; Status = "安装中..."; Output = "";
        var sb = new System.Text.StringBuilder();
        var failed = false;
        try
        {
            // Pre-fetch OS codename (noble, jammy, etc.) for APT source URL
            var codenameR = await _ssh.ExecuteCommandAsync(_server, "lsb_release -cs 2>/dev/null || echo 'unknown'");
            var codename = codenameR.Success ? codenameR.Output.Trim() : "unknown";

            async Task<bool> Run(string desc, string cmd)
            {
                sb.AppendLine($"## {desc}");
                sb.AppendLine($"$ {cmd}");
                Output = sb.ToString(); Status = desc + "...";

                var r = await _ssh.ExecuteCommandWithProgressAsync(_server, cmd,
                    onOutput: line => { System.Windows.Application.Current.Dispatcher.Invoke(() => { sb.AppendLine(line); Output = sb.ToString(); }); },
                    onError: line => { System.Windows.Application.Current.Dispatcher.Invoke(() => { sb.AppendLine(line); Output = sb.ToString(); }); });

                var sudoFailed = r.Output.Contains("incorrect password") || r.Output.Contains("try again");
                if (r.Success && !sudoFailed)
                { sb.AppendLine("→ OK"); sb.AppendLine(); Output = sb.ToString(); return true; }
                else
                { sb.AppendLine(sudoFailed ? "→ Sudo密码错误!" : "→ 失败!"); sb.AppendLine(); Output = sb.ToString(); return false; }
            }

            // Step 0: Clean up any stale/broken APT source files from previous runs
            await Run("清理旧源文件", Sudo("rm -f /etc/apt/sources.list.d/pgdg.list"));

            // Step 1: Update packages
            if (!await Run("更新软件包列表", Sudo("apt-get update"))) { failed = true; }

            // Step 2: Install prerequisites
            if (!await Run("安装依赖", Sudo("apt-get install -y curl ca-certificates gnupg"))) { failed = true; }

            // Step 3: Add PG repo key (--batch --yes for headless)
            if (!await Run("添加PG官方GPG密钥",
                SudoPipe("curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --batch --yes --dearmor -o /usr/share/keyrings/pgdg.gpg")))
            { failed = true; }

            // Step 4: Add PG repo using pre-fetched codename
            if (!await Run("配置PG官方APT源",
                SudoPipe($"echo \"deb [signed-by=/usr/share/keyrings/pgdg.gpg] http://apt.postgresql.org/pub/repos/apt {codename}-pgdg main\" | tee /etc/apt/sources.list.d/pgdg.list")))
            { failed = true; }

            // Step 5: Update and install PG
            if (!await Run("更新源列表", Sudo("apt-get update"))) { failed = true; }
            if (!await Run($"安装 PostgreSQL {PgVersion}", Sudo($"apt-get install -y postgresql-{PgVersion}"))) { failed = true; }

            // Step 6: Set postgres password
            if (!failed)
            {
                var pwdSql = $"ALTER USER postgres PASSWORD '{PgPassword.Replace("'", "''")}';";
                if (!await Run("设置postgres密码", SudoSu($"psql -c \"{pwdSql}\"")))
                { sb.AppendLine("→ 密码设置失败（可能PG未正常启动）\n"); }
            }

            // Step 7: Configure listen address and pg_hba.conf
            if (!failed)
            {
                await Run("配置监听地址",
                    Sudo($"sed -i \"s/#listen_addresses = 'localhost'/listen_addresses = '{ListenAddress}'/\" /etc/postgresql/{PgVersion}/main/postgresql.conf"));

                // Ensure password auth is allowed for local/remote connections
                await Run("配置认证方式(pg_hba.conf)",
                    Sudo($"sed -i 's/local   all             all                                     peer/local   all             all                                     md5/' /etc/postgresql/{PgVersion}/main/pg_hba.conf"));
                await Run("允许TCP连接认证",
                    Sudo($"sh -c 'echo \"host all all 0.0.0.0/0 md5\" >> /etc/postgresql/{PgVersion}/main/pg_hba.conf'"));
                await Run("允许流复制连接",
                    Sudo($"sh -c 'echo \"host replication all 0.0.0.0/0 md5\" >> /etc/postgresql/{PgVersion}/main/pg_hba.conf'"));
            }

            // Step 8: Restart
            if (!failed)
            {
                if (!await Run("启动PG服务", Sudo("systemctl restart postgresql")))
                { sb.AppendLine("→ 尝试 pg_ctlcluster...\n"); if (!await Run("备选启动", Sudo($"pg_ctlcluster {PgVersion} main start"))) failed = true; }
            }

            // Step 9: Verify
            if (!failed)
            {
                sb.AppendLine("## 验证安装");
                var ver = await _ssh.ExecuteCommandAsync(_server,
                    SudoSu($"psql -t -A -c \"SELECT version();\""));
                if (ver.Success) sb.AppendLine($"✅ {ver.Output.Trim()}");
                else { sb.AppendLine($"❌ 验证失败: {ver.Error}"); failed = true; }
            }

            sb.AppendLine();
            sb.AppendLine("========================================");
            if (!failed)
            {
                sb.AppendLine("✅ PostgreSQL 安装成功！");
                sb.AppendLine($"  主机: {_server?.Host}:{Port}");
                sb.AppendLine($"  版本: {PgVersion}");
                sb.AppendLine($"  管理员: postgres");
                sb.AppendLine($"  密码: {PgPassword}");
                sb.AppendLine($"  数据目录: /var/lib/postgresql/{PgVersion}/main");
                sb.AppendLine($"  sudo -u postgres psql");

                // Auto-save PG instance config
                if (_server != null)
                {
                    var pg = new PgInstance
                    {
                        ServerConnectionId = _server.Id,
                        Name = $"{_server.Name}-PG",
                        Host = _server.Host,
                        Port = Port,
                        Database = "postgres",
                        Username = "postgres",
                        Password = PgPassword
                    };
                    try { await _db.SavePgInstanceAsync(pg); sb.AppendLine("  已自动配置PG连接信息"); }
                    catch { }
                }
            }
            else
                sb.AppendLine("❌ 安装过程中有步骤失败，请查看上方详情");

            Output = sb.ToString();
            Status = failed ? "安装失败！" : $"PG安装完成！用户:postgres 密码:{PgPassword}";
        }
        catch (Exception ex) { Status = $"安装异常: {ex.Message}"; Output += $"\n异常: {ex}"; }
        finally { IsRunning = false; }
    }
}
