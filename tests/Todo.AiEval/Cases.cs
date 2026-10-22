using Todo.Core.Ai;

namespace Todo.AiEval;

/// <summary>
/// The fixed inputs the prompts are scored on. Today is pinned to the Thursday the prompts were first
/// measured on, so every expected date is written out rather than computed - a computed expectation
/// would share a bug with the code it checks.
/// </summary>
public static class Cases
{
    public static readonly DateOnly Today = new(2026, 10, 22);

    /// <summary>The line is ambiguous in this field, and either answer is acceptable.</summary>
    public const string Any = "*";

    public static readonly ParseCase[] Parse =
    [
        new("Ring til Anna om fakturaen på fredag", Any, "2026-10-23", ["Anna", "faktura"], ["fredag"]),
        new("Send rapporten til Henrik næste mandag", Any, "2026-10-26", ["rapport"], ["mandag"]),
        new("Køb mælk", null, null, ["mælk"], []),
        new("Mads vil have et estimat på login-sagen i morgen", "Mads", "2026-10-23", ["estimat"], ["i morgen"]),
        new("Book mødelokale til onsdag", null, "2026-10-28", ["mødelokale"], ["onsdag"]),
        new("Opdatér certifikatet på testserveren inden 1. november", null, "2026-11-01", ["certifikat"], ["november"]),
        new("Svar Lene fra 2nd level om eksportfejlen senest 30/10", Any, "2026-10-30", ["eksport"], ["30/10"]),

        // Today is a Thursday, so "torsdag" on its own is next week's, not today.
        new("Forbered sprint review til torsdag", null, "2026-10-29", ["sprint review"], ["torsdag"]),
        new("Ryd op i backloggen", null, null, ["backlog"], []),
        new("Peter har bedt om en status på migreringen inden fredag", "Peter", "2026-10-23", ["migrering"], ["fredag"]),
        new("Call the dentist tomorrow", null, "2026-10-23", ["dentist"], ["tomorrow"]),

        // A month is not a day, and the prompt asks for a date only if the line names one.
        new("Husk at forny kørekortet i december", null, null, ["kørekort"], []),

        // The user's own test 2026-10-22: the steps are in the line, so they become subtasks.
        new("Lav en præsentation, først udkast så finpudsning", null, null, ["præsentation"], ["udkast", "finpudsning"], ["udkast", "finpudsning"]),
        new("Flyt kontoret: pak kasser, bestil flyttemand og meld adresseændring", null, null, ["flyt"], ["kasser"], ["kasser", "flyttemand", "adresse"]),
    ];

    public static readonly SplitCase[] Split =
    [
        new(
            "Opdatér certifikatet på testserveren",
            "Certifikatet udløber 1. november. Det ligger i Azure Key Vault og bruges af API-gatewayen.",
            ["Find ud af hvem der ejer key vaulten"],
            "jira",
            Language.Danish),

        // Made in the app, and short: the case the model itself answered in English.
        new("Flyt til nyt kontor", null, [], "manual", Language.Danish),
        new(
            "Planlæg teamdag i november",
            "Budget 5.000 kr. Skal ligge i Odense, og alle skal kunne nå hjem samme dag.",
            ["Find en dato", "Book lokale"],
            "manual",
            Language.Danish),
        new(
            "Prepare the quarterly security review",
            "Covers access rights, open vulnerabilities and the backup restore test.",
            [],
            "ado",
            Language.English),
        new(
            "We should write integration tests before we refactor",
            null,
            [],
            "retro",
            Language.English),
    ];

    public static readonly SummaryCase[] Summary =
    [
        new(
            "CSV-eksport fejler ved store udtræk",
            """
            h3. Baggrund
            Kunden (Region Syd) oplever at eksporten til CSV fejler når der er mere end 10.000 rækker. Fejlen ses i loggen som en OutOfMemoryException i ExportService.

            h3. Reproduktion
            # Log ind som admin
            # Vælg alle borgere
            # Klik Eksportér

            h3. Forventet
            Eksporten skal virke for op til 100.000 rækker, evt. ved at streame filen.

            Mvh
            Lene, 2nd level support
            """,
            "jira",
            Language.Danish,
            ["10.000"],
            ["Lene", "Mvh", "Log ind", "h3."]),
        new(
            "Login page times out",
            "When users with more than 50 roles log in, the login page times out after 30 seconds. The role lookup does one query per role. We should batch the lookup into one query. Seen in production since release 4.12. Thanks, Peter",
            "ado",
            Language.English,
            ["role"],
            ["Peter", "Thanks"]),
        new(
            "Borgerlisten viser forkerte adresser",
            """
            Opdatering 12/10: Vi troede det var cachen, men en tømning hjalp ikke.
            Opdatering 15/10: Fejlen skyldes at CPR-opslaget returnerer den gamle adresse, når borgeren er flyttet inden for de sidste 24 timer.
            Opgaven er nu at læse adressen fra CPR-hændelsen i stedet for fra opslaget.

            /Kasper
            """,
            "jira",
            Language.Danish,
            ["CPR"],
            ["Kasper", "12/10", "cachen"]),
        new(
            "Rapportmodulet skal kunne filtrere på afdeling",
            """
            *Ønske fra kunden:* Rapporterne skal kunne filtreres på afdeling, så hver leder kun ser sin egen.
            {code}
            SELECT * FROM rapport WHERE afdeling_id = @afdeling
            {code}
            Se også [SAAS-123|https://jira.example.com/browse/SAAS-123].
            """,
            "jira",
            Language.Danish,
            ["afdeling"],
            ["{code}", "SELECT", "*Ønske"]),
    ];

