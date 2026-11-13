using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Localizacao;

namespace AlertaEmAcao.Modulos.Identidade;

public sealed class ServicoIdentidade(IUnidadeTrabalho uow, IHasherSenha hasher, ICatalogoMunicipios catalogo, IRelogio relogio, ServicoVinculos vinculos)
{
    public async Task<Usuario> RegistrarAsync(string nome, string email, string? telefone, string senha, string? municipio, CancellationToken ct)
    {
        PoliticaSenha.Validar(senha);
        var normalizado = Usuario.NormalizarEmail(email);
        if (await uow.ContarAsync(uow.Consulta<Usuario>().Where(u => u.Email == normalizado), ct) > 0)
            throw new ErroDominio("email_em_uso", "Já existe uma conta com este e-mail.");
        var u = Usuario.Registrar(nome, normalizado, telefone, hasher.Gerar(senha), municipio, relogio.Agora);
        uow.Adicionar(u);
        await uow.SalvarAsync(ct);
        return u;
    }

    public async Task<Usuario> AutenticarAsync(string email, string senha, CancellationToken ct)
    {
        string normalizado;
        try { normalizado = Usuario.NormalizarEmail(email); }
        catch (ErroDominio) { throw new ErroDominio("credenciais_invalidas", "E-mail ou senha incorretos."); }
        var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Email == normalizado), ct);
        // Mesmo custo de hash quando o usuário não existe (mitiga enumeração por tempo de resposta).
        var ok = u is not null ? hasher.Verificar(senha, u.SenhaHash) : hasher.Verificar(senha, hasher.Gerar("senha-inexistente-x"));
        if (u is null || !ok) throw new ErroDominio("credenciais_invalidas", "E-mail ou senha incorretos.");
        return u;
    }

    public async Task<GrupoFamiliar> DefinirFamiliaAsync(Guid usuarioId, IEnumerable<IntegranteFamilia> integrantes, IEnumerable<ContatoReferencia> contatos, CancellationToken ct)
    {
        var g = await uow.PrimeiroAsync(uow.Consulta<GrupoFamiliar>().Where(x => x.UsuarioId == usuarioId), ct);
        if (g is null) { g = new GrupoFamiliar(usuarioId); uow.Adicionar(g); }
        g.Definir(integrantes, contatos, relogio.Agora);
        await uow.SalvarAsync(ct);
        return g;
    }

    public sealed record DadosLocal(string Apelido, double Latitude, double Longitude, TipoLocal? Tipo, OrigemPosicaoLocal? Origem, string? Precisao,
        EnderecoLocal? Endereco, bool? MoroAqui, MoradoresResidencia? Moradores);

    public Task<LocalMonitorado> AdicionarLocalAsync(Guid usuarioId, string apelido, double lat, double lon, CancellationToken ct) =>
        AdicionarLocalAsync(usuarioId, new DadosLocal(apelido, lat, lon, null, null, null, null, null, null), ct);

    public async Task<LocalMonitorado> AdicionarLocalAsync(Guid usuarioId, DadosLocal d, CancellationToken ct)
    {
        var ativos = await uow.ContarAsync(uow.Consulta<LocalMonitorado>().Where(l => l.UsuarioId == usuarioId && l.Ativo), ct);
        if (ativos >= LocalMonitorado.LimitePorUsuario) throw new ErroDominio("limite_locais", "Máximo de 5 locais monitorados. Remova ou edite um local existente.");
        var p = Ponto(d);
        var l = LocalMonitorado.Criar(usuarioId, d.Apelido, p, catalogo.Identificar(p)?.Codigo, relogio.Agora, d.Tipo ?? TipoLocal.Outro,
            d.Origem ?? OrigemPosicaoLocal.GpsAtual, d.Precisao, d.Endereco, d.MoroAqui ?? false);
        if (d.Moradores is not null && l.Tipo == TipoLocal.Residencia) l.DefinirMoradores(d.Moradores, relogio.Agora);
        if (l.MoroAqui) await UnicaMoradiaAsync(usuarioId, l.Id, ct);
        uow.Adicionar(l);
        await uow.SalvarAsync(ct);
        return l;
    }

    public async Task<LocalMonitorado> AtualizarLocalAsync(Guid usuarioId, Guid id, DadosLocal d, CancellationToken ct)
    {
        var l = await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(x => x.Id == id && x.UsuarioId == usuarioId && x.Ativo), ct)
            ?? throw new ErroNaoEncontrado("Local não encontrado.");
        var morava = l.MoroAqui;
        var p = Ponto(d);
        l.Definir(d.Apelido, d.Tipo ?? l.Tipo, d.MoroAqui ?? l.MoroAqui, p, catalogo.Identificar(p)?.Codigo, d.Origem ?? l.Origem, d.Precisao, d.Endereco ?? l.Endereco, relogio.Agora);
        if (d.Moradores is not null) l.DefinirMoradores(d.Moradores, relogio.Agora);
        if (l.MoroAqui) await UnicaMoradiaAsync(usuarioId, l.Id, ct);
        // Deixou de ser a moradia: vínculos e convites daquela residência caem.
        if (morava && !l.MoroAqui) await vinculos.EncerrarDaResidenciaAsync(l.Id, ct);
        await uow.SalvarAsync(ct);
        return l;
    }

    private static PontoGeo Ponto(DadosLocal d) =>
        PontoGeo.TentarCriar(d.Latitude, d.Longitude, out var p) ? p : throw new ErroDominio("coordenadas_invalidas", "Coordenadas inválidas.");

    /// <summary>Só uma residência "moro aqui" por pessoa; ao trocar, os vínculos da anterior são encerrados.</summary>
    private async Task UnicaMoradiaAsync(Guid usuarioId, Guid manter, CancellationToken ct)
    {
        foreach (var outro in await uow.ListarAsync(uow.Consulta<LocalMonitorado>().Where(x => x.UsuarioId == usuarioId && x.Ativo && x.MoroAqui && x.Id != manter), ct))
        {
            outro.DeixarDeMorar(relogio.Agora);
            await vinculos.EncerrarDaResidenciaAsync(outro.Id, ct);
        }
    }
}
