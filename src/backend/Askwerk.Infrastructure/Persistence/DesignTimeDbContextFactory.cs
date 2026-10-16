// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.EntityFrameworkCore.Design;

namespace Askwerk.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without a running database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AskwerkDbContext>
{
    /// <inheritdoc />
    public AskwerkDbContext CreateDbContext(string[] args) =>
        AskwerkDbContext.Create("Host=localhost;Database=askwerk_design");
}
