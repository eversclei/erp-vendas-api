using System.ComponentModel.DataAnnotations;

namespace ErpVendas.Api.Domain;

public sealed class RegraDeNegocioException(string mensagem) : Exception(mensagem);

public sealed class Produto
{
    public int Id { get; set; }
    public required string Sku { get; set; }
    public required string Nome { get; set; }
    public decimal PrecoVenda { get; set; }
    public int EstoqueMinimo { get; set; }
    public bool Ativo { get; set; } = true;

    // Concorrência otimista: o UPDATE só passa se o saldo não mudou desde a leitura.
    [ConcurrencyCheck]
    public int EstoqueAtual { get; private set; }

    public bool AbaixoDoMinimo => EstoqueAtual < EstoqueMinimo;

    public void Entrada(int quantidade)
    {
        if (quantidade <= 0) throw new RegraDeNegocioException("A quantidade de entrada deve ser maior que zero.");
        EstoqueAtual += quantidade;
    }

    public void Baixar(int quantidade)
    {
        if (quantidade > EstoqueAtual)
            throw new RegraDeNegocioException($"Estoque insuficiente para {Nome}: disponível {EstoqueAtual}, pedido {quantidade}.");
        EstoqueAtual -= quantidade;
    }

    public void Devolver(int quantidade) => EstoqueAtual += quantidade;
}

public enum TipoMovimento
{
    Entrada = 1,
    SaidaVenda = 2,
    DevolucaoCancelamento = 3
}

public sealed class MovimentoEstoque
{
    public int Id { get; set; }
    public int ProdutoId { get; set; }
    public TipoMovimento Tipo { get; set; }
    public int Quantidade { get; set; }
    public int? PedidoId { get; set; }
    public DateTime Data { get; set; } = DateTime.UtcNow;
}

public sealed class Cliente
{
    public int Id { get; set; }
    public required string Nome { get; set; }

    /// <summary>CPF ou CNPJ, só dígitos.</summary>
    public required string Documento { get; set; }
}

public enum StatusPedido
{
    Rascunho = 1,
    Confirmado = 2,
    Faturado = 3,
    Cancelado = 4
}

public sealed class Pedido
{
    /// <summary>Desconto máximo permitido sobre o subtotal, sem aprovação.</summary>
    public const decimal DescontoMaximoPercentual = 0.20m;

    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public StatusPedido Status { get; private set; } = StatusPedido.Rascunho;
    public decimal Desconto { get; private set; }
    public int? NumeroNota { get; private set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmadoEm { get; private set; }
    public DateTime? FaturadoEm { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public List<ItemPedido> Itens { get; set; } = [];

    public decimal Subtotal => Itens.Sum(i => i.Subtotal);
    public decimal Total => Subtotal - Desconto;

    public void AdicionarItem(Produto produto, int quantidade)
    {
        GarantirRascunho();
        if (quantidade <= 0) throw new RegraDeNegocioException("A quantidade deve ser maior que zero.");
        if (!produto.Ativo) throw new RegraDeNegocioException($"O produto {produto.Nome} está inativo.");

        // Mesmo produto soma na linha existente. O preço fica congelado no momento da inclusão.
        var existente = Itens.FirstOrDefault(i => i.ProdutoId == produto.Id);
        if (existente is not null)
        {
            existente.Quantidade += quantidade;
        }
        else
        {
            Itens.Add(new ItemPedido { ProdutoId = produto.Id, Produto = produto, Quantidade = quantidade, PrecoUnitario = produto.PrecoVenda });
        }

        AjustarDescontoAoSubtotal();
    }

    public void RemoverItem(int itemId)
    {
        GarantirRascunho();
        var item = Itens.FirstOrDefault(i => i.Id == itemId) ?? throw new RegraDeNegocioException("Item não encontrado no pedido.");
        Itens.Remove(item);
        AjustarDescontoAoSubtotal();
    }

    public void AplicarDesconto(decimal valor)
    {
        GarantirRascunho();
        if (valor < 0) throw new RegraDeNegocioException("O desconto não pode ser negativo.");
        var maximo = Math.Round(Subtotal * DescontoMaximoPercentual, 2);
        if (valor > maximo)
            throw new RegraDeNegocioException($"Desconto acima do limite de {DescontoMaximoPercentual:P0} (máximo R$ {maximo:N2}).");
        Desconto = valor;
    }

    /// <summary>Confirma e baixa o estoque de todos os itens. Os produtos dos itens precisam estar carregados.</summary>
    public IReadOnlyList<MovimentoEstoque> Confirmar()
    {
        GarantirRascunho();
        if (Itens.Count == 0) throw new RegraDeNegocioException("Não é possível confirmar um pedido sem itens.");

        foreach (var item in Itens) item.Produto!.Baixar(item.Quantidade);

        Status = StatusPedido.Confirmado;
        ConfirmadoEm = DateTime.UtcNow;
        return Itens.Select(i => new MovimentoEstoque { ProdutoId = i.ProdutoId, Tipo = TipoMovimento.SaidaVenda, Quantidade = i.Quantidade, PedidoId = Id }).ToList();
    }

    public void Faturar(int numeroNota)
    {
        if (Status != StatusPedido.Confirmado) throw new RegraDeNegocioException("Só pedidos confirmados podem ser faturados.");
        Status = StatusPedido.Faturado;
        NumeroNota = numeroNota;
        FaturadoEm = DateTime.UtcNow;
    }

    /// <summary>Cancela; se o estoque já tinha sido baixado, devolve.</summary>
    public IReadOnlyList<MovimentoEstoque> Cancelar()
    {
        if (Status == StatusPedido.Faturado) throw new RegraDeNegocioException("Pedido faturado não pode ser cancelado; emita uma devolução.");
        if (Status == StatusPedido.Cancelado) throw new RegraDeNegocioException("O pedido já está cancelado.");

        var movimentos = new List<MovimentoEstoque>();
        if (Status == StatusPedido.Confirmado)
        {
            foreach (var item in Itens)
            {
                item.Produto!.Devolver(item.Quantidade);
                movimentos.Add(new MovimentoEstoque { ProdutoId = item.ProdutoId, Tipo = TipoMovimento.DevolucaoCancelamento, Quantidade = item.Quantidade, PedidoId = Id });
            }
        }

        Status = StatusPedido.Cancelado;
        CanceladoEm = DateTime.UtcNow;
        return movimentos;
    }

    private void GarantirRascunho()
    {
        if (Status != StatusPedido.Rascunho) throw new RegraDeNegocioException($"O pedido está {Status} e não pode mais ser alterado.");
    }

    // Se itens forem removidos, o desconto não pode ultrapassar o novo limite.
    private void AjustarDescontoAoSubtotal() => Desconto = Math.Min(Desconto, Math.Round(Subtotal * DescontoMaximoPercentual, 2));
}

public sealed class ItemPedido
{
    public int Id { get; set; }
    public int PedidoId { get; set; }
    public int ProdutoId { get; set; }
    public Produto? Produto { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Subtotal => Quantidade * PrecoUnitario;
}
