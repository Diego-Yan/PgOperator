using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PgOperator.App.ViewModels;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Services;
using PgOperator.Infra.Ssh;
using PgOperator.Infra.Storage;
using PgOperator.Diagnostics;
using PgOperator.AI;
using Serilog;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace PgOperator.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
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
        try
        {
            db.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "数据库初始化失败，程序将退出");
            var msgBox = MessageBoxManager.GetMessageBoxStandard("启动错误",
                $"数据库初始化失败:\n{ex.Message}",
                ButtonEnum.Ok, Icon.Error);
            msgBox.ShowAsync().GetAwaiter().GetResult();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(1);
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

        // Views — registered in Phase 2 as each view is converted
        // services.AddTransient<ServerListView>(); ...etc

        Services = services.BuildServiceProvider();
        _serviceProvider = (ServiceProvider)Services;
        Log.Information("PgOperator started");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop2)
        {
            desktop2.Exit += (_, _) =>
            {
                Log.Information("PgOperator shutting down");
                _serviceProvider?.Dispose();
                Log.CloseAndFlush();
            };

            var mainVm = Services.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow { DataContext = mainVm };
            // Navigation set in Phase 2 after views are converted
            desktop2.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
