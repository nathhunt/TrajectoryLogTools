# Validation and commissioning

!!! warning "To be confirmed"
    This page is a placeholder until the department supplies:

    - the validation and commissioning status of version 1.0.0-beta;
    - who signed it off, and when;
    - the reference to the department's record;
    - whether 1.0.0-beta is cleared for clinical use, or for testing only.

## What the project tests

For information, the project's own automated tests (run on every change) check that:

- the conversion engine reads and writes logs as the Varian specifications describe, and that a v5.1 →
  v4.0 → v5.1 round trip gives the original byte for byte;
- the delivery comparison and the MLC rebuild give the same numbers as the original tools they replace
  (`compare_trajectory_logs.py` and LogToEclipseMlc), on simulated deliveries ("golden" tests);
- the export, rebuild and comparison commands behave as documented.

These are software tests on synthetic logs. They don't replace the department's validation on its own
machines and logs.
