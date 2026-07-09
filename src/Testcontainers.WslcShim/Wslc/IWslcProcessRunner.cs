namespace Testcontainers.WslcShim.Wslc;

public interface IWslcProcessRunner
{
    Task<WslcCommandResult> RunAsync(WslcCommand command, CancellationToken cancellationToken);
}
