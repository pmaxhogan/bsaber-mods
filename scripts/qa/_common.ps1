# Shared helpers for scripts/qa/*.ps1 (dot-sourced; not meant to be run directly).
# Override the game instance with $env:MAXTWEAKS_BS_DIR.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:InstanceDir = if ($env:MAXTWEAKS_BS_DIR) { $env:MAXTWEAKS_BS_DIR.TrimEnd('\', '/') } else { 'C:\Users\pmaxh\BSManager\BSInstances\1.40.8' }
$script:LogDir = Join-Path $script:InstanceDir 'Logs'
$script:LatestLog = Join-Path $script:LogDir '_latest.log'
$script:CommandFile = Join-Path $script:InstanceDir 'UserData\MaxTweaks.debug-commands.txt'
# Mirror of every MaxTweaks log line, written by the mod with --debug (Common/Debug/DebugLogMirror).
# Scripts read this, not Logs/_latest.log: BSIPA opens _latest.log with FileShare.None for each print
# batch, so a reader holding it open makes BSIPA DROP those log lines.
$script:DebugLogFile = Join-Path $script:InstanceDir 'UserData\MaxTweaks.debug-log.txt'
$script:DefaultShotDir = if ($env:MAXTWEAKS_QA_OUT) { $env:MAXTWEAKS_QA_OUT } else { 'C:\Users\pmaxh\AppData\Local\Temp\claude\C--Users-pmaxh-Documents-python-projects-bsaber\3278f6f3-45dd-41af-839c-8ce255261c38\scratchpad\qa' }
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:DebugPrefix = '[MaxTweaks.Debug]'

function Get-BeatSaberProcess {
  Get-Process -Name 'Beat Saber' -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Get-BeatSaberWindow {
  $p = Get-BeatSaberProcess
  if (-not $p) { throw 'Beat Saber is not running' }
  $p.Refresh()
  if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Beat Saber has no main window yet' }
  return $p.MainWindowHandle
}

if (-not ('MaxTweaksQa.Native' -as [type])) {
  Add-Type -ReferencedAssemblies System.Drawing, System.Drawing.Common, System.Drawing.Primitives -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MaxTweaksQa
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;
        const uint MOUSEEVENTF_MOVE = 0x1, MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4,
            MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10;

        public static bool Focus(IntPtr hWnd)
        {
            if (GetForegroundWindow() == hWnd) return true;
            if (IsIconic(hWnd)) ShowWindow(hWnd, 9);
            uint target = GetWindowThreadProcessId(hWnd, IntPtr.Zero);
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint self = GetCurrentThreadId();
            // A tapped Alt lifts the foreground lock that otherwise refuses SetForegroundWindow from a console.
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            AttachThreadInput(self, fg, true);
            AttachThreadInput(self, target, true);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            AttachThreadInput(self, target, false);
            AttachThreadInput(self, fg, false);
            return GetForegroundWindow() == hWnd;
        }

        static bool IsExtended(ushort vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24: // PgUp PgDn End Home
                case 0x25: case 0x26: case 0x27: case 0x28: // arrows
                case 0x2D: case 0x2E: // Insert Delete
                case 0xA3: case 0xA5: // RCtrl RAlt
                    return true;
                default:
                    return false;
            }
        }

        public static void Key(ushort vk, bool up)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            input.u.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        static void Mouse(uint flags, int dx, int dy)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dx = dx;
            input.u.mi.dy = dy;
            input.u.mi.dwFlags = flags;
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void MouseMoveRelative(int dx, int dy) { Mouse(MOUSEEVENTF_MOVE, dx, dy); }
        public static void MouseButton(bool right, bool up)
        {
            Mouse(right ? (up ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_RIGHTDOWN) : (up ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_LEFTDOWN), 0, 0);
        }

        /// <summary>Moves the cursor to a point given in the window's client coordinates.</summary>
        public static void MouseMoveClient(IntPtr hWnd, int x, int y)
        {
            var p = new POINT { X = x, Y = y };
            ClientToScreen(hWnd, ref p);
            SetCursorPos(p.X, p.Y);
        }

        /// <summary>Captures the window (PW_RENDERFULLCONTENT works for the Unity swapchain) to a PNG.</summary>
        public static string Capture(IntPtr hWnd, string path)
        {
            RECT r;
            GetWindowRect(hWnd, out r);
            int w = Math.Max(1, r.Right - r.Left), h = Math.Max(1, r.Bottom - r.Top);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(hWnd, hdc, 2); }
                    finally { g.ReleaseHdc(hdc); }
                }
                bmp.Save(path, ImageFormat.Png);
            }
            return w + "x" + h;
        }
    }
}
'@
}