    /// <summary>
    /// One board, asked in one call like the app does. Each note tests one thing: an observation made
    /// an action, a named owner kept, nobody invented, a team that is not a person, a note that
    /// already is an action, and a board owner the guess may not replace.
    /// </summary>
    public static readonly RetroCase[] Retro =
    [
        new("one board", [
            new("We should get better at testing", null, ["test"]),
            new("Thomas: renew the TLS certificate before November", "Thomas", ["certificate"]),
            new("Nobody updates the wiki", null, ["wiki"]),
            new("The QA team should own the regression suite", null, ["regression"]),
            new("Book a bigger room for planning", null, ["room"]),
            new("Deploys on Friday keep breaking, ask Mads about the pipeline", Any, ["deploy"]),
            new("Lene should write the release notes", null, ["release notes"], BoardOwner: "Mathias"),
        ]),
    ];

    public static readonly ReviewCase[] Review =
    [
        new(
            "an ordinary week",
            new ReviewFacts(
                4,
                ["Send referatet", "Opdatér certifikatet", "Ryd op i backloggen", "Book mødelokale"],
                [
                    new WaitingFact("Svar fra revisoren om årsregnskabet", "Revisoren", 23),
                    new WaitingFact("Svar fra Bo om loggen", "Bo", 4),
                ],
                2,
                [new AgeFact("Lær at spille harmonika", 210), new AgeFact("Skriv en blog om Angular", 130)]),
            ["revisor", "harmonika"],
            ["Ring til Anna", "Køb mælk", "Opdatér wikien"]),
        new(
            "a quiet week",
            new ReviewFacts(0, [], [], 0, [new AgeFact("Lær at spille harmonika", 210)]),
            ["harmonika"],
            ["revisor", "Send referatet"]),
    ];

    /// <summary>The tasks every search case is asked against: the ten the thresholds were measured on.</summary>
    public static readonly string[] SearchTasks =
    [
        "Forny TLS-certifikatet på testserveren",
        "Ring til Anna om fakturaen",
        "Køb mælk",
        "Opdatér API-gatewayens adgangsnøgle i Key Vault",
        "Book mødelokale til sprint review",
        "Svar Lene om CSV-eksportfejlen",
        "Ryd op i backloggen",
        "Skriv referat fra retro",
        "Bestil ny skærm til Mathias",
        "Migrér borgerlisten til ny CPR-integration",
    ];

    public static readonly SearchCase[] Search =
    [
        new("den med certifikatet", "Forny TLS-certifikatet på testserveren"),
        new("mødet om sprinten", "Book mødelokale til sprint review"),
        new("eksport til excel", "Svar Lene om CSV-eksportfejlen"),
        new("nøgle til gatewayen", "Opdatér API-gatewayens adgangsnøgle i Key Vault"),
        new("ny monitor", "Bestil ny skærm til Mathias", KnownGap: true),
        new("svømmehal åbningstider", null),

        // Measured 2026-10-22: the model matches concepts, not knowledge or synonyms across languages.
        // None of the three clears 0.40 - "ny monitor" topped out at 0.27 against "skærm".
        new("regning", "Ring til Anna om fakturaen", KnownGap: true),
        new("personnummer opslag", "Migrér borgerlisten til ny CPR-integration", KnownGap: true),
    ];

    /// <summary>The pairs the duplicate threshold was set from, both kinds, Danish against English included.</summary>
    public static readonly DuplicateCase[] Duplicates =
    [
        new("Login page times out for users with many roles", "Ret login-timeout for brugere med mange roller", true),
        new("Export to CSV fails over 10,000 rows", "CSV-eksport fejler ved store udtræk", true),
        new("Renew the TLS certificate on the test server", "Forny TLS-certifikat på testserver", true),
        new("Citizen list shows wrong addresses after move", "Borgerlisten viser forkerte adresser", true),
        new("Reports must filter by department", "Rapporter skal kunne filtreres på afdeling", true),
        new("Login page times out for users with many roles", "CSV-eksport fejler ved store udtræk", false),
        new("Forny TLS-certifikat på testserver", "Opdatér API-gatewayens adgangsnøgle i Key Vault", false),
        new("Borgerlisten viser forkerte adresser", "Rapporter skal kunne filtreres på afdeling", false),
        new("Export to CSV fails over 10,000 rows", "Import of CSV fails for empty files", false),
    ];
}
