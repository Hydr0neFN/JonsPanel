# 資源用量

[回到 README](../README.zh-TW.md)

**原廠（JONSBO PC Monitor，Electron）：** 這是實測來的，沒有用估算 —— 34.4 小時開機時間裡
累積 3,966 秒 CPU 時間 = 持續佔用**單一核心的 3.20%**，分散在 Electron 主程序、
4 個子程序與 `SystemInfos.exe`。

**這個替代品：** 已建置、正在跑，實際驅動面板。測量指令：

```powershell
Get-Process JonsboMonitor | Select-Object WorkingSet64, PrivateMemorySize64, CPU
```

原本的設計目標是 working set 低於 30MB、持續佔用單一核心低於 0.3%；下面的穩態
數字在 private working set 上很接近，但 total working set（被獨立式 .NET runtime
的基本開銷墊高）和 CPU 都偏高 —— 各項說明如下。

**這些數字是在一台被無關工作壓到 70-90% CPU 的機器上量的，整個測量期間都是
如此**（用 `\Processor Information(_Total)\% Processor Utility` 確認過），機器
當時並不閒置 —— 所以數字偏保守；閒置的機器只會更好。

- **CPU，穩態：** **約單一核心的 0.7%**，比最佳化前的初始值 **1.263%** 低
  （每次輪詢的 LINQ 感測器巡訪改成快取的 `ISensor` 參照、`Update()` 只管實際
  會讀的硬體節點、WMI RAM 時脈查詢改成快取而不是每次輪詢都重查）。另外還做了
  快/慢兩段更新節奏（主機板/Super I/O 的風扇+Vcore 感測器走 5 秒，CPU/GPU/記憶體
  維持 2 秒預設），但它自己單獨省下多少 CPU 還沒單獨跑過 benchmark。
- **Working set，穩態：** 長時間 soak（程序放著不動，每 60 秒取樣一次）從一開始
  約 56MB，大約 9 分鐘後爬到高原，之後整段都持平：**total working set 約 65MB /
  private working set 約 29.5MB**，沒有再長。這證明是啟動時一次性爬升後進入
  穩態，並沒有記憶體洩漏。**Private working set**（不被共用之獨立式 .NET runtime
  分頁墊高的部分，也是跟原版比更公平的數字）才是對 <30MB 目標真正有意義的；
  total WS 含了獨立式 .NET runtime 自己的共用分頁，單檔獨立式發行不管應用程式
  層怎麼修剪，都壓不到某個底線以下。依賴框架的發行（改用機器上已裝的 .NET
  8.0.29 runtime，不打包）已經建置好了，如果 total WS 比安裝簡便更重要，這是
  一個實際選項；還沒換上去做實際比較。
- **原廠（JONSBO PC Monitor，Electron），供參考：** 持續佔用單一核心 3.20%
  （見上文），沒有量 working set —— 它的程序拆成 5 個以上的 OS 程序，單一可比的
  數字沒什麼意義。
