using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class ServerListViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private readonly ISshService _sshService;

    [ObservableProperty]
    private ObservableCollection<ServerConnection> _servers = new();

    [ObservableProperty]
    private ServerConnection? _selectedServer;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ServerListViewModel(IDatabaseService databaseService, ISshService sshService)
    {
        _databaseService = databaseService;
        _sshService = sshService;
    }

    [RelayCommand]
    private async Task LoadServersAsync()
    {
        IsLoading = true;
        try
        {
            var servers = await _databaseService.GetAllServersAsync();
            Servers = new ObservableCollection<ServerConnection>(servers);
            StatusMessage = Servers.Count > 0
                ? $"已加载 {Servers.Count} 台服务器"
                : "暂无服务器，点击 + 添加";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // [REVIEW-FIX] 添加 [RelayCommand] 特性，使 XAML 中的 DeleteServerCommand 绑定生效
    [RelayCommand]
    private async Task DeleteServerAsync(ServerConnection server)
    {
        if (server == null) return;
        try
        {
            await _databaseService.DeleteServerAsync(server.Id);
            Servers.Remove(server);
            StatusMessage = $"已删除服务器: {server.Name}";
        }
        catch (Exception ex) { StatusMessage = $"删除失败: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task TestConnectionAsync(ServerConnection server)
    {
        if (server == null) return;
        try
        {
            StatusMessage = $"正在测试 {server.Name} 连接...";
            var result = await _sshService.TestConnectionAsync(server);

        if (result.Success)
        {
            server.IsAvailable = true;
            server.OsInfo = result.Output.Split('\n').LastOrDefault()?.Trim() ?? "Connected";
            server.LastConnectedAt = DateTime.UtcNow;
            await _databaseService.SaveServerAsync(server);
            StatusMessage = $"✅ {server.Name} 连接成功 ({result.Duration.TotalSeconds:F1}s)";
        }
        else
        {
            server.IsAvailable = false;
            StatusMessage = $"❌ {server.Name} 连接失败: {result.Error}";
        }
        }
        catch (Exception ex) { StatusMessage = $"连接测试异常: {ex.Message}"; }
    }
}
