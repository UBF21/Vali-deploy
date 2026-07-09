using vali_deploy.Domain;

namespace vali_deploy.Application.Executors;

internal static class RemoteStepResultFactory
{
    public static StepResult NoServer(DeployStep step, StepExecutionContext context, TimeSpan duration) =>
        new()
        {
            Step = step, Success = false, ExitCode = -1,
            Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
            Duration = duration
        };
}
