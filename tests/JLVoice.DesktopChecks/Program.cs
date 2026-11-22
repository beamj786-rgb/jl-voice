using System.Runtime.InteropServices;
using JLVoice;

internal static class Program
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using var form = new Form { Text = "JL Voice disposable native input checks", Width = 640, Height = 240 };
        using var editor = new TextBox { Multiline = true, Dock = DockStyle.Top, Height = 100 };
        using var other = new TextBox { Dock = DockStyle.Bottom };
        form.Controls.Add(editor); form.Controls.Add(other);
        form.Shown += async (_, _) =>
        {
            var initiallyCaps = Control.IsKeyLocked(Keys.CapsLock);
            var taggedEvents = 0;
            IntPtr observer = IntPtr.Zero;
            WindowsInput.Hook callback = (code, message, data) =>
            {
                if (code >= 0 && Marshal.ReadInt32(data) == DictationKeys.CapsLock && WindowsInput.IsOwnInput(data) && WindowsInput.IsInjected(data)) ++taggedEvents;
                return WindowsInput.CallNextHookEx(observer, code, message, data);
            };
            try
            {
                observer = WindowsInput.SetWindowsHookEx(13, callback, WindowsInput.GetModuleHandle(null), 0);
                if (observer == IntPtr.Zero) throw new Exception("Cannot install disposable event-tag observer.");
                form.Activate(); editor.Focus(); await Task.Delay(200);
                var target = WindowsInput.CaptureTarget();
                var clipboard = GetClipboardSequenceNumber();
                const string sentence = "Příští čtvrtek: Visual Studio Code, GitHub. Hello, world! 123 😀";
                await WindowsInput.InsertAsync(sentence, target, CancellationToken.None);
                await Task.Delay(150);
                if (editor.Text != sentence) throw new Exception("Native edit control did not receive the exact Unicode fixture.");
                if (GetClipboardSequenceNumber() != clipboard) throw new Exception("Dictation modified the clipboard.");
                Console.WriteLine("PASS: exact Czech/English/surrogate-pair Unicode insertion and unchanged clipboard sequence");
                other.Focus(); await Task.Delay(100);
                try { await WindowsInput.InsertAsync("MUST NOT APPEAR", target, CancellationToken.None); throw new Exception("Changed target accepted."); }
                catch (InvalidOperationException) { }
                if (other.Text != "" || editor.Text != sentence) throw new Exception("Changed focus received text.");
                Console.WriteLine("PASS: changed native focus refuses insertion");
                WindowsInput.ToggleCapsLock(); await Task.Delay(100);
                if (Control.IsKeyLocked(Keys.CapsLock) == initiallyCaps) throw new Exception("Tagged synthetic Caps toggle failed.");
                WindowsInput.ToggleCapsLock(); await Task.Delay(100);
                if (Control.IsKeyLocked(Keys.CapsLock) != initiallyCaps) throw new Exception("Caps state was not restored.");
                if (taggedEvents != 4) throw new Exception("Synthetic Caps down/up events were not identified by their own tag.");
                Console.WriteLine("PASS: tagged Caps toggles work in both initial states");
                editor.Focus(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { await WindowsInput.InsertAsync("CANCELLED", WindowsInput.CaptureTarget(), cancelled.Token); throw new Exception("Cancelled insertion accepted."); }
                catch (OperationCanceledException) { }
                if (editor.Text != sentence) throw new Exception("Cancellation inserted text.");
                Console.WriteLine("PASS: cancellation before insertion sends no text");
                Console.WriteLine("LIMIT: hosted native edit control, not modern Notepad, browser DOM, physical keyboard or Windows 11 acceptance.");
            }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            finally
            {
                if (Control.IsKeyLocked(Keys.CapsLock) != initiallyCaps) WindowsInput.ToggleCapsLock();
                if (observer != IntPtr.Zero) WindowsInput.UnhookWindowsHookEx(observer);
                GC.KeepAlive(callback);
                form.Close();
            }
        };
        Application.Run(form);
    }
}
