using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Todo.Core.Persistence;

namespace Todo.Api.Tests;

/// <summary>
/// From the settings page on, the database holds tokens in cleartext (the design document's
/// section 3). EnableSensitiveDataLogging is the one-line flag someone adds at 11pm when a save is
/// misbehaving, and it writes every parameter value into the log - so the token would sit in a log
/// file none of the other guards look at. Measured: with that call on TodoHost's DbContext
/// registration the token appears in cleartext in the log while all the token tests stay green.
/// Only a guard on the registration itself sees it.
///
/// What this does not cover: even with the flag off, the log still carries the token's *length*,
/// as in <c>[Parameters=[@p0='?' (Size = 10), @p1='?' (Size = 32)]]</c> where 32 is the length of
/// the test token. That is a small thing, but an unwritten small thing turns into a surprise.
/// </summary>
public class SensitiveLoggingTests : ApiTest
{
    [Fact]
    public void The_shipped_app_never_logs_parameter_values()
    {
        using var scope = Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        Assert.False(db.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled);
    }
}
