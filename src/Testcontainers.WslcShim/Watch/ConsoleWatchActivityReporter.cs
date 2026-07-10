using System.Globalization;
using Testcontainers.WslcShim.Http.Enums;
using Testcontainers.WslcShim.Http.Models;

namespace Testcontainers.WslcShim.Watch;

internal sealed class ConsoleWatchActivityReporter(
    TextWriter writer,
    bool useColor,
    TimeProvider? timeProvider = null) : IWatchActivityReporter
{
    private const string Reset = "\u001b[0m";
    private const string Cyan = "\u001b[36m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Red = "\u001b[31m";
    private const string Dim = "\u001b[2m";
    private const int MaxActivityLength = 120;

    private readonly object outputSync = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private long requestSequence;

    public static ConsoleWatchActivityReporter CreateDefault()
    {
        var colorEnabled = !Console.IsOutputRedirected &&
                           string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        return new ConsoleWatchActivityReporter(Console.Out, colorEnabled);
    }

    public string NextRequestId()
    {
        return Interlocked.Increment(ref requestSequence).ToString("D4", CultureInfo.InvariantCulture);
    }

    public void WriteStartup(ShimRuntimeOptions options)
    {
        lock (outputSync)
        {
            writer.WriteLine();
            writer.WriteLine(Color("WSLc Docker Shim  WATCH", Cyan));
            writer.WriteLine($"  Docker API   {FormatEndpoint(options.FullApiAddress, options.FullApiPort)}");
            writer.WriteLine($"  Ryuk bind    {FormatEndpoint(options.RyukBindAddress, options.RyukEndpoint.Port)}");
            writer.WriteLine($"  Ryuk target  {FormatEndpoint(options.RyukEndpoint.Host, options.RyukEndpoint.Port)}");
            writer.WriteLine($"  PowerShell   $env:DOCKER_HOST = \"{FormatEndpoint(options.FullApiAddress, options.FullApiPort)}\"");
            writer.WriteLine(Color("  Watching Docker requests and WSLc operations. Press Ctrl+C to stop.", Dim));
            writer.WriteLine();
        }
    }

    public void WriteStopping()
    {
        WriteLine("----", "APP ", Color("--", Yellow), "stopping");
    }

    public void RequestStarted(string requestId, ShimListenerKind listenerKind, string method, string path)
    {
        var listener = listenerKind == ShimListenerKind.Ryuk ? "RYUK" : "FULL";
        WriteLine(requestId, "HTTP", Color("->", Cyan), $"{listener,-4} {Sanitize(method),-7} {Sanitize(path)}");
    }

    public void RequestCompleted(string requestId, int statusCode, string method, string path, TimeSpan elapsed)
    {
        var color = statusCode switch
        {
            >= 500 => Red,
            >= 400 => Yellow,
            >= 300 => Cyan,
            _ => Green
        };
        WriteLine(
            requestId,
            "HTTP",
            Color("<-", color),
            $"{Color(statusCode.ToString(CultureInfo.InvariantCulture), color),-3} {Sanitize(method),-7} {Sanitize(path)}  {FormatElapsed(elapsed)}");
    }

    public void RequestCancelled(string requestId, string method, string path, TimeSpan elapsed)
    {
        WriteLine(
            requestId,
            "HTTP",
            Color("!!", Yellow),
            $"CANCEL {Sanitize(method),-7} {Sanitize(path)}  {FormatElapsed(elapsed)}");
    }

    public void RequestFailed(string requestId, string method, string path, Exception exception, TimeSpan elapsed)
    {
        WriteLine(
            requestId,
            "HTTP",
            Color("XX", Red),
            $"ERROR {Sanitize(method),-7} {Sanitize(path)}  {Sanitize(exception.GetType().Name)}  {FormatElapsed(elapsed)}");
    }

    public void CommandStarted(string? requestId, string activityName)
    {
        WriteLine(RequestIdOrBackground(requestId), "WSLC", Color("->", Cyan), Sanitize(activityName));
    }

    public void CommandCompleted(string? requestId, string activityName, int exitCode, TimeSpan elapsed)
    {
        var color = exitCode == 0 ? Green : Red;
        var marker = exitCode == 0 ? "OK" : "XX";
        WriteLine(
            RequestIdOrBackground(requestId),
            "WSLC",
            Color(marker, color),
            $"{Sanitize(activityName)}  exit={exitCode.ToString(CultureInfo.InvariantCulture)}  {FormatElapsed(elapsed)}");
    }

    public void CommandCancelled(string? requestId, string activityName, TimeSpan elapsed)
    {
        WriteLine(
            RequestIdOrBackground(requestId),
            "WSLC",
            Color("!!", Yellow),
            $"{Sanitize(activityName)}  cancelled  {FormatElapsed(elapsed)}");
    }

    public void CommandFailed(string? requestId, string activityName, Exception exception, TimeSpan elapsed)
    {
        WriteLine(
            RequestIdOrBackground(requestId),
            "WSLC",
            Color("XX", Red),
            $"{Sanitize(activityName)}  {Sanitize(exception.GetType().Name)}  {FormatElapsed(elapsed)}");
    }

    private void WriteLine(string requestId, string source, string marker, string message)
    {
        lock (outputSync)
        {
            writer.WriteLine($"{clock.GetLocalNow():HH:mm:ss.fff}  {requestId,-4}  {source,-4}  {marker} {message}");
        }
    }

    private string Color(string value, string color)
    {
        return useColor ? color + value + Reset : value;
    }

    private static string RequestIdOrBackground(string? requestId)
    {
        return string.IsNullOrWhiteSpace(requestId) ? "----" : requestId;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms";
    }

    private static string FormatEndpoint(string host, int port)
    {
        var formattedHost = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return $"tcp://{formattedHost}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Sanitize(string value)
    {
        var sanitized = new string(value
            .Select(character => char.IsControl(character) ? ' ' : character)
            .ToArray());
        return sanitized.Length <= MaxActivityLength
            ? sanitized
            : sanitized[..(MaxActivityLength - 3)] + "...";
    }
}
