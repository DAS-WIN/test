# SRBMiner Monitor 0.1.5

繁體中文 Windows 桌面監控程式，使用自己的 SRBMiner BAT 啟動礦工。深灰／青藍介面包含總覽、挖礦設定與事件紀錄；本版已移除示範模式，以及演算法、錢包、GPU 等表單啟動方式。

## 使用現有 BAT

1. 完整解壓縮 Windows x64 套件，執行 `SRBMiner Monitor.exe`；啟動 GUI 不會自行挖礦。
2. 挖礦設定選「選擇現有 BAT」，選擇 `.bat` 或 `.cmd`。
3. 如果未啟用 API，點「建立監控副本」，存於原 BAT 相同資料夾；原檔保留，只補 API 參數。
4. 按「儲存並啟動 BAT」。礦工在背景執行，不顯示獨立 CMD 視窗。

## 貼上自己的 BAT

1. 挖礦設定選「貼上 BAT 內容」。
2. 選擇礦工資料夾，貼上完整 BAT；例如使用 `%~dp0SRBMiner-MULTI.exe` 的批次檔，資料夾應包含該執行檔。
3. 按「儲存並啟動 BAT」。程式自動補上 `--api-enable --api-port`，其他挖礦參數保留。

貼上模式會在指定資料夾建立唯一名稱的暫存 BAT，因此 `%~dp0`、`cd /d` 與 `logs` 等相對路徑保有原意；停止後移除暫存 BAT。內容以 Windows 使用者 DPAPI 加密存入設定，不能直接搬至另一個 Windows 帳號解密。原有 BAT 檔案不會被貼上內容覆寫。

GUI 不包含礦工，請自行安裝 [官方 SRBMiner-Multi](https://github.com/doktor83/SRBMiner-Multi/releases/tag/3.8.0)。僅支援單一演算法，不調整超頻或風扇設定。

## 更新頻率與總覽

- 第一筆算力立即顯示，此後在電腦時鐘每分鐘切換時更新總算力、每卡算力與趨勢；新礦工階段也立即更新首筆。
- Accepted 接收到分享訊息就更新，另每秒向本機 API 核對分享累積值；與算力刷新互相獨立。API 請求完整逾時 3 秒，不重疊。
- 功耗、風扇、溫度與時脈由 `nvidia-smi` 每 5 秒補充，依 PCI bus 對應。缺少資料顯示 —，不假造零。
- 最近成功分享位於總算力上方；綠底的成功分享統計條已刪除。Accepted 數仍顯示於顯卡下方的分享統計。
- 礦池只顯示位址與來源。可展開「礦工即時輸出」查看最近 500 行原始輸出。
- 算力／功耗／溫度趨勢可選 15 分鐘、1 小時、6 小時。斷線不補值，重啟有分界。

API 與輸出的累積計數採用同一階段較新的觀測值，不將兩份計數相加。重啟後重新計數。單卡可使用 API 總計，多卡從明確 GPU ID 的輸出取得每卡統計。

## 事件紀錄

只保留分享成功 Accepted、失敗 Rejected／failed、逾時 Timeout。算力表、分隔線、Job received、連線、啟停與排程訊息不加入事件列表或事件匯出。API 請求逾時不當成分享逾時。

事件保留最近 2000 筆，顯示及匯出目前篩選的最近 500 筆。啟動與排程狀態仍在總覽及通知文字顯示；技術錯誤寫入啟動診斷紀錄。

## 重啟與退出

「重啟礦工」只重啟 BAT 及其受管理子程序，GUI 保持開啟。自動重啟為：運行指定時間 → 完全停止礦工 → 等待指定分鐘 → 重新啟動，可循環或只執行一次。手動停止取消排程；停止／啟動失敗或非預期退出會暫停排程。

只控制本程式啟動的礦工；外部礦工僅監控。Windows Job 追蹤 BAT 與 `start /b` 等子程序，先嘗試正常停止，10 秒後才終止受管理程序樹。關閉視窗縮至系統匣；選「停止並退出」才結束。

設定在 `%LOCALAPPDATA%\SRBMiner Monitor\settings.json`；舊版 BAT 路徑與排程設定保留，舊表單欄位忽略。GUI 重新開啟不會自行啟動礦工或啟用排程。

## 建置與診斷

需 .NET 10 SDK；Windows x64 發布包含 runtime，不需另裝 SDK。

```powershell
dotnet build SrbMinerMonitor.slnx -c Release
dotnet test tests/SrbMinerMonitor.Tests -c Release
pwsh ./scripts/Smoke-Windows.ps1
pwsh ./scripts/Publish-Windows.ps1
```

若無法啟動，執行 `Start-Diagnostics.cmd`，查看 `%LOCALAPPDATA%\SRBMiner Monitor\Logs\startup-*.log`，無法寫入時改用 `%TEMP%`。升級時先停止舊版並完整解壓新版到新資料夾，不要只替換 EXE。

Windows 整合測試使用不挖礦、不連礦池的測試程序。真實 Windows 11／RTX 5080 的接受結果需實機試跑。詳見 [版本變更](docs/RELEASE-NOTES-0.1.5.md)、[資料來源](docs/DATA-SOURCES.md) 與 [驗證說明](docs/VERIFICATION.md)。
