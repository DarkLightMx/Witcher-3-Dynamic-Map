using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace Witcher_3_Dynamic_Map;

public sealed class Form1 : Form
{
    private const string ProcessName = "witcher3";

    private readonly MapControl map;
    private readonly StatusLabel status;
    private readonly Label regionLabel;
    private readonly Label coords;
    private readonly Label questLabel;
    private readonly Label statsLabel;
    private readonly FilterList filters;
    private readonly ToggleSwitch followSwitch;
    private readonly ToggleSwitch hideVisited;

    private readonly GameSync sync = new();
    private int syncTick;
    private readonly PoiDatabase poiDb = new();
    private readonly TileMap tileMap = new();
    private readonly CancellationTokenSource cts = new();

    private bool gameRunning;
    private long lastProcessCheck = -10000;
    private System.Windows.Forms.Timer timer = null!;

    private double x, y, z;                 // displayed (smoothed) position
    private double targetX, targetY;        // last position received from the mod
    private string currentRegion = "Velen";
    private bool hasPosition;

    public Form1()
    {
        Loc.Load();
        Text = "Witcher 3 Dynamic Map";
        Font = Theme.Font(9);
        Width = 1500;
        Height = 900;
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Window;

        // --- sidebar: a column of cards ---
        var side = new Panel { Dock = DockStyle.Left, Width = 324, Padding = new Padding(12, 12, 6, 12), BackColor = Theme.Window };

        var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Window };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));   // title + status
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));  // Geralt
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));  // quest
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // POI types
        column.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));  // switches + button

        Card NewCard(int bottomGap) => new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, bottomGap),
            Padding = new Padding(16, 12, 16, 12)
        };

        Label NewLabel(float size, FontStyle style, Color color, int height) => new()
        {
            Height = height, Font = Theme.Font(size, style), ForeColor = color, AutoEllipsis = true
        };

        // title + connection status
        var header = NewCard(8);
        var title = NewLabel(13, FontStyle.Bold, Theme.Text, 28);
        title.Text = "Witcher 3 Map";
        status = new StatusLabel { Text = "" };
        status.Set(Loc.T("status.starting"), Theme.Warn);
        Theme.Stack(header, title, status);

        // language: English by default, the choice is remembered
        var langSwitch = new LanguageSwitch { Code = Loc.Language };
        langSwitch.SelectionChanged += (_, _) => Loc.Set(langSwitch.Code);
        header.Controls.Add(langSwitch);
        langSwitch.BringToFront();
        void PlaceLang() => langSwitch.Location = new Point(header.Width - langSwitch.Width - 14, 14);
        header.Resize += (_, _) => PlaceLang();
        PlaceLang();
        column.Controls.Add(header, 0, 0);

        // Geralt: world and coordinates
        var player = NewCard(8);
        var caption1 = NewLabel(8.5f, FontStyle.Bold, Theme.TextDim, 20);
        caption1.Text = Loc.T("card.geralt");
        regionLabel = NewLabel(12, FontStyle.Bold, Theme.Accent, 28);
        regionLabel.Text = Theme.RegionTitle(currentRegion);
        coords = new Label { Height = 24, Font = Theme.Mono(10), ForeColor = Theme.Text, Text = "X ---  Y ---  Z ---" };
        Theme.Stack(player, caption1, regionLabel, coords);
        column.Controls.Add(player, 0, 1);

        // tracked quest from the game
        var quest = NewCard(8);
        var caption2 = NewLabel(8.5f, FontStyle.Bold, Theme.TextDim, 20);
        caption2.Text = Loc.T("card.quest");
        questLabel = new Label { Height = 42, Font = Theme.Font(10, FontStyle.Bold), ForeColor = Theme.Text, Text = Loc.T("quest.noData") };
        statsLabel = NewLabel(9, FontStyle.Regular, Theme.TextDim, 20);
        Theme.Stack(quest, caption2, questLabel, statsLabel);
        column.Controls.Add(quest, 0, 2);

        // POI types with icons
        var types = NewCard(8);
        var typesHeader = new Panel { Height = 30 };
        var typesTitle = NewLabel(10, FontStyle.Bold, Theme.Text, 30);
        typesTitle.Text = Loc.T("poi.title");
        typesTitle.Dock = DockStyle.Left;
        typesTitle.AutoSize = true;
        typesTitle.TextAlign = ContentAlignment.MiddleLeft;
        Label Link(string text)
        {
            var l = new Label
            {
                Text = text, AutoSize = true, Dock = DockStyle.Right, Cursor = Cursors.Hand,
                ForeColor = Theme.Accent, Font = Theme.Font(9), TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(8, 0, 0, 0)
            };
            return l;
        }
        var linkNone = Link(Loc.T("poi.none"));
        var linkAll = Link(Loc.T("poi.all"));
        linkAll.Click += (_, _) => filters!.SetAll(true);
        linkNone.Click += (_, _) => filters!.SetAll(false);
        typesHeader.Controls.Add(linkNone);
        typesHeader.Controls.Add(linkAll);
        typesHeader.Controls.Add(typesTitle);
        filters = new FilterList { Dock = DockStyle.Fill };
        filters.FilterChanged += (_, _) => ApplyFilter();
        types.Controls.Add(filters);
        types.Controls.Add(typesHeader);
        typesHeader.Dock = DockStyle.Top;
        column.Controls.Add(types, 0, 3);

        // switches and the center button
        var actions = NewCard(0);
        followSwitch = new ToggleSwitch { Text = Loc.T("switch.follow"), Checked = true };
        followSwitch.CheckedChanged += (_, _) =>
        {
            map!.Follow = followSwitch.Checked;
            if (followSwitch.Checked && hasPosition) map.CenterOnWorld(x, y);
        };
        hideVisited = new ToggleSwitch { Text = Loc.T("switch.hideVisited") };
        hideVisited.CheckedChanged += (_, _) => { map!.HideVisited = hideVisited.Checked; map.Invalidate(); };
        var gap = new Panel { Height = 10 };
        var center = new AccentButton { Height = 38, Text = Loc.T("button.center") };
        center.Click += (_, _) =>
        {
            map.CenterOnWorld(x, y);
            followSwitch.Checked = true;
        };
        Theme.Stack(actions, followSwitch, hideVisited, gap, center);
        column.Controls.Add(actions, 0, 4);

        side.Controls.Add(column);

        // --- map ---
        map = new MapControl { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 18) };
        map.SetData(tileMap, poiDb, () => currentRegion, () => hasPosition ? (x, y) : null);
        map.Sync = sync;
        map.Follow = followSwitch.Checked;
        map.UserPanned += (_, _) => followSwitch.Checked = false; // dragging the map by hand pauses following
        tileMap.SetOwner(map);
        filters.IconProvider = type => map.Icons.Get(type);

        Controls.Add(map);
        Controls.Add(side);

        // Hide / show the whole left block (button on the map or Ctrl+B).
        var panelToggle = new PanelToggleButton { Location = new Point(12, 12) };
        void TogglePanel()
        {
            side.Visible = !side.Visible;
            panelToggle.Collapsed = !side.Visible;
            map.Invalidate();
        }
        panelToggle.Click += (_, _) => TogglePanel();
        map.Controls.Add(panelToggle);
        var panelTip = new ToolTip();
        panelTip.SetToolTip(panelToggle, Loc.T("tip.panel"));
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.B) { TogglePanel(); e.Handled = true; }
        };

        // Re-apply all static texts when the language changes.
        Loc.Changed += () =>
        {
            caption1.Text = Loc.T("card.geralt");
            caption2.Text = Loc.T("card.quest");
            typesTitle.Text = Loc.T("poi.title");
            linkAll.Text = Loc.T("poi.all");
            linkNone.Text = Loc.T("poi.none");
            followSwitch.Text = Loc.T("switch.follow");
            hideVisited.Text = Loc.T("switch.hideVisited");
            center.Text = Loc.T("button.center");
            panelTip.SetToolTip(panelToggle, Loc.T("tip.panel"));
            regionLabel.Text = Theme.RegionTitle(currentRegion);
            UpdateSyncTexts();
            RefreshFilters();   // type titles (and their sort order) change with the language
            map.Invalidate();
        };

        Load += (_, _) =>
        {
            timer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30 fps, the marker is smoothed between mod samples
            timer.Tick += (_, _) => UpdatePlayer();
            timer.Start();
            LoadMaps();
        };

        FormClosed += (_, _) =>
        {
            timer?.Stop();
            cts.Cancel();
            tileMap.Dispose();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleWindow(Handle);
    }

    private void LoadMaps()
    {
        tileMap.Reload();
        if (!DataInstaller.HasTiles())
            MessageBox.Show(this, Loc.T("msg.noTiles", DataInstaller.MapDirectory),
                "Witcher 3 Dynamic Map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        map.Invalidate();
    }

    // Rebuilds the type list for the current world; the user's switched-off types are remembered.
    private void RefreshFilters()
    {
        filters.SetItems(poiDb.ForRegion(currentRegion)
            .GroupBy(p => p.Type, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Count())));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        poiDb.SetDisabled(new HashSet<string>(filters.Disabled, StringComparer.OrdinalIgnoreCase));
        map.Invalidate();
    }

    // The world is chosen by the game (modMapSync), there is no manual selection.
    private void SetRegion(string region)
    {
        if (region.Equals(currentRegion, StringComparison.OrdinalIgnoreCase)) return;
        currentRegion = MapWorld.Get(region).Name;
        regionLabel.Text = Theme.RegionTitle(currentRegion);
        RefreshFilters();
        if (hasPosition) map.CenterOnWorld(x, y); else map.ResetView();
        map.Invalidate();
    }

    private void PollSync()
    {
        if (sync.Poll()) ApplySync();
        else if (sync.LastSnapshot is null) UpdateSyncTexts();
    }

    // Quest title and the visited counter; also called when the language changes.
    private void UpdateSyncTexts()
    {
        if (sync.LastSnapshot is null)
        {
            questLabel.Text = Loc.T("quest.noData");
            statsLabel.Text = Loc.T(sync.LogExists ? "quest.needMod" : "quest.noLog");
            return;
        }

        var places = sync.Pins.Where(p => !p.IsQuest).ToList();
        questLabel.Text = string.IsNullOrEmpty(sync.QuestTitle) ? Loc.T("quest.none") : sync.QuestTitle;
        statsLabel.Text = Loc.T("quest.visited", places.Count(p => p.Discovered), places.Count);
    }

    private void ApplySync()
    {
        var region = sync.Region;
        if (region is not null)
        {
            SetRegion(region);

            // The game's own pins (base game + DLC + current patch) replace the static POI database.
            // Quest pins are drawn separately as icons.
            poiDb.ReplaceRegion(region, sync.Pins.Where(p => !p.IsQuest).Select(p => new Poi
            {
                Region = region, Type = p.Type, Name = p.Type, X = p.X, Y = p.Y, Z = p.Z, Visited = p.Discovered
            }));
            RefreshFilters();
        }

        UpdateSyncTexts();
        map.Invalidate();
    }

    private void UpdatePlayer()
    {
        // The position comes from the mod via the log (no game memory access).
        if (++syncTick % 2 == 0) PollSync();
        CheckGameProcess();

        if (!gameRunning)
        {
            if (hasPosition) { hasPosition = false; map.Invalidate(); }
            SetStatus("status.notRunning", Theme.Warn);
            return;
        }

        // The mod writes while the player moves and once a second when standing still.
        if (!sync.TryGetPosition(TimeSpan.FromSeconds(3), out var p))
        {
            SetStatus(sync.LogExists ? "status.waiting" : "status.noLog", Theme.Warn);
            return;
        }
        if (Math.Abs(p.X) > 10000 || Math.Abs(p.Y) > 10000) return;

        SetStatus("status.connected", Theme.Good);
        targetX = p.X; targetY = p.Y;

        double nx, ny;
        if (!hasPosition || Math.Abs(targetX - x) + Math.Abs(targetY - y) > 150)
        {
            // First sample or a jump (fast travel, loading): no gliding across the map.
            nx = targetX; ny = targetY;
        }
        else
        {
            // Samples arrive every ~100 ms; the marker glides toward the latest one.
            nx = x + (targetX - x) * 0.3;
            ny = y + (targetY - y) * 0.3;
            if (Math.Abs(targetX - nx) < 0.02) nx = targetX;
            if (Math.Abs(targetY - ny) < 0.02) ny = targetY;
        }

        bool changed = !hasPosition || nx != x || ny != y || p.Z != z;
        x = nx; y = ny; z = p.Z;
        hasPosition = true;
        if (!changed) return;

        // Also follows fast travel: the position jumps and the camera jumps with it.
        if (followSwitch.Checked || !map.HasCentered) map.CenterOnWorld(x, y);
        coords.Text = $"X {x,7:F1}  Y {y,7:F1}  Z {z,5:F1}";
        map.Invalidate();
    }

    // Only used for the status text: is witcher3.exe running at all? Checked every 2 seconds.
    private void CheckGameProcess()
    {
        if (Environment.TickCount64 - lastProcessCheck < 2000) return;
        lastProcessCheck = Environment.TickCount64;
        var ps = Process.GetProcessesByName(ProcessName);
        gameRunning = ps.Length > 0;
        foreach (var p in ps) p.Dispose();
    }

    // key: a Loc key; the text is looked up here, so a language change shows up on the next update.
    private void SetStatus(string key, Color color)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(key, color)); return; }
        status.Set(Loc.T(key), color);
    }
}

