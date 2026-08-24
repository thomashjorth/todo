# Tastaturgenveje for detaljepanelets felter og for rækkerne på listen

Design, 2026-08-24. Brugerens ord: *"Jeg ønsker tastaturgenveje for hvert enkelt type input på en
todo der er aktiv og en genvej for en todo på listen med noget nummerering som del af genvejen."*

## 1. Hvad problemet er

Appen har Alt-genveje i dag — `O I J A S` til de fem skærme, `N` til Ny opgave, `K` til søgefeltet,
`V` og `M` til de to filtre — altså **ni** bogstaver, som `KeyboardJourneyTests` tæller mod sin
`BadgeCount = 9`. De fører alle sammen hen til noget: en skærm, et felt øverst på listen, en
kontakt. Ingen af dem fører ind i en opgave.

To ting mangler derfor. Man kan ikke nå **den enkelte opgave** uden mus eller en lang række Tab, og
man kan ikke nå **et bestemt felt** på den opgave man har åbnet. Sammen er det den daglige vej
gennem appen: vælg en opgave, ret dens deadline, sæt dens status.

Og bogstavrummet er stort set brugt op. Chrome stjæler `D`, `E`, `F`, `Alt+Home` og piletasterne
under udvikling, og de ni ovenfor er taget, så otte nye felter kan ikke få forbogstavet fra deres
eget navn i det samme lag. Det er den begrænsning hele designet er formet af.

## 2. Beslutningerne

Fem valg, truffet af brugeren 2026-08-24, med det der blev valgt fra:

**Felterne får deres eget lag: `Alt+Shift+bogstav`.** Alternativet var at presse dem ned i
restbogstaverne (`B`, `G`, `H`, `L`, `P`, `R`, `T`, `U` …), hvor status ikke kunne få `S` og noten
ikke `N` — en mnemonik der skal læres udenad frem for gættes. Prisen for laget er én modifikator
mere at holde, og at badge-visningen skal vide hvilket lag den viser. Det tredje forslag — et
præfiksbogstav der åbner feltlaget, derefter en tast uden modifikator — blev valgt fra, fordi det
kræver en tilstand i appen, og et forkert andet tryk så er en tavs fejl.

**Rækkerne nummereres 1–9 fra toppen, løbende på tværs af sektionerne.** Række ti og frem har ingen
genvej. Alternativerne var 1–9 pr. sektion, som gør ét ciffer tvetydigt og derfor kræver at man
vælger en sektion først, og et rullende vindue omkring den valgte række, som giver mere rækkevidde
men flytter en opgaves nummer, mens man arbejder — muskelhukommelsen kan ikke bygges.

**Cifrene bruger `Alt`, ikke `Alt+Shift` eller `Ctrl`.** Så viser de eksisterende badges numrene
uden ny mekanik. Prisen er kendt og står allerede i CLAUDE.md som en fælde: Chrome binder
`Alt+1`–`8` til faneskift og `Alt+9` til sidste fane, så genvejen kan **kun** prøves i
Photino-vinduet. `Alt+Shift+ciffer` var fri i begge, men `Shift+1` er `!` på et dansk tastatur, så
opslaget skulle være sket på `event.code` frem for `event.key` — en anden nøgle end resten af
registret bruger.

**Alt+ciffer vælger rækken**, altså nøjagtigt hvad et museklik på rækkeknappen gør. Ikke "vælg og gå
til første felt", som gør det umuligt bare at læse en opgave, og ikke "skift fluebenet", som er en
destruktiv handling bag en tast man kan ramme ved siden af.

**Slet opgave får `Alt+Shift+L`, men kun fokus.** Der er ingen bekræftelse og ingen fortryd i appen,
så det andet tryk *er* bekræftelsen. Undtagelsen fra `activate`-konventionen skal begrundes i
skabelonen ved siden af kaldet, ellers bliver den "rettet" som en inkonsekvens.

## 3. To lag, ét register

`ShortcutStore` er i dag et `Map<string, () => void>` med bogstavet som nøgle. Nøglen bliver **lag
plus tast**, og direktivet får et input til at sige laget:

```
appShortcut="d"  appShortcutModifier="alt-shift"     → nøglen "alt-shift+d"
appShortcut="3"  (udeladt, default "alt")            → nøglen "alt+3"
```

`app.ts` bygger samme nøgle af hændelsen — `event.shiftKey ? 'alt-shift+' : 'alt+'` plus
`event.key.toLowerCase()` — så opslaget er ét `Map.get` som før, og last-writer-wins-egenskaben er
uændret. `aria-keyshortcuts` bliver `"Alt+D"` eller `"Alt+Shift+D"` beregnet af de **samme** to
felter, så mærkaten og virkeligheden ikke kan drive fra hinanden.

