using vali_deploy.Domain;

namespace vali_deploy.Application;

public interface IStepExecutor
{
    StepType Handles { get; }
    Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context);
}