public sealed class MapControl : Control
{
    private TileMap? tiles;
    private PoiDatabase? poi;
    private Func<string>? worldName;
    private Func<(double x, double y)?>? player;

    private readonly PoiIcons icons = new();
    /// <summary>Icons shared with the POI type list.</summary>
    public PoiIcons Icons => icons;
    private readonly ImageAttributes tileAttrs = new();
    private readonly ImageAttributes fadeAttrs = new();

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public GameSync? Sync { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool HideVisited { get; set; }

    /// <summary>The camera follows the player; zooming then keeps the screen centre.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Follow { get; set; }

    public event EventHandler? UserPanned;

    public bool HasCentered => centered;

    private float zoom = 1.0f;
    private double centerX;
    private double centerY;
    private bool centered;
    private Point lastMouse;
    private Point mousePos;
    private bool dragging;
    private int lastTileZ;

    // Compact info card in the top-right corner.
    private Rectangle HudRect => new(Math.Max(8, ClientSize.Width - 244 - 12), 12, 244, 60);

    public MapControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        tileAttrs.SetWrapMode(WrapMode.TileFlipXY); // no seams between tiles
        fadeAttrs.SetColorMatrix(new ColorMatrix { Matrix33 = 0.35f }); // visited POI: 35% opacity
        MouseWheel += OnWheel;
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += OnUp;
        MouseEnter += (_, _) => Focus();
        Resize += (_, _) => Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { icons.Dispose(); tileAttrs.Dispose(); fadeAttrs.Dispose(); }        base.Dispose(disposing);
    }

