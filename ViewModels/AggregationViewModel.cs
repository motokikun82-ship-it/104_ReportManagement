using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ReportManagement.Services;
using SkiaSharp;

namespace ReportManagement.ViewModels;

public enum PeriodMode { Monthly, Yearly, Custom }
public enum ChartTypeMode { Line, StackedBar }
public enum DisplayUnit { Day, Week, Month, Year }

public partial class ItemCheckItem : ObservableObject
{
    [ObservableProperty] private string _itemName = "";
    [ObservableProperty] private bool _isChecked = true;
}

public partial class ItemGroupViewModel : ObservableObject
{
    [ObservableProperty] private string _groupName = "";
    public ObservableCollection<ItemCheckItem> Items { get; } = new();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items) item.IsChecked = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in Items) item.IsChecked = false;
    }
}

public partial class AggregationViewModel : ObservableObject
{
    private readonly AggregationService _agg = new();

    private List<AggregationRow> _rawRows = new();
    private Dictionary<string, string> _periodDisplayNames = new();
    private static readonly string StateFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aggregation_state.json");

    private static readonly SKColor[] Palette =
    [
        new(0x33, 0x66, 0xFF), // blue
        new(0xFF, 0x33, 0x33), // red
        new(0x33, 0xCC, 0x33), // green
        new(0xFF, 0x99, 0x00), // orange
        new(0x99, 0x33, 0xFF), // purple
        new(0x00, 0xCC, 0xCC), // teal
        new(0xFF, 0x66, 0x99), // pink
        new(0x99, 0x99, 0x99), // gray
        new(0x33, 0x99, 0x33), // dark green
        new(0xCC, 0x66, 0x00), // brown
    ];

