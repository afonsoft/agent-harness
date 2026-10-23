using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Taskboard.Integrations.Execution;
using Xunit;

namespace Taskboard.Tests.Unit.Execution;

/// <summary>
/// SPEC-20261020-agent-process-isolation: systemd-run scope wrapping,
/// direct-spawn fallback, PSI parsing + gate behavior.
/// </summary>
public sealed class ProcessIsolationTests
{
    private static ProcessStartInfo BaseInfo() => new("/bin/bash")
    {
        WorkingDirectory = "/tmp",
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };

    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
    {
        var data = pairs
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(data!).Build();
    }

    [Fact]
    public void Isolate_Quando_SystemdRunDisponivel_EmbrulhaComScope()
    {
        var runtimeDir = Path.Combine(Path.GetTempPath(), $"xdg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(runtimeDir, "systemd"));
        var previousXdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        try
        {
            // ctor probes XDG_RUNTIME_DIR — emulate the user manager dir.
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtimeDir);
            var ready = new SystemdScopeProcessIsolator(
                Config(("Harness:ProcessIsolation:MemoryMax", "3G")),
                NullLogger.Instance,
                executableLocator: name => name == "systemd-run" ? "/usr/bin/systemd-run" : null);

            var info = BaseInfo();
            info.ArgumentList.Add("-l");
            info.Environment["FOO"] = "bar";

            var wrapped = ready.Isolate(info, "pty");

            wrapped.FileName.ShouldBe("/usr/bin/systemd-run");
            wrapped.ArgumentList.ShouldContain("--scope");
            wrapped.ArgumentList.ShouldContain("--collect");
            wrapped.ArgumentList.ShouldContain("MemoryMax=3G");
            wrapped.ArgumentList.ShouldContain("MemoryHigh=1.5G");
            // OOMScoreAdjust is Exec-context only — invalid via -p on scopes
            // (systemd-run rejects it); applied through choom instead.
            wrapped.ArgumentList.ShouldNotContain("OOMScoreAdjust=500");
            // "--" separator then original argv.
            var sep = wrapped.ArgumentList.IndexOf("--");
            sep.ShouldBeGreaterThan(0);
            wrapped.ArgumentList[sep + 1].ShouldBe("/bin/bash");
            wrapped.ArgumentList[sep + 2].ShouldBe("-l");
            wrapped.ArgumentList.First(a => a.StartsWith("--unit=")).ShouldContain("harness-agent-pty-");
            wrapped.Environment["FOO"].ShouldBe("bar");
            wrapped.WorkingDirectory.ShouldBe("/tmp");
            wrapped.RedirectStandardOutput.ShouldBeTrue();
            wrapped.ArgumentList.Select(a => a.StartsWith("--unit=")).Count(x => x).ShouldBe(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", previousXdg);
            Directory.Delete(runtimeDir, recursive: true);
        }
    }

    [Fact]
    public void Isolate_Quando_ChoomDisponivel_EmbrulhaComOomScore()
    {
        var runtimeDir = Path.Combine(Path.GetTempPath(), $"xdg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(runtimeDir, "systemd"));
        var previousXdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        try
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtimeDir);
            var ready = new SystemdScopeProcessIsolator(
                Config(),
                NullLogger.Instance,
                executableLocator: name => name switch
                {
                    "systemd-run" => "/usr/bin/systemd-run",
                    "choom" => "/usr/bin/choom",
                    _ => null
                });

            var wrapped = ready.Isolate(BaseInfo(), "pty");

            var sep = wrapped.ArgumentList.IndexOf("--");
            sep.ShouldBeGreaterThan(0);
            wrapped.ArgumentList[sep + 1].ShouldBe("/usr/bin/choom");
            wrapped.ArgumentList[sep + 2].ShouldBe("-n");
            wrapped.ArgumentList[sep + 3].ShouldBe("500");
            wrapped.ArgumentList[sep + 4].ShouldBe("--");
            wrapped.ArgumentList[sep + 5].ShouldBe("/bin/bash");
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", previousXdg);
            Directory.Delete(runtimeDir, recursive: true);
        }
    }

