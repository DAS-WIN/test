# 本次驗證結果

環境：Linux x64，.NET SDK 10.0.403，目標 Windows x64。

| 檢查 | 結果 |
| --- | --- |
| 核心自動測試 | 46 通過、0 失敗、0 略過 |
| 完整 Solution 建置 | 成功，0 警告、0 錯誤 |
| Windows x64 self-contained 發布 | 成功 |
| Windows WPF 煙霧測試 | 通過，已重現預設 Run.Text 綁定唯讀屬性的例外，並驗證 OneWay 修正 |
| Windows 三頁與排程面板繪製 | 通過，1440×900 與 1100×720；無 binding errors |
| 發布後 EXE 一般／示範啟動 | 通過，使用實際 Program.Main 與 Application.Run；視窗完成繪製後正常退出 |
| SRBMiner 3.8.0 真實 GPU／礦池驗收 | 待 Windows 實機 |

測試涵蓋 API 累積分享、缺失欄位、NVIDIA PCI bus 對應、未知感測器、零功耗、接受率、歷史階段與保留、演算法去重、GPU ID、參數分離；以及排程首次起算、完全退出後才等待、手動停止取消、手動重啟重新計時、單次執行、啟停失敗、異常退出、關閉排程與並行啟動。

另以真正的 loopback TCP／HTTP 驗證 JSON 讀取、禁止 HTTP redirect，以及完整回應 body 的 3 秒逾時。所有測試均未執行真正挖礦。

執行命令：

```text
dotnet test tests/SrbMinerMonitor.Tests -c Release
dotnet build -c Release
dotnet publish src/SrbMinerMonitor.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Windows 驗證使用 GitHub Actions 的 Windows Server 2025（10.0.26100）與 .NET 10.0.403。首次修正提交 ff77e05 的 [Windows 執行紀錄](https://github.com/DAS-WIN/test/actions/runs/40684801127) 已通過畫面與發布後 EXE 啟動檢查。這不代表已在使用者的 Windows 11／RTX 5080 上完成實際挖礦測試；其餘項目仍依 WINDOWS-VALIDATION.md 驗收。


## 0.1.2 新增驗證

新增使用者 BAT 的去識別化結構測試：if 檔案檢查、%~dp0、pause、既有參數、API 副本與原始編碼保留。另加入 Windows 端深色樣式、唯讀選單、探測成功／空輸出／stderr／逾時、BAT Job 子程序生命週期與 API 到總覽的整合檢查。Windows 流程由本版本的 CI 實際執行，通過後才會建立 GitHub prerelease，驗證輸出隨版本附上。

## 0.1.3 新增驗證

核心測試包含截圖的成功訊息與統計表格式、ANSI 清理、GPU ID、累積值覆蓋、來源優先／避免重複計數、重啟清空及 BAT 礦池位址。Windows 測試驗證隱藏主控台、stdout/stderr 導入畫面、API 缺少分享時使用輸出、選卡 Accepted、接受率、礦池位址及手動重啟。

排程測試使用實際一分鐘運行與一分鐘等待，檢查先停止、再等待、取得新的礦工 PID，應用程式 PID 不變且視窗持續可見；使用不挖礦、不連外部礦池的測試程序。另保留排程取消、停止失敗與避免雙開的核心測試。完成與否以本版 GitHub Actions 執行紀錄為準。

## 0.1.4 驗證

移除對已刪除示範／表單模式的測試，新增分鐘邊界與同分鐘保持算力、session 更換、API 基準後即時 Accepted、舊回應不倒退、事件分類、明確分享逾時、BAT 貼上與相對路徑測試。

Windows 實際 WPF 驗證啟動無假資料、無示範／表單入口、最近成功分享在算力卡上方且無綠底條；啟動既有與貼上 BAT，核對成功訊息在 2 秒內出現且不推進分鐘算力，事件僅三種分享結果。貼上內容以 DPAPI 保存並可還原，停止後刪除暫存 BAT。另驗證手動與一分鐘運行／一分鐘等待的排程只更換礦工 PID，GUI 不關閉。實際結果以本版 GitHub Actions 紀錄為準。

0.1.5 額外驗證 API 先到、緩衝的成功訊息後到時不重複計數，並再次執行完整 Windows 整合檢查。

## 0.1.6 驗證

核心測試驗證四種單位、正整數限制、舊設定預設 1 分、自訂間隔保存、整份硬體快照在區間內保持，以及 API／文字不同到達順序的分享去重和補全。

Windows 測試額外提供變動的顯卡感測器測試程序：先設為 1 天並暫緩成功文字，確認 API 新成功分享即時出現在最近成功分享，算力／功耗／溫度／風扇／時脈均不變；釋放文字後同一筆補上細節、事件數與 Accepted 不增加。再設定 2 秒，確認所有硬體依新設定刷新且礦工 PID 不變；重讀設定檢查保存成功及零值錯誤提示。另保留 BAT、貼上 BAT、背景程序及排程驗證。實際結果以本版 GitHub Actions 紀錄為準，測試程序不挖礦。

## 0.1.7 停止／重啟驗證

舊 Windows 煙霧測試由 console 子系統執行，可能因本來已有主控台而避開 GUI AttachConsole 的路徑。本版改以實際 WinExe 主機啟動 WPF 測試，並明確斷言 GetConsoleWindow() 為零。使用 WPF 按鈕 Invoke 模式操作總覽的停止與重啟，檢查礦工停止／更換 PID、沒有 ExitRequested、GUI PID 不變且視窗持續可見；每次操作後 GUI 仍沒有主控台。

排程也在同一 WinExe 主機執行完整一分鐘運行、一分鐘等待循環，持續檢查 GUI。測試再暫緩成功分享的 stdout：API 僅更新 Accepted 數字，最近成功分享不改變；原始文字釋放後原樣顯示一次，不產生 API 文字或重複計數。核心測試拒絕零群組、無效程序 ID 與呼叫端自身 PID。實際結果以本版 GitHub Actions 紀錄為準，測試程序不挖礦。