    public void SetData(TileMap tileMap, PoiDatabase database, Func<string> currentWorld, Func<(double x, double y)?> getPlayer)
    {
        tiles = tileMap; poi = database; worldName = currentWorld; player = getPlayer;
        centerX = 0; centerY = 0;
    }

    public void CenterOnWorld(double x, double y)
    {
        centerX = x; centerY = y; centered = true; Invalidate();
    }

    public void ResetView()
    {
        centered = false; zoom = 1f; Invalidate();
    }

    // Screen pixels per game unit on each axis. The map image is not square,
    // so the square game world is stretched over the real image rectangle
    // (this is what the original map data does too).
    private (double x, double y) Scales(MapWorld world)
    {
        double cw = Math.Max(1, ClientSize.Width), ch = Math.Max(1, ClientSize.Height);
        var lay = tiles?.GetLayout(world);
        if (lay is null)
        {
            double s = Math.Min(cw, ch) / Math.Max(world.SizeX, world.SizeY) * zoom;
            return (s, s);
        }
        double k = zoom * Math.Min(cw / lay.RasterW, ch / lay.RasterH); // screen px per image px
        return (k * lay.RasterW / world.SizeX, k * lay.RasterH / world.SizeY);
    }

    private (double x, double y) GetCenter(MapWorld world)
        => centered ? (centerX, centerY) : (world.MinX + world.SizeX / 2, world.MinY + world.SizeY / 2);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (tiles is null || worldName is null || poi is null) return;

        var g = e.Graphics;
        var world = MapWorld.Get(worldName());
        var sc = Scales(world);
        var center = GetCenter(world);

        g.Clear(Color.FromArgb(18, 18, 18));
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.SmoothingMode = SmoothingMode.None;

