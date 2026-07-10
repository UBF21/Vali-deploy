using System.Text.Json;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class DeployHistoryRepository : IDeployHistoryRepository
{
    private readonly string _indexFilePath;

    public DeployHistoryRepository(string? logsDirectory = null)
    {
        var directory = logsDirectory ?? Utils.Constants.DefaultLogsDirectory();
        _indexFilePath = Path.Combine(directory, "deploy-history.jsonl");
    }

    public DeployHistoryQueryResult GetRecent(int count, string? projectFilter = null)
    {
        if (!File.Exists(_indexFilePath))
        {
            return new DeployHistoryQueryResult();
        }

        var (runs, skipped) = ParseIndexFile();
        var ordered = FilterAndOrder(runs, projectFilter, count);

        return new DeployHistoryQueryResult { Runs = ordered, SkippedCorruptedLines = skipped };
    }

    private (List<DeployRunSummary> Runs, int Skipped) ParseIndexFile()
    {
        var runs = new List<DeployRunSummary>();
        var skipped = 0;

        foreach (var line in File.ReadAllLines(_indexFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryParseLine(line, out var summary))
            {
                runs.Add(summary!);
            }
            else
            {
                skipped++;
            }
        }

        return (runs, skipped);
    }

    private static bool TryParseLine(string line, out DeployRunSummary? summary)
    {
        try
        {
            summary = JsonSerializer.Deserialize<DeployRunSummary>(line);
            return summary != null;
        }
        catch (JsonException)
        {
            summary = null;
            return false;
        }
    }

    private static List<DeployRunSummary> FilterAndOrder(List<DeployRunSummary> runs, string? projectFilter, int count)
    {
        var filtered = projectFilter == null
            ? runs
            : runs.Where(r => r.ProjectName == projectFilter).ToList();

        return filtered.OrderByDescending(r => r.StartedAtUtc).Take(count).ToList();
    }
}
