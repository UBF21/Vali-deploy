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
            logger.StartRun("proj", "sub", "Local");

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
            logger.StartRun("shop", "api", "Prod");

            var logFile = Directory.GetFiles(tempLogsDir).Single(f => f.EndsWith(".log"));
            Assert.StartsWith("shop-api-", Path.GetFileName(logFile));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void WriteStep_throws_when_called_before_StartRun()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);

            Assert.Throws<InvalidOperationException>(() =>
                logger.WriteStep(new StepResult { Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0 }));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void WriteStep_appends_multiple_steps_without_overwriting_previous_ones()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("proj", "sub", "Local");

            logger.WriteStep(new StepResult { Step = new DeployStep { Name = "clean" }, Success = true, ExitCode = 0 });
            logger.WriteStep(new StepResult { Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0 });

            var logFile = Directory.GetFiles(tempLogsDir).Single(f => f.EndsWith(".log"));
            var content = File.ReadAllText(logFile);

            Assert.Contains("clean", content);
            Assert.Contains("build", content);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void FinishRun_throws_when_called_before_StartRun()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);

            Assert.Throws<InvalidOperationException>(() => logger.FinishRun(new PipelineResult { Success = true }));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void FinishRun_appends_footer_to_log_and_a_json_line_to_the_history_index()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("shop", "api", "Prod");

            var stepResult = new StepResult
            {
                Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0, Duration = TimeSpan.FromSeconds(5)
            };
            logger.WriteStep(stepResult);

            var pipelineResult = new PipelineResult { Success = true, Steps = new List<StepResult> { stepResult } };
            logger.FinishRun(pipelineResult);

            var logFile = Directory.GetFiles(tempLogsDir).Single(f => f.EndsWith(".log"));
            Assert.Contains("Run finalizado", File.ReadAllText(logFile));

            var indexFile = Path.Combine(tempLogsDir, "deploy-history.jsonl");
            var line = File.ReadAllLines(indexFile).Single();
            var summary = System.Text.Json.JsonSerializer.Deserialize<DeployRunSummary>(line)!;

            Assert.Equal("shop", summary.ProjectName);
            Assert.Equal("api", summary.SubProjectName);
            Assert.Equal("Prod", summary.EnvironmentName);
            Assert.True(summary.Success);
            Assert.Equal(TimeSpan.FromSeconds(5), summary.TotalDuration);
            Assert.Equal(logFile, summary.LogFilePath);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void FinishRun_on_two_consecutive_runs_appends_two_lines_to_the_same_index_file()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);

            logger.StartRun("proj", "sub", "Local");
            logger.FinishRun(new PipelineResult { Success = true, Steps = new List<StepResult>() });

            logger.StartRun("proj", "sub", "Local");
            logger.FinishRun(new PipelineResult { Success = false, Steps = new List<StepResult>() });

            var indexFile = Path.Combine(tempLogsDir, "deploy-history.jsonl");
            var lines = File.ReadAllLines(indexFile);

            Assert.Equal(2, lines.Length);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }
}
