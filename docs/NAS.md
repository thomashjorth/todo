# Todo på NAS'en — tjekliste

Leverance 3 af web-udgaven 2.0.0. Når listen er fulgt, henter NAS'en hvert nyt image selv inden for
fem minutter efter et grønt push til `main`. **Databasen er tom indtil leverance 5**: AI og importen
kræver hjælperen fra leverance 4, og dine tokens må aldrig ligge på NAS'en. Bliv ved med at bruge
`Todo.cmd` på PC'en indtil da. **AI står som utilgængelig på NAS'en** indtil hjælperen findes —
`127.0.0.1:11434` er containerens egen loopback, ikke din PC's.

`data`-mappen skal ligge på NAS'ens egen disk, ikke på et netværksdrev: SQLite's WAL kræver et lokalt
filsystem.

## Én gang på GitHub

1. Efter første grønne push: åbn pakken `todo` under din profil → **Packages** → **Package settings**
   → **Change visibility** → **Public**. En ny pakke er privat, og NAS'en har ingen GitHub-nøgle.

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
6. **Auto-deploy:** Kontrolpanel → Opgaveplanlægning → Opret → Planlagt opgave → Brugerdefineret
   script. Bruger `root`, hvert 5. minut, script:

   ```sh
   sh /volume1/docker/todo/update.sh
   ```

   Hedder kommandoen `docker-compose` med bindestreg på din DSM, så ret de to linjer i `update.sh`.
7. **Backup:** tag `/volume1/docker/todo/data` med i Hyper Backup. Den rummer databasen, backupperne
   før hver migrering og nøglerne der krypterer login-cookien.
8. **Adgang:** åbn **ingen** port i routeren. Uden for huset går vejen gennem Tailscale, som findes
   til DSM i Pakkecenter.

## Rul tilbage

Sæt `image:` i `docker-compose.yml` til `ghcr.io/thomashjorth/todo:<sha>` for den commit der virkede,
og kør `update.sh`. Den planlagte opgave holder sig så til den, indtil du sætter `latest` igen.
