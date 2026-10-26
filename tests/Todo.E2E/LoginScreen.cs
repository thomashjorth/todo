using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class LoginScreen(TodoApp app)
{
    public ILocator Password => app.Page.GetByTestId("login-password");

    public ILocator Submit => app.Page.GetByTestId("login-submit");

    public ILocator Error => app.Page.GetByTestId("login-error");

    public async Task LogInAsync(string password)
    {
        await Password.FillAsync(password);
        await Submit.ClickAsync();
    }
}
