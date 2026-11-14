# 建置與安裝

[回到 README](../README.zh-TW.md)

## 建置

需要 **.NET 8 SDK**（只有 runtime 不夠）—— 這台機器裝的是 8.0.423，路徑在
`C:\Program Files\dotnet\sdk`（用 winget 裝的，套件是
`Microsoft.DotNet.SDK.8`）。

```powershell
cd src\JonsboMonitor
dotnet publish -c Release -r win-x64 --self-contained true
# Output: src\JonsboMonitor\bin\Release\net8.0-windows\win-x64\publish\JonsboMonitor.exe
```

專案目標是 `net8.0-windows`，刻意不用單純的 `net8.0`。這支程式本來就只跑 Windows
（HID 存取、權限提升 manifest、win-x64 RID），而 `-windows` TFM 會把組件標成
`SupportedOSPlatform("windows")`，這樣 RAM 時脈後備那段 WMI 呼叫（見
[已知問題](known-issues.zh-TW.md)）就不用逐一壓警告。

根目錄的 `nuget.config` 把套件還原指向專案資料夾裡的 `./packages`，不用全機器
共用的 NuGet 快取。

## 安裝

```powershell
# from the repo root, after publishing
.\install.ps1
```

這個指令碼會註冊並立刻啟動一個叫 `JonsboMonitorAlt` 的排程工作。登入時啟動
`JonsboMonitor.exe`，以系統管理員權限執行（LibreHardwareMonitorLib 讀
MSR/SMBus/Super I/O 感測器需要），而且不開主控台視窗。

預設情況下，它不會碰、也不會重新啟用原廠的 `JONSBO_PC_Monitor` 排程工作，那個
工作維持停用。`install.ps1` 如果發現它還開著，會直接**斷言失敗**（拒絕安裝，
不只是跳警告），因為兩個一起跑就是兩個程序搶同一個 USB 裝置。要讓指令碼代為
停用，請加 `-DisableVendorTask`，不然就自己先手動關掉。

```powershell
.\uninstall.ps1   # removes the JonsboMonitorAlt task and stops the process
```
