# GreenChrome 界面截图工具
#
# 用法（Windows PowerShell）：
#   1. 先启动 GreenChrome.exe
#   2. powershell -ExecutionPolicy Bypass -File tools\screenshot\screenshot.ps1 [输出路径]
#
# 输出默认写到 docs\screenshot.png（2120x1400，200% DPI 下的原始像素）。
#
# 两个容易踩的坑：
#   · 脚本文件必须是 UTF-8 **带 BOM**。PowerShell 5.1 默认按 GBK 读无 BOM 的 UTF-8，
#     中文字符串会变成乱码并触发语法错误（UnexpectedToken）。
#   · 截图进程必须先调 SetProcessDPIAware()。否则 GetWindowRect 返回逻辑坐标，
#     而 CopyFromScreen 按物理坐标截取，结果截到的是屏幕左上角而不是窗口。
#
# 另外：脚本会把 GreenChrome 置为 TOPMOST 再截图，但若其它窗口也常驻前台，
# 仍可能截到遮挡物。这种情况请手动把 GreenChrome 移到最前再截图。

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @"
using System;using System.Runtime.InteropServices;
public class GcShot {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public struct RECT { public int L,T,R,B; } }
"@

[GcShot]::SetProcessDPIAware() | Out-Null

$outPath = if ($args.Count -gt 0) { $args[0] } else { "D:\cs_project\GreenChrome\docs\screenshot.png" }

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

$proc = Get-Process GreenChrome -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "GreenChrome is not running"; exit 1 }

$root = $AE::FromHandle($proc.MainWindowHandle)

# 点一下「检查更新」，让界面上的版本数据是实时的
$cond = New-Object System.Windows.Automation.AndCondition(
  (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "检查更新")),
  (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)))
$btn = $root.FindFirst($TS::Descendants, $cond)
if ($btn) {
  try {
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Write-Output "clicked check-updates"
  } catch { }
  Start-Sleep -Seconds 8
}

# 置前
[GcShot]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
[GcShot]::SetWindowPos($proc.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x0003) | Out-Null
Start-Sleep -Milliseconds 500
[GcShot]::SetWindowPos($proc.MainWindowHandle, [IntPtr](-2), 0, 0, 0, 0, 0x0003) | Out-Null
[GcShot]::BringWindowToTop($proc.MainWindowHandle) | Out-Null
[GcShot]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 1500

$r = New-Object GcShot+RECT
[GcShot]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
$ww = $r.R - $r.L
$hh = $r.B - $r.T

$bmp = New-Object System.Drawing.Bitmap $ww, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$g.Dispose()

$dir = Split-Path $outPath -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output ("saved {0} ({1}x{2})" -f $outPath, $ww, $hh)
