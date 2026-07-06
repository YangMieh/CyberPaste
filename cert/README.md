# Verified publisher (optional) / 已驗證發行者（選用）

CyberPaste.exe is code-signed with a self-signed certificate whose publisher is **YangMieh / 小咩**. On a machine that has not installed this certificate, Windows shows it as an *unknown / unverified* publisher — this is normal for independent tools and does not affect how the app works.

If you want the Windows Firewall prompt and the UAC dialog to show **YangMieh / 小咩** as a *verified* publisher, run `install-publisher-trust.ps1` **as Administrator** on the machines you control. It installs the public certificate (`CyberPaste-YangMieh.cer`) into the machine's Trusted Root and Trusted Publishers stores.

Only install this on machines you own and trust — installing a root certificate is a security-relevant action. Uninstall any time from `certlm.msc` (Trusted Root Certification Authorities / Trusted Publishers).

<!-- -->

CyberPaste.exe 以自簽憑證簽章，發行者為 **小咩 / YangMieh**。在沒有安裝這張憑證的電腦上，Windows 會把它顯示為「不明／未驗證」發行者——這對獨立工具是正常現象，不影響程式運作。

若你希望 Windows 防火牆提示與 UAC 視窗把 **小咩 / YangMieh** 顯示為「已驗證」的發行者，請在你自己掌控的電腦上以「系統管理員」身分執行 `install-publisher-trust.ps1`。它會把公開憑證（`CyberPaste-YangMieh.cer`）安裝到本機的「受信任的根憑證授權單位」與「受信任的發行者」憑證存放區。

只在你信任的自有電腦上安裝——安裝根憑證是安全相關動作。隨時可從 `certlm.msc`（受信任的根憑證授權單位／受信任的發行者）移除。
