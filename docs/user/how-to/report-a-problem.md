# Report a problem

When something goes wrong, the app shows a message and writes the full details to an error log.

## Find the error log

Both apps write to the same file, for the Windows user:

`%LocalAppData%\TrajectoryLogTools\errors.log`

1. Press ++windows+r++, type `%LocalAppData%\TrajectoryLogTools` and select **OK**.
2. Open `errors.log`. Each entry has the date and time, the app, its version, the .NET runtime, Windows
   and the full error.

The message the app showed names this file too.

## Report it

!!! warning "To be confirmed"
    Who to contact, and how (email, ticket system or GitHub issue), goes here. See
    [Version and support](../about/support.md).

Include:

1. what you did, and what you expected;
2. the message, word for word, or a screenshot of it;
3. the version, from the line at the bottom of the window (for example *Version 1.0.0-beta+aeaf93d*);
4. the entries of `errors.log` from the time it happened.

!!! danger "No patient data"
    Don't attach trajectory logs, screenshots or error logs that show patient data unless your
    department's process allows it. File names and patient IDs can identify a patient.
