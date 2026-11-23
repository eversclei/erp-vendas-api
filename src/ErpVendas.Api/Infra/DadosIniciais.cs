using ErpVendas.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ErpVendas.Api.Infra;

public static class DadosIniciais
{
    public static async Task CriarSeVazioAsync(ErpDbContext db)
    {
        if (await db.Produtos.AnyAsync()) return;

        (string Sku, string Nome, decimal Preco, int Estoque, int Minimo)[] produtos =
        [
            ("NB-001", "Notebook 14\" i5 16GB", 4299.90m, 12, 5),
            ("MS-010", "Mouse sem fio", 89.90m, 80, 20),
            ("TC-020", "Teclado mecânico ABNT2", 349.00m, 25, 10),
            ("MN-027", "Monitor 27\" IPS", 1599.00m, 4, 5),
            ("HD-512", "SSD NVMe 512GB", 329.90m, 40, 15),
            ("CB-HDMI", "Cabo HDMI 2m", 39.90m, 150, 30),
        ];

        foreach (var p in produtos)
        {
            var produto = new Produto { Sku = p.Sku, Nome = p.Nome, PrecoVenda = p.Preco, EstoqueMinimo = p.Minimo };
            produto.Entrada(p.Estoque);
            db.Produtos.Add(produto);
        }

        db.Clientes.AddRange(
            new Cliente { Nome = "Papelaria Central Ltda", Documento = "11222333000181" },
            new Cliente { Nome = "João da Silva", Documento = "52998224725" });

        await db.SaveChangesAsync();
    }
}