Vagten på `!event.ctrlKey && !event.metaKey` bliver stående uændret: `Ctrl+Alt` er AltGr på et dansk
tastatur, og at spise den ville ødelægge indtastning af `@`, `£` og `$`.

**To ting skal måles frem for antages**, og begge i Photino-vinduet:

1. At `Alt+Shift+D` giver `event.key === "D"`, så `toLowerCase()` er nok, og `event.code` ikke
   behøves.
2. At Windows' eget layoutskift på `Alt+Shift` ikke stjæler kombinationen. Skiftet udløses på slip
   uden et bogstav, så `Alt+Shift+D` bør være fri — men "bør" er ikke en måling, og på en maskine
   med kun ét layout installeret sker der under alle omstændigheder ingenting, hvad der gør en grøn
   måling her svagere end den ser ud.

### Badges vises fortsat på Alt alene

Feltlagets mærkater får teksten `⇧D` og vises, når **Alt** er nede — ikke `Alt+Shift`. Det er med
vilje: skal man holde `Alt+Shift` for at *se* mærkaterne, eksponerer man sig for layoutskiftet ved
hvert slip, og den fejl dukker op uger senere som "appen skifter mit tastatur". Med Alt nede ser man
alt; Shift kommer først på i selve anslaget. `ShortcutStore.altHeld` er derfor uændret, og ingen af
de ni eksisterende badge-betingelser skal røres.

### Den tomme tast

Direktivet får én guard: en **tom** `appShortcut` registrerer ingenting og udsender ingen
`aria-keyshortcuts`. Den er nødvendig for rækkerne — kun de første ni har et nummer — og
alternativet er to kopier af rækkeknappen bag et `@if`, hvor den ene ville drive fra den anden.

## 4. Felternes bogstaver

| Genvej | Felt | Handling |
| --- | --- | --- |
| `Alt+Shift+D` | **D**eadline | fokus |
| `Alt+Shift+S` | **S**tartdato | fokus |
| `Alt+Shift+O` | **O**pgavestiller | fokus |
| `Alt+Shift+N` | **N**oten | aktivér |
| `Alt+Shift+T` | s**T**atus | fokus |
| `Alt+Shift+V` | **V**enter på | fokus |
| `Alt+Shift+U` | ny **U**nderopgave | fokus |
| `Alt+Shift+L` | s**L**et opgave | fokus, se afsnit 2 |

`T` frem for `S` til status, fordi startdatoen har det stærkere krav på `S`: den er et felt man
skriver i, hvor status er en vælger man også kan nå med piletasterne, når først den har fokus.

Tre af de otte har en tilstand knyttet til sig, og alle tre løser sig selv med maskineri der
allerede står der:

- **Noten** er den eneste `activate`. Genvejen klikker knappen "Redigér noten", og `TaskDetail`s
  eksisterende `effect(() => this.noteEditor()?.nativeElement.focus())` sætter caret'en i editoren,
  når den dukker op. Er editoren allerede åben, findes knappen ikke, `ngOnDestroy` har afregistreret
  bogstavet, og genvejen gør ingenting — hvad der er det rigtige, for feltet har fokus.
- **Venter på** findes kun bag `@if (task().status === waitingFor)`, så registreringen kommer og går
  med feltet. Samme mekanik som i dag; intet nyt.
- **Slet** er `focus`. Se begrundelsen i afsnit 2.

Panelet renderes **præcis ét sted** — `WideScreen.wide` driver `@if` på både højre spalte og
rækken — så de otte bogstaver ikke kan blive registreret to gange. Havde panelet stået bag
`hidden xl:block`, ville hver af dem have været en dublet, og last-writer-wins ville have gjort den
skjulte kopi til den der svarer.

## 5. Numrene på rækkerne

`TaskList.selectableTasks` er allerede den liste nummereringen har brug for: sektionerne, derefter
"Venter på", derefter "Måske", i skærmens rækkefølge og uden de fuldførte, hvis række er et
almindeligt `<li>` uden panel bag.

```ts
protected readonly numbers = computed(
  () => new Map(this.selectableTasks().slice(0, 9).map((t, i) => [t.id, i + 1])),
);
```

`TaskRow` får et `number = input<number | undefined>()`, som skabelonen sender videre til
`[appShortcut]` på rækkeknappen — tom for række ti og frem, hvor guarden fra afsnit 3 sørger for at
der hverken registreres noget eller står en `aria-keyshortcuts`. Handlingen er `activate`, altså
`toggled.emit()` → `toggle(task)`, og fokus flytter til rækken, fordi et programmatisk `click()`
ikke selv gør det.

