using Dapper;
using ErpVendas.Api.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ErpVendas.Api.Controllers;

// Classes com setters (e não records posicionais) para o Dapper converter os tipos numéricos de cada banco.
public sealed class VendaPorProduto
{
    public string Sku { get; init; } = "";
    public string Produto { get; init; } = "";
    public int Quantidade { get; init; }
    public decimal Faturamento { get; init; }
}

public sealed class EstoqueCritico
{
    public string Sku { get; init; } = "";
    public string Produto { get; init; } = "";
    public int EstoqueAtual { get; init; }
    public int EstoqueMinimo { get; init; }
    public int Faltam { get; init; }
}

/// <summary>
/// Relatórios de leitura com SQL escrito à mão via Dapper: consultas agregadas ficam mais claras
/// e rápidas do que em LINQ, e não carregam entidades no change tracker.
/// </summary>
[ApiController]
[Route("api/relatorios")]
public sealed class RelatoriosController(ErpDbContext db) : ControllerBase
{
    [HttpGet("vendas-por-produto")]
    public async Task<IEnumerable<VendaPorProduto>> VendasPorProduto([FromQuery] DateTime? de, [FromQuery] DateTime? ate)
    {
        const string sql = """
            SELECT p.Sku,
                   p.Nome AS Produto,
                   SUM(i.Quantidade) AS Quantidade,
                   SUM(i.Quantidade * i.PrecoUnitario) AS Faturamento
              FROM ItensPedido i
              JOIN Pedidos ped ON ped.Id = i.PedidoId
              JOIN Produtos p ON p.Id = i.ProdutoId
             WHERE ped.Status = 'Faturado'
               AND ped.FaturadoEm >= @De
               AND ped.FaturadoEm < @Ate
             GROUP BY p.Sku, p.Nome
             ORDER BY Faturamento DESC
            """;

        var conexao = db.Database.GetDbConnection();
        return await conexao.QueryAsync<VendaPorProduto>(sql, new
        {
            De = de ?? DateTime.UtcNow.AddDays(-30),
            Ate = (ate ?? DateTime.UtcNow).AddDays(1)
        });
    }

    [HttpGet("estoque-critico")]
    public async Task<IEnumerable<EstoqueCritico>> EstoqueCriticoAsync()
    {
        const string sql = """
            SELECT Sku, Nome AS Produto, EstoqueAtual, EstoqueMinimo, EstoqueMinimo - EstoqueAtual AS Faltam
              FROM Produtos
             WHERE Ativo = 1 AND EstoqueAtual < EstoqueMinimo
             ORDER BY Faltam DESC
            """;

        return await db.Database.GetDbConnection().QueryAsync<EstoqueCritico>(sql);
    }
}
