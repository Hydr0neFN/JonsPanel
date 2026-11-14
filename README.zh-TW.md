[English](README.md) · **繁體中文**

> **與 JONSBO 無關聯。** 這是獨立、非官方的重新實作，並非 JONSBO 產品，也未獲
> JONSBO 或其子公司認可，與其沒有任何關聯。「JONSBO」只拿來描述這套軟體相容的
> 硬體。
>
> 開發目的只有一個：跟作者自己擁有的 USB HID 裝置互通。做法是觀察裝置的介面。
> 內容不含任何 JONSBO 原始碼、二進位檔、韌體或資產。
>
> 只針對 [測試硬體](#測試硬體) 列出的那一組硬體驗證過。換成其他面板，行為未知，
> 也沒測過 —— 完整的免責（不提供保固）聲明請看 [LICENSE](LICENSE)。
>
> **權利持有人：** 若您認為這裡有任何內容侵犯您的權利，請看
> [CONTACT.md](CONTACT.md)，我們會儘快處理。

# JonsPanel — JONSBO 機殼螢幕的輕量原生驅動程式

一個沒有 GUI 的原生程式，直接驅動 JONSBO USB HID 機殼面板，不需要原廠的
Electron 應用程式。傳輸協定相同，CPU 與記憶體用量低很多；實測數字見
[總覽](#總覽) 與 [docs/resource-usage.zh-TW.md](docs/resource-usage.zh-TW.md)。

## 總覽

| | |
| :-- | :-- |
| **問題** | 原廠「JONSBO PC Monitor」是 Electron 應用程式，光是讓機殼螢幕持續更新，閒置就吃掉單一核心約 3.2%（Electron + 4 個子處理程序 + `SystemInfos.exe`）。 |
| **我做了什麼** | 用 C#/.NET 8 寫一個沒有 GUI 的原生替代品，直接透過 USB HID 跟同一塊螢幕通訊。沒有 Electron、沒有 Chromium、也沒有額外的感測器輪詢程序。 |
| **結果** | 單一核心約 0.7%，比最佳化前的 1.263% 低；穩定狀態下 private working set 約 29.5MB。 |
| **技術堆疊** | C# / .NET 8、LibreHardwareMonitorLib、HidSharp、Windows 排程工作 |

## 測試硬體

就在下面這組設定上端對端驗證過：

- **面板：** JONSBO 機殼螢幕，USB HID VID `0x5131` / PID `0x2007`
- **主機板：** ASUS TUF GAMING B650M-E WIFI
- **CPU：** AMD Ryzen 7 7700
- **GPU：** AMD Radeon RX 9070 XT
- **作業系統：** Windows 11, build 26200

清單以外的硬體都**沒測過**。感測器名稱（Super I/O 的 Vcore/風扇標籤、GPU 廠商
感測器名稱）是 `HardwareSensors.cs` 靠名稱比對盡量找出來的，會因主機板
廠商與 Super I/O 晶片而不同。換一張板子時，請跑
`JonsboMonitor.exe --dump-sensors`，再把對應的 `BestOf(...)` 指到實際的感測器
名稱。詳見 [限制與待辦](#限制與待辦)。

## 運作方式

```
Windows perf counters / LibreHardwareMonitorLib
              |
              v
   HardwareSensors.cs  ->  SensorSnapshot
              |
              v
   FrameBuilder.cs  ->  64-byte HID output report
              |
              v
   JonsboHidDevice.cs  ->  USB HID VID 0x5131 / PID 0x2007
              |
              v
   case display: temps, %, RPM, clocks, date/time
```

面板**不是**點陣圖或圖形螢幕，也**不是**走 CH340 序列介面（COM9）—— 這兩個都是
專案一開始的假設，後來都證明錯了。它其實是小型數字/段碼顯示器（溫度、%、RPM、
時脈、日期/時間），走原生 **USB HID**（VID `0x5131` / PID `0x2007`），一次送一個
64 位元組的輸出報告。感測器數值預設每 1000ms 更新一次，訊框預設每 200ms 重送一次；
`--interval-ms=N` / `--sensor-ms=N` / `--fahrenheit` 可以改這些預設值。

動傳輸格式之前，請先讀 [PROTOCOL.md](PROTOCOL.md)。不要重新引入
framebuffer/影像管線，另一端沒有任何東西會用到它。

## 開發歷程

一開始的假設是錯的。我以為面板是點陣圖螢幕，也以為它走 COM9 上的 CH340 序列
介面；兩個都錯了，實際上是 USB HID 上的數字段碼顯示器。

協定是從原廠那份部分未壓縮的 `app.asar` 原始碼逆推出來的，沒有靠猜，也沒有靠
模糊測試。Python 原型（`prototype/test_frame.py`）送出同一份訊框，確認真面板
會亮、會顯示。但這個確認有它的極限：只驗證了面板*會亮*，沒有驗證 52 個欄位
每一個都落在正確的位元組偏移。

後來拿實際的 Windows 效能計數器，加上 `--dump-sensors` 的診斷傾印，逐欄位比對，
抓到兩個真的 bug。CPU VID 原本讀 ~0.22-0.24V 的假遙測感測器，沒讀到 Super I/O
真正的 Vcore（~1.3V）。RAM 時脈則是直接 0，沒有 WMI 後備。兩個都修好了。

還沒解決的：CPU 風扇與機殼風扇的分配是「Super I/O 回報的前兩個風扇感測器」這種
經驗法則，換板子不保證對得上實體插座。另外，等原型確認面板會不會鎖住最後一個
訊框之後，200ms 的重送間隔應該可以拉長，甚至拿掉，CPU 還能再省。

## 建置與執行

需要 **.NET 8 SDK**（只有 runtime 不夠）；這台機器裝的是 8.0.423，路徑
`C:\Program Files\dotnet\sdk`（用 winget 裝的，`Microsoft.DotNet.SDK.8`）。C#
專案建置沒問題：`dotnet build -c Release`，0 個警告、0 個錯誤。

```powershell
cd src\JonsboMonitor
dotnet publish -c Release -r win-x64 --self-contained true
# Output: src\JonsboMonitor\bin\Release\net8.0-windows\win-x64\publish\JonsboMonitor.exe
```

專案目標是 `net8.0-windows`，刻意不用單純的 `net8.0`；這支程式本來就只跑 Windows
（HID 存取、權限提升 manifest、win-x64 RID），而 `-windows` TFM 會把組件標成
`SupportedOSPlatform("windows")`，這樣 WMI RAM 時脈後備那段就不用逐一壓警告。
根目錄的 `nuget.config` 把套件還原指向專案資料夾裡的 `./packages`。

```powershell
# from the repo root, after publishing
.\install.ps1
```

`install.ps1` 會註冊並立刻啟動一個叫 `JonsboMonitorAlt` 的排程工作。登入時啟動
`JonsboMonitor.exe`，以系統管理員權限執行（LibreHardwareMonitorLib 讀
MSR/SMBus/Super I/O 感測器需要），而且不開主控台視窗。如果原廠的
`JONSBO_PC_Monitor` 工作還開著，它會拒絕安裝，因為兩個程序會搶同一個 USB 裝置。
要讓指令碼順便停用，請加 `-DisableVendorTask`。完整說明見
[docs/build.zh-TW.md](docs/build.zh-TW.md)。

```powershell
.\uninstall.ps1   # removes the JonsboMonitorAlt task and stops the process
```

## 專案結構

專案不大：C# 應用程式在 `src/JonsboMonitor/`，hidapi 驗證腳本在
`prototype/test_frame.py`，協定說明在 `PROTOCOL.md`，建置與安裝腳本放在根目錄。
逐檔說明見 [docs/layout.zh-TW.md](docs/layout.zh-TW.md)。

## 限制與待辦

- **風扇對應：** `HardwareSensors.cs` 用「Super I/O 回報的前兩個風扇感測器」；
  Super I/O 晶片不會可靠標示哪個是實體插座。對相反了就對調 `fanRpms[0]` /
  `fanRpms[1]`，或照 `--dump-sensors` 的名稱寫死。
- **GPU 感測器名稱**因廠商/驅動而異；`BestOf()` 會試幾個常見子字串（"gpu core"、
  "d3d 3d"），都找不到就取第一個。
- **CPU VID（已修）：** 現在優先讀 Super I/O 的 `Vcore`；只有在板子完全沒有
  Super I/O Vcore 時，才退回 CPU 節點自己的 VID。
- **RAM 時脈（已修）：** 改用 WMI 的 `Win32_PhysicalMemory.ConfiguredClockSpeed`
  當後備。它回報 DDR 資料率 MT/s（這組 DDR5-6000 EXPO 是 6000）；真正的時脈
  只有一半（3000MHz，DDR 是雙倍資料率）。刻意報 MT/s，因為工作管理員和多數
  消費級工具就是這樣標 DDR 記憶體的「MHz」，跟使用者旁邊其他工具看到的才一致。
- **重送間隔：** 等原型確認面板會不會鎖住最後一個訊框，200ms 應該可以再拉長。
- **未測硬體：** [測試硬體](#測試硬體) 清單以外的都沒測過。

完整內容：[docs/known-issues.zh-TW.md](docs/known-issues.zh-TW.md)、
[docs/runtime.zh-TW.md](docs/runtime.zh-TW.md)、
[docs/resource-usage.zh-TW.md](docs/resource-usage.zh-TW.md)。

## 授權

本專案自己的程式碼[採用 MIT 授權](LICENSE)。這是跟 JONSBO 硬體互通時觀察到的
傳輸協定所做的獨立重新實作，不含任何 JONSBO 原始碼。`PROTOCOL.md` 用文字描述
觀察到的位元組行為。`FrameBuilder.cs` 逐位元組重現*行為*，因為面板韌體就是要求
精確的位元組值。「JONSBO」只用來標示本專案相容的硬體。詳見 [LICENSE](LICENSE)。
