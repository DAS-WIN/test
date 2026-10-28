# 資料來源與相容性

官方 [Parameters](https://github.com/doktor83/SRBMiner-Multi/blob/master/Parameters) 定義 API、演算法／裝置清單與啟動參數。GUI 使用 `ProcessStartInfo.ArgumentList`，不透過 shell 組合命令。

JSON 對照依 [HiveOS SRBMiner adapter](https://github.com/minershive/hiveos-linux/blob/master/hive/miners/srbminer/h-stats.sh) 的公開使用方式建立：

| 資料 | JSON 路徑 | 處理 |
| --- | --- | --- |
| 版本 | `miner_version` | 字串 |
| 運行時間 | `mining_time` | 秒 |
| 演算法 | `algorithms[0].name` | 第一版要求恰有一個演算法 |
| 總算力 | `algorithms[0].hashrate.now` | H/s；缺少時僅在所有 GPU 有值時加總 |
| 每卡算力 | `algorithms[0].hashrate.gpu.gpuN` | N 為礦工 GPU ID；忽略 total 等 key |
| Accepted | `algorithms[0].shares.accepted` | 直接使用累積值 |
| Rejected | `algorithms[0].shares.rejected` | 不另加 Stale 計算接受率 |
| PCI bus | `gpu_devices[N].bus_id` | 有 device_id 時優先，否則依 adapter 的陣列位置 |
| 型號 | 可選的 `gpu_devices[].name` | 存在才顯示，否則 GPU #N；NVIDIA 可補足 |

`single-algorithm.synthetic.json` 是依公開路徑製作的**合成測試資料**，不是 Windows 3.8.0 API 的真實擷取。沒有 `algorithms` 陣列的舊版 API 與多演算法回應會明確報錯。Windows 真實 JSON／日誌仍待比對。

## NVIDIA 感測器

使用驅動提供的：

```text
nvidia-smi --query-gpu=pci.bus_id,name,power.draw,fan.speed,temperature.gpu,clocks.current.graphics,clocks.current.memory --format=csv,noheader,nounits
```

背景查詢逾時 2 秒。PCI bus 十六進位值對應 API 的十進位 bus ID，不依列舉順序猜 ID。多 PCI domain 出現相同 bus 的歧義，或 API 缺少 bus 時，不填入感測值。`N/A`、不支援、工具不存在都維持未知。

目前無已驗證的 AMD／Intel 感測器介接；算力可從 API 讀取，缺少功耗、效率、風扇、溫度、時脈時顯示「—」。部分 GPU 缺少功耗時，不將部分加總當完整總功耗。最高溫度為已提供資料之最大值。

## 尚未確認的欄位

真實模式不猜測 Stale、分享 Timeout、HW errors、每 GPU 分享、礦池連線狀態、分享延遲或最近成功分享時間的 JSON 路徑，顯示「—／未提供」。API 已連線不等於礦池已連線。顯示的礦池與 Worker 來自設定，不代表外部礦工的實際參數。

stdout／stderr 僅記錄新增事件，不與 API 統計相加。accepted／rejected／connect／error 關鍵字只用於事件分類，不產生分享數值。

文字清單解析支援逐行名稱、編號與常見表格列。辨識失敗會保留既有清單；GPU ID 可手動輸入，演算法只能從已讀取清單選擇。曾以官方 Linux 3.8.0 僅嘗試列出演算法／裝置，環境回報 `Unsupported runtime environment`，未取得可用真實樣本，不能標記實機相容性通過。該礦工沒有放入原始碼或交付套件。

後續請以 Windows 3.8.0 的 API JSON、兩種清單輸出與去識別化分享日誌建立版本 fixture，再擴充已驗證欄位。


## 指定 BAT

BAT 依原始內容執行，啟動工作目錄為 BAT 所在資料夾。GUI 讀取相同 API，不解析錢包來替換使用者參數。未提供礦池／Worker 實際值時顯示「由 BAT 設定」，不使用表單欄位冒充該 BAT 的設定。

監控副本只針對恰好一個可辨識的 SRBMiner 指令插入 API 參數，忽略 echo／rem 與 if 檔案檢查，保留其他原始位元組。複合命令、無法確認的 API 變數或多個礦工命令會要求手動確認。

Windows 整合測試的 FakeMiner 僅提供固定格式的測試 API 與文字清單，不做雜湊運算、不連礦池，也不包含在 GUI 可攜式套件內。

## 0.1.3 輸出補充

使用者截圖提供的真實 3.8.0 輸出格式為 `GPU0[t0] share accepted [ 213ms ][pearlhash][0]`，統計表欄位末端為 A R HW。程式只消費新啟動之受管理程序的 stdout/stderr，不回讀舊日誌。ANSI 顏色碼會移除，逐行成功／拒絕訊息按 GPU 計數，統計表使用累積計數覆蓋；API 相同欄位優先，兩來源不相加。沒有逐卡 API 時，單卡可使用總計，多卡使用明確 GPU ID 的輸出。Stale／Timeout 不從 API 請求或一般錯誤推算。

礦池卡的 BAT 位址僅解析實際 SRBMiner --pool 參數，不從 echo 錢包或任意文字猜測，也不執行變數展開；未取得時顯示等待位址。畫面標示這是 BAT 設定，不宣稱實測礦池已連線。

## 0.1.4 的資料節奏

產品僅使用 BAT；既有表單／演算法偵測與示範資料已移除。API 每秒核對分享及運行資訊；NVIDIA 感測器快取 5 秒。MinuteMetrics 在同一分鐘維持算力，在分鐘切換或 session 更換時接受新樣本，趨勢只加入該樣本。首次樣本即顯示，空白 API 會顯示等待初始化。

本次程序的新輸出逐行辨識 share accepted／rejected／failed／timeout／timed out。事件列表只收這些分享結果，技術與生命週期訊息不混入事件。API 與輸出各自保留累積基準，輸出不從 API 計數上再加一次；兩者用相同 session 的累積最大觀測值校正，避免較舊回應造成倒退或重複加總。

## 0.1.6 即時分享與統一硬體快照

ShareFeed 依 API 的累積 Accepted／Rejected 新增已確認分享，不等待 stdout；文字先到則直接使用文字，API 先到則顯示「API 已確認」，後續以類別和階段內序號補全同一筆。確認時間為應用程式觀測時間，不假稱礦工發送時間，也不產生 API 未提供的 GPU 或延遲。外部礦工首筆歷史總數僅建立基準，不宣稱為新分享；新階段清除對應基準。大量累積跳號僅保留最新 100 筆確認，事件仍有 2000 筆上限。

硬體更新使用 MetricRefreshOptions 的數值與秒／分／時／天，預設 1 分。所有硬體欄位和每卡列一同取自同一快照；API 分享保持獨立。區間按本機時鐘對齊，首次、新階段及間隔修改先採一筆。圖表連線允許的間距依更新間隔調整。儲存間隔不重啟礦工。
