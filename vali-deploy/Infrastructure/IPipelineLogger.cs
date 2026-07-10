using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IPipelineLogger
{
    void StartRun(string projectName, string subProjectName, string environmentName);
    void WriteStep(StepResult stepResult);
    void FinishRun(PipelineResult result);
}
