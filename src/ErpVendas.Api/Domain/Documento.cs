namespace ErpVendas.Api.Domain;

/// <summary>Validação de CPF e CNPJ pelos dígitos verificadores.</summary>
public static class Documento
{
    public static string SoDigitos(string? valor) => new((valor ?? "").Where(char.IsDigit).ToArray());

    public static bool Valido(string? valor)
    {
        var d = SoDigitos(valor);
        return d.Length switch
        {
            11 => CpfValido(d),
            14 => CnpjValido(d),
            _ => false
        };
    }

    private static bool CpfValido(string cpf)
    {
        if (cpf.Distinct().Count() == 1) return false;
        return Digito(cpf[..9], [10, 9, 8, 7, 6, 5, 4, 3, 2]) == cpf[9] - '0'
            && Digito(cpf[..10], [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]) == cpf[10] - '0';
    }

    private static bool CnpjValido(string cnpj)
    {
        if (cnpj.Distinct().Count() == 1) return false;
        return Digito(cnpj[..12], [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == cnpj[12] - '0'
            && Digito(cnpj[..13], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) == cnpj[13] - '0';
    }

    private static int Digito(string numeros, int[] pesos)
    {
        var soma = numeros.Select((c, i) => (c - '0') * pesos[i]).Sum();
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
