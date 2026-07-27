<#
.SYNOPSIS
  Removes the JonsboMonitorAlt scheduled task and stops the running process.

.DESCRIPTION
  Only ever touches the "JonsboMonitorAlt" task created by install.ps1 —
  never the vendor's "JONSBO_PC_Monitor" task, which this project does not
  manage and leaves exactly as found (disabled).
#>
$ErrorActionPreference = "Stop"

$taskName = "JonsboMonitorAlt"

$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($task) {
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "Removed scheduled task '$taskName'."
} else {
    Write-Host "Scheduled task '$taskName' not found (already removed?)."
}

Get-Process -Name "JonsboMonitor" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping running JonsboMonitor.exe (PID $($_.Id))..."
    Stop-Process -Id $_.Id -Force
}

Write-Host "Done. The panel will stay on its last frame (no active updates) until you re-run install.ps1 or re-enable the original JONSBO_PC_Monitor task yourself."
