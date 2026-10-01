using DocumentaleMarta.Core.Modelli;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

/// <summary>Il database dell'archivio (SQLite): le tabelle delle aree, delle cartelle e dei documenti.</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Area> Aree => Set<Area>();
    public DbSet<Cartella> Cartelle => Set<Cartella>();
    public DbSet<Documento> Documenti => Set<Documento>();

    /// <summary>
    /// Descrive al database aree, cartelle e documenti: lunghezze massime, nomi unici, indici di ricerca e cancellazione a
    /// cascata (eliminando un'area se ne vanno cartelle e documenti).
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Area>(e =>
        {
            e.Property(a => a.Nome).HasMaxLength(100).UseCollation("NOCASE");
            e.HasIndex(a => a.Nome).IsUnique();
            e.HasIndex(a => a.PercorsoRelativo).IsUnique();
        });

        modelBuilder.Entity<Cartella>(e =>
        {
            e.Property(c => c.Titolo).HasMaxLength(200);
            e.HasOne(c => c.Area).WithMany(a => a.Cartelle).HasForeignKey(c => c.AreaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.PercorsoRelativo).IsUnique();
            e.HasIndex(c => c.DataScadenza);
        });

        modelBuilder.Entity<Documento>(e =>
        {
            e.Property(d => d.NomeFile).HasMaxLength(260);
            e.Property(d => d.Estensione).HasMaxLength(16);
            e.HasOne(d => d.Cartella).WithMany(c => c.Documenti).HasForeignKey(d => d.CartellaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => d.PercorsoRelativo).IsUnique();
            e.HasIndex(d => d.Hash);
        });
    }
}
