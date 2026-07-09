using vali_deploy.Domain;

namespace vali_deploy.Application;

public class PipelineRunner : IPipelineRunner
{
    private static readonly TimeSpan[] DefaultBackoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) };

    private readonly Dictionary<StepType, IStepExecutor> _executors;
    private readonly Func<int, TimeSpan> _retryDelayProvider;

    public PipelineRunner(IEnumerable<IStepExecutor> executors, Func<int, TimeSpan>? retryDelayProvider = null)
    {
        _executors = executors.ToDictionary(e => e.Handles);
        _retryDelayProvider = retryDelayProvider ?? (attempt => DefaultBackoff[Math.Min(attempt, DefaultBackoff.Length - 1)]);
    }

    public async Task<PipelineResult> RunAsync(List<DeployStep> pipeline, StepExecutionContext context, IProgress<StepResult>? progress)
    {
        var stepResults = new List<StepResult>();

        foreach (var step in pipeline)
        {
            if (!_executors.TryGetValue(step.Type, out var executor))
            {
                throw new InvalidOperationException($"No hay IStepExecutor registrado para StepType.{step.Type}.");
            }

            var result = await ExecuteWithRetryAsync(executor, step, context);
            stepResults.Add(result);
            progress?.Report(result);

            if (!result.Success && !step.ContinueOnFailure)
            {
                return new PipelineResult { Success = false, Steps = stepResults };
            }
        }

        return new PipelineResult { Success = stepResults.All(r => r.Success), Steps = stepResults };
    }

    private async Task<StepResult> ExecuteWithRetryAsync(IStepExecutor executor, DeployStep step, StepExecutionContext context)
    {
        StepResult result;
        var attempt = 1;

        while (true)
        {
            result = await executor.ExecuteAsync(step, context);
            result.AttemptNumber = attempt;

            if (result.Success || attempt > step.RetryCount)
            {
                return result;
            }

            await Task.Delay(_retryDelayProvider(attempt - 1));
            attempt++;
        }
    }
}
