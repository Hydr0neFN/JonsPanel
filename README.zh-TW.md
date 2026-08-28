[English](README.md) · **繁體中文**

> **與 JONSBO 無關聯。** 這是獨立、非官方的重新實作，非 JONSBO 產品，且未獲 JONSBO 或其子公司認可或與其存在任何關聯。「JONSBO」僅作為描述性使用，用以識別此軟體相容之硬體。
>
> 本軟體之開發純粹是為了與作者所擁有的 USB HID 裝置達成互通性，透過觀察該裝置的介面開發而成。其中不包含任何 JONSBO 原始碼、二進位檔案、韌體或資產。
>
> 僅針對 [測試硬體](#測試硬體) 下列出之特定硬體進行驗證。在任何其他面板上的行為皆屬未知且未經測試 —— 關於完全不提供保固之詳細內容請參閱 [LICENSE](LICENSE)。
>
> **權利持有人：** 若您認為此處有任何內容侵犯您的權利，請聯絡 ruanyouyi@hotmail.com，我們將會儘速處理。（另請參閱 [CONTACT.md](CONTACT.md)。）

# JonsPanel — 適用於 JONSBO 機殼螢幕的輕量原生驅動程式

一個無 GUI 的原生程式，可直接驅動 JONSBO USB HID 機殼面板，無需使用原廠的 Electron 應用程式。採用相同的傳輸協定，大幅降低 CPU/記憶體用量：請參閱下方的 [資源用量](#資源用量)，查看與所取代的原廠應用程式之實測數據對比。

## 測試硬體

已在此完全相同的設定組態上端對端驗證運作正常：

- **面板：** JONSBO 機殼螢幕，USB HID VID `0x5131` / PID `0x2007`
- **主機板：** ASUS TUF GAMING B650M-E WIFI
- **CPU：** AMD Ryzen 7 7700
- **GPU：** AMD Radeon RX 9070 XT
- **作業系統：** Windows 11, build 26200

超出該清單的任何硬體皆**未經測試**。感測器來源名稱（Super I/O Vcore/風扇標籤、GPU 廠商感測器名稱）是在 `HardwareSensors.cs` 中透過盡力而為的名稱比對解析，且在不同主機板廠商與 Super I/O 晶片之間存在差異 —— 在不同的主機板上，您可能需要執行 `JonsboMonitor.exe --dump-sensors`（見下文），並將相關的 `BestOf(...)` 呼叫重新指向該主機板的實際感測器名稱。特別是 CPU 風扇與機殼風扇的分配採用「Super I/O 回報的前兩個風扇感測器」啟發式方法，無法保證在各主機板上都能一致對應實體插座 —— 請參閱下方的「已知問題與待修飾處」。

作為「JONSBO PC Monitor」Electron 應用程式的原生、無 GUI 替代方案；原廠程式光是為了維持機殼螢幕更新，閒置時就會佔用單一核心約 3.2% 的 CPU（Electron + 4 個子處理程序 + `SystemInfos.exe`）。本專案直接與完全相同的螢幕通訊：無 Electron、無 Chromium、無額外的感測器輪詢處理程序。

**若您打算修改傳輸格式，請先閱讀 [PROTOCOL.md](PROTOCOL.md)。** 簡短版本：該面板**不是**點陣圖/圖形螢幕，且**不是**透過 CH340 序列介面卡（COM9）驅動 —— 這兩者都是本專案最初的假設，且後來都被證實是錯誤的。它是一個小型數字/段碼顯示器（溫度、%、RPM、時脈、日期/時間），透過原生 **USB HID**（VID `0x5131` / PID `0x2007`）驅動，每次發送一個 64 位元組的輸出報告。未來任何進行擴充的人員請注意：不要重新引入圖框緩衝區（framebuffer）/影像管線，另一端沒有任何元件會使用它。

## 狀態

通訊協定完全從原廠自身（部分還原壓縮的）`app.asar` 原始碼進行逆向工程 —— 非盲猜，亦非模糊測試。Python 原型（`prototype/test_frame.py`）發送了從該原始碼衍生出的完全相同訊框，並確認實際面板會點亮並顯示輸出。請注意該確認的誠實限制：它驗證了面板*會點亮*，而非 52 個欄位中的每一個都落在正確的位元組偏移量上。自那時起，執行中的服務已逐欄位與實際 Windows 效能計數器以及 `--dump-sensors` 診斷傾印（此主機板上每個原始 LHM 感測器名稱/數值 —— 見下文）進行交叉比對，從而發現並修復了兩個實際錯誤：CPU VID 先前讀取的是虛假的 ~0.22-0.24V 遙測感測器，而非 Super I/O 的實際 Vcore（~1.3V），且 RAM 時脈在沒有 WMI 後備方案時只顯示為 0。兩者皆已修復；程式碼中保留的推導理由請參閱下方的「已知問題與待修飾處」。

已安裝 .NET 8 SDK (8.0.423)，且 C# 專案建置完全乾淨 —— `dotnet build -c Release`：0 個警告，0 個錯誤。

## 配置

```
(repo root)
  PROTOCOL.md              byte-level protocol reverse-engineering writeup
                           (interoperability documentation — describes the
                           observed wire format in prose, no vendor source)
  prototype/test_frame.py   Python/hidapi validation script (project-local .venv)
  src/JonsboMonitor/        the actual replacement, C#/.NET 8
    JonsboHidDevice.cs      VID/PID auto-detect + open/write, HidSharp
    HardwareSensors.cs      LibreHardwareMonitorLib -> SensorSnapshot
    FrameBuilder.cs         SensorSnapshot -> 64-byte frame (mirrors device_hid.js exactly)
    Program.cs               retry/backoff loop, CLI flags, logging
  install.ps1 / uninstall.ps1   Scheduled Task management
  nuget.config              keeps `dotnet restore` local to this project folder
```

## 建置

需要 **.NET 8 SDK**（不僅是執行階段）—— 8.0.423 已透過 winget（`Microsoft.DotNet.SDK.8`）安裝於此電腦的 `C:\Program Files\dotnet\sdk`。

```powershell
cd src\JonsboMonitor
dotnet publish -c Release -r win-x64 --self-contained true
# Output: src\JonsboMonitor\bin\Release\net8.0-windows\win-x64\publish\JonsboMonitor.exe
```

目標 Framework 為 `net8.0-windows`，而非純 `net8.0` —— 本應用程式本就僅限 Windows 使用（HID 裝置存取、權限提升資訊清單、win-x64 RID），且 `-windows` TFM 會將組件標記為 `SupportedOSPlatform("windows")`，因此 RAM 時脈後備機制（見下文）所使用的 WMI 呼叫無需逐一抑制警告。

存放庫根目錄下的 `nuget.config` 將套件還原指向此專案資料夾內的 `./packages`，而非全電腦共用的 NuGet 快取。

## 安裝

```powershell
# from the repo root, after publishing
.\install.ps1
```

這會註冊（並立即啟動）一個名為 `JonsboMonitorAlt` 的排程工作，在登入時以提高的權限（必要條件 —— LibreHardwareMonitorLib 需要系統管理員權限以讀取 MSR/SMBus/Super I/O 感測器）且不帶主控台視窗啟動 `JonsboMonitor.exe`。

預設情況下，它刻意**不會**碰觸、重新啟用或以其他方式與原始的 `JONSBO_PC_Monitor` 排程工作互動，該工作會維持停用狀態。若 `install.ps1` 發現該工作仍處於啟用狀態，將會**發出斷言**（拒絕安裝，而非僅發出警告），因為同時執行兩者意味著有兩個處理程序在競爭同一個 USB 裝置。若您希望指令碼代為停用該工作，而非先手動停用，請傳入 `-DisableVendorTask`。

```powershell
.\uninstall.ps1   # removes the JonsboMonitorAlt task and stops the process
```

## 執行階段行為

- 透過 VID `0x5131` / PID `0x2007` 自動偵測面板 —— 無硬編碼的 COM 連接埠，且不依賴其插入的 USB 集線器/連接埠。
- 若未找到裝置（未插上或屬於不同的 JONSBO SKU），它會記錄一次記錄檔並每 5 秒重試一次。絕不會發生忙碌等待。
- 感測器數值預設每 1000ms 重新整理一次（符合原廠應用程式自身的節奏）；HID 訊框預設每 200ms 重新發送一次（亦符合原廠應用程式 —— `device_hid.js` 中的 `getSendIntervalTime()` 回傳常數 `200`）。
- `--interval-ms=N` / `--sensor-ms=N` / `--fahrenheit` CLI 旗標可覆寫預設值。**一旦 Python 原型確認面板是否會鎖定最後一個訊框，或是需要重新發送以維持其狀態**，重新發送間隔很可能可以調高到遠超過 200ms（例如至 1000ms，若具備鎖定功能甚至可完全移除）—— 這是進一步降低 CPU 用量的主要剩餘著力點，且若硬體本身能保持狀態，我們完全不需要原廠那種 200ms 保活風格的迴圈。
- 記錄至 exe 旁的 `logs/jonsbo-monitor.log`（超過 ~5MB 時自動截斷），並在互動式執行時同步鏡像輸出至主控台。建置為 `WinExe`（非 `Exe`），因此一般啟動（排程工作或按兩下）完全不會顯示主控台視窗 —— 無論何種方式，記錄檔皆為真實基準。
- **單一執行個體保護：** 啟動時處理程序會取得具名互斥鎖（`Global\JonsboMonitorAlt`）。若已有另一個實際執行個體持有該鎖，新的執行個體會記錄此事實並結束（結束代碼 2），而非競爭同一個 USB 裝置。傳入 `--allow-multiple` 可略過此檢查（僅供測試）。下方的 `--dump-sensors` 與 `--bench-seconds` 診斷旗標在設計上不受此保護限制 —— 它們僅讀取感測器，在開啟 HID 裝置前就會返回，且絕不會寫入面板，因此可以安全地與實際執行個體並行執行。

**與 `power-profiles\Watch-GameProcess.ps1` 的互動：** 該指令碼的 `$killProcesses` 清單會在受監視的遊戲啟動時終止任何名為 `JONSBO PC Monitor`（原廠處理程序名稱）的項目，其理由是感測器輪詢處理程序在比賽期間所造成的 DPC 延遲代價並不划算。本替代方案的處理程序命名為 `JonsboMonitor`，在設計上**不會**符合該項目 —— 此處未將其加入終止清單，且本專案不會碰觸 `power-profiles\` 中的任何內容。不同於所取代的原廠應用程式，此替代方案足夠輕量（請參閱上方的「資源用量」），可以在遊戲比賽期間持續執行。

## 資源用量

**原廠程式（JONSBO PC Monitor，Electron）：** 實測而非估計 —— 在 34.4 小時的正常運作時間內累積了 3,966 秒的 CPU 時間 = 持續佔用**單一核心的 3.20%**，分散在 Electron 主要處理程序、4 個子處理程序與 `SystemInfos.exe` 之中。

**本替代方案：** 已建置並執行中，實際驅動面板。測量指令：

```powershell
Get-Process JonsboMonitor | Select-Object WorkingSet64, PrivateMemorySize64, CPU
```

最初的設計目標是工作集低於 30MB 且持續佔用單一核心低於 0.3%；下方的穩定狀態數據在私人工作集上接近該目標，但在總工作集（受限於獨立式 .NET 執行階段基本開銷底線）與 CPU 上略高 —— 請參閱下方各項說明。

**這些數據是在測量期間整段時間皆處於非相關工作造成的 70-90% CPU 負載電腦上所測得**（已透過 `\Processor Information(_Total)\% Processor Utility` 確認），而非閒置桌面 —— 因此數據相對保守；閒置電腦上的表現至少會一樣好。

- **CPU，穩定狀態：** **約單一核心的 0.7%**，低於最佳化前的初始值 **1.263%**（每次輪詢的 LINQ 感測器巡訪被替換為快取的 `ISensor` 參照、`Update()` 範圍縮減為僅實際讀取的硬體節點、WMI RAM 時脈查詢被快取而非每次輪詢重新查詢）。進一步的快/慢更新節奏劃分（主機板/Super I/O 風扇+Vcore 感測器採用 5 秒節奏，CPU/GPU/記憶體仍維持 2 秒預設值）已實作，但其單獨帶來的 CPU 差異尚未單獨進行基準測試。
- **工作集，穩定狀態：** 長時間浸泡測試（未受干擾的處理程序，每 60 秒取樣一次）從初始約 56MB 在大約 9 分鐘內上升至高原期，隨後在剩餘的執行時間內維持平穩：**總工作集約 65MB / 私人工作集約 29.5MB**，未再進一步增長。這證實了啟動時的一次性上升並趨於穩定狀態，而非記憶體流失。**私人工作集**（未被共用之獨立式 .NET 執行階段分頁膨脹的部分，也是與原版相比更公平的數據）是針對 <30MB 目標時真正關鍵的指標；總工作集包含了獨立式 .NET 執行階段本身的共用分頁，無論應用程式層級如何修剪，單一檔案獨立式發行都無法將其縮減至特定底線以下。依賴架構之發行版本（依賴電腦上已安裝的 .NET 8.0.29 執行階段而非打包封裝）已建置完成，若該數值比安裝簡便性更為重要，這是一個可進一步縮減總工作集的實際選項；目前尚未替換進行實體比對。
- **原廠程式（JONSBO PC Monitor，Electron），供參考：** 持續佔用單一核心的 3.20%（見上文），未收集工作集數據 —— 該處理程序分散在 5 個以上的作業系統處理程序中，因此單一可比較的數據不具有相同意義。

## 已知問題與待修飾處 / 實際執行時需驗證的事項

- `HardwareSensors.cs` 的 CPU 風扇與機殼風扇分配採用盡力而為的「Super I/O 回報的前兩個風扇感測器」啟發式方法 —— Super I/O 晶片無法可靠標記哪個實體插座對應何者。若在此主機板上順序相反，請在 `HardwareSensors.Read()` 中調換這兩個索引（搜尋 `fanRpms[0]` / `fanRpms[1]`），或使用 `JonsboMonitor.exe --dump-sensors` 傾印實際感測器名稱（見下文）並依名稱寫死。
- GPU 感測器名稱因廠商/驅動程式而異；`BestOf()` 會嘗試比對幾個常見的子字串（"gpu core"、"d3d 3d" 等），並以後備方案「採用第一個找到的項目」替代直接失敗。
- **CPU VID，已修復：** CPU 硬體節點自帶的各核心 `"Core #N VID"` 感測器在此主機板上*並非*真實電壓 —— 無論負載如何，皆讀取到固定的 ~0.22-0.24V（已透過 `--dump-sensors` 確認）。實際測得的 Vcore（負載下介於 1.2-1.3V 範圍，符合 Ryzen 7 7700 的預期）則是由 Nuvoton Super I/O 晶片在 `Motherboard/SubHardware` 下以 `"Vcore"` 公開。`HardwareSensors.Read()` 現在會優先採用該 Super I/O `Vcore` 感測器，且僅在主機板完全沒有 Super I/O Vcore 讀數時，才後備退回使用 CPU 節點（可能依然錯誤）的 VID 感測器。
- **RAM 時脈，已修復：** 並非所有主機板的 Super I/O 都會透過 LHM 公開記憶體時脈感測器（這張主機板就沒有）。後備機制透過 WMI 查詢 `Win32_PhysicalMemory.ConfiguredClockSpeed`，其回報的是以 MT/s 為單位的 DDR 資料傳輸率（此 DDR5-6000 EXPO 記憶體為 6000），而非真實/實際時脈（其一半，即 3000MHz —— DDR 為雙倍資料傳輸率）。我們刻意回報 MT/s 數據：這是 Windows 工作管理員和多數消費級監控工具顯示並標註為 DDR 記憶體「MHz」的數值，因此能符合使用者在其他工具旁預期看到的數值 —— 這與 `FrameBuilder.cs` 中華氏溫度的 `TempEnd` 特異行為具備相同的錯誤相容性考量。若在特定主機板上仍讀取為 0，則表示 WMI 本身未針對任何已安裝的記憶體模組回報已設定的時脈速度。
- `JonsboMonitor.exe --dump-sensors` 會將 LHM 能看到的所有感測器（硬體 / 名稱 / 類型 / 數值）印至 stdout 以及 exe 旁的 `sensor-dump.txt`，然後結束 —— 這是用於找出上述真實 Vcore/RAM 時脈感測器名稱的工具。在任何新主機板上當欄位數值看起來異常時都非常實用：執行它、`grep` 您正在尋找的欄位，並將 `HardwareSensors.cs` 中相關的 `BestOf(...)` 呼叫重新指向實際的感測器名稱。

## 授權與版權

本專案自身的程式碼[採用 MIT 授權](LICENSE)。這是針對與 JONSBO 硬體互通時所觀察到的傳輸協定進行的獨立重新實作 —— 本存放庫中不包含任何 JONSBO 原始碼。`PROTOCOL.md` 以文字描述觀察到的位元組層級行為（欄位偏移量、編碼、已知特異行為），而非重製原廠的 JavaScript；`FrameBuilder.cs` 則使用本專案自身的命名與結構逐位元組重現其*行為*（合法的互通性需求 —— 面板韌體預期收到精確的位元組數值），而非逐行轉譯原廠原始碼。全文使用「JONSBO」僅用於識別本專案相容之硬體。
