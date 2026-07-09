using Spectre.Console;
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Presentation;

public class PipelineExecutionView
{
    public async Task<PipelineResult> RunAsync(IPipelineRunner pipelineRunner, List<DeployStep> steps, StepExecutionContext context)
    {
        PipelineResult? result = null;

        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new SpinnerColumn(), new ElapsedTimeColumn())
            .StartAsync(async ctx => result = await RunPipelineWithProgressAsync(pipelineRunner, steps, context, ctx));

        RenderSummaryTable(result!);
        return result!;
    }

    private static async Task<PipelineResult> RunPipelineWithProgressAsync(
        IPipelineRunner pipelineRunner,
        List<DeployStep> steps,
        StepExecutionContext context,
        ProgressContext ctx)
    {
        var tasks = steps.ToDictionary(s => s, s => ctx.AddTask(s.Name, autoStart: false));
        tasks[steps[0]].StartTask();

        var progress = new Progress<StepResult>(stepResult => OnStepCompleted(stepResult, steps, tasks));

        return await pipelineRunner.RunAsync(steps, context, progress);
    }

    private static void OnStepCompleted(StepResult stepResult, List<DeployStep> steps, Dictionary<DeployStep, ProgressTask> tasks)
    {
        var task = tasks[stepResult.Step];
        task.Value = 100;
        task.Description = DescribeStepStatus(stepResult);

        StartNextTask(stepResult, steps, tasks);
    }

    private static void StartNextTask(StepResult stepResult, List<DeployStep> steps, Dictionary<DeployStep, ProgressTask> tasks)
    {
        var nextIndex = steps.IndexOf(stepResult.Step) + 1;
        if (nextIndex >= steps.Count)
        {
            return;
        }

        tasks[steps[nextIndex]].StartTask();
    }

    private static string DescribeStepStatus(StepResult stepResult)
    {
        if (stepResult.Success)
        {
            return $"[green]✅ {stepResult.Step.Name}[/]";
        }

        if (stepResult.WasSkippedDueToContinueOnFailure)
        {
            return $"[yellow]⚠ {stepResult.Step.Name}[/]";
        }

        return $"[red]❌ {stepResult.Step.Name}[/]";
    }

    private static void RenderSummaryTable(PipelineResult result)
    {
        var table = new Table().AddColumns("Paso", "Estado", "Duración", "Exit Code");

        foreach (var stepResult in result.Steps)
        {
            table.AddRow(
                stepResult.Step.Name,
                DescribeEstado(stepResult),
                stepResult.Duration.ToString(@"mm\:ss"),
                stepResult.ExitCode.ToString());
        }

        AnsiConsole.Write(table);
    }

    private static string DescribeEstado(StepResult stepResult)
    {
        if (stepResult.Success)
        {
            return "[green]OK[/]";
        }

        if (stepResult.WasSkippedDueToContinueOnFailure)
        {
            return "[yellow]WARNING[/]";
        }

        return "[red]FALLÓ[/]";
    }
}
