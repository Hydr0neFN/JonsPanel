<#
.SYNOPSIS
  Installs JonsboMonitor as a Scheduled Task that starts at logon.

.DESCRIPTION
  Registers a Scheduled Task named "JonsboMonitorAlt" (deliberately NOT the
  same name as the vendor's "JONSBO_PC_Monitor" task, which stays disabled
  and untouched) that launches the published, self-contained
  JonsboMonitor.exe at user logon, running elevated (required by
  LibreHardwareMonitorLib for hardware sensor access) and hidden (no console
  window).

  A scheduled task was chosen over a Windows Service because it needs no
  Service Control Manager registration, needs no LocalSystem/service-account
  plumbing, and is trivially un-installed with one Unregister-ScheduledTask
  call — smaller footprint, matches "runs as ... a scheduled task" from the
  brief.

.PARAMETER PublishDir
  Directory containing the published JonsboMonitor.exe. Defaults to the
  standard `dotnet publish` output path for this project.
#>
param(
    [string]$PublishDir = (Join-Path $PSScriptRoot "src\JonsboMonitor\bin\Release\net8.0-windows\win-x64\publish"),

    # Opt-in only: if the vendor task is found enabled, disable it ourselves
    # instead of just refusing to install. Off by default — disabling someone
    # else's task is not something this script does silently.
    [switch]$DisableVendorTask
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $PublishDir "JonsboMonitor.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "JonsboMonitor.exe not found at '$exePath'. Build it first:`n  cd src\JonsboMonitor`n  dotnet publish -c Release -r win-x64 --self-contained true"
    exit 1
}

$taskName = "JonsboMonitorAlt"

# Assert — not just warn — that the vendor's task stays disabled. Both apps
# fight over the same USB device, and install.ps1 must never be the thing
# that lets the vendor task run alongside this replacement.
function Assert-VendorTaskDisabled {
    $vendorTask = Get-ScheduledTask -TaskName "JONSBO_PC_Monitor" -ErrorAction SilentlyContinue
    if (-not $vendorTask) {
        Write-Host "Vendor task 'JONSBO_PC_Monitor' not present. OK."
        return
    }
    if ($vendorTask.State -eq "Disabled") {
        Write-Host "Vendor task 'JONSBO_PC_Monitor' is Disabled. OK."
        return
    }

    if ($DisableVendorTask) {
        Write-Host "Vendor task 'JONSBO_PC_Monitor' is $($vendorTask.State) — disabling it (-DisableVendorTask was passed)."
        Disable-ScheduledTask -TaskName "JONSBO_PC_Monitor" -ErrorAction Stop | Out-Null
        $recheck = (Get-ScheduledTask -TaskName "JONSBO_PC_Monitor").State
        if ($recheck -ne "Disabled") {
            throw "Tried to disable JONSBO_PC_Monitor but its state is still '$recheck'. Refusing to install — the two apps would fight over the same USB device."
        }
        Write-Host "Vendor task confirmed Disabled."
        return
    }

    throw "JONSBO_PC_Monitor scheduled task is NOT disabled (state: $($vendorTask.State)). Refusing to install: running both apps at once will fight over the same USB device. Disable it yourself, or re-run with -DisableVendorTask to have this script disable it for you."
}

Assert-VendorTaskDisabled

$action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $PublishDir
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:UserName -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -Hidden

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Write-Host "Task '$taskName' already exists, replacing it."
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings `
    -Description "Lightweight native replacement for JONSBO PC Monitor (see README.md). Drives the case display over USB HID directly; no Electron." `
    | Out-Null

Write-Host "Installed scheduled task '$taskName'."
Write-Host "Starting it now (it will also auto-start at every future logon)..."
Start-ScheduledTask -TaskName $taskName

Start-Sleep -Seconds 2
$state = (Get-ScheduledTask -TaskName $taskName).State
Write-Host "Task state: $state"
Write-Host "Log file: $PublishDir\logs\jonsbo-monitor.log"

# Final assertion, not just the pre-check: confirm nothing during this run
# re-enabled the vendor task out from under us.
Assert-VendorTaskDisabled
