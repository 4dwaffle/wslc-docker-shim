using Testcontainers.WslcShim.Http.Models;

namespace Testcontainers.WslcShim.Watch;

internal sealed class WatchDashboardService(
    IWatchTerminal terminal,
    WatchDashboardRenderer renderer,
    WatchDashboardState dashboard,
    ShimRuntimeOptions options) : BackgroundService
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(150);
    private int containerOffset;
    private WatchRenderResult? lastRender;
    private string? lastFrame;
    private bool renderRequested = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        terminal.Enter();
        try
        {
            long lastVersion = -1;
            long lastSecond = -1;
            var lastWidth = -1;
            var lastHeight = -1;
            while (!stoppingToken.IsCancellationRequested)
            {
                while (terminal.TryReadKey(out var key))
                {
                    ApplyKey(key);
                }

                var snapshot = dashboard.Snapshot();
                var width = terminal.Width;
                var height = terminal.Height;
                var currentSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var displayedAgesChanged = snapshot.Containers.Count > 0 && currentSecond != lastSecond;
                if (renderRequested ||
                    snapshot.Version != lastVersion ||
                    displayedAgesChanged ||
                    width != lastWidth ||
                    height != lastHeight)
                {
                    lastRender = renderer.Render(
                        snapshot,
                        options,
                        width,
                        height,
                        containerOffset,
                        terminal.UseColor);
                    containerOffset = lastRender.ContainerOffset;
                    if (lastRender.Frame != lastFrame)
                    {
                        terminal.WriteFrame(lastRender.Frame);
                        lastFrame = lastRender.Frame;
                    }

                    lastVersion = snapshot.Version;
                    lastSecond = currentSecond;
                    lastWidth = width;
                    lastHeight = height;
                    renderRequested = false;
                }

                await Task.Delay(RenderInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            terminal.Exit();
        }
    }

    internal void ApplyKey(ConsoleKeyInfo key)
    {
        var result = lastRender;
        if (result is null)
        {
            return;
        }

        var newOffset = MoveOffset(
            containerOffset,
            result.ContainerMaximumOffset,
            result.ContainerPageSize,
            key.Key);
        if (newOffset == containerOffset)
        {
            return;
        }

        containerOffset = newOffset;
        renderRequested = true;
    }

    private static int MoveOffset(int current, int maximum, int pageSize, ConsoleKey key) => key switch
    {
        ConsoleKey.UpArrow => Math.Max(0, current - 1),
        ConsoleKey.DownArrow => Math.Min(maximum, current + 1),
        ConsoleKey.PageUp => Math.Max(0, current - pageSize),
        ConsoleKey.PageDown => Math.Min(maximum, current + pageSize),
        ConsoleKey.Home => 0,
        ConsoleKey.End => maximum,
        _ => current
    };
}
