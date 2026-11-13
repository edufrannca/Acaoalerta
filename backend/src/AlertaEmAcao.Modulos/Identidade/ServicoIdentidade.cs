using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Localizacao;

namespace AlertaEmAcao.Modulos.Identidade;

public sealed class ServicoIdentidade(IUnidadeTrabalho uow, IHasherSenha hasher, ICatalogoMunicipios catalogo, IRelogio relogio)
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

    public async Task<LocalMonitorado> AdicionarLocalAsync(Guid usuarioId, string apelido, double lat, double lon, CancellationToken ct)
    {
        var ativos = await uow.ContarAsync(uow.Consulta<LocalMonitorado>().Where(l => l.UsuarioId == usuarioId && l.Ativo), ct);
        if (ativos >= 5) throw new ErroDominio("limite_locais", "Máximo de 5 locais monitorados.");
        var p = new PontoGeo(lat, lon);
        var l = LocalMonitorado.Criar(usuarioId, apelido, p, catalogo.Identificar(p)?.Codigo, relogio.Agora);
        uow.Adicionar(l);
        await uow.SalvarAsync(ct);
        return l;
    }
}
