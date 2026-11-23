using ErpVendas.Api.Domain;

namespace ErpVendas.Tests;

/// <summary>Regras do pedido e do estoque, sem banco.</summary>
public sealed class PedidoTests
{
    private static Produto NovoProduto(int id, decimal preco, int estoque)
    {
        var p = new Produto { Id = id, Sku = $"SKU-{id}", Nome = $"Produto {id}", PrecoVenda = preco };
        p.Entrada(estoque);
        return p;
    }

    [Fact]
    public void Mesmo_produto_soma_na_linha_e_congela_o_preco()
    {
        var produto = NovoProduto(1, 100m, 10);
        var pedido = new Pedido();

        pedido.AdicionarItem(produto, 2);
        produto.PrecoVenda = 150m;
        pedido.AdicionarItem(produto, 1);

        var item = Assert.Single(pedido.Itens);
        Assert.Equal(3, item.Quantidade);
        Assert.Equal(100m, item.PrecoUnitario);
        Assert.Equal(300m, pedido.Total);
    }

    [Fact]
    public void Desconto_acima_de_20_por_cento_e_recusado()
    {
        var pedido = new Pedido();
        pedido.AdicionarItem(NovoProduto(1, 100m, 10), 1);

        pedido.AplicarDesconto(20m);
        Assert.Throws<RegraDeNegocioException>(() => pedido.AplicarDesconto(20.01m));
        Assert.Equal(80m, pedido.Total);
    }

    [Fact]
    public void Remover_item_reduz_desconto_ao_novo_limite()
    {
        var pedido = new Pedido();
        pedido.AdicionarItem(NovoProduto(1, 100m, 10), 1);
        pedido.AdicionarItem(NovoProduto(2, 50m, 10), 1);
        pedido.Itens[1].Id = 2;
        pedido.AplicarDesconto(30m);

        pedido.RemoverItem(2);

        Assert.Equal(20m, pedido.Desconto);
    }

    [Fact]
    public void Confirmar_sem_estoque_falha_e_nao_muda_status()
    {
        var pedido = new Pedido();
        pedido.AdicionarItem(NovoProduto(1, 10m, 2), 3);

        Assert.Throws<RegraDeNegocioException>(() => pedido.Confirmar());
        Assert.Equal(StatusPedido.Rascunho, pedido.Status);
    }

    [Fact]
    public void Cancelar_pedido_confirmado_devolve_estoque()
    {
        var produto = NovoProduto(1, 10m, 5);
        var pedido = new Pedido();
        pedido.AdicionarItem(produto, 3);
        pedido.Confirmar();
        Assert.Equal(2, produto.EstoqueAtual);

        var movimentos = pedido.Cancelar();

        Assert.Equal(5, produto.EstoqueAtual);
        Assert.Equal(TipoMovimento.DevolucaoCancelamento, Assert.Single(movimentos).Tipo);
    }

    [Fact]
    public void Pedido_faturado_nao_pode_ser_cancelado_nem_alterado()
    {
        var produto = NovoProduto(1, 10m, 5);
        var pedido = new Pedido();
        pedido.AdicionarItem(produto, 1);
        pedido.Confirmar();
        pedido.Faturar(1);

        Assert.Throws<RegraDeNegocioException>(() => pedido.Cancelar());
        Assert.Throws<RegraDeNegocioException>(() => pedido.AdicionarItem(produto, 1));
    }

    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("111.111.111-11", false)]
    [InlineData("529.982.247-24", false)]
    [InlineData("123", false)]
    public void Valida_cpf_e_cnpj(string documento, bool valido) =>
        Assert.Equal(valido, Documento.Valido(documento));
}
