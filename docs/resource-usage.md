# Resource usage

[Back to README](../README.md)

**Original (JONSBO PC Monitor, Electron):** measured, not estimated —
3,966s of CPU time accumulated over 34.4h of uptime = **3.20% of one core**
continuously, spread across the Electron main process, 4 child processes,
and `SystemInfos.exe`.

**This replacement:** built and running, driving the real panel. Measured
with:

```powershell
Get-Process JonsboMonitor | Select-Object WorkingSet64, PrivateMemorySize64, CPU
```

Original design targets were under 30MB working set and under 0.3% of one
core sustained; the steady-state figures below land close to that on
private working set but higher on total working set (self-contained
.NET runtime floor) and on CPU — see the notes on each below.

**These numbers were measured on a machine under 70-90% CPU load from an
unrelated job for the entire measurement window** (confirmed via
`\Processor Information(_Total)\% Processor Utility`), not an idle
desktop — so they're conservative; an idle machine should do at least as
well.

- **CPU, steady state:** **~0.7% of one core**, down from an initial
  **1.263%** before the optimization pass (per-poll LINQ sensor walks
  replaced with cached `ISensor` references, `Update()` scoped to only
  the hardware nodes actually read, WMI RAM-clock lookup cached instead
  of re-queried every poll). A further fast/slow update-cadence split
  (Motherboard/Super I/O fan+Vcore sensors on a 5s cadence, CPU/GPU/Memory
  still on the 2s default) is implemented but its own CPU delta hasn't
  been benchmarked in isolation yet.
- **Working set, steady state:** a long-run soak (untouched process,
  sampled every 60s) climbed from an initial ~56MB up over roughly 9
  minutes to a plateau, then held flat there for the remainder of the
  run: **~65MB total working set / ~29.5MB private working set**, with no
  further growth. That confirms a one-time startup ramp settling into a
  steady state, not a leak. **Private working set** (the part not
  inflated by shared self-contained-.NET-runtime pages, and the fairer
  number to compare against the original) is the one that matters against
  the <30MB target; total WS includes the self-contained .NET runtime's
  own shared pages, which a single-file self-contained publish can't
  shrink below a certain floor regardless of app-level trimming.
  Framework-dependent publish (relying on the machine's installed .NET
  8.0.29 runtime instead of bundling it) has been built and is a real
  option to shrink total WS further if that number matters more than
  install-simplicity; not yet swapped in for a live comparison.
- **Original (JONSBO PC Monitor, Electron), for reference:** 3.20% of one
  core continuously (see above), no working-set figure collected — the
  process is split across 5+ OS processes so a single comparable number
  isn't meaningful the same way.
