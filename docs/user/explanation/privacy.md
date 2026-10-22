# Privacy and Eclipse

## The apps only read and write files

Trajectory Log Tools and Trajectory Log Converter work on files: the trajectory logs you open, and the
files you ask them to write. They:

- **open no Eclipse (ESAPI) session**, need no Eclipse login and read nothing from the Eclipse database;
- **never change or delete a log**: exports and rebuilt files are new files;
- **send nothing over the network** and need no internet access (this guide, too, works offline);
- write their own small files only: the settings, the recent-files list and the error log, in your
  Windows profile (see [Files and folders](../reference/files.md)).

The Eclipse launcher scripts only start the app as a separate program. They read no patient data, so
they run with or without a patient open and need no approval as write-enabled scripts.

## Patient data in logs

Trajectory logs do contain patient data: the header's metadata holds the patient ID and the plan name,
and file names may too. The apps show the patient ID wherever a log appears, so you can check you have
the right patient, and warn when Compare's A and B are different patients.

Treat logs, exports, rebuilt files, reports and screenshots as patient records. To share a delivery for
QA, you can change the patient ID and plan name in a v4.0 export: see
[Change header details](../how-to/export-header-details.md).

All the pictures in this guide show synthetic demo logs (patients `PHANTOM-01` to `PHANTOM-05`), made by
the project's test tools, never patient data.
