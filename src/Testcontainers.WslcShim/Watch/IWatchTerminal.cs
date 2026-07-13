namespace Testcontainers.WslcShim.Watch;

internal interface IWatchTerminal
{
    int Width { get; }
    int Height { get; }
    bool UseColor { get; }
    void Enter();
    void WriteFrame(string frame);
    bool TryReadKey(out ConsoleKeyInfo key);
    void Exit();
}

internal sealed class SystemWatchTerminal(TextWriter? writer = null) : IWatchTerminal
{
    private const string EnterAlternateScreen = "\u001b[?1049h\u001b[?25l";
    private const string LeaveAlternateScreen = "\u001b[?25h\u001b[?1049l";
    private const string Reset = "\u001b[0m";
    private const string CursorHome = "\u001b[H";
    private const string ClearScreen = "\u001b[2J";
    private const string EraseLineTail = "\u001b[K";
    private const string EraseScreenTail = "\u001b[J";
    private readonly TextWriter output = writer ?? Console.Out;

    public static bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public int Width => ReadDimension(() => Console.WindowWidth, 120);

    public int Height => ReadDimension(() => Console.WindowHeight, 30);

    public bool UseColor => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    public void Enter()
    {
        output.Write(EnterAlternateScreen + Reset + CursorHome + ClearScreen);
        output.Flush();
    }

    public void WriteFrame(string frame)
    {
        var inPlaceFrame = frame.Replace(
            Environment.NewLine,
            EraseLineTail + Environment.NewLine,
            StringComparison.Ordinal);
        output.Write(Reset + CursorHome + inPlaceFrame + EraseScreenTail);
        output.Flush();
    }

    public bool TryReadKey(out ConsoleKeyInfo key)
    {
        try
        {
            if (Console.KeyAvailable)
            {
                key = Console.ReadKey(intercept: true);
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        key = default;
        return false;
    }

    public void Exit()
    {
        output.Write(Reset + LeaveAlternateScreen);
        output.Flush();
    }

    private static int ReadDimension(Func<int> read, int fallback)
    {
        try
        {
            return read();
        }
        catch (IOException)
        {
            return fallback;
        }
    }
}
