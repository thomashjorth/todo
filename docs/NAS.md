# Todo på NAS'en — tjekliste

Web-udgaven 2.0.0. Når listen er fulgt, henter NAS'en hvert nyt image selv inden for fem minutter
efter et grønt push til `main`. **Jira, Azure DevOps og AI går gennem hjælperen på PC'en** —
`Helper.cmd` — og dine tokens ligger kun dér, aldrig på NAS'en. Uden hjælperen kører appen, men siger
at den ikke er forbundet.

`data`-mappen skal ligge på NAS'ens egen disk, ikke på et netværksdrev: SQLite's WAL kræver et lokalt
filsystem.

## GitHub

Intet at gøre. Pakken arvede repoets offentlige synlighed: målt 2026-10-26 efter første grønne
push, svarede GHCR 200 på `ghcr.io/thomashjorth/todo:latest` med et anonymt token. NAS'en behøver
derfor ingen GitHub-nøgle.

## Én gang på NAS'en

1. **Container Manager** fra Pakkecenter, hvis den ikke er der.
2. **Mappen:** opret `/volume1/docker/todo` og i den `data`. Kopiér `deploy/docker-compose.yml`,
   `deploy/update.sh` og `deploy/.env.example` derind, og omdøb den sidste til `.env`.
3. **Ejeren:** slå SSH til (Kontrolpanel → Terminal & SNMP), log ind som den bruger der ejer `data`,
   og kør `id`. `uid=1026(…) gid=100(users)` giver `TODO_UID=1026` og `TODO_GID=100` i `.env`.
4. **Passwordet**, lavet *i* containeren, så du ikke skal have .NET på NAS'en. Passwordet går gennem
   miljøet og aldrig gennem kommandolinjen, så det ikke havner i historikken:

   ```sh
   read -rs TODO_PASSWORD && export TODO_PASSWORD
   sudo -E docker run --rm -e TODO_PASSWORD ghcr.io/thomashjorth/todo:latest hash-password
   unset TODO_PASSWORD
   ```

   Læg linjen der begynder med `pbkdf2-sha256$` i `.env` som `TODO_PASSWORD_HASH='…'` — **i enkelte
   anførselstegn**, ellers læser Compose hvert `$` som en variabel. Det kan være et andet password
   end det lokale.
5. **Projektet:** Container Manager → Projekt → Opret → stien `/volume1/docker/todo`, brug den
   eksisterende `docker-compose.yml`. Appen svarer på `http://<nas>:8080`.
6. **Hjælperen:** start `Helper.cmd` på PC'en, log ind på `http://<nas>:8080`, og åbn Indstillinger →
   Forbindelser. Skriv `http://<nas>:8080` som serverens adresse, tryk **Lav en ny nøgle**, og læg
   hash'en i `.env` som `TODO_HELPER_KEY_HASH='…'` — i enkelte anførselstegn, som passwordets. Genstart
   projektet. Siden siger "Forbundet til serveren." når den er inde. Spørger Edge om siden må nå
   enheder på dit lokale netværk, så sig ja: det er NAS'ens side der viser hjælperens. Viser gruppen
   kun en sætning om PC'en efter tre sekunder, så åbn `http://127.0.0.1:47600/connections` direkte —
   og sig det, for så er rammen blokeret (se `docs/HANDOFF.md`).
7. **Auto-deploy:** Kontrolpanel → Opgaveplanlægning → Opret → Planlagt opgave → Brugerdefineret
   script. Bruger `root`, hvert 5. minut, script:

   ```sh
   sh /volume1/docker/todo/update.sh
   ```

   Hedder kommandoen `docker-compose` med bindestreg på din DSM, så ret de to linjer i `update.sh`.
8. **Backup:** tag `/volume1/docker/todo/data` med i Hyper Backup. Den rummer databasen, backupperne
   før hver migrering og nøglerne der krypterer login-cookien.
9. **Adgang:** åbn **ingen** port i routeren. Uden for huset går vejen gennem Tailscale, som findes
   til DSM i Pakkecenter.

## Flyt dine data

Gør det når du er klar til at skifte, for fra da af er det NAS'en der har opgaverne — `Todo.cmd` på
PC'en holder sin egen database og følger ikke med.

1. `ExportData.cmd` på PC'en. Den skriver `todo.db` på skrivebordet: én fil, med det nyeste fra WAL'en,
   og **uden** de Jira- og ADO-tokens 1.16.0 gemte. Din egen database læses kun.
2. Stop projektet i Container Manager.
3. Slet `todo.db`, `todo.db-wal` og `todo.db-shm` i `/volume1/docker/todo/data`, hvis de findes, og
   kopiér den eksporterede `todo.db` derind — som den bruger der ejer mappen, så filen er dens.
4. Start projektet. Migreringerne kører ved første start og tager en backup før dem.

## Rul tilbage

Sæt `image:` i `docker-compose.yml` til `ghcr.io/thomashjorth/todo:<sha>` for den commit der virkede,
og kør `update.sh`. Den planlagte opgave holder sig så til den, indtil du sætter `latest` igen.
