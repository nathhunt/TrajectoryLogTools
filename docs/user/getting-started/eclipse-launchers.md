# Set up the Eclipse launchers

Each app comes with a small Eclipse script that starts it from **Tools > Scripts**. Eclipse runs
scripts on .NET Framework 4.8, so the script only starts the app as a separate program. The app keeps
running if you close Eclipse, and Eclipse stays usable while the app is open.

The scripts read no patient data. They run with or without a patient open and don't need approval
as write-enabled scripts.

!!! warning "To be confirmed"
    The department's Eclipse version and scripts folder go here. The example below uses a published
    scripts folder on the Eclipse server.

## Trajectory Log Tools

1. Open `LaunchTrajectoryLogTools.cs` from the deployed `TrajectoryLogTools` folder in a text editor.
2. Near the top, set **`ExePath`** to the full path of `TrajectoryLogTools.exe` in the deployed folder:

    ```csharp
    const string ExePath = @"\\server\ESAPI\TrajectoryLogTools\TrajectoryLogTools.exe";
    ```

3. Optional: set **`StartFolder`** to the folder the **Open** dialogs start in, for example where logs
   are copied from the machine. You can still open logs from anywhere.

    ```csharp
    const string StartFolder = @"\\server\Halcyon\TrajectoryLogs";
    ```

4. Save the script, and copy it to your Eclipse scripts folder, for example
   `\\<server>\va_data$\ProgramData\Vision\PublishedScripts`.
5. In Eclipse, select **Tools > Scripts**, then **LaunchTrajectoryLogTools.cs**, then **Run**.

## Trajectory Log Converter

1. Open `LaunchTrajectoryLogConverter.cs` from the deployed `TrajectoryLogConverter` folder.
2. Set **`ExePath`** to the full path of `TrajectoryLogConverter.exe` in that folder.
3. Copy the script to your Eclipse scripts folder and run it from **Tools > Scripts**.
4. The first time, [set the converter's default folders](../how-to/converter-folders.md).

## If it doesn't start

| Message | What to do |
|---|---|
| *Trajectory Log Tools was not found at: …* | `ExePath` is wrong. Fix it in the script and copy the script again. |
| *Could not start Trajectory Log Tools: …* mentioning a policy or access | Application allow-listing blocks the folder. Ask IT to allow it. |

You can also start either app without Eclipse: double-click its `.exe`, or drag a `.bin` log onto
`TrajectoryLogTools.exe` to open it straight away.
