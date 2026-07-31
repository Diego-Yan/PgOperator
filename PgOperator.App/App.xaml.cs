using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PgOperator.App.ViewModels;
using PgOperator.App.Views;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Services;
using PgOperator.Infra.Ssh;
using PgOperator.Infra.Storage;
using PgOperator.Diagnostics;
using PgOperator.AI;
using Serilog;

namespace PgOperator.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // [REVIEW-FIX] 保存 ServiceProvider 引用，在退出时正确释放 Singleton 资源（如 SSH 连接）
    private static ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PgOperator", "logs", "pgoperator-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var services = new ServiceCollection();

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PgOperator", "data", "pgoperator.db");
        var connectionString = $"Data Source={dbPath}";

        // Infrastructure
        var db = new DatabaseService(connectionString);
        // [REVIEW-FIX] 保留 try-catch 保护，但回退为同步调用避免 async void OnStartup 导致
        // WPF 在 Services 尚未初始化时就创建 MainWindow 的竞态问题
        try
        {
            db.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "数据库初始化失败，程序将退出");
            MessageBox.Show($"数据库初始化失败:\n{ex.Message}", "启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        services.AddSingleton<IDatabaseService>(db);
        services.AddSingleton<ISshService, SshService>();
        services.AddSingleton<DiagnosticEngine>();
        services.AddSingleton<AiAnalysisService>();
        services.AddSingleton<BackupService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<ServerListViewModel>();
        services.AddTransient<AddServerViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<SqlQueryViewModel>();
        services.AddTransient<UserManagementViewModel>();
        services.AddTransient<ConfigManagementViewModel>();
        services.AddTransient<DiagnoseViewModel>();
        services.AddTransient<BackupViewModel>();
        services.AddTransient<ReplicationViewModel>();
        services.AddTransient<MaintenanceViewModel>();
        services.AddTransient<ObjectBrowserViewModel>();
        services.AddTransient<ImportExportViewModel>();
        services.AddTransient<AlertViewModel>();
        services.AddTransient<TaskSchedulerViewModel>();
        services.AddTransient<AiSettingsViewModel>();
        services.AddTransient<DeployViewModel>();

        // Views
        services.AddTransient<ServerListView>();
        services.AddTransient<DashboardView>();
        services.AddTransient<SqlQueryView>();
        services.AddTransient<UserManagementView>();
        services.AddTransient<ConfigManagementView>();
        services.AddTransient<DiagnoseView>();
        services.AddTransient<BackupView>();
        services.AddTransient<ReplicationView>();
        services.AddTransient<MaintenanceView>();
        services.AddTransient<ObjectBrowserView>();
        services.AddTransient<ImportExportView>();
        services.AddTransient<AiSettingsView>();
        services.AddTransient<DeployView>();

        Services = services.BuildServiceProvider();
        _serviceProvider = (ServiceProvider)Services;
        Log.Information("PgOperator started");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("PgOperator shutting down");
        // [REVIEW-FIX] 释放 DI 容器，确保 SshService 等 IDisposable Singleton 的连接被正确关闭
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
