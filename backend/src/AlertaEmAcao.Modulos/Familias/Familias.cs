using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Familias;

public sealed record IntegranteFamilia(
    string? Apelido,
    int? Idade,
    bool MobilidadeReduzida,
    bool DependeAssistencia,
    bool UsaEquipamentoMedico,
    string? NecessidadesEspeciais);

public sealed record ContatoReferencia(string Nome, string Telefone, string? Relacao);

/// <summary>
/// Grupo familiar. Contém dados sensíveis (idades, saúde, crianças): é persistido
/// cifrado (ver Infraestrutura/ProtecaoDados) e só é exposto ao próprio usuário e,
/// como resumo, à análise de uma ocorrência que ele mesmo abrir.
/// </summary>
public sealed class GrupoFamiliar
{
    public Guid UsuarioId { get; private set; }
    public List<IntegranteFamilia> Integrantes { get; private set; } = [];
    public List<ContatoReferencia> Contatos { get; private set; } = [];
    public DateTimeOffset AtualizadoEm { get; private set; }

    private GrupoFamiliar() { }

    public GrupoFamiliar(Guid usuarioId) { UsuarioId = usuarioId; }

    public void Definir(IEnumerable<IntegranteFamilia> integrantes, IEnumerable<ContatoReferencia> contatos, DateTimeOffset agora)
    {
        var lista = integrantes.ToList();
        if (lista.Count > 30) throw new ErroDominio("familia_grande", "Máximo de 30 integrantes.");
        foreach (var i in lista)
        {
            if (i.Idade is < 0 or > 125) throw new ErroDominio("idade_invalida", "Idade deve estar entre 0 e 125.");
            if (i.NecessidadesEspeciais?.Length > 300) throw new ErroDominio("texto_longo", "Descrição de necessidades muito longa.");
        }
        var contatosLista = contatos.ToList();
        if (contatosLista.Count > 10) throw new ErroDominio("contatos_demais", "Máximo de 10 contatos.");
        foreach (var c in contatosLista)
            if (string.IsNullOrWhiteSpace(c.Nome) || c.Telefone.Count(char.IsDigit) < 8)
                throw new ErroDominio("contato_invalido", "Contato precisa de nome e telefone.");
        Integrantes = lista;
        Contatos = contatosLista;
        AtualizadoEm = agora;
    }

    public int Quantidade => Integrantes.Count;
    public int Criancas => Integrantes.Count(i => i.Idade is < 12);
    public int Idosos => Integrantes.Count(i => i.Idade is >= 60);
    public bool AlguemComMobilidadeReduzida => Integrantes.Any(i => i.MobilidadeReduzida);
    public bool AlguemDependeAssistencia => Integrantes.Any(i => i.DependeAssistencia);
    public bool AlguemUsaEquipamentoMedico => Integrantes.Any(i => i.UsaEquipamentoMedico);
}
