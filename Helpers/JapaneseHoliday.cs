using System;
using System.Collections.Generic;
using System.Linq;

namespace ReportManagement.Helpers;

/// <summary>
/// 日本の祝日・振替休日・国民の休日を判定するヘルパー。
/// 固定日付・第N週・春分/秋分の近似式・振替休日・国民の休日を自動計算します（令和対応）。
/// </summary>
public static class JapaneseHoliday
{
    /// <summary>指定日の祝日名を取得する。祝日でなければ false。</summary>
    public static bool IsHoliday(DateTime date, out string? name)
    {
        var map = GetHolidays(date.Year);
        return map.TryGetValue(date, out name);
    }

    // ─── キャッシュ ─────────────────────────────────────────────

    private static readonly Dictionary<int, Dictionary<DateTime, string>> _cache = new();

    /// <summary>指定年の全祝日マップを取得する（内部キャッシュ）。</summary>
    public static Dictionary<DateTime, string> GetHolidays(int year)
    {
        if (_cache.TryGetValue(year, out var map)) return map;

        map = BuildHolidays(year);
        _cache[year] = map;
        return map;
    }

    /// <summary>指定年の祝日一覧を組み立てる。</summary>
    private static Dictionary<DateTime, string> BuildHolidays(int year)
    {
        var result = new Dictionary<DateTime, string>();

        // 1. 固定日付の祝日
        AddFixed(result, year, 1, 1, "元日");
        AddFixed(result, year, 2, 11, "建国記念の日");
        AddFixed(result, year, 4, 29, "昭和の日");
        AddFixed(result, year, 5, 3, "憲法記念日");
        AddFixed(result, year, 5, 4, "みどりの日");
        AddFixed(result, year, 5, 5, "こどもの日");
        AddFixed(result, year, 11, 3, "文化の日");
        AddFixed(result, year, 11, 23, "勤労感謝の日");

        // 天皇誕生日（令和：2020年以降は2月23日）
        if (year >= 2020)
            AddFixed(result, year, 2, 23, "天皇誕生日");

        // 山の日（2016年以降）
        if (year >= 2016)
            AddFixed(result, year, 8, 11, "山の日");

        // 2. 第N週の祝日
        AddNthMonday(result, year, 1, 2, "成人の日");
        AddNthMonday(result, year, 7, 3, "海の日");
        AddNthMonday(result, year, 9, 3, "敬老の日");
        AddNthMonday(result, year, 10, 2, "スポーツの日");

        // 3. 春分・秋分の日（近似式: 2000–2099）
        if (year >= 2000 && year <= 2099)
        {
            int shunbun = (int)(20.8431 + 0.242194 * (year - 2000)) - (year - 2000) / 4;
            int shubun = (int)(23.2488 + 0.242194 * (year - 2000)) - (year - 2000) / 4;
            AddFixed(result, year, 3, shunbun, "春分の日");
            AddFixed(result, year, 9, shubun, "秋分の日");
        }

        // 4. 振替休日・国民の休日を追加（収束するまで繰り返し）
        AddDerivedHolidays(result, year);

        return result;
    }

    // ─── 固定日付追加 ──────────────────────────────────────────

    private static void AddFixed(Dictionary<DateTime, string> map, int year, int month, int day, string name)
    {
        try { map[new DateTime(year, month, day)] = name; } catch { }
    }

    // ─── 第N週の月曜日 ──────────────────────────────────────────

    private static DateTime NthMonday(int year, int month, int nth)
    {
        var firstDay = new DateTime(year, month, 1);
        int diff = ((int)DayOfWeek.Monday - (int)firstDay.DayOfWeek + 7) % 7;
        return firstDay.AddDays(diff + 7 * (nth - 1));
    }

    private static void AddNthMonday(Dictionary<DateTime, string> map, int year, int month, int nth, string name)
    {
        map[NthMonday(year, month, nth)] = name;
    }

    // ─── 振替休日・国民の休日 ──────────────────────────────────

    /// <summary>
    /// 振替休日（祝日が日曜日の翌平日）と国民の休日（祝日に挟まれた平日）を追加する。
    /// 振替休日は各日曜祝日に1回のみ、国民の休日は収束するまで繰り返す。
    /// </summary>
    private static void AddDerivedHolidays(Dictionary<DateTime, string> map, int year)
    {
        // 振替休日: 日曜祝日の翌平日を1回だけ追加
        var sundayHolidays = map.Keys.Where(d => d.DayOfWeek == DayOfWeek.Sunday).ToList();
        foreach (var date in sundayHolidays)
        {
            var sub = date.AddDays(1);
            while (sub.Year == year && map.ContainsKey(sub))
                sub = sub.AddDays(1);

            if (sub.Year == year)
                map[sub] = "振替休日";
        }

        // 国民の休日: 祝日に挟まれた平日を収束するまで繰り返し追加
        bool changed;
        do
        {
            changed = false;
            var holidays = map.Keys.OrderBy(d => d).ToList();

            for (int i = 0; i < holidays.Count - 1; i++)
            {
                var current = holidays[i];
                var next = holidays[i + 1];
                var sandwiched = current.AddDays(1);

                if (sandwiched == next) continue;
                if (sandwiched.DayOfWeek == DayOfWeek.Saturday || sandwiched.DayOfWeek == DayOfWeek.Sunday) continue;
                if (sandwiched.Year != year) continue;
                if (map.ContainsKey(sandwiched)) continue;
                if (sandwiched.AddDays(1) != next) continue;

                map[sandwiched] = "国民の休日";
                changed = true;
            }
        }
        while (changed);
    }
}
