using System.Diagnostics;
using System.Text.RegularExpressions;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class GitCheckoutExecutor : IStepExecutor
{
    private static readonly Regex ValidBranchName = new(@"\A[A-Za-z0-9][A-Za-z0-9._/-]*\z", RegexOptions.Compiled);

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

        if (!ValidBranchName.IsMatch(branch))
        {
            return InvalidBranchResult(step, branch, stopwatch.Elapsed);
        }

        var checkoutCommand = $"git checkout {branch}";
        var checkoutResult = await _processRunner.RunAsync(checkoutCommand, context.ProjectPath);

        if (checkoutResult.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, checkoutResult, checkoutResult.StdOut, checkoutCommand, stopwatch.Elapsed);
        }

        if (!ShouldSyncBeforeBuild(step))
        {
            stopwatch.Stop();
            return BuildResult(step, checkoutResult, checkoutResult.StdOut, checkoutCommand, stopwatch.Elapsed);
        }

        const string pullCommand = "git pull";
        var pullResult = await _processRunner.RunAsync(pullCommand, context.ProjectPath);
        stopwatch.Stop();

        return BuildResult(step, pullResult, checkoutResult.StdOut + pullResult.StdOut, $"{checkoutCommand} && {pullCommand}", stopwatch.Elapsed);
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

    private static StepResult InvalidBranchResult(DeployStep step, string branch, TimeSpan duration) => new()
    {
        Step = step,
        Success = false,
        ExitCode = -1,
        Error = $"La rama '{branch}' no es un nombre de rama válido para GitCheckout: solo se permiten letras, dígitos, '.', '_', '/' y '-'.",
        Duration = duration
    };

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, string command, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Command = command,
        Duration = duration
    };
}
