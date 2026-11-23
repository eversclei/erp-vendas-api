using System.ComponentModel.DataAnnotations;
using ErpVendas.Api.Domain;
using ErpVendas.Api.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ErpVendas.Api.Controllers;

public sealed record ProdutoDto(int Id, string Sku, string Nome, decimal PrecoVenda, int EstoqueAtual, int EstoqueMinimo, bool AbaixoDoMinimo, bool Ativo)
{
    public static ProdutoDto De(Produto p) => new(p.Id, p.Sku, p.Nome, p.PrecoVenda, p.EstoqueAtual, p.EstoqueMinimo, p.AbaixoDoMinimo, p.Ativo);
}

public sealed record SalvarProdutoRequest(
    [Required, StringLength(40)] string Sku,
    [Required, StringLength(150, MinimumLength = 3)] string Nome,
    [Range(0.01, 1_000_000)] decimal PrecoVenda,
    [Range(0, 100_000)] int EstoqueMinimo);

public sealed record EntradaEstoqueRequest([Range(1, 100_000)] int Quantidade);

public sealed record ClienteDto(int Id, string Nome, string Documento);

public sealed record NovoClienteRequest([Required, StringLength(150, MinimumLength = 3)] string Nome, [Required] string Documento);

[ApiController]
[Route("api/produtos")]
public sealed class ProdutosController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ProdutoDto>> Listar([FromQuery] bool? abaixoDoMinimo, [FromQuery] string? busca)
    {
        var query = db.Produtos.AsNoTracking().Where(p => p.Ativo);
        if (!string.IsNullOrWhiteSpace(busca))
            query = query.Where(p => p.Nome.Contains(busca) || p.Sku.Contains(busca));
        if (abaixoDoMinimo == true)
            query = query.Where(p => p.EstoqueAtual < p.EstoqueMinimo);

        var produtos = await query.OrderBy(p => p.Nome).ToListAsync();
        return produtos.Select(ProdutoDto.De);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProdutoDto>> Obter(int id)
    {
        var produto = await db.Produtos.FindAsync(id);
        return produto is null ? NotFound() : ProdutoDto.De(produto);
    }

    [HttpPost]
    public async Task<ActionResult<ProdutoDto>> Criar(SalvarProdutoRequest req)
    {
        var sku = req.Sku.Trim().ToUpperInvariant();
        if (await db.Produtos.AnyAsync(p => p.Sku == sku))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["sku"] = ["Já existe um produto com este SKU."] }));

        var produto = new Produto { Sku = sku, Nome = req.Nome.Trim(), PrecoVenda = req.PrecoVenda, EstoqueMinimo = req.EstoqueMinimo };
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = produto.Id }, ProdutoDto.De(produto));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProdutoDto>> Atualizar(int id, SalvarProdutoRequest req)
    {
        var produto = await db.Produtos.FindAsync(id);
        if (produto is null) return NotFound();

        // O preço novo vale só para pedidos futuros: itens já lançados guardam o preço da época.
        produto.Nome = req.Nome.Trim();
        produto.PrecoVenda = req.PrecoVenda;
        produto.EstoqueMinimo = req.EstoqueMinimo;
        await db.SaveChangesAsync();
        return ProdutoDto.De(produto);
    }

    [HttpPost("{id:int}/entradas")]
    public async Task<ActionResult<ProdutoDto>> Entrada(int id, EntradaEstoqueRequest req)
    {
        var produto = await db.Produtos.FindAsync(id);
        if (produto is null) return NotFound();

        produto.Entrada(req.Quantidade);
        db.Movimentos.Add(new MovimentoEstoque { ProdutoId = id, Tipo = TipoMovimento.Entrada, Quantidade = req.Quantidade });
        await db.SaveChangesAsync();
        return ProdutoDto.De(produto);
    }
}

[ApiController]
[Route("api/clientes")]
public sealed class ClientesController(ErpDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ClienteDto>> Listar() =>
        await db.Clientes.AsNoTracking().OrderBy(c => c.Nome).Select(c => new ClienteDto(c.Id, c.Nome, c.Documento)).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<ClienteDto>> Criar(NovoClienteRequest req)
    {
        var documento = Documento.SoDigitos(req.Documento);
        if (!Documento.Valido(documento))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["documento"] = ["CPF ou CNPJ inválido."] }));
        if (await db.Clientes.AnyAsync(c => c.Documento == documento))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["documento"] = ["Cliente já cadastrado."] }));

        var cliente = new Cliente { Nome = req.Nome.Trim(), Documento = documento };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return Created($"/api/clientes/{cliente.Id}", new ClienteDto(cliente.Id, cliente.Nome, cliente.Documento));
    }
}
