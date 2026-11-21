using System.Globalization;
using System.Text.Json;
using FortniteCompanion.Core.Replay;
using Microsoft.Data.Sqlite;

namespace FortniteCompanion.Core.Data;

/// <summary>Bir replay sonucunun veri tabanına yazılma sonucu.</summary>
public sealed record MatchUpsertOutcome(bool NewMatch, int NewRows, int UpdatedRows, bool AlreadyCounted);

/// <summary>
/// SQLite veri tabanı. Tek bağlantı + kilit: uygulama tek süreçtir ve yazma yükü çok küçüktür.
/// Maç, oyuncu-maç ve değişiklik günlüğü tablolarında DELETE tetikleyicilerle engellenmiştir (kalıcı geçmiş);
/// bir kayıt toplamdan çıkarılmak istenirse "hariç tutulur".
/// </summary>
public sealed class Database : IDisposable
{
    public const int SchemaVersion = 1;

    private readonly SqliteConnection _conn;
    private readonly object _lock = new();

    public string? FilePath { get; }

    private Database(SqliteConnection conn, string? filePath)
    {
        _conn = conn;
        FilePath = filePath;
        _conn.Open();
        Exec("PRAGMA foreign_keys = ON;");
        if (filePath != null)
        {
            Exec("PRAGMA journal_mode = WAL;");
            Exec("PRAGMA synchronous = NORMAL;");
        }
        Migrate();
    }

