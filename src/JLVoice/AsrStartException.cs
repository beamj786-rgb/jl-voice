using System.ComponentModel;

namespace JLVoice;

internal sealed class AsrStartException(int nativeCode, Win32Exception cause)
    : Exception("Windows could not start the speech engine.", cause)
{
    internal int NativeCode { get; } = nativeCode;
    internal bool PolicyBlocked => NativeCode is 4551 or 1260 or 577;
}

internal sealed record ErrorNotice(string Title, string Message, string Details)
{
    internal static ErrorNotice From(Exception error, string language)
    {
        var cs = language == "cs";
        if (error is AsrStartException start)
        {
            var title = start.PolicyBlocked
                ? cs ? "Windows zablokoval rozpoznávání." : "Windows blocked speech recognition."
                : cs ? "Rozpoznávání nelze spustit." : "Speech recognition could not start.";
            var message = start.PolicyBlocked
                ? cs ? "Ochrana Windows nepovolila spuštění modulu JLVoiceAsr.exe. Pro Smart App Control potřebujeme důvěryhodně podepsanou sestavu; u firemních zásad povolení správce."
                     : "Windows protection did not allow JLVoiceAsr.exe to start. Smart App Control needs a trusted signed build; managed policies may require administrator approval."
                : cs ? "Windows nespustil modul JLVoiceAsr.exe. Ověřte oprávnění ke složce a stav ochrany v Zabezpečení Windows."
                     : "Windows could not start JLVoiceAsr.exe. Check folder permissions and the protection status in Windows Security.";
            // Do not put the exception's user-profile path into normal UI or reports.
            return new(title, message, $"JLVoiceAsr.exe · Win32 {start.NativeCode}");
        }
        var text = error.Message;
        if (text.Length > 600) text = text[..600] + "…";
        return new(cs ? "Akci se nepodařilo dokončit." : "The action could not be completed.", text, "");
    }
}