# Brings the game window to the foreground; returns $true when it is (re-checked after a short settle).
function Invoke-GameFocus {
  param([Parameter(Mandatory)][IntPtr]$Hwnd, [int]$Attempts = 4)
  # Windows' foreground lock sometimes refuses the first attempt (or another window steals focus
  # back right after), so retry with a growing settle time.
  for ($i = 1; $i -le $Attempts; $i++) {
    $null = [MaxTweaksQa.Native]::Focus($Hwnd)
    Start-Sleep -Milliseconds (100 * $i)
    if ([MaxTweaksQa.Native]::GetForegroundWindow() -eq $Hwnd) { return $true }
  }
  return $false
}

# Byte length of the log, so a wait only sees lines written after this point.
function Get-LogOffset {
  param([string]$Path = $script:DebugLogFile)
  if (Test-Path -LiteralPath $Path) { return (Get-Item -LiteralPath $Path).Length }
  return 0
}

# Polls a log (default: the MaxTweaks debug mirror) from $Offset until a line matches $Pattern.
# Returns the matching line, or $null on timeout, and sets $script:LastMatchEnd to the byte offset
# just after the match. Every line that contains $Echo (a plain substring) and starts at or after
# $EchoFrom is written to the host. For BSIPA's own logs (Logs/*), the file is opened only after its
# size changed, to keep the window in which BSIPA's exclusive open could fail small.
function Wait-LogPattern {
  param(
    [Parameter(Mandatory)][string]$Pattern,
    [double]$TimeoutSec = 60,
    [long]$Offset = -1,
    [string]$Path = $script:DebugLogFile,
    [string]$Echo = $null,
    [long]$EchoFrom = 0
  )
  if ($Offset -lt 0) { $Offset = Get-LogOffset -Path $Path }
  $lineStart = $Offset
  $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
  $pending = ''
  $utf8 = New-Object System.Text.UTF8Encoding($false)
  $cautious = $Path.StartsWith($script:LogDir, [StringComparison]::OrdinalIgnoreCase)
  while ([DateTime]::UtcNow -lt $deadline) {
    $exists = Test-Path -LiteralPath $Path
    if ($exists -and $cautious -and (Get-Item -LiteralPath $Path).Length -eq $Offset) { $exists = $false }
    if ($exists) {
      $fs = $null
      try {
        $fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
        if ($fs.Length -lt $Offset) { $Offset = 0; $lineStart = 0; $pending = '' }
        if ($fs.Length -gt $Offset) {
          $fs.Seek($Offset, 'Begin') | Out-Null
          $buf = New-Object byte[] ($fs.Length - $Offset)
          $read = $fs.Read($buf, 0, $buf.Length)
          $Offset += $read
          $text = $pending + $utf8.GetString($buf, 0, $read)
          $lines = $text -split "`n"
          $pending = $lines[-1]
          foreach ($line in $lines[0..($lines.Length - 2)]) {
            $l = $line.TrimEnd("`r")
            $thisStart = $lineStart
            $lineStart += $utf8.GetByteCount($line) + 1
            if ($Echo -and $thisStart -ge $EchoFrom -and $l.Contains($Echo)) { Write-Host $l }
            if ($l -match $Pattern) { $script:LastMatchEnd = $lineStart; return $l }
          }
        }
      } catch [System.IO.IOException] {
      } finally {
        if ($fs) { $fs.Dispose() }
      }
    }
    Start-Sleep -Milliseconds 100
  }
  return $null
}

# ASCII names to virtual-key codes for send-keys.ps1.
function Get-VirtualKey {
  param([Parameter(Mandatory)][string]$Name)
  $n = $Name.ToUpperInvariant()
  $map = @{
    'ENTER' = 0x0D; 'RETURN' = 0x0D; 'BACKSPACE' = 0x08; 'TAB' = 0x09; 'ESC' = 0x1B; 'ESCAPE' = 0x1B; 'SPACE' = 0x20
    'LEFT' = 0x25; 'UP' = 0x26; 'RIGHT' = 0x27; 'DOWN' = 0x28; 'SHIFT' = 0x10; 'CTRL' = 0x11; 'ALT' = 0x12
    'PGUP' = 0x21; 'PGDN' = 0x22; 'END' = 0x23; 'HOME' = 0x24; 'DELETE' = 0x2E; 'INSERT' = 0x2D
  }
  if ($map.ContainsKey($n)) { return [uint16]$map[$n] }
  if ($n -match '^F([1-9]|1[0-2])$') { return [uint16](0x6F + [int]$Matches[1]) }
  if ($n.Length -eq 1 -and $n -match '[A-Z0-9]') { return [uint16][char]$n }
  throw "Unknown key '$Name' (letters, digits, Enter, Backspace, Tab, Esc, Space, arrows Up/Down/Left/Right, Shift, Ctrl, Alt, F1-F12, PgUp, PgDn, Home, End)"
}
