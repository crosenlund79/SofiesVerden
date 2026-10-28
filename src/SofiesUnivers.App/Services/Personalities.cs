namespace SofiesUnivers.App.Services;

/// <summary>One of Sofie's five fixed personality universes. Only styling and short messages differ.</summary>
public sealed record Personality(string Id, string Emoji, string Name, string Colors, string[] Greetings, string[] SavedMessages);

public static class Personalities
{
    public static readonly IReadOnlyList<Personality> All =
    [
        new("boheme", "🌺", "Den sprælske boheme", "Koral, turkis og safran",
            ["Lad farverne flyde i dag, Sofie 🌺", "Kreativt kaos er også en slags orden.", "Fri som en sommerkjole i vinden."],
            ["Gemt – med koraller og kærlighed.", "Sådan! Endnu et farvestrøg på lærredet.", "Gemt. Universet nikker anerkendende."]),
        new("victoriansk", "🕯️", "Den stilfulde victorianer", "Blomme, bordeaux og antikguld",
            ["Velkommen tilbage til salonen, Sofie.", "Elegance er at gøre det svære let.", "Tebordet er dækket – lad os ordne papirerne."],
            ["Behørigt indført i protokollen.", "Noteret med fjerpen og segllak.", "Gemt – med stil og fløjl."]),
        new("hippie", "🌈", "Den flippede hippie", "Solsikke, orange og jordgrøn",
            ["Peace, love & papirarbejde ✌️", "Good vibes only – også i regnskabet.", "Lad solen skinne på tallene i dag."],
            ["Groovy! Det er gemt.", "Gemt – ren karma.", "Far out! Alt er på plads."]),
        new("muse", "🔮", "Den mystiske muse", "Midnatsblå, ametyst og guld",
            ["Stjernerne står gunstigt for bogføring.", "Månen hvisker: alt er i balance.", "Velkommen, kære muse."],
            ["Skrevet i stjernerne – og gemt.", "Universet har registreret det.", "Gemt under månens vagtsomme øje."]),
        new("diva", "💋", "Den rebelske diva", "Bærpink, rosa og drama",
            ["Hej skat – scenen er din 💋", "Fabelagtig, som altid.", "Regler? Kun dine egne."],
            ["Gemt, darling!", "Fejlfrit. Selvfølgelig.", "Det sad lige i skabet – gemt."]),
    ];

    public static Personality Default => All[0];

    public static Personality Find(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Default;
}

/// <summary>The active personality. Changes apply instantly and are remembered on this device.</summary>
public sealed class ThemeService(BrowserApp browser)
{
    private int _savedCounter;

    public Personality Current { get; private set; } = Personalities.Default;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            Current = Personalities.Find(await browser.GetThemeAsync());
        }
        catch (Exception)
        {
            Current = Personalities.Default;
        }
    }

    public async Task SetAsync(string id)
    {
        var next = Personalities.Find(id);
        Current = next;
        Changed?.Invoke(); // update the UI first; persisting is best effort
        await browser.SetThemeAsync(next.Id);
    }

    public string Greeting(DateOnly today) => Current.Greetings[today.DayNumber % Current.Greetings.Length];

    public string SavedMessage() => Current.SavedMessages[_savedCounter++ % Current.SavedMessages.Length];
}
