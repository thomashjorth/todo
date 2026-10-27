# Web-udgaven 2.0.0, leverance 5: "Forbindelser" og flytningen — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** Brugeren taster hjælperens server, nøgle og tokens i en gruppe på indstillingssiden, som er
en iframe fra hjælperen; en vagt fastslår at tokenet aldrig når værten; dataene kan flyttes til
NAS'en uden tokens; og grenen `web-hosting/helper` er klar til at blive flettet til `main`.

**Designet er `docs/plans/2026-10-26-web-hosting-design.md`, afsnit 4.** Leverance 4 er
`docs/plans/2026-10-26-web-hosting-4-helper.md`, og arbejdet fortsætter på samme gren.

**Planen kan være forkert.** Mål frem for at tro, og ret planfilen når en måling modsiger den.

## Beslutninger truffet i planen

- **Microfrontenden er en anden rod i samme Angular-bundle**, ikke et projekt for sig. `main.ts`
  starter `ConnectionsRoot` når stien er `/connections`, ellers appen. Hjælperen indlejrer den samme
  `wwwroot` i sin exe og serverer den på `127.0.0.1:47600`. Så deler siden Tailwind, sprogfilerne,
  Vitest og typetjekket med appen — og **koden kommer fra hjælperens exe, aldrig fra NAS'en**, hvilket
  er designets krav: NAS-kode på tokenets origin ville kunne læse det.
- **Iframens adresse er en indstilling på værten** (`Helper:PageUrl`, standard
  `http://127.0.0.1:47600`), båret til klienten i `SettingsResponse.helperPageUrl`. Fast i frontenden
  kunne E2E kun køre mod brugerens egen hjælper på 47600.
- **Siden og appen taler med `postMessage`, og origin tjekkes begge veje.** Appen giver sin origin og
  sit sprog i iframens URL (`?parent=…&lang=…`); siden sender kun til den origin og kun tre beskeder:
  `ready`, `height`, `changed`. Ingen af dem bærer en hemmelighed. Appen tager kun imod fra
  `helperPageUrl`'s origin og fra netop sin iframes vindue.
- **Kommer `ready` ikke inden for tre sekunder**, viser appen "Forbindelserne indstilles fra den PC
  hvor hjælperen kører."
- **Parring:** siden laver en ny nøgle på hjælperen og viser dens hash én gang, til NAS'ens `.env`
  (`TODO_HELPER_KEY_HASH`) og — for `Todo.cmd` — brugerens miljøvariabel `Helper__KeyHash`
  (`SetHelperKey.cmd`). Nøglen selv forlader aldrig hjælperen.
- **`frame-ancestors`** på hjælperens sider: serverens origin, når den er sat; ellers `*`, fordi
  første opsætning netop sker inde i appens iframe, før hjælperen kender serveren.
- **Flytningen eksporterer uden tokens.** `Todo.Host export-db <fil>` laver `VACUUM INTO` en ny fil og
  sletter de to token-rækker i kopien. Ellers ville de ligge på NAS'en — i databasen indtil
  migreringen kører, og i backuppen den tager lige før.

## Opgaver

1. **Eksporten** (`DatabaseExport` i Core, test, verbet i `Program.cs`).
2. **Hjælperen serverer siden:** indlejret `wwwroot`, `/connections` falder tilbage på `index.html`,
   `frame-ancestors`, og `POST /api/server/pair` der svarer hash'en. Tests i `Todo.Helper.Tests`.
3. **`helperPageUrl`** på kontrakten og værten.
4. **`ConnectionsRoot`** med sin store: server, nøgle, Jira, ADO, tunnelens status, og beskederne til
   appen. Vitest.
5. **Gruppen "Forbindelser"** på indstillingssiden: iframen, beskederne, reserveteksten, og at
   `changed` genindlæser indstillingerne. Vitest.
6. **E2E:** rejsen med den rigtige vært og den rigtige hjælper, **vagten der fastslår at det tastede
   token aldrig optræder i nogen request til værten** (set fejle), og kontrasten inde i iframen.
7. **Scripts og compose:** `Helper.cmd`, `SetHelperKey.cmd`, `ExportData.cmd`,
   `Helper__KeyHash` i compose og `.env.example`. Docs: `NAS.md`, README, HANDOFF, CLAUDE.md.
8. **`Check.cmd`, push af grenen, grøn CI — og spørg brugeren før fletningen**, for den sletter de
   lokale tokens ved næste `Todo.cmd` og sender det første image med hjælperen til NAS'en.