    public static Database OpenFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private }.ToString();
        return new Database(new SqliteConnection(cs), path);
    }

    /// <summary>Testler için bellek içi veri tabanı.</summary>
    public static Database OpenInMemory() => new(new SqliteConnection("Data Source=:memory:"), null);

    public void Dispose()
    {
        lock (_lock)
        {
            _conn.Dispose();
        }
    }

    // ------------------------------------------------------------------ yardımcılar

    // Kind=Unspecified değerler UTC kabul edilir (yerel saat sanılıp kaydırılmaz). Yerel saat yazmak isteyen
    // çağıran, Kind=Local vermelidir.
    internal static string? D(DateTime? d) => d is not DateTime v
        ? null
        : (v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc))
            .ToString("O", CultureInfo.InvariantCulture);

    // Not: RoundtripKind, AdjustToUniversal ile birlikte kullanılamaz (ArgumentException). Önce olduğu gibi okunur,
    // sonra UTC'ye çevrilir. Yazılan değerler her zaman "O" biçiminde ve "Z" sonekli UTC'dir.
    internal static DateTime? PD(object? v)
    {
        if (v is not string s || !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
        {
            return null;
        }
        return d.Kind switch
        {
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };
    }

    private static object Db(object? v) => v ?? DBNull.Value;

    private static int? NI(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);
    private static string? NS(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Cmd(string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, Db(value));
        }
        return cmd;
    }

    // ------------------------------------------------------------------ şema

    private void Migrate()
    {
        lock (_lock)
        {
            Exec("""
                CREATE TABLE IF NOT EXISTS meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT
                );
                CREATE TABLE IF NOT EXISTS players (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    display_name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    epic_names   TEXT NOT NULL DEFAULT '',
                    sort_order   INTEGER NOT NULL DEFAULT 0,
                    created_utc  TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS baselines (
                    player_id   INTEGER PRIMARY KEY REFERENCES players(id),
                    matches     INTEGER NOT NULL DEFAULT 0,
                    chests      INTEGER NOT NULL DEFAULT 0,
                    kills       INTEGER NOT NULL DEFAULT 0,
                    cutoff_utc  TEXT NOT NULL,
                    note        TEXT,
                    updated_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS matches (
                    session_id       TEXT PRIMARY KEY,
                    started_utc      TEXT,
                    duration_s       INTEGER,
                    playlist         TEXT,
                    map              TEXT,
                    is_custom        INTEGER NOT NULL DEFAULT 0,
                    team_size        INTEGER,
                    total_players    INTEGER,
                    placement        INTEGER,
                    game_version     TEXT,
                    source           TEXT NOT NULL,
                    analyzer_version INTEGER NOT NULL DEFAULT 0,
                    first_seen_utc   TEXT NOT NULL,
                    excluded         INTEGER NOT NULL DEFAULT 0,
                    exclude_reason   TEXT,
                    overlap_override INTEGER NOT NULL DEFAULT 0,
                    note             TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_matches_started ON matches(started_utc);
                CREATE TABLE IF NOT EXISTS match_players (
                    session_id       TEXT NOT NULL REFERENCES matches(session_id),
                    observer         TEXT NOT NULL,
                    player_name      TEXT NOT NULL COLLATE NOCASE,
                    is_recorder      INTEGER NOT NULL DEFAULT 0,
                    is_bot           INTEGER NOT NULL DEFAULT 0,
                    chests           INTEGER NOT NULL DEFAULT 0,
                    chests_by_kind   TEXT,
                    ammo_boxes       INTEGER NOT NULL DEFAULT 0,
                    other_containers INTEGER NOT NULL DEFAULT 0,
                    destroyed_chests INTEGER NOT NULL DEFAULT 0,
                    kills            INTEGER,
                    source           TEXT NOT NULL,
                    file_name        TEXT,
                    imported_from    TEXT,
                    created_utc      TEXT NOT NULL,
                    PRIMARY KEY (session_id, observer, player_name)
                );
                CREATE TABLE IF NOT EXISTS processed_files (
                    file_key      TEXT PRIMARY KEY,
                    file_name     TEXT NOT NULL,
                    status        TEXT NOT NULL,
                    session_id    TEXT,
                    message       TEXT,
                    processed_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS edit_log (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts_utc     TEXT NOT NULL,
                    entity     TEXT NOT NULL,
                    entity_key TEXT NOT NULL,
                    field      TEXT NOT NULL,
                    old_value  TEXT,
                    new_value  TEXT,
                    reason     TEXT
                );
                CREATE TABLE IF NOT EXISTS live_sessions (
                    id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    player_id         INTEGER NOT NULL,
                    started_utc       TEXT NOT NULL,
                    ended_utc         TEXT,
                    manual_count      INTEGER NOT NULL DEFAULT 0,
                    status            TEXT NOT NULL,
                    linked_session_id TEXT
                );
                CREATE TRIGGER IF NOT EXISTS trg_no_delete_matches BEFORE DELETE ON matches
                BEGIN SELECT RAISE(ABORT, 'Kalici gecmis: mac kayitlari silinemez, bunun yerine haric tutun.'); END;
                CREATE TRIGGER IF NOT EXISTS trg_no_delete_match_players BEFORE DELETE ON match_players
                BEGIN SELECT RAISE(ABORT, 'Kalici gecmis: mac oyuncu kayitlari silinemez.'); END;
                CREATE TRIGGER IF NOT EXISTS trg_no_delete_edit_log BEFORE DELETE ON edit_log
                BEGIN SELECT RAISE(ABORT, 'Degisiklik gecmisi silinemez.'); END;
                CREATE TRIGGER IF NOT EXISTS trg_no_delete_baselines BEFORE DELETE ON baselines
                BEGIN SELECT RAISE(ABORT, 'Gecmis baslangic kayitlari silinemez.'); END;
                """);
            using var cmd = Cmd("INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_version', $v);", null, ("$v", SchemaVersion.ToString(CultureInfo.InvariantCulture)));
            cmd.ExecuteNonQuery();
        }
        AddColumnIfMissing("players", "hidden", "INTEGER NOT NULL DEFAULT 0");
        RepairLegacyStartTimes();
    }

    private void AddColumnIfMissing(string table, string column, string definition)
    {
        lock (_lock)
        {
            using (var info = Cmd($"PRAGMA table_info({table});"))
            using (var r = info.ExecuteReader())
            {
                while (r.Read())
                {
                    if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }
            Exec($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        }
    }

    /// <summary>
    /// Sürüm 0.1.x'in (analiz sürümü 1) kaydettiği replay maçlarının başlangıç saatini düzeltir: o sürüm UTC saati
    /// yerel saat sanıp yerel saat farkı kadar (Türkiye'de 3 saat) erken yazıyordu. Bu yüzden yeni maçlar
    /// "geçmiş başlangıç tarihinden önce" görünüp örtüşme sayılıyordu. Her açılışta ve içe aktarmadan sonra çalışır;
    /// düzeltilen maç sürüm 2'ye yükseltilir, böylece ikinci kez kaydırılmaz.
    /// </summary>
    public int RepairLegacyStartTimes()
    {
        lock (_lock)
        {
            var list = new List<(string Id, DateTime Start)>();
            using (var q = Cmd("SELECT session_id, started_utc FROM matches WHERE source = 'replay' AND analyzer_version < 2 AND started_utc IS NOT NULL;"))
            using (var r = q.ExecuteReader())
            {
                while (r.Read())
                {
                    if (PD(r.GetString(1)) is DateTime s)
                    {
                        list.Add((r.GetString(0), s));
                    }
                }
            }
            if (list.Count == 0)
            {
                return 0;
            }

            using var tx = _conn.BeginTransaction();
            foreach (var (id, start) in list)
            {
                var fixedStart = start + TimeZoneInfo.Local.GetUtcOffset(start);
                using (var u = Cmd("UPDATE matches SET started_utc = $s, analyzer_version = 2 WHERE session_id = $id;", tx,
                           ("$s", D(fixedStart)), ("$id", id)))
                {
                    u.ExecuteNonQuery();
                }
                LogUnlocked(tx, "match", id, "started_utc", D(start), D(fixedStart),
                    "Otomatik düzeltme: 0.1.x sürümü başlangıç saatini yerel saat farkı kadar erken kaydetmişti");
            }
            tx.Commit();
            return list.Count;
        }
    }

    public string? GetMeta(string key)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT value FROM meta WHERE key = $k;", null, ("$k", key));
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetMeta(string key, string? value)
    {
        lock (_lock)
        {
            using var cmd = Cmd("INSERT INTO meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", null,
                ("$k", key), ("$v", value));
            cmd.ExecuteNonQuery();
        }
    }

    // ------------------------------------------------------------------ değişiklik günlüğü

    private void LogUnlocked(SqliteTransaction? tx, string entity, string key, string field, string? oldValue, string? newValue, string? reason)
    {
        using var cmd = Cmd("""
            INSERT INTO edit_log(ts_utc, entity, entity_key, field, old_value, new_value, reason)
            VALUES ($ts, $e, $k, $f, $o, $n, $r);
            """, tx, ("$ts", D(DateTime.UtcNow)), ("$e", entity), ("$k", key), ("$f", field), ("$o", oldValue), ("$n", newValue), ("$r", reason));
        cmd.ExecuteNonQuery();
    }

    public void Log(string entity, string key, string field, string? oldValue, string? newValue, string? reason)
    {
        lock (_lock)
        {
            LogUnlocked(null, entity, key, field, oldValue, newValue, reason);
        }
    }

    public List<EditLogEntry> GetEditLog(int limit = 500)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT id, ts_utc, entity, entity_key, field, old_value, new_value, reason FROM edit_log ORDER BY id DESC LIMIT $l;", null, ("$l", limit));
            using var r = cmd.ExecuteReader();
            var list = new List<EditLogEntry>();
            while (r.Read())
            {
                list.Add(new EditLogEntry
                {
                    Id = r.GetInt64(0),
                    TimestampUtc = PD(r.GetString(1)) ?? DateTime.UtcNow,
                    Entity = r.GetString(2),
                    EntityKey = r.GetString(3),
                    Field = r.GetString(4),
                    OldValue = NS(r, 5),
                    NewValue = NS(r, 6),
                    Reason = NS(r, 7),
                });
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ oyuncular ve geçmiş başlangıç

    /// <summary>
    /// İlk çalıştırmada dört oyuncuyu ve önceden tutulan gerçek istatistikleri "Geçmişten eklenen kayıtlar"
    /// olarak ekler. Oyuncu tablosu doluysa hiçbir şey yapmaz (veriler asla üzerine yazılmaz).
    /// </summary>
    public bool SeedDefaults(DateTime cutoffUtc)
    {
        lock (_lock)
        {
            using (var count = Cmd("SELECT COUNT(*) FROM players;"))
            {
                if (Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                {
                    return false;
                }
            }

            using var tx = _conn.BeginTransaction();
            var order = 0;
            foreach (var seed in SeedData.Players)
            {
                using (var ins = Cmd("INSERT INTO players(display_name, epic_names, sort_order, created_utc) VALUES ($n, '', $o, $c);", tx,
                           ("$n", seed.Name), ("$o", order++), ("$c", D(DateTime.UtcNow))))
                {
                    ins.ExecuteNonQuery();
                }

                long id;
                using (var last = Cmd("SELECT last_insert_rowid();", tx))
                {
                    id = Convert.ToInt64(last.ExecuteScalar(), CultureInfo.InvariantCulture);
                }

                using (var b = Cmd("""
                           INSERT INTO baselines(player_id, matches, chests, kills, cutoff_utc, note, updated_utc)
                           VALUES ($p, $m, $c, $k, $cut, $note, $u);
                           """, tx, ("$p", id), ("$m", seed.Matches), ("$c", seed.Chests), ("$k", seed.Kills),
                           ("$cut", D(cutoffUtc)), ("$note", SeedData.Note), ("$u", D(SeedData.SeededAtUtc))))
                {
                    b.ExecuteNonQuery();
                }

                LogUnlocked(tx, "baseline", seed.Name, "seed", null,
                    $"maç={seed.Matches}, sandık={seed.Chests}, kill={seed.Kills}", "İlk kurulum: önceden tutulan istatistikler");
            }
            tx.Commit();
            return true;
        }
    }

    public List<Player> GetPlayers()
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT id, display_name, epic_names, sort_order, hidden FROM players ORDER BY sort_order, id;");
            using var r = cmd.ExecuteReader();
            var list = new List<Player>();
            while (r.Read())
            {
                list.Add(new Player
                {
                    Id = r.GetInt64(0),
                    DisplayName = r.GetString(1),
                    EpicNames = r.GetString(2),
                    SortOrder = r.GetInt32(3),
                    Hidden = r.GetInt32(4) != 0,
                });
            }
            return list;
        }
    }

    public Player AddPlayer(string displayName, string epicNames = "")
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            int order;
            using (var max = Cmd("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM players;", tx))
            {
                order = Convert.ToInt32(max.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            using (var ins = Cmd("INSERT INTO players(display_name, epic_names, sort_order, created_utc) VALUES ($n, $e, $o, $c);", tx,
                       ("$n", displayName.Trim()), ("$e", epicNames.Trim()), ("$o", order), ("$c", D(DateTime.UtcNow))))
            {
                ins.ExecuteNonQuery();
            }
            long id;
            using (var last = Cmd("SELECT last_insert_rowid();", tx))
            {
                id = Convert.ToInt64(last.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            LogUnlocked(tx, "player", displayName, "create", null, epicNames, "Oyuncu eklendi");
            tx.Commit();
            return new Player { Id = id, DisplayName = displayName.Trim(), EpicNames = epicNames.Trim(), SortOrder = order };
        }
    }

    public void SetEpicNames(long playerId, string epicNames, string reason)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            string? old = null, name = null;
            using (var q = Cmd("SELECT epic_names, display_name FROM players WHERE id = $id;", tx, ("$id", playerId)))
            using (var r = q.ExecuteReader())
            {
                if (r.Read())
                {
                    old = r.GetString(0);
                    name = r.GetString(1);
                }
            }
            if (name == null)
            {
                return;
            }
            var normalized = string.Join(", ", epicNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (string.Equals(old, normalized, StringComparison.Ordinal))
            {
                return;
            }
            using (var u = Cmd("UPDATE players SET epic_names = $e WHERE id = $id;", tx, ("$e", normalized), ("$id", playerId)))
            {
                u.ExecuteNonQuery();
            }
            LogUnlocked(tx, "player", name, "epic_names", old, normalized, reason);
            tx.Commit();
        }
    }

    /// <summary>Oyuncuyu gizler/gösterir (ekipten ayrılan biri). Veriler silinmez.</summary>
    public void SetHidden(long playerId, bool hidden, string reason)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            string? name;
            using (var q = Cmd("SELECT display_name FROM players WHERE id = $id;", tx, ("$id", playerId)))
            {
                name = q.ExecuteScalar() as string;
            }
            if (name == null)
            {
                return;
            }
            using (var u = Cmd("UPDATE players SET hidden = $h WHERE id = $id;", tx, ("$h", hidden ? 1 : 0), ("$id", playerId)))
            {
                u.ExecuteNonQuery();
            }
            LogUnlocked(tx, "player", name, "hidden", (!hidden).ToString(), hidden.ToString(), reason);
            tx.Commit();
        }
    }

    /// <summary>Görünen adı değiştirir. Aynı ad başka oyuncuda varsa istisna fırlatır.</summary>
    public void RenamePlayer(long playerId, string newName, string reason)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
        {
            throw new ArgumentException("Ad boş olamaz.", nameof(newName));
        }
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            string? old;
            using (var q = Cmd("SELECT display_name FROM players WHERE id = $id;", tx, ("$id", playerId)))
            {
                old = q.ExecuteScalar() as string;
            }
            if (old == null || string.Equals(old, newName, StringComparison.Ordinal))
            {
                return;
            }
            using (var u = Cmd("UPDATE players SET display_name = $n WHERE id = $id;", tx, ("$n", newName), ("$id", playerId)))
            {
                u.ExecuteNonQuery();
            }
            LogUnlocked(tx, "player", old, "display_name", old, newName, reason);
            tx.Commit();
        }
    }

    public List<Baseline> GetBaselines()
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT player_id, matches, chests, kills, cutoff_utc, note, updated_utc FROM baselines;");
            using var r = cmd.ExecuteReader();
            var list = new List<Baseline>();
            while (r.Read())
            {
                list.Add(new Baseline
                {
                    PlayerId = r.GetInt64(0),
                    Matches = r.GetInt32(1),
                    Chests = r.GetInt32(2),
                    Kills = r.GetInt32(3),
                    CutoffUtc = PD(r.GetString(4)) ?? DateTime.UtcNow,
                    Note = NS(r, 5),
                    UpdatedUtc = PD(r.GetString(6)) ?? DateTime.UtcNow,
                });
            }
            return list;
        }
    }

    /// <summary>Geçmiş başlangıç değerlerini günceller; değişen her alan günlüğe yazılır.</summary>
    public void UpdateBaseline(long playerId, int matches, int chests, int kills, DateTime cutoffUtc, string? note, string reason)
    {
        if (matches < 0 || chests < 0 || kills < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(matches), "Değerler negatif olamaz.");
        }

        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            string playerName;
            using (var n = Cmd("SELECT display_name FROM players WHERE id = $id;", tx, ("$id", playerId)))
            {
                playerName = n.ExecuteScalar() as string ?? throw new InvalidOperationException("Oyuncu bulunamadı.");
            }

            Baseline? old = null;
            using (var q = Cmd("SELECT matches, chests, kills, cutoff_utc, note FROM baselines WHERE player_id = $id;", tx, ("$id", playerId)))
            using (var r = q.ExecuteReader())
            {
                if (r.Read())
                {
                    old = new Baseline
                    {
                        PlayerId = playerId,
                        Matches = r.GetInt32(0),
                        Chests = r.GetInt32(1),
                        Kills = r.GetInt32(2),
                        CutoffUtc = PD(r.GetString(3)) ?? DateTime.UtcNow,
                        Note = NS(r, 4),
                    };
                }
            }

            using (var up = Cmd("""
                       INSERT INTO baselines(player_id, matches, chests, kills, cutoff_utc, note, updated_utc)
                       VALUES ($p, $m, $c, $k, $cut, $note, $u)
                       ON CONFLICT(player_id) DO UPDATE SET matches = excluded.matches, chests = excluded.chests,
                           kills = excluded.kills, cutoff_utc = excluded.cutoff_utc, note = excluded.note, updated_utc = excluded.updated_utc;
                       """, tx, ("$p", playerId), ("$m", matches), ("$c", chests), ("$k", kills), ("$cut", D(cutoffUtc)),
                       ("$note", note), ("$u", D(DateTime.UtcNow))))
            {
                up.ExecuteNonQuery();
            }

            void Changed(string field, string? a, string? b)
            {
                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    LogUnlocked(tx, "baseline", playerName, field, a, b, reason);
                }
            }

            Changed("matches", old?.Matches.ToString(CultureInfo.InvariantCulture), matches.ToString(CultureInfo.InvariantCulture));
            Changed("chests", old?.Chests.ToString(CultureInfo.InvariantCulture), chests.ToString(CultureInfo.InvariantCulture));
            Changed("kills", old?.Kills.ToString(CultureInfo.InvariantCulture), kills.ToString(CultureInfo.InvariantCulture));
            Changed("cutoff_utc", D(old?.CutoffUtc), D(cutoffUtc));
            Changed("note", old?.Note, note);
            tx.Commit();
        }
    }

    // ------------------------------------------------------------------ maçlar

    private const string MatchColumns =
        "session_id, started_utc, duration_s, playlist, map, is_custom, team_size, total_players, placement, game_version, source, analyzer_version, first_seen_utc, excluded, exclude_reason, overlap_override, note";

    private static MatchRow ReadMatch(SqliteDataReader r) => new()
    {
        SessionId = r.GetString(0),
        StartedUtc = PD(NS(r, 1)),
        DurationSeconds = NI(r, 2),
        Playlist = NS(r, 3),
        Map = NS(r, 4),
        IsCustom = r.GetInt32(5) != 0,
        TeamSize = NI(r, 6),
        TotalPlayers = NI(r, 7),
        Placement = NI(r, 8),
        GameVersion = NS(r, 9),
        Source = DataSourceText.FromDb(r.GetString(10)),
        AnalyzerVersion = r.GetInt32(11),
        FirstSeenUtc = PD(r.GetString(12)) ?? DateTime.UtcNow,
        Excluded = r.GetInt32(13) != 0,
        ExcludeReason = NS(r, 14),
        OverlapOverride = r.GetInt32(15) != 0,
        Note = NS(r, 16),
    };

    public List<MatchRow> GetMatches()
    {
        lock (_lock)
        {
            using var cmd = Cmd($"SELECT {MatchColumns} FROM matches ORDER BY started_utc DESC;");
            using var r = cmd.ExecuteReader();
            var list = new List<MatchRow>();
            while (r.Read())
            {
                list.Add(ReadMatch(r));
            }
            return list;
        }
    }

    public MatchRow? GetMatch(string sessionId)
    {
        lock (_lock)
        {
            using var cmd = Cmd($"SELECT {MatchColumns} FROM matches WHERE session_id = $s;", null, ("$s", sessionId));
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadMatch(r) : null;
        }
    }

    private static MatchPlayerRow ReadMatchPlayer(SqliteDataReader r)
    {
        Dictionary<string, int>? kinds = null;
        var json = NS(r, 6);
        if (!string.IsNullOrEmpty(json))
        {
            try { kinds = JsonSerializer.Deserialize<Dictionary<string, int>>(json); } catch (JsonException) { kinds = null; }
        }
        return new MatchPlayerRow
        {
            SessionId = r.GetString(0),
            Observer = r.GetString(1),
            PlayerName = r.GetString(2),
            IsRecorder = r.GetInt32(3) != 0,
            IsBot = r.GetInt32(4) != 0,
            Chests = r.GetInt32(5),
            ChestsByKind = kinds ?? new Dictionary<string, int>(),
            AmmoBoxes = r.GetInt32(7),
            OtherContainers = r.GetInt32(8),
            DestroyedChests = r.GetInt32(9),
            Kills = NI(r, 10),
            Source = DataSourceText.FromDb(r.GetString(11)),
            FileName = NS(r, 12),
            ImportedFrom = NS(r, 13),
            CreatedUtc = PD(r.GetString(14)) ?? DateTime.UtcNow,
        };
    }

    private const string MatchPlayerColumns =
        "session_id, observer, player_name, is_recorder, is_bot, chests, chests_by_kind, ammo_boxes, other_containers, destroyed_chests, kills, source, file_name, imported_from, created_utc";

    public List<MatchPlayerRow> GetMatchPlayers()
    {
        lock (_lock)
        {
            using var cmd = Cmd($"SELECT {MatchPlayerColumns} FROM match_players;");
            using var r = cmd.ExecuteReader();
            var list = new List<MatchPlayerRow>();
            while (r.Read())
            {
                list.Add(ReadMatchPlayer(r));
            }
            return list;
        }
    }

    private void InsertMatchUnlocked(SqliteTransaction tx, MatchRow m)
    {
        using var cmd = Cmd($"""
            INSERT INTO matches({MatchColumns})
            VALUES ($s, $st, $d, $pl, $map, $cu, $ts, $tp, $pc, $gv, $src, $av, $fs, $ex, $er, $oo, $note);
            """, tx,
            ("$s", m.SessionId), ("$st", D(m.StartedUtc)), ("$d", m.DurationSeconds), ("$pl", m.Playlist), ("$map", m.Map),
            ("$cu", m.IsCustom ? 1 : 0), ("$ts", m.TeamSize), ("$tp", m.TotalPlayers), ("$pc", m.Placement), ("$gv", m.GameVersion),
            ("$src", DataSourceText.ToDb(m.Source)), ("$av", m.AnalyzerVersion), ("$fs", D(m.FirstSeenUtc)),
            ("$ex", m.Excluded ? 1 : 0), ("$er", m.ExcludeReason), ("$oo", m.OverlapOverride ? 1 : 0), ("$note", m.Note));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Satır yoksa ekler (true), varsa dokunmaz (false).</summary>
    private bool InsertMatchPlayerUnlocked(SqliteTransaction tx, MatchPlayerRow p, bool replaceExisting)
    {
        // Var olan satır silinmeden güncellenir (DELETE tetikleyicisi kalıcı geçmişi korur).
        var conflict = replaceExisting
            ? "ON CONFLICT(session_id, observer, player_name) DO UPDATE SET is_recorder = excluded.is_recorder, is_bot = excluded.is_bot, chests = excluded.chests, chests_by_kind = excluded.chests_by_kind, ammo_boxes = excluded.ammo_boxes, other_containers = excluded.other_containers, destroyed_chests = excluded.destroyed_chests, kills = excluded.kills, source = excluded.source, file_name = excluded.file_name"
            : "ON CONFLICT(session_id, observer, player_name) DO NOTHING";
        using var cmd = Cmd($"""
            INSERT INTO match_players({MatchPlayerColumns})
            VALUES ($s, $o, $n, $rec, $bot, $c, $kinds, $ammo, $other, $des, $k, $src, $file, $imp, $cr)
            {conflict};
            """, tx,
            ("$s", p.SessionId), ("$o", p.Observer), ("$n", p.PlayerName), ("$rec", p.IsRecorder ? 1 : 0), ("$bot", p.IsBot ? 1 : 0),
            ("$c", p.Chests), ("$kinds", JsonSerializer.Serialize(p.ChestsByKind)), ("$ammo", p.AmmoBoxes), ("$other", p.OtherContainers),
            ("$des", p.DestroyedChests), ("$k", p.Kills), ("$src", DataSourceText.ToDb(p.Source)), ("$file", p.FileName),
            ("$imp", p.ImportedFrom), ("$cr", D(p.CreatedUtc)));
        return cmd.ExecuteNonQuery() > 0;
    }

    private bool MatchPlayerExistsUnlocked(SqliteTransaction tx, string session, string observer, string player)
    {
        using var cmd = Cmd("SELECT 1 FROM match_players WHERE session_id = $s AND observer = $o AND player_name = $n;", tx,
            ("$s", session), ("$o", observer), ("$n", player));
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>
    /// Bir replay sonucunu kaydeder. Aynı maç (aynı oturum kimliği + aynı gözlemci) ikinci kez sayılmaz.
    /// Daha yeni analiz sürümü gelirse satırlar güncellenir ve günlüğe yazılır.
    /// </summary>
    public MatchUpsertOutcome UpsertReplayMatch(ReplayMatchResult result, string? importedFrom = null)
    {
        if (!result.RecorderFound || result.Players.Count == 0)
        {
            throw new InvalidOperationException("Kaydeden oyuncu bulunamayan replay kaydedilemez.");
        }

        var observer = string.IsNullOrWhiteSpace(result.RecorderName) ? "?" : result.RecorderName!.Trim();

        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            MatchRow? existing = null;
            using (var q = Cmd($"SELECT {MatchColumns} FROM matches WHERE session_id = $s;", tx, ("$s", result.SessionId)))
            using (var r = q.ExecuteReader())
            {
                if (r.Read())
                {
                    existing = ReadMatch(r);
                }
            }

            var newMatch = existing == null;
            if (newMatch)
            {
                InsertMatchUnlocked(tx, new MatchRow
                {
                    SessionId = result.SessionId,
                    StartedUtc = result.MatchStartUtc,
                    DurationSeconds = result.DurationSeconds,
                    Playlist = result.Playlist,
                    Map = result.Map,
                    IsCustom = result.IsCustomMatch,
                    TeamSize = result.TeamSize,
                    TotalPlayers = result.TotalPlayers,
                    Placement = result.Placement,
                    GameVersion = result.GameVersion,
                    Source = DataSource.Replay,
                    AnalyzerVersion = result.AnalyzerVersion,
                    FirstSeenUtc = DateTime.UtcNow,
                });
            }
            else if (existing!.AnalyzerVersion < result.AnalyzerVersion)
            {
                using var up = Cmd("UPDATE matches SET analyzer_version = $v, duration_s = COALESCE($d, duration_s), placement = COALESCE($p, placement) WHERE session_id = $s;", tx,
                    ("$v", result.AnalyzerVersion), ("$d", result.DurationSeconds), ("$p", result.Placement), ("$s", result.SessionId));
                up.ExecuteNonQuery();
                LogUnlocked(tx, "match", result.SessionId, "analyzer_version",
                    existing.AnalyzerVersion.ToString(CultureInfo.InvariantCulture),
                    result.AnalyzerVersion.ToString(CultureInfo.InvariantCulture), "Daha yeni analiz sürümüyle yeniden okundu");
            }

            var reanalysis = existing != null && existing.AnalyzerVersion < result.AnalyzerVersion;
            int added = 0, updated = 0;
            foreach (var p in result.Players)
            {
                var name = string.IsNullOrWhiteSpace(p.Name) ? (p.IsBot ? "Bot" : "?") : p.Name.Trim();
                var row = new MatchPlayerRow
                {
                    SessionId = result.SessionId,
                    Observer = observer,
                    PlayerName = name,
                    IsRecorder = p.IsRecorder,
                    IsBot = p.IsBot,
                    Chests = p.ChestsTotal,
                    ChestsByKind = new Dictionary<string, int>(p.ChestsByKind),
                    AmmoBoxes = p.AmmoBoxes,
                    OtherContainers = p.OtherContainers,
                    DestroyedChests = p.DestroyedChests,
                    Kills = p.Kills,
                    Source = p.IsRecorder ? DataSource.Replay : DataSource.ReplayPartial,
                    FileName = result.FileName,
                    ImportedFrom = importedFrom,
                    CreatedUtc = DateTime.UtcNow,
                };

                var exists = MatchPlayerExistsUnlocked(tx, result.SessionId, observer, name);
                if (!exists)
                {
                    InsertMatchPlayerUnlocked(tx, row, replaceExisting: false);
                    added++;
                }
                else if (reanalysis)
                {
                    InsertMatchPlayerUnlocked(tx, row, replaceExisting: true);
                    updated++;
                }
            }

            tx.Commit();
            return new MatchUpsertOutcome(newMatch, added, updated, AlreadyCounted: !newMatch && added == 0 && updated == 0);
        }
    }

    /// <summary>İçe aktarılan ham satırları ekler (zaten varsa atlar). Eklenen satır sayısını döndürür.</summary>
    public int ImportRows(MatchRow match, IEnumerable<MatchPlayerRow> rows)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            bool exists;
            using (var q = Cmd("SELECT 1 FROM matches WHERE session_id = $s;", tx, ("$s", match.SessionId)))
            {
                exists = q.ExecuteScalar() != null;
            }
            if (!exists)
            {
                InsertMatchUnlocked(tx, match);
            }
            var added = 0;
            foreach (var row in rows)
            {
                row.SessionId = match.SessionId;
                if (InsertMatchPlayerUnlocked(tx, row, replaceExisting: false))
                {
                    added++;
                }
            }
            tx.Commit();
            return added;
        }
    }

    /// <summary>Replay yoksa (replay kaydı kapalı vb.) manuel maç ekler.</summary>
    public string AddManualMatch(Player player, DateTime startedUtc, int? durationSeconds, int chests, int? kills, string? note, string reason)
    {
        var session = "manual-" + Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            InsertMatchUnlocked(tx, new MatchRow
            {
                SessionId = session,
                StartedUtc = startedUtc,
                DurationSeconds = durationSeconds,
                Source = DataSource.Manual,
                FirstSeenUtc = DateTime.UtcNow,
                Note = note,
            });
            InsertMatchPlayerUnlocked(tx, new MatchPlayerRow
            {
                SessionId = session,
                Observer = "manual",
                PlayerName = player.ManualKey,
                IsRecorder = true,
                Chests = chests,
                Kills = kills,
                Source = DataSource.Manual,
                CreatedUtc = DateTime.UtcNow,
            }, replaceExisting: false);
            LogUnlocked(tx, "match", session, "create_manual", null,
                $"oyuncu={player.DisplayName}, sandık={chests}, kill={kills?.ToString(CultureInfo.InvariantCulture) ?? "-"}", reason);
            tx.Commit();
        }
        return session;
    }

    public void SetExcluded(string sessionId, bool excluded, string reason)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            int? old;
            using (var q = Cmd("SELECT excluded FROM matches WHERE session_id = $s;", tx, ("$s", sessionId)))
            {
                var v = q.ExecuteScalar();
                old = v == null ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            if (old == null || (old != 0) == excluded)
            {
                return;
            }
            using (var u = Cmd("UPDATE matches SET excluded = $e, exclude_reason = $r WHERE session_id = $s;", tx,
                       ("$e", excluded ? 1 : 0), ("$r", excluded ? reason : null), ("$s", sessionId)))
            {
                u.ExecuteNonQuery();
            }
            LogUnlocked(tx, "match", sessionId, "excluded", (old != 0).ToString(), excluded.ToString(), reason);
            tx.Commit();
        }
    }

    public void SetOverlapOverride(string sessionId, bool include, string reason)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            using (var u = Cmd("UPDATE matches SET overlap_override = $o WHERE session_id = $s;", tx, ("$o", include ? 1 : 0), ("$s", sessionId)))
            {
                if (u.ExecuteNonQuery() == 0)
                {
                    return;
                }
            }
            LogUnlocked(tx, "match", sessionId, "overlap_override", (!include).ToString(), include.ToString(), reason);
            tx.Commit();
        }
    }

    // ------------------------------------------------------------------ işlenen dosyalar

    public static string FileKey(FileInfo file) =>
        $"{file.Name}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";

    public bool IsProcessed(string fileKey)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT 1 FROM processed_files WHERE file_key = $k;", null, ("$k", fileKey));
            return cmd.ExecuteScalar() != null;
        }
    }

    public void MarkProcessed(string fileKey, string fileName, string status, string? sessionId, string? message)
    {
        lock (_lock)
        {
            using var cmd = Cmd("""
                INSERT INTO processed_files(file_key, file_name, status, session_id, message, processed_utc)
                VALUES ($k, $f, $st, $s, $m, $t)
                ON CONFLICT(file_key) DO UPDATE SET status = excluded.status, session_id = excluded.session_id,
                    message = excluded.message, processed_utc = excluded.processed_utc;
                """, null, ("$k", fileKey), ("$f", fileName), ("$st", status), ("$s", sessionId), ("$m", message), ("$t", D(DateTime.UtcNow)));
            cmd.ExecuteNonQuery();
        }
    }

    public List<ProcessedFile> GetProcessedFiles(int limit = 300)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT file_key, file_name, status, session_id, message, processed_utc FROM processed_files ORDER BY processed_utc DESC LIMIT $l;", null, ("$l", limit));
            using var r = cmd.ExecuteReader();
            var list = new List<ProcessedFile>();
            while (r.Read())
            {
                list.Add(new ProcessedFile
                {
                    FileKey = r.GetString(0),
                    FileName = r.GetString(1),
                    Status = r.GetString(2),
                    SessionId = NS(r, 3),
                    Message = NS(r, 4),
                    ProcessedUtc = PD(r.GetString(5)) ?? DateTime.UtcNow,
                });
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ canlı manuel sayaç

    public LiveSession? GetOpenLiveSession(long playerId)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT id, player_id, started_utc, ended_utc, manual_count, status, linked_session_id FROM live_sessions WHERE player_id = $p AND status = 'open' ORDER BY id DESC LIMIT 1;", null, ("$p", playerId));
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadLive(r) : null;
        }
    }

    public List<LiveSession> GetLiveSessions(int limit = 200)
    {
        lock (_lock)
        {
            using var cmd = Cmd("SELECT id, player_id, started_utc, ended_utc, manual_count, status, linked_session_id FROM live_sessions ORDER BY id DESC LIMIT $l;", null, ("$l", limit));
            using var r = cmd.ExecuteReader();
            var list = new List<LiveSession>();
            while (r.Read())
            {
                list.Add(ReadLive(r));
            }
            return list;
        }
    }

    private static LiveSession ReadLive(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        PlayerId = r.GetInt64(1),
        StartedUtc = PD(r.GetString(2)) ?? DateTime.UtcNow,
        EndedUtc = PD(NS(r, 3)),
        ManualCount = r.GetInt32(4),
        Status = r.GetString(5),
        LinkedSessionId = NS(r, 6),
    };

    public LiveSession StartLiveSession(long playerId)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            using (var ins = Cmd("INSERT INTO live_sessions(player_id, started_utc, manual_count, status) VALUES ($p, $s, 0, 'open');", null,
                       ("$p", playerId), ("$s", D(now))))
            {
                ins.ExecuteNonQuery();
            }
            using var last = Cmd("SELECT last_insert_rowid();");
            var id = Convert.ToInt64(last.ExecuteScalar(), CultureInfo.InvariantCulture);
            return new LiveSession { Id = id, PlayerId = playerId, StartedUtc = now, Status = "open" };
        }
    }

    public void SetLiveCount(long liveId, int count)
    {
        lock (_lock)
        {
            using var cmd = Cmd("UPDATE live_sessions SET manual_count = $c WHERE id = $id AND status = 'open';", null, ("$c", Math.Max(0, count)), ("$id", liveId));
            cmd.ExecuteNonQuery();
        }
    }

    public void CloseLiveSession(long liveId, string status, string? linkedSessionId)
    {
        lock (_lock)
        {
            using var cmd = Cmd("UPDATE live_sessions SET status = $st, ended_utc = $e, linked_session_id = $l WHERE id = $id;", null,
                ("$st", status), ("$e", D(DateTime.UtcNow)), ("$l", linkedSessionId), ("$id", liveId));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Yalnızca testler: ham SQL çalıştırır (kalıcı geçmiş tetikleyicilerini doğrulamak için).</summary>
    internal int ExecuteForTest(string sql)
    {
        lock (_lock)
        {
            using var cmd = Cmd(sql);
            return cmd.ExecuteNonQuery();
        }
    }

    // ------------------------------------------------------------------ yedekleme

    /// <summary>Çevrimiçi SQLite yedeği (uygulama çalışırken güvenle alınır).</summary>
    public void BackupTo(string destinationPath)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
            using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destinationPath, Pooling = false }.ToString());
            dest.Open();
            _conn.BackupDatabase(dest);
        }
    }
}
