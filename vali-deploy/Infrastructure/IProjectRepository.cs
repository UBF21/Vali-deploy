using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IProjectRepository
{
    DeployConfig Load();
    void Save(DeployConfig config);
}
