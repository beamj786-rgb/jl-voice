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
        if (error is CloudSpeechException cloud)
        {
            var message = cloud.Failure switch
            {
                CloudFailure.Consent => cs ? "V Nastavení potvrďte odesílání nahraného zvuku do NVIDIA a uložte změny, nebo vyberte Offline." : "Confirm audio upload to NVIDIA in Settings and save, or select Offline.",
                CloudFailure.Key => cs ? "V Nastavení zadejte platný NVIDIA API klíč a uložte změny." : "Enter a valid NVIDIA API key in Settings and save.",
                CloudFailure.Language => cs ? "Pro cloudový test vyberte Češtinu nebo English. Automatický jazyk je zatím pouze offline." : "Choose Czech or English for the cloud test. Automatic is currently offline only.",
                CloudFailure.Unauthorized => cs ? "NVIDIA nepřijala API klíč (401). Zkontrolujte jej v Nastavení." : "NVIDIA rejected the API key (401). Check it in Settings.",
                CloudFailure.Forbidden => cs ? "NVIDIA nepovolila přístup (403). Ověřte oprávnění svého účtu." : "NVIDIA denied access (403). Check your account permissions.",
                CloudFailure.RateLimited => cs ? "NVIDIA omezila požadavky (429). Zkuste to později; požadavek automaticky neopakujeme." : "NVIDIA rate-limited the request (429). Try later; no automatic retry.",
                CloudFailure.Network => cs ? "NVIDIA není dostupná. Zkontrolujte připojení nebo vyberte Offline." : "NVIDIA is unreachable. Check connectivity or select Offline.",
                CloudFailure.Timeout => cs ? "Cloudový přepis překročil časový limit. Není zaručeno, že jej server nezpracoval." : "Cloud transcription timed out. The server may already have processed it.",
                CloudFailure.Response => cs ? "NVIDIA nevrátila použitelný přepis." : "NVIDIA did not return a usable transcript.",
                _ => cs ? "Cloudový přepis NVIDIA selhal. Zkuste to později nebo vyberte Offline." : "NVIDIA cloud transcription failed. Try later or select Offline."
            };
            return new(cs ? "Cloudový přepis se nezdařil." : "Cloud transcription failed.", message, "");
        }
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
