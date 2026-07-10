using vali_deploy.Utils;

namespace vali_deploy.Tests.Utils;

public class ConstantsTests
{
    [Fact]
    public void DefaultLogsDirectory_ends_with_expected_relative_path()
    {
        var path = Constants.DefaultLogsDirectory();

        Assert.EndsWith(Path.Combine("vali-deploy", "logs"), path);
    }
}
