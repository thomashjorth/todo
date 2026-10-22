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
}
