using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class GitCheckoutExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public GitCheckoutExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.GitCheckout;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var branch = ResolveBranch(step, context);

        if (string.IsNullOrWhiteSpace(branch))
        {
            return MissingBranchResult(step, stopwatch.Elapsed);
        }

        var checkoutResult = await _processRunner.RunAsync($"git checkout {branch}", context.ProjectPath);

        if (checkoutResult.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, checkoutResult, checkoutResult.StdOut, stopwatch.Elapsed);
        }

        if (!ShouldSyncBeforeBuild(step))
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = true, ExitCode = 0,
                Output = checkoutResult.StdOut, Duration = stopwatch.Elapsed
            };
        }

        var pullResult = await _processRunner.RunAsync("git pull", context.ProjectPath);
        stopwatch.Stop();

        return BuildResult(step, pullResult, checkoutResult.StdOut + pullResult.StdOut, stopwatch.Elapsed);
    }

    private static string? ResolveBranch(DeployStep step, StepExecutionContext context) =>
        step.Args.GetValueOrDefault("Branch") ?? context.Environment.DefaultBranch;

    private static bool ShouldSyncBeforeBuild(DeployStep step) =>
        step.Args.GetValueOrDefault("SyncBeforeBuild", "true") == "true";

    private static StepResult MissingBranchResult(DeployStep step, TimeSpan duration) => new()
    {
        Step = step,
        Success = false,
        ExitCode = -1,
        Error = "No se definió rama para GitCheckout: falta Args[\"Branch\"] y el DeployEnvironment no tiene DefaultBranch.",
        Duration = duration
    };

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Duration = duration
    };
}
