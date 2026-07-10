using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class DeployHistoryRepositoryTests
{
    private static string WriteIndex(string logsDir, params DeployRunSummary[] summaries)
    {
        var indexFile = Path.Combine(logsDir, "deploy-history.jsonl");
        File.WriteAllLines(indexFile, summaries.Select(s => JsonSerializer.Serialize(s)));
        return indexFile;
    }

    [Fact]
    public void GetRecent_on_missing_index_file_returns_empty_result()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var repository = new DeployHistoryRepository(tempLogsDir);

            var result = repository.GetRecent(30);

            Assert.Empty(result.Runs);
            Assert.Equal(0, result.SkippedCorruptedLines);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_orders_runs_by_StartedAtUtc_descending()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var older = new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var newer = new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
            WriteIndex(tempLogsDir, older, newer);

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(30);

            Assert.Equal(new[] { newer.RunId, older.RunId }, result.Runs.Select(r => r.RunId));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_filters_by_project_name()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var shop = new DeployRunSummary { ProjectName = "shop", SubProjectName = "api", StartedAtUtc = DateTime.UtcNow };
            var billing = new DeployRunSummary { ProjectName = "billing", SubProjectName = "worker", StartedAtUtc = DateTime.UtcNow };
            WriteIndex(tempLogsDir, shop, billing);

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(30, projectFilter: "shop");

            Assert.Single(result.Runs);
            Assert.Equal("shop", result.Runs[0].ProjectName);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_respects_the_count_limit()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var summaries = Enumerable.Range(0, 5)
                .Select(i => new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = DateTime.UtcNow.AddMinutes(i) })
                .ToArray();
            WriteIndex(tempLogsDir, summaries);

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(2);

            Assert.Equal(2, result.Runs.Count);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_skips_corrupted_lines_without_losing_valid_ones()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var valid = new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = DateTime.UtcNow };
            var indexFile = Path.Combine(tempLogsDir, "deploy-history.jsonl");
            File.WriteAllLines(indexFile, new[] { "{ esto no es json valido", JsonSerializer.Serialize(valid) });

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(30);

            Assert.Single(result.Runs);
            Assert.Equal(1, result.SkippedCorruptedLines);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }
}
