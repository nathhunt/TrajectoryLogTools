# What the apps are for

Every time a Halcyon or Ethos machine treats, it writes a **trajectory log**: a binary `.bin` file
that records, every 20 ms, where each axis was meant to be (**expected**) and where it was
(**actual**): gantry, collimator, MU, the dose servo (beam hold) state, the control point and every
MLC leaf. Machines on Halcyon 5 write version 5.1 logs; earlier software wrote version 4.0.

## Trajectory Log Tools

The main app. Open any number of logs, from any folders, and:

- **view** a delivery: MU delivered against planned, duration, interruptions, MLC error, a delivery
  timeline, each field, every axis, the MLC error map and the raw file;
- **compare** two deliveries of a plan, matched by control point, with optional tolerances;
- **compare two files** value by value, for example to check a v4.0 conversion against its v5.1 original;
- **rebuild Eclipse MLC files** from a delivery, one per field, each with a status that says
  whether it is ready to import;
- **export** logs to v4.0 `.bin`, CSV, a full dump as CSV, or a raw dump as text.

It replaces four earlier tools: the old *TrajectoryLogConverter* GUI and *HalcyonTrajectoryLogTool*,
*TrajectoryLogDump*, `compare_trajectory_logs.py` and *LogToEclipseMlc*.

## Trajectory Log Converter

A small app that does one job: converts the v5.1 logs in a default input folder (for example the
day's logs) to v4.0 in a default output folder, always the same way.

!!! warning "To be confirmed"
    Which software or process reads the v4.0 logs, and why it needs v4.0, goes here.

## Who uses them

!!! warning "To be confirmed"
    Who uses which app, and when (for example daily, after each fraction, weekly), goes here.

## Next steps

1. [Install and deploy](install.md) the apps (for whoever looks after the deployment).
2. [Set up the Eclipse launchers](eclipse-launchers.md), so the apps start from **Tools > Scripts**.
3. [First run](first-run.md): check the app starts and works on your workstation.