    public int[] AvailableYears { get; private set; } = [];
    public int[] Months { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
    public string[] MonthNames { get; } =
        ["1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月"];

    [ObservableProperty]
    private PeriodMode _periodMode = PeriodMode.Monthly;

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private int _selectedMonth;

    [ObservableProperty]
    private DateTime _customStart;

    [ObservableProperty]
    private DateTime _customEnd;

    [ObservableProperty]
    private ItemGroupViewModel? _selectedGroup;

    [ObservableProperty]
    private ChartTypeMode _chartType = ChartTypeMode.Line;

    [ObservableProperty]
    private DisplayUnit _displayUnit = DisplayUnit.Month;

    [ObservableProperty]
    private DataView? _aggregationTable;

    [ObservableProperty]
    private object? _series;

    [ObservableProperty]
    private object? _xAxes;

    [ObservableProperty]
    private object? _yAxes;

    [ObservableProperty]
    private string _statusMessage = "";

    public string YearRangeLabel { get; private set; } = "";
    public ObservableCollection<ItemGroupViewModel> Groups { get; } = new();

    public AggregationViewModel()
    {
        var now = DateTime.Now;
        _selectedYear = now.Year;
        _selectedMonth = now.Month;
        _customStart = new DateTime(now.Year, now.Month, 1);
        _customEnd = now;

        AvailableYears = _agg.GetAvailableYears().ToArray();
        if (AvailableYears.Length == 0) AvailableYears = [now.Year];
        UpdateYearRangeLabel();

        if (!AvailableYears.Contains(SelectedYear))
            SelectedYear = AvailableYears.Last();

        LoadItems();
        LoadState();
        _ = RefreshAsync();
    }

    partial void OnPeriodModeChanged(PeriodMode value)
    {
        UpdateDefaultDisplayUnit();
        _ = RefreshAsync();
    }
    partial void OnSelectedYearChanged(int value) => _ = RefreshAsync();
    partial void OnSelectedMonthChanged(int value) => _ = RefreshAsync();
    partial void OnCustomStartChanged(DateTime value)
    {
        UpdateDefaultDisplayUnit();
        _ = RefreshAsync();
    }
    partial void OnCustomEndChanged(DateTime value)
    {
        UpdateDefaultDisplayUnit();
        _ = RefreshAsync();
    }

    public void LoadItems()
    {
        Groups.Clear();

        var grouped = _agg.GetGroupedItems();
        var groupedByGroup = grouped
            .GroupBy(g => g.GroupName)
            .OrderBy(g => g.First().GroupSort)
            .ToList();

        foreach (var grp in groupedByGroup)
        {
            var groupVm = new ItemGroupViewModel { GroupName = grp.Key };
            foreach (var item in grp)
            {
                var checkItem = new ItemCheckItem { ItemName = item.ItemName, IsChecked = true };
                checkItem.PropertyChanged += OnItemCheckedChanged;
                groupVm.Items.Add(checkItem);
            }
            Groups.Add(groupVm);
        }

        var uncategorized = _agg.GetUncategorizedItems();
        if (uncategorized.Count > 0)
        {
            var groupVm = new ItemGroupViewModel { GroupName = "未分類" };
            foreach (var name in uncategorized)
            {
                var checkItem = new ItemCheckItem { ItemName = name, IsChecked = true };
                checkItem.PropertyChanged += OnItemCheckedChanged;
                groupVm.Items.Add(checkItem);
            }
            Groups.Add(groupVm);
        }

        if (Groups.Count > 0)
            SelectedGroup = Groups[0];
    }

    private void OnItemCheckedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemCheckItem.IsChecked))
            RebuildFromCache();
    }

    partial void OnDisplayUnitChanged(DisplayUnit value) => _ = RefreshAsync();
    partial void OnChartTypeChanged(ChartTypeMode value) => RebuildChart();

    private void UpdateDefaultDisplayUnit()
    {
        if (PeriodMode == PeriodMode.Monthly)
        {
            DisplayUnit = DisplayUnit.Month;
            return;
        }
        if (PeriodMode == PeriodMode.Yearly)
        {
            DisplayUnit = DisplayUnit.Year;
            return;
        }
        int days = (CustomEnd - CustomStart).Days;
        if (days <= 31)
            DisplayUnit = DisplayUnit.Day;
        else if (days <= 182)
            DisplayUnit = DisplayUnit.Week;
        else
            DisplayUnit = DisplayUnit.Month;
    }

    private void UpdateYearRangeLabel()
    {
        YearRangeLabel = AvailableYears.Length >= 2
            ? $"{AvailableYears[0]} ～ {AvailableYears[^1]}"
            : $"{AvailableYears[0]}";
        OnPropertyChanged(nameof(YearRangeLabel));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            StatusMessage = "集計中...";

            string start, end;
            switch (PeriodMode)
            {
                case PeriodMode.Monthly:
                    start = $"{SelectedYear:D4}-01-01";
                    end = $"{SelectedYear:D4}-12-31";
                    break;
                case PeriodMode.Yearly:
                    start = $"{AvailableYears[0]:D4}-01-01";
                    end = $"{AvailableYears[^1]:D4}-12-31";
                    break;
                case PeriodMode.Custom:
                    start = CustomStart.ToString("yyyy-MM-dd");
                    end = CustomEnd.ToString("yyyy-MM-dd");
                    break;
                default:
                    return;
            }

            _rawRows = DisplayUnit switch
            {
                DisplayUnit.Year => _agg.GetYearlyAggregation(start, end),
                DisplayUnit.Month => _agg.GetMonthlyAggregation(start, end),
                DisplayUnit.Week => _agg.GetWeeklyAggregation(start, end),
                DisplayUnit.Day => _agg.GetDailyAggregation(start, end),
                _ => _agg.GetMonthlyAggregation(start, end),
            };

            RebuildFromCache();

            StatusMessage = $"集計完了（{_rawRows.Select(r => r.ItemName).Distinct().Count()}項目）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
    }

    private void RebuildFromCache()
    {
        var selectedItems = Groups
            .SelectMany(g => g.Items)
            .Where(i => i.IsChecked)
            .Select(i => i.ItemName)
            .ToHashSet();
        var filteredRows = _rawRows.Where(r => selectedItems.Contains(r.ItemName)).ToList();
        BuildTable(filteredRows);
        RebuildChart();
    }

    private void BuildTable(List<AggregationRow> rows)
    {
        var periods = rows.Select(r => r.Period)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        if (periods.Count == 0)
        {
            AggregationTable = null;
            return;
        }

        var displayPeriods = periods.Select(FormatPeriod).ToList();

        var avgLabel = DisplayUnit switch
        {
            DisplayUnit.Day => "日平均",
            DisplayUnit.Week => "週平均",
            DisplayUnit.Month => "月平均",
            DisplayUnit.Year => "年平均",
            _ => "平均",
        };

        var table = new DataTable();
        table.Columns.Add("項目名", typeof(string));

        var colNames = new List<string>();
        _periodDisplayNames = new Dictionary<string, string>();
        for (int i = 0; i < displayPeriods.Count; i++)
        {
            string safeName = $"Col{i}";
            table.Columns.Add(safeName, typeof(long));
            colNames.Add(safeName);
            _periodDisplayNames[safeName] = displayPeriods[i];
        }
        table.Columns.Add(avgLabel, typeof(double));

        var itemGroups = rows.GroupBy(r => r.ItemName).OrderBy(g => g.Key);

        foreach (var group in itemGroups)
        {
            var row = table.NewRow();
            row["項目名"] = group.Key;
            long total = 0;
            int nonZeroMonths = 0;

            for (int i = 0; i < periods.Count; i++)
            {
                var match = group.FirstOrDefault(r => r.Period == periods[i]);
                long val = match?.TotalCount ?? 0;
                row[colNames[i]] = val;
                total += val;
                if (val > 0) nonZeroMonths++;
            }

            row[avgLabel] = nonZeroMonths > 0 ? Math.Round((double)total / nonZeroMonths, 1) : 0;
            table.Rows.Add(row);
        }

        var totalRow = table.NewRow();
        totalRow["項目名"] = "合計";
        double grandTotal = 0;

        for (int i = 0; i < periods.Count; i++)
        {
            long sum = 0;
            foreach (DataRow r in table.Rows)
            {
                if (r["項目名"] is string name && name != "合計")
                    sum += Convert.ToInt64(r[colNames[i]]);
            }
            totalRow[colNames[i]] = sum;
            grandTotal += sum;
        }

        int itemCount = table.Rows.Count - 1;
        totalRow[avgLabel] = itemCount > 0 && periods.Count > 0
            ? Math.Round(grandTotal / (periods.Count * itemCount), 1)
            : 0;
        table.Rows.Add(totalRow);

        AggregationTable = table.DefaultView;
    }

    private void RebuildChart()
    {
        var table = AggregationTable?.Table;
        if (table == null || table.Rows.Count <= 1)
        {
            Series = null;
            XAxes = null;
            YAxes = null;
            return;
        }

        var avgLabel = DisplayUnit switch
        {
            DisplayUnit.Day => "日平均",
            DisplayUnit.Week => "週平均",
            DisplayUnit.Month => "月平均",
            DisplayUnit.Year => "年平均",
            _ => "平均",
        };
        var periodCols = table.Columns.Cast<DataColumn>()
            .Where(c => c.ColumnName != "項目名" && c.ColumnName != avgLabel)
            .ToList();

        var periodLabels = periodCols
            .Select(c => _periodDisplayNames.TryGetValue(c.ColumnName, out var dn) ? dn : c.ColumnName)
            .ToList();

        var items = table.Rows.Cast<DataRow>()
            .Where(r => r["項目名"] is string name && name != "合計")
            .ToList();

        var seriesList = new List<ISeries>();
        int colorIdx = 0;

        foreach (var row in items)
        {
            var name = (string)row["項目名"];
            var values = periodCols
                .Select(c => (double)Convert.ToInt64(row[c]))
                .ToArray();

            var color = Palette[colorIdx % Palette.Length];

            if (ChartType == ChartTypeMode.Line)
            {
                seriesList.Add(new LineSeries<double>
                {
                    Name = name,
                    Values = values,
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    GeometrySize = 8,
                    GeometryStroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    Fill = null,
                });
            }
            else
            {
                seriesList.Add(new StackedColumnSeries<double>
                {
                    Name = name,
                    Values = values,
                    Fill = new SolidColorPaint(color),
                    Stroke = null,
                });
            }

            colorIdx++;
        }

        Series = seriesList;

        XAxes = new Axis[]
        {
            new Axis
            {
                Labels = periodLabels,
                LabelsRotation = 45,
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(new SKColor(180, 180, 180)),
            }
        };

        YAxes = new Axis[]
        {
            new Axis
            {
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(new SKColor(180, 180, 180)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(60, 60, 60)),
            }
        };
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        var table = AggregationTable?.Table;
        if (table == null) return;

        var sb = new StringBuilder();

        sb.AppendLine(string.Join("\t", table.Columns.Cast<DataColumn>().Select(c => GetOutputHeaderName(c.ColumnName))));

        foreach (DataRow row in table.Rows)
        {
            var values = row.ItemArray.Select(v => v?.ToString() ?? "");
            sb.AppendLine(string.Join("\t", values));
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            StatusMessage = "クリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"コピー失敗: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var table = AggregationTable?.Table;
        if (table == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "CSVエクスポート",
            Filter = "CSVファイル (*.csv)|*.csv",
            FileName = $"集計_{DateTime.Now:yyyyMMdd}.csv",
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Join(",", table.Columns.Cast<DataColumn>()
                .Select(c => $"\"{GetOutputHeaderName(c.ColumnName)}\"")));

            foreach (DataRow row in table.Rows)
            {
                var values = row.ItemArray.Select(v => $"\"{v?.ToString() ?? ""}\"");
                sb.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            StatusMessage = $"CSVを保存しました: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV保存失敗: {ex.Message}";
        }
    }

    public void SaveState()
    {
        try
        {
            var state = new AggregationState
            {
                ItemStates = Groups.SelectMany(g => g.Items)
                    .ToDictionary(i => i.ItemName, i => i.IsChecked)
            };
            var json = JsonSerializer.Serialize(state);
            File.WriteAllText(StateFilePath, json);
        }
        catch { }
    }

    public void LoadState()
    {
        if (!File.Exists(StateFilePath)) return;
        try
        {
            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<AggregationState>(json);
            if (state?.ItemStates == null) return;

            var allItems = Groups.SelectMany(g => g.Items).ToList();
            foreach (var kv in state.ItemStates)
            {
                var match = allItems.FirstOrDefault(i => i.ItemName == kv.Key);
                if (match != null)
                    match.IsChecked = kv.Value;
            }
        }
        catch { }
    }

    public string? GetPeriodDisplayName(string safeName)
        => _periodDisplayNames.TryGetValue(safeName, out var dn) ? dn : null;

    private string GetOutputHeaderName(string columnName)
        => _periodDisplayNames.TryGetValue(columnName, out var dn) ? dn : columnName;

    private static string FormatPeriod(string period)
    {
        // Month: "2026-01" → "2026/01"
        if (period.Length == 7 && period[4] == '-')
            return period.Replace("-", "/");
        // Day or week Monday: "2026-07-28" → "07/28"
        if (period.Length == 10 && DateTime.TryParse(period, out var dt))
            return dt.ToString("MM/dd");
        return period;
    }
}

public class AggregationState
{
    public Dictionary<string, bool> ItemStates { get; set; } = new();
}
