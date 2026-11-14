# 已知問題與待驗證事項

[回到 README](../README.zh-TW.md)

- `HardwareSensors.cs` 的 CPU 風扇與機殼風扇分配，是「Super I/O 回報的前兩個
  風扇感測器」這種憑經驗猜的規則 —— Super I/O 晶片不會可靠標示哪個實體插座
  對應哪個。如果在這張板子上順序相反，就在 `HardwareSensors.Read()` 裡把兩個
  索引對調（搜尋 `fanRpms[0]` / `fanRpms[1]`），或用
  `JonsboMonitor.exe --dump-sensors` 把實際感測器名稱倒出來（見下文）再照名稱
  寫死。
- GPU 感測器名稱因廠商/驅動而異；`BestOf()` 會試幾個常見子字串（"gpu core"、
  "d3d 3d" 等），都找不到就退回「抓到什麼用什麼」，不會直接失敗。
- **CPU VID，已修：** CPU 硬體節點自己的各核心 `"Core #N VID"` 感測器在這張
  板子上*不是*真實電壓 —— 不管負載多少都讀到固定的 ~0.22-0.24V，已用
  `--dump-sensors` 確認。實際量到的 Vcore（負載下 1.2-1.3V，符合 Ryzen 7 7700
  的預期）是由 Nuvoton Super I/O 晶片在 `Motherboard/SubHardware` 下以
  `"Vcore"` 提供。`HardwareSensors.Read()` 現在優先讀 Super I/O 的 `Vcore`，
  只有在板子完全沒有 Super I/O Vcore 讀數時，才退回 CPU 節點那個（大概還是錯的）
  VID 感測器。
- **RAM 時脈，已修：** 不是每張主機板的 Super I/O 都會透過 LHM 提供記憶體時脈
  感測器（這張就沒有）。後備改用 WMI 的
  `Win32_PhysicalMemory.ConfiguredClockSpeed`，它回報 DDR 資料率 MT/s
  （這組 DDR5-6000 EXPO 是 6000）；真正的時脈只有一半（3000MHz，DDR 是
  雙倍資料率）。我們刻意報 MT/s：Windows 工作管理員和多數消費級監控工具就是用
  這個數字標成 DDR 記憶體的「MHz」，這樣跟使用者旁邊其他工具看到的才一致 ——
  跟 `FrameBuilder.cs` 裡華氏 `TempEnd` 那個怪癖也是同一種「連怪癖都照著相容」的考量。如果在
  某張板子上還是讀到 0，就是 WMI 本身沒回報任何已安裝記憶體的設定時脈。
- `JonsboMonitor.exe --dump-sensors` 會把 LHM 看得到的每個感測器（硬體 / 名稱 /
  類型 / 數值）印到 stdout 和 exe 旁的 `sensor-dump.txt`，然後結束 —— 上面那些
  真正的 Vcore/RAM 時脈感測器名稱就是這樣找出來的。新板子上任何欄位看起來不對
  時都很好用：跑一下、`grep` 你要找的欄位，再把 `HardwareSensors.cs` 裡對應的
  `BestOf(...)` 指到實際的感測器名稱。
