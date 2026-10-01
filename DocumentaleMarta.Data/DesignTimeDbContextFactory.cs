using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocumentaleMarta.Data;

/// <summary>Usata solo dagli strumenti <c>dotnet ef</c> per generare le migrazioni.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>Crea un contesto di prova per gli strumenti che generano le migrazioni.</summary>
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
