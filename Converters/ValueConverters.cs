using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ReportManagement.Converters;

/// <summary>
/// bool → Visibility 変換（true = Visible, false = Collapsed）
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// bool → Visibility 逆変換（true = Collapsed, false = Visible）
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>
/// bool → 打ち消し線用のテキストデコレーション変換（ToDoの完了表示）
/// true = Strikethrough, false = None
/// </summary>
[ValueConversion(typeof(bool), typeof(TextDecorationCollection))]
public class BoolToStrikethroughConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? TextDecorations.Strikethrough : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool → Opacity 変換（true = 0.45 薄く, false = 1.0 通常）
/// ToDoの完了済み行をグレーアウトする際に使用。
/// </summary>
[ValueConversion(typeof(bool), typeof(double))]
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 0.4 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// null または空文字 → Visibility 変換
/// 文字列が空なら Collapsed、値があれば Visible
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>true=▲ / false=▼ を返す（展開アイコン用）。</summary>
public class BoolToExpandIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "▲" : "▼";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 日付文字列（YYYY-MM-DD）を「5月22日（木）」形式に変換する
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class DateStringToJpConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string dateStr && DateTime.TryParse(dateStr, out var dt))
        {
            return dt.ToString("M月d日（ddd）", new CultureInfo("ja-JP"));
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// IsHoliday (bool) → Foreground Brush 変換。
/// true = BrushAccentRed（赤文字）, false = BrushTextPrimary（白文字）
/// 明示的に両方のブラシを返すことで継承の不確定性を排除する。
/// </summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public class HolidayBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return Application.Current.TryFindResource("BrushAccentRed") as Brush
                   ?? new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        return Application.Current.TryFindResource("BrushTextPrimary") as Brush
               ?? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool 逆変換（true → false, false → true）
/// IsEnabled 等の bool プロパティで使用
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
}

/// <summary>
/// 空文字列 → null 変換（ToolTipが空で表示されないようにする）
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public class StringEmptyToNullConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? null : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
