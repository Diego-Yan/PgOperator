using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PgOperator.App.Converters;

public class BoolToStatusIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "CheckCircle" : "AlertCircle";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

public class BoolToZeroVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int idx && idx == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToBackupTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "逻辑备份" : "物理备份";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null; // non-null = enabled
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

// [REVIEW-FIX] 新增：状态文本 → 颜色（含"运行中"=绿，"不可达/错误/失败"=红，"未知/未配置"=灰）
public class StatusTextToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = (value as string ?? "").ToLowerInvariant();
        if (text.Contains("运行") || text.Contains("ok") || text.Contains("成功") || text.Contains("正常"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#43A047")); // green
        if (text.Contains("不可达") || text.Contains("错误") || text.Contains("失败") || text.Contains("异常") || text.Contains("stop"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935")); // red
        if (text.Contains("警告") || text.Contains("注意") || text.Contains("未配置"))
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB8C00")); // orange
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#757575")); // gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// [REVIEW-FIX] 新增：字符串非空 → Visible
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// [REVIEW-FIX] 新增：Severity → 颜色转换（pass=绿 / critical=红 / warning=橙 / info=蓝）
public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() switch
        {
            "critical" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935")), // red
            "warning"  => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB8C00")), // orange
            "info"     => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E88E5")), // blue
            "pass"     => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#43A047")), // green
            _          => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#757575"))  // gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// [REVIEW-FIX] 新增：Severity → 中文标签转换
public class SeverityToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// [REVIEW-FIX] 新增：Severity → 是否显示明细（非 pass 才显示）
public class SeverityToDetailVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() != "pass"
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// [REVIEW-FIX] 新增：Severity → 浅色背景（用于卡片底色，非 pass 项高亮）
public class SeverityToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var severity = value as string ?? "";
        return severity.ToLowerInvariant() switch
        {
            "critical" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE")), // light red
            "warning"  => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0")), // light orange
            "info"     => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E3F2FD")), // light blue
            "pass"     => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9")), // light green
            _          => new SolidColorBrush(Colors.White)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
