using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IDeployHistoryRepository
{
    DeployHistoryQueryResult GetRecent(int count, string? projectFilter = null);
}
