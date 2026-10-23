using Microsoft.Extensions.Configuration;
using NSubstitute;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>Shared fixtures for DelegationService ctor (task-prompt-refiner).</summary>
public static class TaskPromptRefinerTestSupport
{
    /// <summary>Config with refinement turned off — legacy behavior preserved.</summary>
    public static IConfiguration ConfigOff()
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg[TaskPromptRefinementModes.ConfigurationKey].Returns(TaskPromptRefinementModes.Off);
        return cfg;
    }

    /// <summary>Config with template mode + a real refiner (no redactor).</summary>
    public static (IConfiguration Config, ITaskPromptRefiner Refiner) TemplateMode()
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg[TaskPromptRefinementModes.ConfigurationKey].Returns(TaskPromptRefinementModes.Template);
        return (cfg, new TaskPromptRefiner(cfg));
    }
}