using System.Diagnostics;
using ApexClick.Models;

namespace ApexClick.FeaturePack.Export;

public sealed class StandaloneExporter
{
    public async Task<string> PublishAsync(MacroScript script, string outputDir, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        var templateRoot = FindInstalledAssemblyRoot();
        var requiredAssemblies = new[]
        {
            "ApexClick.Models.dll",
            "ApexClick.Services.Capture.dll",
            "ApexClick.Services.Playback.dll",
            "ApexClick.Services.Vision.dll",
            "ApexClick.FeaturePack.dll"
        };
        var missing = requiredAssemblies.Where(x => !File.Exists(Path.Combine(templateRoot, x))).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("Для standalone export не хватает assemblies: " + string.Join(", ", missing));

        var publishDir = Path.GetFullPath(outputDir);
        Directory.CreateDirectory(publishDir);
        var temp = Path.Combine(Path.GetTempPath(), "ApexClickExport", Guid.NewGuid().ToString("N"));
        var lib = Path.Combine(temp, "lib");
        var publish = Path.Combine(temp, "publish");
        Directory.CreateDirectory(lib);
        Directory.CreateDirectory(publish);

        try
        {
            var scenario = Path.Combine(temp, "scenario.mscr");
            await MscrScriptSerializer.SaveAsync(script, scenario, ct);

            foreach (var assembly in requiredAssemblies)
                File.Copy(Path.Combine(templateRoot, assembly), Path.Combine(lib, assembly), overwrite: true);

            var sourceRuntimeConfig = Path.Combine(templateRoot, "ApexClick.FeaturePack.runtimeconfig.json");
            if (File.Exists(sourceRuntimeConfig))
                File.Copy(sourceRuntimeConfig, Path.Combine(lib, "ApexClick.FeaturePack.runtimeconfig.json"), true);

            var project = Path.Combine(temp, "ScenarioRunner.csproj");
            var refs = string.Join(Environment.NewLine, requiredAssemblies.Select(a =>
                $"    <Reference Include=\"{Path.GetFileNameWithoutExtension(a)}\"><HintPath>lib/{a}</HintPath></Reference>"));

            await File.WriteAllTextAsync(project, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
{refs}
  </ItemGroup>
  <ItemGroup>
    <None Include="scenario.mscr" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
""", ct);

            var program = Path.Combine(temp, "Program.cs");
            await File.WriteAllTextAsync(program, """
using ApexClick.FeaturePack.GraphEngine;
using ApexClick.Models;
using ApexClick.Services.Capture;
using ApexClick.Services.Playback;
using ApexClick.Services.Vision;

var mscrPath = Path.Combine(AppContext.BaseDirectory, "scenario.mscr");
var script = await MscrScriptSerializer.LoadAsync(mscrPath);
using var hook = new InputHookService();
using var vision = new ImageAnalysisService();
var capture = new ScreenCaptureService();
var timing = new PlaybackTimingService();
var playback = new PlaybackEngine(timing, hook);

if (ExecutionGraphPersistence.TryLoad(script, out var graph) && graph is not null)
{
    var interpreter = new ExecutionGraphInterpreter(
        playback,
        new VisionGraphConditionEvaluator(capture, vision),
        new MscrSubScenarioLoader());
    await interpreter.ExecuteScriptGraphAsync(script, graph, cancellationToken: CancellationToken.None);
}
else
{
    await playback.PlayAsync(script, CancellationToken.None);
}
""", ct);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{project}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{publish}\"",
                WorkingDirectory = temp,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("dotnet publish не запустился.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"dotnet publish failed ({process.ExitCode}).\n{stdout}\n{stderr}");

            var publishedExe = Directory.EnumerateFiles(publish, "ScenarioRunner.exe", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? throw new FileNotFoundException("Publish did not produce ScenarioRunner.exe.");
            var safeName = SanitizeName(script.Name);
            var finalExe = Path.Combine(publishDir, safeName + ".exe");
            File.Copy(publishedExe, finalExe, overwrite: true);
            return finalExe;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static string FindInstalledAssemblyRoot() => AppContext.BaseDirectory;

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "ApexClickScenario" : safe;
    }
}
