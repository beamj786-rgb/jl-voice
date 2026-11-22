using System.Runtime.InteropServices;
using JLVoice;

internal static class Program
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var credentialTarget = "JLVoice.Tests." + Guid.NewGuid().ToString("N");
        try
        {
            WindowsCredentialStore.Save("FAKE-NON-AUTHENTICATING-TEST-VALUE", credentialTarget);
            if (WindowsCredentialStore.Load(credentialTarget) != "FAKE-NON-AUTHENTICATING-TEST-VALUE") throw new Exception("Windows credential round-trip failed.");
            WindowsCredentialStore.Forget(credentialTarget);
            if (WindowsCredentialStore.Load(credentialTarget) is not null) throw new Exception("Windows credential removal failed.");
            Console.WriteLine("PASS: isolated fake credential round-trip and removal in current Windows user store");
        }
        finally { WindowsCredentialStore.Forget(credentialTarget); }
        using var form = new Form { Text = "JL Voice disposable native input checks", Width = 640, Height = 240 };
        using var editor = new TextBox { Multiline = true, Dock = DockStyle.Top, Height = 100 };
        using var other = new TextBox { Dock = DockStyle.Bottom };
        form.Controls.Add(editor); form.Controls.Add(other);
        form.Shown += async (_, _) =>
        {
            var initiallyCaps = Control.IsKeyLocked(Keys.CapsLock);
            var taggedEvents = 0;
            var packetEvents = 0;
            IntPtr observer = IntPtr.Zero;
            WindowsInput.Hook callback = (code, message, data) =>
            {
                if (code >= 0 && Marshal.ReadInt32(data) == DictationKeys.CapsLock && WindowsInput.IsOwnInput(data) && WindowsInput.IsInjected(data)) ++taggedEvents;
                if (code >= 0 && Marshal.ReadInt32(data) == 0xe7 && WindowsInput.IsOwnInput(data)) ++packetEvents;
                return WindowsInput.CallNextHookEx(observer, code, message, data);
            };
            try
            {
                observer = WindowsInput.SetWindowsHookEx(13, callback, WindowsInput.GetModuleHandle(null), 0);
                if (observer == IntPtr.Zero) throw new Exception("Cannot install disposable event-tag observer.");
                form.Activate(); editor.Focus(); await Task.Delay(200);
                var target = WindowsInput.CaptureTarget();
                // Disposable test desktop only: seed multiple non-sensitive formats.
                const string originalClipboard = "ORIGINAL CLIPBOARD · žluťoučký 😀";
                const string originalRtf = @"{\rtf1\ansi ORIGINAL}";
                void SeedClipboard(bool persist = true)
                {
                    var data = new DataObject();
                    data.SetData(DataFormats.UnicodeText, false, originalClipboard);
                    data.SetData(DataFormats.Rtf, false, originalRtf);
                    data.SetData("JLVoice.NativeFixture", false, new MemoryStream(new byte[] { 0, 7, 255, 42 }));
                    Clipboard.SetDataObject(data, persist);
                }
                void CheckClipboard()
                {
                    if (Clipboard.GetText() != originalClipboard || (!Clipboard.TryGetData<string>(DataFormats.Rtf, out var rtf) || rtf != originalRtf) ||
                        !Clipboard.TryGetData<MemoryStream>("JLVoice.NativeFixture", out var stream) || !stream.ToArray().SequenceEqual(new byte[] { 0, 7, 255, 42 }))
                        throw new Exception("Clipboard formats/content were not restored.");
                }
                SeedClipboard();
                var sentence = string.Join(" ", Enumerable.Repeat("Příští čtvrtek: Visual Studio Code, GitHub. Hello, world! 123 😀", 8));
                await WindowsInput.InsertAsync(sentence, target, CancellationToken.None, form.Handle);
                await Task.Delay(150);
                if (editor.Text != sentence) throw new Exception("Native edit control did not receive the exact Unicode fixture.");
                CheckClipboard();
                if (packetEvents != 0) throw new Exception("Paste unexpectedly emitted Unicode key packets.");
                Console.WriteLine("PASS: exact long Czech/English/emoji paste, no VK_PACKET events, original text/RTF/binary clipboard restored");
                SeedClipboard(persist: false); editor.Clear();
                await WindowsInput.InsertAsync(sentence, WindowsInput.CaptureTarget(), CancellationToken.None, form.Handle);
                if (editor.Text != sentence) throw new Exception("Paste from delayed-render clipboard failed.");
                CheckClipboard();
                Console.WriteLine("PASS: delayed-render OLE text/RTF/binary clipboard materializes and restores correctly");
                other.Focus(); await Task.Delay(100);
                var clipboard = GetClipboardSequenceNumber();
                try { await WindowsInput.InsertAsync("MUST NOT APPEAR", target, CancellationToken.None, form.Handle); throw new Exception("Changed target accepted."); }
                catch (InvalidOperationException) { }
                if (other.Text != "" || editor.Text != sentence) throw new Exception("Changed focus received text.");
                if (GetClipboardSequenceNumber() != clipboard) throw new Exception("Changed-focus insertion touched clipboard.");
                Console.WriteLine("PASS: changed native focus refuses insertion");
                WindowsInput.ToggleCapsLock(); await Task.Delay(100);
                if (Control.IsKeyLocked(Keys.CapsLock) == initiallyCaps) throw new Exception("Tagged synthetic Caps toggle failed.");
                WindowsInput.ToggleCapsLock(); await Task.Delay(100);
                if (Control.IsKeyLocked(Keys.CapsLock) != initiallyCaps) throw new Exception("Caps state was not restored.");
                if (taggedEvents != 4) throw new Exception("Synthetic Caps down/up events were not identified by their own tag.");
                Console.WriteLine("PASS: tagged Caps toggles work in both initial states");
                editor.Focus(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                clipboard = GetClipboardSequenceNumber();
                try { await WindowsInput.InsertAsync("CANCELLED", WindowsInput.CaptureTarget(), cancelled.Token, form.Handle); throw new Exception("Cancelled insertion accepted."); }
                catch (OperationCanceledException) { }
                if (editor.Text != sentence) throw new Exception("Cancellation inserted text.");
                if (GetClipboardSequenceNumber() != clipboard) throw new Exception("Pre-cancelled insertion touched clipboard.");
                Console.WriteLine("PASS: cancellation before insertion sends no text");
                editor.Text = "before REPLACE after"; editor.Select(7, 7);
                await WindowsInput.InsertAsync("Řeč 😀", WindowsInput.CaptureTarget(), CancellationToken.None, form.Handle);
                if (editor.Text != "before Řeč 😀 after") throw new Exception("Paste did not replace only the selection.");
                CheckClipboard();
                Console.WriteLine("PASS: paste replaces selection and restores all seeded formats");
                if (!Control.IsKeyLocked(Keys.CapsLock)) WindowsInput.ToggleCapsLock();
                await Task.Delay(100); editor.Clear();
                await WindowsInput.InsertAsync("MiXeD čeština 😀", WindowsInput.CaptureTarget(), CancellationToken.None, form.Handle);
                if (editor.Text != "MiXeD čeština 😀" || !Control.IsKeyLocked(Keys.CapsLock)) throw new Exception("Caps-on paste changed text/capitalization state.");
                Console.WriteLine("PASS: Caps-on paste preserves exact capitalization and Caps state");
                using (var lease = await ClipboardTransfer.PublishAsync(form.Handle, "TEMPORARY", CancellationToken.None))
                {
                    Clipboard.SetText("NEW USER COPY");
                    try { lease.CheckUnchanged(); throw new Exception("Changed clipboard accepted for paste."); } catch (InvalidOperationException) { }
                    await lease.RestoreAsync();
                    if (Clipboard.GetText() != "NEW USER COPY") throw new Exception("Concurrent copy was overwritten.");
                }
                Console.WriteLine("PASS: clipboard change aborts paste and newer user copy wins over restoration");
                using (var bitmap = new System.Drawing.Bitmap(4, 4)) { bitmap.SetPixel(1, 1, System.Drawing.Color.Red); Clipboard.SetImage(bitmap); }
                editor.Clear();
                await WindowsInput.InsertAsync("Image backup test", WindowsInput.CaptureTarget(), CancellationToken.None, form.Handle);
                using (var restoredImage = Clipboard.GetImage())
                {
                    if (editor.Text != "Image backup test" || restoredImage is not System.Drawing.Bitmap restoredBitmap || restoredBitmap.GetPixel(1, 1).ToArgb() != System.Drawing.Color.Red.ToArgb())
                        throw new Exception("Image clipboard backup/paste/restore failed.");
                }
                Console.WriteLine("PASS: bitmap clipboard survives paste with its original pixel content");
                const int oversizedLength = 9 * 1024 * 1024;
                Clipboard.SetText(new string('x', oversizedLength));
                var beforeUnsupported = editor.Text;
                try { await WindowsInput.InsertAsync("MUST NOT PASTE", WindowsInput.CaptureTarget(), CancellationToken.None, form.Handle); throw new Exception("Oversized clipboard accepted."); }
                catch (InvalidOperationException) { }
                if (Clipboard.GetText().Length != oversizedLength || editor.Text != beforeUnsupported) throw new Exception("Oversized clipboard destroyed or insertion occurred.");
                Console.WriteLine("PASS: oversized clipboard remains intact and insertion is refused before mutation");
                Clipboard.Clear();
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
