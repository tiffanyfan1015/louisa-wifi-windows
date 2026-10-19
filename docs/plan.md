# Louisa Wi-Fi — Windows 11 移植計畫

## 目標

將 `https://github.com/madeyexz/louisa-wifi` 移植成 Windows 11 背景常駐程式。使用者登入 Windows 後可自動啟動，平常縮在系統匣；偵測到 `LouisaCoffee` 時，依已知門市／BSSID 記錄或候選密碼嘗試連線。不得中斷使用者現有的正常 Wi-Fi 連線。

## 技術方向

- 優先使用 **C# / .NET 10 / WPF**，單一 Windows 系統匣程式（不需要常駐主視窗）。
- Wi-Fi 掃描、BSSID 取得、設定檔管理、連線與事件監聽：Windows Native Wi-Fi (WLAN) API；原型可用 `netsh`，正式版盡量避免解析在地化命令輸出。
- 設定與記錄存於 `%LOCALAPPDATA%/LouisaWifi/`，敏感資料採 Windows DPAPI 或等效使用者範圍保護。
- 發布可執行的 Windows x64 `.exe`（可選擇 self-contained single-file）；提供「隨登入啟動」開關、暫停及退出選項。

## 第一步：審查原始專案（先做，勿猜測）

1. 讀取完整 repo：README、授權、`macos/app/Sources/*.swift`、門市資料、腳本與其他資源。
2. 產出簡短 `docs/PORTING_NOTES.md`：各檔案職責、macOS 專屬 API、可沿用的資料與演算法、需重寫的模組、未知風險。
3. 核實 SSID、門市／密碼資料格式、密碼嘗試順序、BSSID 快取邏輯及更新機制；若與本計畫假設不同，以原始碼為準並記錄差異。
4. 遵守原 repo 授權並保留必要的 copyright / license notice。

## 第二步：Windows MVP

1. 建立 WPF 專案與系統匣圖示，支援「啟用／暫停自動連線」、「立即檢查」、「隨登入啟動」、「結束」。
2. 實作 WLAN API 封裝：列舉無線網卡、掃描 SSID/BSSID、接收連線狀態事件、建立／更新 Wi-Fi profile、連線及逾時處理。
3. 移植門市資料與密碼候選規則；優先使用已成功的 BSSID → 門市／密碼記錄。未知 BSSID 時使用定位候選門市（需權限）或手動選擇門市／密碼。
4. 連線成功後儲存記錄；驗證 Wi-Fi 已連線與網際網路可用性是兩件不同的事，分開顯示狀態。
5. 安全規則：不搶走既有可用 Wi-Fi；使用者可停用自動連線；限制嘗試次數及冷卻時間；不得無限循環猜密碼；只處理使用者授權連線的網路。
6. 提供基本日誌（不明文輸出密碼）、錯誤提示與手動重試。

## 第三步：測試與發布

- **不在路易莎也能做**：建置、啟動／退出、系統匣、開機自啟、一般 Wi-Fi 掃描、使用自有熱點測試連線與重試、資料持久化。
- **到路易莎再驗證**：實際 SSID/BSSID、門市匹配、候選密碼、首次連線、下次自動連線、失敗與網路切換。
- 執行 `dotnet build`、基本單元測試，補充 `docs/README-WINDOWS.md`（需求、建置、使用、權限、限制）。
- MVP 穩定後再考慮統計介面、自動更新與更完整的定位；不要阻擋第一版交付。

## 專案架構（2026-10-19 整理）
```
louisa-wifi-windows/
├── macos/               # 原版 macOS 程式、資源、腳本與 README
├── windows/             # Windows 11 版本
│   ├── LouisaWifi.sln
│   ├── LouisaWifi.App/
│   ├── LouisaWifi.Core/
│   ├── LouisaWifi.Windows/
│   └── LouisaWifi.Tests/
├── data/                # 共用門市資料
├── scripts/             # Windows 建置與共用資料工具
├── docs/                # Windows 使用文件、移植筆記與本計畫
│   ├── README-WINDOWS.md
│   ├── PORTING_NOTES.md
│   └── plan.md
├── README.md            # 專案入口
└── LICENSE
```

## Codex 執行指示

請先檢查目前 repo，完成 `docs/PORTING_NOTES.md`，再逐步實作可編譯的 Windows 11 MVP。優先確保核心掃描／連線／記憶功能，不要先做華麗 UI。每完成一個階段請執行可行的測試並更新文件。若執行環境不是 Windows，請明確標示哪些 API 與 GUI 功能無法在當前環境驗證，勿聲稱已實機測試。遇到不確定的原始行為，先查原始碼，不要自行假設。