        DrawTiles(g, world, sc, center);
        DrawPois(g, world, sc, center);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var p = player?.Invoke();
        if (p is not null) DrawPlayer(g, world, sc, center, p.Value);
        DrawHud(g, world);
    }

    private void DrawTiles(Graphics g, MapWorld world, (double x, double y) sc, (double x, double y) center)
    {
        var layout = tiles!.GetLayout(world);
        if (layout is null) return;

        // Screen pixels per image pixel at the most detailed zoom level.
        double k = sc.x * world.SizeX / layout.RasterW;
        int z = (int)Math.Clamp(Math.Round(layout.MaxZ + Math.Log2(k)), layout.MinZ, layout.MaxZ);
        lastTileZ = z;

        // Image size at zoom z (taken from the tile files on disk, width and height separately).
        double f = Math.Pow(2, layout.MaxZ - z);
        double wz = layout.RasterW / f, hz = layout.RasterH / f;
        double kz = k * f;                                         // screen px per image px at zoom z

        double pxC = (center.x - world.MinX) / world.SizeX * wz;    // from the left
        double pybC = (center.y - world.MinY) / world.SizeY * hz;   // from the BOTTOM (TMS)

        double halfW = ClientSize.Width / 2.0 / kz;
        double halfH = ClientSize.Height / 2.0 / kz;

        int cols = (int)Math.Ceiling(wz / 256.0);
        int rows = (int)Math.Ceiling(hz / 256.0);

        int tx0 = Math.Max(0, (int)Math.Floor((pxC - halfW) / 256.0));
        int tx1 = Math.Min(cols - 1, (int)Math.Floor((pxC + halfW) / 256.0));
        int r0 = Math.Max(0, (int)Math.Floor((pybC - halfH) / 256.0));
        int r1 = Math.Min(rows - 1, (int)Math.Floor((pybC + halfH) / 256.0));

        float size = (float)(256.0 * kz);

        for (int tx = tx0; tx <= tx1; tx++)
            for (int r = r0; r <= r1; r++)
            {
                float dx = (float)((tx * 256.0 - pxC) * kz + ClientSize.Width / 2.0);
                float dy = (float)((pybC - (r + 1) * 256.0) * kz + ClientSize.Height / 2.0);
                var dest = new RectangleF(dx, dy, size + 0.5f, size + 0.5f);

                var bmp = tiles.GetOrQueue(world, z, tx, r);
                if (bmp is not null)
                {
                    DrawTileImage(g, bmp, dest, new RectangleF(0, 0, bmp.Width, bmp.Height));
                    continue;
                }

                // Tile is still loading: show a blurry part of a lower-zoom tile meanwhile.
                for (int j = 1; j <= 4 && z - j >= layout.MinZ; j++)
                {
                    var parent = tiles.TryGetCached(world, z - j, tx >> j, r >> j);
                    if (parent is null) continue;

                    float part = parent.Width / (float)(1 << j);
                    float sx = (tx - ((tx >> j) << j)) * part;
                    float rowInParent = (r - ((r >> j) << j)) * part;
                    float sy = parent.Height - rowInParent - part;
                    DrawTileImage(g, parent, dest, new RectangleF(sx, sy, part, part));
                    break;
                }
            }
    }

    private readonly PointF[] tilePoints = new PointF[3];

    // DrawImage has no (RectangleF + ImageAttributes) overload, so a 3-point
    // parallelogram (upper-left, upper-right, lower-left) is used instead.
    private void DrawTileImage(Graphics g, Bitmap bmp, RectangleF dest, RectangleF src)
    {
        tilePoints[0] = new PointF(dest.Left, dest.Top);
        tilePoints[1] = new PointF(dest.Right, dest.Top);
        tilePoints[2] = new PointF(dest.Left, dest.Bottom);
        g.DrawImage(bmp, tilePoints, src, GraphicsUnit.Pixel, tileAttrs);
    }

    private (double x, double y) ScreenToMap(MapWorld world, Point p)
    {
        var c = GetCenter(world);
        var sc = Scales(world);
        return (c.x + (p.X - ClientSize.Width / 2.0) / sc.x,
                c.y - (p.Y - ClientSize.Height / 2.0) / sc.y);
    }

    private PointF WorldToScreen(double wx, double wy, (double x, double y) sc, (double x, double y) center)
    {
        return new PointF(
            (float)((wx - center.x) * sc.x + ClientSize.Width / 2.0),
            (float)((center.y - wy) * sc.y + ClientSize.Height / 2.0));
    }

    private void DrawPois(Graphics g, MapWorld world, (double x, double y) sc, (double x, double y) center)
    {
        if (poi is null) return;

        float size = Math.Clamp(12f + 4f * MathF.Log2(zoom + 1f), 12f, 30f);
        float half = size / 2f;
        int w = ClientSize.Width, h = ClientSize.Height;

        foreach (var p in poi.ForRegion(world.Name))
        {
            if (!p.Enabled) continue;
            if (p.Visited && HideVisited) continue;

            var s = WorldToScreen(p.X, p.Y, sc, center);
            if (s.X < -size || s.X > w + size || s.Y < -size || s.Y > h + size) continue;

            var icon = icons.Get(p.Type);
            if (p.Visited)
                g.DrawImage(icon, new Rectangle((int)(s.X - half), (int)(s.Y - half), (int)size, (int)size),
                    0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, fadeAttrs);
            else
                g.DrawImage(icon, s.X - half, s.Y - half, size, size);
        }

        DrawQuestPins(g, world, sc, center);
    }

    // Quest pins come straight from the game (modMapSync), drawn as gold diamonds.
    private void DrawQuestPins(Graphics g, MapWorld world, (double x, double y) sc, (double x, double y) center)
    {
        var sync = Sync;
        if (sync is null || !string.Equals(sync.Region, world.Name, StringComparison.OrdinalIgnoreCase)) return;

        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.Gold);
        using var edge = new Pen(Color.Black, 2f);
        float qHalf = Math.Clamp(16f + 4f * MathF.Log2(zoom + 1f), 16f, 34f) / 2f; // quest icons a bit larger than POI
        foreach (var q in sync.Pins)
        {
            if (!q.IsQuest) continue;
            var s = WorldToScreen(q.X, q.Y, sc, center);
            if (s.X < -20 || s.X > ClientSize.Width + 20 || s.Y < -20 || s.Y > ClientSize.Height + 20) continue;

            var icon = icons.GetOrNull(q.Type);
            if (icon is not null)
            {
                g.DrawImage(icon, s.X - qHalf, s.Y - qHalf, qHalf * 2, qHalf * 2);
                continue;
            }

            var pts = new[] { new PointF(s.X, s.Y - 9), new PointF(s.X + 9, s.Y), new PointF(s.X, s.Y + 9), new PointF(s.X - 9, s.Y) };
            g.FillPolygon(fill, pts);
            g.DrawPolygon(edge, pts);
        }
        g.SmoothingMode = old;
    }

    private void DrawPlayer(Graphics g, MapWorld world, (double x, double y) sc, (double x, double y) center, (double x, double y) pos)
    {
        var s = WorldToScreen(pos.x, pos.y, sc, center);
        using var outer = new SolidBrush(Color.FromArgb(70, 255, 30, 30));
        g.FillEllipse(outer, s.X - 14, s.Y - 14, 28, 28);
        using var inner = new SolidBrush(Color.Red);
        g.FillEllipse(inner, s.X - 6, s.Y - 6, 12, 12);
        using var pen = new Pen(Color.White, 2);
        g.DrawEllipse(pen, s.X - 6, s.Y - 6, 12, 12);
    }

    private void DrawHud(Graphics g, MapWorld world)
    {
        var mp = ScreenToMap(world, mousePos);
        var hud = HudRect;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedRect(new Rectangle(hud.X, hud.Y, hud.Width - 1, hud.Height - 1), 10))
        {
            using var bg = new SolidBrush(Color.FromArgb(215, 32, 32, 32));
            using var border = new Pen(Color.FromArgb(70, 255, 255, 255));
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var titleFont = Theme.Font(10, FontStyle.Bold);
        using var smallFont = Theme.Font(9);
        using var monoFont = Theme.Mono(9.5f);
        TextRenderer.DrawText(g, Theme.RegionTitle(world.Name), titleFont, new Point(hud.X + 12, hud.Y + 8), Theme.Text);
        TextRenderer.DrawText(g, $"{zoom:F2}×", smallFont,
            new Rectangle(hud.X, hud.Y + 8, hud.Width - 12, 20), Theme.TextDim, TextFormatFlags.Right);
        TextRenderer.DrawText(g, $"X {mp.x,8:F1}   Y {mp.y,8:F1}", monoFont, new Point(hud.X + 12, hud.Y + 33), Theme.TextDim);
    }

    private void OnWheel(object? sender, MouseEventArgs e)
    {
        if (worldName is null) return;
        var world = MapWorld.Get(worldName());

        var oldSc = Scales(world);
        var c = GetCenter(world);
        double cw = ClientSize.Width / 2.0, ch = ClientSize.Height / 2.0;

        // World point under the cursor stays under the cursor after zooming.
        double wx = c.x + (e.X - cw) / oldSc.x;
        double wy = c.y - (e.Y - ch) / oldSc.y;

        zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.25f : 0.8f), 0.25f, 16f);

        if (!Follow)
        {
            var newSc = Scales(world);
            centerX = wx - (e.X - cw) / newSc.x;
            centerY = wy + (e.Y - ch) / newSc.y;
            centered = true;
        }
        Invalidate();
    }

    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragging = true; lastMouse = e.Location;
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        mousePos = e.Location;
        if (!dragging)
        {
            Invalidate(HudRect); // only the HUD with coordinates
            return;
        }
        if (tiles is null || worldName is null) return;
        var w = MapWorld.Get(worldName());
        var sc = Scales(w);
        if (sc.x <= 0 || sc.y <= 0) return;

        // Materialise the default center so the first drag does not jump.
        var c = GetCenter(w);
        centerX = c.x - (e.X - lastMouse.X) / sc.x;
        centerY = c.y + (e.Y - lastMouse.Y) / sc.y;
        centered = true;
        lastMouse = e.Location;
        Invalidate();
        UserPanned?.Invoke(this, EventArgs.Empty);
    }

    private void OnUp(object? sender, MouseEventArgs e) => dragging = false;
}

