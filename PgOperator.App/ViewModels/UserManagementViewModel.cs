using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class UserManagementViewModel : ObservableObject
{
    private readonly ISshService _sshService;
    private ServerConnection? _server;
    private PgInstance? _pgInstance;

    [ObservableProperty] private ObservableCollection<PgRole> _roles = new();
    [ObservableProperty] private PgRole? _selectedRole;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private bool _isLoading;

    // Edit selected user fields
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editPassword = string.Empty;
    [ObservableProperty] private bool _editLogin = true;
    [ObservableProperty] private bool _editSuperuser;
    [ObservableProperty] private bool _editCreateDb;
    [ObservableProperty] private bool _editCreateRole;
    [ObservableProperty] private bool _editReplication;
    [ObservableProperty] private bool _editBypassRls;
    [ObservableProperty] private string _editValidUntil = string.Empty;

    // New role
    [ObservableProperty] private string _newRoleName = string.Empty;
    [ObservableProperty] private string _newRolePassword = string.Empty;
    [ObservableProperty] private bool _isLoginRole = true;
    [ObservableProperty] private bool _isSuperuser;
    [ObservableProperty] private bool _canCreateDb;
    [ObservableProperty] private bool _canCreateRole;
    [ObservableProperty] private bool _canReplication;
    [ObservableProperty] private bool _canBypassRls;

    public UserManagementViewModel(ISshService sshService) { _sshService = sshService; }
    public void SetContext(ServerConnection server, PgInstance pgInstance) { _server = server; _pgInstance = pgInstance; }

    public void SelectRole(PgRole role)
    {
        SelectedRole = role;
        EditName = role.Name;
        EditLogin = role.CanLogin;
        EditSuperuser = role.IsSuperuser;
        EditCreateDb = role.CanCreateDb;
        EditCreateRole = role.CanCreateRole;
        EditReplication = role.CanReplication;
        EditBypassRls = role.CanBypassRls;
        EditValidUntil = role.ValidUntil ?? "";
        EditPassword = "";
    }

    partial void OnSelectedRoleChanged(PgRole? value)
    {
        if (value == null) return;
        EditName = value.Name;
        EditLogin = value.CanLogin;
        EditSuperuser = value.IsSuperuser;
        EditCreateDb = value.CanCreateDb;
        EditCreateRole = value.CanCreateRole;
        EditReplication = value.CanReplication;
        EditBypassRls = value.CanBypassRls;
        EditValidUntil = value.ValidUntil ?? "";
        EditPassword = "";
    }

    [RelayCommand]
    private async Task LoadRolesAsync()
    {
        if (_server == null || _pgInstance == null) return;
        IsLoading = true;
        try
        {
            var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance,
                "SELECT rolname, rolsuper::text, rolcreaterole::text, rolcreatedb::text, " +
                "rolcanlogin::text, rolreplication::text, rolbypassrls::text, " +
                "rolvaliduntil::text FROM pg_roles ORDER BY rolname;");
            if (result.Success)
            {
                var roles = new ObservableCollection<PgRole>();
                foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 7) continue;
                    roles.Add(new PgRole
                    {
                        Name = parts[0].Trim(), IsSuperuser = parts[1].Trim() == "true",
                        CanCreateRole = parts[2].Trim() == "true", CanCreateDb = parts[3].Trim() == "true",
                        CanLogin = parts[4].Trim() == "true", CanReplication = parts[5].Trim() == "true",
                        CanBypassRls = parts[6].Trim() == "true",
                        ValidUntil = parts.Length > 7 ? parts[7].Trim() : null
                    });
                }
                Roles = roles;
                StatusMessage = $"已加载 {Roles.Count} 个角色/用户";
            }
        }
        catch (Exception ex) { StatusMessage = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (_server == null || _pgInstance == null || SelectedRole == null) return;
        if (string.IsNullOrWhiteSpace(EditPassword)) { StatusMessage = "新密码不能为空"; return; }

        var sql = $"ALTER ROLE \"{SelectedRole.Name}\" PASSWORD '{EditPassword.Replace("'", "''")}';";
        var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, sql);
        StatusMessage = result.Success ? $"用户 {SelectedRole.Name} 密码已修改" : $"修改失败: {result.Error}";
        if (result.Success) EditPassword = "";
    }

    [RelayCommand]
    private async Task UpdatePrivilegesAsync()
    {
        if (_server == null || _pgInstance == null || SelectedRole == null) return;

        var flags = new List<string>();
        if (EditLogin) flags.Add("LOGIN"); else flags.Add("NOLOGIN");
        if (EditSuperuser) flags.Add("SUPERUSER"); else flags.Add("NOSUPERUSER");
        if (EditCreateDb) flags.Add("CREATEDB"); else flags.Add("NOCREATEDB");
        if (EditCreateRole) flags.Add("CREATEROLE"); else flags.Add("NOCREATEROLE");
        if (EditReplication) flags.Add("REPLICATION"); else flags.Add("NOREPLICATION");
        if (EditBypassRls) flags.Add("BYPASSRLS"); else flags.Add("NOBYPASSRLS");

        var validUntil = !string.IsNullOrWhiteSpace(EditValidUntil)
            ? $"VALID UNTIL '{EditValidUntil}'" : "";

        var sql = $"ALTER ROLE \"{SelectedRole.Name}\" WITH {string.Join(" ", flags)} {validUntil};";
        var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, sql);
        StatusMessage = result.Success ? $"用户 {SelectedRole.Name} 属性已更新" : $"更新失败: {result.Error}";
        if (result.Success) await LoadRolesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task CreateRoleAsync()
    {
        if (_server == null || _pgInstance == null || string.IsNullOrWhiteSpace(NewRoleName)) return;
        var flags = new List<string>();
        if (IsLoginRole) flags.Add("LOGIN"); else flags.Add("NOLOGIN");
        if (IsSuperuser) flags.Add("SUPERUSER"); if (CanCreateDb) flags.Add("CREATEDB");
        if (CanCreateRole) flags.Add("CREATEROLE"); if (CanReplication) flags.Add("REPLICATION");
        if (CanBypassRls) flags.Add("BYPASSRLS");
        var pwd = !string.IsNullOrEmpty(NewRolePassword) && IsLoginRole ? $"PASSWORD '{NewRolePassword.Replace("'", "''")}'" : "";

        var sql = $"CREATE ROLE \"{NewRoleName}\" WITH {string.Join(" ", flags)} {pwd};";
        var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, sql);
        StatusMessage = result.Success ? $"角色 {NewRoleName} 创建成功" : $"创建失败: {result.Error}";
        if (result.Success) { NewRoleName = ""; NewRolePassword = ""; await LoadRolesCommand.ExecuteAsync(null); }
    }

    [RelayCommand]
    private async Task DropRoleAsync(PgRole role)
    {
        if (_server == null || _pgInstance == null || role == null) return;
        var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, $"DROP ROLE IF EXISTS \"{role.Name}\";");
        StatusMessage = result.Success ? $"角色 {role.Name} 已删除" : $"删除失败: {result.Error}";
        if (result.Success) await LoadRolesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task RevokePublicSchemaAsync()
    {
        if (_server == null || _pgInstance == null) return;
        var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, "REVOKE CREATE ON SCHEMA public FROM PUBLIC;");
        StatusMessage = result.Success ? "已撤销public schema的CREATE权限" : $"失败: {result.Error}";
    }
}

public class PgRole
{
    public string Name { get; set; } = string.Empty;
    public bool IsSuperuser { get; set; }
    public bool CanCreateRole { get; set; }
    public bool CanCreateDb { get; set; }
    public bool CanLogin { get; set; }
    public bool CanReplication { get; set; }
    public bool CanBypassRls { get; set; }
    public string? ValidUntil { get; set; }
}
