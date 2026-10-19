# Louisa Wi-Fi for Windows

Windows 11 x64 系統匣 MVP。使用 C# / .NET 10 / WPF，門市資料沿用原專案。

## 使用

1. 將發布資料夾放在固定位置，執行 `LouisaWifi.exe`。self-contained 版本不需另外安裝 .NET。
2. 初次開啟會顯示選店視窗。搜尋門市名稱或地址，確認已獲准使用門市 Wi-Fi，選擇候選或輸入店內密碼。
3. Windows 詢問定位權限時允許；拒絕時可由系統匣「Windows 定位權限設定」調整。
4. 如已連上其他 Wi-Fi，程式保留現有連線。要切換時，請先透過 Windows 自行中斷。
5. 成功後記住該基地台。從系統匣啟用「自動連線」，之後可自動使用記錄。
6. 可選「使用定位尋找附近門市」，對未知基地台嘗試附近 5 公里內最近三店的候選。拒絕定位或無可靠位置時請手動選店。

關閉選店視窗後仍在系統匣執行。右鍵選單提供立即檢查、暫停、取消本次嘗試、隨登入啟動及結束。
首次預設暫停自動連線、停用地理定位與登入啟動。請只用於已獲准使用的網路。

## 建置與測試

以下指令從儲存庫根目錄執行，需要 Windows 11 與 .NET 10 SDK。SDK 若位於專案 `.tools/dotnet`，腳本會優先使用。

```powershell
./scripts/build-windows.ps1
./scripts/build-windows.ps1 -Publish
./scripts/test-windows-smoke.ps1
```

產物：`artifacts/win-x64/LouisaWifi.exe`，連同 LICENSE 與本文件分發。

測試使用不需額外套件的 console runner；`dotnet test` 不會執行這套測試：

```powershell
dotnet run --project windows/LouisaWifi.Tests -c Release
```

測試包含真實 JSON、候選順序／去重、重試上限、既有連線保護、取消／事件重複、冷卻、權限錯誤、BSSID 驗證、XML 逸出及 current-user DPAPI 儲存。
Wi-Fi 流程使用模擬介面，不會切換測試電腦的網路。CI 只建立 artifact，不會自動發布 GitHub Release。
`test-windows-smoke.ps1` 使用已發布的執行檔離線渲染選店畫面到 `artifacts/ui-smoke/window.png`，不呼叫 WLAN 或改動使用者設定。

## 權限、儲存與限制

- 設定及成功記錄：`%LOCALAPPDATA%/LouisaWifi/`。`learned.dpapi` 由目前 Windows 使用者的 DPAPI 保護，不能直接移給其他帳號。
- `settings.json` 不含密碼；`app.log` 不記密碼或 profile XML，大小到 512 KiB 時保留一份舊日誌。
- 登入啟動使用目前使用者的 Run 登錄值 `LouisaWifi`。移動程式位置後請關閉再開啟登入啟動；移除前先關閉。
- 暫時 WLAN profile 不覆寫既有 profile；程式退出時不主動中斷已成功的連線。
- 每輪最多 9 次候選，每次最多等待 25 秒，失敗冷卻 10 分鐘。手動重試可略過冷卻。
- 已知 BSSID 優先直接使用記錄，不等待定位；記錄失效可手動選店重新嘗試候選。
- 每 60 秒檢查及監聽 WLAN 事件；事件限制至少間隔 15 秒，已連線時不掃描。
- 目前支援 WPA2-Personal / AES 與 8–63 個 ASCII 字元密碼。WPA3-only、企業驗證及網頁登入自動化不支援。
- 掃描及 BSSID 可能受 Windows 定位權限限制。手動選店也不能繞過系統權限。
- 定位資料超過 10 分鐘或精度差於 1 公里時，不用來挑選門市。
- 網際網路狀態引用 Windows 系統判定，可能包括 Ethernet/VPN；不是該 Wi-Fi 路徑的獨立連通測試。
- 資料解密失敗或損壞時停止啟動，不覆寫原記錄。先備份資料，再移開損壞檔案可重建。
- 統計、自動更新及完整安裝程式不在 MVP；門市資料隨建置版本更新。

## 實機驗收

2026-10-14 本機驗證：Release 建置無警告／錯誤；自動化測試涵蓋上述案例及 Native 中間失敗／過期事件；額外 Native WLAN handle／網卡列舉測試通過（1 張網卡）；WPF 視窗離線載入與渲染成功，內嵌 519 筆門市。未執行 Wi-Fi 掃描、實際連線或改動目前網路。

下列項目需在適當環境逐項驗證，不能以模擬測試取代：

- [ ] 自有 WPA2/AES 熱點（SSID 設為 LouisaCoffee）：錯密碼、正確密碼、逾時、取消。
- [ ] 關閉／允許 Windows 定位服務、無網卡、Wi-Fi 關閉。
- [ ] 同 SSID 不同密碼的兩個基地台，確認只記住實際 BSSID。
- [ ] 保留既有 Wi-Fi、連線過程使用者自行切換網路。
- [ ] 睡眠喚醒、多網卡、關閉／重開程式後自動連線。
- [ ] 登出登入後自啟、全新未安裝 SDK 的 Windows 啟動。
- [ ] 路易莎現場首次選店、後續 BSSID 自動連線與網際網路狀態。

原始行為與移植差異見 [PORTING_NOTES.md](PORTING_NOTES.md)。保留原 MIT LICENSE。
