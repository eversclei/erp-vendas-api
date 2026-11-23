using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErpVendas.Api.Controllers;
using ErpVendas.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace ErpVendas.Tests;

public sealed class ErpFactory : WebApplicationFactory<Program>
{
    private readonly string _banco = $"Data Source=erp-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _conexaoViva;

    public ErpFactory()
    {
        _conexaoViva = new SqliteConnection(_banco);
        _conexaoViva.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Sqlite", _banco);
        builder.UseSetting("Seed:DadosIniciais", "true");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _conexaoViva.Dispose();
    }
}

/// <summary>Fluxo completo pela API: pedido → itens → confirmação → faturamento → relatório.</summary>
public sealed class FluxoDeVendaTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        Assert.True(resposta.IsSuccessStatusCode, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<ProdutoDto> Produto(HttpClient http, string sku) =>
        (await http.GetFromJsonAsync<List<ProdutoDto>>("/api/produtos", Json))!.Single(p => p.Sku == sku);

    private static async Task<PedidoDto> NovoPedido(HttpClient http) =>
        await Ler<PedidoDto>(await http.PostAsJsonAsync("/api/pedidos", new NovoPedidoRequest(1)));

    [Fact]
    public async Task Venda_completa_baixa_estoque_numera_nota_e_aparece_no_relatorio()
    {
        using var api = new ErpFactory();
        var http = api.CreateClient();
        var mouse = await Produto(http, "MS-010");

        var pedido = await NovoPedido(http);
        await Ler<PedidoDto>(await http.PostAsJsonAsync($"/api/pedidos/{pedido.Id}/itens", new NovoItemRequest(mouse.Id, 5)));
        await Ler<PedidoDto>(await http.PostAsJsonAsync($"/api/pedidos/{pedido.Id}/desconto", new DescontoRequest(49.50m)));
        var confirmado = await Ler<PedidoDto>(await http.PostAsync($"/api/pedidos/{pedido.Id}/confirmar", null));
        var faturado = await Ler<PedidoDto>(await http.PostAsync($"/api/pedidos/{pedido.Id}/faturar", null));

        Assert.Equal(StatusPedido.Confirmado, confirmado.Status);
        Assert.Equal(StatusPedido.Faturado, faturado.Status);
        Assert.Equal(1, faturado.NumeroNota);
        Assert.Equal(400m, faturado.Total);
        Assert.Equal(mouse.EstoqueAtual - 5, (await Produto(http, "MS-010")).EstoqueAtual);

        var relatorio = await http.GetFromJsonAsync<List<VendaPorProduto>>("/api/relatorios/vendas-por-produto", Json);
        var linha = Assert.Single(relatorio!);
        Assert.Equal("MS-010", linha.Sku);
        Assert.Equal(5, linha.Quantidade);
        Assert.Equal(449.50m, linha.Faturamento);
    }

    [Fact]
    public async Task Notas_sao_numeradas_em_sequencia()
    {
        using var api = new ErpFactory();
        var http = api.CreateClient();
        var cabo = await Produto(http, "CB-HDMI");
        var notas = new List<int?>();

        for (var i = 0; i < 3; i++)
        {
            var pedido = await NovoPedido(http);
            await http.PostAsJsonAsync($"/api/pedidos/{pedido.Id}/itens", new NovoItemRequest(cabo.Id, 1));
            await http.PostAsync($"/api/pedidos/{pedido.Id}/confirmar", null);
            notas.Add((await Ler<PedidoDto>(await http.PostAsync($"/api/pedidos/{pedido.Id}/faturar", null))).NumeroNota);
        }

        Assert.Equal([1, 2, 3], notas);
    }

    [Fact]
    public async Task Confirmar_sem_estoque_retorna_409_e_nao_baixa_nada()
    {
        using var api = new ErpFactory();
        var http = api.CreateClient();
        var monitor = await Produto(http, "MN-027");
        var pedido = await NovoPedido(http);
        await http.PostAsJsonAsync($"/api/pedidos/{pedido.Id}/itens", new NovoItemRequest(monitor.Id, monitor.EstoqueAtual + 1));

        var resposta = await http.PostAsync($"/api/pedidos/{pedido.Id}/confirmar", null);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(monitor.EstoqueAtual, (await Produto(http, "MN-027")).EstoqueAtual);
    }

    [Fact]
    public async Task Relatorio_de_estoque_critico_lista_produtos_abaixo_do_minimo()
    {
        using var api = new ErpFactory();
        var http = api.CreateClient();

        var criticos = await http.GetFromJsonAsync<List<EstoqueCritico>>("/api/relatorios/estoque-critico", Json);

        var monitor = Assert.Single(criticos!);
        Assert.Equal("MN-027", monitor.Sku);
        Assert.Equal(1, monitor.Faltam);
    }

    [Fact]
    public async Task Cliente_com_documento_invalido_e_recusado()
    {
        using var api = new ErpFactory();

        var resposta = await api.CreateClient().PostAsJsonAsync("/api/clientes", new NovoClienteRequest("Fulano", "123.456.789-00"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
