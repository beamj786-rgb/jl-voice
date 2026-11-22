# NVIDIA v aplikaci: vývojový test Windows x64

1. Rozbal celý nový ZIP do samostatné složky. Spusť JLVoice.exe; Python ani .NET ručně neinstaluj.
2. Nastavení → Rozpoznávání → NVIDIA cloud · Parakeet TDT.
3. Vlož svůj NVIDIA API klíč do maskovaného pole. Klíč neposílej do chatu ani hlášení chyby. Bez volby Zapamatovat zůstává pouze pro toto spuštění. Volba Zapamatovat používá Správce pověření Windows.
4. Přečti upozornění a potvrď odesílání audiozáznamu do NVIDIA. Vyber jazyk Čeština (pro anglický test English), mikrofon a Uložit. Auto je v tomto testu jen offline.
5. Vrať se do Poznámkového bloku, klikni do textu, drž Caps Lock alespoň 250 ms, řekni jednu souvislou větu a pusť. Po odpovědi se přepis vloží do původního pole. Krátké klepnutí dál přepíná Caps Lock.
6. Opakuj v prohlížeči a s Caps Lockem již zapnutým. Ověř přesný text, diakritiku, interpunkci, správné cílové pole a nezměněný stav Caps Locku.
7. Nastavení → Pokročilé: volitelně uchovej jednu nahrávku v paměti pro opakovaný přepis bez vkládání. Stejnou nahrávku můžeš přepsat přes NVIDIA i offline Base/Small: změň poskytovatele, ulož a použij opakovaný přepis. Retence je po spuštění vypnutá. Nepoužívej citlivé audio.

Pošli vyslovenou větu, skutečný přepis, vybraný jazyk a zobrazený čas. Klíč a soukromé audio neposílej. Čas cloudového požadavku zahrnuje síť a službu; serverový čas inference API neposkytuje. Čas od puštění klávesy po vložení je další samostatné měření na tvém PC. Starší rychlosti CLI nejsou ověřením latence celé aplikace.

## Chyby a soukromí

401: zkontroluj klíč; 403: přístup účtu ke službě; 429: limit služby. Aplikace požadavek automaticky neopakuje ani nepřepíná na jiného poskytovatele. Po 120 sekundách se čekání ukončí. Zrušení nemůže vzít zpět již odeslané audio. Po chybě lze začít nové diktování. Výchozí offline režim nevyžaduje účet ani internet. Cloudová dostupnost, limity a pokračování bezplatného vývojového přístupu nejsou garantované.

Klíč není v settings.json ani diagnostice. Zapomenout klíč odstraní session klíč i uložené pověření JLVoice.NVIDIA.APIKey. Před smazáním portable složky zapomenutí proveď; jinak zůstane volitelně uložené pověření ve Windows. Podrobnosti: PRIVACY.md.

NVIDIA endpoint: https://2b940e91-a70e-4483-a958-d50408b96589.invocation.api.nvcf.nvidia.com/v1/audio/transcriptions
Dokumentace: https://build.nvidia.com/nvidia/parakeet-tdt-0_6b/api

Tento ZIP není podepsaný ani produkční release. Pokud Windows zablokuje JLVoice.exe, cloud tento problém neřeší. Nevypínej bezpečnostní ochrany. Cloud nepotřebuje lokální JLVoiceAsr.exe; offline režim jej nadále používá.

## Co ověřují automatické testy

Core checks testují HTTP přes paměťovou simulaci: žádný požadavek bez souhlasu, jazyka a platného formátu klíče; HTTPS multipart/WAV; odpověď s diakritikou; 401/403/429/přesměrování/chyby služby; limity odpovědi; zrušení; žádný automatický retry. Windows desktop checks testují izolované falešné pověření a jeho odstranění. WPF review ověřuje výchozí offline režim, validační brány, výběr poskytovatele, odvolání souhlasu, zapomenutí klíče a světlé/tmavé nastavení. Review má zakázaný přepis a nepoužívá skutečný klíč ani síť.

Živá integrace mikrofon → NVIDIA → Notepad/prohlížeč se musí ověřit uživatelem. Skutečný endpoint byl dosud testován uživatelem přes CLI na šesti veřejných CZ/EN vzorcích; výsledek není kvalifikací přesnosti ani 8GB hardwaru. Příslušný stav CI a případné chyby musí být uvedeny při předání ZIPu.

## Výsledek ověření 22. 11. 2026

Windows CI pro zdroj `2764fe750002993cb6a2a9ad758f0baae492fd08` [prošlo](https://github.com/beamj786-rgb/jl-voice/actions/runs/45061431829). Prošly core/mock HTTP kontroly, izolované Windows Credential Manager round-trip/odstranění, nativní Unicode vkládání a označené Caps přepnutí, integrita balíčku, testy workeru, offline anglická referenční nahrávka, idle smoke a WPF review. `review.json` má prázdné `issues`; tmavý i světlý cloudový náhled byl vizuálně zkontrolován. UI fixture dočasně používá F9 pro kontrolu kompatibility; distribuované výchozí nastavení zůstává Caps Lock HOLD. Snímky používají falešný klíč, žádný skutečný NVIDIA požadavek.

[Windows test ZIP](https://github.com/beamj786-rgb/jl-voice/actions/runs/45061431829/artifacts/14768769586) má 311 375 742 bajtů, retenci do 29. 11. 2026. Jde o nepodepsaný vývojový balíček, nikoli release. Živý test aplikace s mikrofonem, NVIDIA účtem a Notepadem/prohlížečem ani fyzická 8GB akceptace nebyly tímto CI provedeny.
