using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Witcher_3_Dynamic_Map;

public sealed record GamePin(string Type, double X, double Y, double Z, bool Discovered, bool Known)
{
    public bool IsQuest => Type.Contains("quest", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the snapshots written by the modMapSync game mod into scriptslog.txt.
/// A snapshot is B|world|quest, then P|type|x|y|z|discovered|known lines, then E|count.
/// Only a finished snapshot replaces the previous one.
/// </summary>
public sealed class GameSync
{
    private const string Marker = "@@MS|";
    private const long MaxInitialRead = 8 * 1024 * 1024;

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "The Witcher 3", "scriptslog.txt");

    private long position = -1;
    private string leftover = "";
    private string? pendingWorld;
    private string pendingQuest = "";
    private List<GamePin>? pending;

    public string? WorldPath { get; private set; }
    public string? Region => WorldPath is null ? null : RegionFromWorldPath(WorldPath);
    public string QuestTitle { get; private set; } = "";
    public IReadOnlyList<GamePin> Pins { get; private set; } = Array.Empty<GamePin>();
    public DateTime? LastSnapshot { get; private set; }
    public bool LogExists { get; private set; }

    // Player position written by the mod (the app does not read the game's memory).
    private (double X, double Y, double Z) playerPos;
    private DateTime playerAt = DateTime.MinValue;
    private bool ignorePositions;

    /// <summary>The last position sent by the mod, if it is not older than <paramref name="maxAge"/>.</summary>
    public bool TryGetPosition(TimeSpan maxAge, out (double X, double Y, double Z) pos)
    {
        pos = playerPos;
        return DateTime.UtcNow - playerAt <= maxAge;
    }

    public static string? RegionFromWorldPath(string path)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("bob")) return "Toussaint";
        if (p.Contains("kaer_morhen")) return "Kaer Morhen";
        if (p.Contains("skellige")) return "Skellige";
        if (p.Contains("prolog")) return "White Orchard";
        if (p.Contains("novigrad")) return "Velen";
        return null;
    }

    /// <summary>Reads new log lines. Returns true when a new finished snapshot was applied.</summary>
    public bool Poll()
    {
        bool updated = false;
        try
        {
            LogExists = File.Exists(LogPath);
            if (!LogExists) return false;

            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = fs.Length;

            // Positions from the old part of the log (first read) must not count as "current".
            ignorePositions = position < 0;
            if (position < 0)
            {
                // First read: only the tail of a possibly huge log. The first (cut) line is dropped.
                position = Math.Max(0, length - MaxInitialRead);
                leftover = position > 0 ? "\n" : "";
            }
            else if (length < position)
            {
                // The game recreated the log.
                position = 0;
                leftover = "";
                pending = null;
            }

            if (length == position) return false;

            fs.Seek(position, SeekOrigin.Begin);
            var buf = new byte[length - position];
            int read = fs.Read(buf, 0, buf.Length);
            position += read;

            var text = leftover + Encoding.UTF8.GetString(buf, 0, read);
            int lastNl = text.LastIndexOf('\n');
            if (lastNl < 0) { leftover = text; return false; }
            leftover = text[(lastNl + 1)..];

            foreach (var line in text[..lastNl].Split('\n'))
                if (ParseLine(line)) updated = true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return updated;
    }

    private bool ParseLine(string line)
    {
        int idx = line.IndexOf(Marker, StringComparison.Ordinal);
        if (idx < 0) return false;

        var f = line[(idx + Marker.Length)..].TrimEnd('\r', ' ').Split('|');
        switch (f[0])
        {
            case "B" when f.Length >= 2:
                pendingWorld = f[1];
                pendingQuest = f.Length > 2 ? string.Join("|", f.Skip(2)) : "";
                pending = new List<GamePin>();
                return false;

            case "P" when pending is not null && f.Length >= 7:
                if (double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    pending.Add(new GamePin(f[1], x, y, z, f[5] == "1", f[6] == "1"));
                return false;

            case "L" when f.Length >= 4:
                if (!ignorePositions &&
                    double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lx) &&
                    double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ly) &&
                    double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lz))
                {
                    playerPos = (lx, ly, lz);
                    playerAt = DateTime.UtcNow;
                }
                return false;

            case "E" when pending is not null:
                WorldPath = pendingWorld;
                QuestTitle = pendingQuest;
                Pins = pending;
                LastSnapshot = DateTime.Now;
                pending = null;
                return true;
        }
        return false;
    }
}
