# CyberPaste

A lossless shared clipboard for your LAN — copy on one PC, paste on another as if it were local. Text and images ride the normal clipboard; files and folders are streamed at full speed over a dedicated channel.

<!-- -->

同一區網內的無損共享剪貼簿——在一台電腦複製，到另一台直接貼上，就像本機一樣。文字與圖片走一般剪貼簿；檔案與資料夾走專用高速通道全速傳輸。

## Features / 功能

- **Text** — Unicode-safe plain-text sync.
- **Images** — lossless PNG, no re-compression, no blur.
- **Files & folders — high-speed bulk transfer.** Copy a folder on one PC; on another, paste it into any folder using **Ctrl+V, right-click Paste, or the toolbar Paste button**. CyberPaste streams the whole set over one dedicated connection into the folder you pasted to, with its own progress window and an overwrite prompt. No Explorer conflict dialogs.
- **Zero-config discovery** — peers on the same LAN find each other automatically (UDP broadcast plus per-subnet directed beacons that self-heal).
- **Automatic best-path selection** — wired > Wi-Fi > virtual > VPN. It avoids Hamachi/VPN, picks the fastest link, and fails over to the next path mid-transfer.
- **Byte-level resume** — a dropped link resumes from the exact offset, only re-sending what was left.
- **Tray-only UI** — a single system-tray icon; open means the channel is on.

<!-- -->

- **文字** — 保留 Unicode 的純文字同步。
- **圖片** — 無損 PNG，不重新壓縮、不糊掉。
- **檔案與資料夾 — 高速大宗傳輸。** 在一台電腦複製資料夾；到另一台，用 **Ctrl+V、右鍵貼上、或工具列的貼上按鈕** 貼進任意資料夾。CyberPaste 會用一條專用連線把整批檔案全速串流進你貼上的那個資料夾，附自己的進度視窗與覆蓋詢問，不會跳 Explorer 的衝突框。
- **零設定探索** — 同區網的電腦自動互相發現（UDP 廣播，加上對各網卡子網的定向 beacon，可自我修復）。
- **自動選最佳通道** — 有線 > Wi-Fi > 虛擬 > VPN。自動避開 Hamachi/VPN、走最快的線，傳輸中若斷線會自動切換到次佳路徑。
- **位元組級續傳** — 線路中斷後從精確位移接回，只補傳剩下的部分。
- **只有系統匣介面** — 一顆系統匣圖示；開啟就代表通道打開。

## How it works / 運作原理

Each PC runs a tray agent that watches the local clipboard and mirrors changes to discovered peers. Text and images go straight onto the clipboard. For files, the receiver places a tiny hidden marker file on its clipboard; when you paste it into a folder (by any method), CyberPaste detects it, streams the real files over a dedicated TCP connection into that folder, then removes the marker. Because Explorer never copies the real files itself, there are no conflict dialogs and any paste method works.

<!-- -->

每台電腦跑一個系統匣常駐程式，監看本機剪貼簿並把變化鏡像給已發現的夥伴。文字與圖片直接進剪貼簿。檔案則是：接收端在剪貼簿放一個隱藏的小標記檔，當你用任何方式把它貼進某個資料夾，CyberPaste 偵測到後就用一條專用 TCP 連線把真正的檔案串流進那個資料夾，再把標記檔移除。因為 Explorer 從頭到尾不碰真正的檔案，所以不會有衝突框、任何貼上方式都能用。

## Usage / 使用方式

1. Run `CyberPaste.exe` on each PC (administrator rights required).
2. The first time, allow it through Windows Firewall on private networks.
3. Copy on PC A; on PC B, paste into any folder. Text and images paste as usual; a folder starts a high-speed transfer with its own progress window.

<!-- -->

1. 在每台電腦執行 `CyberPaste.exe`（需要系統管理員權限）。
2. 第一次執行時，在 Windows 防火牆允許私人網路。
3. 在 A 機複製；到 B 機，貼進任意資料夾。文字圖片照常貼上；資料夾則會開始高速傳輸並顯示自己的進度視窗。

## Build / 編譯

CyberPaste needs only the .NET Framework 4.x compiler that ships with Windows — no SDK, no dependencies. Run `build.bat` and it produces `build/CyberPaste.exe`.

