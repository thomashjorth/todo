# Web-udgaven 2.0.0, leverance 3: container og auto-deploy — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** Et push til `main` giver grønne tests, et image der er prøvet i en rigtig container, og et
image i `ghcr.io/thomashjorth/todo`. NAS'en henter det selv inden for fem minutter. NAS'en kører
med en **tom** database indtil leverance 5.

**Designet er `docs/plans/2026-10-26-web-hosting-design.md`, afsnit 6.** Køreplanen er
`docs/plans/2026-10-26-web-hosting.md`.

**Planen kan være forkert.** Mål frem for at tro, og ret planfilen når en måling modsiger den.

## Tre målinger der omskrev designet, før en linje blev skrevet

1. **CI har aldrig været grøn.** GitHubs API giver nul gennemførte kørsler; hver kørsel siden
   september fejler i `dotnet test`, også på `v1.16.0`. Loggen kræver login. Et image-job med
   `needs: test` ville aldrig køre, og et uden ville deploye utestet kode. **Opgave 1 er derfor at
   gøre CI grøn**, før noget andet bygges oven på den.
2. **Watchtower er arkiveret** (17. december 2025, sidste udgivelse 2023, ingen sikkerhedsrettelser).
   Vedligeholderen fraråder forkene. Designets alternativ tages i stedet: **en planlagt opgave i
   DSM** der kører `docker compose pull` og `up -d` hvert femte minut. Intet tredjepartsimage.
3. **Docker på udviklingsmaskinen kører Windows-containere.** Et Linux-image kan hverken bygges
   eller køres lokalt, og at skifte tilstand er brugerens opsætning. **Imaget bygges og prøves
   derfor kun i CI**, på ubuntu — og proben der skal fælde et dårligt image, er et script CI kører.

## En fælde i koden, fundet ved læsning

**`TodoHost.Build` sætter `UseUrls("http://127.0.0.1:0")`, når konfigurationsnøglen `urls` mangler.**
.NET-imagets `ASPNETCORE_HTTP_PORTS=8080` er en *anden* nøgle, så en container uden mere ville lytte
på loopback inde i sig selv og være utilgængelig udefra — mens alt i den ser sundt ud. Imaget sætter
derfor `ASPNETCORE_URLS=http://+:8080`, som bliver til nøglen `urls`. Proben kalder udefra, så den
fælder netop den fejl.

---

### Opgave 1: CI bliver grøn

Genskab fejlen fra en frisk clone kørt som CI kører den, find årsagen, ret den, og bekræft med en
kørsel på GitHub. Kan årsagen ikke genskabes lokalt, så spørg brugeren om loggen.

### Opgave 2: Imaget

- `Dockerfile` i tre trin: `node:22` bygger Angular ind i `src/Todo.Host/wwwroot`;
  `mcr.microsoft.com/dotnet/sdk:10.0` publicerer `Todo.Host` til linux-x64, framework-dependent;
  `mcr.microsoft.com/dotnet/aspnet:10.0` kører den som imagets ikke-root-bruger.
- `ENV ASPNETCORE_URLS=http://+:8080`, `TZ=Europe/Copenhagen`, `Data__Path=/data/todo.db`,
  `VOLUME /data`, `EXPOSE 8080`. Nøglerne ligger så i `/data/keys` af sig selv.
- `TZ` er bærende: `SystemClock.Today` er `DateTime.Now`, og i UTC er "i dag" i går mellem 00 og 02.
  **Om `tzdata` findes i aspnet-imaget er umålt** — proben måler det.
- `.dockerignore`: `node_modules`, `bin`, `obj`, `wwwroot`-output, `.git`, `publish`, `tests`.

### Opgave 3: Proben

`scripts/probe-image.sh <image>`: starter containeren med en engangs-hash og en midlertidig
`/data`, venter på `/api/health`, og kræver
- 200 på `/api/health`, `/` med `<app-root` i kroppen, og `/scalar/`;
- **401** på `/api/tasks` uden cookie — login er slået til i imaget;
- at processen ikke kører som root (`docker exec … id -u` ≠ 0);
- at containerens tidszone er dansk (`date +%Z` er `CET` eller `CEST`);
- at databasen og nøglemappen ligger i `/data`.

`*.sh` får `eol=lf` i `.gitattributes`: et script med CRLF fejler i bash på `\r`.

### Opgave 4: CI-jobbet

`image` i `ci.yml`: `needs: test`, kun ved push til `main`, `ubuntu-latest`, `permissions:
packages: write`. Bygger imaget, kører proben, og skubber først derefter `:latest` og `:<sha>` med
`GITHUB_TOKEN`. Pakken bliver **privat** ved første push — synligheden sættes til offentlig én gang i
pakkens indstillinger (brugerens skridt; mål det efter første push).

### Opgave 5: NAS'en

- `deploy/docker-compose.yml`: todo-containeren, porten, volumet, `restart: unless-stopped`,
  `user:` fra `.env`, og `Auth__PasswordHash` fra `.env`.
- `deploy/.env.example` med de tre værdier og hvordan hver findes.
- `deploy/update.sh`: `docker compose pull` og `up -d`, til DSM's opgaveplanlægger.
- `docs/NAS.md`: tjeklisten brugeren følger — mappe, ejer (`id` på NAS'en), hash lavet *i*
  containeren med `TODO_PASSWORD` i miljøet, Container Manager, den planlagte opgave, Tailscale.

### Opgave 6: Docs, `Check.cmd` og testtal
