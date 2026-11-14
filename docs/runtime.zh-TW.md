# 執行階段行為

[回到 README](../README.zh-TW.md)

- 靠 VID `0x5131` / PID `0x2007` 自動找面板 —— 沒有寫死的 COM 埠，插在哪個 USB
  hub/埠都沒差。
- 找不到裝置時（沒插，或是別款 JONSBO SKU），它記一次 log，然後每 5 秒重試。
  不會忙等。
- 感測器數值預設每 1000ms 更新一次（跟原廠應用程式的節奏一樣）；HID 訊框預設每
  200ms 重送一次（也跟原廠一樣 —— `device_hid.js` 的 `getSendIntervalTime()`
  回傳常數 `200`）。
- `--interval-ms=N` / `--sensor-ms=N` / `--fahrenheit` 可以改預設值。**等 Python
  原型確認面板會不會鎖住最後一個訊框、還是得靠重送才維持狀態**之後，重送間隔
  大概可以拉到遠超過 200ms（例如 1000ms；如果會鎖住，甚至可以整個拿掉）——
  這是繼續壓 CPU 的主要著力點，而且硬體自己撐得住的話，原廠那種 200ms 保活
  迴圈我們根本不需要。
- Log 寫到 exe 旁邊的 `logs/jonsbo-monitor.log`（超過 ~5MB 自動截斷），互動式
  執行時同步鏡射到主控台。建置成 `WinExe`（不是 `Exe`），所以正常啟動（排程
  工作或雙擊）完全不會跳出主控台視窗 —— 不管哪種方式，log 檔才是準的。
- **單一執行個體保護：** 啟動時會取一個具名 mutex
  （`Global\JonsboMonitorAlt`）。如果已經有另一個真的在跑，新的那個會記下這件事
  然後結束（結束代碼 2），不會去搶同一個 USB 裝置。要跳過檢查請加
  `--allow-multiple`（只供測試）。下面的 `--dump-sensors` 和 `--bench-seconds`
  診斷旗標設計上不受這個保護限制 —— 它們只讀感測器，在開 HID 裝置之前就 return，
  也從不寫面板，所以可以跟正式執行個體一起跑。

**跟 `power-profiles\Watch-GameProcess.ps1` 的關係：** 那支指令碼的
`$killProcesses` 清單會在監看的遊戲啟動時，砍掉任何叫 `JONSBO PC Monitor`
（原廠的程序名稱）的東西，理由是比賽期間感測器輪詢程序造成的 DPC 延遲並不
划算。這個替代品的程序叫 `JonsboMonitor`，設計上**不會**對到那一條 —— 這裡沒
把它加進終止清單，`power-profiles\` 裡的東西本專案也一概不碰。這個替代品夠省
（見 [資源用量](resource-usage.zh-TW.md)），比賽期間照樣開著就行，跟它取代的
原廠應用程式不同。
