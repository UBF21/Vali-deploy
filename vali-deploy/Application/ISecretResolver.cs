namespace vali_deploy.Application;

public interface ISecretResolver
{
    string Resolve(string envVarName);
}
