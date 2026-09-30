using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

/// <summary>Un contesto nuovo per ogni operazione: niente stato condiviso tra una schermata e l'altra.</summary>
public class AppDbContextFactory(DbContextOptions<AppDbContext> opzioni) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(opzioni);
}
