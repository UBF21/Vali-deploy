using vali_deploy.Domain;

namespace vali_deploy.Application;

public interface IPipelineRunner
{
    Task<PipelineResult> RunAsync(List<DeployStep> pipeline, StepExecutionContext context, IProgress<StepResult>? progress);
}