Nummer-badgen skal have `aria-hidden="true"`. Den står inde i rækkeknappen, og
`TaskListScreen.RowTitled` matcher knappens **fulde** tilgængelige navn, så et synligt `3` derinde
ville få hver eksisterende rækkelokator til at holde op med at matche — en fejl der ligner en
manglende række.

To konsekvenser er værd at skrive ned, fordi de ellers bliver læst som fejl:

**Numrene springer over den fuldførte sektion.** Med "Vis færdige" slået til står de fuldførte
mellem "Venter på" og "Måske", og de har ingen numre — så badges kan gå `…6, 7` og fortsætte med `8`
under en unummereret blok. Det er den rigtige adfærd: en fuldført række har intet panel at vælge, så
et nummer på den ville være en tast der ikke gør noget.

**Numrene flytter sig, når listen gør.** En søgning, et statusskifte eller en ny opgave omfordeler
1–9. Det er prisen for "den n'te række", og netop den pris ville det rullende vindue have gjort
permanent.

## 6. Vagterne

`Every_shortcut_letter_on_screen_is_its_own` sammenligner allerede hele `aria-keyshortcuts`-strengen
ordinalt, så `Alt+O` og `Alt+Shift+O` er distinkte af sig selv: **logikken skal ikke røres.** Men
dens `BadgeCount = 9` skal, og den konstant er delt med badge-optællingen, hvor den er påstanden om
at Alt-hold faktisk maler mærkaterne.

Det er den ene rigtige komplikation i leverancen: **tallet er ikke længere en konstant.** Det er ni
faste bogstaver plus `min(9, antal valgbare rækker)` i fixturet plus otte feltbogstaver, hvoraf `V`
kun findes når status er "Venter på". En konstant der skal genberegnes for hver fixture-ændring, er
en vagt der bliver slået fra første gang den er i vejen. Derfor:

- Konstanten bliver **tre**: `NavBadges = 9` for navigationen og listekontrollerne,
  `FieldBadges = 7` for panelet uden hvem-feltet, og rækkerne **tælles fra fixturet** frem for
  skrives ned.
- Optællingen sker to gange i samme rejse: med panelet **lukket** (`NavBadges` + rækkerne) og med
  panelet **åbent** (plus `FieldBadges`). Forskellen mellem de to er selve påstanden om at feltlaget
  kom med, og den kan ikke bestå på en side der renderede ingen af dem.

Nye rejser, som hver skal ses fejle for sig:

- **`Alt+3` vælger tredje række.** Fixturet skal have rækkerne i en orden hvor tredje **ikke** er
  den der ville blive valgt af sig selv: auto-valget tager `[0]` side by side, så en rejse på 480 px
  og en påstand på række **tre** er assertionens tænder.
- **Række ti har ingen `aria-keyshortcuts`.** Guarden fra afsnit 3 målt frem for antaget.
- **`Alt+Shift+D` giver deadline-feltet fokus.**
- **`Alt+Shift+L` giver sletteknappen fokus uden at slette.** Den er den eneste der kan bevise
  `focus` frem for `activate`, og påstanden er at opgaven stadig står på listen bagefter.
- **`Alt+Shift+N` åbner noteeditoren *og* sætter caret'en i den**, altså at det eksisterende
  effekt-fokus bærer genvejen med.

`ContrastTests` koster også noget: `⇧D` er en ny tekstflade inde i detaljepanelet, og feltlagets
mærkater skal måles i begge temaer. Vagten kan ikke se en farve der aldrig blev renderet, så rejsen
skal **holde Alt nede, mens snapshottet tages** — det gør den ikke i dag, hvor badges kun findes
under de fire tastaturrejser.

## 7. Hvad der ikke laves

- **Ingen genvej til fluebenet.** "Fuldfør nummer n" er en destruktiv handling bag en tast man kan
  ramme ved siden af, og appen har ingen fortryd.
- **Ingen sektionsvalg og intet rullende vindue.** Se afsnit 2.
- **Ingen gemt tilstand.** Numrene beregnes af listen, ikke gemt, og der er intet felt på
  kontrakten og ingen migrering i denne leverance. Backenden røres ikke.
- **Ingen genvej på indstillingssiden eller importskærmene.** Feltlaget hører til opgavelisten, og
  `KeyboardJourneyTests` sammenligner kun de genveje der er renderet nu — en genvej der først findes
  på en anden skærm, skal have sit eget spørgsmål, når den kommer.