/// <summary>
/// POI icons. A PNG named like the POI type (e.g. roadsign.png) in
/// %LocalAppData%\Witcher3DynamicMap\Data\Icons is used when present;
/// otherwise a built-in icon is rendered once and cached.
/// </summary>
public sealed class PoiIcons : IDisposable
{
    private const int IconPx = 48;
    private readonly Dictionary<string, Bitmap> cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roadsign"] = "\u27A4",
        ["harbor"] = "\u2693",
        ["monsternest"] = "\u2620",
        ["placeofpower"] = "\u2726",
        ["treasurehuntmappin"] = "\u25C6",
        ["bossandtreasure"] = "\u2620",
        ["banditcamp"] = "\u2694",
        ["noticeboard"] = "!",
        ["entrance"] = "\u2302",
        ["contraband"] = "\u2691",
        ["infestedvineyard"] = "\u2623",
        ["rescuingtown"] = "\u271A",
        ["blacksmith"] = "\u2692",
        ["armorer"] = "\u2692",
        ["herbalist"] = "\u273F",
        ["alchemytable"] = "\u2697",
        ["boat"] = "\u26f5",
        ["teleport"] = "\u21af",
        ["whetstone"] = "\u2736",
        ["magiclamp"] = "\u2600",
        ["rift"] = "\u2748",
        ["banditcampfire"] = "\u2694",
        ["dungeoncrawl"] = "\u2302",
        ["spoilsofwar"] = "\u2691",
        ["playerstash"] = "\u25a3",
        ["herb"] = "\u273f",
        ["armorrepairtable"] = "\u2692",
    };

    // Game pin type (also the lowercase types of the static POI database) -> file name in the Icons folder
    // (the icon set of the witcher3map project). Types without a match use the generic "poi" icon.
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RoadSign"] = "signpost",
        ["Boat"] = "boat",
        ["TreasureHuntMappin"] = "treasure",
        ["BanditCampfire"] = "banditcamp",
        ["BanditCamp"] = "banditcamp",
        ["BossAndTreasure"] = "guarded",
        ["MonsterNest"] = "monsternest",
        ["Entrance"] = "entrance",
        ["Teleport"] = "fasttravel",
        ["NoticeBoard"] = "notice",
        ["Whetstone"] = "grindstone",
        ["ArmorRepairTable"] = "armourerstable",
        ["Harbor"] = "harbor",
        ["PlaceOfPower"] = "pop",
        ["SpoilsOfWar"] = "spoils",
        ["Contraband"] = "smugglers",
        ["Shopkeeper"] = "shopkeeper",
        ["Enchanter"] = "shopkeeper",
        ["Herb"] = "herbalist",
        ["Herbalist"] = "herbalist",
        ["Alchemic"] = "alchemy",
        ["AlchemyTable"] = "alchemy",
        ["Innkeeper"] = "innkeep",
        ["Blacksmith"] = "blacksmith",
        ["Armorer"] = "armourer",
        ["InfestedVineyard"] = "vineyardinfestation",
        ["RescuingTown"] = "abandoned",
        ["DungeonCrawl"] = "monsterden",
        ["MagicLamp"] = "poi",
        ["Rift"] = "poi",
        ["PlayerStash"] = "poi",
        ["ChapterQuest"] = "quest",
        ["StoryQuest"] = "quest",
        ["SideQuest"] = "sidequests",
        ["MonsterQuest"] = "sidequests",
        ["TreasureQuest"] = "sidequests",
        ["QuestAvailable"] = "sidequests",
    };

    private readonly Dictionary<string, Bitmap?> fileCache = new(StringComparer.OrdinalIgnoreCase);

    public Bitmap Get(string type)
    {
        if (cache.TryGetValue(type, out var bmp)) return bmp;
        bmp = GetOrNull(type) ?? Render(type);
        cache[type] = bmp;
        return bmp;
    }

    /// <summary>The icon from the Icons folder, or null when there is no file for this type.</summary>
    public Bitmap? GetOrNull(string type)
    {
        if (fileCache.TryGetValue(type, out var bmp)) return bmp;
        bmp = LoadFile(Files.TryGetValue(type, out var file) ? file : type);
        fileCache[type] = bmp;
        return bmp;
    }

    private static Bitmap? LoadFile(string name)
    {
        try
        {
            foreach (var dir in new[] { DataInstaller.IconDirectory })
                foreach (var ext in new[] { ".png", ".webp", ".jpg" })
                {
                    var path = Path.Combine(dir, name + ext);
                    if (!File.Exists(path)) continue;

                    using var ms = new MemoryStream(File.ReadAllBytes(path));
                    using var src = Image.FromStream(ms);
                    // The icons are not square: keep the aspect ratio, centred on a transparent square.
                    var dst = new Bitmap(IconPx, IconPx, PixelFormat.Format32bppPArgb);
                    using var g = Graphics.FromImage(dst);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float k = Math.Min((float)IconPx / src.Width, (float)IconPx / src.Height);
                    float w = src.Width * k, h = src.Height * k;
                    g.DrawImage(src, (IconPx - w) / 2, (IconPx - h) / 2, w, h);
                    return dst;
                }
        }
        catch { }
        return null;
    }

    private static Bitmap Render(string type)
    {
        var color = PoiColor(type);
        var glyph = Glyphs.TryGetValue(type, out var gl) ? gl : "\u25CF";

        var bmp = new Bitmap(IconPx, IconPx, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var rect = new RectangleF(3, 3, IconPx - 6, IconPx - 6);
        using (var fill = new SolidBrush(Color.FromArgb(235, 22, 22, 22)))
            g.FillEllipse(fill, rect);
        using (var ring = new Pen(color, 3f))
            g.DrawEllipse(ring, rect);

        using var font = new Font("Segoe UI Symbol", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var tb = new SolidBrush(color);
        g.DrawString(glyph, font, tb, new RectangleF(0, 1, IconPx, IconPx), sf);
        return bmp;
    }

    private static Color PoiColor(string type) => type.ToLowerInvariant() switch
    {
        "roadsign" => Color.Gold,
        "harbor" => Color.CornflowerBlue,
        "monsternest" => Color.Firebrick,
        "placeofpower" => Color.MediumPurple,
        "treasurehuntmappin" => Color.Gold,
        "bossandtreasure" => Color.Orange,
        "banditcamp" => Color.OrangeRed,
        "noticeboard" => Color.LightSkyBlue,
        "entrance" => Color.BurlyWood,
        "contraband" => Color.DeepSkyBlue,
        "infestedvineyard" => Color.YellowGreen,
        "rescuingtown" => Color.LimeGreen,
        "blacksmith" => Color.Silver,
        "armorer" => Color.LightGray,
        "herbalist" => Color.Green,
        "alchemytable" => Color.MediumSeaGreen,
        "boat" => Color.CornflowerBlue,
        "teleport" => Color.Violet,
        "whetstone" or "armorrepairtable" => Color.Silver,
        "magiclamp" => Color.Khaki,
        "rift" => Color.MediumOrchid,
        "banditcampfire" => Color.OrangeRed,
        "dungeoncrawl" => Color.BurlyWood,
        "spoilsofwar" => Color.Orange,
        "playerstash" => Color.Gold,
        "herb" => Color.Green,
        _ => Color.White
    };

    public void Dispose()
    {
        // Disposing a Bitmap twice is harmless; Get() puts file icons into both dictionaries.
        foreach (var b in fileCache.Values) b?.Dispose();
        foreach (var b in cache.Values) b.Dispose();
        fileCache.Clear();
        cache.Clear();
    }
}

public sealed class PoiDatabase
{
    private readonly List<Poi> all = new();
    private readonly Dictionary<string, List<Poi>> byRegion = new(StringComparer.OrdinalIgnoreCase);

    public int Count => all.Count;
    public IEnumerable<string> Types => all.Select(p => p.Type).Distinct(StringComparer.OrdinalIgnoreCase);

    public void SetDisabled(HashSet<string> disabledTypes)
    {
        foreach (var p in all) p.Enabled = !disabledTypes.Contains(p.Type);
    }

    public void ReplaceRegion(string region, IEnumerable<Poi> items)
    {
        all.RemoveAll(p => string.Equals(p.Region, region, StringComparison.OrdinalIgnoreCase));
        var list = items.ToList();
        byRegion[region] = list;
        all.AddRange(list);
    }

    public IReadOnlyList<Poi> ForRegion(string region)
        => byRegion.TryGetValue(region, out var list) ? list : Array.Empty<Poi>();
}

public sealed class Poi
{
    public string Region { get; set; } = "";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Set from the game's discovered map pins (GameSync).</summary>
    public bool Visited { get; set; }
}

/// <summary>Real geometry of a tile pyramid, read from the files on disk.</summary>
public sealed record TileLayout(int MinZ, int MaxZ, double RasterW, double RasterH, int TilesX, int TilesY);

public sealed class TileMap : IDisposable
{
    private const int MaxCached = 450;

    private sealed record CacheEntry(string Key, Bitmap Bitmap);

    // All dictionaries below are touched from the UI thread only.
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> cache = new();
    private readonly LinkedList<CacheEntry> lru = new();
    private readonly HashSet<string> pending = new();
    private readonly HashSet<string> missing = new();
    private readonly Dictionary<string, string?> roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TileLayout?> layouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(4);

    private Control? owner;
    private volatile int generation;
    private bool disposed;

    public void SetOwner(Control c) => owner = c;

    public TileLayout? GetLayout(MapWorld w)
    {
        if (layouts.TryGetValue(w.TileFolder, out var cached)) return cached;
        var root = GetRoot(w);
        TileLayout? layout = null;
        if (root is not null)
        {
            try { layout = BuildLayout(root); } catch { }
        }
        // Negative results are cached too (cleared by Reload after a download),
        // so we do not rescan the disk on every repaint.
        layouts[w.TileFolder] = layout;
        return layout;
    }

    private static TileLayout? BuildLayout(string root)
    {
        var zooms = Directory.EnumerateDirectories(root)
            .Select(d => int.TryParse(Path.GetFileName(d), out var v) ? v : -1)
            .Where(v => v >= 0)
            .OrderBy(v => v)
            .ToList();
        if (zooms.Count == 0) return null;

        int minZ = zooms[0], maxZ = zooms[^1];
        int tilesX = 0, tilesY = 0;

        foreach (var xd in Directory.EnumerateDirectories(Path.Combine(root, maxZ.ToString())))
        {
            if (!int.TryParse(Path.GetFileName(xd), out var xi)) continue;
            tilesX = Math.Max(tilesX, xi + 1);
            foreach (var f in Directory.EnumerateFiles(xd))
                if (int.TryParse(Path.GetFileNameWithoutExtension(f), out var yi))
                    tilesY = Math.Max(tilesY, yi + 1);
        }
        if (tilesX == 0 || tilesY == 0) return null;

        double rasterW = tilesX * 256.0, rasterH = tilesY * 256.0;

        // Refine with tilemapresource.xml (exact raster size, not rounded to a whole tile).
        try
        {
            var xmlPath = Path.Combine(root, "tilemapresource.xml");
            if (File.Exists(xmlPath))
            {
                var doc = XDocument.Load(xmlPath);
                var bb = doc.Descendants("BoundingBox").FirstOrDefault();
                var top = doc.Descendants("TileSet").FirstOrDefault(t => (int?)t.Attribute("order") == maxZ);
                if (bb is not null && top is not null)
                {
                    double upp = double.Parse((string)top.Attribute("units-per-pixel")!, CultureInfo.InvariantCulture);
                    double Get(string n) => double.Parse((string)bb.Attribute(n)!, CultureInfo.InvariantCulture);
                    double w = Math.Abs(Get("maxx") - Get("minx"));
                    double h = Math.Abs(Get("maxy") - Get("miny"));
                    double rw = Math.Round(w / upp), rh = Math.Round(h / upp);
                    if (rw > rasterW - 256 && rw <= rasterW) rasterW = rw;
                    if (rh > rasterH - 256 && rh <= rasterH) rasterH = rh;
                }
            }
        }
        catch { }

        return new TileLayout(minZ, maxZ, rasterW, rasterH, tilesX, tilesY);
    }

    public Bitmap? TryGetCached(MapWorld w, int z, int x, int y)
    {
        if (!cache.TryGetValue(Key(w, z, x, y), out var node)) return null;
        lru.Remove(node);
        lru.AddFirst(node);
        return node.Value.Bitmap;
    }

    /// <summary>Returns the tile if it is in memory; otherwise loads it in the background.</summary>
    public Bitmap? GetOrQueue(MapWorld w, int z, int x, int y)
    {
        var bmp = TryGetCached(w, z, x, y);
        if (bmp is not null) return bmp;
        Queue(w, z, x, y);
        return null;
    }

    private void Queue(MapWorld w, int z, int x, int y)
    {
        var key = Key(w, z, x, y);
        if (pending.Contains(key) || missing.Contains(key)) return;
        var root = GetRoot(w);
        if (root is null) return;

        pending.Add(key);
        int gen = generation;

        Task.Run(async () =>
        {
            Bitmap? bmp = null;
            await gate.WaitAsync();
            try { if (gen == generation) bmp = LoadBitmap(root, z, x, y); }
            catch { bmp = null; }
            finally { gate.Release(); }

            var o = owner;
            if (o is null || o.IsDisposed) { bmp?.Dispose(); return; }
            try { o.BeginInvoke(() => Complete(key, gen, bmp)); }
            catch { bmp?.Dispose(); }
        });
    }

    private void Complete(string key, int gen, Bitmap? bmp)
    {
        pending.Remove(key);
        if (disposed || gen != generation) { bmp?.Dispose(); return; }
        if (bmp is null) { missing.Add(key); return; }

        cache[key] = lru.AddFirst(new CacheEntry(key, bmp));
        while (lru.Count > MaxCached && lru.Last is { } last)
        {
            lru.RemoveLast();
            cache.Remove(last.Value.Key);
            last.Value.Bitmap.Dispose();
        }
        owner?.Invalidate();
    }

    private static Bitmap? LoadBitmap(string root, int z, int x, int y)
    {
        var path = FindTile(root, z, x, y);
        if (path is null) return null;

        using var ms = new MemoryStream(File.ReadAllBytes(path));
        using var src = Image.FromStream(ms);
        // Premultiplied ARGB is the fastest format for GDI+ to draw.
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(dst);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(src, 0, 0, src.Width, src.Height);
        return dst;
    }

    private static string Key(MapWorld w, int z, int x, int y) => $"{w.TileFolder}:{z}:{x}:{y}";

    private string? GetRoot(MapWorld w)
    {
        if (roots.TryGetValue(w.TileFolder, out var cached)) return cached;
        var baseDir = DataInstaller.MapDirectory;
        string? dir = null;
        if (Directory.Exists(baseDir))
            dir = Directory.EnumerateDirectories(baseDir, w.TileFolder, SearchOption.AllDirectories).FirstOrDefault();
        roots[w.TileFolder] = dir;
        return dir;
    }

    private static string? FindTile(string root, int z, int x, int y)
    {
        var direct = Path.Combine(root, z.ToString(), x.ToString(), y + ".png");
        if (File.Exists(direct)) return direct;
        var jpg = Path.Combine(root, z.ToString(), x.ToString(), y + ".jpg");
        return File.Exists(jpg) ? jpg : null;
    }

    public void Reload()
    {
        generation++;
        foreach (var e in lru) e.Bitmap.Dispose();
        lru.Clear();
        cache.Clear();
        pending.Clear();
        missing.Clear();
        roots.Clear();
        layouts.Clear();
    }

    public void Dispose()
    {
        disposed = true;
        Reload();
    }
}

public sealed class MapWorld
{
    public string Name { get; init; } = "";
    public string TileFolder { get; init; } = "";
    public double MinX { get; init; }
    public double MinY { get; init; }
    public double SizeX { get; init; }
    public double SizeY { get; init; }
    /// <summary>Shortcut for a square world.</summary>
    public double Size { init { SizeX = value; SizeY = value; } }
    public double MaxX => MinX + SizeX;
    public double MaxY => MinY + SizeY;

    // The game rectangle that the whole tile image covers (game units, Y up, tile row 0 at the bottom).
    // Velen is the Hearts of Stone map from witcher3map-maps (hos_velen); its bounds were fitted by
    // matching the game's own road sign pins against the signposts of that map (rms < 8 game units).
    public static readonly MapWorld[] All =
    {
        new() { Name="Velen", TileFolder="hos_velen", MinX=-1073.27, MinY=-1612.09, SizeX=4310.06, SizeY=4846.08 },
        new() { Name="Skellige", TileFolder="skellige", MinX=-3736, MinY=-3736, Size=7472 },
        new() { Name="Kaer Morhen", TileFolder="kaer_morhen", MinX=-4000, MinY=-4000, Size=8000 },
        new() { Name="Toussaint", TileFolder="toussaint", MinX=-4000, MinY=-4000, Size=8000 },
        new() { Name="White Orchard", TileFolder="white_orchard", MinX=-1000, MinY=-1000, Size=2000 }
    };

    public static MapWorld Get(string name) => All.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static MapWorld Resolve(double x, double y)
    {
        // Prefer the small worlds if the coordinate clearly fits them.
        foreach (var w in All.Where(w => w.Name != "Velen"))
            if (x >= w.MinX && x <= w.MaxX && y >= w.MinY && y <= w.MaxY)
                return w;
        return All[0];
    }
}

public static class DataInstaller
{
    // The map tiles are shipped with the app: Data\Maps next to the exe. A "Data" folder counts only if it
    // really contains tiles (an empty folder is skipped). Candidates, in order:
    //   1. Data next to the exe (the release package)
    //   2. Data in the parent folders, up to 4 levels (running from bin\Release\... inside the project folder)
    //   3. %LocalAppData%\Witcher3DynamicMap\Data (developer machine)
    public static string BaseDirectory { get; } = ResolveBaseDirectory();
    public static string MapDirectory => Path.Combine(BaseDirectory, "Maps");
    public static string IconDirectory => Path.Combine(AppContext.BaseDirectory, "Icons");

    private static string ResolveBaseDirectory()
    {
        var candidates = new List<string>();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && candidates.Count < 5; dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "Data"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Witcher3DynamicMap", "Data"));

        foreach (var c in candidates)
            if (ContainsTiles(Path.Combine(c, "Maps"))) return c;
        return candidates[0]; // nothing found: the "tiles not found" message names this folder
    }

    private static bool ContainsTiles(string mapDir)
        => Directory.Exists(mapDir) &&
           Directory.EnumerateFiles(mapDir, "*.*", SearchOption.AllDirectories)
                    .Any(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                              f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));

    public static bool HasTiles() => ContainsTiles(MapDirectory);
}
