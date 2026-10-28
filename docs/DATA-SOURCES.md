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
