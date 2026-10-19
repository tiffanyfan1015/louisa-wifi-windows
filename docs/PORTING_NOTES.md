# Windows 移植審查

以目前 checkout 的 Swift 原始碼為準，2026-10-14。

2026-10-19 目錄整理：原版程式與資源移至 `macos/`，以下路徑以儲存庫根目錄為基準；Swift 檔案位於 `macos/app/Sources/`。門市資料與抓取腳本仍為兩個版本共用。

## 原始結構

- `main.swift`：AppKit 生命週期、CoreWLAN link 事件、15 秒計時器、URL scheme。
- `Joiner.swift`：掃描、連線、候選去重、BSSID 快取、10 分鐘失敗冷卻、手動中斷。
- `Stores.swift`：JSON 模型、縣市分組與 CoreLocation 距離排序。
- `Locator.swift`：CoreLocation 權限及位置更新。
- `MenuController.swift`：AppKit 選單、SwiftUI 統計視窗、ServiceManagement 登入啟動。
- `Notifier.swift`：UserNotifications；`Tracker.swift`：每分鐘累積使用紀錄；`StatsView.swift`：統計視圖。
- `Updater.swift`：GitHub Release 檢查與 macOS bundle 更新。
- `data/stores.json`：name/address/phone/lat/lng/passwords。候選已由 Python 產生，不需在 C# 重複推導。
- `scripts/fetch_stores.py`：抓官網、整理電話、保留 8–63 字元候選與有效座標。
- `macos/build.sh`、`macos/install.sh`、`macos/app/Info.plist`、`macos/raycast/louisa-wifi.js`：macOS 建置、安裝、權限與腳本整合。
- `.github/workflows/`：macOS CI/發布、每週更新門市資料。Windows 使用獨立 CI。

## 核實行為與差異

- SSID 為 `LouisaCoffee`。候選順序：已知 BSSID，接著 5 公里內最近三店的第一組、各店第二組、各店第三組，再以密碼去重。
- 原版自動流程只要 Wi-Fi 已關聯即退出；README「可上網才保留」不是精確的原始行為。Windows 保守保留任何既有 Wi-Fi，手動入口同樣遵守。
- 原版 `remember` 同時記錄強於 -75 dBm 的附近 BSSID，可能誤認鄰店；Windows 僅記錄實際成功 BSSID。
- 原版手動失敗訊息含密碼，會寫入日誌與 status.json；Windows 不記錄密碼、profile XML 或完整例外內容。
- 原版 learned.json 明文保存密碼；Windows 用 current-user DPAPI 保護整份成功記錄。
- Windows 採事件加低頻檢查、每輪最多 9 次、每次 25 秒、失敗冷卻 10 分鐘；使用者操作可取消後續嘗試。
- 已知 BSSID 不等待地理定位，記錄失效時可手動選店重新嘗試；未知 BSSID 才取得附近候選。
- Windows 初次啟動需使用者啟用自動連線並確認網路使用授權。未知基地台可手動選店；定位候選為可選功能。
- 使用暫時 Wi-Fi profile，避免覆寫使用者既有同名 profile。只支援 WPA2-Personal/AES；不支援的安全性不嘗試。
- MIT 授權與 Copyright (c) 2026 Ian Hsiao 隨發布保留。

## Windows 專屬風險

Native WLAN API 的掃描及 BSSID 受 Windows 定位同意管制。拒絕存取不能當成掃描空結果。
WlanConnect 返回成功只代表接受要求，需等待事件並核實介面、profile、SSID、BSSID。
掃描事件與連線事件來自任意執行緒，必須釋放 native buffers 並避免 callback 阻塞。
多網卡、睡眠喚醒、使用者自行切換網路、驅動差異與同名鄰店需實機驗證。
首次定位可能逾時或精度不足；不使用過舊或低精度位置。

參考：[Wi-Fi 定位權限](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes)、[WlanConnect](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanconnect)。

統計、macOS URL/Raycast 整合與自動更新不屬 Windows MVP；門市資料隨程式版本更新。
