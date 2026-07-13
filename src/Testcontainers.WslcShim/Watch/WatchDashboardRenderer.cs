using System.Globalization;
using Testcontainers.WslcShim.Http.Models;

namespace Testcontainers.WslcShim.Watch;

internal sealed record WatchRenderResult(
    string Frame,
    int ContainerOffset,
    int ContainerMaximumOffset,
    int ContainerPageSize);

internal sealed class WatchDashboardRenderer(TimeProvider? timeProvider = null)
{
    private const string Reset = "\u001b[0m";
    private const string Cyan = "\u001b[36m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Red = "\u001b[31m";
    private const string Dim = "\u001b[2m";
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public WatchRenderResult Render(
        WatchDashboardSnapshot snapshot,
        ShimRuntimeOptions options,
        int width,
        int height,
        int containerOffset,
        bool useColor)
    {
        if (width < 86 || height < 10)
        {
            var message = $"WSLc Docker Shim WATCH — resize terminal to at least 86x10 (now {width}x{height})";
            return new WatchRenderResult(Fit(message, Math.Max(width, 1)), 0, 0, 1);
        }

        var containerPageSize = height - 6;
        var containerLines = BuildContainerLines(snapshot.Containers, width, useColor);
        var containerMaximum = Math.Max(0, containerLines.Count - containerPageSize);
        containerOffset = Math.Clamp(containerOffset, 0, containerMaximum);

        var lines = new List<string>(height)
        {
            Color("WSLc Docker Shim  WATCH", Cyan, useColor),
            Color(
                Fit(
                    $"Docker {FormatEndpoint(options.FullApiAddress, options.FullApiPort)}  |  Ryuk {FormatEndpoint(options.RyukEndpoint.Host, options.RyukEndpoint.Port)}",
                    width),
                Dim,
                useColor),
            string.Empty,
            Color($"CONTAINERS ({snapshot.Containers.Count})", Cyan, useColor),
            BuildContainerHeader(width, useColor)
        };
        lines.AddRange(SliceAndPad(containerLines, containerOffset, containerPageSize, width));
        lines.Add(Color("↑/↓ scroll  PgUp/PgDn page  Home/End jump  Ctrl+C stop", Dim, useColor));

        return new WatchRenderResult(
            string.Join(Environment.NewLine, lines.Take(height)),
            containerOffset,
            containerMaximum,
            containerPageSize);
    }

    private IReadOnlyList<string> BuildContainerLines(
        IReadOnlyList<WatchContainerSnapshot> containers,
        int width,
        bool useColor)
    {
        if (containers.Count == 0)
        {
            return [Color("<no shim containers>", Dim, useColor)];
        }

        var columns = GetContainerColumns(width);
        return containers.Select(container => Color(
            JoinColumns(
                columns,
                ShortId(container.Id),
                container.Name,
                ShortImage(container.Image),
                FormatAge(clock.GetUtcNow() - container.CreatedAt),
                container.Status,
                container.CpuUsage,
                container.MemoryUsage,
                container.Ports),
            ColorFor(container.Status),
            useColor)).ToArray();
    }

    private static IReadOnlyList<string> SliceAndPad(
        IReadOnlyList<string> lines,
        int offset,
        int count,
        int width)
    {
        var visible = lines.Skip(offset).Take(count).ToList();
        while (visible.Count < count)
        {
            visible.Add(new string(' ', width));
        }

        return visible;
    }

    private static string BuildContainerHeader(int width, bool useColor)
    {
        var columns = GetContainerColumns(width);
        return Color(
            JoinColumns(
                columns,
                "CONTAINER ID",
                "NAME",
                "IMAGE",
                "CREATED",
                "STATUS",
                "CPU %",
                "MEMORY",
                "PORTS"),
            Cyan,
            useColor);
    }

    private static int[] GetContainerColumns(int width)
    {
        const int id = 12;
        const int created = 9;
        const int status = 12;
        const int cpu = 7;
        const int memory = 10;
        const int gaps = 7;
        var flexible = Math.Max(28, width - id - created - status - cpu - memory - gaps);
        var name = Math.Clamp(flexible / 4, 8, 18);
        var image = Math.Clamp(flexible / 3, 10, 28);
        var ports = Math.Max(10, flexible - name - image);
        return [id, name, image, created, status, cpu, memory, ports];
    }

    private static string JoinColumns(IReadOnlyList<int> widths, params string[] values) =>
        string.Join(" ", values.Select((value, index) => Cell(value, widths[index])));

    private static string Cell(string? value, int width) => Fit(Sanitize(value ?? string.Empty), width).PadRight(width);

    private static string Fit(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        return value.Length <= width ? value : width == 1 ? "…" : value[..(width - 1)] + "…";
    }

    private static string Sanitize(string value) =>
        new(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());

    private static string ShortId(string? id) => string.IsNullOrWhiteSpace(id) ? "<pending>" : id[..Math.Min(12, id.Length)];
    private static string ShortImage(string image) => image.Split('@', 2)[0];

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) return "now";
        if (age.TotalSeconds < 60) return $"{Math.Max(0, (int)age.TotalSeconds)}s ago";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

    private static string FormatEndpoint(string host, int port)
    {
        var formattedHost = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return $"tcp://{formattedHost}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ColorFor(string status) => status switch
    {
        "creating" or "starting" or "stopping" or "removing" => Yellow,
        "failed" => Red,
        "running" => Green,
        "removed" or "exited" => Dim,
        _ when status.StartsWith("exited", StringComparison.Ordinal) => Dim,
        _ => Reset
    };

    private static string Color(string value, string color, bool useColor) =>
        useColor && color != Reset ? color + value + Reset : value;
}