    [Fact]
    public void Isolate_Quando_SystemdRunAusente_RetornaOriginal()
    {
        var isolator = new SystemdScopeProcessIsolator(
            Config(),
            NullLogger.Instance,
            executableLocator: _ => null);

        isolator.IsAvailable.ShouldBeFalse();
        var info = BaseInfo();
        isolator.Isolate(info, "pty").ShouldBeSameAs(info);
    }

    [Fact]
    public void Isolate_Quando_Desabilitado_RetornaOriginal()
    {
        var isolator = new SystemdScopeProcessIsolator(
            Config(("Harness:ProcessIsolation:Enabled", "false")),
            NullLogger.Instance,
            executableLocator: _ => "/usr/bin/systemd-run");

        isolator.IsAvailable.ShouldBeFalse();
        var info = BaseInfo();
        isolator.Isolate(info, "pty").ShouldBeSameAs(info);
    }

    [Fact]
    public void Gate_Quando_PressaoAcima_Recusa()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mempressure-{Guid.NewGuid():N}");
        File.WriteAllText(file, "some avg10=10.00 avg60=45.50 avg300=30.00 total=0\nfull avg10=0.00 avg60=0.00 avg300=0.00 total=0\n");
        try
        {
            var gate = new MemoryPressureGate(file, 30.0, NullLogger<MemoryPressureGate>.Instance);
            gate.SampleSomeAvg60().ShouldBe(45.50);
            gate.Allow(out var sampled).ShouldBeFalse();
            sampled.ShouldBe(45.50);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Gate_Quando_PressaoAbaixo_Permite()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mempressure-{Guid.NewGuid():N}");
        File.WriteAllText(file, "some avg10=1.00 avg60=2.00 avg300=0.00 total=0\n");
        try
        {
            var gate = new MemoryPressureGate(file, 30.0, NullLogger<MemoryPressureGate>.Instance);
            gate.Allow(out var sampled).ShouldBeTrue();
            sampled.ShouldBe(2.00);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Gate_Quando_ArquivoAusente_FailOpen()
    {
        var gate = new MemoryPressureGate(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
            30.0,
            NullLogger<MemoryPressureGate>.Instance);
        gate.SampleSomeAvg60().ShouldBeNull();
        gate.Allow(out var sampled).ShouldBeTrue();
        sampled.ShouldBeNull();
    }

    [Fact]
    public void Gate_Quando_ConteudoInvalido_FailOpen()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mempressure-{Guid.NewGuid():N}");
        File.WriteAllText(file, "garbage\nsome avg60=abc\n");
        try
        {
            var gate = new MemoryPressureGate(file, 30.0, NullLogger<MemoryPressureGate>.Instance);
            gate.SampleSomeAvg60().ShouldBeNull();
            gate.Allow(out _).ShouldBeTrue();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Gate_Snapshot_ExpoemContadoresCgroup()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cgroup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "memory.pressure"), "some avg10=0.00 avg60=5.00 avg300=0.00 total=0\n");
        File.WriteAllText(Path.Combine(dir, "memory.current"), "1234567\n");
        File.WriteAllText(Path.Combine(dir, "memory.high"), "max\n");
        File.WriteAllText(Path.Combine(dir, "memory.events"), "low 0\nhigh 3\nmax 1\noom 0\noom_kill 0\n");
        try
        {
            var gate = new MemoryPressureGate(
                Path.Combine(dir, "memory.pressure"), 30.0, NullLogger<MemoryPressureGate>.Instance);
            var snap = gate.Snapshot();
            snap.SomeAvg60.ShouldBe(5.00);
            snap.CurrentBytes.ShouldBe(1234567);
            snap.HighBytes.ShouldBeNull(); // "max" → null
            snap.Events.ShouldNotBeNull()["high"].ShouldBe(3);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Noop_Isolate_RetornaOriginal()
    {
        var info = BaseInfo();
        NoopProcessIsolator.Instance.Isolate(info, "x").ShouldBeSameAs(info);
    }
}
