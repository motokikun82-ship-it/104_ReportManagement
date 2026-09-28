using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ReportManagement.Services;

public class AggregationService
{
    private SqliteConnection OpenConnection()
    {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report.db");
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public List<AggregationRow> GetMonthlyAggregation(string startDate, string endDate)
    {
        return QueryAggregation(
            "SELECT ItemName, strftime('%Y-%m', EntryDate) AS Period, SUM(Count) AS TotalCount " +
            "FROM DailyEntries " +
            "WHERE EntryDate >= @Start AND EntryDate <= @End AND IsCountable = 1 AND (ShowCheck = 0 OR IsExecuted = 1) " +
            "GROUP BY ItemName, Period " +
            "ORDER BY ItemName, Period",
            startDate, endDate);
    }

    public List<AggregationRow> GetYearlyAggregation(string startDate, string endDate)
    {
        return QueryAggregation(
            "SELECT ItemName, strftime('%Y', EntryDate) AS Period, SUM(Count) AS TotalCount " +
            "FROM DailyEntries " +
            "WHERE EntryDate >= @Start AND EntryDate <= @End AND IsCountable = 1 AND (ShowCheck = 0 OR IsExecuted = 1) " +
            "GROUP BY ItemName, Period " +
            "ORDER BY ItemName, Period",
            startDate, endDate);
    }

    public List<AggregationRow> GetDailyAggregation(string startDate, string endDate)
    {
        return QueryAggregation(
            "SELECT ItemName, EntryDate AS Period, SUM(Count) AS TotalCount " +
            "FROM DailyEntries " +
            "WHERE EntryDate >= @Start AND EntryDate <= @End AND IsCountable = 1 AND (ShowCheck = 0 OR IsExecuted = 1) " +
            "GROUP BY ItemName, Period " +
            "ORDER BY ItemName, Period",
            startDate, endDate);
    }

    public List<AggregationRow> GetWeeklyAggregation(string startDate, string endDate)
    {
        return QueryAggregation(
            "SELECT ItemName, " +
            "  DATE(EntryDate, '-' || CAST((CAST(strftime('%w', EntryDate) AS INTEGER) + 6) % 7 AS TEXT) || ' days') AS Period, " +
            "  SUM(Count) AS TotalCount " +
            "FROM DailyEntries " +
            "WHERE EntryDate >= @Start AND EntryDate <= @End AND IsCountable = 1 AND (ShowCheck = 0 OR IsExecuted = 1) " +
            "GROUP BY ItemName, Period " +
            "ORDER BY ItemName, Period",
            startDate, endDate);
    }

    public List<string> GetAllItemNames()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT ItemName FROM DailyEntries WHERE IsCountable = 1 ORDER BY ItemName";
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public List<GroupedItem> GetGroupedItems()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT ti.ItemName, tg.GroupName, tg.SortOrder AS GroupSort, ti.SortOrder AS ItemSort " +
            "FROM TemplateItems ti " +
            "JOIN TemplateGroups tg ON tg.Id = ti.GroupId " +
            "WHERE ti.IsCountable = 1 " +
            "  AND ti.ItemName IN (SELECT DISTINCT ItemName FROM DailyEntries WHERE IsCountable = 1) " +
            "ORDER BY tg.SortOrder, ti.SortOrder";
        using var reader = cmd.ExecuteReader();
        var items = new List<GroupedItem>();
        while (reader.Read())
            items.Add(new GroupedItem(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3)));
        return items;
    }

    public List<string> GetUncategorizedItems()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT DISTINCT ItemName FROM DailyEntries " +
            "WHERE IsCountable = 1 " +
            "  AND ItemName NOT IN (SELECT ItemName FROM TemplateItems) " +
            "ORDER BY ItemName";
        using var reader = cmd.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public List<int> GetAvailableYears()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(CAST(strftime('%Y', EntryDate) AS INTEGER)), " +
                          "MAX(CAST(strftime('%Y', EntryDate) AS INTEGER)) FROM DailyEntries";
        using var reader = cmd.ExecuteReader();
        if (reader.Read() && !reader.IsDBNull(0))
        {
            int min = reader.GetInt32(0);
            int max = reader.GetInt32(1);
            var years = new List<int>();
            for (int y = min; y <= max; y++) years.Add(y);
            return years;
        }
        return new List<int> { DateTime.Now.Year };
    }

    private List<AggregationRow> QueryAggregation(string sql, string startDate, string endDate)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", startDate);
        cmd.Parameters.AddWithValue("@End", endDate);
        using var reader = cmd.ExecuteReader();
        var rows = new List<AggregationRow>();
        while (reader.Read())
            rows.Add(new AggregationRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2)));
        return rows;
    }
}

public record AggregationRow(string ItemName, string Period, long TotalCount);
public record GroupedItem(string ItemName, string GroupName, int GroupSort, int ItemSort);
