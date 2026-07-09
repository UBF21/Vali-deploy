using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class EnvVarSecretResolverTests
{
    [Fact]
    public void Resolve_returns_value_when_env_var_exists()
    {
        Environment.SetEnvironmentVariable("VALI_DEPLOY_TEST_SECRET", "s3cr3t");
        var resolver = new EnvVarSecretResolver();

        var value = resolver.Resolve("VALI_DEPLOY_TEST_SECRET");

        Assert.Equal("s3cr3t", value);
        Environment.SetEnvironmentVariable("VALI_DEPLOY_TEST_SECRET", null);
    }

    [Fact]
    public void Resolve_throws_explicit_error_when_env_var_missing()
    {
        Environment.SetEnvironmentVariable("VALI_DEPLOY_TEST_MISSING", null);
        var resolver = new EnvVarSecretResolver();

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("VALI_DEPLOY_TEST_MISSING"));
        Assert.Contains("VALI_DEPLOY_TEST_MISSING", ex.Message);
    }
}
