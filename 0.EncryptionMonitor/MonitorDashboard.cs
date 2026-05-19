using Spectre.Console;
using Spectre.Console.Rendering;

namespace EncryptionMonitor;

/// <summary>
/// Builds and owns the Spectre.Console live display.
/// Call <see cref="Update"/> from the SignalR handler thread; Spectre's
/// <c>LiveDisplayContext</c> serialises the refresh internally.
/// </summary>
public sealed class MonitorDashboard
{
    // ── Layout panels ─────────────────────────────────────────────────────────
    private readonly Table _metricsTable = BuildMetricsTable();
    private readonly BreakdownChart _gcChart = BuildGcChart();

    // ── State (written by SignalR callback, read by live loop) ────────────────
    private ResourceMetricsSnapshot _latest = new(
        Timestamp: DateTimeOffset.UtcNow,
        CpuPercent: 0, MemoryUsedMb: 0, MemoryLimitMb: 0, MemoryPercent: 0,
        GcHeapMb: 0, GcGen0Collections: 0, GcGen1Collections: 0, GcGen2Collections: 0,
        ThreadPoolWorkerThreads: 0, UptimeSeconds: 0,
        SignalRConnectionCount: 0, OrleansGrainCount: 0);

    private int _receivedCount = 0;
    private string _statusLine = "[grey]Waiting for first snapshot…[/]";

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the renderable that <see cref="AnsiConsole.Live"/> should wrap.
    /// </summary>
    public IRenderable Root => BuildRoot();

    /// <summary>
    /// Applies a fresh snapshot and rebuilds display data. Thread-safe via
    /// <c>Volatile.Write</c> + <c>Interlocked</c> — no lock needed because
    /// Spectre only renders from the live-loop thread.
    /// </summary>
    public void Update(ResourceMetricsSnapshot snapshot)
    {
        Volatile.Write(ref _latest, snapshot);
        Interlocked.Increment(ref _receivedCount);
        _statusLine = $"[green]Connected[/] · last update [grey]{snapshot.Timestamp:HH:mm:ss.fff} UTC[/]";
    }

    public void SetStatus(string markup) => _statusLine = markup;

    /// <summary>
    /// Rebuilds all renderable state from the latest snapshot and returns
    /// the updated root element for the live context.
    /// </summary>
    public IRenderable Refresh()
    {
        var s = Volatile.Read(ref _latest);

        // ── Metrics table ─────────────────────────────────────────────────
        _metricsTable.Rows.Clear();

        AddRow(_metricsTable, "Timestamp", $"{s.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        AddRow(_metricsTable, "Uptime", FormatUptime(s.UptimeSeconds));
        AddRow(_metricsTable, "─ CPU ─", string.Empty);
        AddRow(_metricsTable, "  CPU", BuildBar(s.CpuPercent, 100, "[red]") + $"  {s.CpuPercent:F1} %");
        AddRow(_metricsTable, "─ Memory ─", string.Empty);
        AddRow(_metricsTable, "  Used", BuildBar(s.MemoryPercent, 100, "[blue]") + $"  {s.MemoryUsedMb:F0} MB / {s.MemoryLimitMb:F0} MB  ({s.MemoryPercent:F1} %)");
        AddRow(_metricsTable, "  GC Heap", $"{s.GcHeapMb:F1} MB");
        AddRow(_metricsTable, "─ GC ─", string.Empty);
        AddRow(_metricsTable, "  Gen 0", $"{s.GcGen0Collections:N0}");
        AddRow(_metricsTable, "  Gen 1", $"{s.GcGen1Collections:N0}");
        AddRow(_metricsTable, "  Gen 2", $"{s.GcGen2Collections:N0}");
        AddRow(_metricsTable, "─ Threads ─", string.Empty);
        AddRow(_metricsTable, "  Workers", $"{s.ThreadPoolWorkerThreads}");
        AddRow(_metricsTable, "─ Cluster ─", string.Empty);
        AddRow(_metricsTable, "  SignalR", $"{s.SignalRConnectionCount} connection{(s.SignalRConnectionCount == 1 ? "" : "s")}");
        AddRow(_metricsTable, "  Orleans", $"{s.OrleansGrainCount:N0} grain activation{(s.OrleansGrainCount == 1 ? "" : "s")}");

        // ── GC breakdown chart ────────────────────────────────────────────
        _gcChart.Data.Clear();
        var total = s.GcGen0Collections + s.GcGen1Collections + s.GcGen2Collections;
        if (total > 0)
        {
            _gcChart.AddItem("Gen 0", (double)s.GcGen0Collections / total * 100, Color.Green);
            _gcChart.AddItem("Gen 1", (double)s.GcGen1Collections / total * 100, Color.Yellow);
            _gcChart.AddItem("Gen 2", (double)s.GcGen2Collections / total * 100, Color.Red);
        }
        else
        {
            _gcChart.AddItem("(no collections yet)", 100, Color.Grey);
        }

        return BuildRoot();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IRenderable BuildRoot()
    {
        var layout = new Layout("root")
            .SplitRows(
                new Layout("header"),
                new Layout("body").SplitColumns(
                    new Layout("metrics"),
                    new Layout("gc")),
                new Layout("footer"));

        layout["header"].Update(
            new Panel(new Markup("[bold yellow] EncryptionProject — Live Resource Monitor[/]"))
                .Border(BoxBorder.Rounded)
                .Padding(0, 0));

        layout["metrics"].Update(
            new Panel(_metricsTable)
                .Header("[bold]Process Metrics[/]")
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));

        layout["gc"].Update(
            new Panel(_gcChart)
                .Header("[bold]GC Collection Distribution[/]")
                .Border(BoxBorder.Rounded)
                .Padding(1, 0));

        layout["footer"].Update(
            new Panel(new Markup(_statusLine + $"  ·  [grey]snapshots received: {_receivedCount}[/]"))
                .Border(BoxBorder.Rounded)
                .Padding(0, 0));

        layout["header"].Size(3);
        layout["footer"].Size(3);

        return layout;
    }

    private static Table BuildMetricsTable()
    {
        var t = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Metric").Width(18))
            .AddColumn(new TableColumn("Value"));
        return t;
    }

    private static BreakdownChart BuildGcChart() =>
        new BreakdownChart().Width(40);

    private static void AddRow(Table table, string label, string value) =>
        table.AddRow(new Markup($"[grey]{Markup.Escape(label)}[/]"), new Markup(value));

    private static string BuildBar(double value, double max, string colour)
    {
        const int width = 20;
        var filled = (int)Math.Round(value / max * width);
        filled = Math.Clamp(filled, 0, width);
        return colour + new string('█', filled) + "[grey]" + new string('░', width - filled) + "[/][/]";
    }

    private static string FormatUptime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
