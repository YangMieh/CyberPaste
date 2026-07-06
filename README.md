# CyberPaste

A lossless shared clipboard for your LAN — copy on one PC, paste on another as if it were local. Text, images, and files (real bytes, streamed on demand).

<!-- -->

同一區網內的無損共享剪貼簿——在一台電腦 Ctrl+C，到另一台直接 Ctrl+V，就像本機一樣。支援文字、圖片、檔案（傳真正的 bytes，貼上當下才串流）。

## Features / 功能

- **Text** — Unicode-safe plain-text sync.
- **Images** — lossless PNG, no re-compression, no blur.
- **Files** — delayed rendering: the source streams the real bytes straight into the folder you paste to. No temp copy, no duplication, and Windows' own copy dialog shows the real progress.
- **Zero-config discovery** — peers on the same LAN find each other automatically (UDP broadcast plus per-subnet directed beacons that self-heal).
- **Automatic best-path selection** — wired > Wi-Fi > virtual > VPN. It avoids Hamachi/VPN, picks the fastest link, and fails over to the next path mid-transfer.
- **Byte-level resume** — a dropped link resumes from the exact offset, invisible to Explorer.
- **Tray-only UI** — a single system-tray icon; open means the channel is on.

<!-- -->

- **文字** — 保留 Unicode 的純文字同步。
- **圖片** — 無損 PNG，不重新壓縮、不糊掉。
- **檔案** — 延遲渲染：來源機在你按下貼上的當下，把真正的 bytes 直接串流進你貼上的那個資料夾。無暫存、不雙倍，還沿用 Windows 原生複製視窗顯示真實進度。
- **零設定探索** — 同區網的電腦自動互相發現（UDP 廣播，加上對各網卡子網的定向 beacon，可自我修復）。
- **自動選最佳通道** — 有線 > Wi-Fi > 虛擬 > VPN。自動避開 Hamachi/VPN、走最快的線，傳輸中若斷線會自動切換到次佳路徑。
- **位元組級續傳** — 線路中斷後從精確位移接回，對檔案總管完全隱形。
- **只有系統匣介面** — 一顆系統匣圖示；開啟就代表通道打開。

## How it works / 運作原理

Each PC runs a tray agent that watches the local clipboard and mirrors changes to discovered peers. Files use OLE delayed rendering (`IDataObject` + `CFSTR_FILECONTENTS`), so the paste is carried out by Explorer's own copy engine — the source only feeds bytes when they are actually requested.

<!-- -->

每台電腦跑一個系統匣常駐程式，監看本機剪貼簿並把變化鏡像給已發現的夥伴。檔案走 OLE 延遲渲染（`IDataObject` + `CFSTR_FILECONTENTS`），所以貼上其實是檔案總管自己的複製引擎在執行——來源機只在真正被索取時才餵 bytes。

## Usage / 使用方式

1. Run `CyberPaste.exe` on each PC (administrator rights required).
2. The first time, allow it through Windows Firewall on private networks.
3. Copy on PC A, paste on PC B. That's it.

<!-- -->

1. 在每台電腦執行 `CyberPaste.exe`（需要系統管理員權限）。
2. 第一次執行時，在 Windows 防火牆允許私人網路。
3. 在 A 機複製，到 B 機貼上。就這樣。

## Build / 編譯

CyberPaste needs only the .NET Framework 4.x compiler that ships with Windows — no SDK, no dependencies. Run `build.bat` and it produces `build/CyberPaste.exe`.

<!-- -->

CyberPaste 只需要 Windows 內建的 .NET Framework 4.x 編譯器——不用 SDK、無外部相依。執行 `build.bat` 就會產生 `build/CyberPaste.exe`。

## Ports & Security / 連接埠與安全

UDP 45888 for discovery, TCP 45889 for transfer. Designed for a trusted LAN; traffic is not encrypted.

<!-- -->

UDP 45888 用於探索，TCP 45889 用於傳輸。為可信任的內網設計，流量不加密。

## Author / 作者

Made by [XiaoMie / 小咩](https://github.com/YangMieh).

<!-- -->

由 [小咩 / XiaoMie](https://github.com/YangMieh) 製作。
