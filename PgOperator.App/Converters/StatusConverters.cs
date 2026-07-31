using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PgOperator.App.Converters;

public class BoolToStatusIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "CheckCircle" : "AlertCircle";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush(Color.Parse("#4CAF50"))
            : new SolidColorBrush(Color.Parse("#F44336"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToInverseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

public class BoolToZeroVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int idx && idx == 0;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToBackupTypeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "逻辑备份" : "物理备份";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StatusTextToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string ?? "").ToLowerInvariant();
        if (text.Contains("运行") || text.Contains("ok") || text.Contains("成功") || text.Contains("正常"))
            return new SolidColorBrush(Color.Parse("#43A047"));
        if (text.Contains("不可达") || text.Contains("错误") || text.Contains("失败") || text.Contains("异常") || text.Contains("stop"))
            return new SolidColorBrush(Color.Parse("#E53935"));
        if (text.Contains("警告") || text.Contains("注意") || text.Contains("未配置"))
            return new SolidColorBrush(Color.Parse("#FB8C00"));
        return new SolidColorBrush(Color.Parse("#757575"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SeverityToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() switch
        {
            "critical" => new SolidColorBrush(Color.Parse("#E53935")),
            "warning"  => new SolidColorBrush(Color.Parse("#FB8C00")),
            "info"     => new SolidColorBrush(Color.Parse("#1E88E5")),
            "pass"     => new SolidColorBrush(Color.Parse("#43A047")),
            _          => new SolidColorBrush(Color.Parse("#757575"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SeverityToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() switch
        {
            "critical" => "严重",
            "warning"  => "警告",
            "info"     => "建议",
            "pass"     => "通过",
            _          => severity
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SeverityToDetailVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() != "pass";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SeverityToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() switch
        {
            "critical" => new SolidColorBrush(Color.Parse("#FFEBEE")),
            "warning"  => new SolidColorBrush(Color.Parse("#FFF3E0")),
            "info"     => new SolidColorBrush(Color.Parse("#E3F2FD")),
            "pass"     => new SolidColorBrush(Color.Parse("#E8F5E9")),
            _          => new SolidColorBrush(Colors.White)
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
