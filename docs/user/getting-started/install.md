# Install and deploy

This page is for whoever looks after the deployment. Users don't install anything.

!!! warning "To be confirmed"
    The department's deploy location (share path) and who maintains the deployment go here.
    The steps below use `\\server\ESAPI\` as an example.

## What you need

- Windows 10 version 1607, Windows Server 2016 or later, 64-bit. This covers Eclipse workstations
  and Citrix servers.
- **Nothing to install.** Each app is a self-contained folder that carries its own .NET 10 runtime.
- Read access to the folders for every user, and write access to the converter's folder for whoever
  sets its default folders (see [Set the converter's folders](../how-to/converter-folders.md)).

## The deploy folders

Publishing makes two separate folders. Either can be deployed, moved or removed without the other.

```text
TrajectoryLogTools\                  about 180 MB
  TrajectoryLogTools.exe             the app
  TrajectoryLogCli.exe               a command-line tool for scripts (not covered by this guide)
  LaunchTrajectoryLogTools.cs        the Eclipse launcher script
  Help\                              this guide (index.html)
  *.dll                              the .NET runtime and libraries: keep everything together
TrajectoryLogConverter\              about 160 MB
  TrajectoryLogConverter.exe         the converter
  LaunchTrajectoryLogConverter.cs    its Eclipse launcher script
  TrajectoryLogConverter.settings.json  its default folders (written by Settings, kept on redeploy)
  Help\                              this guide
  *.dll                              its own .NET runtime and libraries
```

!!! note
    Each app is a folder, not a single `.exe`. A single-file app unpacks DLLs to `%TEMP%` when it
    starts, which application allow-listing (AppLocker, WDAC) on clinical PCs and Citrix often blocks.

## Deploy

1. Get the two folders. A developer makes them with `scripts\publish.ps1` (see the repository's
   `README.md`), or copies them from the previous release.
2. Copy each **whole** folder to a location every Eclipse workstation, or Citrix server, can read,
   for example `\\server\ESAPI\TrajectoryLogTools\` and `\\server\ESAPI\TrajectoryLogConverter\`.
3. When you redeploy over an existing converter folder, keep `TrajectoryLogConverter.settings.json`:
   publishing doesn't overwrite it.
4. Check that `Help\index.html` is in both folders. If it's missing, the **Help** button says so.
5. [Set up the Eclipse launchers](eclipse-launchers.md).
6. If the apps don't start, ask IT to allow the deployed folders in application allow-listing
   (AppLocker or WDAC).

## Update to a new version

1. Read [What's new](../about/whats-new.md).
2. Replace the folders' contents with the new ones (keep the converter's settings file).
3. Start each app and check the version on the line at the bottom of the window.
