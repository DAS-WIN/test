# SRBMiner Monitor

繁體中文 Windows 桌面監控程式，採用 C#、.NET 10、WPF 與 MVVM。依「設計 SRBMiner-Multi GUI」最後方案製作深灰／青藍介面，支援單機、多張 GPU、單一演算法。

## 0.1.1 啟動修正

修正 0.1.0 在顯示視窗時退出的問題：WPF `Run.Text` 預設雙向綁定，但最後更新與運行時間為唯讀屬性，現已明確改為單向綁定。另修正繁中運行時間格式，加入啟動錯誤提示與診斷紀錄。

若先前程序仍留在工作管理員，先結束舊版 SRBMiner Monitor，再將新版完整解壓縮到新資料夾執行，勿只替換 EXE。

若仍無法啟動，執行 `Start-Diagnostics.cmd`，查看 `%LOCALAPPDATA%\SRBMiner Monitor\Logs\startup-*.log`。錯誤視窗也會顯示實際紀錄路徑；無法寫入該目錄時會改用 `%TEMP%\SRBMiner Monitor\Logs`。若尚未進入 .NET 程式，診斷啟動器會將 runtime host 訊息保存到 `%TEMP%\SRBMiner-Monitor-host.log`。

## 快速開始

1. 在 Windows 10/11 x64 解壓縮可攜式套件，保留資料夾內所有檔案。
2. 執行 `SRBMiner Monitor.exe`。第一次啟動不會自動挖礦。
3. 要先看畫面，點左下角「開啟示範模式」，或執行 `Start-Demo.cmd`。所有模擬資料都有標示，不啟動真正的礦工。
4. 回到本機監控，進入「挖礦設定」，選擇自己安裝的官方 `SRBMiner-MULTI.exe`。
5. 點「讀取支援演算法」，選擇演算法；設定礦池主機、連接埠、錢包及 Worker。
6. 點「偵測顯卡」並勾選 GPU。若清單格式不相容，可根據 `--list-devices` 結果手動填入 ID，例如 `0,1`。
7. 儲存設定後回總覽點「開始」，或直接使用「儲存並重啟」。

GUI 不包含 SRBMiner。請從 [官方發佈頁](https://github.com/doktor83/SRBMiner-Multi/releases/tag/3.8.0) 另外安裝。預設 API 為 `127.0.0.1:21550`，只控制 GUI 自己啟動的礦工。外部執行的礦工可監控，但不接管啟停。

## 功能

- 總算力、GPU 功耗、效率、最高溫度、每張 GPU 的感測資訊與時脈。
- 每 5 秒更新；API 完整請求逾時 3 秒，不重疊請求。更新失敗保留最後讀值，超過 15 秒標示過期。
- 分享計數使用 API 累積值，不在輪詢時重複加總。缺少的 Stale／Timeout／HW errors 顯示「—」。
- 功耗、風扇、溫度與核心／顯存頻率由本機 `nvidia-smi` 補充，依 PCI bus 對應；不確定時保持未知。
- 選取 GPU 切換分享與趨勢，按「全部 GPU」回到彙總。API 沒有每卡分享時顯示「—」。
- 算力／功耗／溫度趨勢，最近 15 分鐘／1 小時／6 小時；平均值顯示實際樣本跨度，斷線不補值、重啟有分界。
- 演算法讀取、去重、本機快取、10 秒逾時與失敗保留原清單。
- 自動重啟：運行指定時間 → 確認完全停止 → 等待 X 分鐘 → 啟動，可循環或只執行一次。
- 事件篩選／匯出、密碼遮蔽、Windows DPAPI 密碼儲存、系統匣。

## 自動重啟與退出

先啟動 GUI 管理的礦工，再點「自動重啟」設定並套用。第一次倒數從套用開始，後續從新程序成功啟動後開始。手動停止會取消後續啟動；關閉排程讓礦工繼續運行。啟停失敗或非預期退出會暫停排程。

重新開啟 GUI 只還原排程設定，不自行挖礦或啟用排程。此功能不重新啟動 Windows。關閉視窗會縮至系統匣；請選「停止並退出」才真正結束。正常停止超過 10 秒才會終止受管理程序。

## 設定與資料

設定存於 `%LOCALAPPDATA%\SRBMiner Monitor\settings.json`。密碼由目前 Windows 使用者的 DPAPI 保護，不能直接移至其他帳號解密；其他設定與錢包是本機明文。事件保留最近 2000 筆於記憶體，畫面及匯出為目前篩選的最近 500 筆。

「儲存設定」允許未填妥的草稿，不影響運行中的礦工與監控連接埠。「取消變更」還原最後儲存版本。啟動必須有有效執行檔、演算法、GPU、礦池及錢包。礦池測試只驗證 TCP／TLS。

## 建置

需 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
dotnet build SrbMinerMonitor.slnx -c Release
dotnet test tests/SrbMinerMonitor.Tests -c Release
dotnet run --project src/SrbMinerMonitor.App -- --demo
pwsh ./scripts/Publish-Windows.ps1
```

發布腳本產生自帶 .NET runtime 的 Windows x64 資料夾與 ZIP，目標電腦不需安裝 SDK。Linux 可跑核心測試與交叉建置，無法執行 WPF。

詳見 [實作說明](docs/IMPLEMENTATION.md)、[資料來源](docs/DATA-SOURCES.md) 與 [Windows 驗收](docs/WINDOWS-VALIDATION.md)。
