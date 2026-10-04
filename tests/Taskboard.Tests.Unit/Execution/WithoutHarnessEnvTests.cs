using Shouldly;
using Taskboard.Integrations.Execution;
using Xunit;

namespace Taskboard.Tests.Unit.Execution;

public class WithoutHarnessEnvTests
{
    [Fact]
    public void Dado_VarsHarness_Quando_Apply_Entao_RemoveHarneETaskboard()
    {
        var env = new Dictionary<string, string?>
        {
            ["HARNESS_HOME"] = "/x",
            ["TASKBOARD_DB"] = "/y",
            ["PATH"] = "/usr/bin",
        };

        WithoutHarnessEnv.Apply(env, "/work");

        env.ShouldNotContainKey("HARNESS_HOME");
        env.ShouldNotContainKey("TASKBOARD_DB");
        env["PATH"].ShouldBe("/usr/bin");
    }

    [Fact]
    public void Dado_PwdEStqale_Quando_ApplyComWorkdir_Entao_PwdViraWorkdir()
    {
        var env = new Dictionary<string, string?>
        {
            ["PWD"] = "/server/launch/dir",
            ["OLDPWD"] = "/algum/lugar",
        };

        WithoutHarnessEnv.Apply(env, "/worktrees/task-1");

        env["PWD"].ShouldBe("/worktrees/task-1");
        env.ShouldNotContainKey("OLDPWD");
    }

    [Fact]
    public void Dado_Pwd_Quando_ApplySemWorkdir_Entao_RemovePwd()
    {
        var env = new Dictionary<string, string?>
        {
            ["PWD"] = "/server/launch/dir",
            ["OLDPWD"] = "/algum/lugar",
        };

        WithoutHarnessEnv.Apply(env, null);

        env.ShouldNotContainKey("PWD");
        env.ShouldNotContainKey("OLDPWD");
    }

    [Fact]
    public void Dado_Pwd_Quando_ApplyWorkdirVazio_Entao_RemovePwd()
    {
        var env = new Dictionary<string, string?> { ["PWD"] = "/dir" };

        WithoutHarnessEnv.Apply(env, "   ");

        env.ShouldNotContainKey("PWD");
    }
}
