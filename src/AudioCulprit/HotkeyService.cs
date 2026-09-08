using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioCulprit;

internal sealed class HotkeyService : NativeWindow, IDisposable
{
    private const int Id = 0x41C;
    private readonly Action pressed;
    public bool Registered { get; private set; }
    public int LastError { get; private set; }
    public string Label { get; private set; } = "Ctrl + Alt + A";
    public HotkeyService(Action pressed)
    {
        this.pressed = pressed;
        CreateHandle(new CreateParams { Caption = "AudioCulprit hotkey", Parent = new IntPtr(-3) });
    }
    public bool Enable(bool enabled)
    {
        if (Registered) UnregisterHotKey(Handle, Id);
        Registered = enabled && RegisterHotKey(Handle, Id, 0x4003, 0x41); // Ctrl+Alt+A, no repeat
        Label = "Ctrl + Alt + A";
        if (enabled && !Registered && Marshal.GetLastWin32Error() == 1409)
        {
            Registered = RegisterHotKey(Handle, Id, 0x4003, 0x79); // F10 if A is occupied
            Label = "Ctrl + Alt + F10";
        }
        LastError = enabled && !Registered ? Marshal.GetLastWin32Error() : 0;
        return !enabled || Registered;
    }
    protected override void WndProc(ref Message message)
    {
        if (Registered && message.Msg == 0x0312 && message.WParam.ToInt32() == Id) { pressed(); return; }
        base.WndProc(ref message);
    }
    public void Dispose() { Enable(false); DestroyHandle(); }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
