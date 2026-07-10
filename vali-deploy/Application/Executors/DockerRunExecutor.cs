using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

/// <summary>
/// Ejecuta "docker run -it --rm" heredando la consola del proceso padre, para que el usuario pueda
/// interactuar con el contenedor. A diferencia del resto de los IStepExecutor, no depende de
/// IProcessRunner: esa abstracción captura la salida como texto para loguearla, lo cual es incompatible
/// con una sesión interactiva real — usa IInteractiveProcessLauncher en su lugar.
/// </summary>
public class DockerRunExecutor : IStepExecutor
{
    private readonly IInteractiveProcessLauncher _launcher;

    public DockerRunExecutor(IInteractiveProcessLauncher launcher) => _launcher = launcher;

    public StepType Handles => StepType.DockerRun;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var runArgs = step.Args.GetValueOrDefault("RunArgs", "");
        var runArgsSuffix = string.IsNullOrWhiteSpace(runArgs) ? "" : $" {runArgs}";
        var command = $"docker run -it --rm{runArgsSuffix} {imageTag}";

        var exitCode = await _launcher.RunInheritingConsoleAsync(command, context.ProjectPath, new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" });
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = exitCode == 0,
            ExitCode = exitCode,
            Output = "(sesión interactiva — salida no capturada)",
            Duration = stopwatch.Elapsed
        };
    }
}
