# SRBMiner Monitor 0.1.1

修正 0.1.0 雙擊後視窗未出現、程序退出的啟動問題。

- 將唯讀 LastUpdate 與 Uptime 的 Run.Text 明確設為 OneWay，避免 WPF 預設雙向綁定拋出 InvalidOperationException。
- 修正 TimeSpan 格式的中文字元引用，避免示範資料更新後產生格式錯誤。
- 提供啟動錯誤視窗與 `%LOCALAPPDATA%\SRBMiner Monitor\Logs` 診斷檔，另附 Start-Diagnostics.cmd 收集 .NET host 訊息。
- 系統匣或標題列外觀無法初始化時，保留主視窗供操作。
- 加入 Windows Run.Text 例外重現、單向綁定回歸檢查、三頁畫面與發布後 EXE 的一般／示範啟動驗證。

請先結束舊版程序，再完整解壓縮到新資料夾，執行 SRBMiner Monitor.exe 或 Start-Demo.cmd。套件不包含 SRBMiner 礦工。

Windows 介面啟動與實際 GPU／礦池相容性為不同驗證範圍；實際挖礦與 SRBMiner 3.8.0 感測欄位仍依 WINDOWS-VALIDATION.md 驗收。
