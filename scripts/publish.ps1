<#
.SYNOPSIS
    Publishes the two deployable folders, each self-contained for 64-bit Windows (no .NET install needed):
      <DeployDir>                          TrajectoryLogTools.exe (GUI), TrajectoryLogCli.exe, LaunchTrajectoryLogTools.cs
      <DeployDir>\..\TrajectoryLogConverter TrajectoryLogConverter.exe, LaunchTrajectoryLogConverter.cs

.DESCRIPTION
    Same as Visual Studio's Publish > Deploy, or the VS Code "publish" task. Copy each WHOLE folder
    to where the Eclipse workstations can read it (local folder or network share), then point
    ExePath in each launcher script at its exe.

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -DeployDir \\server\ESAPI\TrajectoryLogTools
#>
param(
    # Default: Deploy\TrajectoryLogTools at the repository root (the converter: Deploy\TrajectoryLogConverter).
    [string] $DeployDir
)

$project = Join-Path $PSScriptRoot '..\src\TrajectoryLogTools.App\TrajectoryLogTools.App.csproj'
$arguments = @('publish', $project, '-p:PublishProfile=Deploy')
# No trailing backslash: Windows PowerShell would let it escape the closing quote of a path with
# spaces. The .NET SDK adds the backslash itself.
if ($DeployDir) { $arguments += "-p:PublishDir=$($DeployDir.TrimEnd('\'))" }
& dotnet @arguments
exit $LASTEXITCODE
