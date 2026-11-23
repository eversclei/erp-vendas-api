# ERP Vendas · API (.NET)

> Projeto de portfólio: o núcleo de vendas de um mini-ERP.

API REST de vendas com produtos, estoque, clientes, pedidos e faturamento, mais relatórios gerenciais.

## Stack

- **ASP.NET Core (.NET 10)** com **Controllers** e `[ApiController]`
- **EF Core** para escrita e regras de negócio, com **SQL Server**
- **Dapper** para relatórios (SQL escrito à mão, consultas agregadas)
- **xUnit** + `WebApplicationFactory`: 16 testes de domínio e de integração
- Documentação interativa com **Scalar** (OpenAPI)

## Regras de negócio

**Pedido:** `Rascunho → Confirmado → Faturado`, ou `Cancelado`.
- Itens só podem ser alterados em rascunho. O mesmo produto soma na linha existente.
- O **preço fica congelado** no item no momento da inclusão. Mudar o preço do produto não afeta pedidos já lançados.
- Desconto limitado a **20% do subtotal**. Se itens forem removidos, o desconto é reajustado ao novo limite.
- Ao **confirmar**, o estoque é baixado e cada saída vira um `MovimentoEstoque` (rastreabilidade).
- Ao **cancelar** um pedido confirmado, o estoque é devolvido. Pedido faturado não pode ser cancelado.
- O **faturamento** numera a nota em sequência dentro de uma transação, com índice único contra duplicidade.

**Estoque:**
- Não fica negativo: estoque insuficiente retorna **409** e não baixa nada.
- **Concorrência otimista** no saldo (`[ConcurrencyCheck]`): duas vendas simultâneas do último item não passam as duas.

**Clientes:** CPF/CNPJ validados pelos dígitos verificadores.

## Endpoints principais

| Método | Rota | O que faz |
|---|---|---|
| `GET` | `/api/produtos?abaixoDoMinimo=true` | Lista produtos, com filtro de estoque crítico |
| `POST` | `/api/produtos/{id}/entradas` | Entrada de estoque |
| `POST` | `/api/pedidos` | Abre um pedido (rascunho) |
| `POST` | `/api/pedidos/{id}/itens` | Adiciona item |
| `POST` | `/api/pedidos/{id}/desconto` | Aplica desconto |
| `POST` | `/api/pedidos/{id}/confirmar` | Confirma e baixa o estoque |
| `POST` | `/api/pedidos/{id}/faturar` | Fatura e numera a nota |
| `POST` | `/api/pedidos/{id}/cancelar` | Cancela, devolvendo o estoque se preciso |
| `GET` | `/api/relatorios/vendas-por-produto?de=&ate=` | Faturamento por produto (Dapper) |
| `GET` | `/api/relatorios/estoque-critico` | Produtos abaixo do mínimo (Dapper) |

Erros seguem o padrão **ProblemDetails** (RFC 9457): 400 para validação, 404 quando não encontrado, 409 para regra de negócio ou conflito de concorrência.

## Como rodar

```bash
docker compose up -d --build     # SQL Server + API
```

- API e documentação: http://localhost:5090/scalar
- O banco é criado pelas migrations na subida, com produtos e clientes de exemplo.

Testes (não precisam de SQL Server: usam SQLite em memória):

```bash
dotnet test
```

## Estrutura

```
src/ErpVendas.Api/
  Domain/        Produto, Pedido, ItemPedido, MovimentoEstoque, Cliente, validação de documento
  Infra/         DbContext, migrations, dados iniciais
  Controllers/   cadastros, pedidos, relatórios (Dapper)
tests/ErpVendas.Tests/
  PedidoTests.cs          regras do domínio
  FluxoDeVendaTests.cs    venda completa pela API, numeração de notas, estoque, relatórios
```
