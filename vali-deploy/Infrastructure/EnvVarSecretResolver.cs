using vali_deploy.Application;

namespace vali_deploy.Infrastructure;

public class EnvVarSecretResolver : ISecretResolver
{
    public string Resolve(string envVarName)
    {
        var value = Environment.GetEnvironmentVariable(envVarName);

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"La variable de entorno '{envVarName}' no está definida o está vacía. " +
                "Configurala antes de correr el pipeline.");
        }

        return value;
    }
}
