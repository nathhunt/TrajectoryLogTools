<#
.SYNOPSIS
    Publishes the deployable folder: TrajectoryLogTools.exe (GUI), TrajectoryLogCli.exe and the
    Eclipse launcher script, self-contained for 64-bit Windows (no .NET install needed).

.DESCRIPTION
    Same as Visual Studio's Publish > Deploy, or the VS Code "publish" task. Copy the WHOLE folder
    to where the Eclipse workstations can read it (local folder or network share), then point
    ExePath in LaunchTrajectoryLogTools.cs at TrajectoryLogTools.exe in it.

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -DeployDir \\server\ESAPI\TrajectoryLogTools
#>
param(
    # Default: Deploy\TrajectoryLogTools at the repository root.
    [string] $DeployDir
)

$project = Join-Path $PSScriptRoot '..\src\TrajectoryLogTools.App\TrajectoryLogTools.App.csproj'
$arguments = @('publish', $project, '-p:PublishProfile=Deploy')
# No trailing backslash: Windows PowerShell would let it escape the closing quote of a path with
# spaces. The .NET SDK adds the backslash itself.
if ($DeployDir) { $arguments += "-p:PublishDir=$($DeployDir.TrimEnd('\'))" }
& dotnet @arguments
exit $LASTEXITCODE
