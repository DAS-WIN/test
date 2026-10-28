# 本次驗證結果

環境：Linux x64，.NET SDK 10.0.403，目標 Windows x64。

| 檢查 | 結果 |
| --- | --- |
| 核心自動測試 | 29 通過、0 失敗、0 略過 |
| 完整 Solution 建置 | 成功，0 警告、0 錯誤 |
| Windows x64 self-contained 發布 | 成功 |
| Windows WPF 煙霧測試工具 | 已編譯，尚未在 Windows 執行 |
| SRBMiner 3.8.0 真實 GPU／礦池驗收 | 待 Windows 實機 |

測試涵蓋 API 累積分享、缺失欄位、NVIDIA PCI bus 對應、未知感測器、零功耗、接受率、歷史階段與保留、演算法去重、GPU ID、參數分離；以及排程首次起算、完全退出後才等待、手動停止取消、手動重啟重新計時、單次執行、啟停失敗、異常退出、關閉排程與並行啟動。

另以真正的 loopback TCP／HTTP 驗證 JSON 讀取、禁止 HTTP redirect，以及完整回應 body 的 3 秒逾時。所有測試均未執行真正挖礦。

執行命令：

```text
dotnet test tests/SrbMinerMonitor.Tests -c Release
dotnet build -c Release
dotnet publish src/SrbMinerMonitor.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Windows 測試尚未完成的項目，請依 WINDOWS-VALIDATION.md 驗收。新增 CI workflow 本身不代表遠端已執行。
