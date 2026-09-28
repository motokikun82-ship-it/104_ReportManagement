using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ReportManagement.Models;

namespace ReportManagement.Services;

/// <summary>
/// SQLiteデータベースへの全CRUD操作を担当するシングルトンサービス。
/// アプリケーション全体から唯一のインスタンスを通じてDBアクセスを行う。
/// データベースファイルは実行ファイルと同じフォルダに report.db として保存される。
/// </summary>
public class DatabaseService
{
    // ─── シングルトン ───────────────────────────────────────────
    private static DatabaseService? _instance;
    public static DatabaseService Instance => _instance ??= new DatabaseService();

    private readonly string _connectionString;

    private DatabaseService()
    {
        // 実行ファイルと同じフォルダにDBを配置
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "report.db");
        _connectionString = $"Data Source={dbPath}";
    }

    // ─── 接続ヘルパー ────────────────────────────────────────────
    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        // WALモードで書き込み速度を向上させる
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    // ═══════════════════════════════════════════════════════════════
    // DB初期化
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// アプリ初回起動時にテーブルを作成する。
    /// 既に存在する場合は何もしない（CREATE TABLE IF NOT EXISTS）。
    /// </summary>
    public void InitializeDatabase()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            -- 日報エントリーテーブル
            CREATE TABLE IF NOT EXISTS DailyEntries (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                EntryDate   TEXT    NOT NULL,
                ItemName    TEXT    NOT NULL,
                Count       INTEGER NOT NULL DEFAULT 0,
                IsCountable INTEGER NOT NULL DEFAULT 1,
                Note        TEXT    NOT NULL DEFAULT '',
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                IsExecuted  INTEGER NOT NULL DEFAULT 1,
                ShowCheck   INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL DEFAULT '',
                UpdatedAt   TEXT    NOT NULL DEFAULT ''
            );

            -- テンプレートグループテーブル
            CREATE TABLE IF NOT EXISTS TemplateGroups (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupName TEXT    NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsDefault INTEGER NOT NULL DEFAULT 0
            );

            -- テンプレート項目テーブル
            CREATE TABLE IF NOT EXISTS TemplateItems (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupId      INTEGER NOT NULL,
                ItemName     TEXT    NOT NULL,
                DefaultCount INTEGER NOT NULL DEFAULT 0,
                IsCountable  INTEGER NOT NULL DEFAULT 1,
                SortOrder    INTEGER NOT NULL DEFAULT 0,
                NeedCheck    INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (GroupId) REFERENCES TemplateGroups(Id) ON DELETE CASCADE
            );

            -- ToDoタスクテーブル
            CREATE TABLE IF NOT EXISTS TodoTasks (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskDate    TEXT    NOT NULL,
                TaskName    TEXT    NOT NULL,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL DEFAULT '',
                StartTime   TEXT    NOT NULL DEFAULT '',
                EndTime     TEXT    NOT NULL DEFAULT '',
                Description TEXT    NOT NULL DEFAULT ''
            );

            -- よく使う項目テーブル（個別に呼び出して日報に追加する用）
            CREATE TABLE IF NOT EXISTS FavoriteItems (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemName  TEXT    NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            -- メモテーブル
            CREATE TABLE IF NOT EXISTS Notes (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Title     TEXT    NOT NULL DEFAULT '',
                Content   TEXT    NOT NULL DEFAULT '',
                NoteDate  TEXT    NOT NULL,
                CreatedAt TEXT    NOT NULL DEFAULT '',
                UpdatedAt TEXT    NOT NULL DEFAULT ''
            );

            -- 会社独自祝日テーブル
            CREATE TABLE IF NOT EXISTS CompanyHolidays (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                HolidayDate TEXT    NOT NULL,
                Name        TEXT    NOT NULL,
                IsRecurring INTEGER NOT NULL DEFAULT 1,
                Note        TEXT    NOT NULL DEFAULT '',
                CreatedAt   TEXT    NOT NULL DEFAULT '',
                UpdatedAt   TEXT    NOT NULL DEFAULT ''
            );

            -- アプリ設定（キー・バリュー）
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            -- インデックス（日付での検索を高速化）
            CREATE INDEX IF NOT EXISTS idx_entries_date ON DailyEntries(EntryDate);
            CREATE INDEX IF NOT EXISTS idx_todos_date   ON TodoTasks(TaskDate);
            CREATE INDEX IF NOT EXISTS idx_notes_date   ON Notes(NoteDate);
        ";
        cmd.ExecuteNonQuery();

        // デフォルトテンプレートがなければ初期データを投入
        EnsureDefaultTemplate(conn);

        // 既存DBのToDoタスクに StartTime/EndTime/Description カラムがなければ追加
        ApplyTodoMigration(conn);

        // 既存DBに実行チェック用カラムがなければ追加
        ApplyExecutedMigration(conn);

        // 過去のバグでStartTimeにCreatedAtが入ってしまったデータを修復
        FixCorruptedStartTime(conn);
    }

    private static void ApplyTodoMigration(SqliteConnection conn)
    {
        // ALTER TABLE を試みて "duplicate column name" エラーなら既存と判断して無視する。
        // pragma_table_info のテーブル値関数はWALモードや一部環境で誤動作することがあるため、
        // この try-catch 方式が最も確実なマイグレーション手法。
        foreach (var colDef in new[]
        {
            "StartTime   TEXT NOT NULL DEFAULT ''",
            "EndTime     TEXT NOT NULL DEFAULT ''",
            "Description TEXT NOT NULL DEFAULT ''",
        })
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE TodoTasks ADD COLUMN {colDef};";
                alter.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
            {
                // カラムが既に存在する場合は何もしない
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyTodoMigration エラー: {ex.Message}");
                throw;
            }
        }
    }

    private static void ApplyExecutedMigration(SqliteConnection conn)
    {
        // DailyEntries: IsExecuted（既定1=実行済み扱いで既存集計を維持）、ShowCheck（既定0=非表示）
        // TemplateItems: NeedCheck（既定0=チェック不要）
        foreach (var (table, colDef) in new[]
        {
            ("DailyEntries", "IsExecuted  INTEGER NOT NULL DEFAULT 1"),
            ("DailyEntries", "ShowCheck   INTEGER NOT NULL DEFAULT 0"),
            ("TemplateItems", "NeedCheck   INTEGER NOT NULL DEFAULT 0"),
        })
        {
            try
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {colDef};";
                alter.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
            {
                // カラムが既に存在する場合は何もしない
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyExecutedMigration エラー: {ex.Message}");
                throw;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // CompanyHolidays CRUD
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>全会社祝日を取得する。</summary>
    public List<CompanyHoliday> GetAllCompanyHolidays()
    {
        var list = new List<CompanyHoliday>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, HolidayDate, Name, IsRecurring, Note, CreatedAt, UpdatedAt
            FROM CompanyHolidays
            ORDER BY HolidayDate;
        ";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadCompanyHoliday(reader));
        return list;
    }

    /// <summary>指定日付範囲の会社祝日を取得する。</summary>
    public List<CompanyHoliday> GetCompanyHolidaysByDateRange(string startDate, string endDate)
    {
        var list = new List<CompanyHoliday>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, HolidayDate, Name, IsRecurring, Note, CreatedAt, UpdatedAt
            FROM CompanyHolidays
            WHERE HolidayDate BETWEEN $start AND $end
            ORDER BY HolidayDate;
        ";
        cmd.Parameters.AddWithValue("$start", startDate);
        cmd.Parameters.AddWithValue("$end", endDate);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadCompanyHoliday(reader));
        return list;
    }

    /// <summary>会社祝日を新規作成し、IDを返す。</summary>
    public int InsertCompanyHoliday(CompanyHoliday holiday)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = @"
            INSERT INTO CompanyHolidays (HolidayDate, Name, IsRecurring, Note, CreatedAt, UpdatedAt)
            VALUES ($date, $name, $recur, $note, $now, $now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$date",   holiday.HolidayDate);
        cmd.Parameters.AddWithValue("$name",   holiday.Name);
        cmd.Parameters.AddWithValue("$recur",  holiday.IsRecurring ? 1 : 0);
        cmd.Parameters.AddWithValue("$note",   holiday.Note);
        cmd.Parameters.AddWithValue("$now",    now);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>会社祝日を更新する。</summary>
    public void UpdateCompanyHoliday(CompanyHoliday holiday)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE CompanyHolidays
            SET HolidayDate = $date,
                Name        = $name,
                IsRecurring = $recur,
                Note        = $note,
                UpdatedAt   = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id",    holiday.Id);
        cmd.Parameters.AddWithValue("$date",  holiday.HolidayDate);
        cmd.Parameters.AddWithValue("$name",  holiday.Name);
        cmd.Parameters.AddWithValue("$recur", holiday.IsRecurring ? 1 : 0);
        cmd.Parameters.AddWithValue("$note",  holiday.Note);
        cmd.Parameters.AddWithValue("$now",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>会社祝日を削除する。</summary>
    public void DeleteCompanyHoliday(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CompanyHolidays WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定日付が会社祝日かどうか判定する（繰り返し設定を考慮）。</summary>
    public bool IsCompanyHoliday(DateTime date, IEnumerable<CompanyHoliday> holidays, out string? name)
    {
        name = null;
        foreach (var h in holidays)
        {
            if (DateTime.TryParse(h.HolidayDate, out var hDate))
            {
                bool match = h.IsRecurring
                    ? hDate.Month == date.Month && hDate.Day == date.Day
                    : hDate == date;
                if (!match) continue;

                // 名前が空文字のエントリーは「この日を祝日から除外する」という意味
                if (string.IsNullOrEmpty(h.Name))
                {
                    name = null;
                    return false; // 上書き（祝日ではない）
                }
                name = h.Name;
                return true;
            }
        }
        return false;
    }

    /// <summary>指定日付に祝日上書き（空名エントリー）があるか確認する。</summary>
    public bool HasHolidayOverride(DateTime date, IEnumerable<CompanyHoliday> holidays)
    {
        foreach (var h in holidays)
        {
            if (DateTime.TryParse(h.HolidayDate, out var hDate))
            {
                bool match = h.IsRecurring
                    ? hDate.Month == date.Month && hDate.Day == date.Day
                    : hDate == date;
                if (match && string.IsNullOrEmpty(h.Name))
                    return true;
            }
        }
        return false;
    }

    private static CompanyHoliday ReadCompanyHoliday(SqliteDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        HolidayDate = r.GetString(1),
        Name        = r.GetString(2),
        IsRecurring = r.GetInt32(3) == 1,
        Note        = r.GetString(4),
        CreatedAt   = r.GetString(5),
        UpdatedAt   = r.GetString(6),
    };

    /// <summary>StartTimeに誤ってCreatedAtが入ったデータをクリアする。</summary>
    private static void FixCorruptedStartTime(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE TodoTasks
            SET StartTime = ''
            WHERE StartTime LIKE '____-__-__ __:__:__'
               OR StartTime LIKE '____/__/__ __:__:__';
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// テンプレートが1件もない場合、サンプルのデフォルトテンプレートを作成する。
    /// </summary>
    private void EnsureDefaultTemplate(SqliteConnection conn)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM TemplateGroups;";
        long count = (long)(countCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        // デフォルトグループを作成
        using var grpCmd = conn.CreateCommand();
        grpCmd.CommandText = @"
            INSERT INTO TemplateGroups (GroupName, SortOrder, IsDefault)
            VALUES ('通常業務', 0, 1);
        ";
        grpCmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid();";
        long groupId = (long)(idCmd.ExecuteScalar() ?? 0L);

        // サンプル項目を追加
        var items = new[] { "商品登録", "受注処理", "問い合わせ対応", "返品・交換処理" };
        for (int i = 0; i < items.Length; i++)
        {
            using var itemCmd = conn.CreateCommand();
            itemCmd.CommandText = @"
                INSERT INTO TemplateItems (GroupId, ItemName, DefaultCount, IsCountable, SortOrder)
                VALUES ($gid, $name, 0, 1, $sort);
            ";
            itemCmd.Parameters.AddWithValue("$gid",  groupId);
            itemCmd.Parameters.AddWithValue("$name", items[i]);
            itemCmd.Parameters.AddWithValue("$sort", i * 10);
            itemCmd.ExecuteNonQuery();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DailyEntries CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>指定日付の日報エントリー一覧を表示順に取得する。</summary>
    public List<DailyEntry> GetEntriesByDate(string date)
    {
        var list = new List<DailyEntry>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EntryDate, ItemName, Count, IsCountable, Note, SortOrder, IsExecuted, ShowCheck, CreatedAt, UpdatedAt
            FROM DailyEntries
            WHERE EntryDate = $date
            ORDER BY SortOrder, Id;
        ";
        cmd.Parameters.AddWithValue("$date", date);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadDailyEntry(reader));
        }
        return list;
    }

    /// <summary>指定日付の最小SortOrderを取得する（エントリーがない場合はnull）。</summary>
    /// <param name="date">対象日付</param>
    /// <param name="isCountable">true=件数ありグループ内の最小値、false=メモグループ内の最小値、null=全体</param>
    public int? GetMinSortOrderForDate(string date, bool? isCountable = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (isCountable.HasValue)
        {
            cmd.CommandText = "SELECT MIN(SortOrder) FROM DailyEntries WHERE EntryDate = $date AND IsCountable = $cnt;";
            cmd.Parameters.AddWithValue("$cnt", isCountable.Value ? 1 : 0);
        }
        else
        {
            cmd.CommandText = "SELECT MIN(SortOrder) FROM DailyEntries WHERE EntryDate = $date;";
        }
        cmd.Parameters.AddWithValue("$date", date);
        var val = cmd.ExecuteScalar();
        if (val == null || val == DBNull.Value) return null;
        return Convert.ToInt32(val);
    }

    /// <summary>指定日付の最大SortOrderを取得する（エントリーがない場合は0）。</summary>
    /// <param name="date">対象日付</param>
    /// <param name="isCountable">true=件数ありグループ内の最大値、false=メモグループ内の最大値、null=全体</param>
    public int GetMaxSortOrderForDate(string date, bool? isCountable = null)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        if (isCountable.HasValue)
        {
            cmd.CommandText = "SELECT COALESCE(MAX(SortOrder), 0) FROM DailyEntries WHERE EntryDate = $date AND IsCountable = $cnt;";
            cmd.Parameters.AddWithValue("$cnt", isCountable.Value ? 1 : 0);
        }
        else
        {
            cmd.CommandText = "SELECT COALESCE(MAX(SortOrder), 0) FROM DailyEntries WHERE EntryDate = $date;";
        }
        cmd.Parameters.AddWithValue("$date", date);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>全日報エントリーを取得する（エクスポート／インポート用）。</summary>
    public List<DailyEntry> GetAllEntries()
    {
        var list = new List<DailyEntry>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EntryDate, ItemName, Count, IsCountable, Note, SortOrder, IsExecuted, ShowCheck, CreatedAt, UpdatedAt
            FROM DailyEntries
            ORDER BY EntryDate, SortOrder, Id;
        ";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadDailyEntry(reader));
        return list;
    }

    /// <summary>指定日付＋項目名のエントリーが既に存在するか。</summary>
    public bool EntryExists(string date, string itemName)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM DailyEntries WHERE EntryDate = $date AND ItemName = $name";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$name", itemName);
        return (long)(cmd.ExecuteScalar() ?? 0) > 0;
    }

    /// <summary>日付＋項目名でエントリーIDを取得する（存在しなければnull）。</summary>
    public int? GetEntryIdByKey(string date, string itemName)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM DailyEntries WHERE EntryDate = $date AND ItemName = $name LIMIT 1";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$name", itemName);
        var val = cmd.ExecuteScalar();
        return val != null ? (int?)(long)val : null;
    }

    /// <summary>日付＋項目名でエントリーを削除する。</summary>
    public void DeleteEntryByKey(string date, string itemName)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DailyEntries WHERE EntryDate = $date AND ItemName = $name";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$name", itemName);
        cmd.ExecuteNonQuery();
    }

    /// <summary>新しい日報エントリーを挿入し、採番されたIDを返す。</summary>
    public int InsertEntry(DailyEntry entry)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = @"
            INSERT INTO DailyEntries
                (EntryDate, ItemName, Count, IsCountable, Note, SortOrder, IsExecuted, ShowCheck, CreatedAt, UpdatedAt)
            VALUES
                ($date, $name, $count, $cnt, $note, $sort, $exec, $show, $now, $now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$date",  entry.EntryDate);
        cmd.Parameters.AddWithValue("$name",  entry.ItemName);
        cmd.Parameters.AddWithValue("$count", entry.Count);
        cmd.Parameters.AddWithValue("$cnt",   entry.IsCountable ? 1 : 0);
        cmd.Parameters.AddWithValue("$note",  entry.Note);
        cmd.Parameters.AddWithValue("$sort",  entry.SortOrder);
        cmd.Parameters.AddWithValue("$exec",  entry.IsExecuted ? 1 : 0);
        cmd.Parameters.AddWithValue("$show",  entry.ShowCheck ? 1 : 0);
        cmd.Parameters.AddWithValue("$now",   now);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>既存の日報エントリーを更新する（件数・メモの変更に使用）。</summary>
    public void UpdateEntry(DailyEntry entry)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE DailyEntries
            SET ItemName    = $name,
                Count       = $count,
                IsCountable = $cnt,
                Note        = $note,
                SortOrder   = $sort,
                IsExecuted  = $exec,
                ShowCheck   = $show,
                UpdatedAt   = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id",    entry.Id);
        cmd.Parameters.AddWithValue("$name",  entry.ItemName);
        cmd.Parameters.AddWithValue("$count", entry.Count);
        cmd.Parameters.AddWithValue("$cnt",   entry.IsCountable ? 1 : 0);
        cmd.Parameters.AddWithValue("$note",  entry.Note);
        cmd.Parameters.AddWithValue("$sort",  entry.SortOrder);
        cmd.Parameters.AddWithValue("$exec",  entry.IsExecuted ? 1 : 0);
        cmd.Parameters.AddWithValue("$show",  entry.ShowCheck ? 1 : 0);
        cmd.Parameters.AddWithValue("$now",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定IDの日報エントリーを削除する。</summary>
    public void DeleteEntry(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DailyEntries WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// デフォルトテンプレートグループの項目を当日に自動生成する。アプリ起動時に呼び出す。
    /// 既存エントリーがあっても、未登録のテンプレート項目だけを先頭に挿入する（重複スキップ）。
    /// </summary>
    /// <returns>1件以上挿入した場合 true、追加なしの場合 false</returns>
    public bool AutoGenerateFromTemplate(string date)
    {
        using var conn = CreateConnection();

        // デフォルトテンプレートグループを取得
        using var grpCmd = conn.CreateCommand();
        grpCmd.CommandText = @"
            SELECT Id FROM TemplateGroups
            WHERE IsDefault = 1
            ORDER BY SortOrder
            LIMIT 1;
        ";
        object? grpIdObj = grpCmd.ExecuteScalar();
        if (grpIdObj == null) return false;
        long groupId = (long)grpIdObj;

        // そのグループの項目を取得して日報に追加
        using var itemCmd = conn.CreateCommand();
        itemCmd.CommandText = @"
            SELECT ItemName, DefaultCount, IsCountable, SortOrder, NeedCheck
            FROM TemplateItems
            WHERE GroupId = $gid
            ORDER BY SortOrder;
        ";
        itemCmd.Parameters.AddWithValue("$gid", groupId);
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        var templateRows = new List<(string Name, int Count, int Countable, int Sort, int NeedCheck)>();
        using (var reader = itemCmd.ExecuteReader())
        {
            while (reader.Read())
                templateRows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        if (templateRows.Count == 0) return false;

        var existingNames = new HashSet<string>(GetEntriesByDate(date).Select(e => e.ItemName));
        var missing = templateRows.Where(t => !existingNames.Contains(t.Name)).ToList();
        if (missing.Count == 0) return false;

        // 既存が0件なら従来通りテンプレートのSortOrderで挿入
        if (existingNames.Count == 0)
        {
            foreach (var t in missing)
                InsertEntryRow(conn, date, t, t.Sort, now);
            return true;
        }

        // 既存あり：不足分だけ先頭に挿入（件数あり／メモの各グループで既存最小値より前）
        InsertEntriesAtTop(conn, date, missing, now);
        return true;
    }

    /// <summary>
    /// 指定行を当日の先頭に挿入する。件数あり・メモの各グループ内で既存最小値より前に配置し、
    /// テンプレート順を保つ。既存行のSortOrderは変更しない。
    /// </summary>
    private static void InsertEntriesAtTop(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string date,
        List<(string Name, int Count, int Countable, int Sort, int NeedCheck)> rows,
        string now)
    {
        var countable = rows.Where(r => r.Countable == 1).ToList();
        var memos = rows.Where(r => r.Countable != 1).ToList();

        int? minCountable = GetMinSortOrder(conn, date, true);
        int? minMemo = GetMinSortOrder(conn, date, false);

        int next = minCountable.HasValue ? minCountable.Value - countable.Count * 10 : 0;
        foreach (var t in countable)
        {
            InsertEntryRow(conn, date, t, next, now);
            next += 10;
        }

        next = minMemo.HasValue ? minMemo.Value - memos.Count * 10 : 0;
        foreach (var t in memos)
        {
            InsertEntryRow(conn, date, t, next, now);
            next += 10;
        }
    }

    private static int? GetMinSortOrder(Microsoft.Data.Sqlite.SqliteConnection conn, string date, bool countable)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(SortOrder) FROM DailyEntries WHERE EntryDate = $date AND IsCountable = $cnt;";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$cnt", countable ? 1 : 0);
        var val = cmd.ExecuteScalar();
        if (val == null || val == DBNull.Value) return null;
        return Convert.ToInt32(val);
    }

    private static void InsertEntryRow(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string date,
        (string Name, int Count, int Countable, int Sort, int NeedCheck) t,
        int sortOrder,
        string now)
    {
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO DailyEntries
                (EntryDate, ItemName, Count, IsCountable, Note, SortOrder, IsExecuted, ShowCheck, CreatedAt, UpdatedAt)
            VALUES ($date, $name, $count, $cnt, '', $sort, $exec, $show, $now, $now);
        ";
        insertCmd.Parameters.AddWithValue("$date",  date);
        insertCmd.Parameters.AddWithValue("$name",  t.Name);
        insertCmd.Parameters.AddWithValue("$count", t.Count);
        insertCmd.Parameters.AddWithValue("$cnt",   t.Countable);
        insertCmd.Parameters.AddWithValue("$sort",  sortOrder);
        // 要チェック項目は未実行・チェック表示で登録、それ以外は実行済み扱い・非表示
        insertCmd.Parameters.AddWithValue("$exec",  t.NeedCheck == 1 ? 0 : 1);
        insertCmd.Parameters.AddWithValue("$show",  t.NeedCheck);
        insertCmd.Parameters.AddWithValue("$now",   now);
        insertCmd.ExecuteNonQuery();
    }

    // ─── 検索 ────────────────────────────────────────────────────

    /// <summary>指定日付の未実行チェック項目の件数を返す（終了時確認用）。</summary>
    public int GetUnexecutedCount(string date)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM DailyEntries WHERE EntryDate = $date AND ShowCheck = 1 AND IsExecuted = 0;";
        cmd.Parameters.AddWithValue("$date", date);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>指定日付の未実行チェック項目名の一覧を返す（終了時確認用）。</summary>
    public List<string> GetUnexecutedItemNames(string date)
    {
        var list = new List<string>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ItemName FROM DailyEntries WHERE EntryDate = $date AND ShowCheck = 1 AND IsExecuted = 0 ORDER BY SortOrder;";
        cmd.Parameters.AddWithValue("$date", date);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>
    /// 特定の項目名で過去のデータを検索する。
    /// 実施した日付と合計件数の一覧を返す（集計用）。
    /// 未実行の要チェック項目は除外する。
    /// </summary>
    public List<(string Date, string ItemName, int TotalCount)> SearchByItemName(string itemName)
    {
        var result = new List<(string, string, int)>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT EntryDate, ItemName, SUM(Count) as TotalCount
            FROM DailyEntries
            WHERE ItemName LIKE $name
              AND (ShowCheck = 0 OR IsExecuted = 1)
            GROUP BY EntryDate, ItemName
            ORDER BY EntryDate DESC;
        ";
        cmd.Parameters.AddWithValue("$name", $"%{itemName}%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }
        return result;
    }

    /// <summary>
    /// フリーワードでエントリー（項目名・メモ）を横断検索する。
    /// </summary>
    public List<DailyEntry> SearchFreeWord(string keyword)
    {
        var list = new List<DailyEntry>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, EntryDate, ItemName, Count, IsCountable, Note, SortOrder, IsExecuted, ShowCheck, CreatedAt, UpdatedAt
            FROM DailyEntries
            WHERE ItemName LIKE $kw OR Note LIKE $kw
            ORDER BY EntryDate DESC, SortOrder;
        ";
        cmd.Parameters.AddWithValue("$kw", $"%{keyword}%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadDailyEntry(reader));
        }
        return list;
    }

    /// <summary>
    /// 指定年月において日報が1件以上存在する日付の一覧を返す（カレンダーのマーカー表示用）。
    /// </summary>
    public HashSet<int> GetDaysWithEntries(int year, int month)
    {
        var days = new HashSet<int>();
        string prefix = $"{year:D4}-{month:D2}-%";
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT EntryDate
            FROM DailyEntries
            WHERE EntryDate LIKE $prefix;
        ";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string dateStr = reader.GetString(0);
            if (DateTime.TryParse(dateStr, out var dt))
                days.Add(dt.Day);
        }
        return days;
    }

    // ─── ヘルパー ────────────────────────────────────────────────
    private static DailyEntry ReadDailyEntry(SqliteDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        EntryDate   = r.GetString(1),
        ItemName    = r.GetString(2),
        Count       = r.GetInt32(3),
        IsCountable = r.GetInt32(4) == 1,
        Note        = r.GetString(5),
        SortOrder   = r.GetInt32(6),
        IsExecuted  = r.GetInt32(7) == 1,
        ShowCheck   = r.GetInt32(8) == 1,
        CreatedAt   = r.GetString(9),
        UpdatedAt   = r.GetString(10),
    };

    /// <summary>
    /// 過去の日報エントリーの項目名一覧を使用回数順に取得する。
    /// excludeDate を指定すると、その日付に既に登録されている項目を除外する。
    /// </summary>
    public List<EntryHistoryItem> GetEntryHistory(int limit = 50, string? excludeDate = null)
    {
        var list = new List<EntryHistoryItem>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();

        if (excludeDate == null)
        {
            cmd.CommandText = @"
                SELECT ItemName, COUNT(*) AS TimesUsed, MAX(EntryDate) AS LastUsed
                FROM DailyEntries
                GROUP BY ItemName
                ORDER BY TimesUsed DESC
                LIMIT $limit;
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT ItemName, COUNT(*) AS TimesUsed, MAX(EntryDate) AS LastUsed
                FROM DailyEntries
                WHERE ItemName NOT IN (
                    SELECT ItemName FROM DailyEntries WHERE EntryDate = $excludeDate
                )
                GROUP BY ItemName
                ORDER BY TimesUsed DESC
                LIMIT $limit;
            ";
            cmd.Parameters.AddWithValue("$excludeDate", excludeDate);
        }

        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new EntryHistoryItem
            {
                ItemName = reader.GetString(0),
                TimesUsed = reader.GetInt32(1),
                LastUsed = reader.GetString(2),
            });
        }
        return list;
    }

    // ═══════════════════════════════════════════════════════════════
    // TemplateGroups CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>全テンプレートグループを項目付きで取得する。</summary>
    public List<TemplateGroup> GetAllTemplateGroups()
    {
        var groups = new List<TemplateGroup>();
        using var conn = CreateConnection();

        using var grpCmd = conn.CreateCommand();
        grpCmd.CommandText = "SELECT Id, GroupName, SortOrder, IsDefault FROM TemplateGroups ORDER BY SortOrder;";
        using var grpReader = grpCmd.ExecuteReader();
        while (grpReader.Read())
        {
            groups.Add(new TemplateGroup
            {
                Id        = grpReader.GetInt32(0),
                GroupName = grpReader.GetString(1),
                SortOrder = grpReader.GetInt32(2),
                IsDefault = grpReader.GetInt32(3) == 1,
            });
        }

        // 各グループの項目を取得
        foreach (var grp in groups)
        {
            grp.Items = GetItemsByGroupId(grp.Id, conn);
        }
        return groups;
    }

    /// <summary>新しいテンプレートグループを追加し、IDを返す。</summary>
    public int InsertTemplateGroup(TemplateGroup group)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO TemplateGroups (GroupName, SortOrder, IsDefault)
            VALUES ($name, $sort, $def);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$name", group.GroupName);
        cmd.Parameters.AddWithValue("$sort", group.SortOrder);
        cmd.Parameters.AddWithValue("$def",  group.IsDefault ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>テンプレートグループを更新する。</summary>
    public void UpdateTemplateGroup(TemplateGroup group)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE TemplateGroups
            SET GroupName = $name, SortOrder = $sort, IsDefault = $def
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id",   group.Id);
        cmd.Parameters.AddWithValue("$name", group.GroupName);
        cmd.Parameters.AddWithValue("$sort", group.SortOrder);
        cmd.Parameters.AddWithValue("$def",  group.IsDefault ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>テンプレートグループを削除する（所属する項目も CASCADE で削除）。</summary>
    public void DeleteTemplateGroup(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM TemplateGroups WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    // TemplateItems CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>指定グループIDのテンプレート項目一覧を取得する。</summary>
    public List<TemplateItem> GetItemsByGroupId(int groupId, SqliteConnection? existingConn = null)
    {
        var list = new List<TemplateItem>();
        bool ownConn = existingConn == null;
        var conn = existingConn ?? CreateConnection();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, GroupId, ItemName, DefaultCount, IsCountable, SortOrder, NeedCheck
                FROM TemplateItems
                WHERE GroupId = $gid
                ORDER BY SortOrder;
            ";
            cmd.Parameters.AddWithValue("$gid", groupId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TemplateItem
                {
                    Id           = reader.GetInt32(0),
                    GroupId      = reader.GetInt32(1),
                    ItemName     = reader.GetString(2),
                    DefaultCount = reader.GetInt32(3),
                    IsCountable  = reader.GetInt32(4) == 1,
                    SortOrder    = reader.GetInt32(5),
                    NeedCheck    = reader.GetInt32(6) == 1,
                });
            }
        }
        finally
        {
            if (ownConn) conn.Dispose();
        }
        return list;
    }

    /// <summary>項目名に一致するテンプレート項目を検索する。見つからなければ null。</summary>
    public TemplateItem? FindTemplateItem(string itemName)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, GroupId, ItemName, DefaultCount, IsCountable, SortOrder, NeedCheck
            FROM TemplateItems
            WHERE ItemName = $name
            LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$name", itemName);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new TemplateItem
            {
                Id           = reader.GetInt32(0),
                GroupId      = reader.GetInt32(1),
                ItemName     = reader.GetString(2),
                DefaultCount = reader.GetInt32(3),
                IsCountable  = reader.GetInt32(4) == 1,
                SortOrder    = reader.GetInt32(5),
                NeedCheck    = reader.GetInt32(6) == 1,
            };
        }
        return null;
    }

    /// <summary>新しいテンプレート項目を追加し、IDを返す。</summary>
    public int InsertTemplateItem(TemplateItem item)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO TemplateItems (GroupId, ItemName, DefaultCount, IsCountable, SortOrder, NeedCheck)
            VALUES ($gid, $name, $def, $cnt, $sort, $need);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$gid",  item.GroupId);
        cmd.Parameters.AddWithValue("$name", item.ItemName);
        cmd.Parameters.AddWithValue("$def",  item.DefaultCount);
        cmd.Parameters.AddWithValue("$cnt",  item.IsCountable ? 1 : 0);
        cmd.Parameters.AddWithValue("$sort", item.SortOrder);
        cmd.Parameters.AddWithValue("$need", item.NeedCheck ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>テンプレート項目を更新する。</summary>
    public void UpdateTemplateItem(TemplateItem item)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE TemplateItems
            SET GroupId      = $gid,
                ItemName     = $name,
                DefaultCount = $def,
                IsCountable  = $cnt,
                SortOrder    = $sort,
                NeedCheck    = $need
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id",   item.Id);
        cmd.Parameters.AddWithValue("$gid",  item.GroupId);
        cmd.Parameters.AddWithValue("$name", item.ItemName);
        cmd.Parameters.AddWithValue("$def",  item.DefaultCount);
        cmd.Parameters.AddWithValue("$cnt",  item.IsCountable ? 1 : 0);
        cmd.Parameters.AddWithValue("$sort", item.SortOrder);
        cmd.Parameters.AddWithValue("$need", item.NeedCheck ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>テンプレート項目を削除する。</summary>
    public void DeleteTemplateItem(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM TemplateItems WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 指定グループのテンプレート項目を当日の日報の先頭に追加する（手動呼び出し用）。
    /// 同日・同名の項目が既にある場合はスキップする（重複防止）。
    /// </summary>
    /// <returns>実際に追加した件数</returns>
    public int AddTemplateGroupToDate(int groupId, string date)
    {
        var items = GetItemsByGroupId(groupId);
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using var conn = CreateConnection();

        // 同日・同名は重複スキップ
        var existingNames = new HashSet<string>(GetEntriesByDate(date).Select(e => e.ItemName));
        var missing = items.Where(i => !existingNames.Contains(i.ItemName)).ToList();
        if (missing.Count == 0) return 0;

        var rows = missing
            .Select(i => (Name: i.ItemName, Count: i.DefaultCount, Countable: i.IsCountable ? 1 : 0, Sort: i.SortOrder, NeedCheck: i.NeedCheck ? 1 : 0))
            .ToList();

        // 既存が0件ならテンプレートのSortOrderのまま挿入
        if (existingNames.Count == 0)
        {
            foreach (var t in rows)
                InsertEntryRow(conn, date, t, t.Sort, now);
            return rows.Count;
        }

        // 既存あり：先頭に挿入
        InsertEntriesAtTop(conn, date, rows, now);
        return rows.Count;
    }

    // ═══════════════════════════════════════════════════════════════
    // TodoTasks CRUD
    // ═══════════════════════════════════════════════════════════════

    private static TodoTask ReadTodoTask(SqliteDataReader r)
    {
        int i = 0;
        return new TodoTask
        {
            Id          = r.GetInt32(i++),
            TaskDate    = r.GetString(i++),
            TaskName    = r.GetString(i++),
            IsCompleted = r.GetInt32(i++) == 1,
            SortOrder   = r.GetInt32(i++),
            CreatedAt   = r.GetString(i++),
            StartTime   = SanitizeTimeValue(r, i++),
            EndTime     = SanitizeTimeValue(r, i++),
            Description = r.IsDBNull(i) ? string.Empty : r.GetString(i++),
        };
    }

    /// <summary>時刻フィールドから不正な日時値を除去する。</summary>
    private static string SanitizeTimeValue(SqliteDataReader r, int index)
    {
        if (r.IsDBNull(index)) return string.Empty;
        string val = r.GetString(index).Trim();
        // 日付形式（YYYY-MM-DD HH:mm:ss など）が入っていたら空にする
        if (val.Length >= 16 && val.Contains(' ')) return string.Empty;
        return val;
    }

    /// <summary>指定日付のToDoタスク一覧を取得する。</summary>
    public List<TodoTask> GetTodosByDate(string date)
    {
        var list = new List<TodoTask>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt,
                   COALESCE(StartTime,''), COALESCE(EndTime,''), COALESCE(Description,'')
            FROM TodoTasks
            WHERE TaskDate = $date
            ORDER BY SortOrder, Id;
        ";
        cmd.Parameters.AddWithValue("$date", date);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadTodoTask(reader));
        return list;
    }

    /// <summary>
    /// 指定日付のToDoリストを取得する（表示用）。
    /// 当日: 未完了のみ（編集可能）
    /// 過去日: 未完了のみ（履歴として読み取り専用、完了済みは表示しない）
    /// 重複除外: 同じタスク名でより新しい日付のものがあれば古い方を除外
    /// </summary>
    public List<TodoTask> GetTodosForDisplay(string date)
    {
        var list = new List<TodoTask>();
        using var conn = CreateConnection();
        
        // 先にマイグレーションを適用してカラムが存在することを保証
        ApplyTodoMigration(conn);
        
        using var cmd = conn.CreateCommand();
        
        // 当日または過去は全てのタスク（完了含む）＋過去の未完了を取得（引き継ぎ用）
        // 未来の日はその日のタスクのみを取得する
        // 重複除外: 同じタスク名でより新しい日付のものがあれば古い方を除外
        cmd.CommandText = @"
            WITH Filtered AS (
                SELECT Id, TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt,
                       COALESCE(StartTime,'') AS StartTime, 
                       COALESCE(EndTime,'') AS EndTime, 
                       COALESCE(Description,'') AS Description,
                       ROW_NUMBER() OVER (PARTITION BY TaskName ORDER BY TaskDate DESC) as rn
                FROM TodoTasks
                WHERE TaskDate = $date
                   OR (TaskDate < $date AND IsCompleted = 0 AND $date <= $today)
            )
            SELECT Id, TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt,
                   StartTime, EndTime, Description
            FROM Filtered
            WHERE rn = 1
            ORDER BY TaskDate DESC, SortOrder, Id;
        ";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$today", DateTime.Today.ToString("yyyy-MM-dd"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadTodoTask(reader));
        return list;
    }

    /// <summary>全ToDoタスクを取得する（エクスポート／インポート用）。</summary>
    public List<TodoTask> GetAllTodoTasks()
    {
        var list = new List<TodoTask>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt,
                   COALESCE(StartTime,''), COALESCE(EndTime,''), COALESCE(Description,'')
            FROM TodoTasks
            ORDER BY TaskDate, SortOrder, Id;
        ";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadTodoTask(reader));
        return list;
    }

    /// <summary>指定日付＋タスク名のToDoが既に存在するか。</summary>
    public bool TodoExists(string date, string taskName)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TodoTasks WHERE TaskDate = $date AND TaskName = $name";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$name", taskName);
        return (long)(cmd.ExecuteScalar() ?? 0) > 0;
    }

    /// <summary>日付＋タスク名でToDo IDを取得する（存在しなければnull）。</summary>
    public int? GetTodoIdByKey(string date, string taskName)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM TodoTasks WHERE TaskDate = $date AND TaskName = $name LIMIT 1";
        cmd.Parameters.AddWithValue("$date", date);
        cmd.Parameters.AddWithValue("$name", taskName);
        var val = cmd.ExecuteScalar();
        return val != null ? (int?)(long)val : null;
    }

    /// <summary>全ToDoからキーワード検索する（タイトル・内容・時刻）。</summary>
    public List<TodoTask> SearchTodos(string keyword)
    {
        var list = new List<TodoTask>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt,
                   COALESCE(StartTime,''), COALESCE(EndTime,''), COALESCE(Description,'')
            FROM TodoTasks
            WHERE TaskName LIKE $kw OR Description LIKE $kw OR StartTime LIKE $kw OR EndTime LIKE $kw
            ORDER BY TaskDate DESC, SortOrder, Id;
        ";
        cmd.Parameters.AddWithValue("$kw", $"%{keyword}%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(ReadTodoTask(reader));
        return list;
    }

    /// <summary>新しいToDoタスクを追加し、IDを返す。</summary>
    public int InsertTodo(TodoTask todo)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = @"
            INSERT INTO TodoTasks (TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt, StartTime, EndTime, Description)
            VALUES ($date, $name, $done, $sort, $now, $start, $end, $desc);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$date",  todo.TaskDate);
        cmd.Parameters.AddWithValue("$name",  todo.TaskName);
        cmd.Parameters.AddWithValue("$done",  todo.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$sort",  todo.SortOrder);
        cmd.Parameters.AddWithValue("$now",   now);
        cmd.Parameters.AddWithValue("$start", todo.StartTime ?? "");
        cmd.Parameters.AddWithValue("$end",   todo.EndTime ?? "");
        cmd.Parameters.AddWithValue("$desc",  todo.Description ?? "");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>ToDoタスクを更新する（完了/未完了の切り替えなど）。</summary>
    public void UpdateTodo(TodoTask todo)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE TodoTasks
            SET TaskName    = $name,
                IsCompleted = $done,
                SortOrder   = $sort,
                StartTime   = $start,
                EndTime     = $end,
                Description = $desc
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id",   todo.Id);
        cmd.Parameters.AddWithValue("$name", todo.TaskName);
        cmd.Parameters.AddWithValue("$done", todo.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$sort", todo.SortOrder);
        cmd.Parameters.AddWithValue("$start", todo.StartTime ?? "");
        cmd.Parameters.AddWithValue("$end",   todo.EndTime ?? "");
        cmd.Parameters.AddWithValue("$desc",  todo.Description ?? "");
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定年月において未完了のToDoが1件以上存在する日付の一覧を返す。</summary>
    public HashSet<int> GetDaysWithIncompleteTodos(int year, int month)
    {
        var days = new HashSet<int>();
        string prefix = $"{year:D4}-{month:D2}-%";
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT TaskDate
            FROM TodoTasks
            WHERE TaskDate LIKE $prefix AND IsCompleted = 0;
        ";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (DateTime.TryParse(reader.GetString(0), out var dt))
                days.Add(dt.Day);
        }
        return days;
    }

    /// <summary>ToDoタスクを削除する。</summary>
    public void DeleteTodo(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM TodoTasks WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    // FavoriteItems CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>全てのよく使う項目を取得する。</summary>
    public List<FavoriteItem> GetAllFavoriteItems()
    {
        var list = new List<FavoriteItem>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ItemName, SortOrder FROM FavoriteItems ORDER BY SortOrder, Id;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new FavoriteItem
            {
                Id        = reader.GetInt32(0),
                ItemName  = reader.GetString(1),
                SortOrder = reader.GetInt32(2),
            });
        }
        return list;
    }

    /// <summary>よく使う項目を追加し、IDを返す。</summary>
    public int InsertFavoriteItem(FavoriteItem item)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO FavoriteItems (ItemName, SortOrder)
            VALUES ($name, $sort);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$name", item.ItemName);
        cmd.Parameters.AddWithValue("$sort", item.SortOrder);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>よく使う項目を削除する。</summary>
    public void DeleteFavoriteItem(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM FavoriteItems WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    // Notes CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>指定日付のメモ一覧を取得する。</summary>
    public List<Note> GetNotesByDate(string date)
    {
        var list = new List<Note>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Title, Content, NoteDate, CreatedAt, UpdatedAt
            FROM Notes
            WHERE NoteDate = $date
            ORDER BY UpdatedAt DESC;
        ";
        cmd.Parameters.AddWithValue("$date", date);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadNote(reader));
        return list;
    }

    /// <summary>タイトルまたは内容でメモを検索する。</summary>
    public List<Note> SearchNotes(string keyword)
    {
        var list = new List<Note>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Title, Content, NoteDate, CreatedAt, UpdatedAt
            FROM Notes
            WHERE Title LIKE $kw OR Content LIKE $kw
            ORDER BY UpdatedAt DESC;
        ";
        cmd.Parameters.AddWithValue("$kw", $"%{keyword}%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadNote(reader));
        return list;
    }

    /// <summary>指定IDのメモを取得する。</summary>
    public Note? GetNoteById(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Title, Content, NoteDate, CreatedAt, UpdatedAt
            FROM Notes WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return ReadNote(reader);
        return null;
    }

    /// <summary>タイトル＋日付でメモが既に存在するか。</summary>
    public bool NoteExists(string title, string date)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Notes WHERE Title = $title AND NoteDate = $date";
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$date", date);
        return (long)(cmd.ExecuteScalar() ?? 0) > 0;
    }

    /// <summary>タイトル＋日付でメモIDを取得する（存在しなければnull）。</summary>
    public int? GetNoteIdByKey(string title, string date)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Notes WHERE Title = $title AND NoteDate = $date LIMIT 1";
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$date", date);
        var val = cmd.ExecuteScalar();
        return val != null ? (int?)(long)val : null;
    }

    /// <summary>メモを新規作成し、IDを返す。</summary>
    public int InsertNote(Note note)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        cmd.CommandText = @"
            INSERT INTO Notes (Title, Content, NoteDate, CreatedAt, UpdatedAt)
            VALUES ($title, $content, $date, $now, $now);
            SELECT last_insert_rowid();
        ";
        cmd.Parameters.AddWithValue("$title",   note.Title);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$date",    note.NoteDate);
        cmd.Parameters.AddWithValue("$now",     now);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>メモを更新する。</summary>
    public void UpdateNote(Note note)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Notes
            SET Title     = $title,
                Content   = $content,
                NoteDate  = $date,
                UpdatedAt = $now
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id",      note.Id);
        cmd.Parameters.AddWithValue("$title",   note.Title);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$date",    note.NoteDate);
        cmd.Parameters.AddWithValue("$now",     DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>メモを削除する。</summary>
    public void DeleteNote(int id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Notes WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>指定年月においてメモが1件以上存在する日付の一覧を返す（カレンダー用）。</summary>
    public HashSet<int> GetDaysWithNotes(int year, int month)
    {
        var days = new HashSet<int>();
        string prefix = $"{year:D4}-{month:D2}-%";
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT NoteDate
            FROM Notes
            WHERE NoteDate LIKE $prefix;
        ";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string dateStr = reader.GetString(0);
            if (DateTime.TryParse(dateStr, out var dt))
                days.Add(dt.Day);
        }
        return days;
    }

    /// <summary>全メモを取得する（新着順）。</summary>
    public List<Note> GetAllNotes()
    {
        var list = new List<Note>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Title, Content, NoteDate, CreatedAt, UpdatedAt
            FROM Notes
            ORDER BY UpdatedAt DESC;
        ";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadNote(reader));
        return list;
    }

    private static Note ReadNote(SqliteDataReader r) => new()
    {
        Id        = r.GetInt32(0),
        Title     = r.GetString(1),
        Content   = r.GetString(2),
        NoteDate  = r.GetString(3),
        CreatedAt = r.GetString(4),
        UpdatedAt = r.GetString(5),
    };

    // ═══════════════════════════════════════════════════════════════
    // AppSettings CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>設定値を取得する。存在しない場合は defaultValue を返す。</summary>
    public string GetSetting(string key, string defaultValue = "")
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string ?? defaultValue;
    }

    /// <summary>設定値を保存する（Upsert）。</summary>
    public void SetSetting(string key, string value)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $val)
            ON CONFLICT(Key) DO UPDATE SET Value = $val;
        ";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$val", value);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════
    // 移行ユーティリティ（テンプレート→日報）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 指定日付までの未完了タスクを当日へ引き継ぐ（元の日付を保持）。
    /// 既に当日に同一タスクが存在する場合は重複追加しない。
    /// </summary>
    public void CarryOverIncompleteTodos(string toDate)
    {
        var incomplete = new List<TodoTask>();
        using var conn = CreateConnection();

        // StartTime/EndTime/Description カラムが確実に存在するようにマイグレーション
        ApplyTodoMigration(conn);

        // 今日以前で未完了のタスクを全て取得（重複除外のため今日分のタスク名を取得）
        var todayNames = new HashSet<string>();
        using (var todayCmd = conn.CreateCommand())
        {
            todayCmd.CommandText = "SELECT TaskName FROM TodoTasks WHERE TaskDate = $date;";
            todayCmd.Parameters.AddWithValue("$date", toDate);
            using var todayReader = todayCmd.ExecuteReader();
            while (todayReader.Read())
                todayNames.Add(todayReader.GetString(0));
        }

        // 今日以前で未完了のタスクを取得（今日以外）
        using var selCmd = conn.CreateCommand();
        selCmd.CommandText = @"
            SELECT Id, TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt,
                   COALESCE(StartTime,''), COALESCE(EndTime,''), COALESCE(Description,'')
            FROM TodoTasks
            WHERE TaskDate < $date AND IsCompleted = 0
            ORDER BY TaskDate DESC, SortOrder;
        ";
        selCmd.Parameters.AddWithValue("$date", toDate);
        using var reader = selCmd.ExecuteReader();
        while (reader.Read())
        {
            int i = 0;
            var task = new TodoTask
            {
                Id          = reader.GetInt32(i++),
                TaskDate    = reader.GetString(i++),  // 元の日付を保持
                TaskName    = reader.GetString(i++),
                IsCompleted = reader.GetInt32(i++) == 1,
                SortOrder   = reader.GetInt32(i++),
                CreatedAt   = reader.GetString(i++),
                StartTime   = SanitizeTimeValue(reader, i++),
                EndTime     = SanitizeTimeValue(reader, i++),
                Description = reader.IsDBNull(i) ? string.Empty : reader.GetString(i++),
            };
            // 今日には同名タスクがない場合のみ追加
            if (!todayNames.Contains(task.TaskName))
            {
                incomplete.Add(task);
                todayNames.Add(task.TaskName); // 追加済みの名前を記録して重複を防ぐ
            }
        }

        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var task in incomplete)
        {
            using var insCmd = conn.CreateCommand();
            insCmd.CommandText = @"
                INSERT INTO TodoTasks (TaskDate, TaskName, IsCompleted, SortOrder, CreatedAt, StartTime, EndTime, Description)
                VALUES ($date, $name, 0, $sort, $now, $start, $end, $desc);
            ";
            insCmd.Parameters.AddWithValue("$date", task.TaskDate);  // 元の日付を保持
            insCmd.Parameters.AddWithValue("$name", task.TaskName);
            insCmd.Parameters.AddWithValue("$sort", task.SortOrder);
            insCmd.Parameters.AddWithValue("$now",  now);
            insCmd.Parameters.AddWithValue("$start", task.StartTime ?? "");
            insCmd.Parameters.AddWithValue("$end",   task.EndTime ?? "");
            insCmd.Parameters.AddWithValue("$desc",  task.Description ?? "");
            insCmd.ExecuteNonQuery();
        }

        // 引き継ぎ完了後、過去の未完了タスクをすべて完了（チェック）状態にして
        // 過去分が未チェックのまま残り続けるのを防ぐ
        using var updCmd = conn.CreateCommand();
        updCmd.CommandText = "UPDATE TodoTasks SET IsCompleted = 1 WHERE TaskDate < $date AND IsCompleted = 0;";
        updCmd.Parameters.AddWithValue("$date", toDate);
        updCmd.ExecuteNonQuery();
    }
}
