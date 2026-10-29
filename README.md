# SRBMiner Monitor 0.3.0

繁體中文 Windows 桌面監控工具，以 BAT 啟動本機 SRBMiner-Multi，集中顯示硬體資訊、分享結果、礦工輸出與重啟狀態。

## 安裝與啟動

完整解壓縮 Windows x64 套件，執行 `SRBMiner Monitor.exe`。套件包含 .NET 執行環境；礦工需另行安裝，可從 [SRBMiner-Multi 官方發布頁](https://github.com/doktor83/SRBMiner-Multi/releases)取得。監控程式啟動時不會自行開始挖礦。

## BAT 設定

挖礦設定提供兩種輸入方式：

- **選擇現有 BAT**：選取 `.bat` 或 `.cmd`。若未啟用 API，可按「建立監控副本」，存於原檔相同資料夾，再使用副本啟動。
- **貼上 BAT 內容**：選擇礦工資料夾並貼上完整內容。啟動時自動補上 API 參數，其他參數保留。

按「儲存並啟動 BAT」開始。礦工在背景執行，輸出整合至總覽，不開啟獨立 CMD 視窗。現有 BAT 的工作目錄為其所在資料夾；貼上內容的工作目錄為選定的礦工資料夾。貼上模式建立暫存 BAT，停止後移除，原檔不被覆寫。

目前支援單一演算法。礦池、錢包及挖礦參數由 BAT 設定；監控程式不調整超頻、風扇或功耗限制。

## 監控與事件

硬體資訊包含算力、GPU 功耗、效率、溫度、風扇與時脈。挖礦設定可輸入 1–10000，選擇秒／分／時／天，預設 1 分鐘。儲存後立即套用，不需重啟礦工。首筆資料、新礦工階段及間隔變更時會立即取得一筆樣本。

Accepted／Rejected 數字每秒核對本機 API，與硬體更新間隔分開。「最近成功分享」及事件紀錄只顯示實際礦工輸出的成功、失敗與分享逾時訊息。API 計數與輸出計數不重複加總；重啟建立新的統計階段。未知資料顯示「—」。

總覽提供 GPU 表格、15 分鐘／1 小時／6 小時趨勢、礦池位址，以及最近 500 行礦工輸出。事件保留 2000 筆，可篩選及匯出最近 500 筆。API 連線逾時不視為分享逾時。

## 停止與重啟

| 操作 | 行為 |
| --- | --- |
| 停止礦工 | 停止受管理的 BAT 與礦工，取消待重啟動作，監控視窗保持開啟 |
| 重啟礦工 | 停止舊礦工後重新啟動，監控視窗保持開啟 |
| 定時自動重啟 | 運行指定時間 → 完全停止 → 等待指定分鐘 → 啟動；可循環或執行一次 |
| 非預期退出自動重啟 | 挖礦設定中獨立啟用；意外退出後等待 10 秒再啟動 |

定時排程啟用時，意外退出依排程的等待時間倒數，兩項同時啟用只啟動一個礦工。自動啟動失敗會重新倒數再試。關閉獨立異常重啟選項並儲存，可取消其待執行動作。

自動重啟沿用目前運行的 BAT 設定，尚未套用的 BAT 修改不會自動生效。程式只控制自身啟動的礦工；外部礦工僅監控。關閉視窗會縮至系統匣，選「結束應用程式」才會停止礦工並退出。

## 本機資料與診斷

設定儲存於 `%LOCALAPPDATA%\SRBMiner Monitor\settings.json`。貼上的 BAT 內容以 Windows DPAPI 加密保存，需由相同 Windows 帳戶解密。硬體更新間隔與異常重啟選項儲存後立即生效，BAT 修改在下次啟動或重啟時套用。

啟動診斷位於 `%LOCALAPPDATA%\SRBMiner Monitor\Logs`，無法寫入時使用暫存資料夾。無法啟動時可執行 `Start-Diagnostics.cmd`。升級請先結束程式，再完整解壓新版至新資料夾。

## 建置

開發需要 .NET 10 SDK；WPF 執行與整合測試需要 Windows。

```powershell
dotnet build SrbMinerMonitor.slnx -c Release
dotnet test tests/SrbMinerMonitor.Tests -c Release
pwsh ./scripts/Smoke-Windows.ps1
pwsh ./scripts/Publish-Windows.ps1
```

詳見[架構](docs/IMPLEMENTATION.md)、[資料來源](docs/DATA-SOURCES.md)、[驗證方式](docs/VERIFICATION.md)及[版本說明](docs/RELEASE-NOTES-0.3.0.md)。

## 可擴充礦工支援

目前正式清單只有 SRBMiner-Multi；設定頁可明確選擇挖礦程式。後續版本可加入獨立轉接器，沿用監控、程序控制與排程。CPU/GPU、演算法集合及速率單位已納入資料模型；這不代表目前已驗證其他礦工或 SRBMiner 雙挖。

舊設定自動以 SRBMiner 載入，首次寫入新版設定前保存備份。未知礦工或較新設定格式會提示更新，不會改用 SRBMiner 解讀。開發方式見 [新增礦工指南](docs/ADDING-MINERS.md)。
