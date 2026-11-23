using System.ComponentModel.DataAnnotations;
using ErpVendas.Api.Domain;
using ErpVendas.Api.Infra;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ErpVendas.Api.Controllers;

public sealed record ItemDto(int Id, int ProdutoId, string Produto, int Quantidade, decimal PrecoUnitario, decimal Subtotal);

public sealed record PedidoDto(
    int Id, int ClienteId, string Cliente, StatusPedido Status, decimal Subtotal, decimal Desconto, decimal Total,
    int? NumeroNota, DateTime CriadoEm, DateTime? ConfirmadoEm, DateTime? FaturadoEm, IReadOnlyList<ItemDto> Itens)
{
    public static PedidoDto De(Pedido p) => new(
        p.Id, p.ClienteId, p.Cliente?.Nome ?? "", p.Status, p.Subtotal, p.Desconto, p.Total, p.NumeroNota,
        p.CriadoEm, p.ConfirmadoEm, p.FaturadoEm,
        p.Itens.Select(i => new ItemDto(i.Id, i.ProdutoId, i.Produto?.Nome ?? "", i.Quantidade, i.PrecoUnitario, i.Subtotal)).ToList());
}

public sealed record NovoPedidoRequest([Range(1, int.MaxValue)] int ClienteId);
public sealed record NovoItemRequest([Range(1, int.MaxValue)] int ProdutoId, [Range(1, 10_000)] int Quantidade);
public sealed record DescontoRequest([Range(0, 1_000_000)] decimal Valor);

[ApiController]
[Route("api/pedidos")]
public sealed class PedidosController(ErpDbContext db) : ControllerBase
{
    private IQueryable<Pedido> PedidosCompletos() =>
        db.Pedidos.Include(p => p.Cliente).Include(p => p.Itens).ThenInclude(i => i.Produto);

    [HttpGet]
    public async Task<IEnumerable<PedidoDto>> Listar([FromQuery] StatusPedido? status)
    {
        var query = PedidosCompletos().AsNoTracking();
        if (status is not null) query = query.Where(p => p.Status == status);
        var pedidos = await query.OrderByDescending(p => p.Id).Take(100).ToListAsync();
        return pedidos.Select(PedidoDto.De);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PedidoDto>> Obter(int id)
    {
        var pedido = await PedidosCompletos().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
        return pedido is null ? NotFound() : PedidoDto.De(pedido);
    }

    [HttpPost]
    public async Task<ActionResult<PedidoDto>> Criar(NovoPedidoRequest req)
    {
        if (!await db.Clientes.AnyAsync(c => c.Id == req.ClienteId))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["clienteId"] = ["Cliente não encontrado."] }));

        var pedido = new Pedido { ClienteId = req.ClienteId };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return await Recarregar(pedido.Id, criado: true);
    }

    [HttpPost("{id:int}/itens")]
    public Task<ActionResult<PedidoDto>> AdicionarItem(int id, NovoItemRequest req) =>
        Alterar(id, async pedido =>
        {
            var produto = await db.Produtos.FindAsync(req.ProdutoId)
                ?? throw new RegraDeNegocioException("Produto não encontrado.");
            pedido.AdicionarItem(produto, req.Quantidade);
        });

    [HttpDelete("{id:int}/itens/{itemId:int}")]
    public Task<ActionResult<PedidoDto>> RemoverItem(int id, int itemId) =>
        Alterar(id, pedido =>
        {
            pedido.RemoverItem(itemId);
            return Task.CompletedTask;
        });

    [HttpPost("{id:int}/desconto")]
    public Task<ActionResult<PedidoDto>> Desconto(int id, DescontoRequest req) =>
        Alterar(id, pedido =>
        {
            pedido.AplicarDesconto(req.Valor);
            return Task.CompletedTask;
        });

    [HttpPost("{id:int}/confirmar")]
    public Task<ActionResult<PedidoDto>> Confirmar(int id) =>
        Alterar(id, pedido =>
        {
            db.Movimentos.AddRange(pedido.Confirmar());
            return Task.CompletedTask;
        });

    [HttpPost("{id:int}/cancelar")]
    public Task<ActionResult<PedidoDto>> Cancelar(int id) =>
        Alterar(id, pedido =>
        {
            db.Movimentos.AddRange(pedido.Cancelar());
            return Task.CompletedTask;
        });

    [HttpPost("{id:int}/faturar")]
    public async Task<ActionResult<PedidoDto>> Faturar(int id)
    {
        // Numeração da nota dentro da transação; o índice único em NumeroNota impede duplicidade.
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await PedidosCompletos().SingleOrDefaultAsync(p => p.Id == id);
        if (pedido is null) return NotFound();

        var proximaNota = (await db.Pedidos.MaxAsync(p => p.NumeroNota) ?? 0) + 1;
        pedido.Faturar(proximaNota);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return PedidoDto.De(pedido);
    }

    /// <summary>Carrega o pedido completo, aplica a alteração de domínio e grava numa transação.</summary>
    private async Task<ActionResult<PedidoDto>> Alterar(int id, Func<Pedido, Task> alteracao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await PedidosCompletos().SingleOrDefaultAsync(p => p.Id == id);
        if (pedido is null) return NotFound();

        await alteracao(pedido);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return PedidoDto.De(pedido);
    }

    private async Task<ActionResult<PedidoDto>> Recarregar(int id, bool criado = false)
    {
        var pedido = await PedidosCompletos().AsNoTracking().SingleAsync(p => p.Id == id);
        var dto = PedidoDto.De(pedido);
        return criado ? CreatedAtAction(nameof(Obter), new { id }, dto) : dto;
    }
}
