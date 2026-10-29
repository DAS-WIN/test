# 新增挖礦程式支援

正式轉接器隨應用程式發布。核心不載入外部 DLL，也不下載礦工程式。首版註冊清單只有 SRBMiner-Multi。

## 模組入口

實作 `IMinerAdapter` 並在 `MinerAdapterRegistry.Default` 的組合入口註冊。使用穩定、區分大小寫的識別碼；未知識別碼必須報錯，不能回退到其他解析器。

`Descriptor` 宣告名稱、實際完成實機驗收的版本、可提供能力、原生硬體欄位與 API 輪詢間隔。未實機確認的版本不要列入 `VerifiedVersions`；目前 SRBMiner 的此清單為空，表示現有合成資料回歸不等同實機驗收。

`ValidateSettings` 檢查設定及 BAT 是否對應該程式；`PrepareLaunch` 回傳 `MinerLaunchPlan`，不可啟動程序或建立暫存檔。API 參數、監控副本及礦池位址的語法規則都放在該轉接器內。共用 `MinerProcess` 執行計畫、持有 Job 並清理暫存檔。

## 監控契約

每次 `CreateMonitorSession` 建立獨立物件，包含該階段的解析器及累積計數。`ReadAsync` 負責協定、路徑、驗證、格式轉換、完整請求逾時、回應大小限制與取消。僅連本機 API；不要跟隨重新導向或將機密寫入診斷。

回傳 `MinerSnapshot`，裝置放入 `Gpus` 建構參數（沿用 0.2.0 原始碼相容名稱；共用讀取入口為 `Devices`），演算法放入 `Algorithms`。裝置允許 CPU/GPU；填寫 UUID 或完整 PCI 位址。演算法速率使用基礎單位，如 H/s、Sol/s，不預先縮放。每裝置速率的 key 必須對應裝置 ID。`DeviceBuses` 是舊 API 只有 bus 資訊時的保守對應提示；新轉接器優先提供完整硬體識別。

`ConsumeOutput` 只消費本階段新增輸出。回傳正規化文字及實際可辨識的分享事件；一般錯誤、API 逾時及計數變化不產生分享事件。`GetShares` 定義自身 API/輸出範圍與合併策略，不可直接相加兩份累積值。`ResetCounters` 清除該階段計數，`DisposeAsync` 釋放通訊資源。

`MonitoringService` 管理階段取消、舊回應丟棄、單次讀取互斥、裝置投影、硬體採樣及歷史。API 分享更新不受硬體採樣間隔限制。不同演算法不加總；多演算法目前保留集合，但不宣稱 SRBMiner 已支援雙挖。

## 專用設定與畫面

共用設定頁保留 BAT、工作目錄、礦機名稱、連接埠及重啟選項。專用欄位存於 `MinerSettings.AdapterSettings`。

在 App 的礦工 UI 模組呼叫 `MinerOptionsRegistry.Register`，回傳實作 `IMinerOptionsViewModel` 的設定 ViewModel，並在該模組的 ResourceDictionary 定義對應 WPF DataTemplate。`Apply` 將編輯結果寫回設定；既有 ContentControl 顯示該 ViewModel，不在共用頁面依礦工名稱增加條件分支。

缺失資料保留 null，零只代表實際零值。硬體感測器透過 `IHardwareSensorProvider` 獨立接入；不以礦工支援清單推定顯卡感測支援。

## 交付驗證

新增 API/輸出合成 fixture，覆蓋缺值、單位、API/輸出先後順序、重複計數、計數重設、裝置對應及取消。使用共同監控服務驗證，不直接從測試繞過服務讀解析器。

執行核心測試、完整 Solution 建置、Windows Smoke、發布內容檢查及封裝後啟動驗證。最後記錄指定版本的 Windows 實機驗收結果與能力表，再將版本加入已驗證清單。測試礦工與測試轉接器不可打包至正式套件。
