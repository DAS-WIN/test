# 自動驗證

## 核心測試

```powershell
dotnet test tests/SrbMinerMonitor.Tests -c Release
```

涵蓋 API 讀取、欄位缺失、PCI bus 對應、分享去重、統計表解析、單位格式、BAT 副本與貼上內容、採樣間隔、歷史保留、停止訊號參數，以及可控時鐘下的重啟與復原流程。

所有範例均為合成資料，使用保留網域、一般化裝置名稱及錢包占位值。

## Windows 整合測試

```powershell
pwsh ./scripts/Smoke-Windows.ps1
```

以沒有附掛主控台的 WinExe 主機載入實際 WPF 視窗，透過不挖礦的測試程序提供本機 HTTP API、感測器數值與 stdout／stderr。

檢查內容包括三頁繪製、資料繫結、BAT 兩種來源、DPAPI 保存、背景輸出、分享即時更新、硬體間隔、程序樹擁有權及實際停止／重啟按鈕。排程驗證實際等待一分鐘運行及一分鐘停止間隔；異常復原驗證獨立十秒等待、手動停止取消，以及定時排程遇到退出後的完整等待。

啟停與復原過程確認 GUI PID 不變、視窗持續可見且沒有附掛礦工主控台。畫面與紀錄輸出至 `artifacts/screenshots`。

## 發布檢查

```powershell
pwsh ./scripts/Verify-ReleaseContent.ps1
pwsh ./scripts/Publish-Windows.ps1
pwsh ./scripts/Verify-PublishedStartup.ps1
```

CI 先執行原始碼內容檢查、核心測試、完整建置及 Windows 整合驗證，再打包可攜式程式並啟動實際 EXE，確認視窗完成繪製後正常退出。原始碼與發行封裝另檢查沒有本機設定、個人日誌、暫存 BAT 或對話連結。

自動驗證使用合成測試程序，不代表所有硬體、驅動、礦工版本與外部礦池組合均已驗證。實機驗收項目見 [Windows 驗收](WINDOWS-VALIDATION.md)。
