using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

/// <summary>
/// The one journey that logs in through the page. Every other journey logs in through the API in
/// <see cref="TodoApp.OpenAsync"/>, so this is where the form, the error line and the reload after
/// the login are measured.
/// <para>
/// The language is a setting and the settings are behind the login, so the login page speaks the
/// system's language. <see cref="TodoApp.OpenLoggedOutAsync"/> pins that to Danish.
/// </para>
/// </summary>
public class LoginJourneyTests(BrowserFixture fixture) : BrowserTest(fixture)
{
    private const int ColumnWidth = 480;

    [Fact]
    public async Task Without_the_cookie_the_app_asks_for_the_password_and_lets_the_right_one_in()
    {
        await OpenLoggedOutAsync(new() { Width = ColumnWidth, Height = 1000 });

        // The shell's own loads met 401s and sent the page here; the navigation is hidden, because
        // every link in it leads behind the login.
        await Assertions.Expect(App.Page).ToHaveURLAsync($"{Host.BaseUrl}/login");
        await Assertions.Expect(App.Page.Locator("nav")).ToBeHiddenAsync();

        await App.Login.LogInAsync("forkert");
        await Assertions.Expect(App.Login.Error).ToHaveTextAsync("Forkert adgangskode.");

        await App.Login.LogInAsync(RunningHost.TestPassword);

        // The reload lands on the task list with the navigation back.
        await App.Tasks.WaitUntilShownAsync();
        await Assertions.Expect(App.Page).ToHaveURLAsync($"{Host.BaseUrl}/");
        await Assertions.Expect(App.Page.GetByTestId("nav-tasks")).ToHaveTextAsync("Opgaver");

        // And the cookie outlives the page: a reload stays logged in.
        await App.ReloadAsync();
        await Assertions.Expect(App.Page).ToHaveURLAsync($"{Host.BaseUrl}/");
    }
}
