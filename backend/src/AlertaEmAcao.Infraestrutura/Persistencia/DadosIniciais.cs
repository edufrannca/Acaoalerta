using System.Globalization;
using System.Text;
using AlertaEmAcao.Infraestrutura.Adaptadores;
using AlertaEmAcao.Modulos.Abrigos;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Municipios;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Infraestrutura.Persistencia;

/// <summary>
/// Dados iniciais. Tudo que não é fonte pública real é FICTÍCIO e identificado como tal
/// (nomes com "SIMULADA", "[FICTÍCIO]", contas @demo.alerta).
/// </summary>
public static class DadosIniciais
{
    public const string SenhaDemo = "Demo@2026";
    public const string DominioDemo = "demo.alerta";

    public static string Slug(string nome)
    {
        var s = nome.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '-');
        }
        return string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries).Where(p => p is not ("DE" or "DA" or "DOS" or "DO")));
    }

    public static string CodigoDefesaCivil(string codigoIbge) => "DC-" + Slug(MunicipiosRmr.Nome(codigoIbge)!);

    public static IEnumerable<(string Codigo, string Nome, TipoInstituicao Tipo, string? Municipio, string Jurisdicao)> Centrais()
    {
        foreach (var (cod, nome) in MunicipiosRmr.Todos)
            yield return (CodigoDefesaCivil(cod), $"Defesa Civil de {nome} — CENTRAL SIMULADA", TipoInstituicao.DefesaCivilMunicipal, cod, $"Município de {nome}");
        yield return ("CBM-SIM", "Corpo de Bombeiros — RECEPTOR SIMULADO", TipoInstituicao.CorpoBombeiros, null, "RMR (estadual)");
        yield return ("SAMU-SIM", "SAMU 192 — RECEPTOR SIMULADO", TipoInstituicao.Samu, null, "RMR (metropolitano)");
        yield return ("PM-SIM", "Polícia Militar — RECEPTOR SIMULADO", TipoInstituicao.PoliciaMilitar, null, "RMR (estadual)");
        yield return ("ORG-MAOS", "Rede Fictícia Mãos Solidárias — ORGANIZAÇÃO SIMULADA", TipoInstituicao.OrganizacaoApoio, "2611606", "Recife");
        yield return ("ORG-MARE", "Coletivo Fictício Maré Alta — ORGANIZAÇÃO SIMULADA", TipoInstituicao.OrganizacaoApoio, "2609600", "Olinda");
    }

    public static async Task AplicarAsync(AlertaDbContext db, IHasherSenha hasher, IRelogio relogio, CancellationToken ct)
    {
        var agora = relogio.Agora;

        if (!await db.Set<Municipio>().AnyAsync(ct))
        {
            var limites = FonteLimitesApac.CarregarCopiaLocal();
            foreach (var (cod, nome) in MunicipiosRmr.Todos)
                db.Add(new Municipio(cod, nome, limites[cod], "APAC — Limite municipal (cópia local de 12/11/2026, simplificada ~300 m)", false,
                    new DateTimeOffset(2026, 11, 12, 21, 40, 0, TimeSpan.FromHours(-3)),
                    cod == "2611606"
                        ? "Áreas de risco: Dados Abertos Recife (Regional Sul, ODbL). Abrigos: COP Recife (ArcGIS). Demais regionais podem não estar na base aberta."
                        : "Não identificamos base pública aberta de setores de risco ou abrigos para este município. Consulte a Defesa Civil municipal."));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Set<Instituicao>().AnyAsync(ct))
        {
            foreach (var c in Centrais())
                db.Add(Instituicao.Criar(c.Codigo, c.Nome, c.Tipo, c.Municipio, c.Jurisdicao, CanalIntegracao.HttpCentralSimulada,
                    EstadoIntegracao.Simulada, $"/simulador/centrais/{c.Codigo}/mensagens"));
            db.AddRange(
                RegraEncaminhamento.Criar(null, TipoInstituicao.DefesaCivilMunicipal, Gatilho.Sempre, "Todo pedido de socorro vai à Defesa Civil do município"),
                RegraEncaminhamento.Criar(null, TipoInstituicao.CorpoBombeiros, Gatilho.RiscoHidrologico, "Alagamento, inundação, ilhamento ou correnteza"),
                RegraEncaminhamento.Criar(null, TipoInstituicao.CorpoBombeiros, Gatilho.RiscoGeologico, "Deslizamento, desabamento ou barreira instável"),
                RegraEncaminhamento.Criar(null, TipoInstituicao.CorpoBombeiros, Gatilho.PessoaFerida, "Pessoa ferida ou soterrada"),
                RegraEncaminhamento.Criar(null, TipoInstituicao.Samu, Gatilho.PessoaFerida, "Pessoa ferida"),
                RegraEncaminhamento.Criar(null, TipoInstituicao.Samu, Gatilho.NecessidadeMedica, "Dependência de equipamento ou cuidado médico"),
                RegraEncaminhamento.Criar(null, TipoInstituicao.PoliciaMilitar, Gatilho.SegurancaPublica, "Risco à segurança pública"));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Set<Usuario>().AnyAsync(u => u.ContaDemonstracao, ct))
        {
            var h = hasher.Gerar(SenhaDemo);
            Usuario U(string nome, string email, Papel p, string? mun, string? central = null) =>
                Usuario.Registrar(nome, $"{email}@{DominioDemo}", null, h, mun, agora, p, central, demonstracao: true);

            var ana = U("Ana (cidadã fictícia)", "cidada", Papel.Cidadao, "2611606");
            ana.AtualizarPerfil(null, null, "2611606", "Endereço de referência fictício — Ibura, Recife", true, true, true);
            var joao = U("João (cidadão fictício)", "cidadao.olinda", Papel.Cidadao, "2609600");
            joao.AtualizarPerfil(null, null, "2609600", "Endereço de referência fictício — Olinda", true, true, false);
            var bia = U("Bia (voluntária fictícia)", "voluntaria", Papel.Voluntario, "2611606");
            bia.AtualizarPerfil(null, null, "2611606", null, true, false, false);
            var usuarios = new List<Usuario>
            {
                ana, joao, bia,
                U("Operador(a) Defesa Civil Recife — SIMULADA", "operador.dc.recife", Papel.OperadorCentral, "2611606", CodigoDefesaCivil("2611606")),
                U("Operador(a) Defesa Civil Olinda — SIMULADA", "operador.dc.olinda", Papel.OperadorCentral, "2609600", CodigoDefesaCivil("2609600")),
                U("Operador(a) Defesa Civil Ipojuca — SIMULADA", "operador.dc.ipojuca", Papel.OperadorCentral, "2607208", CodigoDefesaCivil("2607208")),
                U("Operador(a) Defesa Civil Jaboatão — SIMULADA", "operador.dc.jaboatao", Papel.OperadorCentral, "2607901", CodigoDefesaCivil("2607901")),
                U("Operador(a) Bombeiros — RECEPTOR SIMULADO", "operador.bombeiros", Papel.OperadorCentral, null, "CBM-SIM"),
                U("Operador(a) SAMU — RECEPTOR SIMULADO", "operador.samu", Papel.OperadorCentral, null, "SAMU-SIM"),
                U("Rede Fictícia Mãos Solidárias", "org.maos", Papel.Organizacao, "2611606", "ORG-MAOS"),
                U("Administrador(a) da demonstração", "admin", Papel.Administrador, null),
            };
            db.AddRange(usuarios);
            await db.SaveChangesAsync(ct);

            var fam = new GrupoFamiliar(ana.Id);
            fam.Definir([
                new IntegranteFamilia("Ana", 34, false, false, false, null),
                new IntegranteFamilia("Carlos", 37, false, false, false, null),
                new IntegranteFamilia("Lia", 7, false, false, false, null),
                new IntegranteFamilia("Dona Rita", 72, true, true, false, "Usa andador"),
            ], [new ContatoReferencia("Contato fictício", "(81) 90000-0000", "irmã")], agora);
            db.Add(fam);
            // Pontos arbitrários (não são endereços de pessoas reais).
            db.Add(LocalMonitorado.Criar(ana.Id, "Casa (fictícia)", new PontoGeo(-8.1262, -34.9478), "2611606", agora));
            db.Add(LocalMonitorado.Criar(ana.Id, "Casa da mãe (fictícia)", new PontoGeo(-8.0476, -34.8770), "2611606", agora));
            db.Add(LocalMonitorado.Criar(joao.Id, "Casa (fictícia)", new PontoGeo(-8.0089, -34.8553), "2609600", agora));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Set<NecessidadePublicada>().AnyAsync(ct))
        {
            var maos = await db.Set<Instituicao>().FirstAsync(i => i.Codigo == "ORG-MAOS", ct);
            var mare = await db.Set<Instituicao>().FirstAsync(i => i.Codigo == "ORG-MARE", ct);
            db.AddRange(
                NecessidadePublicada.Publicar(maos.Id, maos.Codigo, maos.Nome, "2611606", TipoAjuda.DoacaoAgua, "Água mineral para famílias desalojadas",
                    "Garrafas de 1,5 L ou galões lacrados.", "Ponto de coleta FICTÍCIO — sede da Rede Mãos Solidárias (endereço de demonstração)", true, agora),
                NecessidadePublicada.Publicar(maos.Id, maos.Codigo, maos.Nome, "2611606", TipoAjuda.KitHigiene, "Kits de higiene",
                    "Sabonete, creme dental, escova, absorventes.", "Ponto de coleta FICTÍCIO — sede da Rede Mãos Solidárias (endereço de demonstração)", true, agora),
                NecessidadePublicada.Publicar(mare.Id, mare.Codigo, mare.Nome, "2609600", TipoAjuda.ApoioEmAbrigo, "Voluntários para triagem de doações",
                    "Turnos de 4 h, longe de áreas alagadas.", "Local verificado FICTÍCIO — galpão do Coletivo Maré Alta (endereço de demonstração)", true, agora));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Set<Abrigo>().AnyAsync(a => a.Ficticio, ct))
        {
            db.AddRange(
                Abrigo.Criar("demo-olinda-1", "Ginásio de Demonstração de Olinda", "Ginásio", "Endereço fictício", "2609600", new PontoGeo(-8.0050, -34.8600),
                    Acessibilidade.Possui, "Dados FICTÍCIOS do modo demonstração", null, "DEMONSTRATIVO", null, agora, agora, ficticio: true),
                Abrigo.Criar("demo-jaboatao-1", "Escola de Demonstração de Jaboatão", "Escola", "Endereço fictício", "2607901", new PontoGeo(-8.1120, -35.0150),
                    Acessibilidade.NaoInformada, "Dados FICTÍCIOS do modo demonstração", null, "DEMONSTRATIVO", null, agora, agora, ficticio: true),
                Abrigo.Criar("demo-ipojuca-1", "Centro Comunitário de Demonstração de Ipojuca", "Centro comunitário", "Endereço fictício", "2607208",
                    new PontoGeo(-8.4000, -35.0620), Acessibilidade.Inexistente, "Dados FICTÍCIOS do modo demonstração", null, "DEMONSTRATIVO", null, agora, agora, ficticio: true));
            await db.SaveChangesAsync(ct);
        }
    }
}
