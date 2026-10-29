# 資料來源

## SRBMiner JSON API

API 讀取固定使用 `127.0.0.1` 與設定連接埠，預設 21550。完整請求逾時三秒、回應上限 2 MB，停用 HTTP Proxy 及重新導向，不允許重疊讀取。

欄位格式參考 [SRBMiner 參數文件](https://github.com/doktor83/SRBMiner-Multi/blob/master/Parameters)及 [HiveOS adapter](https://github.com/minershive/hiveos-linux/blob/master/hive/miners/srbminer/h-stats.sh)。

| 資料 | JSON 路徑 |
| --- | --- |
| 版本 | `miner_version` |
| 運行時間（秒） | `mining_time` |
| 演算法 | `algorithms[0].name` |
| 總算力（H/s） | `algorithms[0].hashrate.now` |
| 每卡算力（H/s） | `algorithms[0].hashrate.gpu.gpuN` |
| 累積成功／拒絕 | `algorithms[0].shares.accepted` / `rejected` |
| GPU ID 與 PCI bus | `gpu_devices[].device_id` / `bus_id` |

目前要求恰有一個演算法。缺失欄位維持未知，無效 JSON 或不支援的結構會顯示錯誤，不補造數值。只有所有 GPU 算力均已知時才以每卡值加總替代缺失的總算力。

## 礦工輸出

程式只讀取受管理程序本次運行的 stdout／stderr，不回讀舊日誌。移除 ANSI 顏色碼後辨識 `share accepted`、`share rejected`、`share failed`、`share timeout` 與 `share timed out`。統計表的 A／R／HW 欄位按 GPU ID 讀取累積計數。開發費用分享不計入。

API 與輸出分別維護累積值，同一階段採用最大已知觀測值，避免兩來源重複加總或舊回應造成倒退。單卡可以總計補足每卡統計；多卡需要明確的 GPU ID。未提供的 Stale、Timeout 或 HW errors 不從一般錯誤推算。

最近成功分享與事件只使用實際輸出文字，API 不產生文字事件。API 連線逾時不當作分享逾時。

## NVIDIA 感測器

硬體採樣時背景執行驅動提供的指令，逾時兩秒：

```text
nvidia-smi --query-gpu=pci.bus_id,name,power.draw,fan.speed,temperature.gpu,clocks.current.graphics,clocks.current.memory --format=csv,noheader,nounits
```

PCI bus 十六進位值對應 API 的十進位 ID，不依列舉順序推測。對應不明、不支援、工具不存在或 `N/A` 時保留未知。部分 GPU 缺少功耗時不顯示部分加總；效率在功耗未知或為零時維持未知。目前未提供 AMD／Intel 感測器介接。

## BAT 與採樣間隔

礦池位址來自實際 SRBMiner 指令的 `--pool` 參數，不執行變數展開，不將位址顯示當作礦池連線確認。監控副本只對單一可辨識指令補上 API 參數；複合命令、多個礦工指令或無法辨識的 API 變數會要求手動設定。

全部硬體欄位依設定的秒／分／時／天統一採樣，預設每分鐘邊界。首次、新階段及間隔變更立即採樣。API 分享每秒核對，文字輸出到達時立即處理，均不受硬體間隔限制。

測試目錄僅包含合成 API、合成輸出及保留網域下的測試位址。測試程序不執行雜湊運算，也不連接礦池。
