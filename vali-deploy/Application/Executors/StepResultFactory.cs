using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

internal static class StepResultFactory
{
    public static StepResult NoServer(DeployStep step, StepExecutionContext context, TimeSpan duration) =>
        new()
        {
            Step = step, Success = false, ExitCode = -1,
            Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
            Duration = duration
        };

    public static StepResult FromProcessResult(DeployStep step, ProcessRunResult run, TimeSpan duration) =>
        new()
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = duration
        };
}
