# Louisa Wi-Fi — Windows 11

Windows 系統匣程式：搜尋路易莎門市、嘗試店內密碼，並記住成功連線的基地台。
原版 macOS 程式與資源集中在 [`macos/`](macos/README.md)。

## 開啟程式

本機已建置的執行檔：`artifacts/win-x64/LouisaWifi.exe`。
首次啟動預設暫停自動連線；實際連線測試與限制請見使用文件。

## 文件

- [Windows 使用、建置與測試](docs/README-WINDOWS.md)
- [移植審查與行為差異](docs/PORTING_NOTES.md)
- [開發計畫](docs/plan.md)
- [原版 macOS 說明](macos/README.md)

## 專案結構

```text
windows/                  Windows C# / .NET / WPF 程式與測試
macos/                    原版 Swift 程式、截圖、Raycast 與建置／安裝腳本
docs/                     Windows 使用文件、移植筆記與計畫
data/                     兩個版本共用的門市資料
scripts/                  Windows 建置／檢查及共用門市資料更新工具
.github/workflows/        Windows、macOS CI 與資料更新工作流程
artifacts/win-x64/        本機 Windows 發布產物（不納入 Git）
LICENSE                   原專案 MIT 授權
```

## 建置 Windows

在專案根目錄執行，需要 Windows 11 與 .NET 10 SDK：

```powershell
./scripts/build-windows.ps1 -Publish
```

完整操作方式與尚待實機驗證的項目見 [Windows 文件](docs/README-WINDOWS.md)。
原專案：[madeyexz/louisa-wifi](https://github.com/madeyexz/louisa-wifi)，保留 MIT LICENSE 與原作者著作權聲明。
