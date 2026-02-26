using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace _HoldSense.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint WmHotkey = 0x0312;
    private const int VkC = 0x43;
    private const int VkW = 0x57;
    private const int HotkeyAudio = 1;
    private const int HotkeyDetection = 2;

    private Thread? _thread;
    private uint _threadId;
    private volatile bool _running;

    public bool KeybindEnabled { get; set; } = true;

    public event EventHandler? AudioTogglePressed;
    public event EventHandler? DetectionTogglePressed;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(Worker) { IsBackground = true, Name = "GlobalHotkeyListener" };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        }

        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private void Worker()
    {
        _threadId = GetCurrentThreadId();
        RegisterHotKey(IntPtr.Zero, HotkeyAudio, ModControl | ModAlt, VkC);
        RegisterHotKey(IntPtr.Zero, HotkeyDetection, ModControl | ModAlt, VkW);

        try
        {
            while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message != WmHotkey)
                {
                    continue;
                }

                if (!KeybindEnabled)
                {
                    continue;
                }

                if (msg.wParam == (IntPtr)HotkeyAudio)
                {
                    AudioTogglePressed?.Invoke(this, EventArgs.Empty);
                }
                else if (msg.wParam == (IntPtr)HotkeyDetection)
                {
                    DetectionTogglePressed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyAudio);
            UnregisterHotKey(IntPtr.Zero, HotkeyDetection);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
