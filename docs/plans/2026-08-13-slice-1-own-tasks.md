# Skive 1 — dine egne opgaver

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** En app du faktisk kan bruge: opret, redigér og afslut opgaver med deadline, opgavestiller, note og en tjekliste af underopgaver, vist i én liste inddelt efter hvor meget de haster.

**Architecture:** `Todo.Core` får domænet (entiteter, `TodoDbContext` på SQLite, `IClock`, deadline-inddeling som ren funktion). `Todo.Host` udstiller CRUD som håndskrevne minimal APIs, der stadig håndhæves af drift-testen mod `contracts/openapi.yaml`. Angular får én `TaskStore`-service med signals; komponenter kalder aldrig HTTP selv.

**Tech Stack:** EF Core 10.0.11 (SQLite) · dotnet-ef 10.0.11 som lokalt værktøj · Angular 22 signals · Tailwind 4.3.3 · xunit.v3 · Playwright 1.62.0

**Verificeret 2026-08-13:** `Microsoft.EntityFrameworkCore.Sqlite`, `.Design` og `dotnet-ef` findes alle i 10.0.11. NSwag 14.7.1 har både `DateType` (C#) og `DateTimeType` (C# og TS). Ugedage brugt i testene: 2026-08-12 er onsdag, 2026-08-16 søndag, 2026-08-17 mandag.

## Beslutninger truffet før denne plan

| Emne | Valg |
| --- | --- |
| Redigering | Inline i listen. Ingen dialog — ved 480 px ville den dække alt. |
| Færdige opgaver | Forsvinder straks. En "Vis færdige"-kontakt henter dem frem. |
| State i Angular | Signal-baseret `TaskStore`. **Ikke NgRx** — bevidst fravalg til én entitet. |
| Underopgaver | Tjekliste: titel + flueben, ét niveau, aldrig egen deadline eller egen plads i sektionerne. Forælderen viser "2/5". |

## Navngivning — læs dette først

**Kald aldrig noget `Task` eller `TaskStatus` i C#.** `System.Threading.Tasks.Task` og `System.Threading.Tasks.TaskStatus` findes i enhver fil via implicit usings, og en kollision giver fejl der peger et helt andet sted hen end årsagen. Derfor:

- Kontraktens skemaer hedder `TodoTask`, `TodoStatus`, `TodoSubTask`, `DeadlineBucket`.
- EF-entiteterne hedder `TaskItem` og `SubTask` i `Todo.Core`.

## Bevidst uden for skive 1

Sortering på tværs af sektioner, tags, gentagne opgaver, søgning, undo, tastaturgenveje. Ingen eksterne kilder — `SourceId` sættes til `manual` og bruges ikke til noget endnu.

---

## Task 1: Deadline-inddeling som ren funktion

Den eneste rigtige forretningslogik i skiven. Ingen database, ingen HTTP — derfor kan den testes udtømmende på millisekunder.

**Files:**
- Create: `src/Todo.Core/Todo.Core.csproj`
- Create: `src/Todo.Core/IClock.cs`
- Create: `src/Todo.Core/DeadlineBuckets.cs`
- Create: `tests/Todo.Core.Tests/Todo.Core.Tests.csproj`
- Create: `tests/Todo.Core.Tests/FixedClock.cs`
- Create: `tests/Todo.Core.Tests/DeadlineBucketsTests.cs`

**Step 1: Opret projekterne**

`src/Todo.Core/Todo.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Todo.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

`tests/Todo.Core.Tests/Todo.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Todo.Core\Todo.Core.csproj" />
  </ItemGroup>
</Project>
```

`<Using Include="Xunit" />` er nødvendig — xunit.v3 har ingen implicit global using. `xUnit1051` er allerede håndteret af `tests/Directory.Build.props`; undertryk den ikke igen.

**Step 2: Skriv de fejlende tests**

`tests/Todo.Core.Tests/FixedClock.cs`:

```csharp
using Todo.Core;

namespace Todo.Core.Tests;

public sealed class FixedClock(DateOnly today) : IClock
{
    public DateTime UtcNow { get; } = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public DateOnly Today { get; } = today;
}
```

`tests/Todo.Core.Tests/DeadlineBucketsTests.cs`:

```csharp
using Todo.Core;

namespace Todo.Core.Tests;

public class DeadlineBucketsTests
{
    // 2026-08-12 is a Wednesday, so the week ends Sunday 2026-08-16.
    private static readonly DateOnly Wednesday = new(2026, 8, 12);

    [Fact]
    public void No_deadline_is_its_own_bucket()
        => Assert.Equal(DeadlineBucket.NoDeadline, DeadlineBuckets.For(null, Wednesday));

    [Fact]
    public void Yesterday_is_overdue()
        => Assert.Equal(DeadlineBucket.Overdue, DeadlineBuckets.For(new(2026, 8, 11), Wednesday));

    [Fact]
    public void Today_is_today()
        => Assert.Equal(DeadlineBucket.Today, DeadlineBuckets.For(Wednesday, Wednesday));

    [Fact]
    public void Tomorrow_is_this_week()
        => Assert.Equal(DeadlineBucket.ThisWeek, DeadlineBuckets.For(new(2026, 8, 13), Wednesday));

    [Fact]
    public void The_coming_sunday_is_still_this_week()
        => Assert.Equal(DeadlineBucket.ThisWeek, DeadlineBuckets.For(new(2026, 8, 16), Wednesday));

    [Fact]
    public void The_following_monday_is_later()
        => Assert.Equal(DeadlineBucket.Later, DeadlineBuckets.For(new(2026, 8, 17), Wednesday));

    [Fact]
    public void On_a_sunday_tomorrow_belongs_to_the_next_week()
    {
        var sunday = new DateOnly(2026, 8, 16);
        Assert.Equal(DeadlineBucket.Later, DeadlineBuckets.For(new(2026, 8, 17), sunday));
    }
}
```

Den sidste test er den vigtige. Uger går fra mandag til søndag, så på en søndag er "denne uge" slut i dag — og mandag hører til næste uge. En naiv "inden for syv dage"-implementering består alle de andre tests og fejler denne.

**Step 3: Kør testene — de skal fejle**

```bash
dotnet sln add src/Todo.Core/Todo.Core.csproj tests/Todo.Core.Tests/Todo.Core.Tests.csproj
```

Run: `dotnet test tests/Todo.Core.Tests`
Expected: **compile error** — `IClock`, `DeadlineBucket` og `DeadlineBuckets` findes ikke.

**Step 4: Skriv implementeringen**

`src/Todo.Core/IClock.cs`:

```csharp
namespace Todo.Core;

public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
```

`Today` bruger lokal tid med vilje: "i dag" er brugerens dag, ikke UTC's.

`src/Todo.Core/DeadlineBuckets.cs`:

```csharp
namespace Todo.Core;

public enum DeadlineBucket
{
    Overdue,
    Today,
    ThisWeek,
    Later,
    NoDeadline,
}

public static class DeadlineBuckets
{
    public static DeadlineBucket For(DateOnly? deadline, DateOnly today)
    {
        if (deadline is not { } due)
        {
            return DeadlineBucket.NoDeadline;
        }

        if (due < today)
        {
            return DeadlineBucket.Overdue;
        }

        if (due == today)
        {
            return DeadlineBucket.Today;
        }

        return due <= EndOfWeek(today) ? DeadlineBucket.ThisWeek : DeadlineBucket.Later;
    }

    // Weeks run Monday to Sunday, so on a Sunday the week ends today.
    private static DateOnly EndOfWeek(DateOnly today)
        => today.AddDays(((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7);
}
```

**Step 5: Kør testene igen**

Run: `dotnet test tests/Todo.Core.Tests`
Expected: **7 passed**.

Run: `dotnet test Todo.sln`
Expected: **12 passed** (5 fra skive 0 + 7 nye), 0 warnings.

**Step 6: Commit**

```bash
git add -A && git commit -m "✨ Add deadline bucketing as a pure, clock-injected function"
```

---

## Task 2: Persistering med EF Core og SQLite

**Files:**
- Modify: `src/Todo.Core/Todo.Core.csproj`
- Create: `src/Todo.Core/TaskItem.cs`
- Create: `src/Todo.Core/SubTask.cs`
- Create: `src/Todo.Core/TodoDbContext.cs`
- Create: `src/Todo.Core/TodoDatabase.cs`
- Modify: `.config/dotnet-tools.json` (via kommando)
- Modify: `src/Todo.Host/Todo.Host.csproj`
- Modify: `src/Todo.Host/TodoHost.cs`
- Modify: `tests/Todo.TestSupport/RunningHost.cs`
- Create: `src/Todo.Core/Migrations/**` (genereret)

**Step 1: Pakker og værktøj**

```bash
dotnet add src/Todo.Core package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.11
```

```bash
dotnet add src/Todo.Core package Microsoft.EntityFrameworkCore.Design --version 10.0.11
```

```bash
dotnet tool install dotnet-ef --version 10.0.11
```

Værktøjet skal være **lokalt** i `.config/dotnet-tools.json`, hvor NSwag allerede ligger. Den globalt installerede `dotnet-ef` er 7.0.16 og kan ikke læse en EF Core 10-model. Kør altid `dotnet dotnet-ef …`, aldrig `dotnet ef …`, så du er sikker på at ramme den lokale.

**Step 2: Entiteterne**

`src/Todo.Core/TaskItem.cs`:

```csharp
namespace Todo.Core;

public enum TodoStatus
{
    Open,
    InProgress,
    Done,
}

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SourceId { get; set; } = "manual";

    public string Title { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateOnly? Deadline { get; set; }

    public string? Requester { get; set; }

    public TodoStatus Status { get; set; } = TodoStatus.Open;

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<SubTask> SubTasks { get; set; } = [];
}
```

`src/Todo.Core/SubTask.cs`:

```csharp
namespace Todo.Core;

public class SubTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }
}
```

`Deadline` er `DateOnly` — ikke `DateTime` og under ingen omstændigheder `DateTimeOffset`. SQLite har ikke rigtige typer, og EF Core lagrer `DateTimeOffset` som tekst, hvor sortering og sammenligning ikke er korrekt. `CompletedAt` og `CreatedAt` er `DateTime` i UTC.

**Step 3: DbContext**

`src/Todo.Core/TodoDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Todo.Core;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<SubTask> SubTasks => Set<SubTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();
        task.Property(t => t.Title).IsRequired().HasMaxLength(500);
        task.Property(t => t.SourceId).IsRequired().HasMaxLength(50);
        task.Property(t => t.Status).HasConversion<string>();
        task.HasIndex(t => t.Deadline);

        task.HasMany(t => t.SubTasks)
            .WithOne()
            .HasForeignKey(s => s.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubTask>()
            .Property(s => s.Title).IsRequired().HasMaxLength(500);
    }
}
```

`HasConversion<string>()` på status: gemmes som `"Open"` frem for `0`, så databasen kan læses uden kodebogen, og indsættelse af en ny værdi midt i enummet ikke omdøber eksisterende rækker.

**Step 4: Opstart, backup og WAL**

`src/Todo.Core/TodoDatabase.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Todo.Core;

public static class TodoDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EdoraTodo",
        "todo.db");

    public static async Task PrepareAsync(TodoDbContext db, string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        // The data exists only on this machine, so a failed migration is permanent loss.
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any() && File.Exists(databasePath))
        {
            File.Copy(databasePath, $"{databasePath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}");
        }

        await db.Database.MigrateAsync();

        // Background sync will write while the UI reads.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
```

Brug **aldrig** `EnsureCreated()`. Den opretter skemaet uden om `__EFMigrationsHistory`, og næste `MigrateAsync()` fejler så på en database der "allerede findes".

**Step 5: Kobl det på hosten**

I `src/Todo.Host/Todo.Host.csproj`, tilføj `<ProjectReference Include="..\Todo.Core\Todo.Core.csproj" />`.

I `TodoHost.Build`, efter `AddOpenApi()`:

```csharp
        var databasePath = builder.Configuration["Data:Path"] ?? TodoDatabase.DefaultPath;
        builder.Services.AddDbContext<TodoDbContext>(o => o.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddSingleton<IClock, SystemClock>();
```

Og efter `var app = builder.Build();`:

```csharp
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            TodoDatabase.PrepareAsync(db, databasePath).GetAwaiter().GetResult();
        }
```

`TodoHost.Build` er synkron, og migreringen skal være færdig før første request. Det er den ene blokerende ventetid i opstarten, og den er bevidst.

**Step 6: Isolér testene fra din rigtige database**

Det her er ikke valgfrit: uden det skriver hver testkørsel i `%APPDATA%\EdoraTodo\todo.db`.

I `tests/Todo.TestSupport/RunningHost.cs`, giv `StartAsync` en midlertidig databasesti og ryd op efter den:

```csharp
    private readonly string _databasePath;

    public static async Task<RunningHost> StartAsync(params string[] extraArgs)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(), "EdoraTodo.Tests", $"{Guid.NewGuid():N}.db");

        string[] args =
        [
            "--urls", "http://127.0.0.1:0",
            "--contentRoot", RepoPaths.HostContentRoot,
            "--Data:Path", databasePath,
            .. extraArgs
        ];
        ...
    }
```

`DisposeAsync` skal slette filen og dens `-wal`/`-shm`-sidefiler efter `StopAsync`. Slår sletningen fejl, så lad testen bestå — en efterladt midlertidig fil er ikke værd at fejle på.

**Step 7: Første migration**

```bash
dotnet dotnet-ef migrations add InitialCreate --project src/Todo.Core --startup-project src/Todo.Host
```

**Læs den genererede migration igennem.** Bekræft at `Deadline` bliver `TEXT`, at `Status` bliver `TEXT`, og at fremmednøglen fra `SubTasks` har `onDelete: Cascade`.

**Step 8: Verificér**

Run: `dotnet test Todo.sln`
Expected: **12 passed**, 0 warnings. De eksisterende tests starter nu hosten med en rigtig database og skal stadig være grønne.

Bekræft desuden manuelt at der ikke er oprettet noget i `%APPDATA%\EdoraTodo\` af testene.

**Step 9: Commit**

```bash
git add -A && git commit -m "🗃️ Add SQLite persistence with migrations, WAL and a pre-migration backup"
```

---

## Task 3: Kontrakt og CRUD-endpoints for opgaver

Kontrakten ændres først. Drift-testen bliver rød, og det **er** den røde fase.

**Files:**
- Modify: `contracts/openapi.yaml`
- Modify: `scripts/generate-api.ps1`
- Modify: `src/Todo.Host/TodoHost.cs`
- Create: `src/Todo.Host/TaskEndpoints.cs`
- Create: `tests/Todo.Api.Tests/TaskEndpointsTests.cs`

**Step 1: Lær generatoren om datoer**

I `scripts/generate-api.ps1`, tilføj til `openapi2csclient`:

```
    /dateType:System.DateOnly `
```

og til `openapi2tsclient`:

```
    /dateTimeType:string `
```

**Dette er den vigtigste linje i hele skiven.** En deadline er en dato uden klokkeslæt. Rejser den gennem en `DateTimeOffset` i C# eller en `Date` i TypeScript, bliver den fortolket som midnat i en tidszone, og en opgave med frist i dag kan vises som i går. Som ISO-streng hele vejen kan det ikke ske.

Kontrollér flagnavnene med `dotnet nswag help openapi2csclient` hvis et afvises.

**Step 2: Udvid kontrakten**

Tilføj til `contracts/openapi.yaml` under `paths`:

```yaml
  /api/tasks:
    get:
      operationId: listTasks
      tags:
        - Tasks
      parameters:
        - name: includeCompleted
          in: query
          required: false
          schema:
            type: boolean
            default: false
      responses:
        '200':
          description: The tasks, ordered by deadline.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TodoTaskListResponse'
    post:
      operationId: createTask
      tags:
        - Tasks
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateTodoTaskRequest'
      responses:
        '201':
          description: The created task.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TodoTask'
  /api/tasks/{id}:
    put:
      operationId: updateTask
      tags:
        - Tasks
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            format: uuid
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateTodoTaskRequest'
      responses:
        '200':
          description: The updated task.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TodoTask'
        '404':
          description: No task with that id.
    delete:
      operationId: deleteTask
      tags:
        - Tasks
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
            format: uuid
      responses:
        '204':
          description: The task is gone.
        '404':
          description: No task with that id.
```

Og under `components.schemas`:

```yaml
    TodoStatus:
      type: string
      enum: [open, inProgress, done]
    DeadlineBucket:
      type: string
      enum: [overdue, today, thisWeek, later, noDeadline]
    TodoTask:
      type: object
      additionalProperties: false
      required: [id, sourceId, title, status, bucket, createdAt, subTasks]
      properties:
        id: { type: string, format: uuid }
        sourceId: { type: string }
        title: { type: string }
        note: { type: string, nullable: true }
        deadline: { type: string, format: date, nullable: true }
        requester: { type: string, nullable: true }
        status: { $ref: '#/components/schemas/TodoStatus' }
        bucket: { $ref: '#/components/schemas/DeadlineBucket' }
        completedAt: { type: string, format: date-time, nullable: true }
        createdAt: { type: string, format: date-time }
        subTasks:
          type: array
          items:
            $ref: '#/components/schemas/TodoSubTask'
    TodoSubTask:
      type: object
      additionalProperties: false
      required: [id, title, isDone]
      properties:
        id: { type: string, format: uuid }
        title: { type: string }
        isDone: { type: boolean }
    TodoTaskListResponse:
      type: object
      additionalProperties: false
      required: [items]
      properties:
        items:
          type: array
          items:
            $ref: '#/components/schemas/TodoTask'
    CreateTodoTaskRequest:
      type: object
      additionalProperties: false
      required: [title]
      properties:
        title: { type: string, minLength: 1, maxLength: 500 }
        note: { type: string, nullable: true }
        deadline: { type: string, format: date, nullable: true }
        requester: { type: string, nullable: true }
    UpdateTodoTaskRequest:
      type: object
      additionalProperties: false
      required: [title, status]
      properties:
        title: { type: string, minLength: 1, maxLength: 500 }
        note: { type: string, nullable: true }
        deadline: { type: string, format: date, nullable: true }
        requester: { type: string, nullable: true }
        status: { $ref: '#/components/schemas/TodoStatus' }
```

`subTasks` er med i `TodoTask` allerede nu, men får først endpoints i task 8. En tom liste er et ærligt svar; en kontrakt der ændrer form to gange er ikke.

**Step 3: Regenerér og se drift-testen fejle**

Run: `powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1`
Run: `dotnet test tests/Todo.Api.Tests`

Expected: `ContractDriftTests` **FEJLER** med fire operationer i expected der mangler i actual. Det er den røde fase — noter fejlteksten.

Bekræft samtidig at `GeneratedCodeFreshnessTests` består: hash-filen er opdateret af generatoren.

**Step 4: Implementér endpoints**

`src/Todo.Host/TaskEndpoints.cs` — en `MapTasks(this IEndpointRouteBuilder)`-extension der mapper de fire operationer. Krav:

- `listTasks` returnerer opgaver sorteret efter deadline, med `null` sidst, derefter `CreatedAt`. Uden `includeCompleted=true` udelades `Status = Done`.
- `bucket` udregnes med `DeadlineBuckets.For(task.Deadline, clock.Today)`. **Aldrig i frontend** — logikken skal have ét sted, og det sted har unit tests.
- `createTask` sætter `CreatedAt = clock.UtcNow`, `SourceId = "manual"`, returnerer `201` med `Location`.
- `updateTask` sætter `CompletedAt = clock.UtcNow` når status skifter til `Done`, og rydder feltet igen hvis den skifter væk fra `Done`.
- Tom eller kun-mellemrum i `title` giver `400`.
- Ukendt id giver `404`, ikke `500`.

Registrér med `app.MapTasks();` i `TodoHost.Build`, før `MapFallbackToFile`.

**Step 5: Skriv API-testene**

`tests/Todo.Api.Tests/TaskEndpointsTests.cs`, mindst:

- Oprettet opgave kan hentes igen, og `bucket` er `noDeadline`.
- Opgave med gårsdagens deadline får `bucket = overdue`.
- Færdig opgave er udeladt som standard og med i svaret ved `includeCompleted=true`.
- Skift til `done` sætter `completedAt`; skift tilbage til `open` rydder det.
- Tom titel giver `400`.
- `PUT` og `DELETE` på et ukendt id giver `404`.
- `DELETE` fjerner opgaven.

Brug `RunningHost` som de øvrige tests. Hver test får sin egen tomme database.

**Step 6: Kør alt**

Run: `dotnet test Todo.sln`
Expected: alle grønne — drift-testen igen med, nu fordi implementeringen matcher kontrakten.

**Step 7: Commit**

```bash
git add -A && git commit -m "✨ Add task CRUD endpoints driven by the contract"
```

---

## Task 4: Listen i Angular

**Files:**
- Create: `src/Todo.Web/src/app/tasks/task-store.ts`
- Create: `src/Todo.Web/src/app/tasks/task-list.ts`
- Create: `src/Todo.Web/src/app/tasks/task-list.html`
- Modify: `src/Todo.Web/src/app/app.ts`
- Modify: `src/Todo.Web/src/app/app.html`

**Step 1: Store med signals**

`task-store.ts`: en `@Injectable({ providedIn: 'root' })`-service der ejer al HTTP.

```ts
@Injectable({ providedIn: 'root' })
export class TaskStore {
  private readonly client = inject(TasksClient);

  readonly tasks = signal<TodoTask[]>([]);
  readonly showCompleted = signal(false);

  readonly sections = computed(() => {
    const order: DeadlineBucket[] = ['overdue', 'today', 'thisWeek', 'later', 'noDeadline'];
    return order
      .map((bucket) => ({ bucket, tasks: this.tasks().filter((t) => t.bucket === bucket) }))
      .filter((section) => section.tasks.length > 0);
  });

  async load(): Promise<void> {
    const response = await firstValueFrom(this.client.listTasks(this.showCompleted()));
    this.tasks.set(response.items);
  }
}
```

Komponenter må aldrig kalde `client` direkte og aldrig `.subscribe()`. Tomme sektioner vises ikke — en overskrift uden indhold er støj i en smal spalte.

**Step 2: Listekomponenten**

`ng generate component tasks/task-list` og tilpas. Skabelonen skal:

- Løbe gennem `store.sections()` og vise en overskrift pr. sektion med dansk tekst: Overskredet, I dag, Denne uge, Senere, Uden deadline.
- Vise titel, og under den deadline og opgavestiller som små grå linjer — **ikke** i kolonner ved siden af hinanden.
- Give `Overskredet` en rød accent via Tailwind (`text-red-600` / `border-red-200`), resten neutral.
- Have `data-testid="task-section"` på hver sektion og `data-testid="task-row"` på hver række, så E2E kan gribe fat.

Alt layout skal virke ved 465 px uden vandret scroll. Ingen CSS-fil — kun Tailwind-klasser, og uprefixede klasser er den smalle udgave.

**Step 3: Vis den i appen**

`app.html` bliver til overskriften "Todo" plus `<app-task-list />`. Health-linjen fra skive 0 må gerne blive stående nederst i småt.

**Step 4: Verificér**

Run: `powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1`
Run: `dotnet run --project src\Todo.Host -- --headless --urls http://127.0.0.1:5199`

Databasen er tom, så listen er tom. Opret en opgave med et direkte `POST /api/tasks` og genindlæs siden — den skal nu stå i den rigtige sektion. Rapportér hvad du så.

**Step 5: Commit**

```bash
git add -A && git commit -m "✨ Show tasks grouped into deadline sections"
```

---

## Task 5: Opret opgave inline

**Files:**
- Modify: `task-store.ts`, `task-list.ts`, `task-list.html`

Et enkelt inputfelt øverst i listen. Enter opretter opgaven med kun en titel og rydder feltet; alt andet udfyldes bagefter ved at folde rækken ud.

- Tom eller kun-mellemrum: gør ingenting, ingen fejlmeddelelse. Feltet er ikke en formular.
- Efter oprettelse genindlæses listen, så den nye opgave lander i den rigtige sektion.
- `data-testid="new-task-input"`.

Slut med `dotnet test Todo.sln` grøn og en commit: `✨ Add tasks inline from the top of the list`

---

## Task 6: Fold ud, redigér og slet

**Files:**
- Modify: `task-store.ts`, `task-list.ts`, `task-list.html`

Klik på en række folder den ud på stedet. Udfoldet vises deadline (`<input type="date">`), opgavestiller, note (`<textarea>`), status (`<select>`) og en sletteknap. Kun én række ad gangen er foldet ud — hold `expandedId = signal<string | null>(null)` i komponenten, ikke i storen; det er visningstilstand, ikke data.

- Gem sker ved blur eller Enter på et felt, ikke via en Gem-knap. Ingen dialog, ingen ekstra klik.
- Deadline sendes som `yyyy-MM-dd`-streng, præcis som `<input type="date">` leverer den. **Konvertér den aldrig til en `Date`** — det er der en dags forskydning gemmer sig.
- Sletning sker uden bekræftelse. Det er en personlig todo-liste; en dialog for hver sletning er værre end at oprette opgaven igen.

Commit: `✨ Edit and delete tasks inline`

---

## Task 7: Afslut opgaver og vis færdige

**Files:**
- Modify: `task-store.ts`, `task-list.ts`, `task-list.html`

Et flueben yderst til venstre på hver række sætter status til `done`. Opgaven forsvinder fra listen med det samme.

En "Vis færdige"-kontakt øverst sætter `showCompleted` og genindlæser. Færdige vises i en egen sektion nederst, overstreget og grå, med mulighed for at fjerne fluebenet igen.

- `data-testid="complete-toggle"` på fluebenet, `data-testid="show-completed"` på kontakten.
- Genindlæs efter hver statusændring — serveren ejer `bucket` og `completedAt`.

Commit: `✨ Complete tasks and bring them back with a toggle`

---

## Task 8: Underopgaver — kontrakt og endpoints

Samme mønster som task 3: kontrakt først, drift-testen rød, derefter implementering.

**Endpoints:**

```
POST   /api/tasks/{id}/subtasks              → 201 TodoSubTask
PUT    /api/tasks/{id}/subtasks/{subTaskId}  → 200 TodoSubTask
DELETE /api/tasks/{id}/subtasks/{subTaskId}  → 204
```

`CreateSubTaskRequest` har kun `title`. `UpdateSubTaskRequest` har `title` og `isDone`.

**Regler, som API-testene skal dække:**

- En underopgave hører til præcis én opgave. Ukendt opgave-id giver `404`.
- Nye underopgaver får `SortOrder` = højeste eksisterende + 1, så tjeklisten beholder sin rækkefølge.
- Sletning af en opgave sletter dens underopgaver (cascade). Test det på databasen, ikke kun gennem API'et.
- **En underopgave påvirker ikke forælderens status.** At sætte flueben ved alle fem afslutter ikke opgaven — det gør du selv. Automatikken ser smart ud og rammer forkert, første gang en tjekliste ikke er udtømmende.
- Underopgaver har ingen deadline og optræder aldrig i deadline-sektionerne.

Commit: `✨ Add subtask endpoints as a checklist under each task`

---

## Task 9: Tjeklisten i Angular

**Files:**
- Modify: `task-store.ts`, `task-list.ts`, `task-list.html`

I den udfoldede række: en liste af underopgaver med flueben og titel, plus et inputfelt til at tilføje en ny med Enter. På den sammenfoldede række vises fremdrift som `2/5` i småt — kun når opgaven har underopgaver.

- Klik på en underopgaves flueben må ikke folde rækken sammen. Husk `$event.stopPropagation()`.
- `data-testid="subtask-row"`, `data-testid="new-subtask-input"`, `data-testid="subtask-progress"`.

Commit: `✨ Show and edit the subtask checklist inline`

---

## Task 10: E2E og samlet grøn kørsel

**Files:**
- Create: `tests/Todo.E2E/TaskJourneyTests.cs`
- Modify: `README.md`
- Modify: `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Skriv rejsen som én test**

Ved viewport 480 × 1000, mod en tom database:

1. Skriv "Køb kaffe" i `new-task-input`, tryk Enter → rækken findes i "Uden deadline".
2. Fold ud, sæt deadline til i dag → rækken flytter til "I dag".
3. Tilføj to underopgaver → `subtask-progress` viser `0/2`; sæt flueben ved den ene → `1/2`.
4. Sæt flueben ved opgaven → den forsvinder fra listen.
5. Slå "Vis færdige" til → den er der igen, overstreget.
6. Undervejs: `document.documentElement.scrollWidth <= 480` holder stadig.

Brug `Expect(...)`-assertions, ikke `Sleep`. Hvert skridt venter på et resultat, ikke på tid.

**Step 2: Se den fejle**

Bryd én ting med vilje — fx få `updateTask` til ikke at sætte `completedAt` — og bekræft at rejsen fejler på det rigtige skridt. Ret tilbage. Husk at `Name = "..."` i Playwright matcher på delstreng med mindre `Exact = true` sættes; det narrede allerede en test i skive 0.

**Step 3: Opdatér dokumentationen**

README får et afsnit om databasen: hvor `todo.db` ligger, at migrationer køres ved opstart, hvor backupfilerne havner, og hvordan man nulstiller (slet filen). Designdokumentet får skive 1 markeret som færdig og de faktiske datamodelfelter, hvis de afveg.

**Step 4: Kør alt fra bunden**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Run: `dotnet test Todo.sln`
Run: `npm.cmd run test --prefix src\Todo.Web -- --watch=false`

Expected: alt grønt, 0 warnings.

**Step 5: Commit**

```bash
git add -A && git commit -m "✅ Cover the full task journey end to end"
```

---

## Færdig når

- Du kan oprette, redigere, nedbryde og afslutte en opgave i appen uden at røre et API-værktøj.
- Listen inddeler efter deadline, og færdige forsvinder indtil du beder om dem.
- `dotnet test Todo.sln` og Vitest er grønne, 0 warnings.
- Drift-testen og friskheds-testen er hver set fejle mindst én gang i denne skive.
- Ingen CSS- eller SCSS-regler er skrevet.
- Testene har ikke rørt `%APPDATA%\EdoraTodo\`.

## Til skive 2 (retro-import)

- `TaskItem` mangler stadig `ExternalKey`, som retro-dedup skal bruge. Det bliver skivens første migration.
- Import-skærmen genbruger `TaskStore`; den skal ikke have sin egen HTTP-kode.
- Alias-listen til `Action Owner` hører i `Setting`, som endnu ikke findes som tabel.
