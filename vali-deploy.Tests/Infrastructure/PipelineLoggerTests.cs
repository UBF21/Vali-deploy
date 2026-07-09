using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class PipelineLoggerTests
{
    [Fact]
    public void WriteStep_appends_step_result_to_the_run_log_file()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("proj", "sub");

            logger.WriteStep(new StepResult
            {
                Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0, Output = "ok"
            });

            var logFile = Directory.GetFiles(tempLogsDir).Single();
            var content = File.ReadAllText(logFile);

            Assert.Contains("build", content);
            Assert.Contains("ExitCode: 0", content);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void StartRun_creates_file_named_with_project_subproject_and_timestamp()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("shop", "api");

            var logFile = Directory.GetFiles(tempLogsDir).Single();
            Assert.StartsWith("shop-api-", Path.GetFileName(logFile));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }
}
