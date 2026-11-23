using ErpVendas.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ErpVendas.Api.Infra;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<MovimentoEstoque> Movimentos => Set<MovimentoEstoque>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> Itens => Set<ItemPedido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Produto>(e =>
        {
            e.ToTable("Produtos");
            e.Property(x => x.Sku).HasMaxLength(40);
            e.Property(x => x.Nome).HasMaxLength(150);
            e.Property(x => x.PrecoVenda).HasPrecision(18, 2);
            e.HasIndex(x => x.Sku).IsUnique();
            e.Ignore(x => x.AbaixoDoMinimo);
        });

        modelBuilder.Entity<MovimentoEstoque>(e =>
        {
            e.ToTable("MovimentosEstoque");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(30);
            e.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId);
            e.HasIndex(x => new { x.ProdutoId, x.Data });
        });

        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("Clientes");
            e.Property(x => x.Nome).HasMaxLength(150);
            e.Property(x => x.Documento).HasMaxLength(14);
            e.HasIndex(x => x.Documento).IsUnique();
        });

        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("Pedidos");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Desconto).HasPrecision(18, 2);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.NumeroNota).IsUnique();
            e.Ignore(x => x.Subtotal);
            e.Ignore(x => x.Total);
        });

        modelBuilder.Entity<ItemPedido>(e =>
        {
            e.ToTable("ItensPedido");
            e.Property(x => x.PrecoUnitario).HasPrecision(18, 2);
            e.HasOne(x => x.Produto).WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Subtotal);
        });
    }
}
