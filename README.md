# Todo

Personlig todo-app. Design: `docs/plans/2026-08-13-todo-app-design.md`.

## Kom i gang

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet run --project src\Todo.Host
```

## Udvikling med hot reload

Terminal 1:

```
dotnet run --project src\Todo.Host -- --headless --urls http://127.0.0.1:5199
```

Terminal 2:

```
npm.cmd start --prefix src\Todo.Web
```

Åbn http://localhost:4200. Brug `npm.cmd`, ikke `npm` — PowerShell-shimmen er i stykker.

## Tests

Backend og end-to-end (xUnit):

```
dotnet test Todo.sln
```

Angular-unittests (Vitest):

```
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

E2E kræver at `scripts\build-web.ps1` er kørt først, så den byggede app ligger i
`src\Todo.Host\wwwroot`. Første E2E-kørsel henter Chromium ned og tager derfor
et par minutter.

## Kontrakten

`contracts/openapi.yaml` ejer API'et. Ændrer du den, så kør `scripts\generate-api.ps1`
og commit den genererede kode — ellers fejler `GeneratedCodeFreshnessTests`.

## Styling

Al styling er standard Tailwind utility-klasser. Der skrives ingen CSS eller SCSS
i dette projekt. Appen bruges i en spalte på cirka 480 px, hvilket er under
Tailwinds `sm`-brydepunkt — de uprefixede klasser er den smalle udgave, og
`sm:`/`md:` bruges kun til at udvide.