<!-- -->

CyberPaste 只需要 Windows 內建的 .NET Framework 4.x 編譯器——不用 SDK、無外部相依。執行 `build.bat` 就會產生 `build/CyberPaste.exe`。

## Ports & Security / 連接埠與安全

UDP 45888 for discovery, TCP 45889 for transfer. Designed for a trusted LAN; traffic is not encrypted.

<!-- -->

UDP 45888 用於探索，TCP 45889 用於傳輸。為可信任的內網設計，流量不加密。

## Changelog / 更新日誌

- **v1.3.0** — Baseline. Two-way text / image / file sync across machines using OLE delayed-rendering virtual files, plus file logging.
- **v1.3.1** — Self-healing discovery: per-subnet directed beacons let peers rediscover each other automatically, fixing the "can't transfer a second time" bug. Added crash / freeze logging and a heartbeat.
- **v1.3.2** — Connection pooling: reuses ~8–10 long-lived connections instead of one TCP connection per file, plus Wi-Fi unicast keep-alive.
- **v1.3.3** — Byte-level transparent resume: a dropped link resumes from the exact offset and only re-sends the remaining bytes, invisible to Explorer.
- **v1.3.4** — Automatic best-path selection (wired > Wi-Fi > virtual > VPN, avoids Hamachi) and automatic failover to the next-best path mid-transfer.
- **v1.3.5** — Renamed to CyberPaste; system-tray menu polish.
- **v1.4.2** — High-speed bulk file transfer. Files now stream over one dedicated connection into the folder you paste to, with CyberPaste's own progress window and an overwrite prompt — no Explorer conflict dialogs. Paste with Ctrl+V, right-click Paste, or the toolbar Paste button (a tiny hidden marker file is detected by a drive watcher that survives repeated multi-GB transfers). Measured ~0.9–1.1 Gbps over a VM NAT link. (current release)

<!-- -->

- **v1.3.0** — 基準版。以 OLE 延遲渲染虛擬檔達成跨機的文字／圖片／檔案雙向同步，並加入檔案日誌。
- **v1.3.1** — 探索自我修復：對各網卡子網發定向 beacon，讓夥伴自動重新發現彼此，修好「傳完一次後第二次傳不動」的問題。加入崩潰／凍結記錄與心跳。
- **v1.3.2** — 連線池：以約 8～10 條長連線反覆重用，取代「一個檔一條 TCP 連線」，並加上 Wi-Fi 單播保活。
- **v1.3.3** — 位元組級透明續傳：線路中斷後從精確位移接回，只補傳剩餘的 bytes，對檔案總管完全隱形。
- **v1.3.4** — 自動選最佳通道（有線 > Wi-Fi > 虛擬 > VPN，避開 Hamachi），傳輸中路徑失效會自動切換到次佳路徑。
- **v1.3.5** — 更名為 CyberPaste；系統匣選單微調。
- **v1.4.2** — 高速大宗檔案傳輸。檔案改用一條專用連線串流進你貼上的那個資料夾，附 CyberPaste 自己的進度視窗與覆蓋詢問——不再跳 Explorer 衝突框。可用 Ctrl+V、右鍵貼上、或工具列的貼上按鈕（放一個隱藏小標記檔，由磁碟監看偵測，且能撐過連續多 GB 傳輸）。VM NAT 連線實測約 0.9～1.1 Gbps。（目前版本）

## Verified publisher / 已驗證發行者

CyberPaste.exe is code-signed by **YangMieh / 小咩**. To have Windows show it as a *verified* publisher on machines you control, run `cert/install-publisher-trust.ps1` as Administrator. See [cert/README.md](cert/README.md) for details.

<!-- -->

CyberPaste.exe 由 **小咩 / YangMieh** 簽章。若要讓 Windows 在你掌控的電腦上把它顯示為「已驗證」發行者，請以系統管理員身分執行 `cert/install-publisher-trust.ps1`，詳見 [cert/README.md](cert/README.md)。

## Author / 作者

Made by [YangMieh / 小咩](https://github.com/YangMieh).

<!-- -->

由 [小咩 / YangMieh](https://github.com/YangMieh) 製作。
