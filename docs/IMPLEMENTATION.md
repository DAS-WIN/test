# 程式架構

專案採用 C#、.NET 10、WPF 與 MVVM。介面分為總覽、挖礦設定及事件紀錄，核心不依賴 WPF。

## 模組

| 模組 | 責任 |
| --- | --- |
| Models / Display | 監控資料、設定、單位及畫面格式 |
| IMinerProcess / MinerController | 程序抽象與序列化的啟停、重啟、異常復原 |
| RestartScheduler | 使用單調時鐘管理運行、停止、等待、啟動與異常復原倒數 |
| MinerProcess / WindowsBatchProcess | BAT 啟動、Job 程序樹擁有權、輸出擷取與暫存檔清理 |
| ConsoleStopSignal | 以獨立輔助程序傳送受管理群組的停止訊號 |
| BatchFileInspector / PastedBatch | 解析礦池位址、建立 API 監控副本及暫存 BAT |
| ApiReader / ApiParser | 讀取本機 JSON API，轉換為監控快照 |
| NvidiaSensors | 取得 NVIDIA 感測資料並依 PCI bus 合併 |
| MinuteMetrics / MetricRefreshOptions | 依設定間隔同步更新全部硬體資訊 |
| MetricHistory / TrendChart | 保存六小時樣本及繪製趨勢 |
| SettingsStore / StartupDiagnostics | 設定保存、DPAPI 與本機診斷 |
| MainViewModel / Views | 命令、畫面狀態、資料繫結與三頁介面 |

## 程序生命週期

BAT 以暫停狀態建立，先加入 Windows Job 再恢復，確保子程序不會在指派 Job 前逸出。Job 追蹤包含 `start /b` 的後代程序；只有受管理的程序樹能被停止。stdout 與 stderr 透過管線讀取，stdin 使用 NUL，避免背景 BAT 的 PAUSE 等待輸入。

停止時由獨立輔助程序發送 CTRL_BREAK；GUI 不附掛礦工主控台。十秒後仍未退出則終止受管理 Job，確認全部退出及 API 連接埠釋放後才允許新程序。

所有生命週期操作共用非同步鎖。手動停止取消排程與復原倒數；手動重啟重新計算已啟用的運行時間。非預期退出時，定時排程使用設定的等待分鐘；沒有定時排程而啟用異常復原時使用十秒等待。自動啟動失敗會再次等待，停止失敗則顯示錯誤並禁止重複啟動。

## 資料更新

API 請求與硬體查詢序列化執行。分享計數、輸出文字與硬體採樣分開更新；同一份硬體快照供總覽卡片、GPU 列及趨勢使用。新程序以獨立 session 重設計數及圖表分界，斷線不補造樣本。

設定以暫存檔寫入後替換。貼上 BAT 的內容以 Windows DPAPI 保護，運行期間的暫存 BAT 在停止後清理。程式重新開啟時載入設定，不自行啟動礦工或排程。
